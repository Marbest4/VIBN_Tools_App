using Microsoft.Win32;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VIBN_Tools.ContainerGeneration.BusinessLogic;
using VIBN_Tools.ContainerGeneration.BusinessLogic.ContainerData;
using VIBN_Tools.ContainerGeneration.BusinessLogic.RequirementsXml;
using VIBN_Tools.ContainerGeneration.BusinessLogic.ZuLiData;
using VIBN_Tools.ContainerGeneration.BusinessLogic.ZuLiData.Interfaces;
using VIBN_Tools.ContainerGeneration.Models;
using VIBN_Tools.ContainerGeneration.Utils;
using VIBN_Tools.GlobalClasses;
using VIBN_Tools.ContainerGeneration.AI;

namespace VIBN_Tools.Application.VM
{
    /// <summary>
    /// WPF-facing container-generation view model. File processing and
    /// workspace state are supplied by focused base classes.
    /// </summary>
    public sealed class ContainerGenerationPageVM : ContainerGenerationWorkflowVM
    {
        public ICommand OpenInterfaceFile => GetCommandBindingAsync(Open_InterfaceFile);

        public ICommand OpenRequirementsXml => GetCommandBindingAsync(Open_RequirementsXml);

        public ICommand LoadSettings => GetCommandBindingAsync(Load_Settings);

        public ICommand SaveSettings => GetCommandBinding(Save_Settings);

        public ICommand GenerateContainers => GetCommandBindingAsync(Generate_Containers);

        public ICommand ValidateWorkspace => GetCommandBinding(Validate_Workspace);

        public ICommand ExportContainers => GetCommandBinding(Export_Containers);

        public ICommand LoadData => GetCommandBinding(Load_Data);

        public ICommand SaveData => GetCommandBinding(Save_Data);

        public ICommand DeleteItem => GetCommandBinding(Delete_Item);

        public ICommand UndoLastAction => GetCommandBinding(Undo_LastAction);

        public ICommand RedoLastAction => GetCommandBinding(Redo_LastAction);

        public ICommand ClearActivityLog => GetCommandBinding(Clear_ActivityLog);

        public ICommand ApplyReimportSelection => GetCommandBinding(Apply_ReimportSelection);

        public ICommand CancelReimportSelection => GetCommandBinding(Cancel_ReimportSelection);

        public ICommand SelectAllReimportChanges => GetCommandBinding(
    () => SetAllReimportChanges(true));

        public ICommand SelectNoReimportChanges => GetCommandBinding(
    () => SetAllReimportChanges(false));

        public ICommand ContainerDataGridPreviewKeyDown => GetCommandBinding(ContainerGrid_OnPreviewKeyDown);

        public ICommand ContainerDataGridDragOver => GetCommandBinding(ContainerGrid_OnDragOver);

        public ICommand DataGridDrop => GetCommandBinding(Datagrid_OnDrop);

        public ICommand DataGridMouseMove => GetCommandBinding(Datagrid_OnMouseMove);

        public ICommand SelectionChangedExecuted => GetCommandBinding(SelectionChanged_Executed);

public ContainerGenerationPageVM()
        {
            Settings = new ContainerGenerationSettings();

            FilteredEntries = new ObservableCollection<ContainerEntry>();
            UnassignedEntries = new ObservableCollection<ContainerEntry>();
            ContainerList = new ObservableCollection<ContainerData>();
            ContainerList.CollectionChanged += ContainerList_CollectionChanged;

            SelectedContainers = new List<ContainerData>();

            ComponentTypes = new ObservableCollection<string>();


            // Configure and initialise Debounce-Timer
            _debounceTimerContainerData = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _debounceTimerContainerData.Tick += (sender, eventArgs) =>
            {
                _debounceTimerContainerData.Stop();
                FilterContainerGrid();
            };

            _debounceTimerUnassignedData = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _debounceTimerUnassignedData.Tick += (sender, eventArgs) =>
            {
                _debounceTimerUnassignedData.Stop();
                FilterUnassignedEntriesGrid();
            };

            _debounceTimerFilteredData = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _debounceTimerFilteredData.Tick += (sender, eventArgs) =>
            {
                _debounceTimerFilteredData.Stop();
                FilterFilteredEntriesGrid();
            };

            SelectedReviewFilter = ReviewFilterOptions[0];


            StatusText = string.Empty;

            Zuli = new ZuLiDefault();

            RequirementsFile = new RequirementsXml();
            RequirementsProvider.RequirementsFile = this.RequirementsFile;


            ContainerGenerator = new ContainerGenerator();

            LoadDefaultSettings();

            AddActivity(
                "System",
                "Container-Generator geöffnet",
                "Neue Bearbeitungssitzung gestartet.");


        }

