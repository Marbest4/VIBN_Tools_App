using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using VIBN_Tools.Application.Behaviors;
using VIBN_Tools.ContainerToFeeVisual;
using VIBN_Tools.GlobalClasses;
using VIBN_Tools.Settings;

namespace VIBN_Tools.Application.VM;

/// <summary>
/// Presentation model for the optional visual Container2FEE workflow.  The
/// existing ContainerToFeePageVM is intentionally not used or modified.  All
/// mutations go through <see cref="ContainerToFeeVisualPlanService"/>, which
/// keeps assignments, sidecars and undo/redo consistent with the unchanged
/// legacy generation executor.
/// </summary>
public sealed class ContainerToFeeVisualPageVM : MvvmBase
{
    private const string LogArea = "Container2FEE Visual";
    private readonly ContainerToFeeVisualPlanService _planService;
    private readonly ApplicationLogService _log;
    private readonly FeeConnectionService _connection;
    private CancellationTokenSource? _operationCancellation;
    private ContainerToFeeVisualTreeNodeVM? _selectedTreeNode;
    private ContainerToFeeVisualTargetVM? _selectedTarget;
    private ContainerToFeeVisualFeeInterfaceVM? _selectedExistingInterface;
    private string _treeFilter = string.Empty;
    private string _feeObjectFilter = string.Empty;
    private bool _showOnlyCompatibleFeeObjects;
    private bool _isBusy;
    private string _statusText = "Container-XML öffnen, um eine Vorschau zu erstellen.";
    private string _sourceXmlPath = string.Empty;
    private bool _isApplyingPlan;

    public ContainerToFeeVisualPageVM()
        : this(new ContainerToFeeVisualPlanService(), ResolveConnection(), ApplicationLogService.Instance)
    {
    }

    /// <summary>Creates the page around an already loaded plan, e.g. for host integration and UI tests.</summary>
    public ContainerToFeeVisualPageVM(ContainerToFeeVisualPlanService planService)
        : this(planService, ResolveConnection(), ApplicationLogService.Instance)
    {
    }

    internal ContainerToFeeVisualPageVM(
        ContainerToFeeVisualPlanService planService,
        FeeConnectionService connection,
        ApplicationLogService log)
    {
        _planService = planService;
        _connection = connection;
        _log = log;

        FeeObjectsView = CollectionViewSource.GetDefaultView(AvailableFeeObjects);
        FeeObjectsView.Filter = FilterFeeObject;
        FeeObjectsView.SortDescriptions.Add(
            new SortDescription(nameof(ContainerToFeeVisualFeeObjectVM.Name), ListSortDirection.Ascending));

        OpenXmlCommand = new VisualAsyncCommand(OpenXmlAsync, () => !IsBusy);
        LoadPlanCommand = new VisualAsyncCommand(LoadPlanAsync, () => !IsBusy);
        SavePlanCommand = new VisualAsyncCommand(SavePlanAsync, () => HasPlan && !IsBusy);
        RefreshFeeObjectsCommand = new VisualAsyncCommand(
            RefreshFeeObjectsAsync,
            () => HasPlan && Connection.CanUseFeeFeatures && !IsBusy);
        AutoAssignCommand = new VisualRelayCommand(
            AutoAssignMatches,
            () => HasPlan && AvailableFeeObjects.Count > 0 && !IsBusy);
        StartGenerationCommand = new VisualAsyncCommand(
            StartGenerationAsync,
            () => HasPlan && Connection.CanUseFeeFeatures && !IsBusy && !HasValidationErrors);
        LinkOnlyCommand = new VisualAsyncCommand(
            LinkOnlyAsync,
            () => HasPlan && Connection.CanUseFeeFeatures && !IsBusy && !HasValidationErrors);
        SelectAllCommand = new VisualRelayCommand(
            () => SetAllGenerationSelected(true),
            () => HasPlan && !IsBusy);
        DeselectAllCommand = new VisualRelayCommand(
            () => SetAllGenerationSelected(false),
            () => HasPlan && !IsBusy);
        CancelCommand = new VisualRelayCommand(CancelOperation, () => IsBusy);
        UndoCommand = new VisualRelayCommand(Undo, () => _planService.CanUndo && !IsBusy);
        RedoCommand = new VisualRelayCommand(Redo, () => _planService.CanRedo && !IsBusy);
        DropCommand = new VisualRelayCommand<ContainerToFeeVisualDropRequest>(
            HandleDrop,
            CanHandleDrop);
        RemoveAssignmentCommand = new VisualRelayCommand<ContainerToFeeVisualAssignmentVM>(
            RemoveAssignment,
            assignment => assignment is not null && !IsBusy);

        _planService.PlanChanged += OnPlanChanged;
        _connection.PropertyChanged += OnConnectionPropertyChanged;

        if (_planService.CurrentPlan is not null)
        {
            ApplyPlan(_planService.CurrentPlan);
            StatusText = "Gespeicherter visueller Plan ist geladen.";
        }
    }

