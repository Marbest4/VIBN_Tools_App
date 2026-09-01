using System.IO;
using VIBN_Tools.GlobalClasses.FeeObjects;

namespace VIBN_Tools.ContainerToFeeVisual;

/// <summary>
/// Coordinates parsing, sidecar persistence, validated drag/drop changes,
/// undo/redo and execution through the unchanged legacy generator.
/// </summary>
public sealed class ContainerToFeeVisualPlanService
{
    private readonly IVisualPlanLogger _logger;
    private readonly ContainerXmlVisualPlanParser _parser;
    private readonly VisualPlanSidecarStore _sidecarStore;
    private readonly FeeSimObjectDiscovery _discovery;
    private readonly LegacyContainerToFeeExecutionAdapter _executor;
    private readonly ExistingSimObjectLinkAdapter _linkExecutor;
    private readonly Stack<PlanState> _undo = new();
    private readonly Stack<PlanState> _redo = new();
    private IReadOnlyList<VisualFeeObject> _feeObjects = [];
    private IReadOnlyDictionary<string, FeeAbstractObject> _runtimeObjects =
        new Dictionary<string, FeeAbstractObject>(StringComparer.Ordinal);
    private bool _hasDiscoveredFeeObjects;

    public ContainerToFeeVisualPlanService()
        : this(new VisualPlanLogger())
    {
    }

    internal ContainerToFeeVisualPlanService(IVisualPlanLogger logger)
    {
        _logger = logger;
        _parser = new ContainerXmlVisualPlanParser(logger);
        _sidecarStore = new VisualPlanSidecarStore(logger);
        _discovery = new FeeSimObjectDiscovery(logger);
        _executor = new LegacyContainerToFeeExecutionAdapter(logger);
        _linkExecutor = new ExistingSimObjectLinkAdapter(logger);
    }

    public event EventHandler<VisualPlanChangedEventArgs>? PlanChanged;