        private void Delete_Item(object parameter)
        {
            if (parameter is ContainerEntry item)
            {
                var sourceContainer = ContainerList.FirstOrDefault(
                    container => container.DataList.Contains(item));
                if (sourceContainer is null)
                {
                    Logger.Warn("Could not remove entry {EntryId}: no source container found.", item.ID);
                    return;
                }

                RunWorkspaceAction(
                    "Signal aus Container entfernen",
                    () =>
                    {
                        var result = GenerationWorkspaceEditor.MoveToUnassigned(
                            item,
                            item.Signal,
                            ContainerList,
                            UnassignedEntries,
                            FilteredEntries);
                        MarkAsManual(item, "Manuell aus dem Container entfernt.");
                        if (result.RemovedDuplicateOccurrences > 0)
                        {
                            Logger.Warn(
                                "Removed {DuplicateCount} duplicate workspace occurrences for entry {EntryId}.",
                                result.RemovedDuplicateOccurrences,
                                item.ID);
                        }
                    },
                    $"Signal „{item.Signal}“ aus Container „{sourceContainer.Component}“");
            }
        }



        /// <summary>
        /// Filters the unassigned entries grid based on the search text.
        /// </summary>
        /// <remarks>
        /// This method filters the <see cref="ContainerList"/> collection based on the <see cref="SearchTextContainer"/>.
        /// If the search text is empty or less than 2 characters, the filter is cleared. Otherwise, a short delay is introduced
        /// to keep the input responsive before starting the search.
        /// The filter checks for matches in various properties of the <see cref="ContainerData"/>.
        /// </remarks>
        private void FilterContainerGrid()
        {
            var view = CollectionViewSource.GetDefaultView(ContainerList);
            var search = SearchTextContainer?.Trim() ?? string.Empty;
            var reviewFilter = SelectedReviewFilter?.Value ?? WorkspaceReviewFilter.All;
            ApplyWorkspaceFilter(
                view,
                string.IsNullOrEmpty(search) && reviewFilter == WorkspaceReviewFilter.All
                    ? null
                    : item =>
                        item is ContainerData container &&
                        (string.IsNullOrEmpty(search) ||
                         ContainerWorkspaceSearch.Matches(container, search)) &&
                        MatchesReviewFilter(container, reviewFilter),
                "Container");
        }


        /// <summary>
        /// Filters the unassigned entries grid based on the search text.
        /// </summary>
        /// <remarks>
        /// This method filters the <see cref="UnassignedEntries"/> collection based on the <see cref="SearchTextUnassignedEntries"/>.
        /// If the search text is empty or less than 2 characters, the filter is cleared. Otherwise, a short delay is introduced
        /// to keep the input responsive before starting the search.
        /// The filter checks for matches in various properties of the <see cref="ContainerEntry"/>.
        /// </remarks>
        private void FilterUnassignedEntriesGrid()
        {
            var viewUnassigned = CollectionViewSource.GetDefaultView(UnassignedEntries);
            var search = SearchTextUnassignedEntries?.Trim() ?? string.Empty;
            var reviewFilter = SelectedReviewFilter?.Value ?? WorkspaceReviewFilter.All;
            ApplyWorkspaceFilter(
                viewUnassigned,
                string.IsNullOrEmpty(search) && reviewFilter == WorkspaceReviewFilter.All
                    ? null
                    : item =>
                        item is ContainerEntry entry &&
                        (string.IsNullOrEmpty(search) ||
                         ContainerWorkspaceSearch.Matches(entry, search)) &&
                        MatchesReviewFilter(entry, reviewFilter),
                "nicht zugeordnete Signale");

        }