    public FeeConnectionService Connection => _connection;

    public ObservableCollection<ContainerToFeeVisualTreeNodeVM> TreeRoots { get; } = new();

    public ObservableCollection<ContainerToFeeVisualTargetVM> Targets { get; } = new();

    public ObservableCollection<VisualEdge> VisibleEdges { get; } = new();

    public ObservableCollection<ContainerToFeeVisualFeeObjectVM> AvailableFeeObjects { get; } = new();

    public ObservableCollection<ContainerToFeeVisualFeeInterfaceVM> AvailableFeeInterfaces { get; } = new();

    public ObservableCollection<VisualIssue> Issues { get; } = new();

    public ICollectionView FeeObjectsView { get; }

    public ICommand OpenXmlCommand { get; }

    public ICommand LoadPlanCommand { get; }

    public ICommand SavePlanCommand { get; }

    public ICommand RefreshFeeObjectsCommand { get; }

    public ICommand AutoAssignCommand { get; }

    public ICommand StartGenerationCommand { get; }

    public ICommand LinkOnlyCommand { get; }

    public ICommand SelectAllCommand { get; }

    public ICommand DeselectAllCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand RedoCommand { get; }

    public ICommand DropCommand { get; }

    public ICommand RemoveAssignmentCommand { get; }

    public bool HasPlan => _planService.CurrentPlan is not null;

    public bool HasValidationErrors => Issues.Any(issue => issue.Severity == VisualIssueSeverity.Error);

    public bool IsFeeObjectDiscoveryAvailable => Connection.CanUseFeeFeatures && HasPlan && !IsBusy;

    public string FeeUnavailableReason => Connection.CanUseFeeFeatures
        ? string.Empty
        : FeeConnectionService.MissingConnectionMessage;

    public string SourceXmlPath
    {
        get => _sourceXmlPath;
        private set
        {
            _sourceXmlPath = value;
            OnPropertyChanged();
        }
    }

