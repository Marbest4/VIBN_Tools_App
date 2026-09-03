using System.Diagnostics;
using System.Xml.Linq;
using SixLabors.Fonts;
using VIBN_Tools.ContainerGeneration.BusinessLogic;
using VIBN_Tools.ContainerGeneration.BusinessLogic.ZuLiData;
using VIBN_Tools.ContainerGeneration.Models;

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

        Console.WriteLine(
            $"Container-Generation-Smoke-Test erfolgreich; SixLabors.Fonts {fontsVersion}.");
        return 0;
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
