using VIBN_Tools.ContainerToFee;
using VIBN_Tools.GlobalClasses;
using VIBN_Tools.GlobalClasses.FeeObjects;
using VIBN_Tools.Settings;
using System.Xml.Linq;
using static VIBN_Tools.GlobalClasses.Interfaces;

namespace VIBN_Tools.ContainerToFeeVisual;

/// <summary>
/// Applies the visual plan to freshly parsed legacy containers and delegates
/// all actual creation to the established Container2FEE executor. This keeps
/// the generated FEE behavior identical to the existing tab.
/// </summary>
internal sealed class LegacyContainerToFeeExecutionAdapter(IVisualPlanLogger logger)
{
    public async Task<VisualExecutionResult> ExecuteAsync(
        VisualPlan plan,
        IReadOnlyDictionary<string, FeeAbstractObject> runtimeObjects,
        IReadOnlyDictionary<string, FeeInterface> runtimeInterfaces,
        CancellationToken cancellationToken)
    {
        if (Services.Connection?.CanUseFeeFeatures != true)
            return Failure(FeeConnectionService.MissingConnectionMessage, "FEE_NOT_CONNECTED");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var binding = RuntimeVisualPlanBinder.Bind(plan, runtimeObjects);
            if (!binding.Success)
                return new VisualExecutionResult(false, binding.Issue!.Message, [binding.Issue]);

            var selectedBindings = binding.Containers
                .Where(item => plan.IsGenerationSelected(item.PlanNode.Id))
                .ToArray();
            var signalRequests = selectedBindings
                .SelectMany(binding => binding.RuntimeContainer.EnumerateAssignedSignals().Select(signal =>
                    new SignalResolutionRequest(
                        binding.PlanNode.Id,
                        binding.PlanNode.Name,
                        signal)))
                .Concat(binding.UnknownSignals.Select(signal =>
                    new SignalResolutionRequest(
                        "unknown-signals",
                        "Unbekannte Signale",
                        signal)))
                .ToArray();
            var signalPlan = SignalResolutionPlanner.Build(
                signalRequests,
                runtimeInterfaces.Values);
            if (!signalPlan.IsValid)
            {
                return new VisualExecutionResult(
                    false,
                    "Vorhandene Signale konnten nicht eindeutig aufgelöst werden.",
                    signalPlan.Issues);
            }
            signalPlan.ApplyExistingBindings();

            // Missing variables require the installed generation provider, not
            // an existing interface instance with a fixed display name. The
            // legacy generator creates a fresh timestamped instance as well.
            var timestamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            var generationInterface = new FeeInterface
            {
                Name = $"Auto Generated (at {timestamp})",
                ProviderGuid = Defines.GrobGenerationInterfaceProviderGuid,
            };
            if (signalPlan.MissingSignals.Count > 0)
            {
                var providers = await Services.ApiInstance.Interface.GetProvidersOfProjectAsync();
                var providerResolution = GrobGenerationInterfaceResolver.ResolveProvider(
                    providers.Select(provider => new GrobGenerationProviderIdentity(
                        provider.ProviderGuid,
                        provider.ProviderName ?? string.Empty)));
                if (!providerResolution.IsValid)
                {
                    var issue = providerResolution.Issue!;
                    return new VisualExecutionResult(false, issue.Message, [issue]);
                }

                if (!await generationInterface.CreateInterfaceAsync())
                {
                    return Failure(
                        "Das Grob Generation Interface konnte nicht als neue FEE-Interfaceinstanz angelegt werden.",
                        "GROB_GENERATION_INTERFACE_CREATE_FAILED");
                }
            }

            var selectedContainers = selectedBindings
                .Select(item => item.RuntimeContainer)
                .ToArray();
            ContainerToFeeService.LinkAddonContainers(selectedContainers);
            var sortedContainers = selectedBindings
                .Select(item => item.RuntimeContainer)
                .OrderBy(container => container.GetType().Name, StringComparer.Ordinal)
                .ThenBy(container => container.ComponentName, StringComparer.Ordinal)
                .ToArray();

            // Create only the unique missing variables before any BasicFrame,
            // logic or SimObject is written. All later legacy calls reuse the
            // resolved GUIDs and therefore cannot duplicate the variables.
            foreach (var missing in signalPlan.MissingSignals)
            {
                if (!await missing.Signal.CreateSignalAsync(generationInterface))
                {
                    return Failure(
                        $"Signal '{missing.Signal.Tag}' konnte im Grob Generation Interface nicht erzeugt werden. " +
                        "Bereits zuvor angelegte Signale können bestehen geblieben sein; Containerobjekte wurden noch nicht erzeugt.",
                        "GENERATED_SIGNAL_NOT_AVAILABLE",
                        missing.ContainerId);
                }
            }
            signalPlan.ApplyCreatedBindings(generationInterface);

            if (selectedContainers.Length > 0)
            {
                var includedContainerIds = plan.Nodes
                    .Where(node => node.Kind == VisualNodeKind.Container)
                    .Where(node =>
                        plan.IsGenerationSelected(node.Id) ||
                        !ContainerMetadataCatalog.TryGet(node.TypeName, out _))
                    .Select(node => node.Id)
                    .ToHashSet(StringComparer.Ordinal);
                var sourceDocument = XDocument.Load(plan.SourceXmlPath, LoadOptions.None);
                var signalSources = selectedBindings.ToDictionary(
                    item => item.PlanNode.Id,
                    item => (IReadOnlyList<FeeContainerSignalSource>)item.RuntimeContainer
                        .EnumerateAssignedSignals()
                        .Select(signal => new FeeContainerSignalSource(
                            signal.Comment ?? string.Empty,
                            signal.Tag ?? string.Empty,
                            string.IsNullOrWhiteSpace(signal.Path) ? signal.Address ?? string.Empty : signal.Path,
                            signal.IOTypeString ?? string.Empty,
                            signal.Guid))
                        .ToArray(),
                    StringComparer.Ordinal);
                var provenance = FeeContainerProvenanceCodec.Create(
                    sourceDocument,
                    includedContainerIds,
                    plan.SourceFingerprint,
                    signalSources);
                var expectedBindings = signalSources.Values.Sum(signals => signals.Count);
                if (provenance.SignalBindings.Count != expectedBindings)
                {
                    return Failure(
                        "Die Container-Einträge konnten nicht eindeutig den aufgelösten FEE-Signalen zugeordnet werden. " +
                        "Die Generierung wurde vor dem BasicFrame abgebrochen.",
                        "PROVENANCE_SIGNAL_BINDING_INCOMPLETE");
                }
                var basicFrame = new FeeBasicFrame
                {
                    Name = $"Auto Generated (at {timestamp})",
                    PersistentTags = provenance.Tags,
                };
                await basicFrame.CreateAsync();
                await basicFrame.SendAndWaitAsync();
                cancellationToken.ThrowIfCancellationRequested();

                await ContainerToFeeService.CreateAllContainersAsync(
                    sortedContainers,
                    generationInterface,
                    basicFrame);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (binding.UnknownSignals.Count > 0)
            {
                await Parallel.ForEachAsync(
                    binding.UnknownSignals,
                    cancellationToken,
                    async (signal, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        await signal.CreateSignalAsync(generationInterface);
                    });
            }

            logger.Information(
                $"Visuelle Generierung abgeschlossen: {selectedContainers.Length} Container " +
                $"({signalPlan.ExistingBindings.Count} vorhandene, " +
                $"{signalPlan.MissingSignals.Count} neu zu erzeugende Signale), " +
                $"{binding.UnknownSignals.Count} unbekannte Signale. " +
                "Der erzeugte BasicFrame enthält Container2FEE-Provenienz für FEE2Container.");
            return new VisualExecutionResult(
                true,
                $"Generierung abgeschlossen: {selectedContainers.Length} Container wurden verarbeitet; " +
                $"{signalPlan.ExistingBindings.Count} Signale wurden wiederverwendet und " +
                $"{signalPlan.MissingSignals.Count} im Grob Generation Interface erzeugt.",
                []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Error("Die visuelle Container2FEE-Generierung ist fehlgeschlagen.", exception);
            return new VisualExecutionResult(
                false,
                "Generierung fehlgeschlagen. Details stehen im Protokoll.",
                [new VisualIssue(
                    VisualIssueSeverity.Error,
                    "LEGACY_EXECUTION_FAILED",
                    exception.Message)]);
        }
    }

    private static VisualExecutionResult Failure(string message, string code, string? nodeId = null) =>
        new(false, message, [new VisualIssue(VisualIssueSeverity.Error, code, message, nodeId)]);
}
