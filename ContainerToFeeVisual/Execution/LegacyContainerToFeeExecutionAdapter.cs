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
            var createSignalBindings = selectedBindings
                .Where(item => plan.ShouldCreateSignals(item.PlanNode.Id))
                .ToArray();
            var reuseSignalBindings = selectedBindings
                .Where(item => !plan.ShouldCreateSignals(item.PlanNode.Id))
                .ToArray();

            FeeInterface? existingInterface = null;
            if (reuseSignalBindings.Length > 0)
            {
                var selectedInterface = plan.ExistingInterfaceSelection;
                if (selectedInterface is null ||
                    !runtimeInterfaces.TryGetValue(selectedInterface.InterfaceGuid, out existingInterface))
                {
                    return Failure(
                        "Das ausgewählte Interface für vorhandene Signale ist nicht verfügbar.",
                        "EXISTING_INTERFACE_MISSING");
                }

                var signalIssues = ExistingInterfaceSignalBinder.Bind(
                    reuseSignalBindings,
                    existingInterface);
                if (signalIssues.Count > 0)
                {
                    return new VisualExecutionResult(
                        false,
                        "Vorhandene Signale konnten nicht eindeutig aufgelöst werden.",
                        signalIssues);
                }
            }

            var selectedContainers = selectedBindings
                .Select(item => item.RuntimeContainer)
                .ToArray();
            ContainerToFeeService.LinkAddonContainers(selectedContainers);
            var sortedCreateSignalContainers = createSignalBindings
                .Select(item => item.RuntimeContainer)
                .OrderBy(container => container.GetType().Name, StringComparer.Ordinal)
                .ThenBy(container => container.ComponentName, StringComparer.Ordinal)
                .ToArray();
            var sortedReuseSignalContainers = reuseSignalBindings
                .Select(item => item.RuntimeContainer)
                .OrderBy(container => container.GetType().Name, StringComparer.Ordinal)
                .ThenBy(container => container.ComponentName, StringComparer.Ordinal)
                .ToArray();

            var timestamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            var unknownInterface = new FeeInterface
            {
                Name = $"Unknown Signals (generated at {timestamp})",
            };

            if (selectedContainers.Length > 0)
            {
                var basicFrame = new FeeBasicFrame
                {
                    Name = $"Auto Generated (at {timestamp})",
                };
                await basicFrame.CreateAsync();
                await basicFrame.SendAndWaitAsync();
                cancellationToken.ThrowIfCancellationRequested();

                if (sortedCreateSignalContainers.Length > 0)
                {
                    var generatedInterface = new FeeInterface
                    {
                        Name = $"Auto Generated (at {timestamp})",
                    };
                    if (!await generatedInterface.CreateInterfaceAsync())
                    {
                        return Failure(
                            "Das neue FEE-Interface wurde nicht verfügbar; es wurden keine Signale erzeugt.",
                            "GENERATED_INTERFACE_NOT_AVAILABLE");
                    }
                    await ContainerToFeeService.CreateAllContainersAsync(
                        sortedCreateSignalContainers,
                        generatedInterface,
                        basicFrame);
                }

                if (sortedReuseSignalContainers.Length > 0)
                {
                    await ContainerToFeeService.CreateAllContainersAsync(
                        sortedReuseSignalContainers,
                        existingInterface!,
                        basicFrame);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (binding.UnknownSignals.Count > 0)
            {
                await unknownInterface.CreateInterfaceAsync();
                await Parallel.ForEachAsync(
                    binding.UnknownSignals,
                    cancellationToken,
                    async (signal, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        await signal.CreateSignalAsync(unknownInterface);
                    });
            }

            logger.Information(
                $"Visuelle Generierung abgeschlossen: {selectedContainers.Length} Container " +
                $"({sortedCreateSignalContainers.Length} mit neuen, " +
                $"{sortedReuseSignalContainers.Length} mit vorhandenen Signalen), " +
                $"{binding.UnknownSignals.Count} unbekannte Signale.");
            return new VisualExecutionResult(
                true,
                $"Generierung abgeschlossen: {selectedContainers.Length} Container wurden verarbeitet; " +
                $"{sortedReuseSignalContainers.Length} davon verwenden vorhandene Signale ohne Überschreiben.",
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
