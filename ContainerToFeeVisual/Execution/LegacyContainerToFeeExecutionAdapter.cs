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

            var selectedContainers = binding.Containers
                .Where(item => plan.IsGenerationSelected(item.PlanNode.Id))
                .Select(item => item.RuntimeContainer)
                .ToArray();
            ContainerToFeeService.LinkAddonContainers(selectedContainers);
            var sortedContainers = selectedContainers
                .OrderBy(container => container.GetType().Name, StringComparer.Ordinal)
                .ThenBy(container => container.ComponentName, StringComparer.Ordinal)
                .ToArray();

            var timestamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            var generatedInterface = new FeeInterface
            {
                Name = $"Auto Generated (at {timestamp})",
            };
            var unknownInterface = new FeeInterface
            {
                Name = $"Unknown Signals (generated at {timestamp})",
            };

            if (sortedContainers.Length > 0)
            {
                var basicFrame = new FeeBasicFrame
                {
                    Name = $"Auto Generated (at {timestamp})",
                };
                await basicFrame.CreateAsync();
                await basicFrame.SendAndWaitAsync();
                cancellationToken.ThrowIfCancellationRequested();

                await generatedInterface.CreateInterfaceAsync();
                await ContainerToFeeService.CreateAllContainersAsync(
                    sortedContainers,
                    generatedInterface,
                    basicFrame);
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
                $"Visuelle Generierung abgeschlossen: {sortedContainers.Length} Container, " +
                $"{binding.UnknownSignals.Count} unbekannte Signale.");
            return new VisualExecutionResult(
                true,
                $"Generierung abgeschlossen: {sortedContainers.Length} Container wurden verarbeitet.",
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
