using System.Data;
using System.Data.Common;
using System.Diagnostics;
using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.DatabaseCompatibility.RuntimeProbe;

internal static class ResourceLockGates
{
    private const int FiniteWaitMilliseconds = 150;
    private const int CancellationWaitMilliseconds = 5_000;
    private static readonly TimeSpan GateBudget = TimeSpan.FromSeconds(30);

    public static async Task<string> TransactionAsync(RuntimeFixture fixture)
    {
        using var budget = new CancellationTokenSource(GateBudget);
        var cancellationToken = budget.Token;
        var tenant = Guid.NewGuid();
        var version = Guid.NewGuid();
        var floor = Guid.NewGuid();
        var resource = FloorKey(tenant, version, floor);
        var differentTenantResource = FloorKey(Guid.NewGuid(), version, floor);
        await using var holder = fixture.Core(tenant);
        await using var contender = fixture.Core(tenant);
        await holder.Database.OpenConnectionAsync(cancellationToken);
        await contender.Database.OpenConnectionAsync(cancellationToken);
        await RequireDistinctSessionsAsync(fixture, holder.Database.GetDbConnection(), contender.Database.GetDbConnection(), cancellationToken);

        await using (var holderTransaction = await holder.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
        await using (var contenderTransaction = await contender.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
        {
            ProbeAssert.Require(await DatabaseResourceLocks.TryAcquireTransactionAsync(holder, resource, 0, cancellationToken),
                "The first transaction must acquire the exact floor-style logical key.");
            ProbeAssert.Require(!await DatabaseResourceLocks.TryAcquireTransactionAsync(contender, resource, 0, cancellationToken),
                "A second physical transaction must fail one zero-timeout attempt on the held key.");
            await RequireUsableAsync(contender.Database.GetDbConnection(), contenderTransaction.GetDbTransaction(), cancellationToken);

            var timer = Stopwatch.StartNew();
            ProbeAssert.Require(!await DatabaseResourceLocks.TryAcquireTransactionAsync(contender, resource, FiniteWaitMilliseconds, cancellationToken),
                "A held logical key must remain unavailable after a finite wait.");
            RequireFiniteWait(timer.Elapsed);
            await RequireUsableAsync(contender.Database.GetDbConnection(), contenderTransaction.GetDbTransaction(), cancellationToken);
            ProbeAssert.Require(await DatabaseResourceLocks.TryAcquireTransactionAsync(contender, differentTenantResource, 0, cancellationToken),
                "A different tenant resource namespace must remain independently acquirable.");

            await holderTransaction.CommitAsync(cancellationToken);
            ProbeAssert.Require(await DatabaseResourceLocks.TryAcquireTransactionAsync(contender, resource, 0, cancellationToken),
                "Holder commit must release the original key to the waiting physical transaction.");
            await contenderTransaction.RollbackAsync(cancellationToken);
        }

        await using (var holderTransaction = await holder.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
        await using (var contenderTransaction = await contender.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
        {
            ProbeAssert.Require(await DatabaseResourceLocks.TryAcquireTransactionAsync(holder, resource, 0, cancellationToken),
                "Rollback case must reacquire its original key.");
            ProbeAssert.Require(!await DatabaseResourceLocks.TryAcquireTransactionAsync(contender, resource, 0, cancellationToken),
                "Rollback case must observe real contention before rollback.");
            await holderTransaction.RollbackAsync(cancellationToken);
            ProbeAssert.Require(await DatabaseResourceLocks.TryAcquireTransactionAsync(contender, resource, 0, cancellationToken),
                "Explicit holder rollback must release the key.");
            await contenderTransaction.RollbackAsync(cancellationToken);
        }

        await using (var contenderTransaction = await contender.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
        {
            var holderTransaction = await holder.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var holderDisposed = false;
            try
            {
                ProbeAssert.Require(await DatabaseResourceLocks.TryAcquireTransactionAsync(holder, resource, 0, cancellationToken),
                    "Dispose case must acquire a key in an uncommitted transaction.");
                ProbeAssert.Require(!await DatabaseResourceLocks.TryAcquireTransactionAsync(contender, resource, 0, cancellationToken),
                    "Dispose case must observe real contention before disposal.");
                await holderTransaction.DisposeAsync();
                holderDisposed = true;
                ProbeAssert.Require(await DatabaseResourceLocks.TryAcquireTransactionAsync(contender, resource, 0, cancellationToken),
                    "Disposing an uncommitted holder transaction must release the key.");
                await contenderTransaction.RollbackAsync(cancellationToken);
            }
            finally
            {
                if (!holderDisposed) await holderTransaction.DisposeAsync();
            }
        }

        await using (var holderTransaction = await holder.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
        {
            ProbeAssert.Require(await DatabaseResourceLocks.TryAcquireTransactionAsync(holder, resource, 0, cancellationToken),
                "Cancellation case must hold a key before starting its competitor.");
            await using (var canceledTransaction = await contender.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
            {
                try
                {
                    using var canceledWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    canceledWait.CancelAfter(FiniteWaitMilliseconds);
                    var timer = Stopwatch.StartNew();
                    try
                    {
                        await DatabaseResourceLocks.TryAcquireTransactionAsync(contender, resource, CancellationWaitMilliseconds, canceledWait.Token);
                        throw new ProbeAssertionException("A canceled transaction-lock wait must throw cancellation.");
                    }
                    catch (OperationCanceledException) when (canceledWait.IsCancellationRequested)
                    {
                        ProbeAssert.Require(timer.Elapsed < TimeSpan.FromSeconds(10), "Cancellation must finish within a bounded wait.");
                    }
                }
                finally
                {
                    // A command cancellation can leave a PG transaction aborted. Recovery belongs
                    // to the whole owner protocol, rather than another command in that transaction.
                    await canceledTransaction.RollbackAsync(CancellationToken.None);
                }
            }
            await holderTransaction.RollbackAsync(cancellationToken);
        }
        await using (var freshTransaction = await contender.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
        {
            await RequireUsableAsync(contender.Database.GetDbConnection(), freshTransaction.GetDbTransaction(), cancellationToken);
            ProbeAssert.Require(await DatabaseResourceLocks.TryAcquireTransactionAsync(contender, resource, 0, cancellationToken),
                "A fresh transaction after canceled wait and rollback must reacquire the released key.");
            await freshTransaction.RollbackAsync(cancellationToken);
        }
        ProbeAssert.Require(holder.Database.GetDbConnection().State == ConnectionState.Open && contender.Database.GetDbConnection().State == ConnectionState.Open,
            "Resource-lock helpers must preserve explicitly opened caller connections.");
        return "Two native sessions: same-key zero/finite contention, usable transaction after busy timeout, distinct tenant key, commit/rollback/dispose release, cancellation then rollback and fresh transaction. Primitive resource namespaces only; full business isolation remains separate.";
    }

    public static async Task<string> SessionAsync(RuntimeFixture fixture)
    {
        using var budget = new CancellationTokenSource(GateBudget);
        var cancellationToken = budget.Token;
        var resource = "CP6:SpaceIntegrationEvent:OccurredAtUtc:v1:probe:" + Guid.NewGuid().ToString("N");
        await using var holder = fixture.Connection();
        await using var contender = fixture.Connection();
        await holder.OpenAsync(cancellationToken);
        await contender.OpenAsync(cancellationToken);
        await RequireDistinctSessionsAsync(fixture, holder, contender, cancellationToken);
        DatabaseSessionResourceLock? held = null;
        DatabaseSessionResourceLock? peerHeld = null;
        try
        {
            held = await DatabaseResourceLocks.TryAcquireSessionAsync(holder, fixture.Database.Provider, resource, 0, cancellationToken);
            ProbeAssert.Require(held is not null, "A session lock must be acquired on its already-open caller connection.");
            peerHeld = await DatabaseResourceLocks.TryAcquireSessionAsync(contender, fixture.Database.Provider, resource, 0, cancellationToken);
            ProbeAssert.Require(peerHeld is null, "The second physical session must observe the held session resource.");

            using (var canceledWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                canceledWait.CancelAfter(FiniteWaitMilliseconds);
                var timer = Stopwatch.StartNew();
                try
                {
                    peerHeld = await DatabaseResourceLocks.TryAcquireSessionAsync(contender, fixture.Database.Provider, resource, CancellationWaitMilliseconds, canceledWait.Token);
                    throw new ProbeAssertionException("A canceled session-lock wait must throw cancellation.");
                }
                catch (OperationCanceledException) when (canceledWait.IsCancellationRequested)
                {
                    ProbeAssert.Require(timer.Elapsed < TimeSpan.FromSeconds(10), "Session-lock cancellation must finish within a bounded wait.");
                }
            }
            await RequireUsableAsync(contender, null, cancellationToken);

            for (var batch = 0; batch < 2; batch++)
            {
                await using (var batchTransaction = await holder.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken))
                {
                    await RequireUsableAsync(holder, batchTransaction, cancellationToken);
                    await batchTransaction.CommitAsync(cancellationToken);
                }
                peerHeld = await DatabaseResourceLocks.TryAcquireSessionAsync(contender, fixture.Database.Provider, resource, 0, cancellationToken);
                ProbeAssert.Require(peerHeld is null, "A session lock must remain held across separately committed batches.");
            }

            await held!.ReleaseAsync(cancellationToken);
            await held.ReleaseAsync(cancellationToken);
            await held.DisposeAsync();
            ProbeAssert.Require(holder.State == ConnectionState.Open && contender.State == ConnectionState.Open,
                "Explicit release and repeated disposal must not close either caller connection.");
            await RequireUsableAsync(holder, null, cancellationToken);
            peerHeld = await DatabaseResourceLocks.TryAcquireSessionAsync(contender, fixture.Database.Provider, resource, 0, cancellationToken);
            ProbeAssert.Require(peerHeld is not null, "Explicit session release must allow the peer to acquire the same resource.");
            await peerHeld!.DisposeAsync();
            await peerHeld.DisposeAsync();
            await peerHeld.ReleaseAsync(cancellationToken);
            await RequireUsableAsync(contender, null, cancellationToken);

            held = await DatabaseResourceLocks.TryAcquireSessionAsync(holder, fixture.Database.Provider, resource, 0, cancellationToken);
            ProbeAssert.Require(held is not null, "Peer disposal must release its native session lock to the original caller.");
            return "Two native sessions: contention and canceled wait, two actual committed batches retain the lock, explicit release/Dispose are idempotent, peer reacquires and releases, both non-owned connections remain open and usable.";
        }
        finally
        {
            // Every returned owned handle is retained for cleanup even when an assertion fails.
            try { if (peerHeld is not null) await peerHeld.DisposeAsync(); }
            finally { if (held is not null) await held.DisposeAsync(); }
        }
    }

    public static async Task<string> UtcClockAsync(RuntimeFixture fixture)
    {
        using var budget = new CancellationTokenSource(GateBudget);
        var cancellationToken = budget.Token;
        await using var context = fixture.Core();
        await context.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var connection = context.Database.GetDbConnection();
        var nativeTransaction = transaction.GetDbTransaction();
        var nativeClockQuery = fixture.IsPostgreSql ? "SELECT pg_catalog.clock_timestamp()" : "SELECT SYSUTCDATETIME()";
        DateTime? transactionTime = fixture.IsPostgreSql
            ? await NativeUtcAsync(connection, nativeTransaction, "SELECT pg_catalog.transaction_timestamp()", cancellationToken)
            : null;
        var beforeFirst = await NativeUtcAsync(connection, nativeTransaction, nativeClockQuery, cancellationToken);
        var first = await DatabaseUtcClock.ReadUtcNowAsync(context, cancellationToken);
        var afterFirst = await NativeUtcAsync(connection, nativeTransaction, nativeClockQuery, cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);
        var beforeSecond = await NativeUtcAsync(connection, nativeTransaction, nativeClockQuery, cancellationToken);
        var second = await DatabaseUtcClock.ReadUtcNowAsync(context, cancellationToken);
        var afterSecond = await NativeUtcAsync(connection, nativeTransaction, nativeClockQuery, cancellationToken);
        ProbeAssert.Require(first.Kind == DateTimeKind.Utc && second.Kind == DateTimeKind.Utc,
            "Both database-clock reads must retain the UTC Kind contract.");
        ProbeAssert.Require(first >= beforeFirst && first <= afterFirst && second >= beforeSecond && second <= afterSecond,
            "Database-clock values must be bounded by independent native UTC queries on the same server and transaction.");
        ProbeAssert.Require(second > first, "Database UTC wall time must advance inside the same still-open transaction.");
        if (fixture.IsPostgreSql)
        {
            var afterTransactionTime = await NativeUtcAsync(connection, nativeTransaction, "SELECT pg_catalog.transaction_timestamp()", cancellationToken);
            ProbeAssert.Require(afterTransactionTime == transactionTime,
                "PostgreSQL transaction_timestamp must remain fixed while the chosen clock_timestamp advances.");
        }
        await transaction.RollbackAsync(cancellationToken);
        ProbeAssert.Require(connection.State == ConnectionState.Open, "The clock helper must not close the explicit caller connection.");
        return fixture.IsPostgreSql
            ? "Two UTC Kind reads advanced in one native transaction and matched independent clock_timestamp bounds; transaction_timestamp stayed fixed. No application-clock input or business-date conversion."
            : "Two UTC Kind reads advanced in one native transaction and matched independent SYSUTCDATETIME bounds; caller connection remained open. No application-clock input or business-date conversion.";
    }

    private static string FloorKey(Guid tenant, Guid version, Guid floor)
        => $"cp6:space:floor-edit:{tenant:N}:{version:N}:{floor:N}";

    private static void RequireFiniteWait(TimeSpan elapsed)
        => ProbeAssert.Require(elapsed >= TimeSpan.FromMilliseconds(75) && elapsed < TimeSpan.FromSeconds(10),
            "The nominal150ms finite wait must actually wait and complete within a broad bounded interval.");

    private static async Task RequireDistinctSessionsAsync(RuntimeFixture fixture, DbConnection first, DbConnection second, CancellationToken cancellationToken)
    {
        var query = fixture.IsPostgreSql ? "SELECT pg_catalog.pg_backend_pid()" : "SELECT CONVERT(int,@@SPID)";
        var firstId = await ScalarAsync(first, null, query, cancellationToken);
        var secondId = await ScalarAsync(second, null, query, cancellationToken);
        ProbeAssert.Require(firstId is int && secondId is int && !Equals(firstId, secondId),
            "Lock contention requires two independently opened actual database sessions.");
    }

    private static async Task RequireUsableAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
        => ProbeAssert.Require(await ScalarAsync(connection, transaction, "SELECT 1", cancellationToken) is 1,
            "A caller connection/transaction must remain usable for an independent native SELECT.");

    private static async Task<DateTime> NativeUtcAsync(DbConnection connection, DbTransaction transaction, string query, CancellationToken cancellationToken)
    {
        var value = await ScalarAsync(connection, transaction, query, cancellationToken);
        return value is DateTime time
            ? DateTime.SpecifyKind(time, DateTimeKind.Utc)
            : throw new ProbeAssertionException("An independent native UTC query must return a timestamp.");
    }

    private static async Task<object?> ScalarAsync(DbConnection connection, DbTransaction? transaction, string query, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 10;
        command.CommandText = query;
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}
