using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CP6.Client.Api;
using CP6.Client.Core;

namespace CP6.Desktop;

public partial class ShellViewModel : ObservableObject
{
    private readonly IClientSessionService _sessions;
    private readonly Cp6ApiClient _api;
    private readonly ClientUpgradeService _upgrades;
    private readonly WmsTaskService _tasks;
    private readonly WmsRealtimeService _realtime;
    private readonly ILanguageService _language;
    private readonly NativeSsoService _sso;
    private readonly DesktopDeviceActivationService _deviceActivation;
    private readonly LabelGatewayService _labelGateway;
    private readonly ClientDeviceHeartbeatLoop _heartbeat;

    [ObservableProperty] private bool isLoginVisible = true;
    [ObservableProperty] private bool isTaskVisible;
    [ObservableProperty] private bool isUpgradeVisible;
    [ObservableProperty] private bool isTwoFactorVisible;
    [ObservableProperty] private bool canUseEmailOtp;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string userName = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string tenantCode = string.Empty;
    [ObservableProperty] private string twoFactorCode = string.Empty;
    [ObservableProperty] private string twoFactorMethod = "totp";
    [ObservableProperty] private string twoFactorSetupSecret = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string connectionStatus = string.Empty;
    [ObservableProperty] private string upgradeHeading = string.Empty;
    [ObservableProperty] private string? downloadUrl;
    [ObservableProperty] private string currentVersion = string.Empty;
    [ObservableProperty] private string latestVersion = string.Empty;
    [ObservableProperty] private string minimumVersion = string.Empty;
    [ObservableProperty] private string releaseSha256 = string.Empty;
    [ObservableProperty] private bool canDownloadUpdate;
    [ObservableProperty] private MobileTask? selectedTask;
    [ObservableProperty] private ClientDevice? selectedDevice;
    [ObservableProperty] private BarcodeAlias? selectedBarcode;
    [ObservableProperty] private string assignee = string.Empty;
    [ObservableProperty] private string actionReason = string.Empty;
    [ObservableProperty] private string exceptionReasonCode = string.Empty;
    [ObservableProperty] private string activationPayload = string.Empty;
    [ObservableProperty] private string deviceActivationStatus = string.Empty;
    [ObservableProperty] private string printGatewayStatus = string.Empty;
    [ObservableProperty] private string productionSummary = string.Empty;
    [ObservableProperty] private string barcodeValue = string.Empty;
    [ObservableProperty] private string barcodeType = "PRODUCT";
    [ObservableProperty] private string barcodeTarget = string.Empty;
    [ObservableProperty] private string barcodeProduct = string.Empty;
    [ObservableProperty] private string barcodeLot = string.Empty;
    [ObservableProperty] private string barcodeLocation = string.Empty;
    [ObservableProperty] private string barcodePackageUnit = string.Empty;
    [ObservableProperty] private decimal barcodeConversionRate = 1m;
    [ObservableProperty] private string createWarehouse = string.Empty;
    [ObservableProperty] private string createArea = string.Empty;
    [ObservableProperty] private string createFromLocation = string.Empty;
    [ObservableProperty] private string createToLocation = string.Empty;
    [ObservableProperty] private string createProduct = string.Empty;
    [ObservableProperty] private string createLot = string.Empty;
    [ObservableProperty] private decimal createQuantity = 1;
    [ObservableProperty] private string languageCode = "zh-CN";
    [ObservableProperty] private string filterAssignedTo = string.Empty;
    [ObservableProperty] private string filterStatus = string.Empty;
    [ObservableProperty] private bool openOnly;
    [ObservableProperty] private int page = 1;
    [ObservableProperty] private int total;

    private string? _challenge;
    private bool _enroll;
    private bool _clientAccessGranted;
    private ClientUpgradeDecision? _upgradeDecision;
    private string _connectionState = "Offline";
    private string _printGatewayState = "Stopped";
    private TaskAnalytics? _lastAnalytics;
    private string? _activatedDeviceMode;
    private string? _activatedWarehouse;
    private const int PageSize = 50;

