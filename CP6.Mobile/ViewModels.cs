using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CP6.Client.Api;
using CP6.Client.Core;

namespace CP6.Mobile;

public abstract partial class MobileViewModel : ObservableObject
{
    protected MobileViewModel(ILanguageService language)
    {
        Text = new MobileText(language);
    }

    public MobileText Text { get; }

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string message = string.Empty;

    protected async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        Message = string.Empty;
        try { await action(); }
        catch (ApiException ex) { Message = ex.Code ?? ex.Message; }
        catch (Exception ex) { Message = ex.Message; }
        finally { IsBusy = false; }
    }
}

public partial class LoginViewModel : MobileViewModel,
    IRecipient<SsoCallbackMessage>
{
    private readonly IClientSessionService _sessions;
    private readonly ClientUpgradeService _upgrades;
    private readonly NativeSsoService _sso;
    private readonly ILanguageService _language;
    private readonly MobileClientState _state;
    private string? _challenge;
    private bool _enroll;

    [ObservableProperty] private string tenantCode =
        Preferences.Default.Get("cp6.tenant-code", string.Empty);
    [ObservableProperty] private string userName = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string twoFactorCode = string.Empty;
    [ObservableProperty] private string twoFactorMethod = "totp";
    [ObservableProperty] private string twoFactorSetupSecret = string.Empty;
    [ObservableProperty] private bool showTwoFactor;
    [ObservableProperty] private bool canUseEmailOtp;
    [ObservableProperty] private string languageCode = "zh-CN";
    [ObservableProperty] private string badgeNo = string.Empty;
    [ObservableProperty] private string quickPin = string.Empty;

    public string AuthenticatorSecretText =>
        string.Format(
            CultureInfo.CurrentCulture,
            Text.AuthenticatorSecret,
            TwoFactorSetupSecret);

    partial void OnTwoFactorSetupSecretChanged(string value) =>
        OnPropertyChanged(nameof(AuthenticatorSecretText));

    public LoginViewModel(
        IClientSessionService sessions,
        ClientUpgradeService upgrades,
        NativeSsoService sso,
        ILanguageService language,
        MobileClientState state)
        : base(language)
    {
        _sessions = sessions;
        _upgrades = upgrades;
        _sso = sso;
        _language = language;
        _state = state;
        WeakReferenceMessenger.Default.Register(this);
    }

    [RelayCommand]
    private Task InitializeAsync() => RunAsync(async () =>
    {
        var decision = await _upgrades.CheckAccessAsync();
        _state.UpgradeDecision = decision;
        if (!decision.BusinessAllowed)
        {
            await Shell.Current.GoToAsync("upgrade");
            return;
        }
        await _language.LoadAsync(LanguageCode);
        if (await _sessions.RestoreAsync())
            await Shell.Current.GoToAsync("tasks");
    });

    [RelayCommand]
    private Task LoginAsync() => RunAsync(async () =>
    {
        EnsureClientAccess();
        var result = await _sessions.LoginAsync(UserName, Password, EmptyToNull(TenantCode));
        Password = string.Empty;
        if (result.Session != null)
        {
            await Shell.Current.GoToAsync("tasks");
            return;
        }
        _challenge = result.ChallengeToken;
        _enroll = result.State == "enrollmentRequired";
        CanUseEmailOtp = !_enroll;
        ShowTwoFactor = true;
        if (_enroll && _challenge != null)
        {
            var setup = await _sessions.SetupTwoFactorAsync(_challenge);
            TwoFactorSetupSecret = setup.Secret;
        }
        Message = _enroll ? Text.EnrollmentRequired : Text.EnterVerificationCode;
    });

    [RelayCommand]
    private Task VerifyTwoFactorAsync() => RunAsync(async () =>
    {
        EnsureClientAccess();
        if (_challenge == null) return;
        var result = await _sessions.VerifyTwoFactorAsync(
            _challenge, TwoFactorCode, TwoFactorMethod, _enroll);
        if (result.Session != null) await Shell.Current.GoToAsync("tasks");
    });

    [RelayCommand]
    private Task SendEmailOtpAsync() => RunAsync(async () =>
    {
        EnsureClientAccess();
        if (_challenge == null) return;
        await _sessions.RequestEmailOtpAsync(_challenge);
        TwoFactorMethod = "email";
        Message = Text.EmailCodeSent;
    });

    [RelayCommand]
    private Task StartSsoAsync()
        => RunAsync(() =>
        {
            EnsureClientAccess();
            return _sso.StartAsync(TenantCode);
        });

    [RelayCommand]
    private Task QuickSwitchAsync() => RunAsync(async () =>
    {
        EnsureClientAccess();
        var result = await _sessions.QuickSwitchAsync(TenantCode, BadgeNo, QuickPin);
        QuickPin = string.Empty;
        if (result.Session != null) await Shell.Current.GoToAsync("tasks");
    });

    [RelayCommand]
    private Task OpenDeviceActivationAsync()
        => Shell.Current.GoToAsync("device-activation");

    [RelayCommand]
    private Task ChangeLanguageAsync()
        => RunAsync(async () =>
        {
            await _language.LoadAsync(LanguageCode);
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(AuthenticatorSecretText));
        });

    public void Receive(SsoCallbackMessage message)
    {
        MainThread.BeginInvokeOnMainThread(async () => await CompleteSsoAsync(message.Value));
    }

    private Task CompleteSsoAsync(Uri callback) => RunAsync(async () =>
    {
        EnsureClientAccess();
        var result = await _sso.CompleteAsync(callback);
        if (result.Session != null) await Shell.Current.GoToAsync("tasks");
    });

    private static string? EmptyToNull(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void EnsureClientAccess()
    {
        var decision = _state.UpgradeDecision;
        if (decision?.BusinessAllowed == true)
            return;

        throw new ClientBusinessAccessBlockedException(
            decision?.ErrorCode ??
            (decision?.UpgradeRequired == true
                ? "E-CLIENT-UPGRADE-REQUIRED"
                : "E-CLIENT-BOOTSTRAP-REQUIRED"));
    }
}

