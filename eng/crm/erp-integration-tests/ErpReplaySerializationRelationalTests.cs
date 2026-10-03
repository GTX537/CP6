using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Text;
using CP6.Core.Persistence;
using CP6.Core.Services.ErpIntegration;
using CP6.Platform.EntityFramework;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit.Abstractions;

namespace CP6.ErpIntegration.SqlTests;

/// <summary>A separate, required PostgreSQL lane; the original 95-case matrix is unchanged.</summary>
[Collection(ErpRelationalCollection.Name)]
[Trait("DatabaseProvider", "PostgreSql")]
public sealed class ErpReplaySerializationRelationalTests(ErpRelationalFixture database, ITestOutputHelper output)
{
    private const string Actor = "erp-serialization-operator";

    [Fact]
    public async Task Inbox_replay_restarts_the_owned_transaction_after_native_40001()
    {
        RequirePostgreSql();
        var s = new ErpScenario(database);
        await s.InitializeAsync(maxInboxAttempts: 1);
        var request = await s.ReadyOrderAsync();
        var envelope = s.Envelope(request);
        var staged = false;
        var unavailable = s.Handler((stage, _) =>
        {
            Assert.Equal("order-staged", stage);
            staged = true;
            // Prepare a real deadletter through the existing handler's rollback protocol.
            throw new TimeoutException("C03 fixture dependency unavailable after actual order save.");
        });
        var failed = await unavailable.ConsumeAsync(envelope.Payload, envelope.TopicName, envelope.PartitionKey);
        Assert.True(staged);
        Assert.Equal(Cp6InboxDisposition.DeadLettered, failed.Disposition);
        await s.AssertNoOrderAsync();
        ErpInboxReceipt original;
        int outboxBefore;
        await using (var queue = s.Queue())
        {
            original = await queue.Inbox.AsNoTracking().SingleAsync(x => x.MessageId == envelope.MessageId);
            outboxBefore = await queue.Set<Cp6OutboxMessage>().CountAsync(x => x.TenantId == s.Tenant);
        }
        Assert.Equal(ErpInboxStatus.DeadLettered, original.Status);
        Assert.Equal(1, original.AttemptCount);
        var input = new ErpReplayRequest(Guid.NewGuid(), original.RowVersion.ToArray(), original.PayloadSha256,
            "dependency-recovered");
        var factory = new ObservedFactory(database);
        var service = new ErpInboxReplayService(factory, s.Runtime);
        var scheduled = await RaceAsync($"c03:replay:{s.Tenant:D}:{input.OperationId:D}", factory,
            ct => service.ScheduleAsync(s.Tenant, envelope.MessageId, input, Actor, ct));
        Assert.Equal(scheduled[0], scheduled[1]);

        byte[] committedVersion;
        await using (var verify = s.Queue())
        {
            var receipt = await verify.Inbox.AsNoTracking().SingleAsync(x => x.MessageId == envelope.MessageId);
            Assert.Equal(ErpInboxStatus.Processing, receipt.Status);
            Assert.Equal(0, receipt.AttemptCount);
            Assert.Null(receipt.ProcessedAtUtc);
            Assert.Null(receipt.ErrorCode);
            Assert.Equal(s.Clock.GetUtcNow(), receipt.RetryAtUtc);
            Assert.Equal(scheduled[0].ReplayedAtUtc, receipt.ReplayedAtUtc);
            Assert.Equal(original.Payload, receipt.Payload);
            Assert.Equal(original.PayloadSha256, receipt.PayloadSha256);
            Assert.NotEqual(original.RowVersion, receipt.RowVersion);
            committedVersion = receipt.RowVersion.ToArray();
            var audit = Assert.Single(await verify.ReplayAudits.AsNoTracking()
                .Where(x => x.TenantId == s.Tenant).ToArrayAsync());
            Assert.Equal(input.OperationId, audit.OperationId);
            Assert.Equal(input.RowVersion, audit.InputRowVersion);
            Assert.Equal(original.AttemptCount, audit.PreviousAttemptCount);
            Assert.Equal(envelope.MessageId, audit.MessageId);
            Assert.Equal(scheduled[0].ReplayedAtUtc, audit.ReplayedAtUtc);
            Assert.Equal(outboxBefore, await verify.Set<Cp6OutboxMessage>().CountAsync(x => x.TenantId == s.Tenant));
        }
        // Reuse the original input token; no test-level retry or input repair is allowed.
        Assert.Equal(scheduled[0], await new ErpInboxReplayService(database, s.Runtime)
            .ScheduleAsync(s.Tenant, envelope.MessageId, input, Actor));
        await using (var verify = s.Queue())
        {
            Assert.Equal(committedVersion, (await verify.Inbox.AsNoTracking()
                .SingleAsync(x => x.MessageId == envelope.MessageId)).RowVersion);
            Assert.Equal(1, await verify.ReplayAudits.CountAsync(x => x.TenantId == s.Tenant));
        }
        await s.AssertNoOrderAsync();
    }

