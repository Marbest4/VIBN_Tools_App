using System.Diagnostics;
using System.Xml.Linq;
using SixLabors.Fonts;
using VIBN_Tools.ContainerGeneration.BusinessLogic;
using VIBN_Tools.ContainerGeneration.AI;
using VIBN_Tools.ContainerGeneration.BusinessLogic.ZuLiData;
using VIBN_Tools.ContainerGeneration.Models;
using VIBN_Tools.ContainerGeneration.BusinessLogic.ContainerData;
using VIBN_Tools.ContainerGeneration.BusinessLogic.RequirementsXml;
using VIBN_Tools.ContainerGeneration.Utils;
using VIBN_Tools.ContainerToFee;
using VIBN_Tools.ContainerToFee.GrobStandard;
using VIBN_Tools.ContainerToFeeVisual;
using VIBN_Tools.SpecialDevices;

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
        await ValidateFee2ContainerProvenanceRoundTripAsync();
        ValidateFee2SpecialDevicesProvenanceRoundTrip();
        ValidateRuleSuggestionWorkflow();
        await ValidateRequirementsRulePatchWorkflowAsync();

        Console.WriteLine(
            $"Container-Generation-Smoke-Test erfolgreich; SixLabors.Fonts {fontsVersion}.");
        return 0;
    }

    private static void ValidateFee2SpecialDevicesProvenanceRoundTrip()
    {
        var variableGuid = Guid.NewGuid();
        var snapshot = new FeeSpecialDeviceSnapshot(
            FeeSpecialDeviceProvenanceCodec.CurrentSchema,
            "SCN01",
            "Keyence",
            "SR2000",
            null,
            100,
            200,
            new[]
            {
                new FeeSpecialDeviceSignalSnapshot(
                    variableGuid,
                    "SCN01_Ready",
                    "E100.0",
                    "Write",
                    "Bool",
                    "Bereit")
            });
        var tags = FeeSpecialDeviceProvenanceCodec.Encode(snapshot);
        if (!FeeSpecialDeviceProvenanceCodec.TryRead(tags, out var decoded, out var error) ||
            decoded is null || decoded.Prefix != "SCN01" ||
            decoded.Signals.Single().VariableGuid != variableGuid)
        {
            throw new InvalidOperationException($"Special-device provenance round-trip failed: {error}");
        }

        var exportPath = Path.Combine(Path.GetTempPath(), $"vibn-{Guid.NewGuid():N}.specialdevice.json");
        try
        {
            FeeSpecialDeviceProvenanceCodec.SaveAtomically(decoded, exportPath);
            var exported = System.Text.Json.JsonSerializer.Deserialize<FeeSpecialDeviceSnapshot>(
                File.ReadAllText(exportPath));
            if (exported?.InputByte != 100 || exported.OutputByte != 200 || exported.Signals.Count != 1)
                throw new InvalidOperationException("Special-device reverse export lost domain data.");
        }
        finally
        {
            if (File.Exists(exportPath))
                File.Delete(exportPath);
        }

        var damaged = new Dictionary<string, string>(tags, StringComparer.Ordinal)
        {
            [FeeSpecialDeviceProvenanceCodec.HashKey] = new string('0', 64)
        };
        if (FeeSpecialDeviceProvenanceCodec.TryRead(damaged, out _, out _))
            throw new InvalidOperationException("Damaged special-device provenance was accepted.");
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

    private static async Task ValidateFee2ContainerProvenanceRoundTripAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"vibn-fee-roundtrip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.xml");
        var exportedPath = Path.Combine(directory, "exported.xml");
        try
        {
            File.WriteAllText(sourcePath, """
                <ContainerFile>
                  <Container id="1">
                    <Component>FanInCylinder</Component><Type>Cylinder</Type><DataList>
                      <Entry><ID>A</ID><Address>%I0.0</Address><DataType>Bool</DataType><Signal>HomeA</Signal><Slot>PLC_IN_InHomePos</Slot></Entry>
                      <Entry><ID>B</ID><Address>%I0.1</Address><DataType>Bool</DataType><Signal>HomeB</Signal><Slot>PLC_IN_InHomePos</Slot></Entry>
                    </DataList>
                  </Container>
                  <Container id="2">
                    <Component>ExcludedSensor</Component><Type>Sensor</Type><DataList>
                      <Entry><ID>C</ID><Address>%I0.2</Address><DataType>Bool</DataType><Signal>Detected</Signal><Slot>PLC_IN_PartPresent_Ch1</Slot></Entry>
                    </DataList>
                  </Container>
                </ContainerFile>
                """);

            var planService = new ContainerToFeeVisualPlanService();
            var loaded = await planService.LoadXmlAsync(sourcePath);
            if (!loaded.Success || loaded.Plan is null)
                throw new InvalidOperationException(
                    $"Round-trip plan could not be parsed: {loaded.Message}; " +
                    string.Join(" | ", loaded.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
            var selectedId = loaded.Plan.Nodes
                .Single(node => node.Kind == VisualNodeKind.Container && node.Name == "FanInCylinder")
                .Id;
            var source = XDocument.Load(sourcePath);
            var variableA = Guid.NewGuid();
            var variableB = Guid.NewGuid();
            var encoded = FeeContainerProvenanceCodec.Create(
                source,
                new HashSet<string>(StringComparer.Ordinal) { selectedId },
                loaded.Plan.SourceFingerprint,
                new Dictionary<string, IReadOnlyList<FeeContainerSignalSource>>(StringComparer.Ordinal)
                {
                    [selectedId] =
                    [
                        new("A", "HomeA", "%I0.0", "Bool", variableA),
                        new("B", "HomeB", "%I0.1", "Bool", variableB),
                    ]
                });
            if (!FeeContainerProvenanceCodec.TryRead(encoded.Tags, out var decoded, out var error) ||
                decoded is null)
            {
                throw new InvalidOperationException($"Provenance could not be decoded: {error}");
            }
            if (decoded.ContainerCount != 1 || decoded.SignalCount != 2 ||
                decoded.SignalBindings.Count != 2 ||
                decoded.SourceFingerprint != loaded.Plan.SourceFingerprint)
            {
                throw new InvalidOperationException("Provenance selection or counters changed during round-trip.");
            }

            var projection = FeeContainerVariableProjector.Apply(
                decoded,
                [
                    new(variableA, "HomeA_Renamed", "%I7.0", string.Empty, "Bool", "A-NEW"),
                    new(variableB, "HomeB", string.Empty, "GVL_IO.HomeB", "Bool", "B"),
                ],
                new Dictionary<Guid, string>
                {
                    [variableA] = "PLC_IN_InWorkPos",
                    [variableB] = "PLC_IN_InWorkPos"
                });
            if (projection.UpdatedEntries != 2 || projection.MissingVariableGuids.Count != 0 ||
                projection.UpdatedSlots != 2 || projection.UnresolvedSlotVariableGuids.Count != 0)
                throw new InvalidOperationException("Current FEE variable values were not projected completely.");

            FeeContainerProvenanceCodec.SaveAtomically(projection.Snapshot, exportedPath);
            var (containers, unknownSignals) = ContainerToFeeService.ReadInContainerXmlData(exportedPath);
            var cylinder = (GrobCylinder_Container)containers.Single();
            if (unknownSignals.Count != 0 ||
                cylinder.ComponentName != "FanInCylinder" ||
                cylinder.Signals_InWorkPos.Count != 2 ||
                cylinder.Signals_InWorkPos[0].Tag != "HomeA_Renamed" ||
                cylinder.Signals_InWorkPos[0].Address != "%I7.0" ||
                cylinder.Signals_InWorkPos[0].Comment != "A-NEW" ||
                cylinder.Signals_InWorkPos[1].Path != "GVL_IO.HomeB")
            {
                throw new InvalidOperationException(
                    "Container → provenance → Container lost the selected container or PLC_IN fan-in.");
            }

            var damagedTags = encoded.Tags.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            damagedTags[FeeContainerProvenanceCodec.HashKey] = new string('0', 64);
            if (FeeContainerProvenanceCodec.TryRead(damagedTags, out _, out _))
                throw new InvalidOperationException("Damaged provenance checksum was accepted.");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static void ValidateRuleSuggestionWorkflow()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"vibn-rule-suggestions-{Guid.NewGuid():N}");
        var reviewsPath = Path.Combine(directory, "reviews.json");
        try
        {
            var logger = new ActionLogger(directory);
            LogSlotCorrection(logger, "SIG-A", "Ready", "PLC_IN_Old", "PLC_IN_New", "source-1");
            LogSlotCorrection(logger, "SIG-B", "Ready", "PLC_IN_Old", "PLC_IN_New", "source-2");
            LogSlotCorrection(logger, "SIG-C", "Ready", "PLC_IN_Old", "PLC_IN_Alternative", "source-3");
            var other = new ContainerEntry
            {
                SignalId = "SIG-D",
                Signal = "Ready",
                Slot = "PLC_IN_New",
                Address = "%I0.0"
            };
            logger.LogPropertyChange(
                "Cylinder_1", "Cylinder", other, nameof(ContainerEntry.Address),
                "%I0.0", "%I0.7", "source-4");

            var analysis = new RuleSuggestionService().Analyze(
                Directory.GetFiles(directory, "*.jsonl"));
            var preferred = analysis.Suggestions.Single(suggestion => suggestion.NewValue == "PLC_IN_New");
            if (analysis.ParsedEvents != 4 || analysis.InvalidLines != 0 ||
                analysis.Suggestions.Count != 2 || preferred.Frequency != 2 ||
                preferred.RelevantCases != 3 || Math.Abs(preferred.Confidence - (2d / 3d)) > 0.0001)
            {
                throw new InvalidOperationException("Deterministic rule-suggestion confidence is incorrect.");
            }

            var store = new RuleSuggestionReviewStore(reviewsPath);
            store.Save(new Dictionary<string, RuleSuggestionStatus>
            {
                [preferred.Id] = RuleSuggestionStatus.Accepted
            });
            var reviewed = new RuleSuggestionService().Analyze(
                Directory.GetFiles(directory, "*.jsonl"), store.Load());
            if (reviewed.Suggestions.Single(item => item.Id == preferred.Id).Status !=
                RuleSuggestionStatus.Accepted)
            {
                throw new InvalidOperationException("Rule-suggestion review status was not persisted.");
            }

            var json = File.ReadAllText(Directory.GetFiles(directory, "*.jsonl").Single());
            if (!json.Contains("\"SchemaVersion\":2", StringComparison.Ordinal) ||
                !json.Contains("\"TimestampUtc\":", StringComparison.Ordinal) ||
                !json.Contains("\"SourceKey\":", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Structured action-log schema metadata is missing.");
            }

            var legacyPath = Path.Combine(directory, "legacy.log");
            File.WriteAllText(legacyPath, """
                {"SignalId":"LEGACY-1","SignalText":"LegacyReady","FromContainer":"C1","FromComponentType":"Cylinder","FromSlot":"PLC_IN_A","ToContainer":"C1","ComponentType":"Cylinder","ToSlot":"PLC_IN_B","RuleSuggestion":"","MlTop1":null,"MlTop1Score":0}
                """);
            var legacy = new RuleSuggestionService().Analyze([legacyPath]);
            if (legacy.ParsedEvents != 1 || legacy.Suggestions.Count != 1 ||
                legacy.Suggestions[0].PreviousValue != "PLC_IN_A" ||
                legacy.Suggestions[0].NewValue != "PLC_IN_B")
            {
                throw new InvalidOperationException("Schema-1 action logs are no longer readable.");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static void LogSlotCorrection(
        ActionLogger logger,
        string signalId,
        string signal,
        string oldSlot,
        string newSlot,
        string sourceKey)
    {
        var entry = new ContainerEntry
        {
            SignalId = signalId,
            Signal = signal,
            Slot = newSlot
        };
        logger.LogSlotChange(
            "Cylinder_1", "Cylinder", entry, oldSlot, null, null, sourceKey);
    }

    private static async Task ValidateRequirementsRulePatchWorkflowAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"vibn-requirements-patch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var requirementsPath = Path.Combine(directory, "AutoCreate.xml");
        try
        {
            File.WriteAllText(requirementsPath, """
                <AutoCreate>
                  <Components>
                    <Component name="Cylinder" type="Cylinder">
                      <Slots>
                        <Slot name="PLC_IN_Old">
                          <Keygroup type="required"><KeySet><Key keep="true">Ready</Key></KeySet></Keygroup>
                        </Slot>
                        <Slot name="PLC_IN_New">
                          <Keygroup type="required"><KeySet><Key keep="true">Target</Key></KeySet></Keygroup>
                        </Slot>
                      </Slots>
                    </Component>
                  </Components>
                  <FilterList />
                </AutoCreate>
                """);
            var suggestion = new RuleSuggestion(
                "0123456789abcdef",
                "Ready correction",
                "Cylinder",
                "Ready",
                "Slot",
                "PLC_IN_Old",
                "PLC_IN_New",
                3,
                3,
                1,
                RuleSuggestionStatus.Accepted);
            var service = new RequirementsRulePatchService();
            var plan = service.CreatePlan(requirementsPath, [suggestion]);
            if (!plan.UpdatedXml.Contains("match=\"exact\"", StringComparison.Ordinal) ||
                !plan.Preview.Contains("PLC_IN_Old' -> 'PLC_IN_New", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Requirements patch preview misses the exact override.");
            }

            var rejectedUnconfirmedWrite = false;
            try
            {
                service.Apply(plan, explicitlyConfirmed: false);
            }
            catch (InvalidOperationException)
            {
                rejectedUnconfirmedWrite = true;
            }
            if (!rejectedUnconfirmedWrite)
                throw new InvalidOperationException("An unconfirmed requirements patch was written.");

            var applyResult = service.Apply(plan, explicitlyConfirmed: true);
            if (!File.Exists(applyResult.BackupPath) || applyResult.AppliedRules != 1)
                throw new InvalidOperationException("Requirements patch backup was not created.");

            var requirements = new RequirementsXml();
            var read = requirements.ReadFromFile(requirementsPath);
            if (!read.IsSuccess)
                throw new InvalidOperationException("Patched requirements no longer validate.");
            var generator = new ContainerGenerator();
            var generated = await generator.GenerateAsync(new ContainerGenerationRequest(
                [new ContainerEntry { Signal = "Ready", SignalId = "exact-ready" }],
                read.Value,
                [new GroupingRule { TargetField = match => match.ContainerName, GroupOrder = 0 }],
                null,
                IgnoreCase: true,
                UseFilterList: true));
            var assigned = generated.Containers.SelectMany(container => container.DataList).Single();
            if (assigned.Slot != "PLC_IN_New" || generated.UnassignedSignals.Count != 0)
                throw new InvalidOperationException("Exact requirements override did not reroute the signal.");

            var revisedSuggestion = suggestion with
            {
                Id = "fedcba9876543210",
                NewValue = "PLC_IN_Alternative"
            };
            var revisedPlan = service.CreatePlan(requirementsPath, [revisedSuggestion]);
            service.Apply(revisedPlan, explicitlyConfirmed: true);
            var revisedDocument = XDocument.Load(requirementsPath);
            var generatedOverrides = revisedDocument.Descendants("Component")
                .Where(component => component.Attribute("name")?.Value
                    .StartsWith("VIBN AI exact override", StringComparison.Ordinal) == true)
                .ToArray();
            if (generatedOverrides.Length != 1 ||
                generatedOverrides[0].Descendants("Slot").Single().Attribute("name")?.Value !=
                    "PLC_IN_Alternative")
            {
                throw new InvalidOperationException("A revised exact override left a competing stale rule behind.");
            }

            var stalePlan = service.CreatePlan(requirementsPath, [revisedSuggestion]);
            File.AppendAllText(requirementsPath, Environment.NewLine);
            var rejectedStaleWrite = false;
            try
            {
                service.Apply(stalePlan, explicitlyConfirmed: true);
            }
            catch (InvalidOperationException)
            {
                rejectedStaleWrite = true;
            }
            if (!rejectedStaleWrite)
                throw new InvalidOperationException("A stale requirements preview overwrote a changed file.");
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