        /// <summary>
        /// Filters the filtered entries grid based on the search text.
        /// </summary>
        /// <remarks>
        /// This method filters the <see cref="FilteredEntries"/> collection based on the <see cref="SearchTextFilteredEntries"/>.
        /// If the search text is empty or less than 2 characters, the filter is cleared. Otherwise, a short delay is introduced
        /// to keep the input responsive before starting the search.
        /// The filter checks for matches in various properties of the <see cref="ContainerEntry"/>.
        /// </remarks>
        private void FilterFilteredEntriesGrid()
        {
            var viewFiltered = CollectionViewSource.GetDefaultView(FilteredEntries);
            var search = SearchTextFilteredEntries?.Trim() ?? string.Empty;
            var reviewFilter = SelectedReviewFilter?.Value ?? WorkspaceReviewFilter.All;
            ApplyWorkspaceFilter(
                viewFiltered,
                string.IsNullOrEmpty(search) && reviewFilter == WorkspaceReviewFilter.All
                    ? null
                    : item =>
                        item is ContainerEntry entry &&
                        (string.IsNullOrEmpty(search) ||
                         ContainerWorkspaceSearch.Matches(entry, search)) &&
                        MatchesReviewFilter(entry, reviewFilter),
                "gefilterte Signale");
        }

        protected override void RefreshAllWorkspaceFilters()
        {
            if (_debounceTimerContainerData is null)
                return;

            FilterContainerGrid();
            FilterUnassignedEntriesGrid();
            FilterFilteredEntriesGrid();
        }

        private static bool MatchesReviewFilter(
            ContainerData container,
            WorkspaceReviewFilter filter) =>
            filter switch
            {
                WorkspaceReviewFilter.NeedsReview => container.RequiresReview,
                WorkspaceReviewFilter.Changed => container.HasDetectedChanges,
                WorkspaceReviewFilter.ManuallyEdited =>
                    container.DataList.Any(entry => entry.IsManuallyEdited),
                WorkspaceReviewFilter.Unchecked =>
                    !container.ManuallyChecked &&
                    (!container.IsValid || container.HasDetectedChanges),
                WorkspaceReviewFilter.Invalid => !container.IsValid,
                _ => true
            };

        private static bool MatchesReviewFilter(
            ContainerEntry entry,
            WorkspaceReviewFilter filter) =>
            filter switch
            {
                WorkspaceReviewFilter.NeedsReview =>
                    entry.ReviewState is ContainerEntryReviewState.NeedsReview or
                        ContainerEntryReviewState.SourceChanged or
                        ContainerEntryReviewState.NewFromSource or
                        ContainerEntryReviewState.NewlyRecognized ||
                    string.IsNullOrWhiteSpace(entry.Signal),
                WorkspaceReviewFilter.Changed =>
                    entry.ReviewState is not ContainerEntryReviewState.None and
                        not ContainerEntryReviewState.Preserved,
                WorkspaceReviewFilter.ManuallyEdited => entry.IsManuallyEdited,
                WorkspaceReviewFilter.Unchecked =>
                    entry.ReviewState is ContainerEntryReviewState.NeedsReview or
                        ContainerEntryReviewState.SourceChanged or
                        ContainerEntryReviewState.NewFromSource or
                        ContainerEntryReviewState.NewlyRecognized,
                WorkspaceReviewFilter.Invalid => string.IsNullOrWhiteSpace(entry.Signal),
                _ => true
            };

        private void ApplyWorkspaceFilter(
            ICollectionView view,
            Predicate<object>? filter,
            string area)
        {
            try
            {
                if (view is IEditableCollectionView editableView)
                {
                    if (editableView.IsEditingItem)
                        editableView.CommitEdit();
                    if (editableView.IsAddingNew)
                        editableView.CommitNew();
                }

                using (view.DeferRefresh())
                    view.Filter = filter;
            }
            catch (InvalidOperationException ex)
            {
                Logger.Warn(ex, "Could not refresh {FilterArea} filter.", area);
                StatusText =
                    $"Der Filter für {area} konnte während einer laufenden Bearbeitung nicht aktualisiert werden. " +
                    "Bitte Eingabe abschließen und erneut versuchen.";
            }
        }



