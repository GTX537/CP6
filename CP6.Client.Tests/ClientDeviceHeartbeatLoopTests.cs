using System.Net;
using CP6.Client.Api;
using CP6.Client.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Client.Tests;

public sealed class ClientDeviceHeartbeatLoopTests
{
    [Fact]
    public async Task SendsImmediatelyAndContinuesWithoutTaskPageLoads()
    {
        var sessions = FakeSession.Authenticated();
        var context = new FakeHeartbeatContext
        {
            Snapshot = new ClientDeviceHeartbeatSnapshot(
                true,
                "MOVE-001",
                74,
                "WiFi"),
        };
        var sender = new FakeHeartbeatSender();
        await using var loop = CreateLoop(sender, context, sessions);

        await loop.StartAsync();
        await sender.WaitForCallsAsync(2);
        await loop.StopAsync();

        Assert.True(sender.CallCount >= 2);
        Assert.All(sender.Snapshots, snapshot =>
        {
            Assert.Equal("MOVE-001", snapshot.CurrentTaskNo);
            Assert.Equal(74, snapshot.BatteryPercent);
            Assert.Equal("WiFi", snapshot.NetworkType);
        });
    }

    [Fact]
    public async Task SessionChangeWakesLoopAndSendsImmediately()
    {
        var sessions = new FakeSession();
        var context = new FakeHeartbeatContext
        {
            Snapshot = new ClientDeviceHeartbeatSnapshot(true),
        };
        var sender = new FakeHeartbeatSender();
        await using var loop = CreateLoop(
            sender,
            context,
            sessions,
            interval: TimeSpan.FromMinutes(1));

        await loop.StartAsync();
        await Task.Delay(40);
        Assert.Equal(0, sender.CallCount);

        sessions.SetAuthenticated();
        await sender.WaitForCallsAsync(1);
        await loop.StopAsync();

        Assert.Equal(1, sender.CallCount);
    }

    [Fact]
    public async Task RejectedDeviceClearsActivationAndSession()
    {
        var sessions = FakeSession.Authenticated();
        var context = new FakeHeartbeatContext
        {
            Snapshot = new ClientDeviceHeartbeatSnapshot(true),
        };
        var sender = new FakeHeartbeatSender
        {
            Handler = () => Task.FromException<ClientDevice>(
                new ApiException(
                    HttpStatusCode.Conflict,
                    "WM-DEVICE-DISABLED",
                    "Device disabled.")),
        };
        var rejected = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var loop = CreateLoop(sender, context, sessions);
        loop.StateChanged += (_, state) =>
        {
            if (state.Status == ClientDeviceHeartbeatStatus.Rejected)
                rejected.TrySetResult(state.ErrorCode);
        };

        await loop.StartAsync();
        var errorCode = await rejected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("WM-DEVICE-DISABLED", errorCode);
        Assert.True(context.ActivationWasCleared);
        Assert.True(sessions.LogoutCalled);
        Assert.Null(sessions.Current);
    }

    [Fact]
    public async Task RepeatedWakeRequestsNeverOverlapHeartbeatWrites()
    {
        var sessions = FakeSession.Authenticated();
        var context = new FakeHeartbeatContext
        {
            Snapshot = new ClientDeviceHeartbeatSnapshot(true),
        };
        var firstCallEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCall = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new FakeHeartbeatSender
        {
            Handler = async () =>
            {
                firstCallEntered.TrySetResult();
                await releaseFirstCall.Task;
                return new ClientDevice();
            },
        };
        await using var loop = CreateLoop(sender, context, sessions);

        await loop.StartAsync();
        await firstCallEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        for (var i = 0; i < 10; i++)
            loop.RequestImmediate();
        await Task.Delay(40);

        Assert.Equal(1, sender.MaxConcurrentCalls);
        releaseFirstCall.TrySetResult();
        await sender.WaitForCallsAsync(2);
        Assert.Equal(1, sender.MaxConcurrentCalls);
    }

