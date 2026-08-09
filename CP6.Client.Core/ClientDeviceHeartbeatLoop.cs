using CP6.Client.Api;
using Microsoft.Extensions.Logging;

namespace CP6.Client.Core;

public interface IClientDeviceHeartbeatSender
{
    Task<ClientDevice> SendAsync(
        string? currentTaskNo = null,
        int? batteryPercent = null,
        string? networkType = null,
        CancellationToken ct = default);
}

public sealed record ClientDeviceHeartbeatSnapshot(
    bool IsActivated,
    string? CurrentTaskNo = null,
    int? BatteryPercent = null,
    string? NetworkType = null);

public interface IClientDeviceHeartbeatContext
{
    ValueTask<ClientDeviceHeartbeatSnapshot> CaptureAsync(
        CancellationToken ct = default);

    void MarkActivationRequired();
}

public sealed class ClientDeviceHeartbeatSchedule
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(10);
}

public enum ClientDeviceHeartbeatStatus
{
    Stopped,
    WaitingForSession,
    WaitingForActivation,
    Online,
    Offline,
    Rejected,
}

public sealed class ClientDeviceHeartbeatStateChangedEventArgs(
    ClientDeviceHeartbeatStatus status,
    string? errorCode,
    DateTimeOffset? lastSucceededAt) : EventArgs
{
    public ClientDeviceHeartbeatStatus Status { get; } = status;
    public string? ErrorCode { get; } = errorCode;
    public DateTimeOffset? LastSucceededAt { get; } = lastSucceededAt;
}

public sealed class ClientDeviceHeartbeatLoop : IAsyncDisposable
{
    private static readonly HashSet<string> RejectionCodes =
    [
        "WM-DEVICE-DISABLED",
        "WM-DEVICE-NOT-FOUND",
        "WM-DEVICE-NOT-ACTIVATED",
        "WM-DEVICE-PLATFORM-MISMATCH",
        "WM-DEVICE-SIGNATURE-INVALID",
    ];

    private readonly IClientDeviceHeartbeatSender _sender;
    private readonly IClientDeviceHeartbeatContext _context;
    private readonly IClientSessionService _sessions;
    private readonly ClientDeviceHeartbeatSchedule _schedule;
    private readonly ILogger<ClientDeviceHeartbeatLoop> _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private CancellationTokenSource? _runnerCancellation;
    private Task? _runner;
    private bool _disposed;
    private ClientDeviceHeartbeatStatus? _lastStatus;
    private string? _lastErrorCode;
    private DateTimeOffset? _lastSucceededAt;

    public ClientDeviceHeartbeatLoop(
        IClientDeviceHeartbeatSender sender,
        IClientDeviceHeartbeatContext context,
        IClientSessionService sessions,
        ClientDeviceHeartbeatSchedule schedule,
        ILogger<ClientDeviceHeartbeatLoop> logger)
    {
        if (schedule.Interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(schedule), "Heartbeat interval must be positive.");
        if (schedule.RetryInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(schedule), "Heartbeat retry interval must be positive.");

        _sender = sender;
        _context = context;
        _sessions = sessions;
        _schedule = schedule;
        _logger = logger;
        _sessions.SessionChanged += SessionsOnSessionChanged;
    }

