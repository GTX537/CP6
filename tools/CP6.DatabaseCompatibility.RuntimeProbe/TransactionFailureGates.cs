using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using CP6.Core.Persistence;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.DatabaseCompatibility.RuntimeProbe;

internal static class TransactionFailureGates
{
    private static readonly TimeSpan GateBudget = TimeSpan.FromSeconds(45);
    private const int CommandTimeoutSeconds = 15;

    public static async Task<string> WholeTransactionRetryAsync(RuntimeFixture fixture)
    {
        await fixture.VerifyOwnerAsync();
        var name = "w3t_retry_" + Guid.NewGuid().ToString("N")[..16];
        ProbeAssert.Require(Regex.IsMatch(name, "\\A[a-z][a-z0-9_]{0,62}\\z", RegexOptions.CultureInvariant),
            "The transaction fixture identifier must be internally generated and bounded.");
        var table = (fixture.IsPostgreSql ? "public." : "dbo.") + fixture.Column(name);
        var markerName = name + "_marker";
        var markerTable = "public." + fixture.Column(markerName);
        using var budget = new CancellationTokenSource(GateBudget);
        try
        {
            await using (var setup = fixture.Connection())
            {
                await setup.OpenAsync(budget.Token);
                await ExecuteAsync(setup, null,
                    $"CREATE TABLE {table} ({fixture.Column("Id")} INTEGER NOT NULL PRIMARY KEY, {fixture.Column("Value")} INTEGER NOT NULL); " +
                    $"INSERT INTO {table} ({fixture.Column("Id")},{fixture.Column("Value")}) VALUES {(fixture.IsPostgreSql ? "(1,0)" : "(1,0),(2,0)")};",
                    null, budget.Token);
                if (fixture.IsPostgreSql)
                {
                    // A separate marker relation avoids unrelated Serializable predicate conflicts
                    // from a tiny-table sequential scan; it still proves earlier writes fully roll back.
                    await ExecuteAsync(setup, null,
                        $"CREATE TABLE {markerTable} ({fixture.Column("Id")} INTEGER NOT NULL PRIMARY KEY, {fixture.Column("Value")} INTEGER NOT NULL); " +
                        $"INSERT INTO {markerTable} ({fixture.Column("Id")},{fixture.Column("Value")}) VALUES (2,0);",
                        null, budget.Token);
                }
            }

            if (fixture.IsPostgreSql)
            {
                await PostgreSqlSerializationAsync(fixture, table, markerTable, budget.Token);
                return "Two physical Serializable sessions established the same snapshot; a committed competing update caused native40001. The classifier selected retryable SerializationFailure. Entire failed transaction rollback removed its earlier marker-row write; failed Context was disposed before fresh Context reread and completed the whole operation. Both exact fixture tables removed; no statement retry or business-hook replay.";
            }

            await SqlServerDeadlockAsync(fixture, table, budget.Token);
            return "Two physical ReadCommitted sessions each held one row, crossed a shared barrier and requested the other row. Exactly one native1205 victim fully rolled back and one transaction committed; classifier selected retryable Deadlock. Both Contexts were disposed before a fresh Context reread and completed the whole operation. Exact fixture table removed; no failed-transaction statement retry or business-hook replay.";
        }
        finally
        {
            try
            {
                if (fixture.IsPostgreSql) await CleanupAsync(fixture, markerName, markerTable);
            }
            finally
            {
                await CleanupAsync(fixture, name, table);
            }
        }
    }

