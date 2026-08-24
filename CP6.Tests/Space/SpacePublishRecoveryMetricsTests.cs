using System.Text;
using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.WebApi.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Prometheus;

namespace CP6.Tests.Space;

public sealed class SpacePublishRecoveryMetricsSnapshotProviderTests
{
    private static readonly Guid TenantA =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime NowUtc =
        new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Snapshot_uses_audit_state_time_and_aggregates_all_tenants()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await SeedAsync(
            databaseName,
            TenantA,
            SpacePublishAttemptStatus.WaitingRetry,
            NowUtc.AddHours(-2),
            NowUtc.AddMinutes(-20));
        await SeedAsync(
            databaseName,
            TenantB,
            SpacePublishAttemptStatus.ManualIntervention,
            NowUtc.AddHours(-8),
            NowUtc.AddHours(-5));
        await SeedAsync(
            databaseName,
            TenantB,
            SpacePublishAttemptStatus.ReconciliationRequired,
            NowUtc.AddHours(-6),
            NowUtc.AddHours(-3));

        await using var context = NewContext(databaseName, TenantA);
        Assert.Single(await context.PublishAttempts.ToArrayAsync());
        var snapshot = await new SpacePublishRecoveryMetricsSnapshotProvider(
            context,
            new FixedClock(NowUtc)).GetSnapshotAsync();

        AssertState(snapshot, SpacePublishRecoveryMetricStates.WaitingRetry,
            count: 1, oldestAgeSeconds: 1_200, breaches: 1);
        AssertState(snapshot, SpacePublishRecoveryMetricStates.ManualIntervention,
            count: 1, oldestAgeSeconds: 18_000, breaches: 1);
        AssertState(snapshot, SpacePublishRecoveryMetricStates.ReconciliationRequired,
            count: 1, oldestAgeSeconds: 10_800, breaches: 0);
        Assert.Equal(
            SpacePublishRecoveryMetricStates.All.OrderBy(value => value),
            snapshot.ByState.Keys.OrderBy(value => value));
    }

    [Fact]
    public async Task Missing_audit_uses_started_time_and_future_time_clamps_to_zero()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await SeedAsync(
            databaseName,
            TenantA,
            SpacePublishAttemptStatus.WaitingRetry,
            NowUtc.AddMinutes(1),
            enteredAtUtc: null);

        await using var context = NewContext(databaseName, TenantA);
        var snapshot = await new SpacePublishRecoveryMetricsSnapshotProvider(
            context,
            new FixedClock(NowUtc)).GetSnapshotAsync();

        AssertState(snapshot, SpacePublishRecoveryMetricStates.WaitingRetry,
            count: 1, oldestAgeSeconds: 0, breaches: 0);
        AssertState(snapshot, SpacePublishRecoveryMetricStates.ManualIntervention,
            count: 0, oldestAgeSeconds: 0, breaches: 0);
        AssertState(snapshot, SpacePublishRecoveryMetricStates.ReconciliationRequired,
            count: 0, oldestAgeSeconds: 0, breaches: 0);
    }

    private static async Task SeedAsync(
        string databaseName,
        Guid tenantId,
        SpacePublishAttemptStatus status,
        DateTime startedAtUtc,
        DateTime? enteredAtUtc)
    {
        await using var context = NewContext(databaseName, tenantId);
        var jobId = Guid.NewGuid();
        var attempt = SpacePublishAttempt.Create(
            tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            baseVersionId: null,
            "cp6-wms-v1",
            Guid.NewGuid().ToString("N"),
            new string('a', 64),
            Guid.NewGuid(),
            approvedBy: null,
            approvalReference: null,
            "{}",
            startedAtUtc,
            Guid.NewGuid());
        attempt.BindInitialJob(jobId);
        attempt.BeginPreflight();
        var eventType = status switch
        {
            SpacePublishAttemptStatus.WaitingRetry => Wait(attempt),
            SpacePublishAttemptStatus.ManualIntervention => Manual(attempt),
            SpacePublishAttemptStatus.ReconciliationRequired =>
                Reconcile(attempt),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        context.PublishAttempts.Add(attempt);
        if (enteredAtUtc.HasValue)
        {
            context.PublishAuditEvents.Add(SpacePublishAuditEvent.Create(
                tenantId,
                attempt.Id,
                jobId,
                batchId: null,
                eventNo: 1,
                eventType,
                attempt.Status,
                attempt.CurrentStep,
                Guid.NewGuid(),
                attempt.CorrelationId,
                enteredAtUtc.Value,
                Guid.NewGuid().ToString("N"),
                "Recovery state entered.",
                attempt.LastErrorCode,
                "{}",
                previousEventHash: null));
        }
        await context.SaveChangesAsync();
    }

    private static SpacePublishAuditEventType Wait(SpacePublishAttempt attempt)
    {
        attempt.WaitForRetry(
            SpacePublishStep.Preflight,
            "WMS_TIMEOUT",
            "Retry scheduled.");
        return SpacePublishAuditEventType.RetryScheduled;
    }

    private static SpacePublishAuditEventType Manual(
        SpacePublishAttempt attempt)
    {
        attempt.RequireManualIntervention(
            "WMS_RETRY_EXHAUSTED",
            "Operator action required.");
        return SpacePublishAuditEventType.ManualInterventionRequired;
    }

    private static SpacePublishAuditEventType Reconcile(
        SpacePublishAttempt attempt)
    {
        attempt.BeginApplyingWms();
        attempt.RequireReconciliation(
            "WMS_RESULT_UNCERTAIN",
            "Reconciliation required.");
        return SpacePublishAuditEventType.ReconciliationRequired;
    }

    private static SpaceContext NewContext(string databaseName, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new SpaceContext(
            options,
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            new FixedClock(NowUtc));
    }

    private static void AssertState(
        SpacePublishRecoveryMetricsSnapshot snapshot,
        string state,
        long count,
        double oldestAgeSeconds,
        long breaches)
    {
        var metrics = snapshot.ByState[state];
        Assert.Equal(count, metrics.Count);
        Assert.Equal(oldestAgeSeconds, metrics.OldestAgeSeconds);
        Assert.Equal(breaches, metrics.SloBreachedCount);
    }

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId) : ISpaceExecutionContext;

    private sealed record FixedClock(DateTime UtcNow) : ISpaceClock;
}