    public ShellViewModel(
        IClientSessionService sessions,
        Cp6ApiClient api,
        ClientUpgradeService upgrades,
        WmsTaskService tasks,
        WmsRealtimeService realtime,
        ILanguageService language,
        NativeSsoService sso,
        DesktopDeviceActivationService deviceActivation,
        LabelGatewayService labelGateway,
        ClientDeviceHeartbeatLoop heartbeat)
    {
        _sessions = sessions;
        _api = api;
        _upgrades = upgrades;
        _tasks = tasks;
        _realtime = realtime;
        _language = language;
        _sso = sso;
        _deviceActivation = deviceActivation;
        _labelGateway = labelGateway;
        _heartbeat = heartbeat;
        _language.LanguageChanged += (_, _) => ApplyLocalizedText();
        _realtime.TaskChanged += (_, _) =>
            System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await LoadTasksAsync());
        _realtime.ConnectionStateChanged += (_, state) =>
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _connectionState = state;
                ConnectionStatus = LocalizeState(state);
            });
        _labelGateway.StateChanged += (_, state) =>
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _printGatewayState = state;
                PrintGatewayStatus = LocalizeState(state);
            });
        _heartbeat.StateChanged += HeartbeatOnStateChanged;
        ApplyLocalizedText();
    }

    public ObservableCollection<MobileTask> Tasks { get; } = new();
    public ObservableCollection<ClientDevice> Devices { get; } = new();
    public ObservableCollection<BarcodeAlias> Barcodes { get; } = new();
    public ILanguageService Text => _language;
    public string AuthenticatorSecretText =>
        string.Format(
            CultureInfo.CurrentCulture,
            _language["client.authenticatorSecret"],
            TwoFactorSetupSecret);
    public string PageSummary => $"{Page} / {Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize))} ({Total})";

    public async Task InitializeAsync()
    {
        await CheckClientAccessAsync();
    }

    [RelayCommand]
    private Task CheckClientAccessAsync()
        => RunAsync(async () =>
        {
            _clientAccessGranted = false;
            IsLoginVisible = false;
            IsTaskVisible = false;
            IsTwoFactorVisible = false;
            IsUpgradeVisible = true;
            CanDownloadUpdate = false;
            UpgradeHeading = _language["client.startupBlocked"];
            StatusMessage = _language["client.startupChecking"];

            _upgradeDecision = await _upgrades.CheckAccessAsync();
            CurrentVersion = _upgradeDecision.CurrentVersion;
            LatestVersion = _upgradeDecision.LatestVersion;
            MinimumVersion = _upgradeDecision.MinimumVersion;
            DownloadUrl = _upgradeDecision.DownloadUri?.AbsoluteUri;
            ReleaseSha256 = _upgradeDecision.Sha256 ?? string.Empty;
            CanDownloadUpdate = _upgradeDecision.CanDownload;
            UpgradeHeading = _upgradeDecision.UpgradeRequired
                ? _language["client.upgradeRequired"]
                : _language["client.startupBlocked"];

            if (!_upgradeDecision.BusinessAllowed)
            {
                StatusMessage = _upgradeDecision.ErrorCode ??
                                string.Format(
                                    CultureInfo.CurrentCulture,
                                    _language["client.minimumVersionValue"],
                                    _upgradeDecision.MinimumVersion);
                return;
            }

            _clientAccessGranted = true;
            IsUpgradeVisible = false;
            IsLoginVisible = true;
            await _language.LoadAsync(LanguageCode);
            if (await _sessions.RestoreAsync())
                await EnterTasksAsync();
        });

    [RelayCommand]
    private Task DownloadUpdateAsync()
        => _upgradeDecision is null
            ? Task.CompletedTask
            : RunAsync(async () =>
            {
                await _upgrades.OpenDownloadAsync(_upgradeDecision);
                StatusMessage = _language["client.updateOpened"];
            });

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (!EnsureClientAccess()) return;
        await RunAsync(async () =>
        {
            var result = await _sessions.LoginAsync(UserName, Password, NullIfEmpty(TenantCode));
            Password = string.Empty;
            if (result.Session != null)
            {
                await EnterTasksAsync();
                return;
            }
            _challenge = result.ChallengeToken;
            _enroll = result.State == "enrollmentRequired";
            CanUseEmailOtp = !_enroll;
            IsTwoFactorVisible = true;
            if (_enroll && _challenge != null)
            {
                var setup = await _sessions.SetupTwoFactorAsync(_challenge);
                TwoFactorSetupSecret = setup.Secret;
            }
            StatusMessage = _enroll
                ? _language["client.enrollmentRequired"]
                : _language["client.enterVerificationCode"];
        });
    }

    [RelayCommand]
    private async Task VerifyTwoFactorAsync()
    {
        if (!EnsureClientAccess()) return;
        if (_challenge == null) return;
        await RunAsync(async () =>
        {
            var result = await _sessions.VerifyTwoFactorAsync(
                _challenge, TwoFactorCode, TwoFactorMethod, _enroll);
            if (result.Session != null) await EnterTasksAsync();
        });
    }

    [RelayCommand]
    private Task SendEmailOtpAsync()
        => _challenge == null
            ? Task.CompletedTask
            : RunAsync(async () =>
            {
                await _sessions.RequestEmailOtpAsync(_challenge);
                TwoFactorMethod = "email";
                StatusMessage = _language["client.emailCodeSent"];
            });

    [RelayCommand]
    private Task StartSsoAsync()
        => !EnsureClientAccess()
            ? Task.CompletedTask
            : RunAsync(() => _sso.StartAsync(TenantCode));

    public Task CompleteSsoAsync(Uri callback)
        => !EnsureClientAccess()
            ? Task.CompletedTask
            : RunAsync(async () =>
        {
            var result = await _sso.CompleteAsync(callback);
            if (result.Session != null) await EnterTasksAsync();
        });

    [RelayCommand]
    private Task LoadTasksAsync()
        => RunAsync(async () =>
        {
            var status = int.TryParse(FilterStatus, out var parsed) ? parsed : (int?)null;
            var result = await _tasks.GetAllAsync(
                NullIfEmpty(FilterAssignedTo),
                status,
                OpenOnly,
                Page,
                PageSize);
            Tasks.Clear();
            foreach (var task in result.Items) Tasks.Add(task);
            Total = result.Total;
            Page = result.Page;
        }, showBusy: false);

    [RelayCommand]
    private async Task ApplyFiltersAsync()
    {
        Page = 1;
        await LoadTasksAsync();
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (Page <= 1) return;
        Page--;
        await LoadTasksAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (Page * PageSize >= Total) return;
        Page++;
        await LoadTasksAsync();
    }

    [RelayCommand]
    private Task CreateTaskAsync()
        => RunAsync(async () =>
        {
            await _tasks.CreateAsync(new CreateMoveTaskRequest
            {
                WarehouseCd = CreateWarehouse,
                AreaCd = NullIfEmpty(CreateArea),
                FromLocationCd = CreateFromLocation,
                ToLocationCd = CreateToLocation,
                ProductCd = CreateProduct,
                LotNo = NullIfEmpty(CreateLot),
                Qty = CreateQuantity,
            });
            await LoadTasksAsync();
        });

    [RelayCommand]
    private Task AssignTaskAsync()
        => SelectedTask == null
            ? Task.CompletedTask
            : RunAsync(async () =>
            {
                SelectedTask = await _tasks.AssignAsync(SelectedTask, Assignee);
                await LoadTasksAsync();
            });

    [RelayCommand]
    private Task CancelTaskAsync()
        => SelectedTask == null
            ? Task.CompletedTask
            : RunAsync(async () =>
            {
                SelectedTask = await _tasks.CancelAsync(SelectedTask, ActionReason);
                await LoadTasksAsync();
            });

    [RelayCommand]
    private Task PauseTaskAsync()
        => SelectedTask == null
            ? Task.CompletedTask
            : RunAsync(async () =>
            {
                SelectedTask = await _tasks.PauseAsync(SelectedTask, ActionReason);
                await LoadTasksAsync();
            });

    [RelayCommand]
    private Task ReleaseTaskAsync()
        => SelectedTask == null
            ? Task.CompletedTask
            : RunAsync(async () =>
            {
                SelectedTask = await _tasks.ReleaseAsync(SelectedTask, ActionReason);
                await LoadTasksAsync();
            });

    [RelayCommand]
    private Task TakeoverTaskAsync()
        => SelectedTask == null
            ? Task.CompletedTask
            : RunAsync(async () =>
            {
                SelectedTask = await _tasks.TakeoverAsync(SelectedTask, Assignee, ActionReason);
                await LoadTasksAsync();
            });

    [RelayCommand]
    private Task RaiseExceptionAsync()
        => SelectedTask == null
            ? Task.CompletedTask
            : RunAsync(async () =>
            {
                SelectedTask = await _tasks.RaiseExceptionAsync(
                    SelectedTask, ExceptionReasonCode, ActionReason);
                await LoadTasksAsync();
            });

    [RelayCommand]
    private Task ActivateDeviceAsync()
        => RunAsync(async () =>
        {
            var activated = await _deviceActivation.ActivateAsync(ActivationPayload);
            TenantCode = activated.TenantCode;
            _activatedDeviceMode = activated.DeviceMode;
            _activatedWarehouse = activated.WarehouseCd;
            ApplyDeviceActivationStatus();
        });

    [RelayCommand]
    private Task StartPrintGatewayAsync()
        => RunAsync(async () =>
        {
            await _labelGateway.StartAsync();
            _printGatewayState = "Running";
            PrintGatewayStatus = LocalizeState(_printGatewayState);
        });

    [RelayCommand]
    private Task StopPrintGatewayAsync()
        => RunAsync(() => _labelGateway.StopAsync());

    [RelayCommand]
    private Task LoadProductionOverviewAsync()
        => RunAsync(LoadProductionOverviewCoreAsync);

    [RelayCommand]
    private Task ToggleDeviceAsync()
        => SelectedDevice is null
            ? Task.CompletedTask
            : RunAsync(async () =>
            {
                SelectedDevice = await _api.UpdateClientDeviceAsync(
                    SelectedDevice.DeviceId,
                    new UpdateClientDeviceRequest
                    {
                        RowVersion = SelectedDevice.RowVersion,
                        Status = SelectedDevice.Status == "Active" ? "Disabled" : "Active",
                        DeviceMode = SelectedDevice.DeviceMode,
                        WarehouseCd = SelectedDevice.WarehouseCd,
                        AreaCd = SelectedDevice.AreaCd
                    });
                await LoadProductionOverviewCoreAsync();
            });

    [RelayCommand]
    private Task SaveBarcodeAsync()
        => RunAsync(async () =>
        {
            await _api.UpsertBarcodeAliasAsync(new UpsertBarcodeAliasRequest
            {
                Id = SelectedBarcode?.Id,
                RowVersion = SelectedBarcode?.RowVersion,
                Barcode = BarcodeValue,
                BarcodeType = BarcodeType,
                TargetKey = BarcodeTarget,
                ProductCd = NullIfEmpty(BarcodeProduct),
                LotNo = NullIfEmpty(BarcodeLot),
                LocationCd = NullIfEmpty(BarcodeLocation),
                PackageUnitCd = NullIfEmpty(BarcodePackageUnit),
                ConversionRate = BarcodeConversionRate,
                IsEnabled = true
            });
            SelectedBarcode = null;
            BarcodeValue = string.Empty;
            BarcodeTarget = string.Empty;
            await LoadProductionOverviewCoreAsync();
        });

    [RelayCommand]
    private Task ChangeLanguageAsync()
        => RunAsync(() => _language.LoadAsync(LanguageCode));

    [RelayCommand]
    private Task LogoutAsync()
        => RunAsync(async () =>
        {
            await _sessions.LogoutAsync();
            await _realtime.StopAsync();
            IsTaskVisible = false;
            IsLoginVisible = true;
            _connectionState = "Offline";
            ConnectionStatus = LocalizeState(_connectionState);
            Tasks.Clear();
        });

    private async Task EnterTasksAsync()
    {
        if (!_clientAccessGranted)
            throw new ClientBusinessAccessBlockedException("E-CLIENT-UPGRADE-REQUIRED");
        IsLoginVisible = false;
        IsTwoFactorVisible = false;
        IsTaskVisible = true;
        await LoadTasksAsync();
        await _realtime.StartAsync();
        _connectionState = "Online";
        ConnectionStatus = LocalizeState(_connectionState);
    }

    private async Task LoadProductionOverviewCoreAsync()
    {
        var analyticsTask = _api.GetTaskAnalyticsAsync();
        var devicesTask = _api.GetClientDevicesAsync();
        var barcodesTask = _api.GetBarcodeAliasesAsync();
        await Task.WhenAll(analyticsTask, devicesTask, barcodesTask);
        var analytics = await analyticsTask;
        Devices.Clear();
        foreach (var device in (await devicesTask).Items) Devices.Add(device);
        Barcodes.Clear();
        foreach (var barcode in (await barcodesTask).Items) Barcodes.Add(barcode);
        _lastAnalytics = analytics;
        ApplyProductionSummary();
    }

    private async Task RunAsync(Func<Task> action, bool showBusy = true)
    {
        if (IsBusy && showBusy) return;
        if (showBusy) IsBusy = true;
        try
        {
            StatusMessage = string.Empty;
            await action();
        }
        catch (Exception ex)
        {
            StatusMessage = ex is ApiException api ? api.Code ?? api.Message : ex.Message;
            if (ex is SessionExpiredException)
            {
                IsTaskVisible = false;
                IsLoginVisible = true;
            }
        }
        finally
        {
            if (showBusy) IsBusy = false;
        }
    }

    private static string? NullIfEmpty(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void HeartbeatOnStateChanged(
        object? sender,
        ClientDeviceHeartbeatStateChangedEventArgs e)
        => System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (e.Status is ClientDeviceHeartbeatStatus.Online
                or ClientDeviceHeartbeatStatus.Offline
                or ClientDeviceHeartbeatStatus.Rejected)
            {
                _connectionState = e.Status.ToString();
                ConnectionStatus = LocalizeState(_connectionState);
            }

            if (e.Status != ClientDeviceHeartbeatStatus.Rejected)
                return;

            StatusMessage = e.ErrorCode ?? "WM-DEVICE-ACTIVATION-REQUIRED";
            await _realtime.StopAsync();
            IsTaskVisible = false;
            IsLoginVisible = true;
            Tasks.Clear();
        });

    private bool EnsureClientAccess()
    {
        if (_clientAccessGranted)
            return true;

        IsLoginVisible = false;
        IsTaskVisible = false;
        IsUpgradeVisible = true;
        StatusMessage = _upgradeDecision?.ErrorCode ??
                        (_upgradeDecision?.UpgradeRequired == true
                            ? "E-CLIENT-UPGRADE-REQUIRED"
                            : "E-CLIENT-BOOTSTRAP-REQUIRED");
        return false;
    }

    partial void OnPageChanged(int value) => OnPropertyChanged(nameof(PageSummary));
    partial void OnTotalChanged(int value) => OnPropertyChanged(nameof(PageSummary));
    partial void OnTwoFactorSetupSecretChanged(string value) =>
        OnPropertyChanged(nameof(AuthenticatorSecretText));
    partial void OnSelectedBarcodeChanged(BarcodeAlias? value)
    {
        if (value is null) return;
        BarcodeValue = value.Barcode;
        BarcodeType = value.BarcodeType;
        BarcodeTarget = value.TargetKey;
        BarcodeProduct = value.ProductCd ?? string.Empty;
        BarcodeLot = value.LotNo ?? string.Empty;
        BarcodeLocation = value.LocationCd ?? string.Empty;
        BarcodePackageUnit = value.PackageUnitCd ?? string.Empty;
        BarcodeConversionRate = value.ConversionRate;
    }

    private void ApplyLocalizedText()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(AuthenticatorSecretText));
        ConnectionStatus = LocalizeState(_connectionState);
        PrintGatewayStatus = LocalizeState(_printGatewayState);
        ApplyDeviceActivationStatus();
        ApplyProductionSummary();
    }

    private string LocalizeState(string state) =>
        state switch
        {
            "Online" => _language["client.online"],
            "Offline" => _language["client.offline"],
            "Reconnecting" => _language["client.reconnecting"],
            "Retrying" => _language["client.retrying"],
            "Running" => _language["client.running"],
            "Stopped" => _language["client.stopped"],
            "Rejected" => _language["client.rejected"],
            _ => state,
        };

    private void ApplyDeviceActivationStatus()
    {
        DeviceActivationStatus = _activatedDeviceMode == null
            ? _language["client.notActivated"]
            : $"{_activatedDeviceMode} / "
              + (_activatedWarehouse ?? _language["client.allWarehouses"]);
    }

    private void ApplyProductionSummary()
    {
        if (_lastAnalytics == null)
        {
            ProductionSummary = _language["client.productionOverviewNotLoaded"];
            return;
        }

        ProductionSummary = string.Format(
            CultureInfo.CurrentCulture,
            _language["client.productionSummary"],
            _lastAnalytics.Created,
            _lastAnalytics.Completed,
            _lastAnalytics.PartiallyCompleted,
            _lastAnalytics.Exceptions,
            _lastAnalytics.Overdue,
            _lastAnalytics.AverageMinutes);
    }
}
