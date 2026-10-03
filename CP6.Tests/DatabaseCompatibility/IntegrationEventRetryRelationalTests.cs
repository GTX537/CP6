using System.Collections.Concurrent;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Options;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Integration;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit.Abstractions;

namespace CP6.Tests.DatabaseCompatibility;

/// <summary>
/// Actual non-Space worker, dispatcher routing and durable alert writes on migrated Core.
/// The MES hook records the external boundary; controlled enumeration selects one actual enabled fixture tenant.
/// </summary>
[Collection(CoreBusinessRelationalCollection.Name)]
public sealed class IntegrationEventRetryRelationalTests(CoreBusinessRelationalFixture fixture, ITestOutputHelper output)
{
    [CoreBusinessFact]
    public async Task Due_non_space_event_uses_actual_dispatcher_and_persists_success()
    {
        var own = await SeedAsync(attempts: 1, addFutureEvent: true);
        var hook = new RecordingMesHook(failingCalls: 0);
        await using var provider = Provider(own.Tenant, hook);
        var logger = new WorkerFailureObserver();
        using var worker = Worker(provider, logger);
        await ScanAsync(worker, logger);

        var saved = await ReadAsync(own);
        Assert.Equal(IntegrationEventStatus.Success, saved.Status);
        Assert.Equal(2, saved.Attempts);
        Assert.Null(saved.NextRetryAt);
        AssertNativeVersionChanged(own.RowVersion, saved.RowVersion);
        AssertOriginalIdentity(own, saved);
        Assert.Equal(new[] { own.SourceNo }, hook.Calls.Select(call => call.SourceNo).ToArray());
        Assert.All(hook.Calls, call => Assert.Equal("wp4-native-worker", call.UserName));
        await using (var verify = fixture.CreateContext(own.Tenant))
        {
            var future = await verify.IntegrationEvents.AsNoTracking().SingleAsync(evt => evt.Id == own.FutureEventId);
            Assert.Equal(IntegrationEventStatus.Failed, future.Status);
            Assert.Equal(0, future.Attempts);
            Assert.Equal(own.FutureDueAtUtc, future.NextRetryAt);
            Assert.Equal(own.FutureRowVersion, future.RowVersion);
            Assert.False(await verify.Sys_OperLogs.AnyAsync(log => log.IsAlert && log.RequestUrl == AlertUrl(own.EventId)));
        }

        // Another actual scan must leave the already successful event and future event untouched.
        await ScanAsync(worker, logger);
        Assert.Single(hook.Calls);
        Assert.Equal(2, (await ReadAsync(own)).Attempts);
        output.WriteLine(fixture.SetupSummary + "; actual ERP->MES route persisted success; future event and successful event were not dispatched again.");
    }

    [CoreBusinessFact]
    public async Task Real_persisted_backoff_recovers_without_rewriting_due_time_or_alert()
    {
        var own = await SeedAsync(attempts: 0);
        var hook = new RecordingMesHook(failingCalls: 1);
        await using var provider = Provider(own.Tenant, hook);
        var logger = new WorkerFailureObserver();
        using var worker = Worker(provider, logger);
        var before = DateTime.UtcNow;
        await ScanAsync(worker, logger);
        var after = DateTime.UtcNow;
        var first = await ReadAsync(own);
        Assert.Equal(IntegrationEventStatus.Failed, first.Status);
        Assert.Equal(1, first.Attempts);
        Assert.Contains(RecordingMesHook.FailureMessage, first.LastError ?? "");
        AssertBackoff(first, before, after, RetryOptions().GetBackoffSeconds(1));
        AssertNativeVersionChanged(own.RowVersion, first.RowVersion);
        AssertOriginalIdentity(own, first);
        Assert.Single(hook.Calls);

        await WaitForPersistedDueAsync(first.NextRetryAt!.Value);
        await ScanAsync(worker, logger);
        var recovered = await ReadAsync(own);
        Assert.Equal(IntegrationEventStatus.Success, recovered.Status);
        Assert.Equal(2, recovered.Attempts);
        Assert.Null(recovered.NextRetryAt);
        Assert.Equal(2, hook.Calls.Count);
        Assert.All(hook.Calls, call => Assert.Equal(own.SourceNo, call.SourceNo));
        AssertNativeVersionChanged(first.RowVersion!, recovered.RowVersion);
        AssertOriginalIdentity(own, recovered);
        await using var verify = fixture.CreateContext(own.Tenant);
        Assert.False(await verify.Sys_OperLogs.AnyAsync(log => log.IsAlert && log.RequestUrl == AlertUrl(own.EventId)));
        output.WriteLine("Actual DateTime.UtcNow worker; configured backoff [2,3,4] seconds; awaited the stored UTC due time without editing NextRetryAt. Hook is a recording boundary.");
    }

