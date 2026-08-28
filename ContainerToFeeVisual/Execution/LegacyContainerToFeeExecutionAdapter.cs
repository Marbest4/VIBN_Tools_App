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
            var (containers, unknownSignals) =
                ContainerToFeeService.ReadInContainerXmlData(plan.SourceXmlPath);

            var containerNodes = plan.Nodes
                .Where(node => node.Kind == VisualNodeKind.Container &&
                               ContainerMetadataCatalog.TryGet(node.TypeName, out _))
                .ToArray();
            if (containerNodes.Length != containers.Count)
            {
                return Failure(
                    "Der visuelle Plan und der bestehende Container-Parser liefern unterschiedliche " +
                    "Containerzahlen. Die Generierung wurde sicherheitshalber nicht gestartet.",
                    "LEGACY_CONTAINER_COUNT_MISMATCH");
            }

            foreach (var runtimeObject in runtimeObjects.Values.OfType<IAssignableSimObject>())
                runtimeObject.AssignedContainer = null!;

            for (var index = 0; index < containers.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var container = containers[index];
                var node = containerNodes[index];

                if (container is ICreatableContainer creatable)
                    creatable.IsCreationRequested = plan.IsCreationRequested(node.Id);

                if (container is not ISimObjectFindOrSelect selectable)
                    continue;

                var runtimeTargets = selectable.GetSimObjectTargets().ToArray();
                var visualTargets = plan.Targets
                    .Where(target => target.ContainerId == node.Id)
                    .ToArray();
                if (runtimeTargets.Length != visualTargets.Length)
                {
                    return Failure(
                        $"Die Zielstruktur von Container '{node.Name}' hat sich gegenüber dem Plan geändert.",
                        "LEGACY_TARGET_COUNT_MISMATCH",
                        node.Id);
                }

                for (var targetIndex = 0; targetIndex < runtimeTargets.Length; targetIndex++)
                {
                    var runtimeTarget = runtimeTargets[targetIndex];
                    var visualTarget = visualTargets[targetIndex];
                    var assignedRuntimeObjects = new List<FeeAbstractObject>();
                    foreach (var assignment in plan.Assignments.Where(item => item.TargetId == visualTarget.Id))
                    {
                        if (!runtimeObjects.TryGetValue(assignment.FeeObjectId, out var runtimeObject))
                        {
                            return Failure(
                                $"FEE-Objekt '{assignment.FeeObjectName}' ist nicht mehr vorhanden.",
                                "ASSIGNED_FEE_OBJECT_MISSING",
                                visualTarget.Id);
                        }
                        if (!runtimeTarget.AllowedType.IsInstanceOfType(runtimeObject))
                        {
                            return Failure(
                                $"FEE-Objekt '{assignment.FeeObjectName}' ist für '{visualTarget.DisplayName}' nicht kompatibel.",
                                "ASSIGNED_FEE_OBJECT_INCOMPATIBLE",
                                visualTarget.Id);
                        }
                        assignedRuntimeObjects.Add(runtimeObject);
                    }

                    runtimeTarget.AssignObjects(assignedRuntimeObjects);
                    foreach (var assignable in assignedRuntimeObjects.OfType<IAssignableSimObject>())
                        assignable.AssignedContainer = selectable;
                }
            }

            ContainerToFeeService.LinkAddonContainers(containers);
            var sortedContainers = containers
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
            if (unknownSignals.Count > 0)
            {
                await unknownInterface.CreateInterfaceAsync();
                await Parallel.ForEachAsync(
                    unknownSignals,
                    cancellationToken,
                    async (signal, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        await signal.CreateSignalAsync(unknownInterface);
                    });
            }

            logger.Information(
                $"Visuelle Generierung abgeschlossen: {sortedContainers.Length} Container, " +
                $"{unknownSignals.Count} unbekannte Signale.");
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
