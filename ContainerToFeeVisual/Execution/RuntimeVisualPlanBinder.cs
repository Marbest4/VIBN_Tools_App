using VIBN_Tools.ContainerToFee;
using VIBN_Tools.GlobalClasses.FeeObjects;
using static VIBN_Tools.GlobalClasses.Interfaces;

namespace VIBN_Tools.ContainerToFeeVisual;

internal sealed record BoundVisualContainer(ContainerBaseClass RuntimeContainer, VisualNode PlanNode);

internal sealed record RuntimeVisualPlanBindingResult(
    bool Success,
    IReadOnlyList<BoundVisualContainer> Containers,
    IReadOnlyList<FeeInterfaceSignal> UnknownSignals,
    VisualIssue? Issue);

/// <summary>
/// Recreates the legacy runtime containers and applies the visual assignments.
/// Both full generation and link-only execution use this single mapping path.
/// </summary>
internal static class RuntimeVisualPlanBinder
{
    public static RuntimeVisualPlanBindingResult Bind(
        VisualPlan plan,
        IReadOnlyDictionary<string, FeeAbstractObject> runtimeObjects)
    {
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
                "Containerzahlen. Der Vorgang wurde sicherheitshalber nicht gestartet.",
                "LEGACY_CONTAINER_COUNT_MISMATCH",
                unknownSignals);
        }

        foreach (var runtimeObject in runtimeObjects.Values.OfType<IAssignableSimObject>())
            runtimeObject.AssignedContainer = null!;

        var bound = new List<BoundVisualContainer>(containers.Count);
        for (var index = 0; index < containers.Count; index++)
        {
            var container = containers[index];
            var node = containerNodes[index];
            bound.Add(new BoundVisualContainer(container, node));

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
                    unknownSignals,
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
                            unknownSignals,
                            visualTarget.Id);
                    }
                    if (!runtimeTarget.AllowedType.IsInstanceOfType(runtimeObject))
                    {
                        return Failure(
                            $"FEE-Objekt '{assignment.FeeObjectName}' ist für " +
                            $"'{visualTarget.DisplayName}' nicht kompatibel.",
                            "ASSIGNED_FEE_OBJECT_INCOMPATIBLE",
                            unknownSignals,
                            visualTarget.Id);
                    }
                    assignedRuntimeObjects.Add(runtimeObject);
                }

                runtimeTarget.AssignObjects(assignedRuntimeObjects);
                foreach (var assignable in assignedRuntimeObjects.OfType<IAssignableSimObject>())
                    assignable.AssignedContainer = selectable;
            }
        }

        return new RuntimeVisualPlanBindingResult(true, bound, unknownSignals, null);
    }

    private static RuntimeVisualPlanBindingResult Failure(
        string message,
        string code,
        IReadOnlyList<FeeInterfaceSignal> unknownSignals,
        string? nodeId = null) =>
        new(false, [], unknownSignals, new VisualIssue(VisualIssueSeverity.Error, code, message, nodeId));
}
