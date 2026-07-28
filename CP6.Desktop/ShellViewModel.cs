using System.Collections.ObjectModel;
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
    [ObservableProperty] private string connectionStatus = "Offline";
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
    [ObservableProperty] private string deviceActivationStatus = "Not activated";
    [ObservableProperty] private string printGatewayStatus = "Stopped";
    [ObservableProperty] private string productionSummary = "Production overview not loaded";
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
        LabelGatewayService labelGateway)
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
        _language.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Text));
        _realtime.TaskChanged += (_, _) =>
            System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await LoadTasksAsync());
        _realtime.ConnectionStateChanged += (_, state) =>
            System.Windows.Application.Current.Dispatcher.Invoke(() => ConnectionStatus = state);
        _labelGateway.StateChanged += (_, state) =>
            System.Windows.Application.Current.Dispatcher.Invoke(() => PrintGatewayStatus = state);
    }

    public ObservableCollection<MobileTask> Tasks { get; } = new();
    public ObservableCollection<ClientDevice> Devices { get; } = new();
    public ObservableCollection<BarcodeAlias> Barcodes { get; } = new();
    public ILanguageService Text => _language;
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
            StatusMessage = "Checking client release policy...";

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
                                $"Minimum version: {_upgradeDecision.MinimumVersion}";
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
                StatusMessage = "Update download opened.";
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
            StatusMessage = _enroll ? "2FA enrollment required" : "Enter verification code";
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
                StatusMessage = "Email verification code sent";
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
            DeviceActivationStatus =
                $"{activated.DeviceMode} / {activated.WarehouseCd ?? "all warehouses"}";
        });

    [RelayCommand]
    private Task StartPrintGatewayAsync()
        => RunAsync(async () =>
        {
            await _labelGateway.StartAsync();
            PrintGatewayStatus = "Running";
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
            ConnectionStatus = "Offline";
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
        ConnectionStatus = "Online";
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
        ProductionSummary =
            $"Created {analytics.Created} · Completed {analytics.Completed} · "
            + $"Partial {analytics.PartiallyCompleted} · Exceptions {analytics.Exceptions} · "
            + $"Overdue {analytics.Overdue} · Avg {analytics.AverageMinutes:F1} min";
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
}