    public event EventHandler<ClientDeviceHeartbeatStateChangedEventArgs>? StateChanged;

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _lifecycleGate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runner is { IsCompleted: false })
            {
                RequestImmediate();
                return;
            }

            while (_wake.Wait(0))
            {
            }

            _runnerCancellation?.Dispose();
            _runnerCancellation = new CancellationTokenSource();
            _runner = RunAsync(_runnerCancellation.Token);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        Task? runner;
        CancellationTokenSource? cancellation;

        await _lifecycleGate.WaitAsync(ct);
        try
        {
            runner = _runner;
            cancellation = _runnerCancellation;
            _runner = null;
            _runnerCancellation = null;
            cancellation?.Cancel();
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (runner is not null)
        {
            try
            {
                await runner.WaitAsync(ct);
            }
            catch (OperationCanceledException) when (
                cancellation?.IsCancellationRequested == true)
            {
            }
        }

        cancellation?.Dispose();
        Publish(ClientDeviceHeartbeatStatus.Stopped);
    }

    public void RequestImmediate()
    {
        if (_disposed || _wake.CurrentCount != 0)
            return;

        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _sessions.SessionChanged -= SessionsOnSessionChanged;
        await StopAsync();
        _lifecycleGate.Dispose();
        _wake.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var delay = TimeSpan.Zero;
        while (!ct.IsCancellationRequested)
        {
            if (delay > TimeSpan.Zero)
                await WaitForDelayOrWakeAsync(delay, ct);

            ct.ThrowIfCancellationRequested();
            var outcome = await TrySendAsync(ct);
            delay = outcome == SendOutcome.TransientFailure
                ? _schedule.RetryInterval
                : _schedule.Interval;
        }
    }

    private async Task<SendOutcome> TrySendAsync(CancellationToken ct)
    {
        if (_sessions.Current is null)
        {
            Publish(ClientDeviceHeartbeatStatus.WaitingForSession);
            return SendOutcome.Skipped;
        }

        ClientDeviceHeartbeatSnapshot snapshot;
        try
        {
            snapshot = await _context.CaptureAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Client device heartbeat context capture failed: {ErrorType}",
                ex.GetType().Name);
            Publish(ClientDeviceHeartbeatStatus.Offline, "E-CLIENT-HEARTBEAT-CONTEXT");
            return SendOutcome.TransientFailure;
        }

        if (!snapshot.IsActivated)
        {
            Publish(ClientDeviceHeartbeatStatus.WaitingForActivation);
            return SendOutcome.Skipped;
        }

        try
        {
            await _sender.SendAsync(
                snapshot.CurrentTaskNo,
                snapshot.BatteryPercent,
                snapshot.NetworkType,
                ct);
            _lastSucceededAt = DateTimeOffset.UtcNow;
            Publish(ClientDeviceHeartbeatStatus.Online);
            return SendOutcome.Sent;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException ex) when (
            ex.Code is not null && RejectionCodes.Contains(ex.Code))
        {
            _logger.LogWarning(
                "Client device heartbeat was rejected: {ErrorCode}",
                ex.Code);
            _context.MarkActivationRequired();
            try
            {
                await _sessions.LogoutAsync(ct);
            }
            catch (Exception logoutError)
            {
                _logger.LogWarning(
                    "Client session cleanup after heartbeat rejection failed: {ErrorType}",
                    logoutError.GetType().Name);
            }
            Publish(ClientDeviceHeartbeatStatus.Rejected, ex.Code);
            return SendOutcome.Rejected;
        }
        catch (Exception ex)
        {
            var errorCode = ex is ApiException api
                ? api.Code
                : "E-CLIENT-HEARTBEAT-UNAVAILABLE";
            _logger.LogWarning(
                "Client device heartbeat failed: {ErrorType} {ErrorCode}",
                ex.GetType().Name,
                errorCode);
            Publish(ClientDeviceHeartbeatStatus.Offline, errorCode);
            return SendOutcome.TransientFailure;
        }
    }

    private async Task WaitForDelayOrWakeAsync(TimeSpan delay, CancellationToken ct)
    {
        using var waitCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delayTask = Task.Delay(delay, waitCancellation.Token);
        var wakeTask = _wake.WaitAsync(waitCancellation.Token);
        var completed = await Task.WhenAny(delayTask, wakeTask);
        waitCancellation.Cancel();
        try
        {
            await completed;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
    }

    private void SessionsOnSessionChanged(object? sender, TokenSession? session)
        => RequestImmediate();

    private void Publish(
        ClientDeviceHeartbeatStatus status,
        string? errorCode = null)
    {
        if (_lastStatus == status
            && string.Equals(
                _lastErrorCode,
                errorCode,
                StringComparison.Ordinal))
            return;

        _lastStatus = status;
        _lastErrorCode = errorCode;
        var handlers = StateChanged;
        if (handlers is null)
            return;

        var args = new ClientDeviceHeartbeatStateChangedEventArgs(
            status,
            errorCode,
            _lastSucceededAt);
        foreach (EventHandler<ClientDeviceHeartbeatStateChangedEventArgs> handler
                 in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Client device heartbeat state observer failed: {ErrorType}",
                    ex.GetType().Name);
            }
        }
    }

    private enum SendOutcome
    {
        Skipped,
        Sent,
        TransientFailure,
        Rejected,
    }
}

internal sealed class InactiveClientDeviceHeartbeatContext
    : IClientDeviceHeartbeatContext
{
    public ValueTask<ClientDeviceHeartbeatSnapshot> CaptureAsync(
        CancellationToken ct = default)
        => ValueTask.FromResult(new ClientDeviceHeartbeatSnapshot(false));

    public void MarkActivationRequired()
    {
    }
}
