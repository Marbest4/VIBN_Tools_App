using System.Xml.Linq;
using VIBN_Tools.ContainerGeneration.Models;

namespace VIBN_Tools.ContainerToFeeVisual;

public static partial class FeeCabinetContainerGrouping
{
    internal static Fee2ContainerRoot Normalize(Fee2ContainerRoot root)
    {
        if (root.Provenance is not { } snapshot) return root;
        var elementGuids = (root.ObjectDetails ?? []).Where(item => item.FeeType.Split('.').Last() == "CabinetElement").Select(item => item.Guid).ToHashSet();
        if (!snapshot.ContainerDocument.Descendants("Container").Any(container => container.Element("Type")?.Value == "CabinetElement" ||
            ContainerFileXml.Objects(container).Any(item => item.Element("FeeType")?.Value?.Split('.').Last() == "CabinetElement") ||
            Guid.TryParse(container.Attribute("targetGuid")?.Value, out var target) && elementGuids.Contains(target))) return root;
        var document = new XDocument(snapshot.ContainerDocument);
        var originals = document.Descendants("Container").ToArray();
        foreach (var binding in snapshot.SignalBindings)
            if (binding.ContainerIndex >= 0 && binding.ContainerIndex < originals.Length)
                originals[binding.ContainerIndex].Element("DataList")?.Elements("Entry").ElementAtOrDefault(binding.EntryIndex)?.SetAttributeValue("feeGuid", binding.VariableGuid.ToString("D"));
        var associations = (root.ObjectAssociations ?? []).ToList();
        var originalIds = originals.ToDictionary(item => item, item => item.Attribute("id")?.Value ?? "");
        ContainerFileXml.EnsureUniqueContainerIds(document);
        for (var index = 0; index < associations.Count; index++)
        {
            var association = associations[index];
            var matches = originals.Where(item => originalIds[item] == association.ContainerId).ToArray();
            if (matches.Length > 1) matches = matches.Where(item => ContainerFileXml.Objects(item).Any(obj => obj.Element("Guid")?.Value == association.ObjectGuid.ToString("D"))).ToArray();
            if (matches.Length == 1) associations[index] = association with { ContainerId = matches[0].Attribute("id")!.Value };
        }
        Apply(document, root.ObjectDetails ?? [], associations);
        var containers = document.Descendants("Container").ToArray();
        var bindings = containers.SelectMany((container, ci) => (container.Element("DataList")?.Elements("Entry") ?? []).Select((entry, ei) => (entry, ci, ei)))
            .Where(item => Guid.TryParse(item.entry.Attribute("feeGuid")?.Value, out var guid) && guid != Guid.Empty)
            .Select(item => new FeeContainerSignalBinding(item.ci, item.ei, Guid.Parse(item.entry.Attribute("feeGuid")!.Value))).ToArray();
        return root with { Provenance = snapshot with { ContainerDocument = document, SignalBindings = bindings, ContainerCount = containers.Length,
                SignalCount = containers.Sum(container => (container.Element("DataList")?.Elements("Entry") ?? []).Count(entry => !string.IsNullOrWhiteSpace(entry.Element("Signal")?.Value))) },
            ObjectAssociations = associations };
    }

}