    [Fact]
    public async Task Delivery_replay_restarts_the_owned_transaction_after_native_40001()
    {
        RequirePostgreSql();
        var s = new ErpScenario(database);
        await s.InitializeAsync();
        var request = await s.ReadyOrderAsync();
        await s.ConsumeAsync(s.Envelope(request));
        ErpOrderBridgeDispatch original;
        int outboxBefore;
        await using (var queue = s.Queue())
        {
            original = await queue.OrderBridges.SingleAsync(x => x.TenantId == s.Tenant);
            // Seed only exhausted worker metadata; the order and bridge came from the real handler.
            original.AttemptCount = 10;
            original.LastErrorCode = "C03_BRIDGE_REPLAY_REQUIRED";
            original.AvailableAtUtc = s.Clock.GetUtcNow();
            await queue.SaveChangesAsync();
            outboxBefore = await queue.Set<Cp6OutboxMessage>().CountAsync(x => x.TenantId == s.Tenant);
        }
        var hash = ErpEventContracts.Hash(Encoding.UTF8.GetBytes($"c03:bridge:{s.Tenant:D}:{original.OrderKey}"));
        var input = new ErpReplayRequest(Guid.NewGuid(), original.RowVersion.ToArray(), hash, "dependency-recovered");
        var factory = new ObservedFactory(database);
        var service = new ErpDeliveryReplayService(factory, s.Runtime);
        var scheduled = await RaceAsync($"c03:delivery-replay:{s.Tenant:D}", factory,
            ct => service.ScheduleBridgeAsync(s.Tenant, original.OrderKey, input, Actor, ct));
        Assert.Equal(scheduled[0], scheduled[1]);

        byte[] committedVersion;
        await using (var verify = s.Queue())
        {
            var bridge = await verify.OrderBridges.AsNoTracking().SingleAsync(x => x.TenantId == s.Tenant);
            Assert.Equal(0, bridge.AttemptCount);
            Assert.Null(bridge.LastErrorCode);
            Assert.Null(bridge.CompletedAtUtc);
            Assert.Null(bridge.LeaseOwner);
            Assert.Null(bridge.LeaseExpiresAtUtc);
            Assert.Equal(s.Clock.GetUtcNow(), bridge.AvailableAtUtc);
            Assert.NotEqual(original.RowVersion, bridge.RowVersion);
            committedVersion = bridge.RowVersion.ToArray();
            var audit = Assert.Single(await verify.Set<ErpDeliveryReplayAudit>().AsNoTracking()
                .Where(x => x.TenantId == s.Tenant).ToArrayAsync());
            Assert.Equal(input.OperationId, audit.OperationId);
            Assert.Equal(input.RowVersion, audit.InputRowVersion);
            Assert.Equal(10, audit.PreviousAttemptCount);
            Assert.Equal("bridge", audit.Kind);
            Assert.Equal(original.OrderKey, audit.TargetId);
            Assert.Equal(scheduled[0].ReplayedAtUtc, audit.ReplayedAtUtc);
            Assert.Equal(outboxBefore, await verify.Set<Cp6OutboxMessage>().CountAsync(x => x.TenantId == s.Tenant));
        }
        Assert.Equal(scheduled[0], await new ErpDeliveryReplayService(database, s.Runtime)
            .ScheduleBridgeAsync(s.Tenant, original.OrderKey, input, Actor));
        await using (var verify = s.Queue())
        {
            Assert.Equal(committedVersion, (await verify.OrderBridges.AsNoTracking()
                .SingleAsync(x => x.TenantId == s.Tenant)).RowVersion);
            Assert.Equal(1, await verify.Set<ErpDeliveryReplayAudit>().CountAsync(x => x.TenantId == s.Tenant));
        }
        await using var business = s.Db();
        Assert.Single(await business.Orders.ToArrayAsync());
        Assert.Single(await business.OrderDetails.ToArrayAsync());
        Assert.Equal(0, s.ExternalCalls.Count);
    }

    private void RequirePostgreSql()
    {
        Assert.True(database.IsPostgreSql, "This required lane must explicitly select PostgreSql; use the original matrix for SQL Server.");
        output.WriteLine(database.SetupSummary);
    }

