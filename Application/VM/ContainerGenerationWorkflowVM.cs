using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
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
    /// Implements import, generation, validation, persistence, reimport and
    /// undo/redo orchestration independently from WPF drag/drop handling.
    /// </summary>
    public abstract class ContainerGenerationWorkflowVM : ContainerGenerationStateVM
    {
        protected abstract void ReattachAllSlotChangedHandlers();

        protected async Task Open_InterfaceFile(object parameter)
        {
            string excelFilter = "Excel Files (*.xls;*.xlsx;*.xlsm)|*.xls;*.xlsx;*.xlsm";
            string filePath = SystemDialog.OpenSelectFileDialog(excelFilter);
            if (string.IsNullOrEmpty(filePath))
                return;

            var importMode = AskForImportMode("ZuLi");
            if (importMode == ImportMode.Cancel)
                return;

            var importedZuli = new ZuLiDefault();
            var result = await importedZuli.ReadFromFileAsync(filePath);
            if (!result.IsSuccess)
            {
                StatusText = result.ErrorMessage;
                return;
            }

            Zuli = importedZuli;
            EnsureSignalIds(Zuli.Items);
            Settings.PathZuli = filePath;
            CommitSuccessfulImport(importMode, "ZuLi");
            OnPropertyChanged(nameof(CanGenerate));
        }

        protected async Task Open_RequirementsXml(object parameter)
        {
            string xmlFilter = "XML File (*.xml)|*.xml";
            string filePath = SystemDialog.OpenSelectFileDialog(xmlFilter);
            if (string.IsNullOrEmpty(filePath))
                return;

            var importMode = AskForImportMode("Requirements-XML");
            if (importMode == ImportMode.Cancel)
                return;

            var importedRequirements = new RequirementsXml();
            var result = await importedRequirements.ReadFromFileAsync(filePath);
            if (result.IsSuccess)
            {
                RequirementsFile = importedRequirements;
                RequirementsProvider.RequirementsFile = RequirementsFile;
                Settings.PathRequirementsXml = filePath;
                ComponentTypes.Clear();
                var components = RequirementsFile.GetComponentTypes().OrderBy(x => x).ToList();
                foreach (var component in components)
                {
                    ComponentTypes.Add(component);
                }

                CommitSuccessfulImport(importMode, "Requirements-XML");
            }
            else
            {
                StatusText = result.ErrorMessage;
                return;
            }

            OnPropertyChanged(nameof(CanGenerate));
        }

        /// <summary>
        /// Loads a saved UI settings XML and initializes the corresponding data immediately.
        /// </summary>
        protected async Task Load_Settings(object parameter)
        {
            string xmlFilter = "XML File (*.xml)|*.xml";
            string filePath = SystemDialog.OpenSelectFileDialog(xmlFilter);
            if (string.IsNullOrEmpty(filePath))
                return;

            var importMode = AskForImportMode("Projekt-Einstellungen");
            if (importMode == ImportMode.Cancel)
                return;

            var resultSettings = XmlHandler.Read(filePath);
            if (!resultSettings.IsSuccess)
            {
                StatusText = resultSettings.ErrorMessage;
                return;
            }

            var previousSettings = Settings.GetSettings();
            if (!Settings.SetSettings(resultSettings.Value))
            {
                StatusText = "Die Projekteinstellungen konnten nicht übernommen werden.";
                return;
            }

            var importedZuli = new ZuLiDefault();
            var importedRequirements = new RequirementsXml();

            var resultZuli = await importedZuli.ReadFromFileAsync(Settings.PathZuli);
            if (!resultZuli.IsSuccess)
            {
                Settings.SetSettings(previousSettings);
                StatusText = resultZuli.ErrorMessage;
                return;
            }

            var resultRequirements = await importedRequirements.ReadFromFileAsync(Settings.PathRequirementsXml);
            if (!resultRequirements.IsSuccess)
            {
                Settings.SetSettings(previousSettings);
                StatusText = resultRequirements.ErrorMessage;
                return;
            }

            Zuli = importedZuli;
            EnsureSignalIds(Zuli.Items);
            RequirementsFile = importedRequirements;
            RequirementsProvider.RequirementsFile = RequirementsFile;

            ComponentTypes.Clear();
            foreach (var component in RequirementsFile.GetComponentTypes().OrderBy(x => x))
                ComponentTypes.Add(component);

            CommitSuccessfulImport(importMode, "Projekt-Einstellungen");
            OnPropertyChanged(nameof(CanGenerate));
        }

        /// <summary>
        /// Exports all UI settings required for a container generation to a XML file.  
        /// </summary>
        protected void Save_Settings(object parameter)
        {
            string xmlFilter = "XML File (*.xml)|*.xml";
            string fileName = $"{Path.GetFileNameWithoutExtension(Settings.PathZuli)}.xml";
            string filePath = SystemDialog.OpenSaveFileDialog(xmlFilter, fileName, "Settings");
            if (!string.IsNullOrEmpty(filePath))
            {
                var result = XmlHandler.Write(Settings.GetSettings(), filePath);
                if (!result.IsSuccess)
                {
                    StatusText = result.ErrorMessage;
                }
                else
                {
                    AddActivity(
                        "Datei",
                        "Generator-Einstellungen gespeichert",
                        filePath);
                }
            }
        }

        /// <summary>
        /// Generates container data based on grouping and substitution rules.
        /// This method clears existing data, generates grouping and substitution rules, and uses these rules to generate container data asynchronously.
        /// It then validates the generated containers and updates the unassigned entries.
        /// </summary>
        protected async Task Generate_Containers(object parameter)
        {
            IsBusyGenerateContainers = true;
            try
            {
                var resultGrouping = Settings.GenerateGroupingRules();
                var resultSubstitution = Settings.GenerateSubstitutionRule();

                if (resultGrouping.IsSuccess)
                {
                    if (resultSubstitution.IsSuccess)
                    {
                        var generationResult = await ContainerGenerator.GenerateAsync(
                            new ContainerGenerationRequest(
                                Zuli.Items,
                                RequirementsFile.Document,
                                resultGrouping.Value,
                                resultSubstitution.Value,
                                ContainerGenerator.IgnoreCase,
                                ContainerGenerator.UseFilterList));

                        var generatedContainers = ContainerData.FromList(
                            generationResult.Containers.ToList());
                        var generatedUnassigned = generationResult.UnassignedSignals.ToList();
                        var generatedFiltered = generationResult.FilteredSignals.ToList();
                        EnsureSignalIds(
                            generatedContainers.SelectMany(container => container.DataList)
                                .Concat(generatedUnassigned)
                                .Concat(generatedFiltered));

                        foreach (var container in generatedContainers)
                            ConfigureContainer(container);

                        string completionStatus;

                        if (_pendingReimportSnapshot is not null)
                        {
                            var summary = GenerationWorkspaceReconciler.Reconcile(
                                _pendingReimportSnapshot,
                                generatedContainers,
                                generatedUnassigned,
                                generatedFiltered,
                                RequirementsFile);

                            foreach (var container in generatedContainers)
                                ConfigureContainer(container);

                            if (summary.Differences.Count > 0)
                            {
                                _pendingGeneratedContainers = generatedContainers;
                                _pendingGeneratedUnassigned = generatedUnassigned;
                                _pendingGeneratedFiltered = generatedFiltered;
                                _pendingReimportSummary = summary;

                                PendingReimportChanges.Clear();
                                foreach (var difference in summary.Differences)
                                {
                                    PendingReimportChanges.Add(difference);
                                    difference.PropertyChanged +=
                                        PendingReimportChange_PropertyChanged;
                                }

                                OnPropertyChanged(nameof(HasPendingReimportChanges));
                                OnPropertyChanged(nameof(ShowReimportNotice));
                                OnPropertyChanged(nameof(PendingReimportSelectionSummary));
                                ReimportNotice =
                                    $"Reimport-Vorschau erzeugt: {summary.Differences.Count} Unterschiede. " +
                                    "Der sichtbare Arbeitsstand wurde noch nicht verändert.";
                                AddActivity(
                                    "Reimport",
                                    "Vergleich erzeugt",
                                    $"{summary.Differences.Count} Unterschiede erkannt; " +
                                    $"{summary.PreservedAssignments} unveränderte Zuordnungen erkannt. " +
                                    "Noch keine Änderung am Arbeitsstand.");
                                StatusText =
                                    $"{summary.Differences.Count} Änderungen wurden erkannt. " +
                                    "Bitte jede Änderung prüfen und anschließend „Auswahl anwenden“ wählen. " +
                                    "Der bisherige Arbeitsstand bleibt bis dahin unverändert.";
                                WasGenerated = false;
                                return;
                            }

                            completionStatus = summary.ToStatusText();
                            ReimportNotice =
                                "Reimport geprüft: Es wurden keine einzeln zu entscheidenden fachlichen Unterschiede erkannt.";
                            AddActivity(
                                "Reimport",
                                "Ohne Unterschiede übernommen",
                                summary.ToStatusText());
                        }
                        else
                        {
                            ReimportNotice = string.Empty;
                            completionStatus =
                                $"Generierung abgeschlossen: {generationResult.Statistics.GeneratedContainers} Container, " +
                                $"{generationResult.Statistics.MatchedSignals} zugeordnet, " +
                                $"{generationResult.Statistics.UnassignedSignals} nicht zugeordnet.";
                        }

                        // Generation and reconciliation operate on local collections.
                        // Only a completely successful result replaces the visible work.
                        ReplaceWorkspace(
                            generatedContainers,
                            generatedUnassigned,
                            generatedFiltered);
                        ReattachAllSlotChangedHandlers();
                        _pendingReimportSnapshot = null;
                        StatusText = completionStatus;
                        WasGenerated = true;
                        AddActivity(
                            "Generierung",
                            "Arbeitsstand erzeugt",
                            completionStatus);

                    }
                    else
                    {
                        StatusText = resultSubstitution.ErrorMessage;
                        WasGenerated = false;
                    }
                }
                else
                {
                    StatusText = resultGrouping.ErrorMessage;
                    WasGenerated = false;
                }
            }
            catch (RegexMatchTimeoutException ex)
            {
                Logger.Error(ex, "Regex timeout during container generation.");
                StatusText =
                    "Die Generierung wurde abgebrochen, weil ein regulärer Ausdruck zu lange benötigt. " +
                    "Bitte Regex vereinfachen oder genauer eingrenzen. Der bisherige Arbeitsstand blieb erhalten.";
                WasGenerated = false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Container generation failed.");
                StatusText =
                    $"Die Generierung konnte nicht abgeschlossen werden: {ex.Message}. " +
                    "Der bisherige Arbeitsstand blieb erhalten.";
                WasGenerated = false;
            }
            finally
            {
                IsBusyGenerateContainers = false;
            }
        }

        /// <summary>
        /// Exports the container data to an XML file.
        /// This method opens a save file dialog, converts the container list to a list of component containers,
        /// adds unassigned entries, and writes the data to an XML file.
        /// </summary>
        protected void Export_Containers(object parameter)
        {
            var summary = CreateValidationSummary();
            StatusText = summary.ToStatusText();
            var confirmation = MessageBox.Show(
                summary.ToDisplayText() +
                Environment.NewLine +
                Environment.NewLine +
                (summary.HasWarnings
                    ? "Es bestehen Prüfhinweise. Soll trotzdem exportiert werden?"
                    : "Die Prüfung war ohne Hinweis. Soll jetzt exportiert werden?"),
                "Prüfzusammenfassung vor Export",
                MessageBoxButton.YesNo,
                summary.HasWarnings ? MessageBoxImage.Warning : MessageBoxImage.Information);

            if (confirmation != MessageBoxResult.Yes)
            {
                AddActivity(
                    "Prüfung",
                    "Export nach Prüfung abgebrochen",
                    summary.ToStatusText());
                if (summary.HasWarnings)
                {
                    SelectedReviewFilter = ReviewFilterOptions.First(
                        option => option.Value == WorkspaceReviewFilter.NeedsReview);
                }
                return;
            }

            string xmlFilter = "XML File (*.xml)|*.xml";
            string fileName = $"{Path.GetFileNameWithoutExtension(Settings.PathZuli)}.xml";
            string filePath = SystemDialog.OpenSaveFileDialog(xmlFilter, fileName, "Container");
            if (!string.IsNullOrEmpty(filePath))
            {
                List<ComponentContainer> exportContainerList = ContainerData.ToComponentContainerList(ContainerList);
                // Add unknown container type with all unassigned entries
                exportContainerList.Add(new ComponentContainer { Component = "unknown", Type = "unknown", DataList = UnassignedEntries });

                Result<string> result = XmlHandler.WriteContainerXml(exportContainerList, filePath, Path.GetFileName(Settings.PathRequirementsXml), Path.GetFileName(Settings.PathZuli));
                if (!result.IsSuccess)
                {
                    StatusText = result.ErrorMessage;
                }
                else
                {
                    AddActivity(
                        "Export",
                        "Container-XML exportiert",
                        $"{filePath} | {ContainerList.Count} Container, " +
                        $"{AssignedSignals} zugeordnete und {UnassignedEntries.Count} nicht zugeordnete Signale.");
                    StatusText =
                        $"Export abgeschlossen. {summary.ToStatusText()}";
                }
            }
        }

        protected void Validate_Workspace(object parameter)
        {
            var summary = CreateValidationSummary();
            StatusText = summary.ToStatusText();
            AddActivity(
                "Prüfung",
                "Prüfzusammenfassung erstellt",
                summary.ToStatusText());
            MessageBox.Show(
                summary.ToDisplayText(),
                "Prüfzusammenfassung",
                MessageBoxButton.OK,
                summary.HasWarnings ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        protected WorkspaceValidationSummary CreateValidationSummary()
        {
            return WorkspaceValidationAnalyzer.Analyze(
                ContainerList,
                UnassignedEntries,
                FilteredEntries);
        }

        protected void Save_Data(object parameter)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "xml (*.xml)|*.xml";
            saveFileDialog.Title = "Choose save location";

            if (saveFileDialog.ShowDialog() == true)
            {
                //Get the path of specified file
                string[] FoundFiles = saveFileDialog.FileNames;
                if (FoundFiles.Length == 1)
                {
                    AddActivity(
                        "Datei",
                        "Arbeitsstand gespeichert",
                        FoundFiles[0]);
                    SavedData CreatedSaveData = new SavedData
                    {
                        ContainerList = ContainerList.ToList(),
                        FilteredEntries = FilteredEntries.ToList(),
                        UnassignedEntries = UnassignedEntries.ToList(),
                        ActivityLog = ActivityLog.ToList(),
                        FilePath = FoundFiles[0],
                    };

                    CreatedSaveData.CaptureEntryStates();
                    CreatedSaveData.SetSettings();
                }
            }

        }

        protected void Load_Data(object parameter)
        {

            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "xml (*.xml)|*.xml";
            openFileDialog.Multiselect = false;
            openFileDialog.Title = "Select the data to load";

            if (openFileDialog.ShowDialog() == true)
            {
                //Get the path of specified file
                string[] FoundFiles = openFileDialog.FileNames;
                if (FoundFiles.Length == 1)
                {
                    try
                    {
                        SavedData loadedData = SavedData.DeserializeProject(FoundFiles[0]);
                        foreach (var container in loadedData.ContainerList)
                            ConfigureContainer(container);

                        // Replace the visible workspace only after the complete file
                        // has been read and validated. A broken file therefore cannot
                        // destroy the current work.
                        if (HasWorkspaceData)
                            CaptureUndo("Gespeicherten Arbeitsstand laden");
                        ReplaceWorkspace(
                            loadedData.ContainerList,
                            loadedData.UnassignedEntries,
                            loadedData.FilteredEntries);

                        _pendingReimportSnapshot = null;
                        ClearPendingReimportResult();
                        ReattachAllSlotChangedHandlers();
                        ActivityLog.Clear();
                        foreach (var logEntry in loadedData.ActivityLog
                                     .OrderByDescending(entry => entry.Timestamp)
                                     .Take(MaximumActivityLogEntries))
                        {
                            ActivityLog.Add(logEntry);
                        }
                        NotifyActivityLogChanged();
                        AddActivity(
                            "Datei",
                            "Arbeitsstand geladen",
                            FoundFiles[0]);
                        WasGenerated = true;
                        StatusText = "Gespeicherter Arbeitsstand wurde geladen.";
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Could not load workspace from {FilePath}.", FoundFiles[0]);
                        MessageBox.Show(
                            "Die Datei konnte nicht gelesen werden. Der aktuelle Arbeitsstand wurde nicht verändert.",
                            "Arbeitsstand laden",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }

        }

        protected void Apply_ReimportSelection(object parameter)
        {
            if (_pendingReimportSummary is null ||
                _pendingGeneratedContainers is null ||
                _pendingGeneratedUnassigned is null ||
                _pendingGeneratedFiltered is null)
            {
                return;
            }

            CaptureUndo("Reimport-Auswahl anwenden");
            var decisions = PendingReimportChanges.ToList();
            var accepted = PendingReimportChanges.Count(change => change.IsAccepted);
            var rejected = PendingReimportChanges.Count - accepted;
            var newSignals = decisions.Count(
                change => change.Kind == ReimportChangeKind.NewFromSource);
            var changedSignals = decisions.Count(
                change => change.Kind == ReimportChangeKind.SourceChanged);

            _suppressUndoCapture = true;
            try
            {
                GenerationWorkspaceReconciler.ApplyDecisions(
                    _pendingReimportSummary,
                    _pendingGeneratedContainers,
                    _pendingGeneratedUnassigned,
                    _pendingGeneratedFiltered);

                foreach (var container in _pendingGeneratedContainers)
                    ConfigureContainer(container);

                ReplaceWorkspace(
                    _pendingGeneratedContainers,
                    _pendingGeneratedUnassigned,
                    _pendingGeneratedFiltered);
                ReattachAllSlotChangedHandlers();
            }
            finally
            {
                _suppressUndoCapture = false;
            }

            _pendingReimportSnapshot = null;
            ClearPendingReimportResult();
            WasGenerated = true;
            RefreshAllWorkspaceFilters();
            foreach (var decision in decisions)
            {
                AddActivity(
                    "Reimport",
                    decision.IsAccepted
                        ? $"{decision.Category} übernommen"
                        : $"{decision.Category}: bisherigen Stand beibehalten",
                    $"Signal {decision.Signal} – {decision.ExactDifference}. " +
                    $"Auswirkung: {decision.DecisionEffect}.");
            }
            AddActivity(
                "Reimport",
                "Auswahl angewendet",
                $"{accepted} Änderungen übernommen; {rejected} Änderungen nicht übernommen. " +
                $"{ContainerList.Count} Container und {AssignedSignals} zugeordnete Signale im resultierenden Arbeitsstand.");

            ReimportNotice =
                $"Arbeitsstand wurde am {DateTime.Now:dd.MM.yyyy HH:mm} durch den Reimport geändert: " +
                $"{newSignals} neue und {changedSignals} in der Quelle geänderte Signale erkannt; " +
                $"{accepted} Änderungen übernommen, {rejected} Änderungen nicht übernommen.";
            StatusText =
                $"Reimport übernommen: {newSignals} neue, {changedSignals} geänderte Signale; " +
                $"{accepted} Änderungen angenommen, " +
                $"{rejected} Änderungen verworfen beziehungsweise bisheriger Stand beibehalten.";
        }

        protected void Cancel_ReimportSelection(object parameter)
        {
            var differenceCount = PendingReimportChanges.Count;
            ClearPendingReimportResult();
            WasGenerated = false;
            ReimportNotice =
                $"Reimport-Vorschau mit {differenceCount} Unterschieden wurde verworfen; " +
                "der Arbeitsstand wurde nicht verändert.";
            AddActivity(
                "Reimport",
                "Vorschau verworfen",
                $"{differenceCount} erkannte Unterschiede; keine Änderung am Arbeitsstand.");
            StatusText =
                "Die Reimport-Vorschau wurde verworfen. Der bisherige Arbeitsstand blieb unverändert.";
        }

        protected void SetAllReimportChanges(bool isAccepted)
        {
            foreach (var change in PendingReimportChanges)
                change.IsAccepted = isAccepted;

            AddActivity(
                "Reimport",
                isAccepted ? "Alle Änderungen ausgewählt" : "Alle Änderungen abgewählt",
                $"{PendingReimportChanges.Count} Vergleichszeilen aktualisiert.");
        }

        protected void ClearPendingReimportResult()
        {
            foreach (var difference in PendingReimportChanges)
                difference.PropertyChanged -= PendingReimportChange_PropertyChanged;

            _pendingGeneratedContainers = null;
            _pendingGeneratedUnassigned = null;
            _pendingGeneratedFiltered = null;
            _pendingReimportSummary = null;
            PendingReimportChanges.Clear();
            OnPropertyChanged(nameof(HasPendingReimportChanges));
            OnPropertyChanged(nameof(ShowReimportNotice));
            OnPropertyChanged(nameof(PendingReimportSelectionSummary));
        }

        protected void PendingReimportChange_PropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ReimportDifference.IsAccepted) or
                nameof(ReimportDifference.DecisionEffect))
            {
                OnPropertyChanged(nameof(PendingReimportSelectionSummary));
            }
        }

        protected void Undo_LastAction(object parameter)
        {
            if (_undoHistory.Count == 0)
                return;

            var state = _undoHistory[^1];
            _undoHistory.RemoveAt(_undoHistory.Count - 1);
            _redoHistory.Add(
                WorkspaceUndoState.Capture(
                    state.Description,
                    ContainerList,
                    UnassignedEntries,
                    FilteredEntries));
            TrimHistory(_redoHistory);
            _isRestoringWorkspace = true;
            _suppressUndoCapture = true;
            try
            {
                ReplaceWorkspace(
                    state.Containers,
                    state.Unassigned,
                    state.Filtered);
                foreach (var container in ContainerList)
                    ConfigureContainer(container);
                ReattachAllSlotChangedHandlers();
            }
            finally
            {
                _suppressUndoCapture = false;
                _isRestoringWorkspace = false;
            }

            WasGenerated = HasWorkspaceData;
            RefreshAllWorkspaceFilters();
            NotifyUndoStateChanged();
            if (state.Description.Contains("Reimport", StringComparison.OrdinalIgnoreCase))
            {
                ReimportNotice =
                    "Die letzte Übernahme des Reimports wurde rückgängig gemacht.";
            }
            AddActivity(
                "Rückgängig",
                state.Description,
                "Der vorherige Arbeitsstand wurde vollständig wiederhergestellt.");
            StatusText = $"Rückgängig ausgeführt: {state.Description}.";
        }

        protected void Redo_LastAction(object parameter)
        {
            if (_redoHistory.Count == 0)
                return;

            var state = _redoHistory[^1];
            _redoHistory.RemoveAt(_redoHistory.Count - 1);
            _undoHistory.Add(
                WorkspaceUndoState.Capture(
                    state.Description,
                    ContainerList,
                    UnassignedEntries,
                    FilteredEntries));
            TrimHistory(_undoHistory);

            _isRestoringWorkspace = true;
            _suppressUndoCapture = true;
            try
            {
                ReplaceWorkspace(
                    state.Containers,
                    state.Unassigned,
                    state.Filtered);
                foreach (var container in ContainerList)
                    ConfigureContainer(container);
                ReattachAllSlotChangedHandlers();
            }
            finally
            {
                _suppressUndoCapture = false;
                _isRestoringWorkspace = false;
            }

            WasGenerated = HasWorkspaceData;
            RefreshAllWorkspaceFilters();
            NotifyUndoStateChanged();
            if (state.Description.Contains("Reimport", StringComparison.OrdinalIgnoreCase))
            {
                ReimportNotice =
                    "Die zuvor rückgängig gemachte Reimport-Übernahme wurde wiederholt.";
            }
            AddActivity(
                "Wiederholen",
                state.Description,
                "Der rückgängig gemachte Arbeitsstand wurde erneut angewendet.");
            StatusText = $"Wiederholen ausgeführt: {state.Description}.";
        }

        protected void CaptureUndo(string description)
        {
            if (_suppressUndoCapture || _isRestoringWorkspace)
                return;

            _undoHistory.Add(
                WorkspaceUndoState.Capture(
                    description,
                    ContainerList,
                    UnassignedEntries,
                    FilteredEntries));

            TrimHistory(_undoHistory);
            _redoHistory.Clear();

            NotifyUndoStateChanged();
        }

        protected void RunWorkspaceAction(
            string description,
            Action action,
            string? details = null)
        {
            var before =
                $"{ContainerList.Count} Container, {AssignedSignals} zugeordnet, " +
                $"{UnassignedEntries.Count} nicht zugeordnet, {FilteredEntries.Count} gefiltert";
            CaptureUndo(description);
            _suppressUndoCapture = true;
            try
            {
                action();
            }
            finally
            {
                _suppressUndoCapture = false;
            }

            RefreshAllWorkspaceFilters();
            var after =
                $"{ContainerList.Count} Container, {AssignedSignals} zugeordnet, " +
                $"{UnassignedEntries.Count} nicht zugeordnet, {FilteredEntries.Count} gefiltert";
            AddActivity(
                "Bearbeitung",
                description,
                $"{(string.IsNullOrWhiteSpace(details) ? string.Empty : details + " | ")}" +
                $"Bestand vorher: {before}; danach: {after}.");
        }

        protected void NotifyUndoStateChanged()
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(UndoDescription));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(RedoDescription));
        }

        protected static void TrimHistory(List<WorkspaceUndoState> history)
        {
            if (history.Count > MaximumUndoActions)
                history.RemoveAt(0);
        }

        protected void AddActivity(string category, string action, string details)
        {
            ActivityLog.Insert(
                0,
                new WorkspaceActivityLogEntry
                {
                    Timestamp = DateTime.Now,
                    Category = category,
                    Action = action,
                    Details = details
                });

            while (ActivityLog.Count > MaximumActivityLogEntries)
                ActivityLog.RemoveAt(ActivityLog.Count - 1);

            NotifyActivityLogChanged();
        }

        protected void Clear_ActivityLog(object parameter)
        {
            ActivityLog.Clear();
            NotifyActivityLogChanged();
            StatusText = "Aktivitätsprotokoll wurde geleert.";
        }

        protected void NotifyActivityLogChanged()
        {
            OnPropertyChanged(nameof(HasActivityLog));
            OnPropertyChanged(nameof(ActivityLogHeader));
        }

        //===========================================================================================================================
        // M E T H O D S   ( H E L P E R S )
        //===========================================================================================================================

        protected enum ImportMode
        {
            NewProject,
            Reimport,
            Cancel
        }

        protected bool HasWorkspaceData =>
            ContainerList.Count > 0 ||
            UnassignedEntries.Count > 0 ||
            FilteredEntries.Count > 0;

        protected ImportMode AskForImportMode(string sourceName)
        {
            if (!HasWorkspaceData && _pendingReimportSnapshot is null)
                return ImportMode.NewProject;

            var result = MessageBox.Show(
                $"{sourceName} einlesen:\n\n" +
                "Ja = bestehende Arbeit erneut prüfen. Die neue Datei wird eingelesen, " +
                "manuelle beziehungsweise bestehende Zuordnungen werden beim nächsten Generieren abgeglichen und gekennzeichnet.\n\n" +
                "Nein = neues Projekt. Die bisherigen Arbeitsergebnisse werden nach erfolgreichem Einlesen verworfen.\n\n" +
                "Abbrechen = nichts ändern.",
                "Importart auswählen",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            return result switch
            {
                MessageBoxResult.Yes => ImportMode.Reimport,
                MessageBoxResult.No => ImportMode.NewProject,
                _ => ImportMode.Cancel
            };
        }

        protected void CommitSuccessfulImport(ImportMode importMode, string sourceName)
        {
            _redoHistory.Clear();
            NotifyUndoStateChanged();

            if (importMode == ImportMode.Reimport)
            {
                ClearPendingReimportResult();
                _pendingReimportSnapshot ??= GenerationWorkspaceReconciler.Capture(
                    ContainerList,
                    UnassignedEntries,
                    FilteredEntries);

                ReimportNotice =
                    $"{sourceName} wurde als Reimport eingelesen. Erst die nächste Generierung erzeugt den Vergleich.";
                AddActivity(
                    "Import",
                    $"{sourceName} als Reimport eingelesen",
                    "Bestehender Arbeitsstand wurde als Vergleichsbasis gesichert.");
                StatusText =
                    $"{sourceName} wurde neu eingelesen. Beim nächsten Generieren werden " +
                    "bestehende Zuordnungen abgeglichen und Änderungen gekennzeichnet.";
            }
            else
            {
                _pendingReimportSnapshot = null;
                ClearPendingReimportResult();
                ClearData();
                _undoHistory.Clear();
                _redoHistory.Clear();
                NotifyUndoStateChanged();
                ReimportNotice = string.Empty;
                AddActivity(
                    "Import",
                    $"{sourceName} als neues Projekt eingelesen",
                    "Vorheriger Container-Arbeitsstand wurde geleert.");
                StatusText = $"{sourceName} wurde als neues Projekt eingelesen.";
            }

            WasGenerated = false;
        }

        protected void ReplaceWorkspace(
            IEnumerable<ContainerData> containers,
            IEnumerable<ContainerEntry> unassigned,
            IEnumerable<ContainerEntry> filtered)
        {
            ClearData();

            foreach (var container in containers)
            {
                EnsureSignalIds(container.DataList);
                ContainerList.Add(container);
            }

            foreach (var entry in unassigned)
            {
                entry.EnsureSignalId();
                UnassignedEntries.Add(entry);
            }

            foreach (var entry in filtered)
            {
                entry.EnsureSignalId();
                FilteredEntries.Add(entry);
            }
        }

        protected static void EnsureSignalIds(IEnumerable<ContainerEntry> entries)
        {
            foreach (var entry in entries)
                entry.EnsureSignalId();
        }

        protected void ConfigureContainer(ContainerData container)
        {
            container.Slots.Clear();
            foreach (var slot in RequirementsFile.GetSlotNames(container.Type))
                container.Slots.Add(slot);

            container.MinSignals = RequirementsFile.GetMinSignals(container.Type);
            container.MaxSignals = RequirementsFile.GetMaxSignals(container.Type);
            container.Validate();
            container.RefreshReimportStatus();
        }

        /// <summary>
        /// Clears the grid data for container as well as unassigned entries.
        /// </summary>
        protected void ClearData()
        {
            foreach (var handler in _slotChangedHandlers)
                handler.Key.SlotChanged -= handler.Value;
            foreach (var handler in _signalClearedHandlers)
                handler.Key.SignalCleared -= handler.Value;
            foreach (var handler in _entryChangingHandlers)
                handler.Key.WorkspaceValueChanging -= handler.Value;
            foreach (var handler in _containerChangingHandlers)
                handler.Key.WorkspaceValueChanging -= handler.Value;
            foreach (var handler in _containerPropertyChangedHandlers)
                handler.Key.PropertyChanged -= handler.Value;
            foreach (var handler in _containerEntryCollectionHandlers)
                handler.Key.DataList.CollectionChanged -= handler.Value;

            _slotChangedHandlers.Clear();
            _signalClearedHandlers.Clear();
            _entryChangingHandlers.Clear();
            _containerChangingHandlers.Clear();
            _containerPropertyChangedHandlers.Clear();
            _containerEntryCollectionHandlers.Clear();
            _lastKnownSlot.Clear();
            ContainerList.Clear();
            UnassignedEntries.Clear();
            FilteredEntries.Clear();
        }

        /// <summary>
        /// Set default settings to initialize the view at startup.
        /// </summary>
        protected void LoadDefaultSettings()
        {
            StatusText = "Ready to start...";
            //Settings.PathZuli = "Path to ZuLi file (.xlsx)";
            //Settings.PathRequirementsXml = "Path to AutoCreate file (.xml)";

            Settings.GroupByComponent = true;
            Settings.GroupByType = false;
            Settings.GroupById = false;
            Settings.GroupByAddress = false;

            Settings.SelectedOption = ContainerGeneration.Models.ContainerGenerationSettings.ComponentOption;
        }

        /// <summary>
        /// Deletes the specified container entry and adds it to the unassigned entries.
        /// This method finds the container that contains the entry, removes the entry from the container,
        /// and adds it to the unassigned entries list.
        /// </summary>
        /// <param name="item">The container entry to delete.</param>
    }
}
