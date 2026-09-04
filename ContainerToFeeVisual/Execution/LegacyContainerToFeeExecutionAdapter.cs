using VIBN_Tools.ContainerToFee;
using VIBN_Tools.GlobalClasses;
using VIBN_Tools.GlobalClasses.FeeObjects;
using VIBN_Tools.Settings;
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
            var generationInterfaceResolution = GrobGenerationInterfaceResolver.Resolve(
                runtimeInterfaces.Values);
            if (!generationInterfaceResolution.IsValid)
            {
                var issue = generationInterfaceResolution.Issue!;
                return new VisualExecutionResult(false, issue.Message, [issue]);
            }
            var generationInterface = generationInterfaceResolution.Interface!;

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

            var timestamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm");

            if (selectedContainers.Length > 0)
            {
                var basicFrame = new FeeBasicFrame
                {
                    Name = $"Auto Generated (at {timestamp})",
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
                $"{binding.UnknownSignals.Count} unbekannte Signale.");
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
