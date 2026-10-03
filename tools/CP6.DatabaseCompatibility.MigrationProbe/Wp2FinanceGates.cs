using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Npgsql;

public static class Wp2FinanceGates
{
    private const string OriginalMemo = "WP2-race-original";
    private const string MutatedMemo = "WP2-race-mutated";

    // The caller verifies the task database owner before invoking this gate.
    public static async Task<string> VerifyRaceAsync(DbConnection connection, bool pg, string connectionString)
    {
        Require(connection.State == ConnectionState.Open, "Finance race requires the already verified open task connection.");
        Require(pg ? connection is NpgsqlConnection : connection is SqlConnection, "Finance race provider must match the verified connection.");
        Require(Regex.IsMatch(connection.Database, "\\ACP6Compat_WP2_[0-9]{8}_[a-f0-9]{8}\\z"), "Finance race requires a dedicated WP2 database.");
        var failures = new List<string>();
        foreach (var operation in new[] { "UPDATE", "DELETE" })
        {
            try { await VerifyOneRaceAsync(connection, pg, connectionString, operation); }
            catch (MigrationAssertionException exception) { failures.Add(exception.Message); }
        }
        Require(failures.Count == 0, string.Join("; ", failures));
        return "UPDATE and DELETE each waited on the actual posting session; after Status=2 committed, E-FIN-160 rejected both mutations and preserved both original lines. Exact fixtures removed and absence verified.";
    }

    private static async Task VerifyOneRaceAsync(DbConnection observer, bool pg, string connectionString, string operation)
    {
        var tenant = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var no = "WP2FIN-" + Guid.NewGuid().ToString("N")[..22];
        var args = new { Tenant = tenant, Entry = entryId, Line = lineId, No = no, Original = OriginalMemo, Mutated = MutatedMemo };
        var entry = Table(pg, "Fin_JournalEntry");
        var line = Table(pg, "Fin_JournalLine");
        string Q(string name) => Quote(pg, name);
        var entryWhere = $"{Q("Id")}=@Entry AND {Q("TenantId")}=@Tenant AND {Q("No")}=@No";
        var lineWhere = $"{Q("Id")}=@Line AND {Q("EntryId")}=@Entry AND {Q("TenantId")}=@Tenant";
        var fixtureAttempted = false;
        try
        {
            Require(await observer.QuerySingleAsync<int>(Command($"SELECT COUNT(*) FROM {entry} WHERE {Q("Id")}=@Entry OR ({Q("TenantId")}=@Tenant AND {Q("No")}=@No)", args)) == 0,
                $"{operation}: entry fixture must be unused.");
            Require(await observer.QuerySingleAsync<int>(Command($"SELECT COUNT(*) FROM {line} WHERE {Q("Id")}=@Line", args)) == 0,
                $"{operation}: line fixture must be unused.");
            fixtureAttempted = true;
            await using (var setup = await observer.BeginTransactionAsync())
            {
                var now = DateTime.SpecifyKind(new DateTime(2026, 10, 2, 12, 0, 0), DateTimeKind.Unspecified);
                Require(await observer.ExecuteAsync(Command($"""
                    INSERT INTO {entry} ({Q("Id")},{Q("TenantId")},{Q("No")},{Q("VoucherDate")},{Q("PeriodId")},
                        {Q("Source")},{Q("Status")},{Q("Description")},{Q("MakerId")},{Q("MakerAt")},{Q("AutoPosted")},{Q("CreateDate")})
                    VALUES (@Entry,@Tenant,@No,@Now,@Period,0,0,@Description,@Maker,@Now,@AutoPosted,@Now)
                    """, new { Entry = entryId, Tenant = tenant, No = no, Now = now, Period = Guid.NewGuid(), Description = "WP2 financial race fixture", Maker = "WP2-probe", AutoPosted = false }, setup)) == 1,
                    $"{operation}: setup must insert one entry.");
                Require(await observer.ExecuteAsync(Command($"""
                    INSERT INTO {line} ({Q("Id")},{Q("EntryId")},{Q("TenantId")},{Q("LineNo")},{Q("AccountId")},
                        {Q("Debit")},{Q("Credit")},{Q("Memo")},{Q("CreateDate")})
                    VALUES (@Line,@Entry,@Tenant,1,@Account,1,0,@Original,@Now)
                    """, new { Line = lineId, Entry = entryId, Tenant = tenant, Account = Guid.NewGuid(), Original = OriginalMemo, Now = now }, setup)) == 1,
                    $"{operation}: setup must insert one line.");
                await setup.CommitAsync();
            }

            await ExecuteRaceAsync(observer, pg, connectionString, operation, entry, line, entryWhere, lineWhere, args);
        }
        finally
        {
            // ExecuteRaceAsync settles and disposes both competing connections first.
            // Reset only our exact entry so the genuine posted-line guard permits cleanup.
            if (fixtureAttempted)
            {
                await using var cleanup = await observer.BeginTransactionAsync();
                await observer.ExecuteAsync(Command($"UPDATE {entry} SET {Q("Status")}=0 WHERE {entryWhere}", args, cleanup));
                await observer.ExecuteAsync(Command($"DELETE FROM {line} WHERE {lineWhere}", args, cleanup));
                await observer.ExecuteAsync(Command($"DELETE FROM {entry} WHERE {entryWhere}", args, cleanup));
                await cleanup.CommitAsync();
                Require(await observer.QuerySingleAsync<int>(Command($"SELECT COUNT(*) FROM {entry} WHERE {entryWhere}", args)) == 0
                    && await observer.QuerySingleAsync<int>(Command($"SELECT COUNT(*) FROM {line} WHERE {lineWhere}", args)) == 0,
                    $"{operation}: exact finance fixtures must be absent after cleanup.");
            }
        }
    }