    public VisualPlan? CurrentPlan { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public IReadOnlyList<VisualFeeObject> DiscoveredFeeObjects => _feeObjects;

    public async Task<VisualPlanLoadResult> LoadXmlAsync(
        string xmlPath,
        CancellationToken cancellationToken = default)
    {
        var result = await _parser.ParseAsync(xmlPath, cancellationToken);
        if (result.Plan is null)
            return result;

        var plan = result.Plan;
        var additionalIssues = new List<VisualIssue>();
        if (File.Exists(plan.SidecarPath))
        {
            var sidecar = await _sidecarStore.ReadAsync(plan.SidecarPath, cancellationToken);
            if (!sidecar.Success || sidecar.Document is null)
            {
                additionalIssues.Add(new VisualIssue(
                    VisualIssueSeverity.Warning,
                    "SIDECAR_AUTOLOAD_FAILED",
                    $"Gespeicherte Änderungen wurden ignoriert: {sidecar.Message}"));
            }
            else if (!string.Equals(
                         sidecar.Document.SourceFingerprint,
                         plan.SourceFingerprint,
                         StringComparison.OrdinalIgnoreCase))
            {
                additionalIssues.Add(new VisualIssue(
                    VisualIssueSeverity.Warning,
                    "SIDECAR_SOURCE_CHANGED",
                    "Die Container-XML wurde seit dem Speichern des visuellen Plans geändert; " +
                    "alte Zuordnungen wurden nicht automatisch übernommen."));
            }
            else
            {
                var documentIssues = ValidateAndApplyDocument(plan, sidecar.Document);
                additionalIssues.AddRange(documentIssues);
            }
        }

        if (additionalIssues.Count > 0)
            plan = CloneWithIssues(plan, additionalIssues);

        SetPlan(plan);
        var allIssues = plan.Issues;
        var success = allIssues.All(issue => issue.Severity != VisualIssueSeverity.Error);
        return new VisualPlanLoadResult(
            success,
            plan,
            allIssues,
            success
                ? "Container-XML und visueller Plan wurden geladen."
                : "Container-XML enthält Fehler; die Generierung bleibt deaktiviert.");
    }

    public async Task<VisualPlanLoadResult> LoadSidecarAsync(
        string sidecarPath,
        CancellationToken cancellationToken = default)
    {
        var sidecar = await _sidecarStore.ReadAsync(sidecarPath, cancellationToken);
        if (!sidecar.Success || sidecar.Document is null)
            return Failure("SIDECAR_READ_FAILED", sidecar.Message);

        var parsed = await _parser.ParseAsync(sidecar.SourceXmlPath, cancellationToken);
        if (parsed.Plan is null)
            return parsed;
        if (parsed.Issues.Any(issue => issue.Severity == VisualIssueSeverity.Error))
            return parsed;

        var plan = parsed.Plan;
        if (!string.Equals(
                sidecar.Document.SourceFingerprint,
                plan.SourceFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                "SIDECAR_SOURCE_CHANGED",
                "Die referenzierte Container-XML wurde seit dem Speichern geändert. " +
                "Der Plan wurde nicht angewendet.");
        }

        var issues = ValidateAndApplyDocument(plan, sidecar.Document);
        if (issues.Any(issue => issue.Severity == VisualIssueSeverity.Error))
        {
            return new VisualPlanLoadResult(
                false,
                null,
                issues,
                "Der gespeicherte Plan enthält ungültige Zuordnungen.");
        }

        plan.SidecarPath = Path.GetFullPath(sidecarPath);
        if (issues.Count > 0)
            plan = CloneWithIssues(plan, issues);
        SetPlan(plan);
        return new VisualPlanLoadResult(true, plan, plan.Issues, "Gespeicherter visueller Plan wurde geladen.");
    }

    public async Task SaveSidecarAsync(
        string? sidecarPath = null,
        CancellationToken cancellationToken = default)
    {
        var plan = CurrentPlan ?? throw new InvalidOperationException("Es ist kein visueller Plan geladen.");
        var targetPath = string.IsNullOrWhiteSpace(sidecarPath)
            ? plan.SidecarPath
            : Path.GetFullPath(sidecarPath);
        await _sidecarStore.SaveAsync(plan, targetPath, cancellationToken);
        plan.SidecarPath = targetPath;
    }

    public async Task<IReadOnlyList<VisualFeeObject>> DiscoverFeeObjectsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _discovery.DiscoverAsync(cancellationToken);
        _feeObjects = result.Objects;
        _runtimeObjects = result.RuntimeObjects;
        _hasDiscoveredFeeObjects = true;
        return _feeObjects;
    }

