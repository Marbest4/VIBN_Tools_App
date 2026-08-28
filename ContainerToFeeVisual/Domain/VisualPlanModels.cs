using System.Collections.ObjectModel;

namespace VIBN_Tools.ContainerToFeeVisual;

/// <summary>Types of objects shown in the declarative Container2FEE plan.</summary>
public enum VisualNodeKind
{
    Root,
    Container,
    BasicFrame,
    Interface,
    Logic,
    SimObjectTarget,
    SimObject,
    Signal,
    TechnicalHelper,
    UnknownSignal
}

/// <summary>Relationship types used by the visual plan.</summary>
public enum VisualEdgeKind
{
    ParentChild,
    SignalToSlot,
    SlotToSlot,
    SimObjectAssignment
}

public enum VisualIssueSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>A validation or runtime message which can be associated with one node.</summary>
public sealed record VisualIssue(
    VisualIssueSeverity Severity,
    string Code,
    string Message,
    string? NodeId = null);

/// <summary>One object, signal or technical helper in the generation plan.</summary>
public sealed class VisualNode
{
    private readonly List<VisualNode> _children = [];

    internal VisualNode(
        string id,
        string? parentId,
        string? containerId,
        VisualNodeKind kind,
        string name,
        string typeName,
        string? slot,
        bool isTechnical,
        bool supportsCreation = false)
    {
        Id = id;
        ParentId = parentId;
        ContainerId = containerId;
        Kind = kind;
        Name = name;
        TypeName = typeName;
        Slot = slot;
        IsTechnical = isTechnical;
        SupportsCreation = supportsCreation;
    }

    public string Id { get; }

    public string? ParentId { get; }

    public string? ContainerId { get; }

    public VisualNodeKind Kind { get; }

    public string Name { get; }

    public string TypeName { get; }

    public string? Slot { get; }

    /// <summary>
    /// Technical nodes may be hidden or collapsed by the UI without losing
    /// generation information.
    /// </summary>
    public bool IsTechnical { get; }

    /// <summary>
    /// Indicates that the unchanged legacy container can create its default
    /// simulation object when no suitable existing object is assigned.
    /// </summary>
    public bool SupportsCreation { get; }

    public IReadOnlyList<VisualNode> Children => _children;

    internal void AddChild(VisualNode node) => _children.Add(node);
}

/// <summary>A directed relationship between two stable plan node IDs.</summary>
public sealed record VisualEdge(
    string Id,
    string SourceId,
    string TargetId,
    VisualEdgeKind Kind,
    string Label);

/// <summary>
/// Lightweight FEE object identity. No SDK object is exposed to the WPF layer,
/// which keeps drag/drop and sidecar persistence deterministic.
/// </summary>
public sealed class VisualFeeObject
{
    internal VisualFeeObject(
        string id,
        string guidString,
        string name,
        string typeName,
        string feeType,
        IReadOnlyCollection<string> assignableTypeNames)
    {
        Id = id;
        GuidString = guidString;
        Name = name;
        TypeName = typeName;
        FeeType = feeType;
        AssignableTypeNames = assignableTypeNames;
    }

    public string Id { get; }

    public string GuidString { get; }

    public string Name { get; }

    public string TypeName { get; }

    public string FeeType { get; }

    /// <summary>CLR type names including all base classes.</summary>
    public IReadOnlyCollection<string> AssignableTypeNames { get; }
}

/// <summary>A typed drop target declared by the unchanged legacy container.</summary>
public sealed class VisualSimObjectTarget
{
    internal VisualSimObjectTarget(
        string id,
        string containerId,
        string displayName,
        string allowedTypeName,
        bool allowMultiSelect)
    {
        Id = id;
        ContainerId = containerId;
        DisplayName = displayName;
        AllowedTypeName = allowedTypeName;
        AllowMultiSelect = allowMultiSelect;
    }

    public string Id { get; }

    public string ContainerId { get; }

    public string DisplayName { get; }

    public string AllowedTypeName { get; }

    public bool AllowMultiSelect { get; }

    public bool CanAssign(VisualFeeObject feeObject) =>
        feeObject is not null &&
        feeObject.AssignableTypeNames.Contains(AllowedTypeName, StringComparer.Ordinal);
}

/// <summary>Persistent assignment from one plan target to an existing FEE object.</summary>
public sealed record VisualAssignment(
    string TargetId,
    string FeeObjectId,
    string FeeObjectName,
    string FeeObjectTypeName);