    private async Task<T[]> RaceAsync<T>(string resource, ObservedFactory factory, Func<CancellationToken, Task<T>> schedule)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var owner = database.CreateDbContext();
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, deadline.Token);
        Assert.True(await DatabaseResourceLocks.TryAcquireTransactionAsync(owner, resource, 0, deadline.Token));
        var ownerPid = ((NpgsqlConnection)owner.Database.GetDbConnection()).ProcessID;
        Task<T>? first = null;
        Task<T>? second = null;
        var released = false;
        try
        {
            first = schedule(deadline.Token);
            second = schedule(deadline.Token);
            await ObserveBothSnapshotsAsync(ownerPid, factory, first, second, deadline.Token);
            await transaction.CommitAsync(deadline.Token);
            released = true;
            var result = await Task.WhenAll(first, second).WaitAsync(deadline.Token);
            factory.AssertOneNativeRetry();
            return result;
        }
        finally
        {
            deadline.Cancel();
            try
            {
                if (!released) await transaction.RollbackAsync(CancellationToken.None);
            }
            finally
            {
                // Cancel and release before joining; no context/connection is disposed while a caller is live.
                try { await Task.WhenAll(new[] { first, second }.OfType<Task<T>>()); }
                catch { /* Preserve the test body's original failure after observing both callers. */ }
                factory.WriteEvidence(output);
            }
        }
    }

    private async Task ObserveBothSnapshotsAsync<T>(int ownerPid, ObservedFactory factory, Task<T> first, Task<T> second,
        CancellationToken ct)
    {
        await using var observer = new NpgsqlConnection(database.ConnectionString);
        await observer.OpenAsync(ct);
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(7))
        {
            ct.ThrowIfCancellationRequested();
            Assert.False(first.IsCompleted || second.IsCompleted, "Both original replay calls must still be waiting for the owned resource.");
            var starts = factory.Events.Where(x => x.Kind == "started").ToArray();
            if (starts.Length == 2)
            {
                Assert.All(starts, start => Assert.Equal(IsolationLevel.Serializable, start.Isolation));
                var pids = starts.Select(x => x.BackendPid).ToArray();
                Assert.Equal(2, pids.Distinct().Count());
                Assert.DoesNotContain(ownerPid, pids);
                // Production uses pg_try_advisory_xact_lock polling, not a blocking advisory call.
                // backend_xmin proves a retained snapshot; the actual last query proves both reached
                // that poll while the exact owner lock remains granted and neither caller owns it.
                const string sql = """
                    WITH held AS (
                        SELECT database, classid, objid, objsubid
                        FROM pg_catalog.pg_locks
                        WHERE pid=@ownerPid AND locktype='advisory' AND granted
                    )
                    SELECT a.pid AS "Pid", a.backend_xmin::text AS "SnapshotXmin"
                    FROM pg_catalog.pg_stat_activity a
                    WHERE a.datname=current_database() AND a.pid=ANY(@pids)
                      AND a.backend_xmin IS NOT NULL
                      AND a.state IN ('active','idle in transaction')
                      AND a.query LIKE '%pg_catalog.pg_try_advisory_xact_lock%'
                      AND (SELECT count(*) FROM held)=1
                      AND NOT EXISTS (
                          SELECT 1 FROM pg_catalog.pg_locks contender JOIN held
                            ON contender.database=held.database AND contender.classid=held.classid
                           AND contender.objid=held.objid AND contender.objsubid=held.objsubid
                          WHERE contender.pid=a.pid AND contender.locktype='advisory' AND contender.granted
                      )
                    """;
                var waiting = (await observer.QueryAsync<SnapshotWaiter>(new CommandDefinition(sql,
                    new { ownerPid, pids }, commandTimeout: 3, cancellationToken: ct))).ToArray();
                if (waiting.Length == 2)
                {
                    Assert.Equal(pids.Order(), waiting.Select(x => x.Pid).Order());
                    Assert.All(waiting, value => Assert.False(string.IsNullOrWhiteSpace(value.SnapshotXmin)));
                    output.WriteLine($"Provider=PostgreSql; ownerPid={ownerPid}; originalSnapshotPids={string.Join(',', pids)}; nativeAdvisoryPollers=2.");
                    return;
                }
            }
            await Task.Delay(25, ct);
        }
        throw new TimeoutException("C03_NATIVE_REPLAY_SNAPSHOT_BARRIER_NOT_OBSERVED");
    }

    private sealed record SnapshotWaiter(int Pid, string SnapshotXmin);

    private sealed record Observation(int Sequence, string Kind, Guid ContextId, Guid TransactionId = default,
        int BackendPid = 0, IsolationLevel Isolation = IsolationLevel.Unspecified, string? SqlState = null);

    /// <summary>Observes real EF/provider events without changing a command, result, exception, or retry.</summary>
    private sealed class ObservedFactory(ErpRelationalFixture database) : IDbContextFactory<ErpIntegrationContext>
    {
        public ConcurrentQueue<Observation> Events { get; } = new();
        private int sequence;

        public ErpIntegrationContext CreateDbContext()
        {
            var profile = DatabaseMigrationProfile.For(database.Database, DatabaseContextKind.ErpIntegration);
            var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<ErpIntegrationContext>(),
                database.Database, database.ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
            options.LogTo((id, _) => id == RelationalEventId.TransactionStarted || id == RelationalEventId.TransactionCommitted ||
                id == RelationalEventId.TransactionDisposed || id == RelationalEventId.CommandError ||
                id == RelationalEventId.TransactionError || id == CoreEventId.SaveChangesFailed ||
                id == CoreEventId.ContextDisposed, Observe);
            var context = new ErpIntegrationContext(options.Options);
            Record("created", context.ContextId.InstanceId);
            context.Database.SetCommandTimeout(20);
            return context;
        }

        public Task<ErpIntegrationContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }

        private void Observe(EventData data)
        {
            if (data is not DbContextEventData { Context: { } context }) return;
            var contextId = context.ContextId.InstanceId;
            if (data is TransactionEventData transaction)
            {
                if (data.EventId == RelationalEventId.TransactionStarted)
                    Record("started", contextId, transaction.TransactionId,
                        ((NpgsqlConnection)transaction.Transaction.Connection!).ProcessID, transaction.Transaction.IsolationLevel);
                else if (data.EventId == RelationalEventId.TransactionCommitted)
                    Record("committed", contextId, transaction.TransactionId);
                else if (data.EventId == RelationalEventId.TransactionDisposed)
                    Record("transaction-disposed", contextId, transaction.TransactionId);
            }
            if (data.EventId == CoreEventId.ContextDisposed) Record("context-disposed", contextId);
            var error = data switch
            {
                CommandErrorEventData command => command.Exception,
                TransactionErrorEventData transactionError => transactionError.Exception,
                DbContextErrorEventData save => save.Exception,
                _ => null
            };
            for (var cause = error; cause is not null; cause = cause.InnerException)
                if (cause is PostgresException native)
                {
                    Record("native-error", contextId, sqlState: native.SqlState);
                    break;
                }
        }

        private void Record(string kind, Guid context, Guid transaction = default, int pid = 0,
            IsolationLevel isolation = IsolationLevel.Unspecified, string? sqlState = null) =>
            Events.Enqueue(new(Interlocked.Increment(ref sequence), kind, context, transaction, pid, isolation, sqlState));

        public void AssertOneNativeRetry()
        {
            var events = Events.OrderBy(x => x.Sequence).ToArray();
            var created = events.Where(x => x.Kind == "created").ToArray();
            var started = events.Where(x => x.Kind == "started").ToArray();
            var failures = events.Where(x => x.Kind == "native-error").ToArray();
            Assert.NotEmpty(failures);
            Assert.All(failures, failure => Assert.Equal("40001", failure.SqlState));
            var failedContext = Assert.Single(failures.Select(x => x.ContextId).Distinct());
            Assert.Equal(3, created.Length);
            Assert.Equal(3, created.Select(x => x.ContextId).Distinct().Count());
            Assert.Equal(3, started.Length);
            Assert.Equal(3, started.Select(x => x.TransactionId).Distinct().Count());
            Assert.All(started, start => Assert.Equal(IsolationLevel.Serializable, start.Isolation));
            Assert.Equal(2, events.Count(x => x.Kind == "committed"));
            Assert.DoesNotContain(events, x => x.Kind == "committed" && x.ContextId == failedContext);
            Assert.All(created, creation => Assert.Single(events,
                x => x.Kind == "context-disposed" && x.ContextId == creation.ContextId));
            var failedStart = Assert.Single(started, x => x.ContextId == failedContext);
            var disposedTransaction = Assert.Single(events,
                x => x.Kind == "transaction-disposed" && x.TransactionId == failedStart.TransactionId);
            var disposedContext = Assert.Single(events,
                x => x.Kind == "context-disposed" && x.ContextId == failedContext);
            Assert.True(failures.Max(x => x.Sequence) < disposedTransaction.Sequence);
            Assert.True(disposedTransaction.Sequence < disposedContext.Sequence);
            Assert.True(disposedContext.Sequence < created[2].Sequence,
                "The failed owned transaction and context must both be disposed before the retry factory call.");
            Assert.NotEqual(failedContext, created[2].ContextId);
        }

        public void WriteEvidence(ITestOutputHelper output)
        {
            foreach (var item in Events.OrderBy(x => x.Sequence))
                output.WriteLine($"Provider=PostgreSql; seq={item.Sequence}; event={item.Kind}; context={item.ContextId:D}; transaction={item.TransactionId:D}; pid={item.BackendPid}; isolation={item.Isolation}; SQLSTATE={item.SqlState ?? "none"}.");
        }
    }
}