    private static async Task ExecuteRaceAsync(DbConnection observer, bool pg, string connectionString, string operation,
        string entry, string line, string entryWhere, string lineWhere, object args)
    {
        string Q(string name) => Quote(pg, name);
        await using DbConnection poster = pg ? new NpgsqlConnection(connectionString) : new SqlConnection(connectionString);
        await using DbConnection mutator = pg ? new NpgsqlConnection(connectionString) : new SqlConnection(connectionString);
        await poster.OpenAsync();
        await mutator.OpenAsync();
        Require(poster.Database == observer.Database && mutator.Database == observer.Database
            && poster.DataSource == observer.DataSource && mutator.DataSource == observer.DataSource,
            $"{operation}: both competing connections must target the verified database and server.");
        var sessionSql = pg ? "SELECT pg_backend_pid()" : "SELECT @@SPID";
        var posterSession = await poster.QuerySingleAsync<int>(Command(sessionSql));
        var mutatorSession = await mutator.QuerySingleAsync<int>(Command(sessionSql));
        Require(posterSession != mutatorSession, $"{operation}: race requires two independent database sessions.");
        await using var posting = await poster.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        using var stopMutation = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<MutationResult>? mutation = null;
        var postingCommitted = false;
        try
        {
            Require(await poster.ExecuteAsync(Command($"UPDATE {entry} SET {Q("Status")}=2 WHERE {entryWhere}", args, posting)) == 1,
                $"{operation}: posting must update exactly one fixture without committing yet.");
            mutation = MutateAsync(mutator, operation == "UPDATE"
                ? $"UPDATE {line} SET {Q("Memo")}=@Mutated WHERE {lineWhere}"
                : $"DELETE FROM {line} WHERE {lineWhere}", args, started, stopMutation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var blocked = await ObserveBlockingAsync(observer, pg, posterSession, mutatorSession, mutation);
            await posting.CommitAsync();
            postingCommitted = true;
            var result = await mutation.WaitAsync(TimeSpan.FromSeconds(10));
            var finalStatus = await observer.QuerySingleAsync<int>(Command($"SELECT {Q("Status")} FROM {entry} WHERE {entryWhere}", args));
            Require(finalStatus == 2, $"{operation}: posting must finish with Status=2.");
            if (result.Committed)
            {
                var changed = await observer.QuerySingleAsync<int>(Command(operation == "UPDATE"
                    ? $"SELECT COUNT(*) FROM {line} WHERE {lineWhere} AND {Q("Memo")}=@Mutated"
                    : $"SELECT COUNT(*) FROM {line} WHERE {lineWhere}", args));
                Require(false, $"{operation}: line mutation committed while posting was uncommitted (blocked={blocked}); after posting committed Status=2, "
                    + (operation == "UPDATE" ? $"mutated line count={changed}." : $"remaining line count={changed}."));
            }
            Require(blocked, $"{operation}: the database never proved the mutator was blocked by the uncommitted posting session; error={ErrorKind(result.Error)}.");
            Require(IsFinanceGuard(result.Error, pg), $"{operation}: after posting commit the mutator must be rejected by E-FIN-160, not {ErrorKind(result.Error)}.");
            Require(await observer.QuerySingleAsync<int>(Command($"SELECT COUNT(*) FROM {line} WHERE {lineWhere} AND {Q("Memo")}=@Original", args)) == 1,
                $"{operation}: the guard must preserve the exact original line after posting commit.");
        }
        finally
        {
            // Release the posting lock before draining/cancelling a pending mutator.
            if (!postingCommitted) await RollbackForDisposalAsync(posting);
            if (mutation is { IsCompleted: false })
            {
                stopMutation.Cancel();
                try { await mutation.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (TimeoutException)
                {
                    await mutator.CloseAsync();
                    try { await mutation.WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (TimeoutException) { throw new MigrationAssertionException($"{operation}: mutator did not settle after cancellation and connection close; race cannot pass."); }
                }
            }
        }
    }

    private static async Task<MutationResult> MutateAsync(DbConnection connection, string sql, object args,
        TaskCompletionSource<bool> started, CancellationToken cancellation)
    {
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellation);
        try
        {
            started.TrySetResult(true);
            var affected = await connection.ExecuteAsync(new CommandDefinition(sql, args, transaction, commandTimeout: 20, cancellationToken: cancellation));
            Require(affected == 1, "Finance race mutation must affect exactly one fixture line.");
            await transaction.CommitAsync(cancellation);
            return new MutationResult(true, null);
        }
        catch (Exception exception)
        {
            await RollbackForDisposalAsync(transaction);
            return new MutationResult(false, exception);
        }
    }

    private static async Task<bool> ObserveBlockingAsync(DbConnection observer, bool pg, int poster, int mutator, Task<MutationResult> mutation)
    {
        var sql = pg
            ? "SELECT CASE WHEN EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@Mutator AND wait_event_type='Lock' AND @Poster=ANY(pg_blocking_pids(pid))) THEN 1 ELSE 0 END"
            : "SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.dm_exec_requests WHERE session_id=@Mutator AND blocking_session_id=@Poster AND wait_type LIKE 'LCK_M%') THEN 1 ELSE 0 END";
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(8))
        {
            if (await observer.QuerySingleAsync<int>(Command(sql, new { Poster = poster, Mutator = mutator })) == 1) return true;
            if (mutation.IsCompleted) return false;
            await Task.Delay(50);
        }
        throw new MigrationAssertionException("Finance race never established database-observed blocking within eight seconds; timeout is a failure.");
    }

    private static bool IsFinanceGuard(Exception? exception, bool pg) => pg
        ? exception is PostgresException { SqlState: "P0001", ConstraintName: "trg_FinJournalLine_NoMutate" } postgres && postgres.MessageText == "E-FIN-160"
        : exception is SqlException sql && sql.Errors.Cast<SqlError>().Any(error => error.Number == 50010 && error.Message.Contains("E-FIN-160", StringComparison.Ordinal));

    private static string ErrorKind(Exception? exception) => exception switch
    {
        null => "no database rejection",
        PostgresException postgres => $"PostgreSQL SQLSTATE {postgres.SqlState}",
        SqlException sql => $"SQL Server error {sql.Number}",
        _ => exception.GetType().Name
    };

    private static async Task RollbackForDisposalAsync(DbTransaction transaction)
    {
        // SQL Server's guard rolls back the transaction itself. This connection is
        // disposed next; final persisted state and independent cleanup remain gates.
        try { await transaction.RollbackAsync(); }
        catch (InvalidOperationException) { }
        catch (DbException) { }
    }

    private static CommandDefinition Command(string sql, object? args = null, DbTransaction? transaction = null)
        => new(sql, args, transaction, commandTimeout: 10);
    private static string Table(bool pg, string name) => $"{Quote(pg, pg ? "public" : "dbo")}.{Quote(pg, name)}";
    private static string Quote(bool pg, string name) => pg ? $"\"{name}\"" : $"[{name}]";
    private static void Require(bool condition, string detail) { if (!condition) throw new MigrationAssertionException(detail); }
    private sealed record MutationResult(bool Committed, Exception? Error);
}
