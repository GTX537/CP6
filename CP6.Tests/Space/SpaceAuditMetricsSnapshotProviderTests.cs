using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using CP6.WebApi.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Prometheus;

namespace CP6.Tests.Space;

public sealed class SpaceAuditMetricsSnapshotProviderTests
{
    private static readonly Guid TenantA =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Snapshot_ignores_tenant_filter_without_exposing_tenant_dimension()
    {
        await using var db = NewDb(TenantA);
        db.SpaceAuditEvents.AddRange(
            Audit(TenantA, SpaceAuditOutcome.Started),
            Audit(TenantA, SpaceAuditOutcome.Succeeded),
            Audit(TenantB, SpaceAuditOutcome.Failed),
            Audit(TenantB, SpaceAuditOutcome.Denied),
            Audit(TenantB, "TenantB-secret-dirty-outcome"),
            Audit(TenantB, null!));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(2, await db.SpaceAuditEvents.CountAsync());

        var snapshot =
            await new SpaceAuditMetricsSnapshotProvider(db).GetSnapshotAsync();

        Assert.Equal(6L, snapshot.Total);
        Assert.Equal(
            new[]
            {
                SpaceAuditOutcome.Denied,
                SpaceAuditOutcome.Failed,
                SpaceAuditOutcome.Started,
                SpaceAuditOutcome.Succeeded,
            },
            snapshot.ByOutcome.Keys.OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(snapshot.ByOutcome.Values, count => Assert.Equal(1L, count));
        Assert.DoesNotContain(
            "TenantB-secret-dirty-outcome",
            snapshot.ByOutcome.Keys);
        Assert.DoesNotContain(
            typeof(SpaceAuditMetricsSnapshot).GetProperties(),
            property => property.Name.Contains(
                "Tenant",
                StringComparison.OrdinalIgnoreCase));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Empty_snapshot_has_long_zero_and_all_fixed_outcomes()
    {
        await using var db = NewDb(TenantA);

        var snapshot =
            await new SpaceAuditMetricsSnapshotProvider(db).GetSnapshotAsync();

        Assert.Equal(0L, snapshot.Total);
        Assert.Equal(4, snapshot.ByOutcome.Count);
        Assert.All(snapshot.ByOutcome.Values, count => Assert.Equal(0L, count));
    }

    [Fact]
    public void Core_assembly_has_no_prometheus_dependency()
    {
        var references = typeof(SpaceAuditMetricsSnapshotProvider)
            .Assembly
            .GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            reference => reference.Name?.StartsWith(
                "Prometheus",
                StringComparison.OrdinalIgnoreCase) == true);
    }

    private static CP6Context NewDb(Guid tenantId)
    {
        var tenant = new TenantContext { CurrentTenantId = tenantId };
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(
                Guid.NewGuid().ToString(),
                inMemory => inMemory.EnableNullChecks(false))
            .Options;
        return new CP6Context(options, tenant);
    }

    private static Space_AuditEvent Audit(Guid tenantId, string outcome) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        OccurredAtUtc = DateTime.UtcNow,
        ActorType = "User",
        ActorId = "metrics-test-user",
        Action = "space.metrics.test",
        ResourceType = "Floor",
        Outcome = outcome,
        CorrelationId = Guid.NewGuid(),
        TraceId = "0123456789abcdef0123456789abcdef",
    };
}

public sealed class SpaceAuditMetricsCollectorTests
{
    private static readonly string[] FixedOutcomes =
    [
        SpaceAuditOutcome.Started,
        SpaceAuditOutcome.Succeeded,
        SpaceAuditOutcome.Failed,
        SpaceAuditOutcome.Denied,
    ];

