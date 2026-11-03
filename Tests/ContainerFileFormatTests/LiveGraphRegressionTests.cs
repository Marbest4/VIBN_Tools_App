using System.Xml.Linq;
using VIBN_Tools.ContainerToFeeVisual;
using VIBN_Tools.Core.Interfaces;

internal static class LiveGraphRegressionTests
{
    internal static async Task RunAsync()
    {
        var logic = Guid.NewGuid(); var joint = Guid.NewGuid(); var copy = Guid.NewGuid(); var other = Guid.NewGuid();
        VisualFeeObjectLink[] links = [new(joint.ToString(), "InTarget", logic.ToString(), "SIM_TargetPosition"),
            new(logic.ToString(), "PLC_IN_InHomePos", copy.ToString(), "Input 01")];
        Require(FeeLogicConnectionMatcher.IsConnected(logic, [joint], links), "A differently named joint lost its connected logic.");
        Require(!FeeLogicConnectionMatcher.IsConnected(other, [joint], links), "An unrelated logic was accepted.");
        var freeHelper = Guid.NewGuid(); var foreignHelper = Guid.NewGuid();
        var reusable = SignalHelperRoutePolicy.Select([new(copy, [new(logic, "PLC_IN_Feedback")]),
            new(freeHelper, []), new(foreignHelper, [new(other, "PLC_IN_Feedback")])], logic, "PLC_IN_Feedback");
        Require(reusable.SequenceEqual([copy]), "An existing MoveBit route was duplicated or a foreign driver was selected.");
        Require(SignalHelperRoutePolicy.Select([new(foreignHelper, [new(other, "Input")])], logic, "Input").Count == 0,
            "A foreign helper input was eligible for rewiring.");
        var endpoints = new Dictionary<Guid, FeeSignalEndpoint>
        {
            [logic] = new(logic, "Cylinder logic", "LogicObject", "Grob_Cylinder"),
            [joint] = new(joint, "CAD Joint 42", "MotionJoint"),
            [copy] = new(copy, "Copy", "BoolCopy5Outputs")
        };
        var graph = new FeeSignalOwnershipGraph(endpoints, links);
        Require(graph.Owners(copy).Single() == new FeeSignalOwnershipGraph.Owner(logic, "PLC_IN_InHomePos"),
            "BoolCopy output signals did not inherit their input-side container.");
        Require(graph.Members(logic).Contains(copy), "The helper vanished from its upstream container.");
        // API direction and enumeration order must not change ownership.
        var reversed = links.Reverse().Select(link => new VisualFeeObjectLink(link.LinkedObjectGuidString, link.LinkedSlotName, link.ObjectGuidString, link.SlotName));
        Require(new FeeSignalOwnershipGraph(endpoints, reversed).Owners(copy).SequenceEqual(graph.Owners(copy)), "Reverse API link orientation changed ownership.");
        endpoints[other] = new(other, "Bridge", "MoveByte");
        var bridge = new FeeSignalOwnershipGraph(endpoints, [new(copy.ToString(), "Output 05", other.ToString(), "Input 01")]);
        Require(bridge.Owners(copy).Single().Guid == bridge.Owners(other).Single().Guid,
            "A helper chain between two interfaces was split into unrelated owners.");
        foreach (var address in new[] { "A4500.0", "%Q4500.0", "PAW4500", "%PQW4500" })
            Require(PlcSignalSlotPolicy.AssignmentWarning(address, "PLC_IN_NO1").Length > 0, "Output/input conflict accepted: " + address);
        Require(PlcSignalSlotPolicy.AssignmentWarning("Axis1.Value", "PLC_IN_X") == "", "A symbolic name was mistaken for an output address.");
        Require(PlcSignalSlotPolicy.AssignmentWarning("A4500.0", "PLC_OUT_Command") == "", "A valid output was rejected.");
        var folder = FindRepository();
        var total = 0; var bad = 0; var files = Directory.GetFiles(folder, "Container*.xml");
        Require(files.Length == 5, "Expected all five supplied Container XML fixtures.");
        foreach (var file in files)
        {
            var xml = XDocument.Load(file);
            var containers = xml.Descendants("Container").ToArray(); total += containers.Length;
            var conflicts = containers.SelectMany(container => container.Descendants("Entry")).Count(entry =>
                PlcSignalSlotPolicy.AssignmentWarning(entry.Element("Address")?.Value, entry.Element("Slot")?.Value).Length > 0);
            bad += conflicts;
            Console.WriteLine($"{Path.GetFileName(file)}: {containers.Length} containers, {conflicts} direction conflicts");
        }
        Require(total == 776 && bad >= 7, "Example coverage or known direction conflicts changed unexpectedly.");
        var id = Guid.NewGuid();
        Require(LogicDefinitionXml.ReadGuid(XElement.Parse($"<LogicObject><Logic PersistedLogicGuid='{id}'/></LogicObject>")) == id,
            "Attribute-based FEE logic identity was lost.");
        Require(LogicDefinitionXml.ReadGuid(XElement.Parse($"<LogicObject><Logic><PersistedLogicGuid>{id}</PersistedLogicGuid></Logic></LogicObject>")) == id,
            "Element-based FEE logic identity was lost.");
        endpoints[logic] = endpoints[logic] with { LogicDefinitionName = "Customer_Unknown", SlotNames = ["PLC_OUT_Command", "PLC_IN_Feedback", "Unused"] };
        var command = Guid.NewGuid(); var feedback = Guid.NewGuid();
        var unknownXml = XDocument.Parse($"""
            <CAAResult><ContainerList><Container id="unknown" targetGuid="{logic}">
            <Component>Unknown</Component><Type>N/A</Type><DataList>
            <Entry feeGuid="{command}"><Signal>Command</Signal><Address>A4500.0</Address><DataType>Bool</DataType></Entry>
            <Entry feeGuid="{feedback}"><Signal>Feedback</Signal><Address>E12.7</Address><DataType>Bool</DataType></Entry>
            </DataList></Container></ContainerList></CAAResult>
            """);
        var export = FeeUnknownLogicExporter.Create([new FeeUnknownLogicSource("Test root", unknownXml, endpoints,
            [new(command, "Command", "A4500.0", "", "Bool", "", "Read", "plc", "PLC"),
             new(feedback, "Feedback", "E12.7", "", "Bool", "", "Write", "io", "IO")],
            [new(command, logic, "PLC_OUT_Command"), new(feedback, logic, "PLC_IN_Feedback", true), new(feedback, copy, "Output 01")], links)]);
        var exportedLogic = export.Containers.Single().Logics.Single(item => item.Guid == logic);
        Require(exportedLogic.LogicDefinitionName == "Customer_Unknown", "N/A export lost the live logic definition.");
        Require(exportedLogic.Slots.Single(item => item.Name == "PLC_IN_Feedback").Signals.Single().Address == "E12.7", "N/A export lost an indirect input signal.");
        Require(exportedLogic.Slots.Single(item => item.Name == "PLC_OUT_Command").Signals.Single().InterfaceName == "PLC", "N/A export lost the command interface.");
        Require(exportedLogic.Slots.Any(item => item.Name == "Unused"), "N/A export omitted an unassigned logic slot.");
        var path = Path.Combine(Path.GetTempPath(), $"vibn-na-{Guid.NewGuid():N}.json");
        try
        {
            await FeeUnknownLogicExporter.SaveAsync(export, path);
            using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Require(json.RootElement.GetProperty("containers").GetArrayLength() == 1, "N/A JSON file did not contain the selected container.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        var cabinetGuid = Guid.NewGuid(); var switchGuid = Guid.NewGuid();
        var cabinets = XDocument.Parse($"""
            <CAAResult><ContainerList>
            <Container id="switch1" targetGuid="{switchGuid}"><Component>Selector</Component><Type>CabinetElement</Type><DataList><Entry feeGuid="{command}"><Signal>On</Signal><Slot>PLC_IN_NO1</Slot></Entry></DataList></Container>
            <Container id="switch2" targetGuid="{switchGuid}"><Component>Selector</Component><Type>CabinetElement</Type><DataList><Entry feeGuid="{feedback}"><Signal>Off</Signal><Slot>PLC_IN_NC1</Slot></Entry></DataList></Container>
            </ContainerList></CAAResult>
            """);
        List<FeeContainerObjectAssociation> associations = [];
        FeeCabinetContainerGrouping.Apply(cabinets,
            [new(cabinetGuid, "Panel", "Cabinet", "", "", ""),
             new(switchGuid, "Selector", "CabinetElement", "Panel", "", "", ParentGuid: cabinetGuid.ToString(), CabinetDefinition: "Grob_2PositionSwitch")], associations);
        var cabinetRows = cabinets.Descendants("Container").ToArray();
        var parentRow = cabinetRows.Single(row => row.Element("Type")?.Value == "Cabinet");
        var childRow = cabinetRows.Single(row => row.Element("Type")?.Value == "CabinetSwitch");
        Require(cabinetRows.Length == 2 && childRow.Attribute("parentContainerId")?.Value == parentRow.Attribute("id")?.Value,
            "Cabinet elements remained duplicate top-level containers.");
        Require(childRow.Descendants("Entry").Count() == 2 && associations.Any(item => item.ObjectGuid == cabinetGuid),
            "Cabinet grouping lost interface signals or its primary Cabinet association.");
        Console.WriteLine("Live graph, helper ownership, logic identity and supplied XML regressions passed.");
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "VIBN_Tools_App.sln"))) return directory.FullName;
        throw new InvalidOperationException("Repository fixtures not found.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
