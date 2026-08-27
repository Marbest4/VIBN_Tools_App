using System.Collections.ObjectModel;
using System.Windows.Input;
using VIBN_Tools.Core.ViCo;
using VIBN_Tools.GlobalClasses;
using VIBN_Tools.SpecialDevices;
using VIBN_Tools.Tia.Client;
using VIBN_Tools.Tia.Contracts;
using VIBN_Tools.Settings;
using static VIBN_Tools.SpecialDevices.DeviceCatalog;

namespace VIBN_Tools.Application.VM;

/// <summary>
/// Coordinates manual Special Device creation and the TIA-backed hardware
/// staging workflow. TIA data is read through the isolated bridge; actual FEE
/// creation starts only after the user has reviewed the generated queue.
/// </summary>
public sealed class SpecialDevicePageVM : MvvmBase, IAsyncDisposable
{
    private readonly ITiaBridgeClient _tiaClient;
    private readonly ITiaHardwareMappingStore _hardwareMappingStore;
    private readonly IApplicationLog _log;
    private CancellationTokenSource? _tiaOperationCancellation;
    private bool _isBusyTia;
    private bool _isDisconnectingTia;
    private bool _isTiaAttached;
    private bool _isBusyCreateDevices;
    private DeviceManufacturer? _selectedManufacturer;
    private object? _selectedDevice;
    private RobotType? _selectedRobotType;
    private string _devicePrefix = string.Empty;
    private int _deviceAddressInput;
    private int _deviceAddressOutput;
    private bool _isEnabledDevicePrefix = true;
    private bool _isEnabledDeviceAddress = true;
    private bool _showRobotType;
    private string? _selectedTiaVersion;
    private TiaPlcInfo? _selectedTiaPlc;
    private int _selectedDeviceIndex = -1;
    private string _statusText = "Special Devices sind bereit.";

    public SpecialDevicePageVM(
        ITiaBridgeClient tiaClient,
        IReadOnlyList<string> installedTiaVersions,
        ITiaHardwareMappingStore hardwareMappingStore,
        IApplicationLog? log = null)
    {
        _tiaClient = tiaClient ?? throw new ArgumentNullException(nameof(tiaClient));
        _hardwareMappingStore = hardwareMappingStore ?? throw new ArgumentNullException(nameof(hardwareMappingStore));
        _log = log ?? NullApplicationLog.Instance;

        foreach (var version in installedTiaVersions)
            InstalledTiaVersions.Add(version);
        SelectedTiaVersion = InstalledTiaVersions.FirstOrDefault();

        AddSpecialDeviceCommand = GetCommandBinding(AddSpecialDevice);
        ConnectTiaCommand = GetCommandBindingAsync(ConnectTiaAsync);
        DisconnectTiaCommand = GetCommandBindingAsync(DisconnectTiaAsync);
        SelectTiaPlcCommand = GetCommandBindingAsync(SelectTiaPlcAsync);
        ReadTiaHardwareCommand = GetCommandBindingAsync(ReadTiaHardwareAsync);
        SaveTiaHardwareMappingCommand = GetCommandBindingAsync(SaveTiaHardwareMappingAsync);
        AddSelectedHardwareDevicesCommand = GetCommandBinding(AddSelectedHardwareDevices);
        DeleteSelectedDevicesCommand = GetCommandBinding(DeleteSelectedDevice);
        DeleteAllDevicesCommand = GetCommandBinding(DeleteAllDevices);
        CreateSpecialDevicesCommand = GetCommandBindingAsync(CreateSpecialDevicesAsync);
        if (Connection is not null)
            Connection.PropertyChanged += OnFeeConnectionPropertyChanged;
    }

    public ObservableCollection<object> DeviceTypes { get; } = new();

    public ObservableCollection<SpecialDevice> SpecialDevices { get; } = new();

    public ObservableCollection<string> InstalledTiaVersions { get; } = new();

    public ObservableCollection<TiaPlcInfo> TiaPlcs { get; } = new();

    public ObservableCollection<TiaHardwareDeviceRowVM> TiaHardwareRows { get; } = new();

    public IReadOnlyList<SpecialDeviceLogicOption> HardwareLogicOptions => SpecialDeviceLogicOption.All;

    public IEnumerable<DeviceManufacturer> Manufacturers => Enum.GetValues<DeviceManufacturer>();

    public IEnumerable<RobotType> RobotTypes => Enum.GetValues<RobotType>();

