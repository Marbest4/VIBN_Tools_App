using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using VIBN_Tools.GlobalClasses;
using VIBN_Tools.Settings;
using VIBN_Tools.SpecialDevices;

namespace VIBN_Tools.Application.VM;

public sealed class Fee2SpecialDevicesPageVM : MvvmBase
{
    private readonly Fee2SpecialDevicesService _service;
    private readonly FeeConnectionService _connection;
    private Fee2SpecialDeviceRoot? _selectedRoot;
    private bool _isBusy;
    private string _statusText = "FEE verbinden und erzeugte Special-Device-Roots einlesen.";

    public Fee2SpecialDevicesPageVM()
        : this(new Fee2SpecialDevicesService(), Services.Connection ?? new FeeConnectionService())
    {
    }

    internal Fee2SpecialDevicesPageVM(
        Fee2SpecialDevicesService service,
        FeeConnectionService connection)
    {
        _service = service;
        _connection = connection;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => CanRefresh);
        ExportCommand = new AsyncCommand(ExportAsync, () => CanExport);
        _connection.PropertyChanged += OnConnectionPropertyChanged;
    }

    public ObservableCollection<Fee2SpecialDeviceRoot> Roots { get; } = new();
    public ObservableCollection<string> Issues { get; } = new();
    public ICommand RefreshCommand { get; }
    public ICommand ExportCommand { get; }
    public FeeConnectionService Connection => _connection;

    public Fee2SpecialDeviceRoot? SelectedRoot
    {
        get => _selectedRoot;
        set
        {
            if (ReferenceEquals(_selectedRoot, value))
                return;
            _selectedRoot = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanExport));
            OnPropertyChanged(nameof(SelectionSummary));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
                return;
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRefresh));
            OnPropertyChanged(nameof(CanExport));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool CanRefresh => !IsBusy && Connection.CanUseFeeFeatures;
    public bool CanExport => !IsBusy && SelectedRoot is not null;
    public string RefreshUnavailableReason => Connection.CanUseFeeFeatures
        ? (IsBusy ? "Ein FEE2SpecialDevices-Vorgang läuft bereits." : string.Empty)
        : Connection.UnavailableReason;
    public string ExportUnavailableReason => SelectedRoot is null
        ? "Zuerst einen durch SpecialDevices2FEE erzeugten Root auswählen."
        : IsBusy ? "Ein FEE2SpecialDevices-Vorgang läuft bereits." : string.Empty;
    public string SelectionSummary => SelectedRoot is null
        ? "Kein Root ausgewählt."
        : $"{SelectedRoot.Snapshot.Manufacturer} / {SelectedRoot.Snapshot.DeviceType}; " +
          $"{SelectedRoot.Snapshot.Signals.Count} Signale, {SelectedRoot.UpdatedSignalCount} aktuell, " +
          $"{SelectedRoot.MissingSignalCount} fehlend.";

    private async Task RefreshAsync()
    {
        if (!CanRefresh)
        {
            StatusText = RefreshUnavailableReason;
            return;
        }
        IsBusy = true;
        try
        {
            var result = await _service.DiscoverAsync();
            Roots.Clear();
            foreach (var root in result.Roots)
                Roots.Add(root);
            Issues.Clear();
            foreach (var issue in result.Issues)
                Issues.Add($"{issue.RootName}: {issue.Message}".TrimStart(':', ' '));
            SelectedRoot = Roots.FirstOrDefault();
            StatusText = result.Roots.Count == 0
                ? $"Keine exportierbaren Geräte gefunden. {result.IgnoredWithoutProvenance} ältere/manuelle BasicFrames wurden ignoriert."
                : $"{result.Roots.Count} Gerät(e) gefunden; {result.IgnoredWithoutProvenance} ältere/manuelle Roots ignoriert; {result.Issues.Count} Hinweis(e).";
            ApplicationLogService.Instance.Information("FEE2SpecialDevices", StatusText);
        }
        catch (Exception exception)
        {
            StatusText = $"Special Devices konnten nicht aus FEE gelesen werden: {exception.Message}";
            ApplicationLogService.Instance.Error("FEE2SpecialDevices", StatusText, exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task ExportAsync()
    {
        if (!CanExport || SelectedRoot is null)
        {
            StatusText = ExportUnavailableReason;
            return Task.CompletedTask;
        }
        var dialog = new SaveFileDialog
        {
            Title = "Special Device aus FEE exportieren",
            Filter = "VIBN Special Device (*.specialdevice.json)|*.specialdevice.json|JSON (*.json)|*.json",
            FileName = $"{SanitizeFileName(SelectedRoot.Snapshot.Prefix)}.specialdevice.json",
            AddExtension = true,
            DefaultExt = ".specialdevice.json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true)
            return Task.CompletedTask;
        IsBusy = true;
        try
        {
            FeeSpecialDeviceProvenanceCodec.SaveAtomically(SelectedRoot.Snapshot, dialog.FileName);
            StatusText = $"Special Device wurde exportiert: {dialog.FileName}";
            ApplicationLogService.Instance.Information("FEE2SpecialDevices", StatusText);
        }
        catch (Exception exception)
        {
            StatusText = $"Special Device konnte nicht exportiert werden: {exception.Message}";
            ApplicationLogService.Instance.Error("FEE2SpecialDevices", StatusText, exception);
        }
        finally
        {
            IsBusy = false;
        }
        return Task.CompletedTask;
    }

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not nameof(FeeConnectionService.CanUseFeeFeatures) and
            not nameof(FeeConnectionService.UnavailableReason))
            return;
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(RefreshUnavailableReason));
        CommandManager.InvalidateRequerySuggested();
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var clean = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "FEE2SpecialDevices" : clean;
    }

    private sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
    {
        private bool _running;
        public bool CanExecute(object? parameter) => !_running && canExecute();
        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
                return;
            _running = true;
            CommandManager.InvalidateRequerySuggested();
            try { await execute(); }
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
