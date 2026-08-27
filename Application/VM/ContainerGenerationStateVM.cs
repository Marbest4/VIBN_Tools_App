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
    /// Owns observable state shared by container-generation workflows and UI
    /// interaction. It deliberately contains no file IO or drag/drop logic.
    /// </summary>
    public abstract class ContainerGenerationStateVM : MvvmBase
    {
//===========================================================================================================================
        // B I N D I N G S   -   B U T T O N S   /   C O M M A N D S
        //===========================================================================================================================












        /// <summary>
        /// Validate if a container generation is possible. Causes the corresponding button to be enabled or not.
        /// </summary>
        public bool CanGenerate => Zuli.Items.Count > 0 && RequirementsFile.IsInitialized && !WasGenerated;


        protected bool _wasgenerated;
        public bool WasGenerated
        {
            get { return _wasgenerated; }
            set 
            { 
                _wasgenerated = value; 
                OnPropertyChanged(nameof(CanGenerate));
            }
        }



        protected bool _isBusyGenerateContainers;
        public bool IsBusyGenerateContainers
        {
            get { return _isBusyGenerateContainers; }
            set { _isBusyGenerateContainers = value; OnPropertyChanged(); }
        }


        /// <summary>
        /// Validate if a loading data is possible. Causes the corresponding button to be enabled or not.
        /// </summary>
        public bool CanLoadData => RequirementsFile.IsInitialized;








        //===========================================================================================================================
        // B I N D I N G S   -   D A T A G R I D   E N T R I E S
        //===========================================================================================================================

        /// <summary>
        /// Gets or sets the filtered entries list displayed on the filtered entries grid. Notifies the UI on change.
        /// </summary
        protected ObservableCollection<ContainerEntry> _filteredEntries = [];
        public ObservableCollection<ContainerEntry> FilteredEntries
        {
            get => _filteredEntries;
            set
            {
                _filteredEntries = value;
                OnPropertyChanged(nameof(FilteredEntries));
            }
        }

        /// <summary>
        /// Gets or sets the currently selected element from the unassigned entries grid. Notifies the UI on change.
        /// </summary
        protected ContainerEntry? _selectedFilteredEntry;
        public ContainerEntry? SelectedFilteredEntry
        {
            get => _selectedFilteredEntry;
            set
            {
                _selectedFilteredEntry = value;
                OnPropertyChanged(nameof(SelectedFilteredEntry));
            }
        }



        /// <summary>
        /// Gets or sets the unassigned entries list displayed on the unassigned entries grid. Notifies the UI on change.
        /// </summary
        protected ObservableCollection<ContainerEntry> _unassignedEntries = [];
        public ObservableCollection<ContainerEntry> UnassignedEntries
        {
            get => _unassignedEntries;
            set
            {
                _unassignedEntries.CollectionChanged -= UpdateUICount;
                _unassignedEntries = value;
                _unassignedEntries.CollectionChanged += UpdateUICount;
                OnPropertyChanged(nameof(UnassignedEntries));
            }
        }

        /// <summary>
        /// Gets or sets the currently selected element from the unassigned entries grid. Notifies the UI on change.
        /// </summary
        protected ContainerEntry? _selectedUnassignedEntry;
        public ContainerEntry? SelectedUnassignedEntry
        {
            get => _selectedUnassignedEntry;
            set
            {
                _selectedUnassignedEntry = value;
                OnPropertyChanged(nameof(SelectedUnassignedEntry));
            }
        }



        /// <summary>
        /// Gets or sets the container list displayed on the container grid. Notifies the UI on change.
        /// </summary
        protected ObservableCollection<ContainerData> _containerList = [];
        public ObservableCollection<ContainerData> ContainerList
        {
            get => _containerList;
            set
            {
                _containerList.CollectionChanged -= UpdateUICount;
                _containerList = value;
                _containerList.CollectionChanged += UpdateUICount;
                OnPropertyChanged(nameof(ContainerList));
            }
        }

        /// <summary>
        /// Gets or sets the currently selected element from the container grid. Notifies the UI on change.
        /// </summary
        protected List<ContainerData> _selectedContainers = [];
        public List<ContainerData> SelectedContainers
        {
            get => _selectedContainers;
            set
            {
                _selectedContainers = value;
                OnPropertyChanged(nameof(SelectedContainers));
            }
        }









        //===========================================================================================================================
        // B I N D I N G S   -   F I L T E R   T E X T
        //===========================================================================================================================


        /// <summary>
        /// Gets or sets the search text for container grid. Notifies the UI on change.
        /// </summary
        protected string _searchTextContainer = string.Empty;
        public string SearchTextContainer
        {
            get => _searchTextContainer;
            set
            {
                _searchTextContainer = value;
                OnPropertyChanged();

                // Start Debounce
                _debounceTimerContainerData.Stop();
                _debounceTimerContainerData.Start();

            }
        }

        /// <summary>
        /// Gets or sets the search text for unassigned entries grid. Notifies the UI on change.
        /// </summary
        protected string _searchTextUnassignedEntries = string.Empty;
        public string SearchTextUnassignedEntries
        {
            get => _searchTextUnassignedEntries;
            set
            {
                _searchTextUnassignedEntries = value;
                OnPropertyChanged();

                // Start Debounce
                _debounceTimerUnassignedData.Stop();
                _debounceTimerUnassignedData.Start();

            }
        }

        /// <summary>
        /// Gets or sets the search text for filtered entries grid. Notifies the UI on change.
        /// </summary
        protected string _searchTextFilteredEntries = string.Empty;
        public string SearchTextFilteredEntries
        {
            get => _searchTextFilteredEntries;
            set
            {
                _searchTextFilteredEntries = value;
                OnPropertyChanged();

                // Start Debounce
                _debounceTimerFilteredData.Stop();
                _debounceTimerFilteredData.Start();

            }
        }



        //===========================================================================================================================
        // P R O P E R T I E S   O F   V I E W - M O D E L
        //===========================================================================================================================

        protected ContainerGenerationSettings _settings = new();
        public ContainerGenerationSettings Settings
        {
            get { return _settings; }
            set
            {
                _settings = value;
                OnPropertyChanged();
            }
        }


        protected static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();



        // DispatcherTimer for filtering SimObjects with Debounce
        // ── ActionLogger: protokolliert jede Drag-and-Drop-Aktion sofort auf Disk ──
        // Speicherort: {ExeOrdner}\vibn_ai_data\actions\YYYYMMDD.jsonl
        // Die Logs werden beim naechsten Training/Check automatisch eingelesen.
        protected readonly ActionLogger _actionLogger = new ActionLogger();
        protected GenerationWorkspaceSnapshot? _pendingReimportSnapshot;
        protected readonly Dictionary<ContainerEntry, EventHandler> _slotChangedHandlers = new();
        protected readonly Dictionary<ContainerEntry, EventHandler<SignalClearedEventArgs>>
            _signalClearedHandlers = new();
        protected readonly Dictionary<ContainerEntry, EventHandler<WorkspaceValueChangingEventArgs>>
            _entryChangingHandlers = new();
        protected readonly Dictionary<ContainerData, EventHandler<WorkspaceValueChangingEventArgs>>
            _containerChangingHandlers = new();
        protected readonly Dictionary<ContainerData, PropertyChangedEventHandler>
            _containerPropertyChangedHandlers = new();
        protected readonly Dictionary<ContainerData, System.Collections.Specialized.NotifyCollectionChangedEventHandler>
            _containerEntryCollectionHandlers = new();
        protected readonly Dictionary<ContainerEntry, string> _lastKnownSlot = new();
        protected readonly List<WorkspaceUndoState> _undoHistory = [];
        protected readonly List<WorkspaceUndoState> _redoHistory = [];
        protected const int MaximumUndoActions = 20;
        protected const int MaximumActivityLogEntries = 250;
        protected bool _suppressUndoCapture;
        protected bool _isRestoringWorkspace;

        protected List<ContainerData>? _pendingGeneratedContainers;
        protected List<ContainerEntry>? _pendingGeneratedUnassigned;
        protected List<ContainerEntry>? _pendingGeneratedFiltered;
        protected ReimportSummary? _pendingReimportSummary;

        public ObservableCollection<ReimportDifference> PendingReimportChanges { get; } = [];
        public ObservableCollection<WorkspaceActivityLogEntry> ActivityLog { get; } = [];

        public bool HasPendingReimportChanges => PendingReimportChanges.Count > 0;
        public bool CanUndo => _undoHistory.Count > 0;
        public bool CanRedo => _redoHistory.Count > 0;
        public string UndoDescription =>
            CanUndo
                ? $"Rückgängig: {_undoHistory[^1].Description}"
                : "Keine Änderung zum Rückgängigmachen";
        public string RedoDescription =>
            CanRedo
                ? $"Wiederholen: {_redoHistory[^1].Description}"
                : "Keine Änderung zum Wiederholen";
        public bool HasActivityLog => ActivityLog.Count > 0;
        public string ActivityLogHeader =>
            $"Aktivitätsprotokoll ({ActivityLog.Count})";
        public string PendingReimportSelectionSummary =>
            PendingReimportChanges.Count == 0
                ? string.Empty
                : $"{PendingReimportChanges.Count} Unterschiede erkannt: " +
                  $"{PendingReimportChanges.Count(change => change.IsAccepted)} werden übernommen, " +
                  $"{PendingReimportChanges.Count(change => !change.IsAccepted)} werden nicht übernommen.";

        protected string _reimportNotice = string.Empty;
        public string ReimportNotice
        {
            get => _reimportNotice;
            protected set
            {
                if (string.Equals(_reimportNotice, value, StringComparison.Ordinal))
                    return;

                _reimportNotice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasReimportNotice));
                OnPropertyChanged(nameof(ShowReimportNotice));
            }
        }

        public bool HasReimportNotice => !string.IsNullOrWhiteSpace(ReimportNotice);
        public bool ShowReimportNotice =>
            HasReimportNotice && !HasPendingReimportChanges;

        public ObservableCollection<WorkspaceReviewFilterOption> ReviewFilterOptions { get; } =
        [
            new(WorkspaceReviewFilter.All, "Alle"),
            new(WorkspaceReviewFilter.NeedsReview, "Prüfen"),
            new(WorkspaceReviewFilter.Changed, "Geändert / neu"),
            new(WorkspaceReviewFilter.ManuallyEdited, "Manuell bearbeitet"),
            new(WorkspaceReviewFilter.Unchecked, "Nicht abgehakt"),
            new(WorkspaceReviewFilter.Invalid, "Ungültig")
        ];

        protected WorkspaceReviewFilterOption? _selectedReviewFilter;
        public WorkspaceReviewFilterOption? SelectedReviewFilter
        {
            get => _selectedReviewFilter;
            set
            {
                if (ReferenceEquals(_selectedReviewFilter, value))
                    return;

                _selectedReviewFilter = value;
                OnPropertyChanged();
                RefreshAllWorkspaceFilters();
            }
        }

        protected DispatcherTimer _debounceTimerContainerData = null!;
        protected DispatcherTimer _debounceTimerUnassignedData = null!;
        protected DispatcherTimer _debounceTimerFilteredData = null!;





        /// <summary>
        /// Gets the count of the currently assigned signals
        /// </summary>
        public int AssignedSignals
        {
            get
            {
                return ContainerList.SelectMany(x => x.DataList).Count();
            }
        }

        /// <summary>
        /// Gets the percent of signals assigned
        /// </summary>
        public double PercentComplete
        {
            get
            {
                int Signals = AssignedSignals;

                int Total = Signals + UnassignedEntries.Count;
                if (Total > 0)
                {
                    return 1.0 * Signals / Total;
                }
                else
                {
                    return 0;
                }
            }
        }


        /// <summary>
        /// Gets or sets the status text message. Notifies the UI on change.
        /// </summary
        protected string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }



        /// <summary>
        /// Collection of component types. Contains all types which were found after reading a AutoCreateFile.
        /// </summary>
        protected ObservableCollection<string> _componentTypes = [];
        public ObservableCollection<string> ComponentTypes
        {
            get => _componentTypes;
            set
            {
                _componentTypes = value;
                OnPropertyChanged();
            }
        }


        /// <summary>
        /// List of container entries read from the ZuLi.
        /// </summary>
        public IZuLiData<ContainerEntry> Zuli { get; protected set; }

        /// <summary>
        /// Instance of a AutoCreate XML.
        /// </summary>
        public IRequirementsXml RequirementsFile { get; protected set; }

        /// <summary>
        /// Module to generate container data.
        /// </summary>
        public ContainerGenerator ContainerGenerator { get; protected set; }









        //===========================================================================================================================
        // C O N S T R U C T O R
        //===========================================================================================================================

        /// <summary>
        /// Initializes a new instance of the <see cref="ContainerGenerationPageVM"/> class.
        /// This constructor sets up the default settings and initializes various components.
        /// </summary>

        protected abstract void RefreshAllWorkspaceFilters();
        protected abstract void UpdateUICount(
            object? sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e);
    }
}

