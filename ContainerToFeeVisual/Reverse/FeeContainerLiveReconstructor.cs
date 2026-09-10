using System.Xml.Linq;

namespace VIBN_Tools.ContainerToFeeVisual;

public sealed record FeeContainerLiveObject(
    Guid Guid,
    string Name,
    string FeeType,
    string? LogicDefinitionName = null,
    string? CabinetDefinition = null,
    string? Label = null);

public sealed record FeeContainerLiveVariable(
    Guid VariableGuid,
    string Signal,
    string Address,
    string Path,
    string DataType,
    string Comment);

public sealed record FeeContainerLiveAssignment(
    Guid VariableGuid,
    Guid TargetObjectGuid,
    string TargetSlot);

public sealed record FeeContainerReconstructionIssue(
    Guid? ObjectGuid,
    string Message);

public sealed record FeeContainerReconstructionResult(
    FeeContainerProvenanceSnapshot Snapshot,
    int InspectedObjectCount,
    int IgnoredObjectCount,
    IReadOnlyList<FeeContainerReconstructionIssue> Issues);

/// <summary>
/// Reconstructs the container schema from a bounded FEE subtree. Exact
/// Container2FEE provenance remains preferable; this mapper only emits
/// container types and signal slots that the forward generator understands.
/// </summary>
public static class FeeContainerLiveReconstructor
{
    public static FeeContainerReconstructionResult Reconstruct(
        Guid rootGuid,
        string rootName,
        IEnumerable<FeeContainerLiveObject> objects,
        IEnumerable<FeeContainerLiveVariable> variables,
        IEnumerable<FeeContainerLiveAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(assignments);

        var sourceObjects = objects.Where(item => item.Guid != Guid.Empty).ToArray();
        var variableByGuid = variables
            .Where(item => item.VariableGuid != Guid.Empty)
            .GroupBy(item => item.VariableGuid)
            .ToDictionary(group => group.Key, group => group.First());
        var assignmentsByObject = assignments
            .Where(item => item.TargetObjectGuid != Guid.Empty && item.VariableGuid != Guid.Empty)
            .GroupBy(item => item.TargetObjectGuid)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var issues = new List<FeeContainerReconstructionIssue>();
        var candidates = new List<ContainerCandidate>();

        foreach (var item in sourceObjects)
        {
            if (!TryCreateCandidate(item, out var candidate, out var ambiguity))
                continue;
            candidates.Add(candidate!);
            if (!string.IsNullOrWhiteSpace(ambiguity))
                issues.Add(new FeeContainerReconstructionIssue(item.Guid, ambiguity));
        }

        var containerElements = new List<XElement>();
        var bindings = new List<FeeContainerSignalBinding>();
        foreach (var candidate in candidates
                     .OrderBy(item => item.ComponentName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.XmlType, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Object.Guid))
        {
            assignmentsByObject.TryGetValue(candidate.Object.Guid, out var objectAssignments);
            objectAssignments ??= [];
            var resolved = objectAssignments
                .Select(assignment => TryCreateEntry(candidate, assignment, variableByGuid))
                .Where(item => item is not null)
                .Cast<ResolvedEntry>()
                .GroupBy(item => (item.Variable.VariableGuid, item.XmlSlot))
                .Select(group => group.First())
                .OrderBy(item => item.XmlSlot, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Variable.Signal, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var componentName = candidate.ComponentName;
            if (string.IsNullOrWhiteSpace(componentName))
                componentName = resolved.FirstOrDefault()?.Variable.Signal;
            if (string.IsNullOrWhiteSpace(componentName))
                componentName = $"FEE-Objekt {candidate.Object.Guid:D}";

            var dataList = new XElement("DataList");
            var containerIndex = containerElements.Count;
            foreach (var entry in resolved)
            {
                var variable = entry.Variable;
                dataList.Add(new XElement("Entry",
                    new XElement("ID", variable.Comment ?? string.Empty),
                    new XElement("Address", string.IsNullOrWhiteSpace(variable.Path)
                        ? variable.Address ?? string.Empty
                        : variable.Path),
                    new XElement("DataType", variable.DataType ?? string.Empty),
                    new XElement("Signal", variable.Signal ?? string.Empty),
                    new XElement("Slot", entry.XmlSlot),
                    new XElement("Note", $"Aus FEE rekonstruiert; Objekt {candidate.Object.Guid:D}.")));
                bindings.Add(new FeeContainerSignalBinding(
                    containerIndex,
                    dataList.Elements("Entry").Count() - 1,
                    variable.VariableGuid));
            }

            if (resolved.Length == 0)
            {
                issues.Add(new FeeContainerReconstructionIssue(
                    candidate.Object.Guid,
                    $"'{componentName}' wurde als {candidate.XmlType} erkannt, besitzt aber keine " +
                    "eindeutig rücklesbare Variablenzuordnung."));
            }

            containerElements.Add(new XElement("Container",
                new XAttribute("id", $"fee:{candidate.Object.Guid:D}"),
                new XElement("Component", componentName),
                new XElement("Type", candidate.XmlType),
                dataList));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("CAAMergeResult",
                new XAttribute("version", "1.0.0.0"),
                new XAttribute("createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                new XAttribute("source", "FEE"),
                new XAttribute("feeRootGuid", rootGuid.ToString("D")),
                new XAttribute("feeRootName", rootName ?? string.Empty),
                new XElement("ContainerList", containerElements)));
        var snapshot = new FeeContainerProvenanceSnapshot(
            new Dictionary<string, string>(StringComparer.Ordinal),
            document,
            bindings,
            containerElements.Count,
            bindings.Count,
            string.Empty);
        return new FeeContainerReconstructionResult(
            snapshot,
            sourceObjects.Length,
            sourceObjects.Length - candidates.Count,
            issues);
    }

    private static bool TryCreateCandidate(
        FeeContainerLiveObject item,
        out ContainerCandidate? candidate,
        out string? ambiguity)
    {
        candidate = null;
        ambiguity = null;
        var feeType = item.FeeType ?? string.Empty;
        string? xmlType = null;
        if (EndsWithType(feeType, "LogicObject"))
        {
            var matches = ContainerMetadataCatalog.FindXmlTypesByLogicName(item.LogicDefinitionName);
            if (matches.Count == 0)
                return false;
            xmlType = matches[0];
            if (matches.Count > 1)
            {
                ambiguity = $"Die Logik '{item.LogicDefinitionName}' passt zu {string.Join(" oder ", matches)}. " +
                            $"Für den Export wird '{xmlType}' verwendet; bitte im Vergleich prüfen.";
            }
        }
        else if (EndsWithType(feeType, "BoolNot"))
        {
            xmlType = "ReturnCircuit";
            ambiguity = "Ein BoolNot kann ohne Provenienz nicht sicher zwischen ReturnCircuit und SafeArea " +
                        "unterschieden werden. Für den Export wird 'ReturnCircuit' verwendet.";
        }
        else if (EndsWithType(feeType, "Button"))
        {
            xmlType = "Button";
        }
        else if (EndsWithType(feeType, "SegmentedLamp"))
        {
            xmlType = "Stacklight";
        }
        else if (EndsWithType(feeType, "CabinetElement"))
        {
            xmlType = MapCabinetType(item.CabinetDefinition);
            if (xmlType is null)
                return false;
        }

        if (xmlType is null || !ContainerMetadataCatalog.TryGet(xmlType, out var descriptor))
            return false;
        var componentName = EndsWithType(feeType, "CabinetElement")
            ? FirstNotBlank(item.Label, item.Name?.Split(';')[0])
            : item.Name;
        candidate = new ContainerCandidate(item, xmlType, descriptor, componentName ?? string.Empty);
        return true;
    }

    private static ResolvedEntry? TryCreateEntry(
        ContainerCandidate candidate,
        FeeContainerLiveAssignment assignment,
        IReadOnlyDictionary<Guid, FeeContainerLiveVariable> variables)
    {
        if (!variables.TryGetValue(assignment.VariableGuid, out var variable))
            return null;
        var slot = MapSlot(candidate, assignment.TargetSlot);
        return string.IsNullOrWhiteSpace(slot) ? null : new ResolvedEntry(variable, slot);
    }

    private static string? MapSlot(ContainerCandidate candidate, string runtimeSlot)
    {
        if (string.IsNullOrWhiteSpace(runtimeSlot))
            return null;
        var directSlot = candidate.Descriptor.Slots.FirstOrDefault(slot =>
            string.Equals(slot, runtimeSlot, StringComparison.OrdinalIgnoreCase));
        if (directSlot is not null)
            return directSlot;

        var normalizedSlot = runtimeSlot.Trim().ToUpperInvariant();

        return candidate.XmlType switch
        {
            "Button" => normalizedSlot switch
            {
                "PRESSED" => "PLC_IN_NO",
                "PRESSEDINVERTED" => "PLC_IN_NC",
                _ => null,
            },
            "Stacklight" => normalizedSlot switch
            {
                "RED" => "PLC_NO_Red",
                "YELLOW" => "PLC_NO_Yellow",
                "GREEN" => "PLC_NO_Green",
                "BLUE" => "PLC_NO_Blue",
                "WHITE" => "PLC_NO_White",
                _ => null,
            },
            "ReturnCircuit" => normalizedSlot switch
            {
                "INPUT 01" => "PLC_OUT_Signal",
                "OUTPUT 01" => "PLC_IN_Signal",
                _ => null,
            },
            "Switch" or "EStop" => normalizedSlot switch
            {
                "NO1" => "PLC_IN_NO1",
                "NO2" => "PLC_IN_NO2",
                "NC1" => "PLC_IN_NC1",
                "NC2" => "PLC_IN_NC2",
                _ => null,
            },
            "Fuse" => normalizedSlot switch
            {
                "NO" => "PLC_IN_NO",
                "NC" => "PLC_IN_NC",
                _ => null,
            },
            "CabinetLamp" when normalizedSlot == "ON" => "PLC_OUT_ON",
            _ => null,
        };
    }

    private static string? MapCabinetType(string? definition)
    {
        if (string.Equals(definition, "Grob_NotAus", StringComparison.OrdinalIgnoreCase))
            return "EStop";
        if (string.Equals(definition, "Fuse", StringComparison.OrdinalIgnoreCase))
            return "Fuse";
        if (definition?.StartsWith("Lamp ", StringComparison.OrdinalIgnoreCase) == true)
            return "CabinetLamp";
        if (string.Equals(definition, "Grob_2PositionSwitch", StringComparison.OrdinalIgnoreCase))
            return "Switch";
        return null;
    }

    private static bool EndsWithType(string value, string typeName) =>
        string.Equals(value, typeName, StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith($".{typeName}", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNotBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private sealed record ContainerCandidate(
        FeeContainerLiveObject Object,
        string XmlType,
        ContainerDescriptor Descriptor,
        string ComponentName);

    private sealed record ResolvedEntry(FeeContainerLiveVariable Variable, string XmlSlot);
}
