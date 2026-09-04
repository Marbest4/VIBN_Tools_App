using System.Diagnostics;
using System.Xml.Linq;
using SixLabors.Fonts;
using VIBN_Tools.ContainerGeneration.BusinessLogic;
using VIBN_Tools.ContainerGeneration.BusinessLogic.ZuLiData;
using VIBN_Tools.ContainerGeneration.Models;
using VIBN_Tools.ContainerGeneration.BusinessLogic.ContainerData;
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