    [Fact]
    public async Task Real_scrape_has_only_fixed_outcomes_and_no_tenant_label()
    {
        var constructor = Assert.Single(
            typeof(SpaceAuditMetricsCollector).GetConstructors());
        Assert.DoesNotContain(
            constructor.GetParameters(),
            parameter => parameter.ParameterType == typeof(IMetricFactory));

        var provider = new DelegateProvider((_, _) => Task.FromResult(
            Snapshot(
                12,
                (SpaceAuditOutcome.Started, 2),
                (SpaceAuditOutcome.Succeeded, 4),
                (SpaceAuditOutcome.Failed, 3),
                (SpaceAuditOutcome.Denied, 1),
                ("tenant-11111111-secret", 2))));
        using var harness = new CollectorHarness(provider);
        harness.Collector.Register();

        var text = await ScrapeAsync(harness.Registry);

        Assert.Contains("cp6_space_audit_event_total 12", text);
        Assert.Contains(
            "cp6_space_audit_event_by_outcome{outcome=\"Started\"} 2",
            text);
        Assert.Contains(
            "cp6_space_audit_event_by_outcome{outcome=\"Succeeded\"} 4",
            text);
        Assert.Contains(
            "cp6_space_audit_event_by_outcome{outcome=\"Failed\"} 3",
            text);
        Assert.Contains(
            "cp6_space_audit_event_by_outcome{outcome=\"Denied\"} 1",
            text);
        Assert.DoesNotContain("tenant=", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenant-11111111-secret", text);
        var outcomeSamples = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith(
                "cp6_space_audit_event_by_outcome{",
                StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(4, outcomeSamples.Length);
        Assert.All(
            outcomeSamples,
            line => Assert.Contains(
                FixedOutcomes,
                outcome => line.Contains(
                    $"outcome=\"{outcome}\"",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Register_twice_adds_one_callback_and_one_metric_family()
    {
        var provider = new DelegateProvider((_, _) => Task.FromResult(
            Snapshot(1, (SpaceAuditOutcome.Started, 1))));
        using var harness = new CollectorHarness(provider);

        Parallel.For(0, 32, _ => harness.Collector.Register());
        harness.Collector.Register();
        var text = await ScrapeAsync(harness.Registry);

        Assert.Equal(1, provider.Calls);
        Assert.Equal(
            1,
            CountLines(text, "# HELP cp6_space_audit_event_total "));
        Assert.Equal(
            1,
            CountLines(text, "# HELP cp6_space_audit_event_by_outcome "));
    }

    [Fact]
    public async Task Missing_outcome_is_reset_to_zero_and_dirty_label_is_never_created()
    {
        var provider = new DelegateProvider((call, _) => Task.FromResult(
            call == 1
                ? Snapshot(
                    9,
                    (SpaceAuditOutcome.Failed, 7),
                    ("arbitrary-secret-label", 2))
                : Snapshot(2, (SpaceAuditOutcome.Succeeded, 2))));
        using var harness = new CollectorHarness(provider);
        harness.Collector.Register();

        var first = await ScrapeAsync(harness.Registry);
        var second = await ScrapeAsync(harness.Registry);

        Assert.Contains(
            "cp6_space_audit_event_by_outcome{outcome=\"Failed\"} 7",
            first);
        Assert.Contains(
            "cp6_space_audit_event_by_outcome{outcome=\"Failed\"} 0",
            second);
        Assert.Contains(
            "cp6_space_audit_event_by_outcome{outcome=\"Succeeded\"} 2",
            second);
        Assert.DoesNotContain("arbitrary-secret-label", first);
        Assert.DoesNotContain("arbitrary-secret-label", second);
    }

    [Fact]
    public async Task Disabled_registration_does_not_resolve_collector_or_create_metrics()
    {
        var registry = Metrics.NewCustomRegistry();
        var provider = new DelegateProvider((_, _) => Task.FromResult(
            Snapshot(1, (SpaceAuditOutcome.Started, 1))));
        var resolutions = 0;
        var services = new ServiceCollection();
        services.AddScoped<ISpaceAuditMetricsSnapshotProvider>(_ => provider);
        services.AddSingleton(sp =>
        {
            resolutions++;
            return new SpaceAuditMetricsCollector(
                sp.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<SpaceAuditMetricsCollector>.Instance,
                registry);
        });
        using var root = services.BuildServiceProvider();

        var registered = SpaceAuditMetricsRegistration.RegisterIfEnabled(
            enabled: false,
            root);
        var text = await ScrapeAsync(registry);

        Assert.False(registered);
        Assert.Equal(0, resolutions);
        Assert.DoesNotContain("cp6_space_audit_event_", text);
    }

    [Fact]
    public async Task Aggregation_exception_logs_only_stable_safe_classification()
    {
        const string secret = "password=do-not-log exception body";
        var provider = new DelegateProvider(
            (_, _) => throw new InvalidOperationException(secret));
        using var harness = new CollectorHarness(provider);
        harness.Collector.Register();

        var text = await ScrapeAsync(harness.Registry);

        Assert.Contains("cp6_space_audit_event_total 0", text);
        var entry = Assert.Single(harness.Logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Contains(
            "SPACE_AUDIT_METRICS_SNAPSHOT_FAILED",
            entry.Message);
        Assert.Contains("InvalidOperationException", entry.Message);
        Assert.Matches("[A-F0-9]{64}", entry.Message);
        Assert.DoesNotContain(secret, entry.Message);
        Assert.DoesNotContain("exception body", entry.Message);
    }

    [Fact]
    public async Task Cancellation_propagates_without_failure_log()
    {
        using var cts = new CancellationTokenSource();
        var provider = new DelegateProvider((_, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot(0));
        });
        using var harness = new CollectorHarness(provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Collector.CollectAsync(cts.Token));

        Assert.Equal(1, provider.Calls);
        Assert.Empty(harness.Logger.Entries);
    }

    [Fact]
    public async Task Concurrent_collections_are_serialized()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maxActive = 0;
        var provider = new DelegateProvider(async (call, ct) =>
        {
            var current = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref maxActive, current);
            try
            {
                if (call == 1)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(ct);
                }

                return Snapshot(call);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });
        using var harness = new CollectorHarness(provider);

        var first = harness.Collector.CollectAsync(CancellationToken.None);
        await entered.Task;
        var second = harness.Collector.CollectAsync(CancellationToken.None);
        var premature = await Task.WhenAny(second, Task.Delay(100));

        Assert.NotSame(second, premature);
        Assert.Equal(1, provider.Calls);

        release.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(2, provider.Calls);
        Assert.Equal(1, maxActive);
    }

    private static SpaceAuditMetricsSnapshot Snapshot(
        long total,
        params (string Outcome, long Count)[] counts) =>
        new(
            total,
            counts.ToDictionary(
                x => x.Outcome,
                x => x.Count,
                StringComparer.Ordinal));

    private static int CountLines(string text, string prefix) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.StartsWith(prefix, StringComparison.Ordinal));

    private static async Task<string> ScrapeAsync(
        CollectorRegistry registry,
        CancellationToken ct = default)
    {
        using var stream = new MemoryStream();
        await registry.CollectAndExportAsTextAsync(stream, ct);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private sealed class CollectorHarness : IDisposable
    {
        private readonly ServiceProvider _root;

        public CollectorHarness(ISpaceAuditMetricsSnapshotProvider provider)
        {
            Registry = Metrics.NewCustomRegistry();
            Logger = new CapturingLogger<SpaceAuditMetricsCollector>();
            var services = new ServiceCollection();
            services.AddScoped<ISpaceAuditMetricsSnapshotProvider>(_ => provider);
            _root = services.BuildServiceProvider();
            Collector = new SpaceAuditMetricsCollector(
                _root.GetRequiredService<IServiceScopeFactory>(),
                Logger,
                Registry);
        }

        public CollectorRegistry Registry { get; }
        public SpaceAuditMetricsCollector Collector { get; }
        public CapturingLogger<SpaceAuditMetricsCollector> Logger { get; }

        public void Dispose() => _root.Dispose();
    }

    private sealed class DelegateProvider : ISpaceAuditMetricsSnapshotProvider
    {
        private readonly Func<
            int,
            CancellationToken,
            Task<SpaceAuditMetricsSnapshot>> _getSnapshot;
        private int _calls;

        public DelegateProvider(
            Func<
                int,
                CancellationToken,
                Task<SpaceAuditMetricsSnapshot>> getSnapshot)
        {
            _getSnapshot = getSnapshot;
        }

        public int Calls => Volatile.Read(ref _calls);

        public Task<SpaceAuditMetricsSnapshot> GetSnapshotAsync(
            CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref _calls);
            return _getSnapshot(call, ct);
        }
    }

    public sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception);

    public sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                exception));
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref target);
                if (candidate <= current)
                    return;
                if (Interlocked.CompareExchange(
                        ref target,
                        candidate,
                        current) == current)
                    return;
            }
        }
    }
}
