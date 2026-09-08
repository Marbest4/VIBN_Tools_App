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
    int MissingSignalCount,
    int UpdatedSlotCount,
    int UnresolvedSlotCount)
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

                var slotResolutions = await ResolveSlotsAsync(
                    guid,
                    provenance!.SignalBindings.Select(binding => binding.VariableGuid),
                    cancellationToken);
                var projection = FeeContainerVariableProjector.Apply(
                    provenance,
                    currentVariables,
                    slotResolutions
                        .Where(result => result.Slot is not null)
                        .ToDictionary(result => result.VariableGuid, result => result.Slot!));
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
                    projection.MissingVariableGuids.Count,
                    projection.UpdatedSlots,
                    projection.UnresolvedSlotVariableGuids.Count));
                foreach (var resolution in slotResolutions.Where(result => result.Issue is not null))
                {
                    issues.Add(new Fee2ContainerDiscoveryIssue(
                        guid,
                        name,
                        resolution.Issue!));
                }
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

    private static async Task<IReadOnlyList<SlotResolution>> ResolveSlotsAsync(
        Guid rootGuid,
        IEnumerable<Guid> variableGuids,
        CancellationToken cancellationToken)
    {
        var scopedObjects = (await Services.ApiInstance!.Object
                .GetAllChildrenFromSceneObjectAsync(rootGuid.ToString()))
            .Select(value => Guid.TryParse(value, out var guid) ? guid : Guid.Empty)
            .Where(guid => guid != Guid.Empty)
            .Append(rootGuid)
            .ToHashSet();
        using var throttle = new SemaphoreSlim(6);
        var tasks = variableGuids.Distinct().Select(async variableGuid =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var assignments = await Services.ApiInstance.Interface
                    .GetAssignedSceneObjectsAsync(variableGuid);
                foreach (var (objectGuid, slots) in assignments.Where(item => scopedObjects.Contains(item.Item1)))
                {
                    foreach (var slot in slots ?? Array.Empty<string>())
                    {
                        if (slot.StartsWith("PLC_", StringComparison.OrdinalIgnoreCase))
                            candidates.Add(slot);
                        if (!string.Equals(slot, "Output 01", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var links = await Services.ApiInstance.Interface
                            .GetSlotSlotAssignmentAsync(objectGuid, "Input 01");
                        foreach (var (linkedGuidText, linkedSlots) in links)
                        {
                            if (!Guid.TryParse(linkedGuidText, out var linkedGuid) ||
                                !scopedObjects.Contains(linkedGuid))
                                continue;
                            foreach (var linkedSlot in linkedSlots ?? Array.Empty<string>())
                            {
                                if (linkedSlot.StartsWith("PLC_", StringComparison.OrdinalIgnoreCase))
                                    candidates.Add(linkedSlot);
                            }
                        }
                    }
                }

                return candidates.Count switch
                {
                    1 => new SlotResolution(variableGuid, candidates.Single(), null),
                    0 => new SlotResolution(
                        variableGuid,
                        null,
                        $"Für Variable {variableGuid:D} wurde innerhalb des Roots keine PLC-Slotroute gefunden."),
                    _ => new SlotResolution(
                        variableGuid,
                        null,
                        $"Variable {variableGuid:D} besitzt mehrere PLC-Slotrouten: {string.Join(", ", candidates.OrderBy(value => value))}.")
                };
            }
            catch (Exception exception)
            {
                return new SlotResolution(
                    variableGuid,
                    null,
                    $"Slotroute für Variable {variableGuid:D} konnte nicht gelesen werden: {exception.Message}");
            }
            finally
            {
                throttle.Release();
            }
        });
        return await Task.WhenAll(tasks);
    }

    private sealed record SlotResolution(Guid VariableGuid, string? Slot, string? Issue);
}