public sealed class SpacePublishRecoveryMetricsCollectorTests
{
    [Fact]
    public void Alert_rules_bind_frozen_metrics_to_the_runbook()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."));
        var path = Path.Combine(
            root,
            "deploy",
            "monitoring",
            "prometheus",
            "space-publish-alerts.yml");
        Assert.True(File.Exists(path), path);
        var rules = File.ReadAllText(path);

        Assert.Contains(
            "CP6SpacePublishAutomaticRecoverySloBreach",
            rules);
        Assert.Contains(
            "cp6_space_publish_recovery_slo_breaches{state=\"waiting_retry\"} > 0",
            rules);
        Assert.Contains(
            "CP6SpacePublishManualRecoverySloBreach",
            rules);
        Assert.Contains(
            "manual_intervention|reconciliation_required",
            rules);
        Assert.Contains("CP6SpacePublishRecoveryMetricsAbsent", rules);
        Assert.Contains("docs/space/runbooks/publish-recovery.md", rules);
        Assert.DoesNotContain("tenant", rules, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("site_id", rules, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attempt_id", rules, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scrape_has_fixed_states_targets_and_no_tenant_dimension()
    {
        var provider = new DelegateProvider(_ => Task.FromResult(Snapshot(
            waiting: new(2, 1_200, 1),
            manual: new(1, 18_000, 1),
            reconciliation: new(3, 600, 0))));
        using var harness = new CollectorHarness(provider);
        harness.Collector.Register();

        var text = await ScrapeAsync(harness.Registry);

        Assert.Contains(
            "cp6_space_publish_recovery_attempts{state=\"waiting_retry\"} 2",
            text);
        Assert.Contains(
            "cp6_space_publish_recovery_oldest_age_seconds{state=\"manual_intervention\"} 18000",
            text);
        Assert.Contains(
            "cp6_space_publish_recovery_slo_breaches{state=\"reconciliation_required\"} 0",
            text);
        Assert.Contains(
            "cp6_space_publish_recovery_target_seconds{state=\"waiting_retry\"} 900",
            text);
        Assert.Contains(
            "cp6_space_publish_recovery_target_seconds{state=\"manual_intervention\"} 14400",
            text);
        Assert.DoesNotContain("tenant=", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("site=", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attempt=", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Snapshot_failure_keeps_last_values_and_logs_safe_classification()
    {
        const string secret = "password=do-not-log WMS response body";
        var provider = new DelegateProvider(call => call == 1
            ? Task.FromResult(Snapshot(
                waiting: new(1, 901, 1),
                manual: new(0, 0, 0),
                reconciliation: new(0, 0, 0)))
            : throw new InvalidOperationException(secret));
        using var harness = new CollectorHarness(provider);
        harness.Collector.Register();

        _ = await ScrapeAsync(harness.Registry);
        var second = await ScrapeAsync(harness.Registry);

        Assert.Contains(
            "cp6_space_publish_recovery_slo_breaches{state=\"waiting_retry\"} 1",
            second);
        var entry = Assert.Single(harness.Logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Contains(
            SpacePublishRecoveryMetricsCollector.SnapshotFailureReasonCode,
            entry.Message);
        Assert.Contains("InvalidOperationException", entry.Message);
        Assert.Matches("[A-F0-9]{64}", entry.Message);
        Assert.DoesNotContain(secret, entry.Message);
    }

    [Fact]
    public async Task Disabled_registration_resolves_no_collector()
    {
        var registry = Metrics.NewCustomRegistry();
        var resolutions = 0;
        var services = new ServiceCollection();
        services.AddSingleton(provider =>
        {
            resolutions++;
            return new SpacePublishRecoveryMetricsCollector(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new CapturingLogger<SpacePublishRecoveryMetricsCollector>(),
                registry);
        });
        using var root = services.BuildServiceProvider();

        var registered = SpacePublishRecoveryMetricsRegistration
            .RegisterIfEnabled(false, root);
        var text = await ScrapeAsync(registry);

        Assert.False(registered);
        Assert.Equal(0, resolutions);
        Assert.DoesNotContain("cp6_space_publish_recovery_", text);
    }

    private static SpacePublishRecoveryMetricsSnapshot Snapshot(
        SpacePublishRecoveryStateMetrics waiting,
        SpacePublishRecoveryStateMetrics manual,
        SpacePublishRecoveryStateMetrics reconciliation) =>
        new(new Dictionary<string, SpacePublishRecoveryStateMetrics>(
            StringComparer.Ordinal)
        {
            [SpacePublishRecoveryMetricStates.WaitingRetry] = waiting,
            [SpacePublishRecoveryMetricStates.ManualIntervention] = manual,
            [SpacePublishRecoveryMetricStates.ReconciliationRequired] =
                reconciliation,
        });

    private static async Task<string> ScrapeAsync(
        CollectorRegistry registry)
    {
        using var stream = new MemoryStream();
        await registry.CollectAndExportAsTextAsync(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private sealed class CollectorHarness : IDisposable
    {
        private readonly ServiceProvider _root;

        public CollectorHarness(
            ISpacePublishRecoveryMetricsSnapshotProvider provider)
        {
            Registry = Metrics.NewCustomRegistry();
            Logger = new CapturingLogger<SpacePublishRecoveryMetricsCollector>();
            var services = new ServiceCollection();
            services.AddScoped(_ => provider);
            _root = services.BuildServiceProvider();
            Collector = new SpacePublishRecoveryMetricsCollector(
                _root.GetRequiredService<IServiceScopeFactory>(),
                Logger,
                Registry);
        }

        public CollectorRegistry Registry { get; }
        public CapturingLogger<SpacePublishRecoveryMetricsCollector> Logger { get; }
        public SpacePublishRecoveryMetricsCollector Collector { get; }

        public void Dispose() => _root.Dispose();
    }

    private sealed class DelegateProvider(
        Func<int, Task<SpacePublishRecoveryMetricsSnapshot>> getSnapshot) :
        ISpacePublishRecoveryMetricsSnapshotProvider
    {
        private int _calls;

        public Task<SpacePublishRecoveryMetricsSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default) =>
            getSnapshot(Interlocked.Increment(ref _calls));
    }

    public sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception);

    public sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                exception));
    }
}