/// <summary>
/// Requests the unchanged legacy container to create its default simulation
/// object when no existing object is assigned.
/// </summary>
public sealed record VisualCreationRequest(string ContainerId, bool IsRequested);

/// <summary>
/// Complete immutable-facing plan. Mutations are restricted to the plan
/// service so every change remains validated and undoable.
/// </summary>
public sealed class VisualPlan
{
    private readonly List<VisualAssignment> _assignments;
    private readonly List<VisualCreationRequest> _creationRequests;
    private readonly List<VisualEdge> _edges;

    internal VisualPlan(
        string sourceXmlPath,
        string sidecarPath,
        string sourceFingerprint,
        IReadOnlyList<VisualNode> nodes,
        IReadOnlyList<VisualNode> roots,
        IReadOnlyList<VisualEdge> edges,
        IReadOnlyList<VisualSimObjectTarget> targets,
        IReadOnlyList<VisualAssignment>? assignments,
        IReadOnlyList<VisualCreationRequest>? creationRequests,
        IReadOnlyList<VisualIssue> issues)
    {
        SourceXmlPath = sourceXmlPath;
        SidecarPath = sidecarPath;
        SourceFingerprint = sourceFingerprint;
        Nodes = nodes;
        Roots = roots;
        _edges = [.. edges];
        Targets = targets;
        _assignments = assignments is null ? [] : [.. assignments];
        _creationRequests = creationRequests is null ? [] : [.. creationRequests];
        Issues = issues;
    }

    public string SourceXmlPath { get; }

    public string SidecarPath { get; internal set; }

    public string SourceFingerprint { get; }

    public IReadOnlyList<VisualNode> Nodes { get; }

    public IReadOnlyList<VisualNode> Roots { get; }

    public IReadOnlyList<VisualEdge> Edges => _edges;

    public IReadOnlyList<VisualSimObjectTarget> Targets { get; }

    public IReadOnlyList<VisualAssignment> Assignments => _assignments;

    public IReadOnlyList<VisualCreationRequest> CreationRequests => _creationRequests;

    public IReadOnlyList<VisualIssue> Issues { get; }

    public VisualNode? FindNode(string id) =>
        Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));

    public VisualSimObjectTarget? FindTarget(string id) =>
        Targets.FirstOrDefault(target => string.Equals(target.Id, id, StringComparison.Ordinal));

    public bool IsCreationRequested(string containerId) =>
        _creationRequests.Any(request =>
            request.IsRequested &&
            string.Equals(request.ContainerId, containerId, StringComparison.Ordinal));

    internal void ReplaceAssignments(IEnumerable<VisualAssignment> assignments)
    {
        _assignments.Clear();
        _assignments.AddRange(assignments);
        RebuildAssignmentEdges();
    }

    internal void ReplaceCreationRequests(IEnumerable<VisualCreationRequest> requests)
    {
        _creationRequests.Clear();
        _creationRequests.AddRange(requests.Where(request => request.IsRequested));
    }

    internal void RebuildAssignmentEdges()
    {
        _edges.RemoveAll(edge => edge.Kind == VisualEdgeKind.SimObjectAssignment);
        _edges.AddRange(_assignments.Select(assignment => new VisualEdge(
            $"edge:assignment:{StableId.Encode(assignment.TargetId)}:{StableId.Encode(assignment.FeeObjectId)}",
            assignment.TargetId,
            assignment.FeeObjectId,
            VisualEdgeKind.SimObjectAssignment,
            "FEE-Zuordnung")));
    }
}

public sealed record VisualPlanLoadResult(
    bool Success,
    VisualPlan? Plan,
    IReadOnlyList<VisualIssue> Issues,
    string Message);

public sealed record VisualAssignmentResult(
    bool Success,
    string Message,
    VisualAssignment? Assignment,
    IReadOnlyList<VisualIssue> Issues);

public sealed record VisualValidationResult(
    bool IsValid,
    IReadOnlyList<VisualIssue> Issues);

public sealed record VisualExecutionResult(
    bool Success,
    string Message,
    IReadOnlyList<VisualIssue> Issues);

public sealed class VisualPlanChangedEventArgs(VisualPlan plan) : EventArgs
{
    public VisualPlan Plan { get; } = plan;
}

internal static class StableId
{
    public static string Encode(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "empty";

        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16]
            .ToLowerInvariant();
    }
}