    [CoreBusinessFact]
    public async Task Real_backoff_exhaustion_persists_dead_letter_and_actual_alert_log()
    {
        var own = await SeedAsync(attempts: 0);
        var hook = new RecordingMesHook(failingCalls: int.MaxValue);
        await using var provider = Provider(own.Tenant, hook);
        var logger = new WorkerFailureObserver();
        using var worker = Worker(provider, logger);
        var options = RetryOptions();
        var priorVersion = own.RowVersion;
        for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
        {
            var before = DateTime.UtcNow;
            await ScanAsync(worker, logger);
            var after = DateTime.UtcNow;
            var saved = await ReadAsync(own);
            Assert.Equal(attempt, saved.Attempts);
            Assert.Equal(attempt, hook.Calls.Count);
            Assert.Contains(RecordingMesHook.FailureMessage, saved.LastError ?? "");
            AssertOriginalIdentity(own, saved);
            AssertNativeVersionChanged(priorVersion, saved.RowVersion);
            priorVersion = saved.RowVersion!.ToArray();
            if (attempt < options.MaxAttempts)
            {
                Assert.Equal(IntegrationEventStatus.Failed, saved.Status);
                AssertBackoff(saved, before, after, options.GetBackoffSeconds(attempt));
                await using var beforeExhaustion = fixture.CreateContext(own.Tenant);
                Assert.False(await beforeExhaustion.Sys_OperLogs.AnyAsync(log => log.IsAlert && log.RequestUrl == AlertUrl(own.EventId)));
                await WaitForPersistedDueAsync(saved.NextRetryAt!.Value);
            }
            else
            {
                Assert.Equal(IntegrationEventStatus.DeadLetter, saved.Status);
                Assert.Null(saved.NextRetryAt);
            }
        }

        await using (var verify = fixture.CreateContext(own.Tenant))
        {
            var alert = await verify.Sys_OperLogs.AsNoTracking().SingleAsync(log =>
                log.IsAlert && log.RequestUrl == AlertUrl(own.EventId));
            Assert.Equal(own.Tenant, alert.TenantId);
            Assert.Equal("system", alert.UserName);
            Assert.Equal("BACKGROUND", alert.HttpMethod);
            Assert.Equal("IntegrationEvent", alert.Controller);
            Assert.Equal("OnOrderCreatedAsync", alert.Action);
            Assert.Equal(500, alert.StatusCode);
            Assert.Contains(own.SourceNo, alert.RequestBody ?? "");
            Assert.Contains("attempts=3", alert.RequestBody ?? "");
            Assert.Contains(RecordingMesHook.FailureMessage, alert.RequestBody ?? "");
        }
        await ScanAsync(worker, logger);
        Assert.Equal(options.MaxAttempts, hook.Calls.Count);
        Assert.Equal(IntegrationEventStatus.DeadLetter, (await ReadAsync(own)).Status);
        await using var final = fixture.CreateContext(own.Tenant);
        Assert.Equal(1, await final.Sys_OperLogs.CountAsync(log => log.IsAlert && log.RequestUrl == AlertUrl(own.EventId)));
        output.WriteLine(fixture.SetupSummary + "; three real polls separated by stored 2s/3s backoff; actual DeadLetterNotifier persisted one alert. No SignalR acceptance or non-Space claim guarantee is asserted.");
    }