    private static async Task PostgreSqlSerializationAsync(RuntimeFixture fixture, string table, string markerTable, CancellationToken token)
    {
        // Both original Contexts end before the recovery read or whole-operation retry starts.
        await using (var stale = fixture.Core())
        await using (var winner = fixture.Core())
        {
            await stale.Database.OpenConnectionAsync(token);
            await winner.Database.OpenConnectionAsync(token);
            await RequireDistinctSessionsAsync(fixture, stale, winner, token);
            await using var staleTransaction = await stale.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await using var winnerTransaction = await winner.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var winnerCommitted = false;
            try
            {
                ProbeAssert.Require(await ReadAsync(fixture, stale, table, 1, token) == 0
                        && await ReadAsync(fixture, winner, table, 1, token) == 0,
                    "Both Serializable transactions must establish the original row snapshot.");
                await IncrementAsync(fixture, stale, markerTable, 2, 1, token);
                await IncrementAsync(fixture, winner, table, 1, 1, token);
                await winnerTransaction.CommitAsync(token);
                winnerCommitted = true;

                PostgresException? rejected = null;
                try
                {
                    await IncrementAsync(fixture, stale, table, 1, 1, token);
                }
                catch (PostgresException exception) when (exception.SqlState == "40001")
                {
                    rejected = exception;
                }

                ProbeAssert.Require(rejected is not null, "The stale Serializable update must produce native40001.");
                var failure = DatabaseFailureClassifier.Classify(rejected!);
                ProbeAssert.Require(failure.Kind == DatabaseFailureKind.SerializationFailure
                        && failure.CanRetryTransaction && failure.SqlState == "40001"
                        && failure.DatabaseErrorCode is null && failure.ConstraintName is null,
                    "Native40001 must retain its SQLSTATE and classify as whole-transaction retryable.");
                // No query or retry is issued inside the failed PostgreSQL transaction.
            }
            finally
            {
                try
                {
                    await staleTransaction.RollbackAsync(CancellationToken.None);
                }
                finally
                {
                    if (!winnerCommitted) await winnerTransaction.RollbackAsync(CancellationToken.None);
                }
            }
        }

        await RequireValuesAsync(fixture, table, markerTable, 1, 0, token);
        await RetryWholeOperationAsync(fixture, table, markerTable, 1, 0, token);
        await RequireValuesAsync(fixture, table, markerTable, 2, 1, token);
    }

    private static async Task SqlServerDeadlockAsync(RuntimeFixture fixture, string table, CancellationToken token)
    {
        await using (var first = fixture.Core())
        await using (var second = fixture.Core())
        {
            await first.Database.OpenConnectionAsync(token);
            await second.Database.OpenConnectionAsync(token);
            await RequireDistinctSessionsAsync(fixture, first, second, token);
            await using var firstTransaction = await first.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            await using var secondTransaction = await second.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            await IncrementAsync(fixture, first, table, 1, 1, token);
            await IncrementAsync(fixture, second, table, 2, 1, token);

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var bothReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = 0;
            async Task<bool> CrossAndFinishAsync(DbContext context, IDbContextTransaction transaction, int otherRow)
            {
                var committed = false;
                var deadlockVictim = false;
                try
                {
                    if (Interlocked.Increment(ref ready) == 2) bothReady.SetResult();
                    await start.Task.WaitAsync(token);
                    await IncrementAsync(fixture, context, table, otherRow, 1, token);
                    await transaction.CommitAsync(token);
                    committed = true;
                    return true;
                }
                catch (SqlException exception) when (exception.Errors.Cast<SqlError>().Any(error => error.Number == 1205))
                {
                    deadlockVictim = true;
                    var failure = DatabaseFailureClassifier.Classify(exception);
                    ProbeAssert.Require(failure.Kind == DatabaseFailureKind.Deadlock && failure.CanRetryTransaction
                            && failure.DatabaseErrorCode == 1205 && failure.SqlState is null && failure.ConstraintName is null,
                        "The original native1205 error collection must classify as whole-transaction retryable.");
                    return false;
                }
                finally
                {
                    if (!committed)
                    {
                        try
                        {
                            await transaction.RollbackAsync(CancellationToken.None);
                        }
                        catch (InvalidOperationException) when (deadlockVictim && transaction.GetDbTransaction().Connection is null)
                        {
                            // Native1205 can already complete and detach the whole SqlTransaction.
                            // No statement is sent to a completed transaction; its Context still gets disposed.
                        }
                    }
                }
            }

            var firstOutcome = CrossAndFinishAsync(first, firstTransaction, 2);
            var secondOutcome = CrossAndFinishAsync(second, secondTransaction, 1);
            await bothReady.Task.WaitAsync(token);
            start.SetResult();
            var outcomes = await Task.WhenAll(firstOutcome, secondOutcome);
            ProbeAssert.Require(outcomes.Count(committed => committed) == 1,
                "Exactly one deadlock transaction must commit and exactly one native1205 victim must fully roll back.");
        }

        await RequireValuesAsync(fixture, table, table, 1, 1, token);
        await RetryWholeOperationAsync(fixture, table, table, 1, 1, token);
        await RequireValuesAsync(fixture, table, table, 2, 2, token);
    }

