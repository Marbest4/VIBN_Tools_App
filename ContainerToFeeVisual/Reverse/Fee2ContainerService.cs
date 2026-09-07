using FS.SDK.Components;
using FS.SDK.Scene.Objects;
using VIBN_Tools.GlobalClasses;
using VIBN_Tools.Settings;

namespace VIBN_Tools.ContainerToFeeVisual;

public sealed record Fee2ContainerRoot(
    Guid Guid,
    string Name,
    FeeContainerProvenanceSnapshot Provenance,
    int UpdatedSignalCount,
    int MissingSignalCount)
{
    public int ContainerCount => Provenance.ContainerCount;
    public int SignalCount => Provenance.SignalCount;
}

public sealed record Fee2ContainerDiscoveryIssue(Guid? Guid, string RootName, string Message);

public sealed record Fee2ContainerDiscoveryResult(
    IReadOnlyList<Fee2ContainerRoot> Roots,
    int IgnoredWithoutProvenance,
    IReadOnlyList<Fee2ContainerDiscoveryIssue> Issues);

/// <summary>
/// Reads only BasicFrames carrying the versioned Container2FEE tag payload.
/// Older or manually created FEE trees are intentionally not guessed.
/// </summary>
public sealed class Fee2ContainerService
{
    public async Task<Fee2ContainerDiscoveryResult> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        if (Services.Connection?.CanUseFeeFeatures != true || Services.ApiInstance is null)
            throw new InvalidOperationException(FeeConnectionService.MissingConnectionMessage);

        var roots = new List<Fee2ContainerRoot>();
        var issues = new List<Fee2ContainerDiscoveryIssue>();
        var ignored = 0;
        var guidValues = await Services.ApiInstance.Object
            .GetSceneObjectGuidsOfTypeAsync(nameof(BasicFrame));
        var currentVariables = (await Services.ApiInstance.Interface.GetAllVariablesAsync())
            .Select(variable => new FeeContainerVariableState(
                variable.VariableGuid,
                variable.Tag ?? string.Empty,
                variable.Address ?? string.Empty,
                variable.Path ?? string.Empty,
                variable.Type.ToString(),
                variable.Comment ?? string.Empty))
            .ToArray();

        foreach (var guidText in guidValues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(guidText, out var guid))
            {
                issues.Add(new Fee2ContainerDiscoveryIssue(
                    null,
                    string.Empty,
                    $"FEE lieferte eine ungültige BasicFrame-ID: '{guidText}'."));
                continue;
            }

            string name = guidText;
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
                if (!tags.ContainsKey(FeeContainerProvenanceCodec.SchemaKey))
                {
                    ignored++;
                    continue;
                }
                if (!FeeContainerProvenanceCodec.TryRead(tags, out var provenance, out var error))
                {
                    issues.Add(new Fee2ContainerDiscoveryIssue(guid, name, error));
                    continue;
                }

                var projection = FeeContainerVariableProjector.Apply(provenance!, currentVariables);
                if (projection.MissingVariableGuids.Count > 0)
                {
                    issues.Add(new Fee2ContainerDiscoveryIssue(
                        guid,
                        name,
                        $"{projection.MissingVariableGuids.Count} in der Provenienz referenzierte " +
                        "FEE-Variablen fehlen; für diese Einträge bleibt der Generierungsstand erhalten."));
                }
                roots.Add(new Fee2ContainerRoot(
                    guid,
                    name,
                    projection.Snapshot,
                    projection.UpdatedEntries,
                    projection.MissingVariableGuids.Count));
            }
            catch (Exception exception)
            {
                issues.Add(new Fee2ContainerDiscoveryIssue(
                    guid,
                    name,
                    $"Der FEE-Root konnte nicht gelesen werden: {exception.Message}"));
            }
        }

        return new Fee2ContainerDiscoveryResult(
            roots.OrderBy(root => root.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            ignored,
            issues);
    }
}