    public string SidecarPath => _planService.CurrentPlan?.SidecarPath ?? string.Empty;

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFeeObjectDiscoveryAvailable));
            InvalidateCommands();
        }
    }

    public string TreeFilter
    {
        get => _treeFilter;
        set
        {
            if (string.Equals(_treeFilter, value, StringComparison.Ordinal))
                return;

            _treeFilter = value ?? string.Empty;
            OnPropertyChanged();
            ApplyTreeFilter();
        }
    }

    public string FeeObjectFilter
    {
        get => _feeObjectFilter;
        set
        {
            if (string.Equals(_feeObjectFilter, value, StringComparison.Ordinal))
                return;

            _feeObjectFilter = value ?? string.Empty;
            OnPropertyChanged();
            FeeObjectsView.Refresh();
        }
    }

    public bool ShowOnlyCompatibleFeeObjects
    {
        get => _showOnlyCompatibleFeeObjects;
        set
        {
            if (_showOnlyCompatibleFeeObjects == value)
                return;

            _showOnlyCompatibleFeeObjects = value;
            OnPropertyChanged();
            FeeObjectsView.Refresh();
        }
    }

    public bool SelectedContainerSupportsCreation
    {
        get
        {
            var containerId = SelectedTreeNode?.ContainerId;
            return containerId is not null &&
                   _planService.CurrentPlan?.FindNode(containerId)?.SupportsCreation == true;
        }
    }

    public bool SelectedContainerSupportsGeneration =>
        SelectedTreeNode?.CanSelectGeneration == true;

    public bool CreateSignalsForSelection
    {
        get
        {
            var containerId = SelectedTreeNode?.ContainerId;
            return containerId is null ||
                   _planService.CurrentPlan?.ShouldCreateSignals(containerId) != false;
        }
        set
        {
            var containerId = SelectedTreeNode?.ContainerId;
            if (containerId is null ||
                CreateSignalsForSelection == value ||
                !_planService.SetSignalCreation(containerId, value))
            {
                return;
            }

            StatusText = value
                ? "Die Signale dieses Containers werden erzeugt."
                : "Vorhandene Signale werden im ausgewählten Interface gesucht und nur neu verknüpft.";
            _log.Information(LogArea, StatusText);
            OnPropertyChanged();
        }
    }

    public ContainerToFeeVisualFeeInterfaceVM? SelectedExistingInterface
    {
        get => _selectedExistingInterface;
        set
        {
            if (ReferenceEquals(_selectedExistingInterface, value))
                return;

            _selectedExistingInterface = value;
            OnPropertyChanged();
            if (!_isApplyingPlan)
                _planService.SetExistingInterface(value?.Model);
        }
    }

    public bool IsCreationRequestedForSelection
    {
        get
        {
            var containerId = SelectedTreeNode?.ContainerId;
            return containerId is not null &&
                   _planService.CurrentPlan?.IsCreationRequested(containerId) == true;
        }
        set
        {
            var containerId = SelectedTreeNode?.ContainerId;
            if (containerId is null ||
                IsCreationRequestedForSelection == value ||
                !_planService.SetCreationRequested(containerId, value))
                return;

            StatusText = value
                ? "Fehlende SimObjects werden bei der Generierung erzeugt."
                : "Nicht zugeordnete SimObjects werden übersprungen.";
            _log.Information(LogArea, StatusText);
            OnPropertyChanged();
        }
    }

    public ContainerToFeeVisualTreeNodeVM? SelectedTreeNode
    {
        get => _selectedTreeNode;
        set
        {
            if (ReferenceEquals(_selectedTreeNode, value))
                return;

            _selectedTreeNode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedContainerSupportsCreation));
            OnPropertyChanged(nameof(SelectedContainerSupportsGeneration));
            OnPropertyChanged(nameof(IsCreationRequestedForSelection));
            OnPropertyChanged(nameof(CreateSignalsForSelection));
            RefreshSelectionProjection();
        }
    }

    public ContainerToFeeVisualTargetVM? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (ReferenceEquals(_selectedTarget, value))
                return;

            _selectedTarget = value;
            OnPropertyChanged();
            FeeObjectsView.Refresh();
        }
    }

    public int ContainerCount => _planService.CurrentPlan?.Nodes.Count(node =>
        node.Kind == VisualNodeKind.Container) ?? 0;

    public int SelectedContainerCount => _planService.CurrentPlan?.Nodes.Count(node =>
        node.Kind == VisualNodeKind.Container &&
        ContainerMetadataCatalog.TryGet(node.TypeName, out _) &&
        _planService.CurrentPlan.IsGenerationSelected(node.Id)) ?? 0;

    public int ObjectCount => _planService.CurrentPlan?.Nodes.Count ?? 0;

    public int EdgeCount => _planService.CurrentPlan?.Edges.Count ?? 0;

    public int AssignmentCount => _planService.CurrentPlan?.Assignments.Count ?? 0;

    private async Task OpenXmlAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Container XML (*.xml)|*.xml|Alle Dateien (*.*)|*.*",
            Title = "Container-XML für visuelle Planung öffnen",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        await RunBusyAsync("Container-XML wird analysiert …", async cancellationToken =>
        {
            VisualPlanLoadResult result = await _planService.LoadXmlAsync(dialog.FileName, cancellationToken);
            PublishIssues(result.Issues);
            if (!result.Success)
            {
                StatusText = result.Message;
                _log.Warning(LogArea, result.Message);
                return;
            }

            ApplyPlan(result.Plan!);
            StatusText = result.Message;
            _log.Information(LogArea, result.Message);
        });
    }

    private async Task LoadPlanAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Container2FEE Visual Plan (*.json)|*.json|Alle Dateien (*.*)|*.*",
            Title = "Gespeicherten Container2FEE-Plan laden",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        await RunBusyAsync("Plan wird geladen …", async cancellationToken =>
        {
            VisualPlanLoadResult result = await _planService.LoadSidecarAsync(dialog.FileName, cancellationToken);
            PublishIssues(result.Issues);
            if (!result.Success)
            {
                StatusText = result.Message;
                _log.Warning(LogArea, result.Message);
                return;
            }

            ApplyPlan(result.Plan!);
            StatusText = result.Message;
            _log.Information(LogArea, result.Message);
        });
    }

    private async Task SavePlanAsync()
    {
        await RunBusyAsync("Plan wird gespeichert …", async cancellationToken =>
        {
            await _planService.SaveSidecarAsync(cancellationToken: cancellationToken);
            OnPropertyChanged(nameof(SidecarPath));
            StatusText = $"Plan gespeichert: {_planService.CurrentPlan?.SidecarPath}";
            _log.Information(LogArea, StatusText);
        });
    }

    private async Task RefreshFeeObjectsAsync()
    {
        if (!Connection.CanUseFeeFeatures)
        {
            Reject(FeeConnectionService.MissingConnectionMessage);
            return;
        }

        await RunBusyAsync("FEE-SimObjects werden gelesen …", async cancellationToken =>
        {
            IReadOnlyList<VisualFeeObject> objects =
                await _planService.DiscoverFeeObjectsAsync(cancellationToken);
            IReadOnlyList<VisualFeeInterface> interfaces =
                await _planService.DiscoverFeeInterfacesAsync(cancellationToken);
            RefreshFeeObjectProjection(objects);
            RefreshFeeInterfaceProjection(interfaces);
            FeeObjectsView.Refresh();
            int automaticAssignments = _planService.AutoAssignMatches();
            StatusText = automaticAssignments > 0
                ? $"{objects.Count} FEE-SimObjects und {interfaces.Count} Interfaces geladen; " +
                  $"{automaticAssignments} automatisch zugeordnet."
                : $"{objects.Count} FEE-SimObjects und {interfaces.Count} Interfaces geladen.";
            _log.Information(LogArea, StatusText);
            InvalidateCommands();
        });
    }

    private void AutoAssignMatches()
    {
        int count = _planService.AutoAssignMatches();
        StatusText = count > 0
            ? $"{count} FEE-SimObject-Zuordnung(en) automatisch erkannt."
            : "Keine weiteren eindeutigen Namens-/Typzuordnungen gefunden.";
        _log.Information(LogArea, StatusText);
    }

    private async Task StartGenerationAsync()
    {
        if (!Connection.CanUseFeeFeatures)
        {
            Reject(FeeConnectionService.MissingConnectionMessage);
            return;
        }

        VisualValidationResult validation = _planService.Validate();
        PublishIssues(validation.Issues);
        if (!validation.IsValid)
        {
            StatusText = "Generierung abgebrochen: Der Plan enthält Fehler.";
            _log.Warning(LogArea, StatusText);
            return;
        }

        await RunBusyAsync("Container werden mit dem bestehenden Executor erzeugt …", async cancellationToken =>
        {
            VisualExecutionResult result = await _planService.ExecuteAsync(cancellationToken);
            PublishIssues(result.Issues);
            StatusText = result.Message;
            if (result.Success)
                _log.Information(LogArea, result.Message);
            else
                _log.Warning(LogArea, result.Message);
        });
    }

    private async Task LinkOnlyAsync()
    {
        if (!Connection.CanUseFeeFeatures)
        {
            Reject(FeeConnectionService.MissingConnectionMessage);
            return;
        }

        await RunBusyAsync(
            "Bestehende FEE-SimObjects werden ohne Neugenerierung verknüpft …",
            async cancellationToken =>
            {
                VisualExecutionResult result =
                    await _planService.LinkExistingAssignmentsOnlyAsync(cancellationToken);
                PublishIssues(result.Issues);
                StatusText = result.Message;
                if (result.Success)
                    _log.Information(LogArea, result.Message);
                else
                    _log.Warning(LogArea, result.Message);
            });
    }

    private void SetAllGenerationSelected(bool selected)
    {
        var changed = _planService.SetAllGenerationSelected(selected);
        StatusText = changed == 0
            ? selected ? "Alle unterstützten Container waren bereits ausgewählt."
                       : "Alle unterstützten Container waren bereits abgewählt."
            : selected ? $"{changed} Container wurden ausgewählt."
                       : $"{changed} Container wurden abgewählt.";
        _log.Information(LogArea, StatusText);
    }

    private void HandleDrop(ContainerToFeeVisualDropRequest? request)
    {
        if (request?.Target is not ContainerToFeeVisualTargetVM target)
            return;

        string? feeObjectId = request.Source switch
        {
            ContainerToFeeVisualFeeObjectVM feeObject => feeObject.Id,
            ContainerToFeeVisualAssignmentVM assignment => assignment.FeeObjectId,
            _ => null,
        };

        if (feeObjectId is null)
        {
            Reject("Das gezogene Element ist kein zuweisbares FEE-SimObject.");
            return;
        }

        VisualAssignmentResult result = _planService.TryAssign(target.Id, feeObjectId);
        PublishIssues(result.Issues);
        if (!result.Success)
        {
            Reject(result.Message);
            return;
        }

        StatusText = result.Message;
        _log.Information(LogArea, result.Message);
    }

    private bool CanHandleDrop(ContainerToFeeVisualDropRequest? request)
    {
        if (IsBusy || request?.Target is not ContainerToFeeVisualTargetVM target)
            return false;

        return request.Source switch
        {
            ContainerToFeeVisualFeeObjectVM feeObject => target.Model.CanAssign(feeObject.Model),
            ContainerToFeeVisualAssignmentVM assignment =>
                string.Equals(target.AllowedTypeName, assignment.FeeObjectTypeName, StringComparison.Ordinal) ||
                string.Equals(target.AllowedTypeName, assignment.FeeType, StringComparison.Ordinal),
            _ => false,
        };
    }

    private void RemoveAssignment(ContainerToFeeVisualAssignmentVM? assignment)
    {
        if (assignment is null)
            return;

        VisualAssignmentResult result =
            _planService.RemoveAssignment(assignment.TargetId, assignment.FeeObjectId);
        PublishIssues(result.Issues);
        if (!result.Success)
        {
            Reject(result.Message);
            return;
        }

        StatusText = result.Message;
        _log.Information(LogArea, result.Message);
    }

    private void Undo()
    {
        if (_planService.Undo())
        {
            StatusText = "Letzte Zuordnungsänderung rückgängig gemacht.";
            _log.Information(LogArea, StatusText);
        }
    }

    private void Redo()
    {
        if (_planService.Redo())
        {
            StatusText = "Zuordnungsänderung wiederhergestellt.";
            _log.Information(LogArea, StatusText);
        }
    }

    private void CancelOperation()
    {
        _operationCancellation?.Cancel();
        StatusText = "Vorgang wird abgebrochen …";
        _log.Information(LogArea, StatusText);
    }

    private async Task RunBusyAsync(string status, Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
            return;

        _operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        StatusText = status;
        try
        {
            await operation(_operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Vorgang abgebrochen.";
            _log.Information(LogArea, StatusText);
        }
        catch (Exception exception)
        {
            StatusText = "Vorgang fehlgeschlagen. Details stehen im Protokoll.";
            _log.Error(LogArea, StatusText, exception);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private void OnPlanChanged(object? sender, VisualPlanChangedEventArgs args)
    {
        void Apply() => ApplyPlan(args.Plan);
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Apply();
        else
            dispatcher.BeginInvoke(Apply);
    }

    private void ApplyPlan(VisualPlan plan)
    {
        string? selectedNodeId = SelectedTreeNode?.Id;
        string? selectedTargetId = SelectedTarget?.Id;
        _isApplyingPlan = true;
        try
        {
            SourceXmlPath = plan.SourceXmlPath;
            ReplaceCollection(TreeRoots, plan.Roots.Select(node => BuildTree(node, plan)));
            RefreshFeeObjectProjection(_planService.DiscoveredFeeObjects);
            RefreshFeeInterfaceProjection(_planService.DiscoveredFeeInterfaces);
            PublishIssues(_planService.Validate().Issues);
            ApplyTreeFilter();

            SelectedTreeNode = FindTreeNode(selectedNodeId) ?? TreeRoots.FirstOrDefault();
            SelectedTarget = Targets.FirstOrDefault(target => target.Id == selectedTargetId) ?? Targets.FirstOrDefault();
            SelectedExistingInterface = AvailableFeeInterfaces.FirstOrDefault(item =>
                string.Equals(
                    item.GuidString,
                    plan.ExistingInterfaceSelection?.InterfaceGuid,
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _isApplyingPlan = false;
        }

        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(SidecarPath));
        OnPropertyChanged(nameof(ContainerCount));
        OnPropertyChanged(nameof(SelectedContainerCount));
        OnPropertyChanged(nameof(ObjectCount));
        OnPropertyChanged(nameof(EdgeCount));
        OnPropertyChanged(nameof(AssignmentCount));
        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(IsFeeObjectDiscoveryAvailable));
        OnPropertyChanged(nameof(SelectedContainerSupportsCreation));
        OnPropertyChanged(nameof(SelectedContainerSupportsGeneration));
        OnPropertyChanged(nameof(IsCreationRequestedForSelection));
        OnPropertyChanged(nameof(CreateSignalsForSelection));
        InvalidateCommands();
    }

    private ContainerToFeeVisualTreeNodeVM BuildTree(VisualNode node, VisualPlan plan) =>
        new(
            node,
            node.Children.Select(child => BuildTree(child, plan)),
            plan.IsGenerationSelected(node.Id),
            node.Kind == VisualNodeKind.Container && ContainerMetadataCatalog.TryGet(node.TypeName, out _),
            GetContainerSimObjectState(node, plan),
            SetGenerationSelected);

    private static ContainerToFeeVisualNodeState GetContainerSimObjectState(
        VisualNode node,
        VisualPlan plan)
    {
        if (node.Kind != VisualNodeKind.Container)
            return ContainerToFeeVisualNodeState.None;

        var targets = plan.Targets.Where(target => target.ContainerId == node.Id).ToArray();
        if (targets.Length == 0)
            return ContainerToFeeVisualNodeState.None;

        var assignedTargetIds = plan.Assignments
            .Select(assignment => assignment.TargetId)
            .ToHashSet(StringComparer.Ordinal);
        return targets.All(target => assignedTargetIds.Contains(target.Id))
            ? ContainerToFeeVisualNodeState.Assigned
            : ContainerToFeeVisualNodeState.Missing;
    }

    private void SetGenerationSelected(string containerId, bool selected)
    {
        if (!_planService.SetGenerationSelected(containerId, selected))
            return;
        StatusText = selected
            ? "Container wurde für die FEE-Aktion ausgewählt."
            : "Container wurde von der FEE-Aktion ausgeschlossen.";
    }

    private ContainerToFeeVisualTreeNodeVM? FindTreeNode(string? id)
    {
        if (id is null)
            return null;

        return TreeRoots.SelectMany(root => root.SelfAndDescendants()).FirstOrDefault(node => node.Id == id);
    }

    private void RefreshSelectionProjection()
    {
        Targets.Clear();
        VisibleEdges.Clear();

        VisualPlan? plan = _planService.CurrentPlan;
        string? containerId = SelectedTreeNode?.ContainerId;
        if (plan is null || string.IsNullOrWhiteSpace(containerId))
        {
            SelectedTarget = null;
            return;
        }

        foreach (VisualSimObjectTarget target in plan.Targets.Where(target => target.ContainerId == containerId))
        {
            var assignments = plan.Assignments.Where(assignment => assignment.TargetId == target.Id);
            Targets.Add(new ContainerToFeeVisualTargetVM(
                target,
                assignments,
                plan.IsCreationRequested(containerId)));
        }

        HashSet<string> nodeIds = plan.Nodes
            .Where(node => node.ContainerId == containerId)
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (VisualEdge edge in plan.Edges.Where(edge =>
                     nodeIds.Contains(edge.SourceId) || nodeIds.Contains(edge.TargetId)))
            VisibleEdges.Add(edge);

        SelectedTarget = Targets.FirstOrDefault();
    }

    private void ApplyTreeFilter()
    {
        foreach (ContainerToFeeVisualTreeNodeVM root in TreeRoots)
            root.ApplyFilter(TreeFilter);
    }

    private bool FilterFeeObject(object item)
    {
        if (item is not ContainerToFeeVisualFeeObjectVM feeObject)
            return false;

        if (!string.IsNullOrWhiteSpace(FeeObjectFilter) &&
            !feeObject.Name.Contains(FeeObjectFilter, StringComparison.OrdinalIgnoreCase) &&
            !feeObject.FeeType.Contains(FeeObjectFilter, StringComparison.OrdinalIgnoreCase) &&
            !feeObject.TypeName.Contains(FeeObjectFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        return !ShowOnlyCompatibleFeeObjects ||
               SelectedTarget is null ||
               SelectedTarget.Model.CanAssign(feeObject.Model);
    }

    private void RefreshFeeObjectProjection(IReadOnlyList<VisualFeeObject>? objects = null)
    {
        var plan = _planService.CurrentPlan;
        var source = objects ?? AvailableFeeObjects.Select(item => item.Model).ToArray();
        ReplaceCollection(
            AvailableFeeObjects,
            source.Select(item => new ContainerToFeeVisualFeeObjectVM(item, plan)));
        FeeObjectsView.Refresh();
    }

    private void RefreshFeeInterfaceProjection(IReadOnlyList<VisualFeeInterface>? interfaces = null)
    {
        var selectedGuid = _planService.CurrentPlan?.ExistingInterfaceSelection?.InterfaceGuid;
        var source = interfaces ?? AvailableFeeInterfaces.Select(item => item.Model).ToArray();
        ReplaceCollection(
            AvailableFeeInterfaces,
            source.Select(item => new ContainerToFeeVisualFeeInterfaceVM(item)));
        SelectedExistingInterface = AvailableFeeInterfaces.FirstOrDefault(item => string.Equals(
            item.GuidString,
            selectedGuid,
            StringComparison.OrdinalIgnoreCase));
    }

    private void PublishIssues(IEnumerable<VisualIssue> issues)
    {
        ReplaceCollection(Issues, issues);
        OnPropertyChanged(nameof(HasValidationErrors));
    }

    private void Reject(string message)
    {
        StatusText = message;
        _log.Warning(LogArea, message);
    }

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not nameof(FeeConnectionService.IsConnected) and
            not nameof(FeeConnectionService.CanUseFeeFeatures))
            return;

        OnPropertyChanged(nameof(FeeUnavailableReason));
        OnPropertyChanged(nameof(IsFeeObjectDiscoveryAvailable));
        InvalidateCommands();
    }

    private void InvalidateCommands() => CommandManager.InvalidateRequerySuggested();

    private static FeeConnectionService ResolveConnection() =>
        Services.Connection ?? new FeeConnectionService();

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (T item in items)
            target.Add(item);
    }

    private sealed class VisualRelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public VisualRelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute ?? (() => true);
        }

        public bool CanExecute(object? parameter) => _canExecute();
        public void Execute(object? parameter) => _execute();
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    private sealed class VisualRelayCommand<T> : ICommand where T : class
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool> _canExecute;

        public VisualRelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute ?? (_ => true);
        }

        public bool CanExecute(object? parameter) => _canExecute(parameter as T);
        public void Execute(object? parameter) => _execute(parameter as T);
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    private sealed class VisualAsyncCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _running;

        public VisualAsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute ?? (() => true);
        }

        public bool CanExecute(object? parameter) => !_running && _canExecute();

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
                return;

            _running = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await _execute();
            }
            finally
            {
                _running = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}