public partial class DeviceActivationViewModel : MobileViewModel
{
    private readonly DeviceActivationService _activation;

    [ObservableProperty] private string activationPayload = string.Empty;

    public DeviceActivationViewModel(
        DeviceActivationService activation,
        ILanguageService language)
        : base(language) => _activation = activation;

    [RelayCommand]
    private Task ActivateAsync() => RunAsync(async () =>
    {
        var result = await _activation.ActivateAsync(ActivationPayload);
        Message = string.Format(
            CultureInfo.CurrentCulture,
            Text.ActivatedDevice,
            result.DeviceId,
            result.DeviceMode);
        await Shell.Current.GoToAsync("..");
    });

    public async Task AcceptQrAsync(string value)
    {
        if (IsBusy) return;
        ActivationPayload = value;
        await ActivateAsync();
    }
}

public partial class TaskListViewModel : MobileViewModel
{
    private readonly IClientSessionService _sessions;
    private readonly WmsTaskService _tasks;
    private readonly WmsRealtimeService _realtime;
    private readonly MobileClientState _state;
    private readonly ClientDeviceHeartbeatLoop _heartbeat;

    public TaskListViewModel(
        IClientSessionService sessions,
        WmsTaskService tasks,
        WmsRealtimeService realtime,
        MobileClientState state,
        ClientDeviceHeartbeatLoop heartbeat,
        ILanguageService language)
        : base(language)
    {
        _sessions = sessions;
        _tasks = tasks;
        _realtime = realtime;
        _state = state;
        _heartbeat = heartbeat;
    }

    public ObservableCollection<MobileTask> Tasks { get; } = new();

    public void Activate()
    {
        _realtime.TaskChanged -= RealtimeOnTaskChanged;
        _realtime.TaskChanged += RealtimeOnTaskChanged;
    }
    public void Deactivate() => _realtime.TaskChanged -= RealtimeOnTaskChanged;

