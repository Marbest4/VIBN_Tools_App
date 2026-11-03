namespace VIBN_Tools.ContainerToFeeVisual;

/// <summary>Matches scene identities using live endpoints rather than CAD labels.</summary>
public static class FeeLogicConnectionMatcher
{
    public static bool IsConnected(Guid logic, IEnumerable<Guid> objects, IEnumerable<VisualFeeObjectLink> links)
    {
        var ids = objects.ToHashSet();
        return links.Any(link => !string.IsNullOrWhiteSpace(link.SlotName) && !string.IsNullOrWhiteSpace(link.LinkedSlotName) &&
            Guid.TryParse(link.ObjectGuidString, out var first) && Guid.TryParse(link.LinkedObjectGuidString, out var second) &&
            (first == logic && ids.Contains(second) || second == logic && ids.Contains(first)));
    }
}
