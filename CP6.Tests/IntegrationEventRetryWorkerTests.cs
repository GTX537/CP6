using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Options;
using CP6.Core.Services;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs.Space;
using CP6.WebApi.BackgroundServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CP6.Tests;

/// <summary>
/// IntegrationEvent retry worker state transition tests.
/// </summary>
public class IntegrationEventRetryWorkerTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 0)]
    [InlineData(10, 4)]
    [InlineData(10, 10)]
    [InlineData(10, 11)]
    public void Worker_rejects_invalid_space_lease_heartbeat_configuration(
        int leaseSeconds,
        int heartbeatSeconds)
    {
        using var provider =
            new ServiceCollection().BuildServiceProvider();
        var error = Assert.Throws<InvalidOperationException>(
            () => NewWorker(
                provider,
                new IntegrationEventOptions
                {
                    SpaceRetryLeaseSeconds = leaseSeconds,
                    SpaceRetryHeartbeatSeconds =
                        heartbeatSeconds,
                }));
        Assert.Equal(
            "SPACE_RETRY_LEASE_OPTIONS_INVALID",
            error.Message);
    }

    [Fact]
    public void Worker_rejects_nonpositive_dead_letter_notification_lease()
    {
        using var provider =
            new ServiceCollection().BuildServiceProvider();
        var error = Assert.Throws<InvalidOperationException>(
            () => NewWorker(
                provider,
                new IntegrationEventOptions
                {
                    SpaceDeadLetterNotificationLeaseSeconds = 0,
                }));
        Assert.Equal(
            "SPACE_RETRY_LEASE_OPTIONS_INVALID",
            error.Message);
    }

    private sealed class DispatchState
    {
        public ConcurrentBag<ISpaceExecutionContext> Contexts { get; } = [];
        public ConcurrentBag<IntegrationEvent> Events { get; } = [];
        public Action<IntegrationEvent>? BeforeResult { get; set; }
        public Func<CancellationToken, Exception?>?
            FailureFactory { get; set; }
        public Func<
            IntegrationEvent,
            CancellationToken,
            Task<bool>>? AsyncResult { get; set; }
        public bool SaveOperationDbBeforeResult { get; set; }
        public int OperationDbTrackedEventCount { get; set; }
        public Exception? Failure { get; set; }
        public bool Result { get; set; } = true;
    }

    private sealed class RecordingDispatcher : IIntegrationEventDispatcher
    {
        private readonly ISpaceExecutionContextAccessor _accessor;
        private readonly CP6Context _db;
        private readonly DispatchState _state;

        public RecordingDispatcher(
            ISpaceExecutionContextAccessor accessor,
            CP6Context db,
            DispatchState state)
        {
            _accessor = accessor;
            _db = db;
            _state = state;
        }

        public async Task<bool> DispatchAsync(
            IntegrationEvent evt,
            CancellationToken ct = default)
        {
            _state.Contexts.Add(_accessor.RequireCurrent());
            _state.Events.Add(evt);
            _state.BeforeResult?.Invoke(evt);
            _state.OperationDbTrackedEventCount =
                _db.ChangeTracker
                    .Entries<IntegrationEvent>()
                    .Count();
            if (_state.SaveOperationDbBeforeResult)
                _db.SaveChanges();
            var failure =
                _state.FailureFactory?.Invoke(ct) ??
                _state.Failure;
            if (failure is not null)
                throw failure;
            if (_state.AsyncResult is not null)
                return await _state.AsyncResult(evt, ct);
            return _state.Result;
        }
    }

    private sealed class AuditState
    {
        private readonly object _sync = new();
        private readonly Queue<bool> _results = new();

        public List<SpaceAuditEventInput> Inputs { get; } = [];
        public List<ISpaceExecutionContext> Contexts { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public bool DefaultResult { get; set; } = true;
        public Action<SpaceAuditEventInput>? AfterRecord { get; set; }

        public void Return(params bool[] results)
        {
            lock (_sync)
            {
                foreach (var result in results)
                    _results.Enqueue(result);
            }
        }

        public bool Record(
            SpaceAuditEventInput input,
            ISpaceExecutionContext context,
            CancellationToken token)
        {
            bool result;
            lock (_sync)
            {
                Inputs.Add(input);
                Contexts.Add(context);
                Tokens.Add(token);
                result = _results.Count == 0
                    ? DefaultResult
                    : _results.Dequeue();
            }
            AfterRecord?.Invoke(input);
            return result;
        }
    }

    private sealed class RecordingAuditWriter : ISpaceAuditWriter
    {
        private readonly ISpaceExecutionContextAccessor _accessor;
        private readonly AuditState _state;

        public RecordingAuditWriter(
            ISpaceExecutionContextAccessor accessor,
            AuditState state)
        {
            _accessor = accessor;
            _state = state;
        }

        public Task<bool> TryAppendAsync(
            SpaceAuditEventInput input,
            CancellationToken ct = default)
        {
            return Task.FromResult(_state.Record(
                input,
                _accessor.RequireCurrent(),
                ct));
        }
    }

    private sealed class RecordingSpaceDeadLetterNotifier :
        ISpaceDeadLetterNotifier
    {
        private readonly IDeadLetterNotifier _inner;

        public RecordingSpaceDeadLetterNotifier(
            IDeadLetterNotifier inner)
        {
            _inner = inner;
        }

        public async Task<bool> TryNotifyDurablyAsync(
            IntegrationEvent evt,
            Guid notificationLeaseId,
            CancellationToken ct = default)
        {
            await _inner.NotifyAsync(evt, ct);
            return true;
        }
    }

    private sealed class SpaceDeadLetterState :
        ISpaceDeadLetterNotifier
    {
        private readonly ConcurrentQueue<bool> _results = new();
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ConcurrentBag<Guid> LeaseIds { get; } = [];

        public bool DefaultResult { get; set; } = true;

        public void Return(params bool[] results)
        {
            foreach (var result in results)
                _results.Enqueue(result);
        }

        public Task<bool> TryNotifyDurablyAsync(
            IntegrationEvent evt,
            Guid notificationLeaseId,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            LeaseIds.Add(notificationLeaseId);
            return Task.FromResult(
                _results.TryDequeue(out var result)
                    ? result
                    : DefaultResult);
        }
    }

    private sealed class NoopDbCommandInterceptor :
        DbCommandInterceptor
    {
    }

    private sealed class RecordingSpaceRetryFinalizer :
        ISpaceRetryFinalizer
    {
        private readonly CP6Context _db;
        private readonly ISpaceExecutionContextAccessor _accessor;
        private readonly AuditState _audit;

        public RecordingSpaceRetryFinalizer(
            CP6Context db,
            ISpaceExecutionContextAccessor accessor,
            AuditState audit)
        {
            _db = db;
            _accessor = accessor;
            _audit = audit;
        }

        public async Task<SpaceRetryFinalizationResult>
            TryFinalizeAsync(
                SpaceRetryFinalizationInput input,
                CancellationToken ct = default)
        {
            var owned = _db.IntegrationEvents.Where(e =>
                e.Id == input.EventId &&
                e.TenantId == input.TenantId &&
                e.Status == IntegrationEventStatus.Failed &&
                e.RetryLeaseId == input.RetryLeaseId &&
                e.Attempts == input.ExpectedAttempts &&
                e.RetryCompletionLeaseId ==
                    input.ExpectedCompletionLeaseId &&
                e.RetryCompletionSucceeded ==
                    input.ExpectedCompletionSucceeded);
            if (!await owned.AnyAsync(ct))
                return SpaceRetryFinalizationResult.LostLease;

            if (!_audit.Record(
                    input.Audit,
                    _accessor.RequireOutcomeCurrent(),
                    ct))
            {
                return SpaceRetryFinalizationResult.AuditUnavailable;
            }

            if (_db.Database.IsRelational())
            {
                var affected = await owned.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(e => e.Status, input.Status)
                        .SetProperty(
                            e => e.LastError,
                            input.LastError)
                        .SetProperty(
                            e => e.NextRetryAt,
                            input.NextRetryAt)
                        .SetProperty(
                            e => e.RetryLeaseId,
                            (Guid?)null)
                        .SetProperty(
                            e => e.RetryCompletionLeaseId,
                            (Guid?)null)
                        .SetProperty(
                            e => e.RetryCompletionSucceeded,
                            (bool?)null)
                        .SetProperty(
                            e => e.DeadLetterNotifiedAtUtc,
                            (DateTime?)null)
                        .SetProperty(
                            e => e.DeadLetterNotificationLeaseId,
                            (Guid?)null)
                        .SetProperty(
                            e => e.DeadLetterNotificationLeaseUntilUtc,
                            (DateTime?)null),
                    ct);
                return affected == 1
                    ? SpaceRetryFinalizationResult.Committed
                    : SpaceRetryFinalizationResult.LostLease;
            }

            var evt = await owned.SingleAsync(ct);
            evt.Status = input.Status;
            evt.LastError = input.LastError;
            evt.NextRetryAt = input.NextRetryAt;
            evt.RetryLeaseId = null;
            evt.RetryCompletionLeaseId = null;
            evt.RetryCompletionSucceeded = null;
            evt.DeadLetterNotifiedAtUtc = null;
            evt.DeadLetterNotificationLeaseId = null;
            evt.DeadLetterNotificationLeaseUntilUtc = null;
            await _db.SaveChangesAsync(ct);
            return SpaceRetryFinalizationResult.Committed;
        }
    }

    private sealed class SqliteIntegrationContext :
        CP6Context
    {
        public SqliteIntegrationContext(
            DbContextOptions<CP6Context> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<IntegrationEvent>().ToTable(
                "T_IntegrationEvent",
                table => table.HasTrigger(
                    "trg_IntegrationEvent_RowVersion"));
        }
    }

    private sealed class NonSpaceConflictContext :
        CP6Context
    {
        public NonSpaceConflictContext(
            DbContextOptions<CP6Context> options)
            : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            if (ChangeTracker
                .Entries<IntegrationEvent>()
                .Any(entry =>
                    entry.State == EntityState.Modified &&
                    entry.Entity.SourceModule != "SPACE"))
            {
                throw new DbUpdateConcurrencyException(
                    "expected non-Space outer batch conflict");
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class CancelAfterSpaceClaimContext :
        CP6Context
    {
        private readonly CancellationTokenSource _host;

        public CancelAfterSpaceClaimContext(
            DbContextOptions<CP6Context> options,
            CancellationTokenSource host)
            : base(options)
        {
            _host = host;
        }

        public override async Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            var claimed = ChangeTracker
                .Entries<IntegrationEvent>()
                .Any(entry =>
                    entry.State == EntityState.Modified &&
                    entry.Entity.SourceModule == "SPACE" &&
                    entry.Entity.RetryLeaseId.HasValue &&
                    entry.Entity.Attempts == 0);
            var result = await base.SaveChangesAsync(
                cancellationToken);
            if (claimed)
                _host.Cancel();
            return result;
        }
    }

    private sealed class DualIntegrationReadBarrier :
        DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothReaders =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readerCount;

        public override async ValueTask<
            InterceptionResult<DbDataReader>>
            ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "T_IntegrationEvent",
                    StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.TrimStart().StartsWith(
                    "SELECT",
                    StringComparison.OrdinalIgnoreCase))
            {
                var ordinal =
                    Interlocked.Increment(ref _readerCount);
                if (ordinal == 2)
                    _bothReaders.TrySetResult();
                if (ordinal <= 2)
                {
                    await _bothReaders.Task.WaitAsync(
                        cancellationToken);
                }
            }

            return await base.ReaderExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }

    private sealed class BackfillClaimInterleavingInterceptor :
        DbCommandInterceptor
    {
        private readonly TaskCompletionSource _backfillCommitted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseBackfill =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _paused;

        public Task WaitForBackfillAsync(
            CancellationToken ct) =>
            _backfillCommitted.Task.WaitAsync(ct);

        public void ReleaseBackfill() =>
            _releaseBackfill.TrySetResult();

        public override async ValueTask<int>
            NonQueryExecutedAsync(
                DbCommand command,
                CommandExecutedEventData eventData,
                int result,
                CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "UPDATE",
                    StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(
                    "\"JobId\" =",
                    StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(
                    "\"PublishAttemptId\" =",
                    StringComparison.OrdinalIgnoreCase) &&
                Interlocked.CompareExchange(
                    ref _paused,
                    1,
                    0) == 0)
            {
                _backfillCommitted.TrySetResult();
                await _releaseBackfill.Task.WaitAsync(
                    cancellationToken);
            }

            return await base.NonQueryExecutedAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }

    private sealed class ExactThrowingWmsConsumer :
        IWmsLocationConsumer
    {
        private readonly Exception _exception;

        public ExactThrowingWmsConsumer(Exception exception)
            => _exception = exception;

        public Task<WmsConsumeResult> ConsumeAsync(
            LocationPublishBatch batch,
            SpaceRetryFence? retryFence = null,
            CancellationToken ct = default)
            => throw _exception;
    }

    private static DbContextOptions<CP6Context> NewOptions()
    {
        return new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private static async Task<CP6Context> SeedAsync(
        DbContextOptions<CP6Context> options,
        IntegrationEvent evt)
    {
        var db = new CP6Context(options);
        db.IntegrationEvents.Add(evt);
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<Guid> PersistCompletionMarkerAsync(
        DbContextOptions<CP6Context> options,
        Guid eventId,
        bool succeeded)
    {
        await using var markerDb = new CP6Context(options);
        var persisted = await markerDb.IntegrationEvents
            .SingleAsync(evt => evt.Id == eventId);
        var completionLeaseId = Assert.IsType<Guid>(
            persisted.RetryLeaseId);
        persisted.RetryCompletionLeaseId =
            completionLeaseId;
        persisted.RetryCompletionSucceeded = succeeded;
        await markerDb.SaveChangesAsync();
        return completionLeaseId;
    }

    private static IntegrationEvent NewFailedEvent(int attempts, DateTime? nextRetryAt)
    {
        return new IntegrationEvent
        {
            Id = Guid.NewGuid(),
            SourceModule = "MES",
            TargetModule = "WMS",
            HookName = "OnWorkOrderIssuedAsync",
            SourceNo = "WO-001",
            Status = IntegrationEventStatus.Failed,
            Attempts = attempts,
            NextRetryAt = nextRetryAt,
            CorrelationId = Guid.NewGuid(),
            PayloadJson = """{"workOrderNo":"WO-001","userName":"worker"}""",
            Creator = "test",
            CreateDate = DateTime.Now,
        };
    }

    private static IntegrationEvent NewFailedSpaceEvent(
        int attempts = 0,
        Guid? correlationId = null,
        Guid? jobId = null,
        Guid? publishAttemptId = null)
    {
        var evt = NewFailedEvent(attempts, DateTime.UtcNow.AddSeconds(-5));
        evt.SourceModule = "SPACE";
        evt.TargetModule = "WMS";
        evt.HookName = "OnLocationPublishedAsync";
        evt.SourceNo = "LPUB-1";
        evt.CorrelationId = correlationId ?? Guid.NewGuid();
        evt.JobId = jobId;
        evt.PublishAttemptId = publishAttemptId;
        evt.PayloadJson = """{"batchNo":"LPUB-1","items":[]}""";
        return evt;
    }

    private static ServiceProvider BuildProvider(
        DbContextOptions<CP6Context> options,
        Mock<IIntegrationEventDispatcher> dispatcher,
        Mock<IDeadLetterNotifier> notifier,
        ISpaceDeadLetterNotifier? spaceNotifier = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new CP6Context(options));
        services.AddScoped(_ => dispatcher.Object);
        services.AddScoped(_ => notifier.Object);
        services.AddScoped<ISpaceDeadLetterNotifier>(sp =>
            spaceNotifier ??
            new RecordingSpaceDeadLetterNotifier(
                sp.GetRequiredService<IDeadLetterNotifier>()));
        // 章10 后台按租户循环：TenantScopeRunner 解析这两个服务（空 Sys_Tenants → 回退默认租户跑一遍）
        services.AddScoped<CP6.Core.Services.Common.ITenantContext, CP6.Core.Services.Common.TenantContext>();
        services.AddScoped<CP6.Core.Services.Common.ITenantEnumerator, CP6.Core.Services.Common.TenantEnumerator>();
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildSpaceProvider(
        DbContextOptions<CP6Context> options,
        DispatchState dispatch,
        AuditState audit,
        Mock<IDeadLetterNotifier>? notifier = null,
        Func<CP6Context>? contextFactory = null,
        ISpaceDeadLetterNotifier? spaceNotifier = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ =>
            contextFactory?.Invoke() ??
            new CP6Context(options));
        services.AddSingleton(dispatch);
        services.AddSingleton(audit);
        services.AddScoped<SpaceExecutionContextAccessor>();
        services.AddScoped<ISpaceExecutionContextAccessor>(
            sp => sp.GetRequiredService<SpaceExecutionContextAccessor>());
        services.AddScoped<ISpaceExecutionContextManager>(
            sp => sp.GetRequiredService<SpaceExecutionContextAccessor>());
        services.AddScoped<IIntegrationEventDispatcher, RecordingDispatcher>();
        services.AddScoped<ISpaceAuditWriter, RecordingAuditWriter>();
        services.AddScoped<
            ISpaceRetryFinalizer,
            RecordingSpaceRetryFinalizer>();
        services.AddScoped(_ =>
            (notifier ?? new Mock<IDeadLetterNotifier>()).Object);
        services.AddScoped<ISpaceDeadLetterNotifier>(sp =>
            spaceNotifier ??
            new RecordingSpaceDeadLetterNotifier(
                sp.GetRequiredService<IDeadLetterNotifier>()));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITenantEnumerator, TenantEnumerator>();
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildRealHookSpaceProvider(
        DbContextOptions<CP6Context> options,
        AuditState audit,
        Exception consumerFailure,
        Mock<IDeadLetterNotifier> notifier)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new CP6Context(options));
        services.AddSingleton(audit);
        services.AddSingleton<IWmsLocationConsumer>(
            new ExactThrowingWmsConsumer(consumerFailure));
        services.AddScoped<SpaceExecutionContextAccessor>();
        services.AddScoped<ISpaceExecutionContextAccessor>(
            sp => sp.GetRequiredService<
                SpaceExecutionContextAccessor>());
        services.AddScoped<ISpaceExecutionContextManager>(
            sp => sp.GetRequiredService<
                SpaceExecutionContextAccessor>());
        services.AddScoped<
            ISpaceAuditWriter,
            RecordingAuditWriter>();
        services.AddScoped<
            ISpaceRetryFinalizer,
            RecordingSpaceRetryFinalizer>();
        services.AddScoped<ISpaceBridgeHook>(sp =>
            new SpaceBridgeHook(
                sp.GetRequiredService<CP6Context>(),
                NullLogger<SpaceBridgeHook>.Instance,
                sp.GetRequiredService<IWmsLocationConsumer>(),
                sp.GetRequiredService<
                    ISpaceExecutionContextAccessor>(),
                sp.GetRequiredService<
                    ISpaceExecutionContextManager>()));
        services.AddScoped<IIntegrationEventDispatcher>(sp =>
            new IntegrationEventDispatcher(
                Mock.Of<IMesBridgeHook>(),
                Mock.Of<IWmsBridgeHook>(),
                Mock.Of<IErpBridgeHook>(),
                Mock.Of<IOrderCancelBridgeHook>(),
                Mock.Of<IFinBridgeHook>(),
                sp.GetRequiredService<ISpaceBridgeHook>()));
        services.AddScoped(_ => notifier.Object);
        services.AddScoped<ISpaceDeadLetterNotifier>(sp =>
            new RecordingSpaceDeadLetterNotifier(
                sp.GetRequiredService<IDeadLetterNotifier>()));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<
            ITenantEnumerator,
            TenantEnumerator>();
        return services.BuildServiceProvider();
    }

    private static (
        SqliteConnection Anchor,
        DbContextOptions<CP6Context> Options)
        NewSqliteClaimDatabase(
            DbCommandInterceptor interceptor)
    {
        var databaseName =
            $"space-claim-{Guid.NewGuid():N}";
        var connectionString =
            $"Data Source={databaseName};Mode=Memory;Cache=Shared";
        var anchor = new SqliteConnection(connectionString);
        anchor.Open();
        var setupOptions =
            new DbContextOptionsBuilder<CP6Context>()
                .UseSqlite(connectionString)
                .Options;
        using (var setup =
               new SqliteIntegrationContext(setupOptions))
        {
            var script = Regex.Replace(
                setup.Database.GenerateCreateScript(),
                "n?varchar\\(max\\)",
                "TEXT",
                RegexOptions.IgnoreCase);
            using var command = anchor.CreateCommand();
            command.CommandText = script;
            command.ExecuteNonQuery();
        }

        using (var trigger = anchor.CreateCommand())
        {
            trigger.CommandText =
                """
                CREATE TRIGGER "trg_IntegrationEvent_RowVersion"
                AFTER UPDATE ON "T_IntegrationEvent"
                BEGIN
                    UPDATE "T_IntegrationEvent"
                    SET "RowVersion" = randomblob(8)
                    WHERE "Id" = NEW."Id";
                END;
                """;
            trigger.ExecuteNonQuery();
        }

        var options =
            new DbContextOptionsBuilder<CP6Context>()
                .UseSqlite(connectionString)
                .AddInterceptors(interceptor)
                .Options;
        return (anchor, options);
    }

    private static ServiceProvider BuildSqliteSpaceProvider(
        DbContextOptions<CP6Context> options,
        DispatchState dispatch,
        AuditState audit,
        Mock<IDeadLetterNotifier> notifier,
        ISpaceDeadLetterNotifier? spaceNotifier = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<CP6Context>(
            _ => new SqliteIntegrationContext(options));
        services.AddSingleton(dispatch);
        services.AddSingleton(audit);
        services.AddScoped<SpaceExecutionContextAccessor>();
        services.AddScoped<ISpaceExecutionContextAccessor>(
            sp => sp.GetRequiredService<
                SpaceExecutionContextAccessor>());
        services.AddScoped<ISpaceExecutionContextManager>(
            sp => sp.GetRequiredService<
                SpaceExecutionContextAccessor>());
        services.AddScoped<
            IIntegrationEventDispatcher,
            RecordingDispatcher>();
        services.AddScoped<
            ISpaceAuditWriter,
            RecordingAuditWriter>();
        services.AddScoped<
            ISpaceRetryFinalizer,
            RecordingSpaceRetryFinalizer>();
        services.AddScoped(_ => notifier.Object);
        services.AddScoped<ISpaceDeadLetterNotifier>(sp =>
            spaceNotifier ??
            new RecordingSpaceDeadLetterNotifier(
                sp.GetRequiredService<IDeadLetterNotifier>()));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<
            ITenantEnumerator,
            TenantEnumerator>();
        return services.BuildServiceProvider();
    }

    private static IntegrationEventRetryWorker NewWorker(
        ServiceProvider provider,
        IntegrationEventOptions? options = null,
        Microsoft.Extensions.Logging.ILogger<
            IntegrationEventRetryWorker>? logger = null)
    {
        return new IntegrationEventRetryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options ?? new IntegrationEventOptions
            {
                MaxAttempts = 5,
                BackoffSeconds = [10, 20],
                PollIntervalSeconds = 60,
            }),
            logger ??
                NullLogger<IntegrationEventRetryWorker>.Instance,
            Options.Create(new SpaceObservabilityOptions
            {
                LegacyIntegrationEventTimeZoneId = "UTC",
            }));
    }

    private static async Task RunWorkerBrieflyAsync(
        ServiceProvider provider,
        IntegrationEventOptions options)
    {
        var worker = new IntegrationEventRetryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<IntegrationEventRetryWorker>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await worker.StartAsync(cts.Token);
        await Task.Delay(200, CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Worker_RetriesDueEvents_AndMarksSuccess()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(options, NewFailedEvent(1, DateTime.UtcNow.AddSeconds(-5)));
        var dispatcher = new Mock<IIntegrationEventDispatcher>();
        var notifier = new Mock<IDeadLetterNotifier>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await using var provider = BuildProvider(options, dispatcher, notifier);

        await RunWorkerBrieflyAsync(provider, new IntegrationEventOptions { PollIntervalSeconds = 60 });

        await using var assertDb = new CP6Context(options);
        var evt = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Success, evt.Status);
        Assert.Equal(2, evt.Attempts);
        Assert.Null(evt.NextRetryAt);
    }

    [Fact]
    public async Task Worker_AfterMaxAttempts_TransitionsToDeadLetter()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(options, NewFailedEvent(4, DateTime.UtcNow.AddSeconds(-5)));
        var dispatcher = new Mock<IIntegrationEventDispatcher>();
        var notifier = new Mock<IDeadLetterNotifier>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        await using var provider = BuildProvider(options, dispatcher, notifier);

        await RunWorkerBrieflyAsync(provider, new IntegrationEventOptions { MaxAttempts = 5, PollIntervalSeconds = 60 });

        await using var assertDb = new CP6Context(options);
        var evt = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.DeadLetter, evt.Status);
        Assert.Equal(5, evt.Attempts);
        Assert.Null(evt.NextRetryAt);
        notifier.Verify(n => n.NotifyAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Worker_DisabledViaOptions_DoesNothing()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(options, NewFailedEvent(1, DateTime.UtcNow.AddSeconds(-5)));
        var dispatcher = new Mock<IIntegrationEventDispatcher>();
        var notifier = new Mock<IDeadLetterNotifier>();
        await using var provider = BuildProvider(options, dispatcher, notifier);

        await RunWorkerBrieflyAsync(provider, new IntegrationEventOptions { Enabled = false, PollIntervalSeconds = 1 });

        await using var assertDb = new CP6Context(options);
        var evt = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Failed, evt.Status);
        Assert.Equal(1, evt.Attempts);
        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Worker_SkipsEventsNotYetDue()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(options, NewFailedEvent(1, DateTime.UtcNow.AddMinutes(10)));
        var dispatcher = new Mock<IIntegrationEventDispatcher>();
        var notifier = new Mock<IDeadLetterNotifier>();
        await using var provider = BuildProvider(options, dispatcher, notifier);

        await RunWorkerBrieflyAsync(provider, new IntegrationEventOptions { PollIntervalSeconds = 60 });

        await using var assertDb = new CP6Context(options);
        var evt = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Failed, evt.Status);
        Assert.Equal(1, evt.Attempts);
        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Worker_BackoffIncreasesAttemptByAttempt()
    {
        var options = NewOptions();
        var evtId = Guid.NewGuid();
        await using (var seedDb = new CP6Context(options))
        {
            var evt = NewFailedEvent(0, DateTime.UtcNow.AddSeconds(-5));
            evt.Id = evtId;
            seedDb.IntegrationEvents.Add(evt);
            await seedDb.SaveChangesAsync();
        }
        var dispatcher = new Mock<IIntegrationEventDispatcher>();
        var notifier = new Mock<IDeadLetterNotifier>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<IntegrationEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var retryOptions = new IntegrationEventOptions
        {
            MaxAttempts = 5,
            BackoffSeconds = new[] { 10, 20 },
            PollIntervalSeconds = 60,
        };

        await using (var provider = BuildProvider(options, dispatcher, notifier))
        {
            var beforeFirst = DateTime.UtcNow;
            await RunWorkerBrieflyAsync(provider, retryOptions);
            await using var assertDb = new CP6Context(options);
            var evt = await assertDb.IntegrationEvents.SingleAsync(e => e.Id == evtId);
            Assert.Equal(1, evt.Attempts);
            Assert.InRange(evt.NextRetryAt!.Value, beforeFirst.AddSeconds(9), DateTime.UtcNow.AddSeconds(12));
            evt.NextRetryAt = DateTime.UtcNow.AddSeconds(-1);
            await assertDb.SaveChangesAsync();
        }

        await using (var provider = BuildProvider(options, dispatcher, notifier))
        {
            var beforeSecond = DateTime.UtcNow;
            await RunWorkerBrieflyAsync(provider, retryOptions);
            await using var assertDb = new CP6Context(options);
            var evt = await assertDb.IntegrationEvents.SingleAsync(e => e.Id == evtId);
            Assert.Equal(2, evt.Attempts);
            Assert.InRange(evt.NextRetryAt!.Value, beforeSecond.AddSeconds(19), DateTime.UtcNow.AddSeconds(22));
        }
    }

    [Fact]
    public async Task Worker_space_exception_uses_system_context_and_safe_failure_evidence()
    {
        var options = NewOptions();
        var jobId = Guid.NewGuid();
        var publishAttemptId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var evt = NewFailedSpaceEvent(
            attempts: 1,
            correlationId,
            jobId,
            publishAttemptId);
        evt.PayloadJson =
            """{"batchNo":"LPUB-1","items":[],"secret":"secret payload marker"}""";
        await using var seedDb = await SeedAsync(
            options,
            evt);
        var dispatch = new DispatchState
        {
            Failure = new InvalidOperationException(
                "secret adapter response"),
        };
        var audit = new AuditState();
        var logger = new Mock<
            Microsoft.Extensions.Logging.ILogger<
                IntegrationEventRetryWorker>>();
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit);
        using var cts = new CancellationTokenSource();

        await NewWorker(
            provider,
            logger: logger.Object).ProcessOnceAsync(cts.Token);

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        var current = Assert.Single(dispatch.Contexts);
        Assert.Equal(SpaceExecutionContext.SystemActor, current.ActorType);
        Assert.Equal(
            "space-worker:integration-event-retry",
            current.ActorId);
        Assert.Equal(
            TenantContext.DefaultTenant,
            current.TenantId);
        Assert.Equal(correlationId, current.CorrelationId);
        Assert.Equal(jobId, current.JobId);
        Assert.Equal(publishAttemptId, current.PublishAttemptId);
        Assert.NotNull(current.RunId);
        Assert.Matches("^[0-9a-f]{32}$", current.TraceId);
        Assert.DoesNotMatch("^0{32}$", current.TraceId);
        Assert.StartsWith("SPACE_ADAPTER_FAILURE:", saved.LastError);
        Assert.DoesNotContain(
            "secret adapter response",
            saved.LastError,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            audit.Inputs,
            x => x.Outcome == SpaceAuditOutcome.Started);
        Assert.Equal(2, audit.Inputs.Count);
        var failed = Assert.Single(
            audit.Inputs,
            x => x.Outcome == SpaceAuditOutcome.Failed);
        Assert.Equal(saved.Attempts, failed.AttemptNo);
        Assert.Equal(
            nameof(InvalidOperationException),
            failed.Evidence?.ExceptionType);
        Assert.Matches(
            "^[0-9A-F]{64}$",
            failed.Evidence?.ErrorFingerprint ?? "");
        Assert.All(
            audit.Inputs,
            x => Assert.DoesNotContain(
                "secret adapter response",
                x.ToString(),
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal(cts.Token, audit.Tokens[0]);
        Assert.Equal(CancellationToken.None, audit.Tokens[^1]);
        Assert.All(
            audit.Contexts,
            value => Assert.Equal(
                TenantContext.DefaultTenant,
                value.TenantId));
        Assert.All(logger.Invocations, invocation =>
        {
            var rendered = string.Join(
                " ",
                invocation.Arguments.Select(
                    x => x?.ToString()));
            Assert.DoesNotContain(
                "secret adapter response",
                rendered,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "secret payload marker",
                rendered,
                StringComparison.OrdinalIgnoreCase);
            Assert.Empty(
                invocation.Arguments.OfType<Exception>());
        });
    }

    [Fact]
    public async Task Worker_space_backfill_is_stable_and_each_attempt_gets_new_run_and_trace()
    {
        var options = NewOptions();
        var evt = NewFailedSpaceEvent();
        var evtId = evt.Id;
        var correlationId = evt.CorrelationId;
        await using var seedDb = await SeedAsync(options, evt);
        var persistedBeforeDispatch = new List<
            (Guid? JobId, Guid? PublishAttemptId)>();
        var dispatch = new DispatchState
        {
            Result = false,
            BeforeResult = _ =>
            {
                using var readDb = new CP6Context(options);
                var persisted = readDb.IntegrationEvents
                    .AsNoTracking()
                    .Single(x => x.Id == evtId);
                persistedBeforeDispatch.Add(
                    (persisted.JobId, persisted.PublishAttemptId));
            },
        };
        var audit = new AuditState();
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit);
        var worker = NewWorker(provider);

        await worker.ProcessOnceAsync();
        Guid publishAttemptId;
        await using (var rewindDb = new CP6Context(options))
        {
            var saved = await rewindDb.IntegrationEvents.SingleAsync();
            Assert.Equal(evtId, saved.JobId);
            publishAttemptId = Assert.IsType<Guid>(
                saved.PublishAttemptId);
            saved.NextRetryAt = DateTime.UtcNow.AddSeconds(-1);
            await rewindDb.SaveChangesAsync();
        }
        await worker.ProcessOnceAsync();

        var contexts = dispatch.Contexts.ToList();
        Assert.Equal(2, contexts.Count);
        Assert.All(contexts, current =>
        {
            Assert.Equal(correlationId, current.CorrelationId);
            Assert.Equal(evtId, current.JobId);
            Assert.Equal(publishAttemptId, current.PublishAttemptId);
        });
        Assert.Equal(
            2,
            contexts.Select(x => x.RunId).Distinct().Count());
        Assert.Equal(
            2,
            contexts.Select(x => x.TraceId).Distinct().Count());
        Assert.Equal(2, persistedBeforeDispatch.Count);
        Assert.All(persistedBeforeDispatch, persisted =>
        {
            Assert.Equal(evtId, persisted.JobId);
            Assert.Equal(
                publishAttemptId,
                persisted.PublishAttemptId);
        });
    }

    [Fact]
    public async Task Worker_space_empty_correlation_is_backfilled_and_dispatched()
    {
        var options = NewOptions();
        var evt = NewFailedSpaceEvent(attempts: 4);
        evt.CorrelationId = Guid.Empty;
        await using var seedDb = await SeedAsync(options, evt);
        var dispatch = new DispatchState();
        var audit = new AuditState();
        var notifier = new Mock<IDeadLetterNotifier>();
        IntegrationEvent? notified = null;
        notifier
            .Setup(x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<IntegrationEvent, CancellationToken>(
                (value, _) => notified = value)
            .Returns(Task.CompletedTask);
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit,
            notifier);

        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Success, saved.Status);
        Assert.Equal(5, saved.Attempts);
        Assert.NotEqual(Guid.Empty, saved.CorrelationId);
        Assert.Null(saved.LastError);
        Assert.Null(saved.NextRetryAt);
        Assert.Null(saved.RetryLeaseId);
        Assert.Equal(saved.Id, saved.JobId);
        Assert.NotNull(saved.PublishAttemptId);
        Assert.Single(dispatch.Contexts);
        Assert.All(
            dispatch.Contexts,
            x => Assert.Equal(
                saved.CorrelationId,
                x.CorrelationId));
        Assert.Collection(
            audit.Inputs,
            x => Assert.Equal(
                SpaceAuditOutcome.Started,
                x.Outcome),
            x => Assert.Equal(
                SpaceAuditOutcome.Succeeded,
                x.Outcome));
        Assert.Null(notified);
        notifier.Verify(
            x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Worker_space_started_audit_failure_does_not_dispatch()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(
            options,
            NewFailedSpaceEvent());
        var dispatch = new DispatchState();
        var audit = new AuditState();
        audit.Return(false, true);
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit);

        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Failed, saved.Status);
        Assert.Equal(0, saved.Attempts);
        Assert.Equal("SPACE_AUDIT_UNAVAILABLE", saved.LastError);
        Assert.NotNull(saved.NextRetryAt);
        Assert.Null(saved.RetryLeaseId);
        Assert.Empty(dispatch.Contexts);
        var started = Assert.Single(audit.Inputs);
        Assert.Equal(SpaceAuditOutcome.Started, started.Outcome);
    }

    [Fact]
    public async Task Worker_started_success_but_lost_start_fence_does_not_dispatch()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(
            options,
            NewFailedSpaceEvent());
        var replacementLease = Guid.NewGuid();
        var dispatch = new DispatchState();
        var audit = new AuditState
        {
            AfterRecord = input =>
            {
                if (input.Outcome != SpaceAuditOutcome.Started)
                    return;
                using var takeover = new CP6Context(options);
                var evt = takeover.IntegrationEvents.Single();
                evt.RetryLeaseId = replacementLease;
                evt.NextRetryAt = DateTime.UtcNow.AddMinutes(5);
                takeover.SaveChanges();
            },
        };
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit);

        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(0, saved.Attempts);
        Assert.Equal(
            IntegrationEventStatus.Failed,
            saved.Status);
        Assert.Equal(replacementLease, saved.RetryLeaseId);
        Assert.Empty(dispatch.Contexts);
        var started = Assert.Single(audit.Inputs);
        Assert.Equal(
            SpaceAuditOutcome.Started,
            started.Outcome);
    }

    [Fact]
    public async Task Worker_space_false_result_sets_safe_rejection_code()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(
            options,
            NewFailedSpaceEvent());
        var dispatch = new DispatchState { Result = false };
        var audit = new AuditState();
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit);

        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Failed, saved.Status);
        Assert.Equal("SPACE_ADAPTER_REJECTED", saved.LastError);
        Assert.Collection(
            audit.Inputs,
            started =>
            {
                Assert.Equal(
                    SpaceAuditOutcome.Started,
                    started.Outcome);
                Assert.Equal(saved.Attempts, started.AttemptNo);
            },
            failed =>
            {
                Assert.Equal(
                    SpaceAuditOutcome.Failed,
                    failed.Outcome);
                Assert.Equal(
                    "SPACE_ADAPTER_REJECTED",
                    failed.ReasonCode);
                Assert.Equal(saved.Attempts, failed.AttemptNo);
            });
    }

    [Fact]
    public async Task Worker_space_success_clears_previous_error_and_retry()
    {
        var options = NewOptions();
        var evt = NewFailedSpaceEvent();
        evt.LastError = "OLD_SAFE_ERROR";
        await using var seedDb = await SeedAsync(options, evt);
        var dispatch = new DispatchState { Result = true };
        var audit = new AuditState();
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit);

        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Success, saved.Status);
        Assert.Null(saved.LastError);
        Assert.Null(saved.NextRetryAt);
        Assert.Collection(
            audit.Inputs,
            started =>
            {
                Assert.Equal(
                    SpaceAuditOutcome.Started,
                    started.Outcome);
                Assert.Equal(saved.Attempts, started.AttemptNo);
            },
            succeeded =>
            {
                Assert.Equal(
                    SpaceAuditOutcome.Succeeded,
                    succeeded.Outcome);
                Assert.Equal(
                    saved.Attempts,
                    succeeded.AttemptNo);
            });
    }

    [Fact]
    public async Task Worker_space_result_audit_failure_marks_outcome_unknown()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(
            options,
            NewFailedSpaceEvent());
        var dispatch = new DispatchState { Result = true };
        var audit = new AuditState();
        audit.Return(true, false);
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit);

        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Failed, saved.Status);
        Assert.Equal(
            "SPACE_OPERATION_OUTCOME_UNKNOWN",
            saved.LastError);
        Assert.NotNull(saved.NextRetryAt);
        Assert.Single(dispatch.Contexts);
        Assert.Equal(2, audit.Inputs.Count);
    }

    [Fact]
    public async Task Worker_space_exhausted_failure_audits_dead_letter_and_notifies_safe_error()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(
            options,
            NewFailedSpaceEvent(attempts: 4));
        var dispatch = new DispatchState
        {
            Failure = new InvalidOperationException(
                "secret dead-letter body"),
        };
        var audit = new AuditState();
        var notifier = new Mock<IDeadLetterNotifier>();
        IntegrationEvent? notified = null;
        notifier
            .Setup(x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<IntegrationEvent, CancellationToken>(
                (value, _) => notified = value)
            .Returns(Task.CompletedTask);
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit,
            notifier);

        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.DeadLetter, saved.Status);
        Assert.Null(saved.NextRetryAt);
        Assert.DoesNotContain(
            "secret dead-letter body",
            notified?.LastError ?? "",
            StringComparison.OrdinalIgnoreCase);
        var outcome = Assert.Single(
            audit.Inputs,
            x => x.Outcome == SpaceAuditOutcome.Failed);
        Assert.Equal(2, audit.Inputs.Count);
        Assert.Equal("SPACE_RETRY_DEAD_LETTER", outcome.ReasonCode);
        Assert.Equal(
            IntegrationEventStatus.DeadLetter,
            outcome.Evidence?.Status);
        Assert.Equal(saved.Attempts, outcome.AttemptNo);
        notifier.Verify(
            x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Worker_space_dispatch_cancellation_propagates_without_failure_outcome()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(
            options,
            NewFailedSpaceEvent());
        using var cts = new CancellationTokenSource();
        var dispatch = new DispatchState
        {
            SaveOperationDbBeforeResult = true,
            FailureFactory = token =>
            {
                cts.Cancel();
                return new OperationCanceledException(token);
            },
        };
        var audit = new AuditState();
        var notifier = new Mock<IDeadLetterNotifier>();
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit,
            notifier);

        var retryOptions = new IntegrationEventOptions
        {
            MaxAttempts = 1,
            BackoffSeconds = [1],
            PollIntervalSeconds = 60,
        };
        var worker = NewWorker(provider, retryOptions);
        var claimStartedAt = DateTime.UtcNow;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => worker.ProcessOnceAsync(cts.Token));

        DateTime leaseUntil;
        await using (var assertDb = new CP6Context(options))
        {
            var saved =
                await assertDb.IntegrationEvents.SingleAsync();
            Assert.Equal(IntegrationEventStatus.Failed, saved.Status);
            Assert.DoesNotContain(
                "SPACE_ADAPTER_FAILURE",
                saved.LastError ?? "",
                StringComparison.Ordinal);
            Assert.Equal(0, dispatch.OperationDbTrackedEventCount);
            Assert.Equal(1, saved.Attempts);
            Assert.Equal(saved.Id, saved.JobId);
            Assert.NotNull(saved.PublishAttemptId);
            leaseUntil = Assert.IsType<DateTime>(
                saved.NextRetryAt);
            Assert.True(
                leaseUntil >= claimStartedAt.AddMinutes(14));
        }
        var started = Assert.Single(audit.Inputs);
        Assert.Equal(SpaceAuditOutcome.Started, started.Outcome);
        Assert.Equal(1, started.AttemptNo);
        Assert.Single(dispatch.Contexts);
        notifier.Verify(
            x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        await worker.ProcessOnceAsync();
        Assert.Single(audit.Inputs);
        Assert.Single(dispatch.Contexts);

        await using (var rewindDb = new CP6Context(options))
        {
            var leased =
                await rewindDb.IntegrationEvents.SingleAsync();
            Assert.Equal(leaseUntil, leased.NextRetryAt);
            leased.NextRetryAt =
                DateTime.UtcNow.AddSeconds(-1);
            await rewindDb.SaveChangesAsync();
        }

        await worker.ProcessOnceAsync();

        await using var recoveredDb = new CP6Context(options);
        var recovered =
            await recoveredDb.IntegrationEvents.SingleAsync();
        Assert.Equal(
            IntegrationEventStatus.DeadLetter,
            recovered.Status);
        Assert.Equal(1, recovered.Attempts);
        Assert.Null(recovered.NextRetryAt);
        Assert.Equal(2, audit.Inputs.Count);
        Assert.Single(dispatch.Contexts);
        notifier.Verify(
            x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Worker_space_empty_legacy_ids_are_backfilled_before_attempt()
    {
        var options = NewOptions();
        var evt = NewFailedSpaceEvent(
            jobId: Guid.Empty,
            publishAttemptId: Guid.Empty);
        var evtId = evt.Id;
        var persistedAttemptsAtDispatch = -1;
        await using var seedDb = await SeedAsync(options, evt);
        var dispatch = new DispatchState
        {
            Result = false,
            BeforeResult = _ =>
            {
                using var readDb = new CP6Context(options);
                var persisted = readDb.IntegrationEvents
                    .AsNoTracking()
                    .Single(x => x.Id == evtId);
                persistedAttemptsAtDispatch =
                    persisted.Attempts;
                Assert.NotEqual(Guid.Empty, persisted.JobId);
                Assert.NotEqual(
                    Guid.Empty,
                    persisted.PublishAttemptId);
                Assert.NotNull(persisted.OccurredAtUtc);
                Assert.Equal(
                    DateTimeKind.Utc,
                    persisted.OccurredAtUtc.Value.Kind);
            },
        };
        var audit = new AuditState();
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit);

        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(evtId, saved.JobId);
        Assert.NotEqual(Guid.Empty, saved.PublishAttemptId);
        Assert.Equal(
            evt.CreateDate.Kind == DateTimeKind.Local
                ? evt.CreateDate.ToUniversalTime()
                : DateTime.SpecifyKind(
                    evt.CreateDate,
                    DateTimeKind.Utc),
            saved.OccurredAtUtc);
        Assert.Equal(1, persistedAttemptsAtDispatch);
        Assert.Equal(1, saved.Attempts);
    }

    [Fact]
    public async Task Worker_space_stuck_at_max_is_recovered_without_dispatch_or_increment()
    {
        var options = NewOptions();
        var evt = NewFailedSpaceEvent(
            attempts: 5,
            jobId: Guid.NewGuid(),
            publishAttemptId: Guid.NewGuid());
        evt.NextRetryAt = null;
        await using var seedDb = await SeedAsync(options, evt);
        var dispatch = new DispatchState();
        var audit = new AuditState();
        var notifier = new Mock<IDeadLetterNotifier>();
        notifier
            .Setup(x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit,
            notifier);

        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(5, saved.Attempts);
        Assert.Equal(IntegrationEventStatus.DeadLetter, saved.Status);
        Assert.Null(saved.NextRetryAt);
        Assert.Empty(dispatch.Contexts);
        var outcome = Assert.Single(audit.Inputs);
        Assert.Equal(SpaceAuditOutcome.Failed, outcome.Outcome);
        Assert.Equal("SPACE_RETRY_DEAD_LETTER", outcome.ReasonCode);
        Assert.Equal(5, outcome.AttemptNo);
        Assert.Equal(
            IntegrationEventStatus.DeadLetter,
            outcome.Evidence?.Status);
        var context = Assert.Single(audit.Contexts);
        Assert.Equal(
            SpaceExecutionContext.SystemActor,
            context.ActorType);
        Assert.NotEqual(Guid.Empty, context.RunId);
        notifier.Verify(
            x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Worker_space_external_success_finalizes_after_host_token_is_cancelled()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(
            options,
            NewFailedSpaceEvent());
        using var cts = new CancellationTokenSource();
        var dispatch = new DispatchState
        {
            Result = true,
            FailureFactory = _ =>
            {
                cts.Cancel();
                return null;
            },
        };
        var audit = new AuditState();
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit);

        var completion = await Record.ExceptionAsync(
            () => NewWorker(provider).ProcessOnceAsync(cts.Token));
        Assert.True(
            completion is null or OperationCanceledException,
            completion?.ToString());

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Success, saved.Status);
        Assert.Equal(1, saved.Attempts);
        Assert.Null(saved.LastError);
        Assert.Null(saved.NextRetryAt);
        Assert.Collection(
            audit.Inputs,
            started => Assert.Equal(
                SpaceAuditOutcome.Started,
                started.Outcome),
            succeeded =>
            {
                Assert.Equal(
                    SpaceAuditOutcome.Succeeded,
                    succeeded.Outcome);
                Assert.Equal(saved.Attempts, succeeded.AttemptNo);
            });
        Assert.Equal(CancellationToken.None, audit.Tokens[^1]);
    }

    [Fact]
    public async Task Worker_completion_marker_wins_over_dispatch_commit_unknown()
    {
        var options = NewOptions();
        var evt = NewFailedSpaceEvent(attempts: 4);
        await using var seedDb = await SeedAsync(options, evt);
        var dispatch = new DispatchState
        {
            AsyncResult = async (dispatched, _) =>
            {
                await PersistCompletionMarkerAsync(
                    options,
                    dispatched.Id,
                    succeeded: true);
                throw new TimeoutException(
                    "simulated acknowledgement loss");
            },
        };
        var audit = new AuditState();
        var notifier = new Mock<IDeadLetterNotifier>();
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit,
            notifier);

        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(5, saved.Attempts);
        Assert.Equal(
            IntegrationEventStatus.Success,
            saved.Status);
        Assert.Null(saved.RetryLeaseId);
        Assert.Null(saved.RetryCompletionLeaseId);
        Assert.Null(saved.RetryCompletionSucceeded);
        Assert.Single(dispatch.Contexts);
        Assert.Collection(
            audit.Inputs,
            started => Assert.Equal(
                SpaceAuditOutcome.Started,
                started.Outcome),
            succeeded => Assert.Equal(
                SpaceAuditOutcome.Succeeded,
                succeeded.Outcome));
        notifier.Verify(
            value => value.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Worker_retries_completion_audit_without_redispatching_wms()
    {
        var options = NewOptions();
        var evt = NewFailedSpaceEvent(attempts: 4);
        await using var seedDb = await SeedAsync(options, evt);
        var dispatch = new DispatchState
        {
            AsyncResult = async (dispatched, _) =>
            {
                await PersistCompletionMarkerAsync(
                    options,
                    dispatched.Id,
                    succeeded: true);
                return true;
            },
        };
        var audit = new AuditState();
        audit.Return(true, false, true);
        var notifier = new Mock<IDeadLetterNotifier>();
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit,
            notifier);
        var worker = NewWorker(provider);

        await worker.ProcessOnceAsync();

        Guid originalCompletionLeaseId;
        await using (var retryDb = new CP6Context(options))
        {
            var pending =
                await retryDb.IntegrationEvents.SingleAsync();
            Assert.Equal(
                IntegrationEventStatus.Failed,
                pending.Status);
            Assert.Equal(5, pending.Attempts);
            originalCompletionLeaseId =
                Assert.IsType<Guid>(
                    pending.RetryCompletionLeaseId);
            Assert.Equal(
                originalCompletionLeaseId,
                pending.RetryLeaseId);
            Assert.True(pending.RetryCompletionSucceeded);
            pending.NextRetryAt =
                DateTime.UtcNow.AddSeconds(-1);
            await retryDb.SaveChangesAsync();
        }

        await worker.ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var recovered =
            await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(
            IntegrationEventStatus.Success,
            recovered.Status);
        Assert.Equal(5, recovered.Attempts);
        Assert.Null(recovered.RetryLeaseId);
        Assert.Null(recovered.RetryCompletionLeaseId);
        Assert.Null(recovered.RetryCompletionSucceeded);
        Assert.Single(dispatch.Contexts);
        Assert.Equal(3, audit.Inputs.Count);
        Assert.Equal(
            2,
            audit.Inputs.Count(input =>
                input.Outcome ==
                    SpaceAuditOutcome.Succeeded));
        notifier.Verify(
            value => value.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Worker_stuck_at_max_with_success_marker_finishes_success()
    {
        var options = NewOptions();
        var originalCompletionLeaseId = Guid.NewGuid();
        var evt = NewFailedSpaceEvent(
            attempts: 5,
            jobId: Guid.NewGuid(),
            publishAttemptId: Guid.NewGuid());
        evt.NextRetryAt = DateTime.UtcNow.AddSeconds(-1);
        evt.RetryLeaseId = originalCompletionLeaseId;
        evt.RetryCompletionLeaseId =
            originalCompletionLeaseId;
        evt.RetryCompletionSucceeded = true;
        await using var seedDb = await SeedAsync(options, evt);
        var dispatch = new DispatchState();
        var audit = new AuditState();
        var notifier = new Mock<IDeadLetterNotifier>();
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit,
            notifier);

        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(
            IntegrationEventStatus.Success,
            saved.Status);
        Assert.Equal(5, saved.Attempts);
        Assert.Null(saved.RetryLeaseId);
        Assert.Null(saved.RetryCompletionLeaseId);
        Assert.Empty(dispatch.Contexts);
        var terminal = Assert.Single(audit.Inputs);
        Assert.Equal(
            SpaceAuditOutcome.Succeeded,
            terminal.Outcome);
        notifier.Verify(
            value => value.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Worker_does_not_start_attempt_after_claim_lease_expires()
    {
        var options = NewOptions();
        var evt = NewFailedSpaceEvent();
        await using var seedDb = await SeedAsync(options, evt);
        var dispatch = new DispatchState();
        var audit = new AuditState
        {
            AfterRecord = input =>
            {
                if (input.Outcome !=
                    SpaceAuditOutcome.Started)
                {
                    return;
                }
                using var expireDb = new CP6Context(options);
                var claimed = expireDb.IntegrationEvents
                    .Single(value => value.Id == evt.Id);
                claimed.NextRetryAt =
                    DateTime.UtcNow.AddSeconds(-1);
                expireDb.SaveChanges();
            },
        };
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit);

        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(0, saved.Attempts);
        Assert.Equal(
            IntegrationEventStatus.Failed,
            saved.Status);
        Assert.NotNull(saved.RetryLeaseId);
        Assert.True(saved.NextRetryAt < DateTime.UtcNow);
        Assert.Empty(dispatch.Contexts);
        var started = Assert.Single(audit.Inputs);
        Assert.Equal(
            SpaceAuditOutcome.Started,
            started.Outcome);
    }

    [Fact]
    public async Task Worker_heartbeat_survives_host_cancel_until_consumer_returns_then_stops()
    {
        var dbOptions = NewOptions();
        await using var seedDb = await SeedAsync(
            dbOptions,
            NewFailedSpaceEvent());
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatch = new DispatchState
        {
            AsyncResult = async (_, ct) =>
            {
                entered.TrySetResult();
                await release.Task;
                throw new OperationCanceledException(ct);
            },
        };
        var audit = new AuditState();
        await using var provider = BuildSpaceProvider(
            dbOptions,
            dispatch,
            audit);
        var workerOptions = new IntegrationEventOptions
        {
            MaxAttempts = 5,
            BackoffSeconds = [10, 20],
            PollIntervalSeconds = 60,
            SpaceRetryLeaseSeconds = 3,
            SpaceRetryHeartbeatSeconds = 1,
        };
        using var host = new CancellationTokenSource();
        var processing = NewWorker(
            provider,
            workerOptions).ProcessOnceAsync(host.Token);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        host.Cancel();
        await Task.Delay(TimeSpan.FromMilliseconds(2_200));

        DateTime renewedAt;
        Guid? leaseId;
        await using (var duringDb = new CP6Context(dbOptions))
        {
            var during = await duringDb.IntegrationEvents
                .SingleAsync();
            renewedAt = during.NextRetryAt!.Value;
            leaseId = during.RetryLeaseId;
            Assert.Equal(
                IntegrationEventStatus.Failed,
                during.Status);
            Assert.Equal(1, during.Attempts);
            Assert.NotNull(leaseId);
            Assert.True(
                renewedAt >
                DateTime.UtcNow.AddMilliseconds(400));
        }

        release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => processing);

        DateTime stoppedAt;
        await using (var stoppedDb = new CP6Context(dbOptions))
        {
            var stopped = await stoppedDb.IntegrationEvents
                .SingleAsync();
            stoppedAt = stopped.NextRetryAt!.Value;
            Assert.Equal(leaseId, stopped.RetryLeaseId);
            Assert.Equal(1, stopped.Attempts);
        }
        await Task.Delay(TimeSpan.FromMilliseconds(1_300));
        await using var assertDb = new CP6Context(dbOptions);
        Assert.Equal(
            stoppedAt,
            (await assertDb.IntegrationEvents
                .SingleAsync()).NextRetryAt);
        var started = Assert.Single(audit.Inputs);
        Assert.Equal(
            SpaceAuditOutcome.Started,
            started.Outcome);
    }

    [Fact]
    public async Task Worker_host_cancel_after_claim_before_started_releases_without_attempt()
    {
        var dbOptions = NewOptions();
        var evt = NewFailedSpaceEvent(
            jobId: Guid.NewGuid(),
            publishAttemptId: Guid.NewGuid());
        await using var seedDb = await SeedAsync(
            dbOptions,
            evt);
        using var host = new CancellationTokenSource();
        var dispatch = new DispatchState();
        var audit = new AuditState();
        await using var provider = BuildSpaceProvider(
            dbOptions,
            dispatch,
            audit,
            contextFactory: () =>
                new CancelAfterSpaceClaimContext(
                    dbOptions,
                    host));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => NewWorker(provider).ProcessOnceAsync(
                host.Token));

        await using var assertDb = new CP6Context(dbOptions);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(0, saved.Attempts);
        Assert.Equal(
            IntegrationEventStatus.Failed,
            saved.Status);
        Assert.Null(saved.RetryLeaseId);
        Assert.Equal("SPACE_RETRY_CANCELLED", saved.LastError);
        Assert.True(saved.NextRetryAt > DateTime.UtcNow);
        Assert.Empty(dispatch.Contexts);
        Assert.Empty(audit.Inputs);
    }

    [Fact]
    public async Task Worker_lost_lease_prevents_old_owner_terminal_audit_and_state()
    {
        var dbOptions = NewOptions();
        await using var seedDb = await SeedAsync(
            dbOptions,
            NewFailedSpaceEvent());
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatch = new DispatchState
        {
            AsyncResult = async (_, _) =>
            {
                entered.TrySetResult();
                await release.Task;
                return true;
            },
        };
        var audit = new AuditState();
        await using var provider = BuildSpaceProvider(
            dbOptions,
            dispatch,
            audit);
        var workerOptions = new IntegrationEventOptions
        {
            MaxAttempts = 5,
            BackoffSeconds = [10, 20],
            PollIntervalSeconds = 60,
            SpaceRetryLeaseSeconds = 3,
            SpaceRetryHeartbeatSeconds = 1,
        };
        var processing = NewWorker(
            provider,
            workerOptions).ProcessOnceAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var replacementLease = Guid.NewGuid();
        await using (var takeoverDb = new CP6Context(dbOptions))
        {
            var owned = await takeoverDb.IntegrationEvents
                .SingleAsync();
            owned.RetryLeaseId = replacementLease;
            owned.NextRetryAt = DateTime.UtcNow.AddMinutes(5);
            await takeoverDb.SaveChangesAsync();
        }
        await Task.Delay(TimeSpan.FromMilliseconds(1_300));
        release.TrySetResult();
        await processing;

        await using var assertDb = new CP6Context(dbOptions);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(
            IntegrationEventStatus.Failed,
            saved.Status);
        Assert.Equal(replacementLease, saved.RetryLeaseId);
        Assert.Equal(1, saved.Attempts);
        var started = Assert.Single(audit.Inputs);
        Assert.Equal(
            SpaceAuditOutcome.Started,
            started.Outcome);
    }

    [Fact]
    public async Task Worker_space_non_host_cancellation_is_sanitized_as_adapter_failure()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(
            options,
            NewFailedSpaceEvent());
        var dispatch = new DispatchState
        {
            Failure = new OperationCanceledException(
                "secret adapter cancellation"),
        };
        var audit = new AuditState();
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit);

        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Failed, saved.Status);
        Assert.StartsWith(
            "SPACE_ADAPTER_FAILURE:OperationCanceledException:",
            saved.LastError);
        Assert.DoesNotContain(
            "secret adapter cancellation",
            saved.LastError);
        var failed = Assert.Single(
            audit.Inputs,
            x => x.Outcome == SpaceAuditOutcome.Failed);
        Assert.Equal("SPACE_ADAPTER_FAILURE", failed.ReasonCode);
        Assert.Equal(
            nameof(OperationCanceledException),
            failed.Evidence?.ExceptionType);
    }

    [Fact]
    public async Task Worker_real_space_hook_exception_preserves_type_for_safe_fingerprint()
    {
        var options = NewOptions();
        var expected = new InvalidOperationException(
            "secret real hook response");
        await using var seedDb = await SeedAsync(
            options,
            NewFailedSpaceEvent());
        var audit = new AuditState();
        var notifier = new Mock<IDeadLetterNotifier>();
        await using var provider =
            BuildRealHookSpaceProvider(
                options,
                audit,
                expected,
                notifier);

        await NewWorker(provider).ProcessOnceAsync();

        var safe = SpaceErrorSanitizer.Classify(
            expected,
            "SPACE_ADAPTER_FAILURE");
        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(
            $"{safe.ReasonCode}:{safe.ExceptionType}:{safe.Fingerprint}",
            saved.LastError);
        Assert.DoesNotContain(
            "secret real hook response",
            saved.LastError);
        var failed = Assert.Single(
            audit.Inputs,
            x => x.Outcome == SpaceAuditOutcome.Failed);
        Assert.Equal(safe.ExceptionType, failed.Evidence?.ExceptionType);
        Assert.Equal(
            safe.Fingerprint,
            failed.Evidence?.ErrorFingerprint);
        Assert.Equal(1, await assertDb.IntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Worker_stuck_space_requires_successful_result_audit_before_dead_letter()
    {
        var options = NewOptions();
        var evt = NewFailedSpaceEvent(
            attempts: 5,
            jobId: Guid.NewGuid(),
            publishAttemptId: Guid.NewGuid());
        evt.NextRetryAt = null;
        await using var seedDb = await SeedAsync(options, evt);
        var dispatch = new DispatchState();
        var audit = new AuditState { DefaultResult = false };
        var notifier = new Mock<IDeadLetterNotifier>();
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit,
            notifier);
        var worker = NewWorker(provider);

        var before = DateTime.UtcNow;
        await worker.ProcessOnceAsync();

        await using (var retryDb = new CP6Context(options))
        {
            var saved = await retryDb.IntegrationEvents.SingleAsync();
            Assert.Equal(IntegrationEventStatus.Failed, saved.Status);
            Assert.Equal("SPACE_AUDIT_UNAVAILABLE", saved.LastError);
            Assert.True(saved.NextRetryAt > before);
            Assert.Equal(5, saved.Attempts);
            saved.NextRetryAt = DateTime.UtcNow.AddSeconds(-1);
            await retryDb.SaveChangesAsync();
        }
        Assert.Empty(dispatch.Contexts);
        notifier.Verify(
            x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        audit.DefaultResult = true;
        await worker.ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var recovered = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.DeadLetter, recovered.Status);
        Assert.Null(recovered.NextRetryAt);
        Assert.Equal(2, audit.Inputs.Count);
        Assert.All(
            audit.Inputs,
            input =>
            {
                Assert.Equal(
                    SpaceAuditOutcome.Failed,
                    input.Outcome);
                Assert.Equal(
                    "SPACE_RETRY_DEAD_LETTER",
                    input.ReasonCode);
            });
        notifier.Verify(
            x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Worker_final_attempt_without_started_or_fallback_audit_remains_retryable()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(
            options,
            NewFailedSpaceEvent(attempts: 4));
        var dispatch = new DispatchState();
        var audit = new AuditState();
        audit.Return(false, false);
        var notifier = new Mock<IDeadLetterNotifier>();
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit,
            notifier);

        var before = DateTime.UtcNow;
        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(4, saved.Attempts);
        Assert.Equal(IntegrationEventStatus.Failed, saved.Status);
        Assert.Equal("SPACE_AUDIT_UNAVAILABLE", saved.LastError);
        Assert.True(saved.NextRetryAt > before);
        Assert.Empty(dispatch.Contexts);
        var started = Assert.Single(audit.Inputs);
        Assert.Equal(SpaceAuditOutcome.Started, started.Outcome);
        notifier.Verify(
            x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Worker_final_attempt_outcome_audit_failure_remains_retryable()
    {
        var options = NewOptions();
        await using var seedDb = await SeedAsync(
            options,
            NewFailedSpaceEvent(attempts: 4));
        var dispatch = new DispatchState { Result = true };
        var audit = new AuditState();
        audit.Return(true, false);
        var notifier = new Mock<IDeadLetterNotifier>();
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit,
            notifier);

        var before = DateTime.UtcNow;
        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(5, saved.Attempts);
        Assert.Equal(IntegrationEventStatus.Failed, saved.Status);
        Assert.Equal(
            "SPACE_OPERATION_OUTCOME_UNKNOWN",
            saved.LastError);
        Assert.True(saved.NextRetryAt > before);
        Assert.Single(dispatch.Contexts);
        Assert.Equal(2, audit.Inputs.Count);
        notifier.Verify(
            x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Worker_two_relational_workers_atomically_claim_last_attempt_once()
    {
        var barrier = new DualIntegrationReadBarrier();
        var database = NewSqliteClaimDatabase(barrier);
        await using var anchor = database.Anchor;
        var evt = NewFailedSpaceEvent(attempts: 4);
        evt.CorrelationId = Guid.Empty;
        await using (var seed =
                     new SqliteIntegrationContext(
                         database.Options))
        {
            seed.IntegrationEvents.Add(evt);
            await seed.SaveChangesAsync();
        }

        var dispatch = new DispatchState { Result = false };
        var audit = new AuditState();
        var notifier = new Mock<IDeadLetterNotifier>();
        notifier
            .Setup(x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await using var provider = BuildSqliteSpaceProvider(
            database.Options,
            dispatch,
            audit,
            notifier);
        var first = NewWorker(provider);
        var second = NewWorker(provider);

        await Task.WhenAll(
            first.ProcessOnceAsync(),
            second.ProcessOnceAsync());

        await using var assertDb =
            new SqliteIntegrationContext(database.Options);
        var saved =
            await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(5, saved.Attempts);
        Assert.Equal(IntegrationEventStatus.DeadLetter, saved.Status);
        Assert.Equal(saved.Id, saved.JobId);
        Assert.NotNull(saved.PublishAttemptId);
        Assert.NotEqual(Guid.Empty, saved.CorrelationId);
        Assert.Equal(
            saved.CorrelationId,
            Assert.Single(dispatch.Contexts).CorrelationId);
        Assert.Collection(
            audit.Inputs.OrderBy(x => x.Outcome),
            failed => Assert.Equal(
                SpaceAuditOutcome.Failed,
                failed.Outcome),
            started => Assert.Equal(
                SpaceAuditOutcome.Started,
                started.Outcome));
        notifier.Verify(
            x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Worker_claim_conflict_does_not_stop_other_events_in_batch()
    {
        var barrier = new DualIntegrationReadBarrier();
        var database = NewSqliteClaimDatabase(barrier);
        await using var anchor = database.Anchor;
        var firstEvent = NewFailedSpaceEvent(
            jobId: Guid.NewGuid(),
            publishAttemptId: Guid.NewGuid());
        firstEvent.NextRetryAt =
            DateTime.UtcNow.AddMinutes(-2);
        var secondEvent = NewFailedSpaceEvent(
            jobId: Guid.NewGuid(),
            publishAttemptId: Guid.NewGuid());
        secondEvent.NextRetryAt =
            DateTime.UtcNow.AddMinutes(-1);
        await using (var seed =
                     new SqliteIntegrationContext(
                         database.Options))
        {
            seed.IntegrationEvents.AddRange(
                firstEvent,
                secondEvent);
            await seed.SaveChangesAsync();
        }

        var secondDispatched =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatch = new DispatchState
        {
            AsyncResult = async (evt, token) =>
            {
                if (evt.Id == firstEvent.Id)
                {
                    await secondDispatched.Task.WaitAsync(
                        TimeSpan.FromSeconds(10),
                        token);
                }
                else
                {
                    secondDispatched.TrySetResult();
                }
                return true;
            },
        };
        var audit = new AuditState();
        var notifier = new Mock<IDeadLetterNotifier>();
        await using var provider = BuildSqliteSpaceProvider(
            database.Options,
            dispatch,
            audit,
            notifier);

        await Task.WhenAll(
            NewWorker(provider).ProcessOnceAsync(),
            NewWorker(provider).ProcessOnceAsync());

        await using var assertDb =
            new SqliteIntegrationContext(database.Options);
        var saved = await assertDb.IntegrationEvents
            .OrderBy(x => x.NextRetryAt)
            .ToListAsync();
        Assert.Equal(2, saved.Count);
        Assert.All(
            saved,
            x => Assert.Equal(
                IntegrationEventStatus.Success,
                x.Status));
        Assert.Equal(2, dispatch.Contexts.Count);
        Assert.Equal(4, audit.Inputs.Count);
        notifier.Verify(
            x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Worker_backfill_reloader_cannot_overwrite_another_workers_future_claim()
    {
        var interleaving =
            new BackfillClaimInterleavingInterceptor();
        var database =
            NewSqliteClaimDatabase(interleaving);
        await using var anchor = database.Anchor;
        var evt = NewFailedSpaceEvent(attempts: 4);
        await using (var seed =
                     new SqliteIntegrationContext(
                         database.Options))
        {
            seed.IntegrationEvents.Add(evt);
            await seed.SaveChangesAsync();
        }

        var dispatcherEntered =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispatcher =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatch = new DispatchState
        {
            AsyncResult = async (_, token) =>
            {
                dispatcherEntered.TrySetResult();
                await releaseDispatcher.Task.WaitAsync(token);
                return true;
            },
        };
        var audit = new AuditState();
        var notifier = new Mock<IDeadLetterNotifier>();
        await using var provider = BuildSqliteSpaceProvider(
            database.Options,
            dispatch,
            audit,
            notifier);
        using var timeout =
            new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var firstWorker = NewWorker(provider);
        var secondWorker = NewWorker(provider);
        Task? firstRun = null;
        Task? secondRun = null;

        try
        {
            firstRun = firstWorker.ProcessOnceAsync(
                timeout.Token);
            await interleaving.WaitForBackfillAsync(
                timeout.Token);

            secondRun = secondWorker.ProcessOnceAsync(
                timeout.Token);
            await dispatcherEntered.Task.WaitAsync(
                timeout.Token);

            interleaving.ReleaseBackfill();
            await firstRun.WaitAsync(timeout.Token);

            await using (var leaseDb =
                         new SqliteIntegrationContext(
                             database.Options))
            {
                var leased =
                    await leaseDb.IntegrationEvents.SingleAsync(
                        timeout.Token);
                Assert.Equal(
                    IntegrationEventStatus.Failed,
                    leased.Status);
                Assert.Equal(5, leased.Attempts);
                Assert.True(
                    leased.NextRetryAt >
                    DateTime.UtcNow.AddMinutes(14));
            }
            Assert.Single(dispatch.Contexts);
            var started = Assert.Single(audit.Inputs);
            Assert.Equal(
                SpaceAuditOutcome.Started,
                started.Outcome);
            notifier.Verify(
                x => x.NotifyAsync(
                    It.IsAny<IntegrationEvent>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);

            releaseDispatcher.TrySetResult();
            await secondRun.WaitAsync(timeout.Token);
        }
        finally
        {
            interleaving.ReleaseBackfill();
            releaseDispatcher.TrySetResult();
            if (firstRun is not null)
                await firstRun.WaitAsync(
                    TimeSpan.FromSeconds(5));
            if (secondRun is not null)
                await secondRun.WaitAsync(
                    TimeSpan.FromSeconds(5));
        }

        await using var assertDb =
            new SqliteIntegrationContext(database.Options);
        var saved =
            await assertDb.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Success, saved.Status);
        Assert.Equal(5, saved.Attempts);
        Assert.Null(saved.NextRetryAt);
        Assert.Single(dispatch.Contexts);
        Assert.Collection(
            audit.Inputs,
            started => Assert.Equal(
                SpaceAuditOutcome.Started,
                started.Outcome),
            succeeded => Assert.Equal(
                SpaceAuditOutcome.Succeeded,
                succeeded.Outcome));
        notifier.Verify(
            x => x.NotifyAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Worker_outer_non_space_conflict_cannot_undo_finalized_space_event()
    {
        var options = NewOptions();
        var space = NewFailedSpaceEvent();
        space.NextRetryAt = DateTime.UtcNow.AddMinutes(-2);
        var nonSpace = NewFailedEvent(
            attempts: 0,
            DateTime.UtcNow.AddMinutes(-1));
        await using (var seed = new CP6Context(options))
        {
            seed.IntegrationEvents.AddRange(space, nonSpace);
            await seed.SaveChangesAsync();
        }
        var dispatch = new DispatchState { Result = true };
        var audit = new AuditState();
        await using var provider = BuildSpaceProvider(
            options,
            dispatch,
            audit,
            contextFactory: () =>
                new NonSpaceConflictContext(options));

        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var saved = await assertDb.IntegrationEvents
            .OrderBy(x => x.SourceModule)
            .ToListAsync();
        var savedSpace = Assert.Single(
            saved,
            x => x.SourceModule == "SPACE");
        var savedNonSpace = Assert.Single(
            saved,
            x => x.SourceModule != "SPACE");
        Assert.Equal(
            IntegrationEventStatus.Success,
            savedSpace.Status);
        Assert.Equal(1, savedSpace.Attempts);
        Assert.Null(savedSpace.RetryLeaseId);
        Assert.Equal(
            IntegrationEventStatus.Failed,
            savedNonSpace.Status);
        Assert.Equal(0, savedNonSpace.Attempts);
        Assert.Collection(
            audit.Inputs,
            started => Assert.Equal(
                SpaceAuditOutcome.Started,
                started.Outcome),
            succeeded => Assert.Equal(
                SpaceAuditOutcome.Succeeded,
                succeeded.Outcome));
    }

    [Fact]
    public async Task Worker_releases_failed_dead_letter_notification_for_next_scan()
    {
        var options = NewOptions();
        var dead = NewFailedSpaceEvent(attempts: 5);
        dead.Status = IntegrationEventStatus.DeadLetter;
        dead.NextRetryAt = null;
        await using var seedDb = await SeedAsync(options, dead);
        var outbox = new SpaceDeadLetterState();
        // Each worker pass now drains both before and after the due loop.
        // Fail both first-pass claims so the next ProcessOnce proves retry.
        outbox.Return(false, false, true);
        await using var provider = BuildSpaceProvider(
            options,
            new DispatchState(),
            new AuditState(),
            spaceNotifier: outbox);
        var worker = NewWorker(provider);

        await worker.ProcessOnceAsync();

        await using (var pendingDb = new CP6Context(options))
        {
            var pending =
                await pendingDb.IntegrationEvents.SingleAsync();
            Assert.Null(pending.DeadLetterNotifiedAtUtc);
            Assert.Null(
                pending.DeadLetterNotificationLeaseId);
            Assert.Null(
                pending.DeadLetterNotificationLeaseUntilUtc);
        }

        await worker.ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var notified =
            await assertDb.IntegrationEvents.SingleAsync();
        Assert.NotNull(notified.DeadLetterNotifiedAtUtc);
        Assert.Null(notified.DeadLetterNotificationLeaseId);
        Assert.Null(
            notified.DeadLetterNotificationLeaseUntilUtc);
        Assert.Equal(3, outbox.Calls);
        Assert.Equal(3, outbox.LeaseIds.Distinct().Count());
    }

    [Fact]
    public async Task Worker_two_relational_outbox_scans_notify_once()
    {
        var database = NewSqliteClaimDatabase(
            new NoopDbCommandInterceptor());
        await using var anchor = database.Anchor;
        var dead = NewFailedSpaceEvent(attempts: 5);
        dead.Status = IntegrationEventStatus.DeadLetter;
        dead.NextRetryAt = null;
        await using (var seed =
                     new SqliteIntegrationContext(
                         database.Options))
        {
            seed.IntegrationEvents.Add(dead);
            await seed.SaveChangesAsync();
        }
        var outbox = new SpaceDeadLetterState();
        var notifier = new Mock<IDeadLetterNotifier>();
        await using var provider = BuildSqliteSpaceProvider(
            database.Options,
            new DispatchState(),
            new AuditState(),
            notifier,
            outbox);

        await Task.WhenAll(
            NewWorker(provider).ProcessOnceAsync(),
            NewWorker(provider).ProcessOnceAsync());

        await using var assertDb =
            new SqliteIntegrationContext(database.Options);
        var notified =
            await assertDb.IntegrationEvents.SingleAsync();
        Assert.NotNull(notified.DeadLetterNotifiedAtUtc);
        Assert.Null(notified.DeadLetterNotificationLeaseId);
        Assert.Equal(1, outbox.Calls);
        Assert.Single(outbox.LeaseIds);
    }

    [Fact]
    public async Task Worker_outer_non_space_conflict_cannot_undo_outbox_ack()
    {
        var options = NewOptions();
        var dead = NewFailedSpaceEvent(attempts: 5);
        dead.Status = IntegrationEventStatus.DeadLetter;
        dead.NextRetryAt = null;
        var nonSpace = NewFailedEvent(
            attempts: 0,
            DateTime.UtcNow.AddSeconds(-1));
        await using (var seed = new CP6Context(options))
        {
            seed.IntegrationEvents.AddRange(dead, nonSpace);
            await seed.SaveChangesAsync();
        }
        var outbox = new SpaceDeadLetterState();
        await using var provider = BuildSpaceProvider(
            options,
            new DispatchState(),
            new AuditState(),
            contextFactory: () =>
                new NonSpaceConflictContext(options),
            spaceNotifier: outbox);

        await NewWorker(provider).ProcessOnceAsync();

        await using var assertDb = new CP6Context(options);
        var events = await assertDb.IntegrationEvents
            .ToListAsync();
        var savedDead = Assert.Single(
            events,
            evt => evt.Id == dead.Id);
        var savedNonSpace = Assert.Single(
            events,
            evt => evt.Id == nonSpace.Id);
        Assert.NotNull(savedDead.DeadLetterNotifiedAtUtc);
        Assert.Null(savedDead.DeadLetterNotificationLeaseId);
        Assert.Equal(
            IntegrationEventStatus.Failed,
            savedNonSpace.Status);
        Assert.Equal(0, savedNonSpace.Attempts);
        Assert.Equal(1, outbox.Calls);
    }

    [Fact]
    public async Task Worker_pre_scan_prevents_poison_due_event_from_starving_existing_outbox()
    {
        var options = NewOptions();
        var dead = NewFailedSpaceEvent(attempts: 5);
        dead.Status = IntegrationEventStatus.DeadLetter;
        dead.NextRetryAt = null;
        dead.CreateDate = DateTime.UtcNow.AddMinutes(-5);
        var poison = NewFailedEvent(
            attempts: 0,
            DateTime.UtcNow.AddMinutes(-30));
        poison.CreateDate = DateTime.UtcNow.AddMinutes(-30);
        await using (var seed = new CP6Context(options))
        {
            seed.IntegrationEvents.AddRange(dead, poison);
            await seed.SaveChangesAsync();
        }

        var dispatcher =
            new Mock<IIntegrationEventDispatcher>();
        dispatcher
            .Setup(value => value.DispatchAsync(
                It.IsAny<IntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(
                "poison adapter cancellation"));
        var legacyNotifier = new Mock<IDeadLetterNotifier>();
        var durableNotifier = new SpaceDeadLetterState();
        await using var provider = BuildProvider(
            options,
            dispatcher,
            legacyNotifier,
            durableNotifier);
        var worker = NewWorker(provider);

        for (var scan = 0; scan < 2; scan++)
        {
            var error = await Record.ExceptionAsync(
                () => worker.ProcessOnceAsync());
            Assert.True(
                error is null or OperationCanceledException,
                error?.ToString());
        }

        await using var assertDb = new CP6Context(options);
        var savedDead = await assertDb.IntegrationEvents
            .SingleAsync(evt => evt.Id == dead.Id);
        Assert.NotNull(savedDead.DeadLetterNotifiedAtUtc);
        Assert.Null(savedDead.DeadLetterNotificationLeaseId);
        Assert.Null(
            savedDead.DeadLetterNotificationLeaseUntilUtc);
        Assert.Equal(1, durableNotifier.Calls);
        Assert.Single(durableNotifier.LeaseIds);
        dispatcher.Verify(
            value => value.DispatchAsync(
                It.Is<IntegrationEvent>(
                    evt => evt.Id == poison.Id),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