    private void RealtimeOnTaskChanged(object? sender, MobileTask task)
        => MainThread.BeginInvokeOnMainThread(async () => await LoadAsync());

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var user = _sessions.Current?.Profile.UserName ?? throw new SessionExpiredException();
        var result = await _tasks.GetMineAndUnassignedAsync(user);
        Tasks.Clear();
        foreach (var task in result.Items) Tasks.Add(task);
        if (_state.SelectedTask is not null)
        {
            _state.SelectedTask = result.Items.FirstOrDefault(
                task => string.Equals(
                    task.TaskNo,
                    _state.SelectedTask.TaskNo,
                    StringComparison.Ordinal));
        }
        _heartbeat.RequestImmediate();
        await _realtime.StartAsync();
    });

    [RelayCommand]
    private async Task OpenTaskAsync(MobileTask task)
    {
        _state.SelectedTask = task;
        await Shell.Current.GoToAsync("task-detail");
    }

    [RelayCommand]
    private Task LogoutAsync() => RunAsync(async () =>
    {
        await _sessions.LogoutAsync();
        await _realtime.StopAsync();
        Tasks.Clear();
        await Shell.Current.GoToAsync("//login");
    });
}

public partial class TaskDetailViewModel : MobileViewModel
{
    private readonly WmsTaskService _tasks;
    private readonly IClientSessionService _sessions;
    private readonly MobileClientState _state;
    private readonly ClientDeviceHeartbeatLoop _heartbeat;

    [ObservableProperty] private MobileTask? task;