        /// <summary>
        /// Finds the first ancestor of the specified type in the visual tree.
        /// </summary>
        /// <typeparam name="T">The type of the ancestor to find. Must be a subclass of DependencyObject.</typeparam>
        /// <param name="current">The starting point in the visual tree to begin the search.</param>
        /// <returns>
        /// The first ancestor of type <typeparamref name="T"/> found in the visual tree, or <c>null</c> if no ancestor of the specified type is found.
        /// </returns>
        /// <remarks>
        /// This method traverses up the visual tree starting from the given <paramref name="current"/> object.
        /// It checks each parent object to see if it matches the specified type <typeparamref name="T"/>.
        /// If a match is found, the ancestor is returned; otherwise, the search continues up the tree.
        /// </remarks>
        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T type)
                {
                    return type;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }


        /// <summary>
        /// Event handler for the log received event. Updates the status text if a log event was received.
        /// </summary>
        /// <param name="sender">Sender of the event.</param>
        /// <param name="message">Log message.</param>
        private void CustomTarget_LogReceived(object? sender, string message)
        {
            StatusText = message;
        }


        //===========================================================================================================================
        // E V E N T S
        //===========================================================================================================================

        protected override void UpdateUICount(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (sender is IEnumerable<ContainerData> ContainerList)
            {
                foreach (ContainerData t_Data in ContainerList)
                {
                    t_Data.DataList.CollectionChanged -= UpdateUICount;
                    t_Data.DataList.CollectionChanged += UpdateUICount;
                }
            }

            OnPropertyChanged(nameof(AssignedSignals));
            OnPropertyChanged(nameof(PercentComplete));
        }


        /// <summary>
        /// Handles the preview key down event to add data to unassigned entries when the delete key is pressed.
        /// This method checks if the delete key is pressed and if the selected container is in the container list,
        /// then adds the data list of the selected container to the unassigned entries.
        /// </summary>
        /// <param name="e">The <see cref="KeyEventArgs"/> instance containing the event data.</param>
        public void ContainerGrid_OnPreviewKeyDown(object parameter)
        {
            if (parameter is not KeyEventArgs e)
                return;

            if (e.Key == Key.Delete)
            {
                var containersToClear = SelectedContainers
                    .Where(container => container is not null && ContainerList.Contains(container))
                    .ToList();
                if (containersToClear.Count == 0)
                    return;

                var signalCount = containersToClear.Sum(container => container.DataList.Count);
                RunWorkspaceAction(
                    "Ausgewählte Container leeren",
                    () =>
                    {
                        foreach (var selectedContainer in containersToClear)
                        {
                            foreach (var item in selectedContainer.DataList.ToList())
                            {
                                GenerationWorkspaceEditor.MoveToUnassigned(
                                    item,
                                    item.Signal,
                                    ContainerList,
                                    UnassignedEntries,
                                    FilteredEntries);
                                MarkAsManual(
                                    item,
                                    "Manuell aus dem Container entfernt.");
                            }
                        }
                    },
                    $"{containersToClear.Count} Container mit {signalCount} Signalen");

                e.Handled = true;
            }
        }

        private void SelectionChanged_Executed(object parameter)
        {
            this.SelectedContainers.Clear();
            if (parameter is IList ConvertedList)
            {
                foreach (var SelectedData in ConvertedList)
                {
                    if (SelectedData is ContainerData ConvertedData)
                    {
                        this.SelectedContainers.Add(ConvertedData);
                    }
                }
            }
        }


        /// <summary>
        /// Handles the type selection change event. After assigning a new type its corresponding slot data will be updated.
        /// </summary>
        /// <param name="e">The <see cref="SelectionChangedEventArgs"/> instance containing the event data.</param>
        public void OnTypeSelectionChanged(SelectionChangedEventArgs e)
        {
            if (e != null && e.AddedItems.Count > 0)
            {
                if (((ComboBox)e.Source).DataContext is ContainerData data)
                {
                    data.Slots.Clear();

                    foreach (var slot in RequirementsFile.GetSlotNames(data.Type))
                        data.Slots.Add(slot);

                    data.MinSignals = RequirementsFile.GetMinSignals(data.Type);
                    data.MaxSignals = RequirementsFile.GetMaxSignals(data.Type);
                    foreach (var entry in data.DataList)
                        MarkAsManual(entry, "Containertyp wurde manuell geändert.");
                    data.Validate();
                }
            }
        }


