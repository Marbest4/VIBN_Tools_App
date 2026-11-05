using VIBN_Tools.GlobalClasses;
using VIBN_Tools.GlobalClasses.FeeObjects;
using VIBN_Tools.Core.Interfaces;

namespace VIBN_Tools.ContainerToFee;

/// <summary>Reuses confirmed signal routes before creating another technical object.</summary>
internal static class ContainerSignalHelperReuse
{
    internal static async Task<Guid?> FindAsync(string type, IEnumerable<(FeeInterfaceSignal? Signal, string Slot)> signals)
    {
        var candidates = new HashSet<Guid>();
        foreach (var (signal, slot) in signals.Where(item => item.Signal?.ReuseExistingWithoutUpdate == true))
        {
            var assignments = await Services.ApiInstance.Interface.GetAssignedSceneObjectsAsync(signal!.Guid)
                ?? throw new InvalidOperationException($"Signalroute '{signal.Tag}' konnte nicht gelesen werden. FEE aktualisieren.");
            foreach (var (guid, slots) in assignments)
                if ((slots ?? []).Contains(slot, StringComparer.OrdinalIgnoreCase))
                {
                    var actual = Services.ApiInstance.XmlHelper.ConvertToString(await Services.ApiInstance.Object.GetPropertyAsync(guid, "Type"));
                    if (string.Equals(actual.Split('.').Last(), type, StringComparison.OrdinalIgnoreCase)) candidates.Add(guid);
                }
        }
        if (candidates.Count > 1)
            throw new InvalidOperationException("Mehrere vorhandene Hilfslogiken sind mit den Signalen verbunden. Keine weitere Hilfslogik erzeugt; Zuordnung prüfen.");
        return candidates.Count == 1 ? candidates.Single() : null;
    }

    internal static async Task<FeeSimpleMove> GetMoveAsync(ContainerBaseClass container, FeeLogic logic,
        string slot, FeeInterfaceSignal signal, FeeInterface targetInterface)
    {
        await signal.CreateSignalAsync(targetInterface);
        var routes = new List<SignalHelperRoute>();
        if (signal.ReuseExistingWithoutUpdate)
        {
            var assignments = await Services.ApiInstance.Interface.GetAssignedSceneObjectsAsync(signal.Guid)
                ?? throw new InvalidOperationException($"Signalroute '{signal.Tag}' konnte nicht gelesen werden.");
            foreach (var (guid, slots) in assignments)
            {
                if (!(slots ?? []).Contains("Output 01", StringComparer.OrdinalIgnoreCase)) continue;
                var type = Services.ApiInstance.XmlHelper.ConvertToString(await Services.ApiInstance.Object.GetPropertyAsync(guid, "Type"));
                if (!string.Equals(type.Split('.').Last(), "MoveBit", StringComparison.OrdinalIgnoreCase)) continue;
                var inputs = await Services.ApiInstance.Interface.GetSlotSlotAssignmentAsync(guid, "Input 01")
                    ?? throw new InvalidOperationException("Eingang der vorhandenen MoveBit-Route konnte nicht gelesen werden.");
                routes.Add(new(guid, inputs.SelectMany(peer => (peer.SlotNames ?? []).DefaultIfEmpty("").Select(name =>
                    new SignalHelperInput(Guid.TryParse(peer.SceneObjectGuid, out var driver) ? driver : Guid.Empty, name))).ToArray()));
            }
        }
        var reusable = SignalHelperRoutePolicy.Select(routes, logic.Guid, slot);
        if (reusable.Count > 1) throw new InvalidOperationException("MoveBit-Signalroute ist mehrdeutig. Keine zusätzliche Hilfslogik erzeugt; Zuordnung prüfen.");
        if (reusable.Count == 1) return new FeeSimpleMove { Guid = reusable[0] };
        var move = new FeeSimpleMove { Name = $"{container.ComponentName} {slot} {signal.Tag}", Parent = logic.Parent };
        if (!await move.CreateAsync() || !await move.SendAndWaitAsync())
            throw new InvalidOperationException($"MoveBit für '{signal.Tag}' wurde von FEE nicht bestätigt.");
        await ContainerObjectProvenance.WriteNewObjectAsync(move, container);
        await ContainerSlotLinkService.AssignVariableAndVerifyAsync(move.Guid, "Output 01", signal.Guid,
            $"{container.ComponentName}: MoveBit Output 01");
        return move;
    }
}