    public ICommand AddSpecialDeviceCommand { get; }

    public ICommand ConnectTiaCommand { get; }

    public ICommand DisconnectTiaCommand { get; }

    public ICommand SelectTiaPlcCommand { get; }

    public ICommand ReadTiaHardwareCommand { get; }

    public ICommand SaveTiaHardwareMappingCommand { get; }

    public ICommand AddSelectedHardwareDevicesCommand { get; }

    public ICommand DeleteSelectedDevicesCommand { get; }

    public ICommand DeleteAllDevicesCommand { get; }

    public ICommand CreateSpecialDevicesCommand { get; }

    public FeeConnectionService? Connection => Services.Connection;

    public DeviceManufacturer? SelectedManufacturer
    {
        get => _selectedManufacturer;
        set
        {
            if (_selectedManufacturer == value)
                return;
            _selectedManufacturer = value;
            OnPropertyChanged();
            LoadDeviceTypesForManufacturer();
        }
    }

    public object? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (ReferenceEquals(_selectedDevice, value))
                return;
            _selectedDevice = value;
            OnPropertyChanged();
            var isSimMode = value is GrobDeviceTypes.SimModeSiemens;
            IsEnabledDevicePrefix = !isSimMode;
            IsEnabledDeviceAddress = !isSimMode;
            if (isSimMode)
                DevicePrefix = "SimMode";
            ShowRobotType = value is not null && RobotTypeDevices.Contains(value);
        }
    }

    public RobotType? SelectedRobotType
    {
        get => _selectedRobotType;
        set
        {
            _selectedRobotType = value;
            OnPropertyChanged();
        }
    }

    public bool ShowRobotType
    {
        get => _showRobotType;
        private set
        {
            _showRobotType = value;
            OnPropertyChanged();
        }
    }

    public string DevicePrefix
    {
        get => _devicePrefix;
        set
        {
            _devicePrefix = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public bool IsEnabledDevicePrefix
    {
        get => _isEnabledDevicePrefix;
        private set
        {
            _isEnabledDevicePrefix = value;
            OnPropertyChanged();
        }
    }

    public int DeviceAddressInput
    {
        get => _deviceAddressInput;
        set
        {
            _deviceAddressInput = value;
            OnPropertyChanged();
        }
    }

    public int DeviceAddressOutput
    {
        get => _deviceAddressOutput;
        set
        {
            _deviceAddressOutput = value;
            OnPropertyChanged();
        }
    }

    /// <remarks>
    /// This preserves the established manual-address convention used by the
    /// original Special Device implementation.
    /// </remarks>
    public SpecialDeviceAddresses DeviceAddresses =>
        new(DeviceAddressOutput, DeviceAddressInput);

    public bool IsEnabledDeviceAddress
    {
        get => _isEnabledDeviceAddress;
        private set
        {
            _isEnabledDeviceAddress = value;
            OnPropertyChanged();
        }
    }

    public string? SelectedTiaVersion
    {
        get => _selectedTiaVersion;
        set
        {
            if (string.Equals(_selectedTiaVersion, value, StringComparison.Ordinal))
                return;
            _selectedTiaVersion = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanConnectTia));
        }
    }

    public TiaPlcInfo? SelectedTiaPlc
    {
        get => _selectedTiaPlc;
        set
        {
            if (ReferenceEquals(_selectedTiaPlc, value))
                return;
            _selectedTiaPlc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSelectTiaPlc));
            OnPropertyChanged(nameof(CanReadTiaHardware));
        }
    }

    public bool IsBusyTia
    {
        get => _isBusyTia;
        private set
        {
            _isBusyTia = value;
            OnPropertyChanged();
            NotifyTiaCommandState();
        }
    }

    public bool CanConnectTia =>
        !IsBusyTia && !_isDisconnectingTia && !_isTiaAttached &&
        !string.IsNullOrWhiteSpace(SelectedTiaVersion);

    public bool CanDisconnectTia => IsBusyTia || _isTiaAttached || _tiaClient.IsConnected;

    public bool CanSelectTiaPlc => !IsBusyTia && _isTiaAttached && SelectedTiaPlc is not null;

    public bool CanReadTiaHardware => CanSelectTiaPlc;

    public bool IsBusyCreateDevices
    {
        get => _isBusyCreateDevices;
        private set
        {
            _isBusyCreateDevices = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanModifyDeviceQueue));
            OnPropertyChanged(nameof(CanCreateInFee));
        }
    }

    public bool CanModifyDeviceQueue => !IsBusyCreateDevices;

    public bool CanCreateInFee => CanModifyDeviceQueue && Connection?.CanUseFeeFeatures == true;

    public int SelectedDeviceIndex
    {
        get => _selectedDeviceIndex;
        set
        {
            _selectedDeviceIndex = value;
            OnPropertyChanged();
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

    public async ValueTask DisposeAsync()
    {
        if (Connection is not null)
            Connection.PropertyChanged -= OnFeeConnectionPropertyChanged;
        _tiaOperationCancellation?.Cancel();
        _tiaOperationCancellation?.Dispose();
        _tiaOperationCancellation = null;
        await _tiaClient.DisposeAsync();
    }

    private void OnFeeConnectionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(FeeConnectionService.IsConnected) or
            nameof(FeeConnectionService.CanUseFeeFeatures))
        {
            OnPropertyChanged(nameof(CanCreateInFee));
        }
    }

    private void AddSpecialDevice()
    {
        if (SelectedManufacturer is null || SelectedDevice is not Enum deviceType)
        {
            StatusText = "Für ein manuelles Gerät Hersteller und Gerätetyp auswählen.";
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(DevicePrefix))
            {
                StatusText = "Für das manuelle Gerät ist ein Präfix erforderlich.";
                return;
            }
            if (ShowRobotType && SelectedRobotType is null)
            {
                StatusText = "Für diese Logik muss ein Robotertyp ausgewählt werden.";
                return;
            }
            if (SpecialDevices.Any(device =>
                    string.Equals(device.DevicePrefix, DevicePrefix.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                StatusText = $"Das Präfix '{DevicePrefix.Trim()}' ist bereits in der Warteschlange. Bitte ein eindeutiges Präfix verwenden.";
                return;
            }

            var device = DeviceFactory.Create(
                SelectedManufacturer.Value,
                deviceType,
                DevicePrefix.Trim(),
                DeviceAddresses,
                SelectedRobotType);
            SpecialDevices.Add(device);
            StatusText = $"{DeviceCatalog.GetDisplayName(deviceType)} wurde mit der Logik '{device.DeviceLogicObject.LogicDefinitionName}' zur Warteschlange hinzugefügt.";
        }
        catch (Exception exception)
        {
            StatusText = "Manuelles Special Device konnte nicht vorbereitet werden.";
            _log.Error("Special Devices", StatusText, exception);
        }
    }

    private async Task ConnectTiaAsync()
    {
        if (IsBusyTia || string.IsNullOrWhiteSpace(SelectedTiaVersion))
            return;

        await RunTiaBusyAsync("Verbindung zu TIA Portal wird hergestellt …", async cancellationToken =>
        {
            await _tiaClient.ConnectAsync(cancellationToken);
            if (!await _tiaClient.PingAsync(cancellationToken))
                throw new InvalidOperationException("TIA Bridge antwortet nicht.");
            await _tiaClient.SelectVersionAsync(SelectedTiaVersion, cancellationToken);
            await _tiaClient.AttachAsync(cancellationToken);
            Replace(TiaPlcs, await _tiaClient.ListPlcsAsync(cancellationToken));
            SelectedTiaPlc = TiaPlcs.FirstOrDefault();
            TiaHardwareRows.Clear();
            _isTiaAttached = true;
            NotifyTiaCommandState();
            StatusText = $"Mit TIA Portal {SelectedTiaVersion} verbunden; {TiaPlcs.Count} PLC(s) gefunden.";
        });
    }

    private async Task DisconnectTiaAsync()
    {
        if (_isDisconnectingTia)
            return;

        _isDisconnectingTia = true;
        _tiaOperationCancellation?.Cancel();
        NotifyTiaCommandState();
        try
        {
            await _tiaClient.DisconnectAsync();
            ResetTiaUi();
            StatusText = "TIA-Verbindungsaufbau abgebrochen und Session getrennt.";
            _log.Information("TIA Hardware", StatusText);
        }
        catch (Exception exception)
        {
            ResetTiaUi();
            StatusText = $"TIA-Session wurde lokal zurückgesetzt; Bridge-Abschluss fehlgeschlagen: {exception.Message}";
            _log.Error("TIA Hardware", StatusText, exception);
        }
        finally
        {
            _isDisconnectingTia = false;
            NotifyTiaCommandState();
        }
    }

    private async Task SelectTiaPlcAsync()
    {
        if (SelectedTiaPlc is null)
            return;

        await RunTiaBusyAsync("PLC wird ausgewählt …", async cancellationToken =>
        {
            await _tiaClient.SelectPlcAsync(SelectedTiaPlc.Index, cancellationToken);
            TiaHardwareRows.Clear();
            StatusText = $"PLC '{SelectedTiaPlc.Name}' ist ausgewählt.";
        });
    }

    private async Task ReadTiaHardwareAsync()
    {
        if (SelectedTiaPlc is null)
        {
            StatusText = "Zuerst mit TIA verbinden und eine PLC auswählen.";
            return;
        }

        await RunTiaBusyAsync("TIA-Hardwarekonfiguration wird gelesen …", async cancellationToken =>
        {
            await _tiaClient.SelectPlcAsync(SelectedTiaPlc.Index, cancellationToken);
            var modules = await _tiaClient.ListHardwareAsync(cancellationToken);
            var savedMappings = await _hardwareMappingStore.LoadAsync(cancellationToken);
            var candidates = modules
                .Select(module => new TiaHardwareDeviceRowVM(module))
                .Where(row => row.InputByte.HasValue || row.OutputByte.HasValue || row.SelectedLogic is not null)
                .ToArray();
            var restored = 0;
            foreach (var candidate in candidates)
            {
                if (savedMappings.TryGetValue(candidate.MappingKey, out var mapping) &&
                    candidate.ApplyMapping(mapping))
                {
                    restored++;
                }
            }
            Replace(TiaHardwareRows, candidates);
            var addressed = candidates.Count(row => row.InputByte.HasValue || row.OutputByte.HasValue);
            var deviceCount = candidates
                .Select(row => row.DeviceGroupName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            StatusText = $"{modules.Count} Hardwareelement(e) traversiert; " +
                         $"{candidates.Length} relevante Modulzeile(n) in {deviceCount} Gerät(en), " +
                         $"davon {addressed} mit E-/A-Adresse und {restored} gespeicherte Zuordnung(en). " +
                         "Logik und Byteadressen prüfen, Zuordnung speichern und dann übernehmen.";
        });
    }

    private async Task SaveTiaHardwareMappingAsync()
    {
        if (TiaHardwareRows.Count == 0)
        {
            StatusText = "Es sind keine TIA-Hardwarezuordnungen zum Speichern vorhanden.";
            return;
        }

        try
        {
            await _hardwareMappingStore.SaveAsync(
                TiaHardwareRows.Select(row => row.ToMapping()).ToArray());
            foreach (var row in TiaHardwareRows)
                row.MarkConfigurationSaved();
            StatusText = $"{TiaHardwareRows.Count} TIA-Hardwarezuordnung(en) wurden lokal gespeichert.";
            _log.Information("TIA Hardware", StatusText);
        }
        catch (Exception exception)
        {
            StatusText = $"TIA-Hardwarezuordnungen konnten nicht gespeichert werden: {exception.Message}";
            _log.Error("TIA Hardware", StatusText, exception);
        }
    }

    private void AddSelectedHardwareDevices()
    {
        var errors = new List<string>();
        var added = 0;
        foreach (var row in TiaHardwareRows.Where(row => row.Include && !row.IsAdded))
        {
            if (!row.TryCreate(out var device, out var error))
            {
                if (error.Length > 0)
                    errors.Add(error);
                continue;
            }

            if (device is null)
                continue;
            var alreadyQueued = SpecialDevices.Any(existing =>
                string.Equals(existing.DevicePrefix, device.DevicePrefix, StringComparison.OrdinalIgnoreCase) &&
                Equals(existing.DeviceType, device.DeviceType) &&
                existing.DeviceAddresses == device.DeviceAddresses);
            if (alreadyQueued)
            {
                errors.Add($"{row.ModuleName}: Dieses Gerät ist bereits in der Warteschlange.");
                continue;
            }

            SpecialDevices.Add(device);
            row.IsAdded = true;
            added++;
        }

        StatusText = errors.Count == 0
            ? $"{added} TIA-Hardwareelement(e) wurden in die Warteschlange übernommen."
            : $"{added} Gerät(e) übernommen; {errors.Count} Zuordnung(en) prüfen: {string.Join(" ", errors.Take(3))}";
        if (errors.Count > 0)
            _log.Warning("Special Devices", StatusText);
    }

    private void DeleteSelectedDevice()
    {
        if (SelectedDeviceIndex is >= 0 and < int.MaxValue && SelectedDeviceIndex < SpecialDevices.Count)
            SpecialDevices.RemoveAt(SelectedDeviceIndex);
    }

    private void DeleteAllDevices()
    {
        SpecialDevices.Clear();
        foreach (var row in TiaHardwareRows)
            row.IsAdded = false;
        StatusText = "Die Special-Device-Warteschlange wurde geleert.";
    }

    private async Task CreateSpecialDevicesAsync()
    {
        if (!Connection.CanUseFeeFeatures)
        {
            StatusText = FeeConnectionService.MissingConnectionMessage;
            _log.Warning("Special Devices", StatusText);
            return;
        }

        if (IsBusyCreateDevices || SpecialDevices.Count == 0)
            return;

        IsBusyCreateDevices = true;
        var created = new List<SpecialDevice>();
        var failures = new List<string>();
        try
        {
            // FEE object creation is intentionally serialized. The underlying
            // SDK keeps a shared connection and is more reliable than an
            // unbounded parallel write burst.
            foreach (var device in SpecialDevices.ToArray())
            {
                try
                {
                    if (await device.CreateAsync())
                        created.Add(device);
                    else
                        failures.Add($"{device.DevicePrefix}: FEE hat keine erfolgreiche Erstellung bestätigt.");
                }
                catch (Exception exception)
                {
                    failures.Add($"{device.DevicePrefix}: {exception.Message}");
                    _log.Error("Special Devices", $"Gerät {device.DevicePrefix} konnte nicht erzeugt werden.", exception);
                }

                // A failed attempt can already have created partial FEE
                // objects. Stop here so later queue entries are not attempted
                // against an uncertain shared SDK state.
                if (failures.Count > 0)
                    break;
            }

            foreach (var device in created)
                SpecialDevices.Remove(device);
            StatusText = failures.Count == 0
                ? $"{created.Count} Special Device(s) wurden erstellt."
                : $"{created.Count} Gerät(e) erstellt; {failures.Count} Gerät(e) bleiben zur Prüfung in der Warteschlange.";
        }
        finally
        {
            IsBusyCreateDevices = false;
        }
    }

    private async Task RunTiaBusyAsync(
        string initialStatus,
        Func<CancellationToken, Task> action)
    {
        if (IsBusyTia)
            return;

        var cancellation = new CancellationTokenSource();
        _tiaOperationCancellation = cancellation;
        IsBusyTia = true;
        StatusText = initialStatus;
        try
        {
            await action(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_isDisconnectingTia)
            {
                StatusText = "TIA-Hardwarevorgang wurde abgebrochen.";
                _log.Information("TIA Hardware", StatusText);
            }
        }
        catch (Exception exception)
        {
            StatusText = $"TIA-Hardwarevorgang fehlgeschlagen: {exception.Message}";
            _log.Error("TIA Hardware", StatusText, exception);
        }
        finally
        {
            if (ReferenceEquals(_tiaOperationCancellation, cancellation))
                _tiaOperationCancellation = null;
            cancellation.Dispose();
            IsBusyTia = false;
        }
    }

    private void ResetTiaUi()
    {
        _isTiaAttached = false;
        SelectedTiaPlc = null;
        TiaPlcs.Clear();
        TiaHardwareRows.Clear();
    }

    private void NotifyTiaCommandState()
    {
        OnPropertyChanged(nameof(CanConnectTia));
        OnPropertyChanged(nameof(CanDisconnectTia));
        OnPropertyChanged(nameof(CanSelectTiaPlc));
        OnPropertyChanged(nameof(CanReadTiaHardware));
    }

    private void LoadDeviceTypesForManufacturer()
    {
        DeviceTypes.Clear();
        SelectedDevice = null;
        if (SelectedManufacturer is null)
            return;

        foreach (var deviceType in Enum.GetValues(DeviceCatalog.DeviceTypeEnums[SelectedManufacturer.Value]))
            DeviceTypes.Add(deviceType!);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }

    private static readonly HashSet<object> RobotTypeDevices = new()
    {
        AtlasCopcoDeviceTypes.Sys6000_Glueing_BMW,
        AtlasCopcoDeviceTypes.Sys6000_Glueing_VASS
    };
}