        /// <summary>
        /// Event that will be fired once loading the view is completed.
        /// </summary>
        /// <param name="view">The main view.</param>
        public void OnViewLoaded()
        {
            var config = NLog.LogManager.Configuration;
            if (config == null)
            {
                Logger.Error("NLog configuration not loaded");
                return;
            }

            var customTarget = config.FindTargetByName<CustomLoggerTarget>("CustomLog");
            if (customTarget == null)
            {
                Logger.Error("CustomLog target not found in NLog configuration.");
                return;
            }

            customTarget.LogReceived += CustomTarget_LogReceived;
        }



        /// <summary>
        /// Handles the mouse move event to initiate a drag-and-drop operation.
        /// </summary>
        /// <param name="e">The <see cref="MouseEventArgs"/> instance containing the event data.</param>
        /// <remarks>
        /// This method checks if the left mouse button is pressed and if the source of the event is a <see cref="DataGrid"/>.
        /// If so, it attempts to find the <see cref="DataGridRow"/> that is the ancestor of the original source of the event.
        /// If a valid <see cref="DataGridRow"/> is found, it retrieves the corresponding data item and checks if it matches the
        /// <see cref="SelectedUnassignedEntry"/>. If all conditions are met, it initiates a drag-and-drop operation with the data item.
        /// </remarks>
        private void Datagrid_OnMouseMove(object parameter)
        {
            if (parameter is not MouseEventArgs e)
                return;

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (e.Source is not DataGrid dataGrid)
                    return;

                var dataGridRow = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
                if (dataGridRow == null)
                    return;

                var data = (ContainerEntry)dataGrid.ItemContainerGenerator.ItemFromContainer(dataGridRow);
                if (data == null)
                    return;

                if (!data.Equals(SelectedUnassignedEntry) && !data.Equals(SelectedFilteredEntry))
                    return;

                var dataObj = new DataObject(data);
                dataObj.SetData("DragSource", dataGrid);
                DragDrop.DoDragDrop(dataGrid, dataObj, DragDropEffects.Move);
            }
        }

        /// <summary>
        /// Handles the drag-over event during a drag-and-drop operation.
        /// </summary>
        /// <param name="e">The DragEventArgs containing the event data.</param>
        /// <remarks>
        /// This method sets the effect of the drag-and-drop operation to indicate a move action
        /// and marks the event as handled to prevent further processing.
        /// </remarks>
        private void ContainerGrid_OnDragOver(object parameter)
        {
            if (parameter is DragEventArgs e)
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Handles the drop event for a drag-and-drop operation involving a DataGrid.
        /// </summary>
        /// <param name="e">The DragEventArgs containing the event data.</param>
        private void Datagrid_OnDrop(object parameter)
        {
            if (parameter is not DragEventArgs e)
                return;
            if (e.Source is not DataGrid DropDataGrid)
                return;
            if (e.Data.GetData(typeof(ContainerEntry)) is not ContainerEntry data)
                return;
            if (e.Data.GetData("DragSource") is not DataGrid)
                return;

            // Get the target row
            if (DropDataGrid.ItemsSource == FilteredEntries)
            {
                RunWorkspaceAction(
                    "Signal als gefiltert einordnen",
                    () =>
                    {
                        GenerationWorkspaceEditor.MoveToFiltered(
                            data,
                            ContainerList,
                            UnassignedEntries,
                            FilteredEntries);
                        MarkAsManual(data, "Manuell als gefiltert eingeordnet.");
                    },
                    $"Signal „{data.Signal}“");
            }
            else if (DropDataGrid.ItemsSource == UnassignedEntries)
            {
                RunWorkspaceAction(
                    "Signal als nicht zugeordnet einordnen",
                    () =>
                    {
                        GenerationWorkspaceEditor.MoveToUnassigned(
                            data,
                            data.Signal,
                            ContainerList,
                            UnassignedEntries,
                            FilteredEntries);
                        MarkAsManual(data, "Manuell als nicht zugeordnet eingeordnet.");
                    },
                    $"Signal „{data.Signal}“");
            }
            else
            {
                var targetRow = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
                if (targetRow == null)
                    return;

                var targetDescription = targetRow.Item is ContainerData target
                    ? $"Container „{target.Component}“ ({target.Type})"
                    : "neuer Container";
                RunWorkspaceAction(
                    "Signal einem Container zuordnen",
                    () => MoveData(targetRow, data),
                    $"Signal „{data.Signal}“ → {targetDescription}");
            }
        }


        /// <summary>
        ///  Logic to move data between collections (e.g. after a DragAndDrop operation).
        /// </summary>
        /// <param name="targetRow">Row in the target grid.</param>
        /// <param name="data">Data entry to add.</param>
        private void MoveData(DataGridRow targetRow, ContainerEntry data)
        {
            if (targetRow.Item is ContainerData targetData)
            {
                // Quell-Container ermitteln (fuer Log: welcher Container verliert das Signal?)
                var sourceContainer = ContainerList.FirstOrDefault(c => c.DataList.Contains(data));

                if (sourceContainer == targetData)
                    return;

                if (sourceContainer != null && sourceContainer != targetData)
                {
                    _actionLogger.LogRemoved(sourceContainer.Component, sourceContainer.Type, data);
                }

                GenerationWorkspaceEditor.MoveToContainer(
                    data,
                    targetData,
                    ContainerList,
                    UnassignedEntries,
                    FilteredEntries);
                AttachSlotChangedHandler(targetData, data);
                MarkAsManual(data, "Manuell einem Container zugeordnet.");
                targetData.ManuallyChecked = false;

                // ActionLog: Signal wurde von sourceContainer nach targetData verschoben
                _actionLogger.LogAdded(
                    containerName: targetData.Component,
                    componentType: targetData.Type,
                    entry: data,
                    ruleSuggestion: data.Slot,
                    mlTop1: null,
                    mlScore: null);

            }
            else if (targetRow.Item == CollectionView.NewItemPlaceholder)
            {
                ContainerData CreatedContainerData = new ContainerData();
                GenerationWorkspaceEditor.MoveToContainer(
                    data,
                    CreatedContainerData,
                    ContainerList,
                    UnassignedEntries,
                    FilteredEntries);
                AttachSlotChangedHandler(CreatedContainerData, data);
                MarkAsManual(data, "Manuell einem neuen Container zugeordnet.");
                CreatedContainerData.ManuallyChecked = false;

                _actionLogger.LogAdded(
                    containerName: CreatedContainerData.Component,
                    componentType: CreatedContainerData.Type,
                    entry: data,
                    ruleSuggestion: data.Slot,
                    mlTop1: null,
                    mlScore: null);

            }
        }

        private void ContainerList_CollectionChanged(
            object? sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (ContainerData container in e.OldItems)
                    UnsubscribeContainer(container);
            }

            if (e.NewItems is not null)
            {
                foreach (ContainerData container in e.NewItems)
                    SubscribeContainer(container);
            }

            UpdateUICount(sender, e);
        }

        private void SubscribeContainer(ContainerData container)
        {
            if (!_containerChangingHandlers.ContainsKey(container))
            {
                EventHandler<WorkspaceValueChangingEventArgs> changingHandler =
                    (sender, args) => OnWorkspaceValueChanging(sender, args);
                _containerChangingHandlers[container] = changingHandler;
                container.WorkspaceValueChanging += changingHandler;
            }

            if (!_containerPropertyChangedHandlers.ContainsKey(container))
            {
                PropertyChangedEventHandler propertyHandler = (_, args) =>
                {
                    if (args.PropertyName is nameof(ContainerData.RequiresReview) or
                        nameof(ContainerData.HasDetectedChanges) or
                        nameof(ContainerData.IsValid) or
                        nameof(ContainerData.ManuallyChecked))
                    {
                        if (!_suppressUndoCapture &&
                            SelectedReviewFilter?.Value != WorkspaceReviewFilter.All)
                        {
                            // A container can emit several dependent review
                            // notifications for one edit. Debouncing collapses
                            // them into one container-view refresh.
                            _debounceTimerContainerData.Stop();
                            _debounceTimerContainerData.Start();
                        }
                    }
                };
                _containerPropertyChangedHandlers[container] = propertyHandler;
                container.PropertyChanged += propertyHandler;
            }

            if (!_containerEntryCollectionHandlers.ContainsKey(container))
            {
                System.Collections.Specialized.NotifyCollectionChangedEventHandler collectionHandler =
                    (_, args) =>
                    {
                        if (args.NewItems is not null)
                        {
                            foreach (ContainerEntry entry in args.NewItems)
                                AttachSlotChangedHandler(container, entry);
                        }
                    };
                _containerEntryCollectionHandlers[container] = collectionHandler;
                container.DataList.CollectionChanged += collectionHandler;
            }

            foreach (var entry in container.DataList)
                AttachSlotChangedHandler(container, entry);
        }

        private void UnsubscribeContainer(ContainerData container)
        {
            if (_containerChangingHandlers.Remove(container, out var changingHandler))
                container.WorkspaceValueChanging -= changingHandler;
            if (_containerPropertyChangedHandlers.Remove(container, out var propertyHandler))
                container.PropertyChanged -= propertyHandler;
            if (_containerEntryCollectionHandlers.Remove(container, out var collectionHandler))
                container.DataList.CollectionChanged -= collectionHandler;
        }

        private void OnWorkspaceValueChanging(
            object? sender,
            WorkspaceValueChangingEventArgs args)
        {
            var isUserChange = !_suppressUndoCapture && !_isRestoringWorkspace;
            var description = args.PropertyName switch
            {
                nameof(ContainerEntry.Signal) => "Signalname ändern",
                nameof(ContainerEntry.Slot) => "Slot ändern",
                nameof(ContainerEntry.ID) => "Quell-ID ändern",
                nameof(ContainerEntry.Address) => "Signaladresse ändern",
                nameof(ContainerEntry.DataType) => "Datentyp ändern",
                nameof(ContainerEntry.Note) => "Notiz ändern",
                nameof(ContainerData.Component) => "Containername ändern",
                nameof(ContainerData.Type) => "Containertyp ändern",
                nameof(ContainerData.ManuallyChecked) => "Prüfstatus ändern",
                _ => "Arbeitsstand ändern"
            };

            CaptureUndo(description);
            if (isUserChange)
            {
                var details =
                    args.PropertyName == nameof(ContainerEntry.Signal) &&
                    string.IsNullOrWhiteSpace(args.NewValue?.ToString())
                        ? $"Die Zuordnung von Signal „{FormatActivityValue(args.PreviousValue)}“ " +
                          "wird entfernt; der Signalname bleibt in „Nicht zugeordnet“ erhalten."
                        : $"{args.PropertyName}: „{FormatActivityValue(args.PreviousValue)}“ → " +
                          $"„{FormatActivityValue(args.NewValue)}“";
                AddActivity("Direkte Änderung", description, details);
            }

            var previousSuppression = _suppressUndoCapture;
            _suppressUndoCapture = true;
            try
            {
                if (sender is ContainerEntry entry)
                {
                    MarkAsManual(entry, $"{description}.");
                    var owner = ContainerList.FirstOrDefault(
                        container => container.DataList.Contains(entry));
                    if (owner is not null)
                        owner.ManuallyChecked = false;
                }
                else if (sender is ContainerData container &&
                         args.PropertyName != nameof(ContainerData.ManuallyChecked))
                {
                    container.ManuallyChecked = false;
                    foreach (var containerEntry in container.DataList)
                        MarkAsManual(containerEntry, $"{description}.");
                }
            }
            finally
            {
                _suppressUndoCapture = previousSuppression;
            }
        }

        private static string FormatActivityValue(object? value)
        {
            var text = value?.ToString();
            return string.IsNullOrWhiteSpace(text) ? "leer" : text.Trim();
        }

        /// <summary>
        /// Haengt den SlotChanged-Handler an einen ContainerEntry.
        /// Wird aufgerufen wenn ein Entry einem Container hinzugefuegt wird.
        /// Verhindert Mehrfach-Registrierung durch Abmelden vor Anmelden.
        /// </summary>
        private void AttachSlotChangedHandler(ContainerData container, ContainerEntry entry)
        {
            if (_slotChangedHandlers.TryGetValue(entry, out var existingHandler))
                entry.SlotChanged -= existingHandler;

            _lastKnownSlot[entry] = entry.Slot;
            EventHandler handler = (_, _) => OnEntrySlotChanged(container, entry);
            _slotChangedHandlers[entry] = handler;
            entry.SlotChanged += handler;

            if (_signalClearedHandlers.TryGetValue(entry, out var existingSignalHandler))
                entry.SignalCleared -= existingSignalHandler;

            EventHandler<SignalClearedEventArgs> signalHandler =
                (_, args) => OnEntrySignalCleared(container, entry, args);
            _signalClearedHandlers[entry] = signalHandler;
            entry.SignalCleared += signalHandler;

            if (_entryChangingHandlers.TryGetValue(entry, out var existingChangingHandler))
                entry.WorkspaceValueChanging -= existingChangingHandler;

            EventHandler<WorkspaceValueChangingEventArgs> changingHandler =
                (sender, args) => OnWorkspaceValueChanging(sender, args);
            _entryChangingHandlers[entry] = changingHandler;
            entry.WorkspaceValueChanging += changingHandler;
        }

        private void OnEntrySignalCleared(
            ContainerData container,
            ContainerEntry entry,
            SignalClearedEventArgs args)
        {
            var previousSuppression = _suppressUndoCapture;
            _suppressUndoCapture = true;
            try
            {
            if (_slotChangedHandlers.TryGetValue(entry, out var slotHandler))
            {
                entry.SlotChanged -= slotHandler;
                _slotChangedHandlers.Remove(entry);
            }

            if (_signalClearedHandlers.TryGetValue(entry, out var signalHandler))
            {
                entry.SignalCleared -= signalHandler;
                _signalClearedHandlers.Remove(entry);
            }

            _lastKnownSlot.Remove(entry);

            var result = GenerationWorkspaceEditor.MoveToUnassigned(
                entry,
                args.PreviousSignal,
                ContainerList,
                UnassignedEntries,
                FilteredEntries);
            MarkAsManual(
                entry,
                "Signalzuordnung wurde durch Leeren der Signalzelle entfernt.");
            container.ManuallyChecked = false;
            Logger.Info(
                "Signal {EntryId} was moved from container {Container} to unassigned; {DuplicateCount} duplicates removed.",
                entry.ID,
                container.Component,
                result.RemovedDuplicateOccurrences);
            StatusText =
                $"Signal „{entry.Signal}“ wurde aus dem Container entfernt und einmalig als nicht zugeordnet eingetragen.";
            }
            finally
            {
                _suppressUndoCapture = previousSuppression;
            }
        }

        private void OnEntrySlotChanged(ContainerData container, ContainerEntry entry)
        {
            var previousSuppression = _suppressUndoCapture;
            _suppressUndoCapture = true;
            try
            {
            _lastKnownSlot.TryGetValue(entry, out var oldSlot);
            _lastKnownSlot[entry] = entry.Slot;

            MarkAsManual(entry, "Slot wurde manuell geändert.");
            container.ManuallyChecked = false;
            container.RefreshReimportStatus();

            if (!string.IsNullOrEmpty(entry.Slot) && oldSlot != entry.Slot)
            {
                _actionLogger.LogSlotChange(
                    containerName: container.Component,
                    componentType: container.Type,
                    entry: entry,
                    oldSlot: oldSlot ?? "",
                    mlTop1: null,
                    mlScore: null);
            }
            }
            finally
            {
                _suppressUndoCapture = previousSuppression;
            }
        }

        protected override void ReattachAllSlotChangedHandlers()
        {
            foreach (var container in ContainerList)
            {
                foreach (var entry in container.DataList)
                    AttachSlotChangedHandler(container, entry);
            }
        }

        private static void MarkAsManual(ContainerEntry entry, string message)
        {
            entry.IsManuallyEdited = true;
            entry.ReviewState = ContainerEntryReviewState.ManuallyEdited;
            entry.ReviewMessage = message;
        }
    }
}
