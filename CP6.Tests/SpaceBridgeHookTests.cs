using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests;

/// <summary>
/// SpaceBridgeHook 测试（ch04 §2）。[InMemory 仅测逻辑]
/// </summary>
public class SpaceBridgeHookTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SpaceExecutionContextAccessor NewExecution(
        Guid? correlationId = null,
        Guid? jobId = null,
        Guid? publishAttemptId = null)
    {
        var execution = new SpaceExecutionContextAccessor();
        execution.Push(SpaceExecutionContext.ForUser(
            Guid.NewGuid(),
            "test-user",
            "Test User",
            correlationId ?? Guid.NewGuid(),
            Guid.NewGuid().ToString("N")));
        execution.Enrich(jobId: jobId, publishAttemptId: publishAttemptId);
        return execution;
    }

    private static LocationPublishBatch Batch(string batchNo = "LPUB-20260613-0001")
        => new()
        {
            BatchNo = batchNo,
            Items =
            {
                new LocationPublishItem
                {
                    Op = "UPSERT",
                    LocationId = Guid.NewGuid(),
                    LocationCode = "A-01-01-01",
                    Version = 1
                }
            }
        };

    [Fact]
    public async Task Publish_PersistsIntegrationEvent_Success()
    {
        using var db = Db();
        var publishAttemptId = Guid.NewGuid();
        var execution = NewExecution(publishAttemptId: publishAttemptId);
        var hook = new SpaceBridgeHook(
            db,
            NullLogger<SpaceBridgeHook>.Instance,
            new NoOpWmsLocationConsumer(),
            execution,
            execution);
        var r = await hook.OnLocationPublishedAsync(
            Batch(),
            execution.Current!.CorrelationId);

        Assert.True(r.Success);
        var evt = await db.IntegrationEvents.SingleAsync();
        Assert.Equal("SPACE", evt.SourceModule);
        Assert.Equal("WMS", evt.TargetModule);
        Assert.Equal("LPUB-20260613-0001", evt.SourceNo);
        Assert.Equal(IntegrationEventStatus.Success, evt.Status);
        Assert.NotNull(evt.JobId);
        Assert.Equal(publishAttemptId, evt.PublishAttemptId);
        Assert.Equal(execution.Current.JobId, evt.JobId);
        Assert.Equal(DateTimeKind.Utc, evt.CreateDate.Kind);
        Assert.Equal(evt.CreateDate, evt.OccurredAtUtc);
        Assert.Equal(DateTimeKind.Utc, evt.OccurredAtUtc!.Value.Kind);
    }

    [Fact]
    public async Task Publish_AllSkipped_PersistsSkippedStatus()
    {
        using var db = Db();
        // Consumer that returns all SKIPPED
        var consumer = new AllSkippedConsumer();
        var execution = NewExecution(publishAttemptId: Guid.NewGuid());
        var hook = new SpaceBridgeHook(
            db,
            NullLogger<SpaceBridgeHook>.Instance,
            consumer,
            execution,
            execution);
        var locId = Guid.NewGuid();
        var batch = new LocationPublishBatch
        {
            BatchNo = "LPUB-20260613-0002",
            Items = { new LocationPublishItem { Op = "UPSERT", LocationId = locId, LocationCode = "B-01-01-01", Version = 1 } }
        };
        var r = await hook.OnLocationPublishedAsync(
            batch,
            execution.Current!.CorrelationId);
        Assert.True(r.Success); // still ok (skipped is not failure)
        var evt = await db.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Skipped, evt.Status);
    }

    [Fact]
    public async Task Hook_PersistEventFalse_DoesNotInsertEventRow()
    {
        using var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var existingJobId = Guid.NewGuid();
        var execution = NewExecution(
            jobId: existingJobId,
            publishAttemptId: Guid.NewGuid());
        var hook = new SpaceBridgeHook(
            db,
            NullLogger<SpaceBridgeHook>.Instance,
            new NoOpWmsLocationConsumer(),
            execution,
            execution);
        var batch = new LocationPublishBatch { BatchNo = "LPUB-20260705-0001" };

        var r = await hook.OnLocationPublishedAsync(
            batch,
            execution.Current!.CorrelationId,
            persistEvent: false);

        Assert.True(r.Success);
        // 重试路径（Dispatcher）走此分支：Worker 更新原事件行，hook 不得再新插一行
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());
        Assert.Equal(existingJobId, execution.Current.JobId);
    }

    [Fact]
    public async Task Hook_persist_event_false_rethrows_original_consumer_exception()
    {
        using var db = Db();
        var execution = NewExecution(
            jobId: Guid.NewGuid(),
            publishAttemptId: Guid.NewGuid());
        var expected = new InvalidOperationException(
            "secret retry response body");
        var hook = new SpaceBridgeHook(
            db,
            NullLogger<SpaceBridgeHook>.Instance,
            new ExactThrowingConsumer(expected),
            execution,
            execution);

        var actual = await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => hook.OnLocationPublishedAsync(
                Batch(),
                execution.Current!.CorrelationId,
                persistEvent: false));

        Assert.Same(expected, actual);
        Assert.Empty(db.IntegrationEvents);
    }

    [Fact]
    public async Task Publish_rejects_correlation_conflict_before_consumer_call()
    {
        using var db = Db();
        var execution = NewExecution(publishAttemptId: Guid.NewGuid());
        var consumer = new RecordingConsumer();
        var hook = new SpaceBridgeHook(
            db,
            NullLogger<SpaceBridgeHook>.Instance,
            consumer,
            execution,
            execution);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => hook.OnLocationPublishedAsync(Batch(), Guid.NewGuid()));

        Assert.Equal("SPACE_EXECUTION_CONTEXT_CONFLICT", error.Message);
        Assert.Equal(0, consumer.Calls);
        Assert.Null(execution.Current!.JobId);
        Assert.Empty(db.IntegrationEvents);
    }

    [Fact]
    public async Task Publish_consumer_rejection_uses_stable_safe_code()
    {
        using var db = Db();
        var execution = NewExecution(publishAttemptId: Guid.NewGuid());
        var hook = new SpaceBridgeHook(
            db,
            NullLogger<SpaceBridgeHook>.Instance,
            new RejectingConsumer(),
            execution,
            execution);

        var result = await hook.OnLocationPublishedAsync(
            Batch(),
            execution.Current!.CorrelationId);

        var evt = await db.IntegrationEvents.SingleAsync();
        Assert.False(result.Success);
        Assert.Equal("SPACE_ADAPTER_REJECTED", result.Message);
        Assert.Equal("SPACE_ADAPTER_REJECTED", evt.LastError);
        Assert.DoesNotContain("secret adapter reason", evt.LastError);
    }

    [Fact]
    public async Task Publish_exception_persists_and_returns_only_sanitized_error()
    {
        using var db = Db();
        var execution = NewExecution(publishAttemptId: Guid.NewGuid());
        var hook = new SpaceBridgeHook(
            db,
            NullLogger<SpaceBridgeHook>.Instance,
            new ThrowingConsumer("secret response body"),
            execution,
            execution);

        var result = await hook.OnLocationPublishedAsync(
            Batch(),
            execution.Current!.CorrelationId);

        var evt = await db.IntegrationEvents.SingleAsync();
        Assert.False(result.Success);
        Assert.StartsWith("SPACE_ADAPTER_FAILURE:", result.Message);
        Assert.Equal(result.Message, evt.LastError);
        Assert.DoesNotContain("secret response body", evt.LastError);
        Assert.DoesNotContain("secret response body", result.Message);
        Assert.NotNull(evt.JobId);
        Assert.Equal(execution.Current.PublishAttemptId, evt.PublishAttemptId);
        Assert.Contains("LPUB-20260613-0001", evt.PayloadJson);
        Assert.DoesNotContain("secret response body", evt.PayloadJson);
    }

    [Fact]
    public async Task Space_persistence_failure_log_is_sanitized_and_has_no_exception_object()
    {
        const string secret = "secret database response body";
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new FirstSaveFailsContext(options, secret);
        var execution = NewExecution(publishAttemptId: Guid.NewGuid());
        var logger = new RecordingLogger();
        var batch = Batch();
        batch.Items[0].LocationCode = "secret payload token";
        var hook = new SpaceBridgeHook(
            db,
            logger,
            new NoOpWmsLocationConsumer(),
            execution,
            execution);

        var result = await hook.OnLocationPublishedAsync(
            batch,
            execution.Current!.CorrelationId);

        Assert.True(result.Success);
        var log = Assert.Single(logger.Entries);
        Assert.Null(log.Exception);
        Assert.Contains("SPACE_OUTBOX_PERSIST_FAILED", log.Message);
        Assert.Contains(nameof(InvalidOperationException), log.Message);
        Assert.DoesNotContain(secret, log.Message);
        Assert.DoesNotContain("secret payload token", log.Message);
        Assert.Empty(db.ChangeTracker.Entries<IntegrationEvent>());

        db.Space_Sites.Add(new CP6.Entity.DomainModels.Space.Space_Site
        {
            Id = Guid.NewGuid(),
            SiteCode = "RECOVERY",
            SiteName = "Recovery"
        });
        await db.SaveChangesAsync();

        Assert.Single(await db.Space_Sites.ToListAsync());
        Assert.Empty(await db.IntegrationEvents.ToListAsync());
    }

    private sealed class AllSkippedConsumer : IWmsLocationConsumer
    {
        public Task<WmsConsumeResult> ConsumeAsync(
            LocationPublishBatch batch,
            SpaceRetryFence? retryFence = null,
            CancellationToken ct = default) =>
            Task.FromResult(new WmsConsumeResult
            {
                Success = true,
                AllSkipped = true,
                Items = batch.Items.ConvertAll(i => new WmsItemResult { LocationId = i.LocationId, Status = "SKIPPED" })
            });
    }

    private sealed class RecordingConsumer : IWmsLocationConsumer
    {
        public int Calls { get; private set; }

        public Task<WmsConsumeResult> ConsumeAsync(
            LocationPublishBatch batch,
            SpaceRetryFence? retryFence = null,
            CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new WmsConsumeResult { Success = true });
        }
    }

    private sealed class RejectingConsumer : IWmsLocationConsumer
    {
        public Task<WmsConsumeResult> ConsumeAsync(
            LocationPublishBatch batch,
            SpaceRetryFence? retryFence = null,
            CancellationToken ct = default)
            => Task.FromResult(new WmsConsumeResult
            {
                Success = false,
                Items =
                {
                    new WmsItemResult
                    {
                        LocationId = Guid.NewGuid(),
                        Status = "REJECTED",
                        Reason = "secret adapter reason"
                    }
                }
            });
    }

    private sealed class ThrowingConsumer : IWmsLocationConsumer
    {
        private readonly string _message;

        public ThrowingConsumer(string message) => _message = message;

        public Task<WmsConsumeResult> ConsumeAsync(
            LocationPublishBatch batch,
            SpaceRetryFence? retryFence = null,
            CancellationToken ct = default)
            => throw new InvalidOperationException(_message);
    }

    private sealed class ExactThrowingConsumer :
        IWmsLocationConsumer
    {
        private readonly Exception _exception;

        public ExactThrowingConsumer(Exception exception)
            => _exception = exception;

        public Task<WmsConsumeResult> ConsumeAsync(
            LocationPublishBatch batch,
            SpaceRetryFence? retryFence = null,
            CancellationToken ct = default)
            => throw _exception;
    }

    private sealed class FirstSaveFailsContext : CP6Context
    {
        private readonly string _message;
        private int _saveCalls;

        public FirstSaveFailsContext(
            DbContextOptions<CP6Context> options,
            string message)
            : base(options)
        {
            _message = message;
        }

        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _saveCalls) == 1)
                throw new InvalidOperationException(_message);

            return base.SaveChangesAsync(
                acceptAllChangesOnSuccess,
                cancellationToken);
        }
    }

    private sealed class RecordingLogger : ILogger<SpaceBridgeHook>
    {
        public List<LogEntry> Entries { get; } = new();

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
            if (logLevel >= LogLevel.Error)
                Entries.Add(new LogEntry(formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(string Message, Exception? Exception);
}
