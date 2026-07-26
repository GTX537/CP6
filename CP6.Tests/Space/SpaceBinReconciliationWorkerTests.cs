using System.Diagnostics;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CP6.Tests.Space;

public sealed class SpaceBinReconciliationWorkerTests
{
    [Fact]
    public async Task ProcessOnce_creates_distinct_system_context_per_tenant_and_audits_counts()
    {
        await using var host = await BuildHostAsync(2);
        using var ambient = StartRootActivity("SpaceBinReconciliationWorkerTests.Ambient");
        var expectedAmbient = Activity.Current;

        await CreateWorker(host).ProcessOnceAsync();

        Assert.Same(expectedAmbient, Activity.Current);
        Assert.Equal(4, host.Audit.Calls.Count);
        Assert.Equal(
            host.Tenants.OrderBy(x => x),
            host.Audit.Calls.Select(x => x.Context.TenantId).Distinct().OrderBy(x => x));

        foreach (var tenantId in host.Tenants)
        {
            var calls = host.Audit.Calls
                .Where(x => x.Context.TenantId == tenantId)
                .ToList();
            Assert.Collection(
                calls,
                started =>
                {
                    Assert.Equal(SpaceAuditOutcome.Started, started.Input.Outcome);
                    AssertSummaryContract(started.Input);
                    Assert.Null(started.Input.Evidence);
                },
                succeeded =>
                {
                    Assert.Equal(SpaceAuditOutcome.Succeeded, succeeded.Input.Outcome);
                    AssertSummaryContract(succeeded.Input);
                    Assert.Equal(1, succeeded.Input.Evidence?.ItemCount);
                    Assert.Equal("Completed", succeeded.Input.Evidence?.Status);
                });

            Assert.All(calls, call =>
            {
                Assert.Equal(SpaceExecutionContext.SystemActor, call.Context.ActorType);
                Assert.Equal("space-worker:bin-reconciliation", call.Context.ActorId);
                Assert.Equal(tenantId, call.Context.TenantId);
                Assert.NotEqual(Guid.Empty, call.Context.CorrelationId);
                Assert.NotNull(call.Context.JobId);
                Assert.NotEqual(Guid.Empty, call.Context.JobId);
                Assert.NotNull(call.Context.RunId);
                Assert.NotEqual(Guid.Empty, call.Context.RunId);
                AssertValidW3CTraceId(call.Context.TraceId);
            });
            Assert.Single(calls.Select(x => x.Context.CorrelationId).Distinct());
            Assert.Single(calls.Select(x => x.Context.JobId).Distinct());
            Assert.Single(calls.Select(x => x.Context.RunId).Distinct());
            Assert.Single(calls.Select(x => x.Context.TraceId).Distinct());
        }

        Assert.Equal(
            host.Tenants.Count,
            host.Audit.Calls.Select(x => x.Context.CorrelationId).Distinct().Count());
        Assert.Equal(
            host.Tenants.Count,
            host.Audit.Calls.Select(x => x.Context.JobId).Distinct().Count());
        Assert.Equal(
            host.Tenants.Count,
            host.Audit.Calls.Select(x => x.Context.RunId).Distinct().Count());
        Assert.Equal(
            host.Tenants.Count,
            host.Audit.Calls.Select(x => x.Context.TraceId).Distinct().Count());
        Assert.All(
            host.Audit.Accessors.Distinct(),
            accessor =>
            {
                Assert.Null(accessor.Current);
                Assert.Null(accessor.OutcomeCurrent);
            });

        var summaries = host.Logger.Entries
            .Where(x => x.State.ContainsKey("DriftCount"))
            .ToList();
        Assert.Equal(host.Tenants.Count, summaries.Count);
        Assert.All(summaries, entry =>
        {
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Null(entry.Exception);
            Assert.Equal(1, entry.State["DriftCount"]);
            Assert.Contains(entry.State["TenantId"], host.Tenants.Cast<object>());
            Assert.IsType<Guid>(entry.State["CorrelationId"]);
            Assert.DoesNotContain("LocationCode", entry.State.Keys);
            Assert.DoesNotContain("LocationId", entry.State.Keys);
            Assert.DoesNotContain("BinVersion", entry.State.Keys);
        });
        Assert.DoesNotContain(
            host.LocationCodes,
            code => host.Logger.Entries.Any(entry =>
                entry.Message.Contains(code, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ProcessOnce_zero_drift_audits_completed_and_logs_information_summary_safely()
    {
        await using var host = await BuildHostAsync(
            1,
            hasDrifts: false);

        await CreateWorker(host).ProcessOnceAsync();

        Assert.Equal(
            [SpaceAuditOutcome.Started, SpaceAuditOutcome.Succeeded],
            host.Audit.Calls.Select(x => x.Input.Outcome));
        var succeeded = host.Audit.Calls[1];
        Assert.Equal(0, succeeded.Input.Evidence?.ItemCount);
        Assert.Equal("Completed", succeeded.Input.Evidence?.Status);

        var summary = Assert.Single(
            host.Logger.Entries,
            x => x.State.ContainsKey("DriftCount"));
        Assert.Equal(LogLevel.Information, summary.Level);
        Assert.Null(summary.Exception);
        Assert.Equal(host.Tenants[0], summary.State["TenantId"]);
        Assert.Equal(succeeded.Context.CorrelationId, summary.State["CorrelationId"]);
        Assert.Equal(0, summary.State["DriftCount"]);
        Assert.DoesNotContain("LocationCode", summary.State.Keys);
        Assert.DoesNotContain("LocationId", summary.State.Keys);
        Assert.DoesNotContain("BinVersion", summary.State.Keys);
        Assert.DoesNotContain(
            host.LocationCodes,
            code => summary.Message.Contains(code, StringComparison.Ordinal));
        Assert.DoesNotContain(
            "payload",
            summary.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            host.Logger.Entries,
            x => x.Level == LogLevel.Warning &&
                 x.State.ContainsKey("DriftCount"));
    }

    [Theory]
    [InlineData(AuditBehavior.ReturnFalse)]
    [InlineData(AuditBehavior.Throw)]
    public async Task ProcessOnce_audit_failure_does_not_block_scan_and_logs_only_safe_metadata(
        AuditBehavior behavior)
    {
        await using var host = await BuildHostAsync(1, behavior);

        await CreateWorker(host).ProcessOnceAsync();

        Assert.Equal(
            [SpaceAuditOutcome.Started, SpaceAuditOutcome.Succeeded],
            host.Audit.Calls.Select(x => x.Input.Outcome));
        var summary = Assert.Single(
            host.Logger.Entries,
            x => x.State.ContainsKey("DriftCount"));
        Assert.Equal(1, summary.State["DriftCount"]);

        var auditFailures = host.Logger.Entries
            .Where(x => x.State.ContainsKey("ErrorFingerprint") &&
                        x.State.ContainsKey("AuditOutcome"))
            .ToList();
        Assert.Equal(2, auditFailures.Count);
        Assert.All(auditFailures, entry =>
        {
            Assert.Null(entry.Exception);
            Assert.Equal(
                "SPACE_RECONCILIATION_AUDIT_WRITE_FAILED",
                entry.State["ReasonCode"]);
            Assert.False(string.IsNullOrWhiteSpace(entry.State["ErrorType"]?.ToString()));
            Assert.Matches("^[0-9A-F]{64}$", entry.State["ErrorFingerprint"]?.ToString());
            Assert.DoesNotContain(AuditState.SecretFailureMessage, entry.Message);
            Assert.DoesNotContain("payload", entry.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task ProcessOnce_scanner_failure_audits_failed_safely_and_continues_other_tenants()
    {
        await using var host = await BuildHostAsync(
            2,
            AuditBehavior.DisposeDatabaseAfterStarted);

        await CreateWorker(host).ProcessOnceAsync();

        var groups = host.Audit.Calls
            .GroupBy(x => x.Context.TenantId)
            .ToList();
        Assert.Equal(2, groups.Count);
        var failedGroup = Assert.Single(
            groups,
            group => group.Any(x =>
                x.Input.Outcome == SpaceAuditOutcome.Failed));
        Assert.Equal(
            [SpaceAuditOutcome.Started, SpaceAuditOutcome.Failed],
            failedGroup.Select(x => x.Input.Outcome));
        Assert.DoesNotContain(
            failedGroup,
            x => x.Input.Outcome == SpaceAuditOutcome.Succeeded);
        var succeededGroup = Assert.Single(
            groups,
            group => group.Any(x =>
                x.Input.Outcome == SpaceAuditOutcome.Succeeded));
        Assert.Equal(
            [SpaceAuditOutcome.Started, SpaceAuditOutcome.Succeeded],
            succeededGroup.Select(x => x.Input.Outcome));

        var failed = failedGroup.Last().Input;
        AssertSummaryContract(failed);
        Assert.Equal("SPACE_RECONCILIATION_SCAN_FAILED", failed.ReasonCode);
        Assert.Equal("Failed", failed.Evidence?.Status);
        Assert.Equal(nameof(ObjectDisposedException), failed.Evidence?.ExceptionType);
        Assert.Matches("^[0-9A-F]{64}$", failed.Evidence?.ErrorFingerprint);

        var failure = Assert.Single(
            host.Logger.Entries,
            x =>
                Equals(
                    x.State.GetValueOrDefault("ReasonCode"),
                    "SPACE_RECONCILIATION_SCAN_FAILED"));
        Assert.Equal(LogLevel.Error, failure.Level);
        Assert.Null(failure.Exception);
        Assert.Equal(nameof(ObjectDisposedException), failure.State["ErrorType"]);
        Assert.Matches("^[0-9A-F]{64}$", failure.State["ErrorFingerprint"]?.ToString());
        Assert.DoesNotContain(
            host.LocationCodes,
            code => failure.Message.Contains(code, StringComparison.Ordinal));
        Assert.DoesNotContain("payload", failure.Message, StringComparison.OrdinalIgnoreCase);
        var summary = Assert.Single(
            host.Logger.Entries,
            x => x.State.ContainsKey("DriftCount"));
        Assert.Equal(succeededGroup.Key, summary.State["TenantId"]);
        Assert.Equal(1, summary.State["DriftCount"]);
        Assert.All(
            host.Audit.Accessors.Distinct(),
            accessor =>
            {
                Assert.Null(accessor.Current);
                Assert.Null(accessor.OutcomeCurrent);
            });
    }

    [Fact]
    public async Task ProcessOnce_cancellation_propagates_without_failed_or_succeeded_audit()
    {
        using var cancellation = new CancellationTokenSource();
        await using var host = await BuildHostAsync(
            1,
            AuditBehavior.CancelAfterStarted,
            cancellation);
        using var ambient = StartRootActivity(
            "SpaceBinReconciliationWorkerTests.CancellationAmbient");
        var expectedAmbient = Activity.Current;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateWorker(host).ProcessOnceAsync(cancellation.Token));

        Assert.Same(expectedAmbient, Activity.Current);
        Assert.Equal(
            [SpaceAuditOutcome.Started],
            host.Audit.Calls.Select(x => x.Input.Outcome));
        Assert.DoesNotContain(
            host.Logger.Entries,
            x => Equals(
                x.State.GetValueOrDefault("ReasonCode"),
                "SPACE_RECONCILIATION_SCAN_FAILED"));
        Assert.All(
            host.Audit.Accessors.Distinct(),
            accessor =>
            {
                Assert.Null(accessor.Current);
                Assert.Null(accessor.OutcomeCurrent);
            });
    }

    [Fact]
    public async Task ProcessOnce_cancelled_host_wins_scanner_exception_without_failed_audit()
    {
        using var cancellation = new CancellationTokenSource();
        await using var host = await BuildHostAsync(
            1,
            AuditBehavior.DisposeDatabaseAndCancelAfterStarted,
            cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateWorker(host).ProcessOnceAsync(cancellation.Token));

        Assert.Equal(
            [SpaceAuditOutcome.Started],
            host.Audit.Calls.Select(x => x.Input.Outcome));
        Assert.DoesNotContain(
            host.Audit.Calls,
            x => x.Input.Outcome == SpaceAuditOutcome.Failed);
        Assert.DoesNotContain(
            host.Logger.Entries,
            x => Equals(
                x.State.GetValueOrDefault("ReasonCode"),
                "SPACE_RECONCILIATION_SCAN_FAILED"));
    }

    [Theory]
    [InlineData(AuditBehavior.CancelAndReturnFalseOnFailed)]
    [InlineData(AuditBehavior.CancelAndThrowOnFailed)]
    [InlineData(AuditBehavior.CancelAndThrowOperationCanceledOnFailed)]
    public async Task ProcessOnce_cancellation_during_failed_audit_wins_without_succeeded_or_degraded_failure_log(
        AuditBehavior behavior)
    {
        using var cancellation = new CancellationTokenSource();
        await using var host = await BuildHostAsync(
            1,
            behavior,
            cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateWorker(host).ProcessOnceAsync(cancellation.Token));

        Assert.Equal(
            [SpaceAuditOutcome.Started, SpaceAuditOutcome.Failed],
            host.Audit.Calls.Select(x => x.Input.Outcome));
        Assert.DoesNotContain(
            host.Audit.Calls,
            x => x.Input.Outcome == SpaceAuditOutcome.Succeeded);
        Assert.DoesNotContain(
            host.Logger.Entries,
            x => Equals(
                x.State.GetValueOrDefault("ReasonCode"),
                "SPACE_RECONCILIATION_SCAN_FAILED"));
        Assert.DoesNotContain(
            host.Logger.Entries,
            x => Equals(
                x.State.GetValueOrDefault("AuditOutcome"),
                SpaceAuditOutcome.Failed));
    }

    private static void AssertSummaryContract(SpaceAuditEventInput input)
    {
        Assert.Equal("space.reconciliation.scan", input.Action);
        Assert.Equal("SpaceBin", input.ResourceType);
        Assert.Null(input.ResourceId);
        Assert.Equal("Worker", input.ClientType);
    }

    private static void AssertValidW3CTraceId(string traceId)
    {
        Assert.Matches("^[0-9a-f]{32}$", traceId);
        Assert.NotEqual(new string('0', 32), traceId);
        Assert.Equal(
            traceId,
            ActivityTraceId.CreateFromString(traceId.AsSpan()).ToHexString());
    }

    private static SpaceBinReconciliationWorker CreateWorker(TestHost host)
        => new(
            host.Provider.GetRequiredService<IServiceScopeFactory>(),
            host.Logger);

    private static Activity StartRootActivity(string name)
        => new Activity(name)
            .SetIdFormat(ActivityIdFormat.W3C)
            .SetParentId(
                ActivityTraceId.CreateRandom(),
                ActivitySpanId.CreateRandom(),
                ActivityTraceFlags.None)
            .Start();

    private static async Task<TestHost> BuildHostAsync(
        int tenantCount,
        AuditBehavior behavior = AuditBehavior.Succeed,
        CancellationTokenSource? cancellation = null,
        bool hasDrifts = true)
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w =>
                w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var audit = new AuditState(behavior, cancellation);
        var logger = new CapturingLogger<SpaceBinReconciliationWorker>();
        var services = new ServiceCollection();
        services.AddSingleton(audit);
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITenantEnumerator, TenantEnumerator>();
        services.AddScoped(sp => new CP6Context(
            options,
            sp.GetRequiredService<ITenantContext>()));
        services.AddScoped<SpaceExecutionContextAccessor>();
        services.AddScoped<ISpaceExecutionContextAccessor>(
            sp => sp.GetRequiredService<SpaceExecutionContextAccessor>());
        services.AddScoped<ISpaceExecutionContextManager>(
            sp => sp.GetRequiredService<SpaceExecutionContextAccessor>());
        services.AddScoped<ISpaceAuditWriter, RecordingAuditWriter>();
        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            });

        var tenants = Enumerable.Range(0, tenantCount)
            .Select(_ => Guid.NewGuid())
            .ToList();
        var locationCodes = tenants
            .Select((_, index) => $"SENSITIVE-LOCATION-{index + 1}")
            .ToList();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
            db.Sys_Tenants.AddRange(
                tenants.Select((tenantId, index) =>
                    new Sys_Tenant
                    {
                        Id = tenantId,
                        TenantCode = $"T{index + 1}",
                        TenantName = $"Tenant {index + 1}",
                        Enable = true,
                    }));
            await db.SaveChangesAsync();
        }

        for (var index = 0; index < tenants.Count; index++)
        {
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>()
                .CurrentTenantId = tenants[index];
            var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
            var id = Guid.NewGuid();
            db.Space_Locations.Add(new Space_Location
            {
                Id = id,
                Status = 1,
                LocationCode = locationCodes[index],
            });
            db.WmsBins.Add(new WmsBin
            {
                Id = id,
                LocationCode = locationCodes[index],
                WarehouseCd = "W1",
                IsActive = !hasDrifts,
                Version = index + 1,
            });
            await db.SaveChangesAsync();
        }

        return new TestHost(
            provider,
            audit,
            logger,
            tenants,
            locationCodes);
    }

    public enum AuditBehavior
    {
        Succeed,
        ReturnFalse,
        Throw,
        DisposeDatabaseAfterStarted,
        CancelAfterStarted,
        DisposeDatabaseAndCancelAfterStarted,
        CancelAndReturnFalseOnFailed,
        CancelAndThrowOnFailed,
        CancelAndThrowOperationCanceledOnFailed,
    }

    private sealed class AuditState
    {
        public const string SecretFailureMessage =
            "raw payload SECRET-AUDIT-WRITER-MESSAGE";

        private readonly AuditBehavior _behavior;
        private readonly CancellationTokenSource? _cancellation;
        private bool _disposedDatabase;

        public AuditState(
            AuditBehavior behavior,
            CancellationTokenSource? cancellation)
        {
            _behavior = behavior;
            _cancellation = cancellation;
        }

        public List<AuditCall> Calls { get; } = [];
        public List<ISpaceExecutionContextAccessor> Accessors { get; } = [];

        public bool Record(
            SpaceAuditEventInput input,
            ISpaceExecutionContext context,
            ISpaceExecutionContextAccessor accessor,
            CP6Context db)
        {
            Calls.Add(new AuditCall(input, context));
            Accessors.Add(accessor);
            if (input.Outcome == SpaceAuditOutcome.Started)
            {
                if ((_behavior ==
                     AuditBehavior.DisposeDatabaseAfterStarted ||
                     IsFailedAuditCancellationBehavior(_behavior)) &&
                    !_disposedDatabase)
                {
                    _disposedDatabase = true;
                    db.Dispose();
                }
                if (_behavior is
                    AuditBehavior.CancelAfterStarted or
                    AuditBehavior.DisposeDatabaseAndCancelAfterStarted)
                {
                    if (_behavior ==
                        AuditBehavior.DisposeDatabaseAndCancelAfterStarted)
                    {
                        db.Dispose();
                    }
                    _cancellation!.Cancel();
                }
            }
            else if (input.Outcome == SpaceAuditOutcome.Failed &&
                     IsFailedAuditCancellationBehavior(_behavior))
            {
                _cancellation!.Cancel();
                return _behavior switch
                {
                    AuditBehavior.CancelAndReturnFalseOnFailed => false,
                    AuditBehavior.CancelAndThrowOnFailed =>
                        throw new InvalidOperationException(
                            SecretFailureMessage),
                    AuditBehavior.CancelAndThrowOperationCanceledOnFailed =>
                        throw new OperationCanceledException(
                            _cancellation.Token),
                    _ => throw new InvalidOperationException(
                        "Unexpected audit behavior"),
                };
            }

            return _behavior switch
            {
                AuditBehavior.ReturnFalse => false,
                AuditBehavior.Throw => throw new InvalidOperationException(
                    SecretFailureMessage),
                _ => true,
            };
        }

        private static bool IsFailedAuditCancellationBehavior(
            AuditBehavior behavior)
            => behavior is
                AuditBehavior.CancelAndReturnFalseOnFailed or
                AuditBehavior.CancelAndThrowOnFailed or
                AuditBehavior.CancelAndThrowOperationCanceledOnFailed;
    }

    private sealed record AuditCall(
        SpaceAuditEventInput Input,
        ISpaceExecutionContext Context);

    private sealed class RecordingAuditWriter : ISpaceAuditWriter
    {
        private readonly ISpaceExecutionContextAccessor _accessor;
        private readonly AuditState _state;
        private readonly CP6Context _db;

        public RecordingAuditWriter(
            ISpaceExecutionContextAccessor accessor,
            AuditState state,
            CP6Context db)
        {
            _accessor = accessor;
            _state = state;
            _db = db;
        }

        public Task<bool> TryAppendAsync(
            SpaceAuditEventInput input,
            CancellationToken ct = default)
            => Task.FromResult(_state.Record(
                input,
                _accessor.RequireCurrent(),
                _accessor,
                _db));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties =
                state as IEnumerable<KeyValuePair<string, object?>>;
            Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                exception,
                properties?.ToDictionary(x => x.Key, x => x.Value) ??
                new Dictionary<string, object?>()));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> State);

    private sealed record TestHost(
        ServiceProvider Provider,
        AuditState Audit,
        CapturingLogger<SpaceBinReconciliationWorker> Logger,
        IReadOnlyList<Guid> Tenants,
        IReadOnlyList<string> LocationCodes) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }
}
