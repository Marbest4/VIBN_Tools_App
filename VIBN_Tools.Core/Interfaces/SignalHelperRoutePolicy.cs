namespace VIBN_Tools.Core.Interfaces;

public sealed record SignalHelperInput(Guid ObjectGuid, string Slot);
public sealed record SignalHelperRoute(Guid HelperGuid, IReadOnlyList<SignalHelperInput> Inputs);

/// <summary>Prefer the already connected route; never take an input from another driver.</summary>
public static class SignalHelperRoutePolicy
{
    public static IReadOnlyList<Guid> Select(IEnumerable<SignalHelperRoute> routes, Guid owner, string slot)
    {
        var compatible = routes.Where(route => route.Inputs.All(input => input.ObjectGuid == owner &&
            string.Equals(input.Slot, slot, StringComparison.OrdinalIgnoreCase))).DistinctBy(route => route.HelperGuid).ToArray();
        var connected = compatible.Where(route => route.Inputs.Count > 0).ToArray();
        return (connected.Length > 0 ? connected : compatible).Select(route => route.HelperGuid).ToArray();
    }
}
