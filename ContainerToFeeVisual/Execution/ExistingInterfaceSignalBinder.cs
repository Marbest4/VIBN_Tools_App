using System.Reflection;
using VIBN_Tools.ContainerToFee;
using VIBN_Tools.GlobalClasses.FeeObjects;

namespace VIBN_Tools.ContainerToFeeVisual;

/// <summary>
/// Resolves every signal of a runtime container against one existing FEE
/// interface before any model mutation starts. Resolved variables retain
/// their SDK GUID and are never updated by the legacy container code.
/// </summary>
internal static class ExistingInterfaceSignalBinder
{
    public static IReadOnlyList<VisualIssue> Bind(
        IReadOnlyList<BoundVisualContainer> containers,
        FeeInterface existingInterface)
    {
        var issues = new List<VisualIssue>();
        var resolved = new List<(FeeInterfaceSignal Requested, FeeInterfaceSignal Existing)>();
        var availableSignals = existingInterface.Signals ?? [];

        foreach (var bound in containers)
        {
            foreach (var requested in EnumerateSignals(bound.RuntimeContainer))
            {
                var matches = FindMatches(requested, availableSignals);
                if (matches.Length == 0)
                {
                    issues.Add(new VisualIssue(
                        VisualIssueSeverity.Error,
                        "EXISTING_SIGNAL_NOT_FOUND",
                        $"Signal '{SignalIdentity(requested)}' aus Container '{bound.PlanNode.Name}' " +
                        $"wurde im Interface '{existingInterface.Name}' nicht gefunden.",
                        bound.PlanNode.Id));
                    continue;
                }
                if (matches.Length > 1)
                {
                    issues.Add(new VisualIssue(
                        VisualIssueSeverity.Error,
                        "EXISTING_SIGNAL_AMBIGUOUS",
                        $"Signal '{SignalIdentity(requested)}' aus Container '{bound.PlanNode.Name}' " +
                        $"ist im Interface '{existingInterface.Name}' nicht eindeutig.",
                        bound.PlanNode.Id));
                    continue;
                }

                resolved.Add((requested, matches[0]));
            }
        }

        if (issues.Count > 0)
            return issues;

        foreach (var item in resolved)
        {
            item.Requested.Guid = item.Existing.Guid;
            item.Requested.ParentInterface = existingInterface;
            item.Requested.ReuseExistingWithoutUpdate = true;
        }

        return [];
    }

    private static FeeInterfaceSignal[] FindMatches(
        FeeInterfaceSignal requested,
        IReadOnlyCollection<FeeInterfaceSignal> available)
    {
        FeeInterfaceSignal[] byTag = string.IsNullOrWhiteSpace(requested.Tag)
            ? []
            : available.Where(candidate => string.Equals(
                    candidate.Tag,
                    requested.Tag,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        var candidates = byTag.Length > 0
            ? byTag
            : available.Where(candidate => SameLocation(candidate, requested)).ToArray();
        if (candidates.Length <= 1)
            return candidates;

        var exactLocation = candidates.Where(candidate => SameLocation(candidate, requested)).ToArray();
        if (exactLocation.Length > 0)
            candidates = exactLocation;
        if (candidates.Length <= 1)
            return candidates;

        var exactTypeAndUsage = candidates.Where(candidate =>
                candidate.IOType == requested.IOType && candidate.Usage == requested.Usage)
            .ToArray();
        return exactTypeAndUsage.Length > 0 ? exactTypeAndUsage : candidates;
    }

    private static bool SameLocation(FeeInterfaceSignal left, FeeInterfaceSignal right)
    {
        var hasAddress = !string.IsNullOrWhiteSpace(right.Address);
        var hasPath = !string.IsNullOrWhiteSpace(right.Path);
        return (hasAddress || hasPath) &&
               (!hasAddress || string.Equals(left.Address, right.Address, StringComparison.OrdinalIgnoreCase)) &&
               (!hasPath || string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<FeeInterfaceSignal> EnumerateSignals(ContainerBaseClass container)
    {
        foreach (var property in container.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.PropertyType == typeof(FeeInterfaceSignal) &&
                property.GetValue(container) is FeeInterfaceSignal signal)
            {
                yield return signal;
            }
            else if (property.PropertyType == typeof(List<FeeInterfaceSignal>) &&
                     property.GetValue(container) is IEnumerable<FeeInterfaceSignal> signals)
            {
                foreach (var listSignal in signals.Where(item => item is not null))
                    yield return listSignal;
            }
        }
    }

    private static string SignalIdentity(FeeInterfaceSignal signal) =>
        !string.IsNullOrWhiteSpace(signal.Tag)
            ? signal.Tag
            : !string.IsNullOrWhiteSpace(signal.Address)
                ? signal.Address
                : signal.Path;
}
