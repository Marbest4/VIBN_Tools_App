using FS.SDK.Components;
using FS.SDK.Scene.Objects;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using VIBN_Tools.ContainerToFeeVisual;
using VIBN_Tools.GlobalClasses;
using VIBN_Tools.GlobalClasses.FeeObjects;
using VIBN_Tools.Settings;
using static VIBN_Tools.GlobalClasses.Services;
using static VIBN_Tools.SpecialDevices.DeviceCatalog;

namespace VIBN_Tools.SpecialDevices;

public sealed record Fee2SpecialDeviceRoot(
    Guid Guid,
    string Name,
    FeeSpecialDeviceSnapshot Snapshot,
    int UpdatedSignalCount,
    int MissingSignalCount,
    bool HasProvenance = true)
{
    public string SourceKind => HasProvenance
        ? "SpecialDevices2FEE-Provenienz"
        : "FEE-Struktur (Rekonstruktion)";
}

public sealed record Fee2SpecialDeviceDiscoveryIssue(Guid? Guid, string RootName, string Message);

public sealed record Fee2SpecialDeviceDiscoveryResult(
    IReadOnlyList<Fee2SpecialDeviceRoot> Roots,
    int IgnoredWithoutProvenance,
    IReadOnlyList<Fee2SpecialDeviceDiscoveryIssue> Issues);

/// <summary>
/// Reads top-level BasicFrames. Tagged roots use the exact reverse snapshot;
/// older roots are reconstructed only when a known device logic definition is
/// identified unambiguously.
/// </summary>
public sealed class Fee2SpecialDevicesService
{
    public async Task<Fee2SpecialDeviceDiscoveryResult> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        if (Services.Connection?.CanUseFeeFeatures != true || Services.ApiInstance is null)
            throw new InvalidOperationException(FeeConnectionService.MissingConnectionMessage);

        var variables = (await Services.ApiInstance.Interface.GetAllVariablesAsync())
            .Select(variable => new FeeInterfaceSignal
            {
                Guid = variable.VariableGuid,
                Tag = variable.Tag,
                Address = variable.Address,
                Path = variable.Path,
                IOType = variable.Type,
                Comment = variable.Comment,
                Usage = variable.Usage,
                References = variable.References
            })
            .GroupBy(variable => variable.Guid)
            .ToDictionary(group => group.Key, group => group.First());
        var roots = new List<Fee2SpecialDeviceRoot>();
        var issues = new List<Fee2SpecialDeviceDiscoveryIssue>();
        var ignored = 0;
        var guidValues = await Services.ApiInstance.Object
            .GetSceneObjectGuidsOfTypeAsync(nameof(BasicFrame));
        var topLevel = await FeeTopLevelBasicFrameDiscovery.DiscoverAsync(guidValues, cancellationToken);
        issues.AddRange(topLevel.Issues.Select(message =>
            new Fee2SpecialDeviceDiscoveryIssue(null, string.Empty, message)));

        foreach (var guid in topLevel.Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = guid.ToString("D");
            try
            {
                var nameXml = await Services.ApiInstance.Object.GetPropertyAsync(
                    guid,
                    nameof(FS.SDK.SceneObject.Name));
                name = Services.ApiInstance.XmlHelper.ConvertToString(nameXml);
                var tagsXml = await Services.ApiInstance.Object.GetPropertyAsync(
                    guid,
                    nameof(TagComponent.TagEntries),
                    nameof(TagComponent));
                var tags = Services.ApiInstance.XmlHelper.ConvertToDictionaryStringString(tagsXml);
                if (!tags.ContainsKey(FeeSpecialDeviceProvenanceCodec.SchemaKey))
                {
                    var reconstructed = await TryReconstructAsync(
                        guid,
                        name,
                        variables.Values.ToArray(),
                        cancellationToken);
                    if (reconstructed.Root is null)
                    {
                        ignored++;
                        if (!string.IsNullOrWhiteSpace(reconstructed.Issue))
                            issues.Add(new Fee2SpecialDeviceDiscoveryIssue(guid, name, reconstructed.Issue));
                    }
                    else
                    {
                        roots.Add(reconstructed.Root);
                        if (!string.IsNullOrWhiteSpace(reconstructed.Issue))
                            issues.Add(new Fee2SpecialDeviceDiscoveryIssue(guid, name, reconstructed.Issue));
                    }
                    continue;
                }
                if (!FeeSpecialDeviceProvenanceCodec.TryRead(tags, out var source, out var error))
                {
                    issues.Add(new Fee2SpecialDeviceDiscoveryIssue(guid, name, error));
                    continue;
                }

                var updated = 0;
                var missing = 0;
                var currentSignals = source!.Signals.Select(signal =>
                {
                    if (!variables.TryGetValue(signal.VariableGuid, out var variable))
                    {
                        missing++;
                        return signal;
                    }
                    updated++;
                    return signal with
                    {
                        Tag = variable.Tag ?? signal.Tag,
                        Address = variable.Address ?? signal.Address,
                        DataType = variable.IOType.ToString(),
                        Comment = variable.Comment ?? signal.Comment
                    };
                }).ToArray();
                var snapshot = source with { Signals = currentSignals };
                roots.Add(new Fee2SpecialDeviceRoot(guid, name, snapshot, updated, missing, true));
                if (missing > 0)
                {
                    issues.Add(new Fee2SpecialDeviceDiscoveryIssue(
                        guid,
                        name,
                        $"{missing} referenzierte FEE-Variable(n) fehlen; der gespeicherte Generierungsstand bleibt erhalten."));
                }
            }
            catch (Exception exception)
            {
                issues.Add(new Fee2SpecialDeviceDiscoveryIssue(
                    guid,
                    name,
                    $"Der FEE-Root konnte nicht gelesen werden: {exception.Message}"));
            }
        }

