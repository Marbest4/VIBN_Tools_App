using System.Diagnostics;
using System.Xml.Linq;
using SixLabors.Fonts;
using VIBN_Tools.ContainerGeneration.BusinessLogic;
using VIBN_Tools.ContainerGeneration.BusinessLogic.ZuLiData;
using VIBN_Tools.ContainerGeneration.Models;
using VIBN_Tools.ContainerGeneration.BusinessLogic.ContainerData;
using VIBN_Tools.ContainerGeneration.BusinessLogic.RequirementsXml;
using VIBN_Tools.ContainerGeneration.Utils;
using VIBN_Tools.ContainerToFee;
using VIBN_Tools.ContainerToFee.GrobStandard;

namespace VIBN_Tools.ContainerGeneration.SmokeTests;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var files = args.Length > 0
            ? args.Select(Path.GetFullPath).ToArray()
            : new[]
            {
                Path.Combine(AppContext.BaseDirectory, "TestData", "Interface5.xlsx"),
                Path.Combine(AppContext.BaseDirectory, "TestData", "Interface7.xlsx")
            };

        var fontsAssembly = typeof(Font).Assembly;
        var fontsVersion = FileVersionInfo.GetVersionInfo(fontsAssembly.Location).FileVersion;
        if (!string.Equals(fontsVersion, "1.0.1.0", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"SixLabors.Fonts 1.0.1.0 erwartet, aber {fontsVersion ?? "keine Version"} aus " +
                $"'{fontsAssembly.Location}' geladen.");
        }

        foreach (var file in files)
            await ValidateImportAndGenerationAsync(file);

        ValidateWorkspacePersistenceAndAutoSaveSettings();
        ValidateSlotMultiplicityPolicy();
        ValidatePlcInputFanInParsing();
        ValidateContainerFileComparison();

        Console.WriteLine(
            $"Container-Generation-Smoke-Test erfolgreich; SixLabors.Fonts {fontsVersion}.");
        return 0;
    }

    private static void ValidateSlotMultiplicityPolicy()
    {
        var plcInput = CreateContainerWithDuplicateSlot("PLC_IN_StatusWord");
        plcInput.Validate();
        if (!plcInput.IsValid)
            throw new InvalidOperationException($"PLC_IN fan-in must be valid: {plcInput.ValidationError}");

        var plcOutput = CreateContainerWithDuplicateSlot("PLC_OUT_ControlWord");
        plcOutput.Validate();
        if (plcOutput.IsValid ||
            !plcOutput.ValidationError.Contains(
                "Ausgänge können nicht doppelt verschaltet werden",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Duplicate PLC_OUT slots must be rejected precisely.");
        }

        var other = CreateContainerWithDuplicateSlot("InternalSlot");
        other.Validate();
        if (other.IsValid ||
            !other.ValidationError.Contains("ausschließlich für PLC_IN_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Non-PLC duplicate slots must remain invalid.");
        }
    }

    private static ContainerData CreateContainerWithDuplicateSlot(string slot) => new()
    {
        Component = "MultiplicityTest",
        Type = "Conveyor",
        DataList = new([
            new ContainerEntry { Signal = "SignalA", Address = "%I0.0", DataType = "Bool", Slot = slot },
            new ContainerEntry { Signal = "SignalB", Address = "%I0.1", DataType = "Bool", Slot = slot }
        ])
    };

    private static void ValidatePlcInputFanInParsing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vibn-fanin-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(path, """
                <ContainerFile>
                  <Container id="1">
                    <Component>FanInConveyor</Component>
                    <Type>Conveyor</Type>
                    <DataList>
                      <Entry><ID>1</ID><Address>%I0.0</Address><DataType>Bool</DataType><Signal>ReadyA</Signal><Slot>PLC_IN_StatusWord</Slot></Entry>
                      <Entry><ID>2</ID><Address>%I0.1</Address><DataType>Bool</DataType><Signal>ReadyB</Signal><Slot>PLC_IN_StatusWord</Slot></Entry>
                    </DataList>
                  </Container>
                </ContainerFile>
                """);

            var (containers, unknownSignals) = ContainerToFeeService.ReadInContainerXmlData(path);
            var container = containers.Single();
            var fanIn = container.GetAdditionalInputFanIns().Single();
            if (unknownSignals.Count != 0 ||
                fanIn.SlotName != "PLC_IN_StatusWord" ||
                fanIn.Signals.Count != 2 ||
                container.CountNonNullSignals() != 2)
            {
                throw new InvalidOperationException("PLC_IN fan-in parsing lost one or more signals.");
            }

            File.WriteAllText(path, File.ReadAllText(path)
                .Replace("PLC_IN_StatusWord", "PLC_OUT_ControlWord", StringComparison.Ordinal));
            try
            {
                _ = ContainerToFeeService.ReadInContainerXmlData(path);
                throw new InvalidOperationException("Duplicate PLC_OUT XML was not rejected.");
            }
            catch (InvalidDataException exception) when (
                exception.Message.Contains("Ausgänge können nicht doppelt verschaltet werden", StringComparison.Ordinal))
            {
            }

            File.WriteAllText(path, """
                <ContainerFile>
                  <Container id="1">
                    <Component>FanInCylinder</Component>
                    <Type>Cylinder</Type>
                    <DataList>
                      <Entry><ID>1</ID><Address>%I0.0</Address><DataType>Bool</DataType><Signal>HomeA</Signal><Slot>PLC_IN_InHomePos</Slot></Entry>
                      <Entry><ID>2</ID><Address>%I0.1</Address><DataType>Bool</DataType><Signal>HomeB</Signal><Slot>PLC_IN_InHomePos</Slot></Entry>
                    </DataList>
                  </Container>
                </ContainerFile>
                """);
            var (listMappedContainers, _) = ContainerToFeeService.ReadInContainerXmlData(path);
            var cylinder = (GrobCylinder_Container)listMappedContainers.Single();
            if (cylinder.Signals_InHomePos.Count != 2 || cylinder.GetAdditionalInputFanIns().Count != 0)
            {
                throw new InvalidOperationException(
                    "Existing PLC_IN list mapping must keep both signals without a duplicate fan-in pass.");
            }
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateWorkspacePersistenceAndAutoSaveSettings()
    {
        var settings = new ContainerGenerationSettings
        {
            AutoSaveEnabled = true,
            AutoSaveIntervalMinutes = 17,
        };
        var restoredSettings = new ContainerGenerationSettings();
        if (!restoredSettings.SetSettings(settings.GetSettings()) ||
            !restoredSettings.AutoSaveEnabled ||
            restoredSettings.AutoSaveIntervalMinutes != 17)
        {
            throw new InvalidOperationException("Container Generation autosave settings were not restored.");
        }

        var path = Path.Combine(Path.GetTempPath(), $"vibn-workspace-{Guid.NewGuid():N}.xml");
        try
        {
            var data = new SavedData { FilePath = path };
            data.CaptureEntryStates();
            data.SetSettings();
            var restored = SavedData.DeserializeProject(path);
            if (restored.ContainerList.Count != 0 || restored.FilePath != path)
                throw new InvalidOperationException("Container Generation workspace round-trip failed.");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateContainerFileComparison()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"vibn-container-compare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var baselinePath = Path.Combine(directory, "baseline.xml");
        var candidatePath = Path.Combine(directory, "candidate.xml");
        try
        {
            File.WriteAllText(baselinePath, BuildContainerFileXml("%I0.0", "PLC_IN_Old", includeRemoved: true, includeAdded: false));
            File.WriteAllText(candidatePath, BuildContainerFileXml("%I0.7", "PLC_IN_New", includeRemoved: false, includeAdded: true));

            var baseline = ContainerFileWorkspaceReader.Read(baselinePath);
            var candidate = ContainerFileWorkspaceReader.Read(candidatePath);
            if (baseline.Containers.Count != 1 || baseline.UnassignedSignals.Count != 1)
                throw new InvalidOperationException("ContainerFile reader did not separate the unknown container.");

            var candidateContainers = candidate.Containers.ToList();
            var candidateUnassigned = candidate.UnassignedSignals.ToList();
            var filtered = new List<ContainerEntry>();
            var summary = GenerationWorkspaceReconciler.Reconcile(
                GenerationWorkspaceReconciler.Capture(
                    baseline.Containers,
                    baseline.UnassignedSignals,
                    []),
                candidateContainers,
                candidateUnassigned,
                filtered,
                new ComparisonRequirements());

            var kinds = summary.Differences.Select(item => item.Kind).ToHashSet();
            if (!kinds.Contains(ReimportChangeKind.SourceChanged) ||
                !kinds.Contains(ReimportChangeKind.RuleSuggestionChanged) ||
                !kinds.Contains(ReimportChangeKind.NewFromSource) ||
                !kinds.Contains(ReimportChangeKind.RemovedFromSource))
            {
                throw new InvalidOperationException("ContainerFile comparison missed add/remove/source/slot changes.");
            }

            foreach (var difference in summary.Differences)
                difference.IsAccepted = difference.Kind is not ReimportChangeKind.RemovedFromSource;
            GenerationWorkspaceReconciler.ApplyDecisions(
                summary,
                candidateContainers,
                candidateUnassigned,
                filtered);

            var entries = candidateContainers.SelectMany(item => item.DataList).ToArray();
            var changed = entries.Single(item => item.ID == "A");
            if (changed.Address != "%I0.7" || changed.Slot != "PLC_IN_New" ||
                entries.All(item => item.ID != "B") || entries.All(item => item.ID != "C"))
            {
                throw new InvalidOperationException("Selective ContainerFile comparison decisions were not applied.");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static string BuildContainerFileXml(
        string address,
        string slot,
        bool includeRemoved,
        bool includeAdded) => $"""
            <CAAMergeResult><ContainerList>
              <Container id="1"><Component>Sensor_1</Component><Type>Sensor</Type><DataList>
                <Entry><ID>A</ID><Address>{address}</Address><DataType>Bool</DataType><Signal>Ready</Signal><Slot>{slot}</Slot><Note /></Entry>
                {(includeRemoved ? "<Entry><ID>B</ID><Address>%I0.1</Address><DataType>Bool</DataType><Signal>Old</Signal><Slot>PLC_IN_Old</Slot><Note /></Entry>" : string.Empty)}
                {(includeAdded ? "<Entry><ID>C</ID><Address>%I0.2</Address><DataType>Bool</DataType><Signal>New</Signal><Slot>PLC_IN_New</Slot><Note /></Entry>" : string.Empty)}
              </DataList></Container>
              <Container id="unknown"><Component>unknown</Component><Type>unknown</Type><DataList>
                <Entry><ID>U</ID><Address>%I9.0</Address><DataType>Bool</DataType><Signal>Unknown</Signal><Slot /><Note /></Entry>
              </DataList></Container>
            </ContainerList></CAAMergeResult>
            """;

    private sealed class ComparisonRequirements : IRequirementsXml
    {
        public string XmlSchema => string.Empty;
        public XDocument Document { get; } = new();
        public bool IsInitialized => true;
        public Result<XDocument> ReadFromFile(string filePath) => Result<XDocument>.Failure("Not supported in test.");
        public Task<Result<XDocument>> ReadFromFileAsync(string filePath) => Task.FromResult(ReadFromFile(filePath));
        public int? GetMinSignals(string componentName) => null;
        public int? GetMaxSignals(string componentName) => null;
        public List<string> GetSlotNames(string componentName) => ["PLC_IN_Old", "PLC_IN_New"];
        public List<string> GetComponentTypes() => ["Sensor"];
    }

    private static async Task ValidateImportAndGenerationAsync(string file)
    {
        if (!File.Exists(file))
            throw new FileNotFoundException("ZuLi-Testdatei fehlt.", file);

        var zuli = new ZuLiDefault();
        var import = await zuli.ReadFromFileAsync(file);
        if (!import.IsSuccess)
            throw new InvalidOperationException($"Import von '{file}' fehlgeschlagen: {import.ErrorMessage}");
        if (import.Value.Count == 0)
            throw new InvalidOperationException($"Import von '{file}' lieferte keine Signale.");

        // The empty requirements model validates the complete XLSX-to-generator
        // hand-off without pretending that a customer component mapping exists.
        var requirements = XDocument.Parse("<AutoCreate><FilterList /></AutoCreate>");
        var generator = new ContainerGenerator();
        var generation = await generator.GenerateAsync(
            new ContainerGenerationRequest(
                import.Value,
                requirements,
                Array.Empty<GroupingRule>(),
                null,
                IgnoreCase: true,
                UseFilterList: false));

        if (generation.Statistics.TotalSignals != import.Value.Count ||
            generation.UnassignedSignals.Count != import.Value.Count)
        {
            throw new InvalidOperationException(
                $"Generatorübergabe für '{file}' ist inkonsistent: " +
                $"Import={import.Value.Count}, Total={generation.Statistics.TotalSignals}, " +
                $"Unassigned={generation.UnassignedSignals.Count}.");
        }

        Console.WriteLine(
            $"{Path.GetFileName(file)}: {import.Value.Count} Signale erfolgreich eingelesen und verarbeitet.");
    }
}
