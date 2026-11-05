using VIBN_Tools.ContainerGeneration.Models;

namespace VIBN_Tools.ContainerToFeeVisual;

public sealed partial class FeeSignalOwnershipGraph
{
    public IReadOnlyList<FeeContainerObjectDetails> CreateDetails(IEnumerable<FeeContainerLiveAssignment> assignments)
    {
        var signals = assignments.ToLookup(item => item.TargetObjectGuid);
        return _objects.Values.Select(item => new FeeContainerObjectDetails(item.Guid, item.Name, item.FeeType,
            item.ParentName, item.LogicDefinitionName.Length > 0 ? item.LogicDefinitionName :
                ContainerFileXml.IsSimpleLogicType(item.FeeType) ? item.FeeType : "",
            string.Join("; ", signals[item.Guid].Select(route => $"{route.TargetSlot} ↔ Signal {route.VariableGuid:D}" + (route.IsIndirect ? " (über Hilfslogik)" : ""))
                .Concat(_peers[item.Guid].Select(peer => $"{peer.LocalSlot} ↔ {_objects.GetValueOrDefault(peer.Guid)?.Name ?? peer.Guid.ToString("D")}/{peer.RemoteSlot}"))
                .Distinct(StringComparer.OrdinalIgnoreCase)),
            VIBN_Tools.SpecialDevices.FeeSpecialDeviceLogicCatalog.Contains(item.LogicDefinitionName), item.ParentGuid,
            item.SlotNames, item.CabinetDefinition)).ToArray();
    }

}