    private static ClientDeviceHeartbeatLoop CreateLoop(
        FakeHeartbeatSender sender,
        FakeHeartbeatContext context,
        FakeSession sessions,
        TimeSpan? interval = null)
        => new(
            sender,
            context,
            sessions,
            new ClientDeviceHeartbeatSchedule
            {
                Interval = interval ?? TimeSpan.FromMilliseconds(25),
                RetryInterval = TimeSpan.FromMilliseconds(10),
            },
            NullLogger<ClientDeviceHeartbeatLoop>.Instance);

    private sealed class FakeHeartbeatSender : IClientDeviceHeartbeatSender
    {
        private readonly object _sync = new();
        private int _activeCalls;
        private int _callCount;

        public Func<Task<ClientDevice>> Handler { get; set; } =
            () => Task.FromResult(new ClientDevice());

        public int CallCount => Volatile.Read(ref _callCount);
        public int MaxConcurrentCalls { get; private set; }
        public List<ClientDeviceHeartbeatSnapshot> Snapshots { get; } = [];

        public async Task<ClientDevice> SendAsync(
            string? currentTaskNo = null,
            int? batteryPercent = null,
            string? networkType = null,
            CancellationToken ct = default)
        {
            var active = Interlocked.Increment(ref _activeCalls);
            lock (_sync)
            {
                MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, active);
                Snapshots.Add(new ClientDeviceHeartbeatSnapshot(
                    true,
                    currentTaskNo,
                    batteryPercent,
                    networkType));
            }
            Interlocked.Increment(ref _callCount);
            try
            {
                return await Handler();
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        public async Task WaitForCallsAsync(int expected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (CallCount < expected)
                await Task.Delay(5, timeout.Token);
        }
    }

    private sealed class FakeHeartbeatContext : IClientDeviceHeartbeatContext
    {
        public required ClientDeviceHeartbeatSnapshot Snapshot { get; init; }
        public bool ActivationWasCleared { get; private set; }

        public ValueTask<ClientDeviceHeartbeatSnapshot> CaptureAsync(
            CancellationToken ct = default)
            => ValueTask.FromResult(Snapshot with
            {
                IsActivated = Snapshot.IsActivated && !ActivationWasCleared,
            });

        public void MarkActivationRequired()
            => ActivationWasCleared = true;
    }

    private sealed class FakeSession : IClientSessionService
    {
        private TokenSession? _current;

        public event EventHandler<TokenSession?>? SessionChanged;
        public TokenSession? Current => Volatile.Read(ref _current);
        public string? AccessToken => Current?.AccessToken;
        public bool LogoutCalled { get; private set; }

        public static FakeSession Authenticated()
        {
            var session = new FakeSession();
            session.SetAuthenticated();
            return session;
        }

        public void SetAuthenticated()
        {
            var current = new TokenSession
            {
                AccessToken = "access",
                RefreshToken = "refresh",
                Profile = new ClientProfile { UserName = "operator" },
            };
            Interlocked.Exchange(ref _current, current);
            SessionChanged?.Invoke(this, current);
        }

        public Task LogoutAsync(CancellationToken ct = default)
        {
            LogoutCalled = true;
            Interlocked.Exchange(ref _current, null);
            SessionChanged?.Invoke(this, null);
            return Task.CompletedTask;
        }

        public Task<bool> RestoreAsync(CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<NativeAuthResult> LoginAsync(
            string userName,
            string password,
            string? tenantCode,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<NativeAuthResult> QuickSwitchAsync(
            string tenantCode,
            string badgeNo,
            string pin,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<TwoFactorSetup> SetupTwoFactorAsync(
            string challenge,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task RequestEmailOtpAsync(
            string challenge,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<NativeAuthResult> VerifyTwoFactorAsync(
            string challenge,
            string code,
            string? method,
            bool enroll,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task AdoptAsync(
            TokenSession session,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task RefreshMergedAsync(
            string? observedAccessToken = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