    public TaskDetailViewModel(
        WmsTaskService tasks,
        IClientSessionService sessions,
        MobileClientState state,
        ClientDeviceHeartbeatLoop heartbeat,
        ILanguageService language)
        : base(language)
    {
        _tasks = tasks;
        _sessions = sessions;
        _state = state;
        _heartbeat = heartbeat;
    }

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        Task = _state.SelectedTask == null
            ? null
            : await _tasks.GetAsync(_state.SelectedTask.TaskNo);
        _state.SelectedTask = Task;
        _heartbeat.RequestImmediate();
    });

    [RelayCommand]
    private Task ClaimOrStartAsync() => RunAsync(async () =>
    {
        if (Task == null) return;
        var user = _sessions.Current?.Profile.UserName ?? throw new SessionExpiredException();
        if (Task.AssignedTo == null)
            Task = await _tasks.ClaimAsync(Task);
        else if (Task.Status == 0 && string.Equals(Task.AssignedTo, user, StringComparison.OrdinalIgnoreCase))
            Task = await _tasks.StartAsync(Task);
        else if (!string.Equals(Task.AssignedTo, user, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("WM-CONFLICT-TASK-NOT-ASSIGNED");
        _state.SelectedTask = Task;
        _heartbeat.RequestImmediate();
    });

    [RelayCommand]
    private async Task OpenScanAsync()
    {
        if (Task?.Status != 1)
        {
            Message = "WM-CONFLICT-TASK-NOT-STARTED";
            return;
        }
        _state.SelectedTask = Task;
        await Shell.Current.GoToAsync("move-scan");
    }
}

public partial class MoveScanViewModel : MobileViewModel
{
    private readonly WmsTaskService _tasks;
    private readonly MobileClientState _state;
    private readonly IOfflineMoveProgressStore _offline;
    private readonly ClientDeviceHeartbeatLoop _heartbeat;
    private readonly ScannerInputProcessor _scanner;
    private readonly MoveScanWorkflow _workflow = new();
    private TaskScanProfile? _scanProfile;
    private Guid _operationId = Guid.NewGuid();
    private string? _pendingClientScanNo;
    private string? _pendingScanStep;
    private string? _pendingScanValue;

    [ObservableProperty] private MobileTask? task;
    [ObservableProperty] private string scanValue = string.Empty;
    [ObservableProperty] private string quantity = string.Empty;
    [ObservableProperty] private string stepTitle = string.Empty;
    [ObservableProperty] private bool canComplete;
    [ObservableProperty] private string partialReason = string.Empty;
    [ObservableProperty] private bool isOfflineProgress;
    [ObservableProperty] private bool isCameraOpen;

    public MoveScanViewModel(
        WmsTaskService tasks,
        MobileClientState state,
        IOfflineMoveProgressStore offline,
        ClientDeviceHeartbeatLoop heartbeat,
        ScannerInputProcessor scanner,
        ILanguageService language)
        : base(language)
    {
        _tasks = tasks;
        _state = state;
        _offline = offline;
        _heartbeat = heartbeat;
        _scanner = scanner;
        WeakReferenceMessenger.Default.Register<MoveScanViewModel, ScanBroadcastMessage>(
            this,
            static (recipient, message) =>
                MainThread.BeginInvokeOnMainThread(
                    async () => await recipient.AcceptExternalScanAsync(
                        message.Value,
                        ScannerInputSource.Broadcast)));
    }

    public ScannerHidTerminator HidTerminator => _scanner.HidTerminator;

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var selected = _state.SelectedTask;
        try
        {
            Task = selected == null ? null : await _tasks.GetAsync(selected.TaskNo);
            if (Task != null)
            {
                _scanProfile = await _tasks.GetScanProfileAsync(Task);
                _workflow.Reset(_scanProfile.Steps.Contains("Lot", StringComparer.OrdinalIgnoreCase));
                await PersistAsync();
            }
        }
        catch (HttpRequestException) when (selected != null)
        {
            var cached = await _offline.ReadAsync();
            if (cached?.Task.TaskNo != selected.TaskNo) throw;
            Task = cached.Task;
            _scanProfile = cached.ScanProfile;
            _operationId = cached.CompletionOperationId;
            _workflow.Reset(_scanProfile.Steps.Contains("Lot", StringComparer.OrdinalIgnoreCase));
            RestoreStep(cached);
            IsOfflineProgress = true;
            Message = "WM-OFFLINE-SCAN-CACHED";
        }
        _state.SelectedTask = Task;
        _heartbeat.RequestImmediate();
        UpdateStep();
    });

    [RelayCommand]
    private Task SubmitScanAsync() =>
        AcceptScanAsync(ScanValue, ScannerInputSource.Manual);

    private Task SubmitAcceptedScanAsync() => RunAsync(async () =>
    {
        if (Task == null) return;
        if (Connectivity.Current.NetworkAccess == NetworkAccess.None)
        {
            Message = "WM-OFFLINE-SCAN-CACHED";
            _workflow.AcceptBarcode(Task, ScanValue);
        }
        else
        {
            var step = _workflow.Step.ToString();
            if (!string.Equals(_pendingScanStep, step, StringComparison.Ordinal)
                || !string.Equals(
                    _pendingScanValue,
                    ScanValue,
                    StringComparison.Ordinal))
            {
                _pendingClientScanNo = Guid.NewGuid().ToString("N");
                _pendingScanStep = step;
                _pendingScanValue = ScanValue;
            }
            var result = await _tasks.ScanAsync(
                Task,
                step,
                ScanValue,
                _pendingClientScanNo);
            ClearPendingScan();
            if (result.Matched == false)
            {
                Message = $"{result.ErrorCode}: {result.RecoveryAction}";
                return;
            }
            _workflow.AcceptValidated(_workflow.Step);
            IsOfflineProgress = false;
        }
        ScanValue = string.Empty;
        await PersistAsync();
        UpdateStep();
    });

    public Task AcceptHidScanAsync(string value) =>
        AcceptScanAsync(value, ScannerInputSource.Hid);

    public Task AcceptExternalScanAsync(
        string value,
        ScannerInputSource source = ScannerInputSource.Broadcast) =>
        AcceptScanAsync(value, source);

    private async Task AcceptScanAsync(string value, ScannerInputSource source)
    {
        if (IsBusy) return;
        var result = _scanner.Accept(value, source);
        if (source == ScannerInputSource.Camera
            && result.Status != ScannerInputStatus.Invalid)
            IsCameraOpen = false;
        if (!result.IsAccepted)
        {
            Message = result.ErrorCode ?? "WM-SCAN-INPUT-INVALID";
            return;
        }

        ScanValue = result.Value!;
        await SubmitAcceptedScanAsync();
    }

    [RelayCommand]
    private async Task ToggleCameraAsync()
    {
        if (!IsCameraOpen)
        {
            var permission = await Permissions.RequestAsync<Permissions.Camera>();
            if (permission != PermissionStatus.Granted)
            {
                Message = "WM-CAMERA-PERMISSION-REQUIRED";
                return;
            }
        }
        IsCameraOpen = !IsCameraOpen;
    }

    [RelayCommand]
    private void ConfirmQuantity()
    {
        Message = string.Empty;
        try
        {
            if (Task == null
                || !decimal.TryParse(Quantity, NumberStyles.Number, CultureInfo.CurrentCulture, out var value))
                throw new InvalidOperationException("WM-MSG-031");
            _workflow.ConfirmQuantity(Task, value);
            _ = PersistAsync();
            UpdateStep();
        }
        catch (Exception ex) { Message = ex.Message; }
    }

    [RelayCommand]
    private Task CompleteAsync() => RunAsync(async () =>
    {
        if (Task == null) return;
        if (Connectivity.Current.NetworkAccess == NetworkAccess.None)
            throw new InvalidOperationException("WM-OFFLINE-COMMIT-NOT-ALLOWED");
        if (_workflow.ConfirmedQuantity < Task.Qty && string.IsNullOrWhiteSpace(PartialReason))
            throw new InvalidOperationException("WM-V2-PARTIAL-REASON-REQUIRED");
        Task = await _tasks.CompleteAsync(
            Task,
            _operationId,
            _workflow.ConfirmedQuantity,
            PartialReason);
        _workflow.MarkCompleted();
        await _offline.ClearAsync();
        _state.SelectedTask = Task;
        _heartbeat.RequestImmediate();
        Message = Text.MoveCompleted;
        CanComplete = false;
    });

    [RelayCommand]
    private Task ReloadTaskAsync() => RunAsync(async () =>
    {
        if (Task == null) return;
        Task = await _tasks.GetAsync(Task.TaskNo);
        if (Task.ExecutionVersion != (_scanProfile?.ExecutionVersion ?? Task.ExecutionVersion))
        {
            _workflow.Reset(_scanProfile?.Steps.Contains("Lot", StringComparer.OrdinalIgnoreCase) == true);
            await _offline.ClearAsync();
            ClearPendingScan();
            Message = "WM-V2-EXECUTION-CHANGED-RESCAN";
        }
        _state.SelectedTask = Task;
        _heartbeat.RequestImmediate();
        if (Task.CompletionOperationId == _operationId)
        {
            Message = Text.MoveCompleted;
            CanComplete = false;
        }
    });

    [RelayCommand]
    private void ResetScan()
    {
        _workflow.Reset(_scanProfile?.Steps.Contains(
            "Lot", StringComparer.OrdinalIgnoreCase) == true);
        _operationId = Guid.NewGuid();
        ScanValue = string.Empty;
        Quantity = string.Empty;
        PartialReason = string.Empty;
        Message = string.Empty;
        ClearPendingScan();
        _ = _offline.ClearAsync();
        UpdateStep();
    }

    private void UpdateStep()
    {
        StepTitle = _workflow.Step switch
        {
            MoveScanStep.SourceLocation => Text.ScanSource,
            MoveScanStep.Product => Text.ScanProduct,
            MoveScanStep.Lot => Text.ScanLot,
            MoveScanStep.TargetLocation => Text.ScanTarget,
            MoveScanStep.Quantity => Text.ConfirmQuantity,
            MoveScanStep.ReadyToComplete => Text.ReadyComplete,
            _ => Text.Completed,
        };
        CanComplete = _workflow.Step == MoveScanStep.ReadyToComplete;
    }

    private Task PersistAsync()
    {
        if (Task == null || Task.Status != 1 || _scanProfile == null)
            return System.Threading.Tasks.Task.CompletedTask;
        return _offline.WriteAsync(new OfflineMoveProgress
        {
            Task = Task,
            ScanProfile = _scanProfile,
            Step = _workflow.Step,
            ConfirmedQuantity = _workflow.ConfirmedQuantity,
            PartialReason = PartialReason,
            CompletionOperationId = _operationId,
            SavedAt = DateTimeOffset.UtcNow,
        });
    }

    private void RestoreStep(OfflineMoveProgress cached)
    {
        while (_workflow.Step < cached.Step && _workflow.Step < MoveScanStep.Quantity)
            _workflow.AcceptValidated(_workflow.Step);
        if (cached.Step >= MoveScanStep.ReadyToComplete && cached.ConfirmedQuantity > 0)
        {
            _workflow.ConfirmQuantity(cached.Task, cached.ConfirmedQuantity);
            Quantity = cached.ConfirmedQuantity.ToString(CultureInfo.CurrentCulture);
        }
        PartialReason = cached.PartialReason ?? string.Empty;
    }

    private void ClearPendingScan()
    {
        _pendingClientScanNo = null;
        _pendingScanStep = null;
        _pendingScanValue = null;
    }
}