public sealed record ContainerToFeeVisualNodeState(string Background, string Description)
{
    public static ContainerToFeeVisualNodeState None { get; } = new("Transparent", string.Empty);
    public static ContainerToFeeVisualNodeState Assigned { get; } =
        new("#FFC6EFCE", "Alle benötigten FEE-SimObjects sind zugeordnet.");
    public static ContainerToFeeVisualNodeState Missing { get; } =
        new("#FFFFC7CE", "Mindestens ein benötigtes FEE-SimObject fehlt.");
}

public sealed class ContainerToFeeVisualTreeNodeVM : NotifyBase
{
    private bool _isExpanded;
    private bool _isVisible = true;
    private bool _isGenerationSelected;
    private readonly Action<string, bool> _setGenerationSelected;
    private readonly bool _canSelectGeneration;

    public ContainerToFeeVisualTreeNodeVM(
        VisualNode model,
        IEnumerable<ContainerToFeeVisualTreeNodeVM> children,
        bool isGenerationSelected,
        bool canSelectGeneration,
        ContainerToFeeVisualNodeState simObjectState,
        Action<string, bool> setGenerationSelected)
    {
        Model = model;
        Children = new ObservableCollection<ContainerToFeeVisualTreeNodeVM>(children);
        _isGenerationSelected = isGenerationSelected;
        _canSelectGeneration = canSelectGeneration;
        SimObjectState = simObjectState;
        _setGenerationSelected = setGenerationSelected;
        _isExpanded = !model.IsTechnical && model.Kind is VisualNodeKind.Root or VisualNodeKind.Container;
    }