        return new Fee2SpecialDeviceDiscoveryResult(
            roots.OrderBy(root => root.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            ignored,
            issues);
    }

    private static async Task<(Fee2SpecialDeviceRoot? Root, string? Issue)> TryReconstructAsync(
        Guid rootGuid,
        string rootName,
        IReadOnlyList<FeeInterfaceSignal> variables,
        CancellationToken cancellationToken)
    {
        var childGuids = (await ApiInstance!.Object
                .GetAllChildrenFromSceneObjectAsync(rootGuid.ToString("D")))
            .Where(value => Guid.TryParse(value, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (childGuids.Length == 0)
            return (null, null);

        var xmlTexts = (await ApiInstance.Object.GetSceneObjectsAsXmlAsync(childGuids)).ToArray();
        var logicDefinitions = await ApiInstance.Logic.GetAllAvailableLogicDefinitionsAsync();
        var logicNames = logicDefinitions
            .Where(item => Guid.TryParse(item.Guid, out _))
            .GroupBy(item => Guid.Parse(item.Guid))
            .ToDictionary(group => group.Key, group => group.First().Name ?? string.Empty);
        var knownDevices = BuildKnownDeviceDefinitions();
        var matches = new List<(Guid LogicGuid, string ObjectName, KnownDeviceDefinition Device)>();
        for (var index = 0; index < Math.Min(childGuids.Length, xmlTexts.Length); index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var xml = XElement.Parse(xmlTexts[index]);
            var persistedLogic = xml.Element("Logic")?.Element("PersistedLogicGuid")?.Value;
            if (!Guid.TryParse(persistedLogic, out var definitionGuid) ||
                !logicNames.TryGetValue(definitionGuid, out var definitionName))
                continue;
            var deviceMatches = knownDevices
                .Where(device => string.Equals(device.LogicDefinitionName, definitionName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (deviceMatches.Length == 1)
            {
                matches.Add((
                    Guid.Parse(childGuids[index]),
                    xml.Attribute("Name")?.Value ?? string.Empty,
                    deviceMatches[0]));
            }
        }

        if (matches.Count == 0)
            return (null, null);
        if (matches.Count > 1)
            return (null, $"Der Root enthält {matches.Count} bekannte Special-Device-Logiken; eine eindeutige Gerätezuordnung ist nicht möglich.");

        var match = matches[0];
        var assignedVariables = await ReadAssignedVariablesAsync(match.LogicGuid, variables, cancellationToken);
        var prefix = ExtractPrefix(match.ObjectName, rootName);
        var robotType = InferRobotType(assignedVariables);
        if (match.Device.RequiresRobotType && robotType is null)
        {
            return (null,
                "Die Geräteart wurde erkannt, der Robotertyp (ABB/Fanuc/Kuka) ist aus den aktuellen Signaladressen jedoch nicht eindeutig ableitbar.");
        }

        var signals = assignedVariables.Select(variable => new FeeSpecialDeviceSignalSnapshot(
            variable.Guid,
            variable.Tag ?? string.Empty,
            variable.Address ?? string.Empty,
            variable.Usage.ToString(),
            variable.IOType.ToString(),
            variable.Comment ?? string.Empty)).ToArray();
        var input = InferBaseAddress(assignedVariables, "Write");
        var output = InferBaseAddress(assignedVariables, "Read");
        var snapshot = new FeeSpecialDeviceSnapshot(
            FeeSpecialDeviceProvenanceCodec.CurrentSchema,
            prefix,
            match.Device.Manufacturer.ToString(),
            match.Device.DeviceType.ToString(),
            robotType?.ToString(),
            input,
            output,
            signals);
        var issue = signals.Length == 0
            ? "Die Geräteart wurde erkannt, aber es wurden keine Variablenzuweisungen an der Gerätelogik gefunden."
            : "Das Gerät wurde ohne Provenienz aus Logikdefinition und Variablenzuweisungen rekonstruiert; bitte vor Wiederverwendung fachlich vergleichen.";
        return (new Fee2SpecialDeviceRoot(rootGuid, rootName, snapshot, signals.Length, 0, false), issue);
    }

    private static IReadOnlyList<KnownDeviceDefinition> BuildKnownDeviceDefinitions()
    {
        var definitions = new List<KnownDeviceDefinition>();
        foreach (var item in DeviceFactory.DeviceFactoryMap)
        {
            var requiresRobot = DeviceMetadata.MetadataMap.TryGetValue(item.Key, out var metadata) &&
                                metadata.RequiresRobotType;
            var probe = item.Value("__probe__", new SpecialDeviceAddresses(0, 0), RobotType.Kuka);
            definitions.Add(new KnownDeviceDefinition(
                item.Key.Item1,
                item.Key.Item2,
                probe.DeviceLogicObject.LogicDefinitionName,
                requiresRobot));
        }
        return definitions;
    }

    private static async Task<IReadOnlyList<FeeInterfaceSignal>> ReadAssignedVariablesAsync(
        Guid logicGuid,
        IReadOnlyList<FeeInterfaceSignal> variables,
        CancellationToken cancellationToken)
    {
        using var throttle = new SemaphoreSlim(6);
        var tasks = variables.Where(variable => variable.References > 0).Select(async variable =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var assignments = await ApiInstance!.Interface.GetAssignedSceneObjectsAsync(variable.Guid);
                return assignments.Any(assignment => assignment.Item1 == logicGuid) ? variable : null;
            }
            finally
            {
                throttle.Release();
            }
        });
        return (await Task.WhenAll(tasks)).OfType<FeeInterfaceSignal>().ToArray();
    }

    private static string ExtractPrefix(string objectName, string rootName)
    {
        var source = string.IsNullOrWhiteSpace(objectName) ? rootName : objectName;
        var marker = source.LastIndexOf(" (", StringComparison.Ordinal);
        var prefix = marker > 0 ? source[..marker] : source;
        return string.IsNullOrWhiteSpace(prefix) ? "FEE_SpecialDevice" : prefix.Trim();
    }

    private static RobotType? InferRobotType(IEnumerable<FeeInterfaceSignal> variables)
    {
        var addresses = variables.Select(variable => (string?)variable.Address ?? string.Empty).ToArray();
        if (addresses.Any(address => address.StartsWith("$IN[", StringComparison.OrdinalIgnoreCase) ||
                                     address.StartsWith("$OUT[", StringComparison.OrdinalIgnoreCase)))
            return RobotType.Kuka;
        if (addresses.Any(address => address.StartsWith("DIN[", StringComparison.OrdinalIgnoreCase) ||
                                     address.StartsWith("DOUT[", StringComparison.OrdinalIgnoreCase)))
            return RobotType.Fanuc;
        if (addresses.Length > 0 && addresses.All(address =>
                double.TryParse(address, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _)))
            return RobotType.ABB;
        return null;
    }

    private static int InferBaseAddress(IEnumerable<FeeInterfaceSignal> variables, string usage)
    {
        var values = variables
            .Where(variable => string.Equals(variable.Usage.ToString(), usage, StringComparison.OrdinalIgnoreCase))
            .Select(variable => ParseAddress((string?)variable.Address))
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
        return values.Length == 0 ? 0 : (int)Math.Floor(values.Min());
    }

    private static double? ParseAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;
        var match = Regex.Match(address, @"(?:\$?(?:IN|OUT)|D(?:IN|OUT)|[AE](?:[BWD])?)?\[?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        return match.Success && double.TryParse(match.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private sealed record KnownDeviceDefinition(
        DeviceManufacturer Manufacturer,
        Enum DeviceType,
        string LogicDefinitionName,
        bool RequiresRobotType);
}