public partial class UpgradeViewModel : MobileViewModel
{
    private readonly MobileClientState _state;
    private readonly ClientUpgradeService _upgrades;
    [ObservableProperty] private string heading = string.Empty;
    [ObservableProperty] private string currentVersion = string.Empty;
    [ObservableProperty] private string latestVersion = string.Empty;
    [ObservableProperty] private string minimumVersion = string.Empty;
    [ObservableProperty] private string downloadUrl = string.Empty;
    [ObservableProperty] private string releaseSha256 = string.Empty;
    [ObservableProperty] private bool canDownloadUpdate;

    public UpgradeViewModel(
        MobileClientState state,
        ClientUpgradeService upgrades,
        ILanguageService language)
        : base(language)
    {
        _state = state;
        _upgrades = upgrades;
        Apply(state.UpgradeDecision ??
              ClientUpgradeDecision.Unavailable(string.Empty));
    }

    [RelayCommand]
    private Task DownloadAsync() => RunAsync(async () =>
    {
        var decision = _state.UpgradeDecision ??
                       throw new InvalidOperationException("E-CLIENT-BOOTSTRAP-REQUIRED");
        await _upgrades.OpenDownloadAsync(decision);
        Message = Text.UpdateOpened;
    });

    [RelayCommand]
    private Task RetryAsync() => RunAsync(async () =>
    {
        var decision = await _upgrades.CheckAccessAsync();
        _state.UpgradeDecision = decision;
        Apply(decision);
        if (decision.BusinessAllowed)
            await Shell.Current.GoToAsync("..");
    });

    [RelayCommand]
    private Task OpenDeviceActivationAsync()
        => Shell.Current.GoToAsync("device-activation");

    private void Apply(ClientUpgradeDecision decision)
    {
        Heading = decision.UpgradeRequired
            ? Text.UpgradeRequired
            : Text.StartupBlocked;
        CurrentVersion = decision.CurrentVersion;
        LatestVersion = decision.LatestVersion;
        MinimumVersion = decision.MinimumVersion;
        DownloadUrl = decision.DownloadUri?.AbsoluteUri ?? string.Empty;
        ReleaseSha256 = decision.Sha256 ?? string.Empty;
        CanDownloadUpdate = decision.CanDownload;
        Message = decision.ErrorCode ?? string.Empty;
    }
}