    public VisualNode Model { get; }
    public string Id => Model.Id;
    public string? ContainerId => Model.ContainerId;
    public string Name => Model.Name;
    public string TypeName => Model.TypeName;
    public string Slot => Model.Slot ?? string.Empty;
    public VisualNodeKind Kind => Model.Kind;
    public bool IsTechnical => Model.IsTechnical;
    public bool SupportsCreation => Model.SupportsCreation;
    public bool CanSelectGeneration => _canSelectGeneration;
    public ContainerToFeeVisualNodeState SimObjectState { get; }
    public string StateBackground => SimObjectState.Background;
    public string SimObjectStateDescription => SimObjectState.Description;
    public ObservableCollection<ContainerToFeeVisualTreeNodeVM> Children { get; }

    public bool IsGenerationSelected
    {
        get => _isGenerationSelected;
        set
        {
            if (!CanSelectGeneration || !SetPropertyChange(ref _isGenerationSelected, value))
                return;
            _setGenerationSelected(Id, value);
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetPropertyChange(ref _isExpanded, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetPropertyChange(ref _isVisible, value);
    }

    public IEnumerable<ContainerToFeeVisualTreeNodeVM> SelfAndDescendants()
    {
        yield return this;
        foreach (ContainerToFeeVisualTreeNodeVM child in Children)
        foreach (ContainerToFeeVisualTreeNodeVM descendant in child.SelfAndDescendants())
            yield return descendant;
    }

    public bool ApplyFilter(string filter)
    {
        bool childMatches = Children.Aggregate(false, (match, child) => child.ApplyFilter(filter) || match);
        bool selfMatches = string.IsNullOrWhiteSpace(filter) ||
                           Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                           TypeName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                           Slot.Contains(filter, StringComparison.OrdinalIgnoreCase);
        IsVisible = selfMatches || childMatches;
        if (!string.IsNullOrWhiteSpace(filter) && childMatches)
            IsExpanded = true;
        return IsVisible;
    }
}

public sealed class ContainerToFeeVisualTargetVM
{
    public ContainerToFeeVisualTargetVM(
        VisualSimObjectTarget model,
        IEnumerable<VisualAssignment> assignments,
        bool isCreationRequested)
    {
        Model = model;
        Assignments = new ObservableCollection<ContainerToFeeVisualAssignmentVM>(
            assignments.Select(assignment => new ContainerToFeeVisualAssignmentVM(assignment, model.AllowedTypeName)));
        IsCreationRequested = isCreationRequested;
    }

    public VisualSimObjectTarget Model { get; }
    public string Id => Model.Id;
    public string DisplayName => Model.DisplayName;
    public string AllowedTypeName => Model.AllowedTypeName;
    public string SelectionMode => Model.AllowMultiSelect ? "Mehrfachauswahl" : "Einzelauswahl";
    public ObservableCollection<ContainerToFeeVisualAssignmentVM> Assignments { get; }
    public bool IsAssigned => Assignments.Count > 0;
    public bool IsCreationRequested { get; }
    public string AssignmentState => IsAssigned
        ? "Vorhandenes FEE-SimObject zugeordnet"
        : IsCreationRequested
            ? "Wird bei der Generierung erzeugt"
            : "Simulationsobjekt fehlt – Zuordnung erforderlich";
    public string StateBackground => IsAssigned
        ? "#FFC6EFCE"
        : IsCreationRequested
            ? "#FFFFF2CC"
            : "#FFFFC7CE";
}

/// <summary>Presentation wrapper for one existing FEE interface.</summary>
public sealed class ContainerToFeeVisualFeeInterfaceVM(VisualFeeInterface model)
{
    public VisualFeeInterface Model { get; } = model;
    public string GuidString => Model.GuidString;
    public string Name => Model.Name;
    public int SignalCount => Model.SignalCount;
    public string DisplayName => $"{Name} ({SignalCount} Signale)";
}

/// <summary>Presentation state showing whether an FEE object is already assigned.</summary>
public sealed class ContainerToFeeVisualFeeObjectVM
{
    public ContainerToFeeVisualFeeObjectVM(VisualFeeObject model, VisualPlan? plan)
    {
        Model = model;
        var assignments = plan?.Assignments
            .Where(assignment => assignment.FeeObjectId == model.Id)
            .ToArray() ?? [];
        AssignedTargets = assignments
            .Select(assignment => DescribeAssignment(plan, assignment))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public VisualFeeObject Model { get; }
    public string Id => Model.Id;
    public string Name => Model.Name;
    public string TypeName => Model.TypeName;
    public string FeeType => Model.FeeType;
    public IReadOnlyList<string> AssignedTargets { get; }
    public bool IsAssigned => AssignedTargets.Count > 0;
    public string AssignmentText => IsAssigned
        ? $"Verknüpft mit: {string.Join("; ", AssignedTargets)}"
        : "Noch nicht zugeordnet";
    public string StateBackground => IsAssigned ? "#FFC6EFCE" : "Transparent";

    private static string DescribeAssignment(VisualPlan? plan, VisualAssignment assignment)
    {
        var target = plan?.FindTarget(assignment.TargetId);
        if (target is null)
            return assignment.TargetId;

        var container = plan?.FindNode(target.ContainerId);
        var logic = plan?.Nodes.FirstOrDefault(node =>
            node.ContainerId == target.ContainerId && node.Kind == VisualNodeKind.Logic);
        var logicOrContainer = logic?.Name ?? container?.TypeName ?? "—";
        return $"Ziel: {target.DisplayName} | Container: {container?.Name ?? "—"} | Logik/Typ: {logicOrContainer}";
    }
}

public sealed class ContainerToFeeVisualAssignmentVM
{
    public ContainerToFeeVisualAssignmentVM(VisualAssignment model, string feeType)
    {
        Model = model;
        FeeType = feeType;
    }

    public VisualAssignment Model { get; }
    public string TargetId => Model.TargetId;
    public string FeeObjectId => Model.FeeObjectId;
    public string FeeObjectName => Model.FeeObjectName;
    public string FeeObjectTypeName => Model.FeeObjectTypeName;
    public string FeeType { get; }
}