    /// <summary>
    /// Reproduces the legacy name-and-type matching for targets which have not
    /// been edited manually. One FEE object remains assignable to only one target.
    /// </summary>
    public int AutoAssignMatches()
    {
        var plan = CurrentPlan;
        if (plan is null || _feeObjects.Count == 0)
            return 0;

        var assignments = plan.Assignments.ToList();
        var assignedObjectIds = assignments
            .Select(assignment => assignment.FeeObjectId)
            .ToHashSet(StringComparer.Ordinal);
        var added = 0;
        var before = Capture(plan);

        foreach (var target in plan.Targets)
        {
            if (assignments.Any(assignment => assignment.TargetId == target.Id))
                continue;

            var containerName = plan.FindNode(target.ContainerId)?.Name ?? string.Empty;
            var matches = _feeObjects
                .Where(target.CanAssign)
                .Where(item => !assignedObjectIds.Contains(item.Id))
                .Where(item => string.Equals(item.Name, containerName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
            if (!target.AllowMultiSelect)
                matches = matches.Take(1).ToArray();

            foreach (var match in matches)
            {
                assignments.Add(ToAssignment(target.Id, match));
                assignedObjectIds.Add(match.Id);
                added++;
            }
        }

        if (added == 0)
            return 0;

        RecordMutation(before);
        plan.ReplaceAssignments(assignments);
        RaisePlanChanged();
        _logger.Information($"{added} FEE-SimObject-Zuordnung(en) automatisch erkannt.");
        return added;
    }

    public VisualAssignmentResult TryAssign(string targetId, string feeObjectId)
    {
        var plan = CurrentPlan;
        if (plan is null)
            return AssignmentFailure("Es ist kein visueller Plan geladen.", "PLAN_NOT_LOADED");

        var target = plan.FindTarget(targetId);
        if (target is null)
            return AssignmentFailure("Das Zuordnungsziel existiert nicht mehr.", "TARGET_NOT_FOUND", targetId);
        var feeObject = _feeObjects.FirstOrDefault(item => item.Id == feeObjectId);
        if (feeObject is null)
            return AssignmentFailure("Das FEE-SimObject ist nicht mehr verfügbar.", "FEE_OBJECT_NOT_FOUND", targetId);
        if (!target.CanAssign(feeObject))
        {
            return AssignmentFailure(
                $"'{feeObject.Name}' ist nicht kompatibel mit '{target.DisplayName}'.",
                "FEE_OBJECT_INCOMPATIBLE",
                targetId);
        }

        var existing = plan.Assignments.FirstOrDefault(assignment =>
            assignment.TargetId == targetId && assignment.FeeObjectId == feeObjectId);
        if (existing is not null)
            return new VisualAssignmentResult(true, "Die Zuordnung besteht bereits.", existing, []);

        var before = Capture(plan);
        var assignments = plan.Assignments
            .Where(assignment => assignment.FeeObjectId != feeObjectId)
            .Where(assignment => target.AllowMultiSelect || assignment.TargetId != targetId)
            .ToList();
        var added = ToAssignment(targetId, feeObject);
        assignments.Add(added);

        RecordMutation(before);
        plan.ReplaceAssignments(assignments);
        RaisePlanChanged();
        return new VisualAssignmentResult(
            true,
            $"'{feeObject.Name}' wurde '{target.DisplayName}' zugeordnet.",
            added,
            []);
    }

    public VisualAssignmentResult RemoveAssignment(string targetId, string feeObjectId)
    {
        var plan = CurrentPlan;
        if (plan is null)
            return AssignmentFailure("Es ist kein visueller Plan geladen.", "PLAN_NOT_LOADED");

        var removed = plan.Assignments.FirstOrDefault(assignment =>
            assignment.TargetId == targetId && assignment.FeeObjectId == feeObjectId);
        if (removed is null)
            return AssignmentFailure("Die Zuordnung existiert nicht mehr.", "ASSIGNMENT_NOT_FOUND", targetId);

        var before = Capture(plan);
        plan.ReplaceAssignments(plan.Assignments.Where(assignment => assignment != removed));
        RecordMutation(before);
        RaisePlanChanged();
        return new VisualAssignmentResult(true, "Zuordnung wurde entfernt.", removed, []);
    }

    public bool SetCreationRequested(string containerId, bool requested)
    {
        var plan = CurrentPlan;
        var container = plan?.FindNode(containerId);
        if (plan is null || container is null || !container.SupportsCreation)
            return false;
        if (plan.IsCreationRequested(containerId) == requested)
            return true;

        var before = Capture(plan);
        var requests = plan.CreationRequests
            .Where(item => item.ContainerId != containerId)
            .ToList();
        if (requested)
            requests.Add(new VisualCreationRequest(containerId, true));
        plan.ReplaceCreationRequests(requests);
        RecordMutation(before);
        RaisePlanChanged();
        return true;
    }

    /// <summary>Includes or excludes one complete legacy container generation unit.</summary>
    public bool SetGenerationSelected(string containerId, bool selected)
    {
        var plan = CurrentPlan;
        var container = plan?.FindNode(containerId);
        if (plan is null || container?.Kind != VisualNodeKind.Container ||
            !ContainerMetadataCatalog.TryGet(container.TypeName, out _))
            return false;
        if (plan.IsGenerationSelected(containerId) == selected)
            return true;

        var before = Capture(plan);
        var selections = plan.GenerationSelections
            .Where(item => item.ContainerId != containerId)
            .ToList();
        if (!selected)
            selections.Add(new VisualGenerationSelection(containerId, false));
        plan.ReplaceGenerationSelections(selections);
        RecordMutation(before);
        RaisePlanChanged();
        return true;
    }

    /// <summary>Selects or deselects all supported legacy container units in one undo step.</summary>
    public int SetAllGenerationSelected(bool selected)
    {
        var plan = CurrentPlan;
        if (plan is null)
            return 0;

        var containers = plan.Nodes
            .Where(node => node.Kind == VisualNodeKind.Container &&
                           ContainerMetadataCatalog.TryGet(node.TypeName, out _))
            .ToArray();
        var changed = containers.Count(node => plan.IsGenerationSelected(node.Id) != selected);
        if (changed == 0)
            return 0;

        var before = Capture(plan);
        plan.ReplaceGenerationSelections(selected
            ? []
            : containers.Select(node => new VisualGenerationSelection(node.Id, false)));
        RecordMutation(before);
        RaisePlanChanged();
        return changed;
    }

    public VisualValidationResult Validate()
    {
        var plan = CurrentPlan;
        if (plan is null)
        {
            var issue = new VisualIssue(
                VisualIssueSeverity.Error,
                "PLAN_NOT_LOADED",
                "Es ist kein visueller Plan geladen.");
            return new VisualValidationResult(false, [issue]);
        }

        var issues = plan.Issues.ToList();
        foreach (var group in plan.Assignments.GroupBy(assignment => assignment.FeeObjectId))
        {
            if (group.Count() > 1)
            {
                issues.Add(new VisualIssue(
                    VisualIssueSeverity.Error,
                    "FEE_OBJECT_ASSIGNED_MULTIPLE_TIMES",
                    $"FEE-Objekt '{group.First().FeeObjectName}' wurde mehrfach zugeordnet."));
            }
        }

        foreach (var target in plan.Targets)
        {
            var targetAssignments = plan.Assignments
                .Where(assignment => assignment.TargetId == target.Id)
                .ToArray();
            if (targetAssignments.Length == 0 &&
                plan.IsGenerationSelected(target.ContainerId) &&
                !plan.IsCreationRequested(target.ContainerId))
            {
                issues.Add(new VisualIssue(
                    VisualIssueSeverity.Error,
                    "SIM_OBJECT_TARGET_UNASSIGNED",
                    $"Für '{target.DisplayName}' fehlt ein verfügbares FEE-SimObject. " +
                    "Ein Objekt zuordnen, die Erzeugung aktivieren oder den Container abwählen.",
                    target.Id));
            }
            if (!target.AllowMultiSelect && targetAssignments.Length > 1)
            {
                issues.Add(new VisualIssue(
                    VisualIssueSeverity.Error,
                    "SINGLE_TARGET_HAS_MULTIPLE_OBJECTS",
                    $"Ziel '{target.DisplayName}' erlaubt nur ein Objekt.",
                    target.Id));
            }

            foreach (var assignment in targetAssignments)
            {
                var discovered = _feeObjects.FirstOrDefault(item => item.Id == assignment.FeeObjectId);
                if (_hasDiscoveredFeeObjects && discovered is null)
                {
                    issues.Add(new VisualIssue(
                        VisualIssueSeverity.Error,
                        "ASSIGNED_FEE_OBJECT_MISSING",
                        $"FEE-Objekt '{assignment.FeeObjectName}' ist nicht mehr vorhanden.",
                        target.Id));
                }
                else if (discovered is not null && !target.CanAssign(discovered))
                {
                    issues.Add(new VisualIssue(
                        VisualIssueSeverity.Error,
                        "ASSIGNED_FEE_OBJECT_INCOMPATIBLE",
                        $"FEE-Objekt '{assignment.FeeObjectName}' ist nicht kompatibel.",
                        target.Id));
                }
            }
        }

        if (!plan.Nodes.Any(node =>
                node.Kind == VisualNodeKind.Container &&
                ContainerMetadataCatalog.TryGet(node.TypeName, out _) &&
                plan.IsGenerationSelected(node.Id)))
        {
            issues.Add(new VisualIssue(
                VisualIssueSeverity.Warning,
                "NO_CONTAINERS_SELECTED",
                "Es ist kein unterstützter Container zur Generierung ausgewählt."));
        }

        foreach (var assignment in plan.Assignments.Where(assignment => plan.FindTarget(assignment.TargetId) is null))
        {
            issues.Add(new VisualIssue(
                VisualIssueSeverity.Error,
                "ASSIGNMENT_TARGET_MISSING",
                $"Das Ziel für '{assignment.FeeObjectName}' existiert nicht mehr.",
                assignment.TargetId));
        }

        var distinct = issues
            .DistinctBy(issue => (issue.Severity, issue.Code, issue.Message, issue.NodeId))
            .ToArray();
        return new VisualValidationResult(
            distinct.All(issue => issue.Severity != VisualIssueSeverity.Error),
            distinct);
    }

    public async Task<VisualExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var plan = CurrentPlan;
        if (plan is null)
            return new VisualExecutionResult(false, "Es ist kein visueller Plan geladen.", Validate().Issues);

        if (_runtimeObjects.Count == 0)
        {
            await DiscoverFeeObjectsAsync(cancellationToken);
            AutoAssignMatches();
        }

        var validation = Validate();
        if (!validation.IsValid)
        {
            return new VisualExecutionResult(
                false,
                "Der Plan enthält Fehler und wurde nicht ausgeführt.",
                validation.Issues);
        }

        return await _executor.ExecuteAsync(plan, _runtimeObjects, cancellationToken);
    }

    /// <summary>
    /// Reuses existing FEE logic objects and writes only the configured
    /// SimObject-to-logic slot assignments. No container, signal or interface
    /// is created in this mode.
    /// </summary>
    public async Task<VisualExecutionResult> LinkExistingAssignmentsOnlyAsync(
        CancellationToken cancellationToken = default)
    {
        var plan = CurrentPlan;
        if (plan is null)
            return new VisualExecutionResult(false, "Es ist kein visueller Plan geladen.", Validate().Issues);

        if (_runtimeObjects.Count == 0)
        {
            await DiscoverFeeObjectsAsync(cancellationToken);
            AutoAssignMatches();
        }

        var validation = Validate();
        if (!validation.IsValid)
            return new VisualExecutionResult(false, "Der Plan enthält Fehler und wurde nicht verknüpft.", validation.Issues);

        return await _linkExecutor.ExecuteAsync(plan, _runtimeObjects, cancellationToken);
    }

    public bool Undo()
    {
        var plan = CurrentPlan;
        if (plan is null || _undo.Count == 0)
            return false;

        _redo.Push(Capture(plan));
        Restore(plan, _undo.Pop());
        RaisePlanChanged();
        return true;
    }

    public bool Redo()
    {
        var plan = CurrentPlan;
        if (plan is null || _redo.Count == 0)
            return false;

        _undo.Push(Capture(plan));
        Restore(plan, _redo.Pop());
        RaisePlanChanged();
        return true;
    }

    private void SetPlan(VisualPlan plan)
    {
        CurrentPlan = plan;
        _undo.Clear();
        _redo.Clear();
        _feeObjects = [];
        _runtimeObjects = new Dictionary<string, FeeAbstractObject>(StringComparer.Ordinal);
        _hasDiscoveredFeeObjects = false;
        RaisePlanChanged();
    }

    private IReadOnlyList<VisualIssue> ValidateAndApplyDocument(
        VisualPlan plan,
        VisualPlanSidecarDocument document)
    {
        var issues = new List<VisualIssue>();
        var assignments = document.Assignments ?? [];
        var requests = document.CreationRequests ?? [];
        var generationSelections = document.GenerationSelections ?? [];

        foreach (var assignment in assignments)
        {
            var target = plan.FindTarget(assignment.TargetId);
            if (target is null)
            {
                issues.Add(new VisualIssue(
                    VisualIssueSeverity.Error,
                    "SIDECAR_TARGET_MISSING",
                    $"Gespeichertes Ziel für '{assignment.FeeObjectName}' existiert nicht mehr.",
                    assignment.TargetId));
            }
        }
        foreach (var duplicate in assignments.GroupBy(item => item.FeeObjectId).Where(group => group.Count() > 1))
        {
            issues.Add(new VisualIssue(
                VisualIssueSeverity.Error,
                "SIDECAR_DUPLICATE_FEE_OBJECT",
                $"FEE-Objekt '{duplicate.First().FeeObjectName}' ist im gespeicherten Plan mehrfach zugeordnet."));
        }
        foreach (var targetGroup in assignments.GroupBy(item => item.TargetId))
        {
            var target = plan.FindTarget(targetGroup.Key);
            if (target is not null && !target.AllowMultiSelect && targetGroup.Count() > 1)
            {
                issues.Add(new VisualIssue(
                    VisualIssueSeverity.Error,
                    "SIDECAR_SINGLE_TARGET_MULTIPLE",
                    $"Ziel '{target.DisplayName}' enthält mehrere gespeicherte Objekte.",
                    target.Id));
            }
        }
        foreach (var request in requests.Where(item => item.IsRequested))
        {
            var node = plan.FindNode(request.ContainerId);
            if (node is null || !node.SupportsCreation)
            {
                issues.Add(new VisualIssue(
                    VisualIssueSeverity.Warning,
                    "SIDECAR_CREATION_UNSUPPORTED",
                    "Eine nicht mehr unterstützte Erzeugungsanforderung wurde ignoriert.",
                    request.ContainerId));
            }
        }
        foreach (var selection in generationSelections)
        {
            var node = plan.FindNode(selection.ContainerId);
            if (node?.Kind != VisualNodeKind.Container ||
                !ContainerMetadataCatalog.TryGet(node.TypeName, out _))
            {
                issues.Add(new VisualIssue(
                    VisualIssueSeverity.Warning,
                    "SIDECAR_GENERATION_TARGET_MISSING",
                    "Eine nicht mehr vorhandene Containerauswahl wurde ignoriert.",
                    selection.ContainerId));
            }
        }

        if (issues.Any(issue => issue.Severity == VisualIssueSeverity.Error))
            return issues;

        plan.ReplaceAssignments(assignments);
        plan.ReplaceCreationRequests(requests.Where(request =>
            request.IsRequested && plan.FindNode(request.ContainerId)?.SupportsCreation == true));
        plan.ReplaceGenerationSelections(generationSelections.Where(selection =>
            !selection.IsSelected &&
            plan.FindNode(selection.ContainerId)?.Kind == VisualNodeKind.Container));
        return issues;
    }

    private static VisualPlan CloneWithIssues(VisualPlan plan, IEnumerable<VisualIssue> additionalIssues)
    {
        var clone = new VisualPlan(
            plan.SourceXmlPath,
            plan.SidecarPath,
            plan.SourceFingerprint,
            plan.Nodes,
            plan.Roots,
            plan.Edges,
            plan.Targets,
            plan.Assignments,
            plan.CreationRequests,
            plan.GenerationSelections,
            plan.Issues.Concat(additionalIssues)
                .DistinctBy(issue => (issue.Severity, issue.Code, issue.Message, issue.NodeId))
                .ToArray());
        return clone;
    }

    private void RecordMutation(PlanState state)
    {
        _undo.Push(state);
        _redo.Clear();
    }

    private static PlanState Capture(VisualPlan plan) =>
        new([.. plan.Assignments], [.. plan.CreationRequests], [.. plan.GenerationSelections]);

    private static void Restore(VisualPlan plan, PlanState state)
    {
        plan.ReplaceAssignments(state.Assignments);
        plan.ReplaceCreationRequests(state.CreationRequests);
        plan.ReplaceGenerationSelections(state.GenerationSelections);
    }

    private void RaisePlanChanged()
    {
        if (CurrentPlan is not null)
            PlanChanged?.Invoke(this, new VisualPlanChangedEventArgs(CurrentPlan));
    }

    private static VisualAssignment ToAssignment(string targetId, VisualFeeObject feeObject) =>
        new(targetId, feeObject.Id, feeObject.Name, feeObject.TypeName);

    private static VisualAssignmentResult AssignmentFailure(
        string message,
        string code,
        string? nodeId = null) =>
        new(false, message, null, [new VisualIssue(VisualIssueSeverity.Error, code, message, nodeId)]);

    private static VisualPlanLoadResult Failure(string code, string message)
    {
        var issue = new VisualIssue(VisualIssueSeverity.Error, code, message);
        return new VisualPlanLoadResult(false, null, [issue], message);
    }

    private sealed record PlanState(
        IReadOnlyList<VisualAssignment> Assignments,
        IReadOnlyList<VisualCreationRequest> CreationRequests,
        IReadOnlyList<VisualGenerationSelection> GenerationSelections);
}