    private async Task<OwnEvent> SeedAsync(int attempts, bool addFutureEvent = false)
    {
        var tenant = Guid.NewGuid();
        var evt = NewEvent(tenant, attempts, DateTime.UtcNow.AddSeconds(-5));
        var future = addFutureEvent ? NewEvent(tenant, 0, DateTime.UtcNow.AddHours(1)) : null;
        await using var seed = fixture.CreateContext(tenant);
        seed.Sys_Tenants.Add(new Sys_Tenant
        {
            Id = tenant, TenantCode = "wp4-retry-" + tenant.ToString("N"),
            TenantName = "WP4 non-Space retry fixture", Enable = true
        });
        seed.IntegrationEvents.Add(evt);
        if (future is not null) seed.IntegrationEvents.Add(future);
        await seed.SaveChangesAsync();
        Assert.NotNull(evt.RowVersion);
        Assert.Equal(8, evt.RowVersion.Length);
        // Capture the future time after native materialization (PostgreSQL stores microsecond precision).
        var futureSnapshot = future is null ? null : await seed.IntegrationEvents.AsNoTracking()
            .SingleAsync(saved => saved.Id == future.Id);
        return new(tenant, evt.Id, evt.SourceNo, evt.CorrelationId, evt.PayloadJson, evt.RowVersion.ToArray(),
            futureSnapshot?.Id, futureSnapshot?.NextRetryAt, futureSnapshot?.RowVersion?.ToArray());
    }

    private static IntegrationEvent NewEvent(Guid tenant, int attempts, DateTime dueAtUtc)
    {
        var sourceNo = "WP4RT-" + Guid.NewGuid().ToString("N")[..20];
        return new IntegrationEvent
        {
            Id = Guid.NewGuid(), TenantId = tenant, SourceModule = "ERP", TargetModule = "MES",
            HookName = "OnOrderCreatedAsync", SourceNo = sourceNo, Status = IntegrationEventStatus.Failed,
            Attempts = attempts, NextRetryAt = dueAtUtc, CorrelationId = Guid.NewGuid(),
            PayloadJson = JsonSerializer.Serialize(new { webOrderNo = sourceNo, userName = "wp4-native-worker" }),
            Creator = "wp4-native-worker", CreateDate = DateTime.Now
        };
    }

    private ServiceProvider Provider(Guid tenant, RecordingMesHook hook)
    {
        var profile = DatabaseMigrationProfile.For(fixture.Database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), fixture.Database,
            fixture.ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options;
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped(sp =>
        {
            var context = new CP6Context(options, sp.GetRequiredService<ITenantContext>());
            context.Database.SetCommandTimeout(30);
            return context;
        });
        services.AddScoped<TenantEnumerator>();
        services.AddScoped<ITenantEnumerator>(sp => new OwnTenantEnumerator(sp.GetRequiredService<TenantEnumerator>(), tenant));
        services.AddScoped<IIntegrationEventDispatcher>(_ => new IntegrationEventDispatcher(hook,
            new Mock<IWmsBridgeHook>(MockBehavior.Strict).Object,
            new Mock<IErpBridgeHook>(MockBehavior.Strict).Object,
            new Mock<IOrderCancelBridgeHook>(MockBehavior.Strict).Object,
            new Mock<IFinBridgeHook>(MockBehavior.Strict).Object,
            new Mock<ISpaceBridgeHook>(MockBehavior.Strict).Object));
        services.AddScoped<IDeadLetterNotifier>(sp => new DeadLetterNotifier(sp,
            sp.GetRequiredService<CP6Context>(), NullLogger<DeadLetterNotifier>.Instance));
        return services.BuildServiceProvider();
    }

    private static IntegrationEventOptions RetryOptions() => new()
    {
        Enabled = true, MaxAttempts = 3, BackoffSeconds = [2, 3, 4], PollIntervalSeconds = 60
    };

    private static IntegrationEventRetryWorker Worker(ServiceProvider provider, WorkerFailureObserver logger)
        => new(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(RetryOptions()), logger);