    private static async Task RetryWholeOperationAsync(RuntimeFixture fixture, string table, string secondTable, int firstExpected, int secondExpected, CancellationToken token)
    {
        // This is a new Context and a new full protocol, not a statement retry or tracker reuse.
        await using var fresh = fixture.Core();
        await fresh.Database.OpenConnectionAsync(token);
        await using var transaction = await fresh.Database.BeginTransactionAsync(
            fixture.IsPostgreSql ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted, token);
        var committed = false;
        try
        {
            ProbeAssert.Require(await ReadAsync(fixture, fresh, table, 1, token) == firstExpected
                    && await ReadAsync(fixture, fresh, secondTable, 2, token) == secondExpected,
                "The fresh transaction must reread only committed state before repeating the entire operation.");
            await IncrementAsync(fixture, fresh, table, 1, 1, token);
            await IncrementAsync(fixture, fresh, secondTable, 2, 1, token);
            await transaction.CommitAsync(token);
            committed = true;
        }
        finally
        {
            if (!committed) await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    private static async Task RequireValuesAsync(RuntimeFixture fixture, string table, string secondTable, int firstExpected, int secondExpected, CancellationToken token)
    {
        await using var observer = fixture.Connection();
        await observer.OpenAsync(token);
        var first = await observer.QuerySingleAsync<int>(new CommandDefinition(
            $"SELECT {fixture.Column("Value")} FROM {table} WHERE {fixture.Column("Id")}=1",
            commandTimeout: CommandTimeoutSeconds, cancellationToken: token));
        var second = await observer.QuerySingleAsync<int>(new CommandDefinition(
            $"SELECT {fixture.Column("Value")} FROM {secondTable} WHERE {fixture.Column("Id")}=2",
            commandTimeout: CommandTimeoutSeconds, cancellationToken: token));
        ProbeAssert.Require(first == firstExpected && second == secondExpected,
            "An independent connection must observe the exact whole-transaction rollback or committed retry result.");
    }

    private static Task<int> ReadAsync(RuntimeFixture fixture, DbContext context, string table, int id, CancellationToken token) =>
        context.Database.GetDbConnection().QuerySingleAsync<int>(new CommandDefinition(
            $"SELECT {fixture.Column("Value")} FROM {table} WHERE {fixture.Column("Id")}=@id",
            new { id }, context.Database.CurrentTransaction!.GetDbTransaction(),
            commandTimeout: CommandTimeoutSeconds, cancellationToken: token));

    private static async Task IncrementAsync(RuntimeFixture fixture, DbContext context, string table, int id, int amount, CancellationToken token)
    {
        var rows = await ExecuteAsync(context.Database.GetDbConnection(), context.Database.CurrentTransaction!.GetDbTransaction(),
            $"UPDATE {table}{(fixture.IsPostgreSql ? "" : " WITH (ROWLOCK)")} SET {fixture.Column("Value")}={fixture.Column("Value")}+@amount WHERE {fixture.Column("Id")}=@id",
            new { id, amount }, token);
        ProbeAssert.Require(rows == 1, "Each protocol update must affect exactly its own fixture row.");
    }

    private static async Task RequireDistinctSessionsAsync(RuntimeFixture fixture, DbContext first, DbContext second, CancellationToken token)
    {
        var query = fixture.IsPostgreSql ? "SELECT pg_catalog.pg_backend_pid()" : "SELECT CONVERT(int,@@SPID)";
        var firstId = await first.Database.GetDbConnection().QuerySingleAsync<int>(new CommandDefinition(query,
            commandTimeout: CommandTimeoutSeconds, cancellationToken: token));
        var secondId = await second.Database.GetDbConnection().QuerySingleAsync<int>(new CommandDefinition(query,
            commandTimeout: CommandTimeoutSeconds, cancellationToken: token));
        ProbeAssert.Require(firstId != secondId, "The competing transactions require two physical native sessions.");
    }

    private static Task<int> ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql, object? parameters, CancellationToken token) =>
        connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction,
            commandTimeout: CommandTimeoutSeconds, cancellationToken: token));

    private static async Task CleanupAsync(RuntimeFixture fixture, string name, string table)
    {
        await using var cleanup = fixture.Connection();
        await cleanup.OpenAsync();
        var query = fixture.IsPostgreSql
            ? "SELECT EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relname=@name AND c.relkind IN ('r','p'))"
            : "SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo') AND name=@name) THEN 1 ELSE 0 END AS bit)";
        if (await cleanup.QuerySingleAsync<bool>(query, new { name }, commandTimeout: CommandTimeoutSeconds))
            await ExecuteAsync(cleanup, null, $"DROP TABLE {table}", null, CancellationToken.None);
        ProbeAssert.Require(!await cleanup.QuerySingleAsync<bool>(query, new { name }, commandTimeout: CommandTimeoutSeconds),
            "The exact generated transaction fixture table must be absent after cleanup.");
    }
}
