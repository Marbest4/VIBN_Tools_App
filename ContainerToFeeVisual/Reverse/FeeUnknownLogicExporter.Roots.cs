namespace VIBN_Tools.ContainerToFeeVisual;

public static partial class FeeUnknownLogicExporter
{
    public static FeeUnknownLogicDocument Create(IEnumerable<Fee2ContainerRoot> roots) => Create(roots
        .Where(root => root.Provenance is not null).Select(root =>
        {
            var endpoints = (root.SignalEndpoints ?? new Dictionary<Guid, FeeSignalEndpoint>()).ToDictionary(item => item.Key, item => item.Value);
            foreach (var item in root.ObjectDetails ?? []) endpoints[item.Guid] = new(item.Guid, item.Name, item.FeeType,
                item.LogicDefinitionName, item.SlotNames, item.ParentGuid, item.ParentName, item.CabinetDefinition);
            return new FeeUnknownLogicSource(root.Name, root.Provenance!.ContainerDocument, endpoints,
                root.LiveVariables ?? [], root.LiveAssignments ?? [], root.ObjectLinks ?? []);
        }));
}