    private static async Task ScanAsync(IntegrationEventRetryWorker worker, WorkerFailureObserver logger)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var scan = worker.ProcessOnceAsync(budget.Token);
        try
        {
            await scan.WaitAsync(budget.Token);
            Assert.Empty(logger.Failures);
        }
        finally
        {
            await budget.CancelAsync();
            try { await scan; }
            catch (OperationCanceledException) when (budget.IsCancellationRequested) { }
            Assert.True(scan.IsCompleted);
        }
    }

    private async Task<IntegrationEvent> ReadAsync(OwnEvent own)
    {
        await using var verify = fixture.CreateContext(own.Tenant);
        return await verify.IntegrationEvents.AsNoTracking().SingleAsync(evt => evt.Id == own.EventId);
    }

    private static void AssertBackoff(IntegrationEvent evt, DateTime before, DateTime after, int seconds)
    {
        Assert.NotNull(evt.NextRetryAt);
        Assert.InRange(evt.NextRetryAt.Value, before.AddSeconds(seconds), after.AddSeconds(seconds));
    }

    private static async Task WaitForPersistedDueAsync(DateTime dueAtUtc)
    {
        // NextRetryAt is explicitly a UTC column; SQL datetime materialization may retain an Unspecified Kind.
        var delay = DateTime.SpecifyKind(dueAtUtc, DateTimeKind.Utc) - DateTime.UtcNow + TimeSpan.FromMilliseconds(50);
        Assert.InRange(delay, TimeSpan.FromMinutes(-1), TimeSpan.FromSeconds(10));
        if (delay > TimeSpan.Zero)
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await Task.Delay(delay, budget.Token);
        }
    }

    private static void AssertOriginalIdentity(OwnEvent original, IntegrationEvent saved)
    {
        Assert.Equal(original.Tenant, saved.TenantId);
        Assert.Equal(original.EventId, saved.Id);
        Assert.Equal(original.SourceNo, saved.SourceNo);
        Assert.Equal(original.CorrelationId, saved.CorrelationId);
        Assert.Equal(original.PayloadJson, saved.PayloadJson);
        Assert.Equal("ERP", saved.SourceModule);
        Assert.Equal("MES", saved.TargetModule);
        Assert.Equal("OnOrderCreatedAsync", saved.HookName);
        Assert.Null(saved.JobId);
        Assert.Null(saved.PublishAttemptId);
        Assert.Null(saved.RetryLeaseId);
        Assert.Null(saved.RetryCompletionLeaseId);
        Assert.Null(saved.RetryCompletionSucceeded);
    }

    private static void AssertNativeVersionChanged(byte[] original, byte[]? current)
    {
        Assert.NotNull(current);
        Assert.Equal(8, current.Length);
        Assert.False(original.AsSpan().SequenceEqual(current));
    }

    private static string AlertUrl(Guid id) => "/integration-event/" + id;

    private sealed record OwnEvent(Guid Tenant, Guid EventId, string SourceNo, Guid CorrelationId, string PayloadJson,
        byte[] RowVersion, Guid? FutureEventId, DateTime? FutureDueAtUtc, byte[]? FutureRowVersion);
    private sealed record HookCall(string SourceNo, string? UserName);

    private sealed class RecordingMesHook(int failingCalls) : IMesBridgeHook
    {
        public const string FailureMessage = "WP4 controlled external MES hook failure";
        private int callCount;
        public ConcurrentQueue<HookCall> Calls { get; } = new();

        public Task<MesBridgeResult> OnOrderCreatedAsync(string webOrderNo, string? userName)
        {
            Calls.Enqueue(new(webOrderNo, userName));
            return Interlocked.Increment(ref callCount) <= failingCalls
                ? Task.FromException<MesBridgeResult>(new InvalidOperationException(FailureMessage))
                : Task.FromResult(MesBridgeResult.Ok(new[] { "WP4-RECORDED-MES" }));
        }
    }

    private sealed class OwnTenantEnumerator(TenantEnumerator actual, Guid tenant) : ITenantEnumerator
    {
        public async Task<IReadOnlyList<Guid>> ListActiveAsync(CancellationToken ct = default)
        {
            var actualEnabledTenants = await actual.ListActiveAsync(ct);
            Assert.Contains(tenant, actualEnabledTenants);
            return new[] { tenant };
        }
    }

    private sealed class WorkerFailureObserver : ILogger<IntegrationEventRetryWorker>
    {
        public ConcurrentQueue<Exception> Failures { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception is not null) Failures.Enqueue(exception);
        }
    }
}
