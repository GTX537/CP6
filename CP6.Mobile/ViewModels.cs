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
        ResetFactor();
        var result = await _sessions.LoginAsync(UserName, Password, EmptyToNull(TenantCode));
        Password = string.Empty;
        await HandleAuthResultAsync(result);
    });

    private async Task HandleAuthResultAsync(NativeAuthResult result)
    {
        if (result.Session != null)
        {
            if (result.RegisteredDeviceVerified) _state.SetDeviceActivated(true);
            ResetFactor();
            await Shell.Current.GoToAsync("tasks");
            return;
        }
        _challenge = result.ChallengeToken ?? throw new InvalidOperationException("当前认证适配未就绪。");
        _enroll = result.State == "enrollmentRequired";
        CanUseEmailOtp = !_enroll;
        ShowTwoFactor = true;
        if (_enroll)
        {
            var setup = await _sessions.SetupTwoFactorAsync(_challenge);
            TwoFactorSetupSecret = setup.Secret;
        }
        Message = _enroll ? Text.EnrollmentRequired : Text.EnterVerificationCode;
    }

    private void ResetFactor()
    {
        _challenge = null;
        _enroll = false;
        TwoFactorSetupSecret = string.Empty;
        TwoFactorCode = string.Empty;
        TwoFactorMethod = "totp";
        ShowTwoFactor = false;
        CanUseEmailOtp = false;
    }

    [RelayCommand]
    private Task VerifyTwoFactorAsync() => RunAsync(async () =>
    {
        EnsureClientAccess();
        if (_challenge == null) return;
        var result = await _sessions.VerifyTwoFactorAsync(
            _challenge, TwoFactorCode, TwoFactorMethod, _enroll);
        await HandleAuthResultAsync(result);
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
            ResetFactor();
            return _sso.StartAsync(TenantCode);
        });

    [RelayCommand]
    private Task QuickSwitchAsync() => RunAsync(async () =>
    {
        EnsureClientAccess();
        ResetFactor();
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
        await HandleAuthResultAsync(await _sso.CompleteAsync(callback));
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
    private Guid _operationId;
    private ClientRequestScope? _scope;
    private bool _completionAttempted;
    private bool _ownerChecked;
    private string? _submittedReason;
    private string? _pendingClientScanNo;
    private string? _pendingScanStep;
    private string? _pendingScanValue;
    private readonly List<OfflineMoveScan> _offlineScans = [];

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
        WeakReferenceMessenger.Default.Register<MoveScanViewModel, MobileSessionChanged>(
            this,
            static (recipient, _) => MainThread.BeginInvokeOnMainThread(recipient.ClearStaleProgress));
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
    private Task LoadAsync() => RunAsync(LoadCoreAsync);

    private async Task LoadCoreAsync()
    {
        var selected = _state.SelectedTask;
        ClearProgress();
        if (selected == null) return;
        var scope = _tasks.CaptureScope();
        _scope = scope;
        var owner = _tasks.InScope(scope);
        var cached = await _offline.ReadAsync(scope, selected.TaskNo);
        try
        {
            var current = await owner.GetAsync(selected.TaskNo);
            RequireScope(scope);
            if (cached != null && WmsTaskService.IsOriginalCompletion(current, cached.CompletionOperationId))
            {
                await _offline.ClearAsync(scope, selected.TaskNo, cached.CompletionOperationId);
                RequireScope(scope);
                ShowCompleted(current);
                return;
            }
            if (cached != null && !_tasks.CanResumeMove(cached.Task, current))
                throw new InvalidOperationException("原作业的归属或执行代次已变化。已保留原草稿，请核对作业。");

            if (current.Status != 1) throw new InvalidOperationException("当前作业未处于可扫描状态。");
            var profile = await owner.GetScanProfileAsync(current);
            RequireScope(scope);
            _ownerChecked = true;
            ShowProgress(current, profile, cached);
            await PersistAsync(scope);
        }
        catch (HttpRequestException error) when (error.StatusCode == null && cached != null)
        {
            RequireScope(scope);
            ShowProgress(cached.Task, cached.ScanProfile, cached);
            IsOfflineProgress = true;
            Message = "WM-OFFLINE-SCAN-CACHED";
        }
    }

    private void ShowProgress(MobileTask current, TaskScanProfile profile, OfflineMoveProgress? saved)
    {
        Task = current;
        _scanProfile = profile;
        _workflow.Reset(profile.Steps.Contains("Lot", StringComparer.OrdinalIgnoreCase));
        _operationId = saved?.CompletionOperationId ?? Guid.NewGuid();
        if (saved != null)
        {
            _completionAttempted = saved.CompletionAttempted;
            _submittedReason = saved.CompletionAttempted ? saved.PartialReason : null;
            _pendingClientScanNo = saved.PendingClientScanNo;
            _pendingScanStep = saved.PendingScanStep;
            _pendingScanValue = saved.PendingScanValue;
            _offlineScans.AddRange(saved.OfflineScans);
            RestoreStep(saved);
        }
        _state.SelectedTask = current;
        _heartbeat.RequestImmediate();
        UpdateStep();
    }

    [RelayCommand]
    private Task SubmitScanAsync() =>
        AcceptScanAsync(ScanValue, ScannerInputSource.Manual);

    private Task SubmitAcceptedScanAsync() => RunAsync(async () =>
    {
        if (Task is not { Status: 1 }) return;
        var scope = RequireScope();
        if (_completionAttempted) throw new InvalidOperationException("请先核对原完成操作。");
        if (_pendingClientScanNo != null &&
            (_pendingScanStep != _workflow.Step.ToString() || _pendingScanValue != ScanValue))
            throw new InvalidOperationException("上一笔扫描结果仍待核对，请使用原扫描内容。");
        if (Connectivity.Current.NetworkAccess == NetworkAccess.None)
        {
            if (_pendingClientScanNo != null) throw new InvalidOperationException("请联网核对上一笔扫描。");
            var deferred = new OfflineMoveScan(Guid.NewGuid().ToString("N"), _workflow.Step.ToString(), ScanValue);
            _workflow.AcceptBarcode(Task, deferred.Value);
            _offlineScans.Add(deferred);
            IsOfflineProgress = true;
            Message = "WM-OFFLINE-SCAN-CACHED";
        }
        else
        {
            await ConfirmOfflineScansAsync(scope);
            RequireScope(scope);
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
            var current = Task;
            await PersistAsync(scope);
            var result = await _tasks.InScope(scope).ScanAsync(
                current, step, ScanValue, _pendingClientScanNo);
            RequireScope(scope);
            ClearPendingScan();
            await PersistAsync(scope);
            RequireScope(scope);
            if (result.Matched == false)
            {
                Message = $"{result.ErrorCode}: {result.RecoveryAction}";
                return;
            }
            _workflow.AcceptValidated(_workflow.Step);
            IsOfflineProgress = false;
        }
        ScanValue = string.Empty;
        await PersistAsync(scope);
        RequireScope(scope);
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
    private Task ConfirmQuantityAsync() => RunAsync(async () =>
    {
        var scope = RequireScope();
        if (_completionAttempted) throw new InvalidOperationException("请先核对原完成操作。");
        if (Task == null
            || !decimal.TryParse(Quantity, NumberStyles.Number, CultureInfo.CurrentCulture, out var value))
            throw new InvalidOperationException("WM-MSG-031");
        _workflow.ConfirmQuantity(Task, value);
        await PersistAsync(scope);
        RequireScope(scope);
        UpdateStep();
    });

    [RelayCommand]
    private Task CompleteAsync() => RunAsync(async () =>
    {
        if (Task is not { Status: 1 }) return;
        var scope = RequireScope();
        if (Connectivity.Current.NetworkAccess == NetworkAccess.None)
            throw new InvalidOperationException("WM-OFFLINE-COMMIT-NOT-ALLOWED");
        var owner = _tasks.InScope(scope);
        var recorded = Task;
        var originalOperation = _operationId;
        if (!_ownerChecked || _completionAttempted)
        {
            var current = await owner.GetAsync(recorded.TaskNo);
            RequireScope(scope);
            if (WmsTaskService.IsOriginalCompletion(current, originalOperation))
            {
                await _offline.ClearAsync(scope, recorded.TaskNo, originalOperation);
                RequireScope(scope);
                ShowCompleted(current);
                return;
            }
            if (!_tasks.CanResumeMove(recorded, current))
                throw new InvalidOperationException("当前作业无法按原归属恢复。已保留原操作。");
            Task = current;
            _ownerChecked = true;
        }

        if (_pendingClientScanNo != null) throw new InvalidOperationException("请先核对原扫描结果。");
        await ConfirmOfflineScansAsync(scope);
        RequireScope(scope);
        var reason = _completionAttempted ? _submittedReason : PartialReason;
        if (_workflow.ConfirmedQuantity < Task.Qty && string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("WM-V2-PARTIAL-REASON-REQUIRED");
        _completionAttempted = true;
        _submittedReason = reason;
        await PersistAsync(scope);
        RequireScope(scope);
        var result = await owner.CompleteAsync(Task, originalOperation, _workflow.ConfirmedQuantity, reason);
        RequireScope(scope);
        if (!WmsTaskService.IsOriginalCompletion(result, originalOperation))
            throw new InvalidOperationException("原完成操作的结果仍待核对。");
        await _offline.ClearAsync(scope, recorded.TaskNo, originalOperation);
        RequireScope(scope);
        ShowCompleted(result);
    });

    private void ShowCompleted(MobileTask current)
    {
        Task = current;
        StepTitle = Text.Completed;
        _state.SelectedTask = current;
        _heartbeat.RequestImmediate();
        Message = Text.MoveCompleted;
        CanComplete = false;
        IsOfflineProgress = false;
    }

    [RelayCommand]
    private Task ReloadTaskAsync() => RunAsync(LoadCoreAsync);

    [RelayCommand]
    private Task ResetScanAsync() => RunAsync(async () =>
    {
        var scope = RequireScope();
        if (Task is not { Status: 1 }) return;
        if (_completionAttempted || _pendingClientScanNo != null)
            throw new InvalidOperationException("请先核对原操作的结果。");
        _offlineScans.Clear();
        _workflow.Reset(_scanProfile?.Steps.Contains("Lot", StringComparer.OrdinalIgnoreCase) == true);
        ScanValue = string.Empty;
        Quantity = string.Empty;
        PartialReason = string.Empty;
        await PersistAsync(scope);
        RequireScope(scope);
        UpdateStep();
    });

    private void ClearStaleProgress()
    {
        if (_scope is { } scope && !_tasks.IsCurrent(scope)) ClearProgress();
    }

    private void ClearProgress()
    {
        _scope = null;
        Task = null;
        _scanProfile = null;
        _operationId = Guid.Empty;
        _completionAttempted = false;
        _ownerChecked = false;
        _submittedReason = null;
        _offlineScans.Clear();
        ClearPendingScan();
        ScanValue = string.Empty;
        Quantity = string.Empty;
        PartialReason = string.Empty;
        StepTitle = string.Empty;
        CanComplete = false;
        IsOfflineProgress = false;
        IsCameraOpen = false;
    }

    private ClientRequestScope RequireScope() => RequireScope(_scope ?? throw new ClientInteractionChangedException());

    private ClientRequestScope RequireScope(ClientRequestScope scope)
    {
        if (_scope != scope || !_tasks.IsCurrent(scope)) throw new ClientInteractionChangedException();
        return scope;
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

    private Task PersistAsync(ClientRequestScope scope)
    {
        RequireScope(scope);
        if (Task == null || Task.Status != 1 || _scanProfile == null)
            return System.Threading.Tasks.Task.CompletedTask;
        return _offline.WriteAsync(scope, new OfflineMoveProgress
        {
            Task = Task,
            ScanProfile = _scanProfile,
            Step = _workflow.Step,
            ConfirmedQuantity = _workflow.ConfirmedQuantity,
            PartialReason = _completionAttempted ? _submittedReason : PartialReason,
            CompletionOperationId = _operationId,
            CompletionAttempted = _completionAttempted,
            PendingClientScanNo = _pendingClientScanNo,
            PendingScanStep = _pendingScanStep,
            PendingScanValue = _pendingScanValue,
            OfflineScans = [.. _offlineScans],
            SavedAt = DateTimeOffset.UtcNow,
        });
    }

    private async Task ConfirmOfflineScansAsync(ClientRequestScope scope)
    {
        if (_offlineScans.Count == 0) return;
        var recorded = Task ?? throw new ClientInteractionChangedException();
        var owner = _tasks.InScope(scope);
        var current = await owner.GetAsync(recorded.TaskNo);
        RequireScope(scope);
        if (!_tasks.CanResumeMove(recorded, current))
            throw new InvalidOperationException("当前作业无法按原归属恢复。已保留待确认的扫描。");

        while (_offlineScans.Count > 0)
        {
            var scan = _offlineScans[0];
            var result = await owner.ScanAsync(current, scan.Step, scan.Value, scan.ClientScanNo);
            RequireScope(scope);
            if (result.Matched != true)
                throw new InvalidOperationException(result.ErrorCode ?? "WM-V2-SCAN-MISMATCH");
            _offlineScans.RemoveAt(0);
            await PersistAsync(scope);
            RequireScope(scope);
        }
        Task = current;
        _ownerChecked = true;
        IsOfflineProgress = false;
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
