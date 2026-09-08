using FS.SDK.Components;
using FS.SDK.Scene.Objects;
using VIBN_Tools.GlobalClasses;
using VIBN_Tools.Settings;

namespace VIBN_Tools.SpecialDevices;

public sealed record Fee2SpecialDeviceRoot(
    Guid Guid,
    string Name,
    FeeSpecialDeviceSnapshot Snapshot,
    int UpdatedSignalCount,
    int MissingSignalCount);

public sealed record Fee2SpecialDeviceDiscoveryIssue(Guid? Guid, string RootName, string Message);

public sealed record Fee2SpecialDeviceDiscoveryResult(
    IReadOnlyList<Fee2SpecialDeviceRoot> Roots,
    int IgnoredWithoutProvenance,
    IReadOnlyList<Fee2SpecialDeviceDiscoveryIssue> Issues);

/// <summary>
/// Reads only BasicFrames tagged after a successful SpecialDevices2FEE run.
/// Current variable values are projected by GUID; names are never guessed.
/// </summary>
public sealed class Fee2SpecialDevicesService
{
    public async Task<Fee2SpecialDeviceDiscoveryResult> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        if (Services.Connection?.CanUseFeeFeatures != true || Services.ApiInstance is null)
            throw new InvalidOperationException(FeeConnectionService.MissingConnectionMessage);

        var variables = (await Services.ApiInstance.Interface.GetAllVariablesAsync())
            .GroupBy(variable => variable.VariableGuid)
            .ToDictionary(group => group.Key, group => group.First());
        var roots = new List<Fee2SpecialDeviceRoot>();
        var issues = new List<Fee2SpecialDeviceDiscoveryIssue>();
        var ignored = 0;
        var guidValues = await Services.ApiInstance.Object
            .GetSceneObjectGuidsOfTypeAsync(nameof(BasicFrame));

        foreach (var guidText in guidValues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(guidText, out var guid))
            {
                issues.Add(new Fee2SpecialDeviceDiscoveryIssue(
                    null,
                    string.Empty,
                    $"FEE lieferte eine ungültige BasicFrame-ID: '{guidText}'."));
                continue;
            }

            var name = guidText;
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
                    ignored++;
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
                        DataType = variable.Type.ToString(),
                        Comment = variable.Comment ?? signal.Comment
                    };
                }).ToArray();
                var snapshot = source with { Signals = currentSignals };
                roots.Add(new Fee2SpecialDeviceRoot(guid, name, snapshot, updated, missing));
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
}
