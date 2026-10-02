using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using Dapper;

namespace CP6.DatabaseCompatibility.Probe;

internal sealed record DirectoryCursor(Guid TenantId, long Generation, long Position);
internal sealed record DirectoryPage(DirectoryCursor Cursor, IReadOnlyList<DirectoryRecord> Rows);
internal sealed class SnapshotChangedException : Exception { }

internal static class IdentityExperiments
{
    public static async Task RunAsync(ProbeDatabase db, ProbeReport report)
    {
        await report.CheckAsync("Identity.StablePagingTenantBindingAndCrossPageChange", () => PagingAsync(db));
        await report.CheckAsync("Identity.SameTransactionGenerationRollbackAndCommit", () => RollbackAsync(db));
        await report.CheckAsync("Identity.TombstoneDuplicateAndOutOfOrderEvent", () => TombstoneAsync(db));
        await report.CheckAsync("Identity.RealProcessRestartAndRevocation", () => RestartAsync(db));
    }

    internal static async Task<DirectoryPage> ReadPageAsync(ProbeDatabase db, DbConnection connection, Guid tenant, DirectoryCursor? cursor, int size)
    {
        if (cursor is not null && cursor.TenantId != tenant) throw new ProbeAssertionException("Cursor tenant binding mismatch.");
        Expect.True(size is > 0 and <= 100, "Probe page size is out of bounds.");
        // Each page observes its generation and rows within one database snapshot/locking boundary.
        await using var transaction = await connection.BeginTransactionAsync(db.Provider == ProbeProvider.PostgreSql ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable);
        var generation = await GenerationAsync(db, connection, tenant, transaction);
        if (cursor is not null && generation != cursor.Generation) throw new SnapshotChangedException();
        var where = $"WHERE {db.Column("TenantId")}=@Tenant AND {db.Column("Position")}>@Position AND {db.Column("IsDeleted")}={db.Bool(false)} ORDER BY {db.Column("Position")}";
        var sql = db.Provider == ProbeProvider.PostgreSql
            ? $"SELECT * FROM {db.Table("DirectoryRecords")} {where} LIMIT @Size"
            : $"SELECT TOP (@Size) * FROM {db.Table("DirectoryRecords")} {where}";
        var rows = (await connection.QueryAsync<DirectoryRecord>(sql, new { Tenant = tenant, Position = cursor?.Position ?? 0, Size = size }, transaction)).ToArray();
        await transaction.CommitAsync();
        return new(new(tenant, generation, rows.Length == 0 ? cursor?.Position ?? 0 : rows[^1].Position), rows);
    }

    internal static Task<long> GenerationAsync(ProbeDatabase db, DbConnection connection, Guid tenant, DbTransaction? transaction = null) =>
        connection.QuerySingleAsync<long>($"SELECT {db.Column("Generation")} FROM {db.Table("TenantGenerations")} WHERE {db.Column("TenantId")}=@Tenant", new { Tenant = tenant }, transaction);

    private static async Task<Guid> SeedTenantAsync(ProbeDatabase db, int rows = 5)
    {
        var tenant = Guid.NewGuid();
        await db.Connection.ExecuteAsync($"INSERT INTO {db.Table("TenantGenerations")} ({db.Column("TenantId")}, {db.Column("Generation")}) VALUES (@Tenant,0)", new { Tenant = tenant });
        for (var position = 1; position <= rows; position++)
            await db.Connection.ExecuteAsync($"INSERT INTO {db.Table("DirectoryRecords")} ({db.Column("TenantId")}, {db.Column("Position")}, {db.Column("EventVersion")}, {db.Column("Value")}, {db.Column("IsDeleted")}) VALUES (@Tenant,@Position,1,@Value,{db.Bool(false)})",
                new { Tenant = tenant, Position = position, Value = $"permission-{position}" });
        return tenant;
    }

    private static async Task<string> PagingAsync(ProbeDatabase db)
    {
        var tenant = await SeedTenantAsync(db);
        var otherTenant = await SeedTenantAsync(db);
        var first = await ReadPageAsync(db, db.Connection, tenant, null, 2);
        var second = await ReadPageAsync(db, db.Connection, tenant, first.Cursor, 2);
        var third = await ReadPageAsync(db, db.Connection, tenant, second.Cursor, 2);
        var positions = first.Rows.Concat(second.Rows).Concat(third.Rows).Select(row => row.Position).ToArray();
        Expect.True(positions.SequenceEqual(new long[] { 1, 2, 3, 4, 5 }), "Stable paging must include each permission exactly once.");
        Expect.True(first.Rows.Concat(second.Rows).Concat(third.Rows).All(row => row.TenantId == tenant), "Directory page must remain tenant-scoped.");
        var rejectedTenant = false;
        try { await ReadPageAsync(db, db.Connection, otherTenant, first.Cursor, 2); }
        catch (ProbeAssertionException) { rejectedTenant = true; }
        Expect.True(rejectedTenant, "A cursor cannot be reused for another tenant.");
        await using var writer = db.NewConnection();
        await writer.OpenAsync();
        await writer.ExecuteAsync($"UPDATE {db.Table("DirectoryRecords")} SET {db.Column("Value")}=@Value WHERE {db.Column("TenantId")}=@Tenant AND {db.Column("Position")}=4", new { Tenant = tenant, Value = "concurrent changed permission" });
        var changed = false;
        try { await ReadPageAsync(db, db.Connection, tenant, first.Cursor, 2); }
        catch (SnapshotChangedException) { changed = true; }
        Expect.True(changed, "Cross-page committed change must reject continuation before returning changed rows.");
        var unaffected = await ReadPageAsync(db, db.Connection, otherTenant, null, 2);
        Expect.True(unaffected.Rows.Count == 2, "Another tenant must retain independent generation and rows.");
        return "Tenant/generation/stable-position cursor returned 1..5 once; cross-tenant reuse rejected; another connection's committed update invalidated old page continuation.";
    }

    private static async Task<string> RollbackAsync(ProbeDatabase db)
    {
        var tenant = await SeedTenantAsync(db);
        var first = await ReadPageAsync(db, db.Connection, tenant, null, 2);
        await using var writer = db.NewConnection();
        await writer.OpenAsync();
        var sql = $"UPDATE {db.Table("DirectoryRecords")} SET {db.Column("IsDeleted")}={db.Bool(true)}, {db.Column("EventVersion")}=2 WHERE {db.Column("TenantId")}=@Tenant AND {db.Column("Position")}=3";
        await using (var transaction = await writer.BeginTransactionAsync())
        {
            await writer.ExecuteAsync(sql, new { Tenant = tenant }, transaction);
            var uncommitted = await GenerationAsync(db, writer, tenant, transaction);
            Expect.True(uncommitted > first.Cursor.Generation, "Directory write must advance generation inside the same actual transaction.");
            await transaction.RollbackAsync();
        }
        Expect.True(await GenerationAsync(db, db.Connection, tenant) == first.Cursor.Generation, "Directory rollback must roll back generation, unlike a bare sequence.");
        var continued = await ReadPageAsync(db, db.Connection, tenant, first.Cursor, 2);
        Expect.True(continued.Rows.Select(row => row.Position).SequenceEqual(new long[] { 3, 4 }), "Rolled-back tombstone must not omit permission from stable next page.");
        await using (var transaction = await writer.BeginTransactionAsync())
        {
            await writer.ExecuteAsync(sql, new { Tenant = tenant }, transaction);
            await transaction.CommitAsync();
        }
        var invalidated = false;
        try { await ReadPageAsync(db, db.Connection, tenant, first.Cursor, 2); }
        catch (SnapshotChangedException) { invalidated = true; }
        Expect.True(invalidated, "Committed mutation and generation must become visible together.");
        return "Real transaction rollback preserved directory row and generation; commit exposed tombstone plus increased generation together and rejected old continuation.";
    }

    private static async Task<string> TombstoneAsync(ProbeDatabase db)
    {
        var tenant = await SeedTenantAsync(db);
        var first = await ReadPageAsync(db, db.Connection, tenant, null, 2);
        var tombstone = $"UPDATE {db.Table("DirectoryRecords")} SET {db.Column("IsDeleted")}={db.Bool(true)}, {db.Column("EventVersion")}=@Version WHERE {db.Column("TenantId")}=@Tenant AND {db.Column("Position")}=3 AND {db.Column("EventVersion")}<@Version";
        Expect.True(await db.Connection.ExecuteAsync(tombstone, new { Tenant = tenant, Version = 6 }) == 1, "New revocation event must persist a tombstone.");
        var generation = await GenerationAsync(db, db.Connection, tenant);
        var resurrection = $"UPDATE {db.Table("DirectoryRecords")} SET {db.Column("IsDeleted")}={db.Bool(false)}, {db.Column("EventVersion")}=@Version WHERE {db.Column("TenantId")}=@Tenant AND {db.Column("Position")}=3 AND {db.Column("EventVersion")}<@Version";
        Expect.True(await db.Connection.ExecuteAsync(resurrection, new { Tenant = tenant, Version = 5 }) == 0, "Out-of-order grant cannot resurrect a newer tombstone.");
        Expect.True(await db.Connection.ExecuteAsync(tombstone, new { Tenant = tenant, Version = 6 }) == 0, "Duplicate revocation must affect zero rows.");
        Expect.True(await GenerationAsync(db, db.Connection, tenant) == generation, "Rejected/duplicate events must not spuriously advance generation.");
        var changed = false;
        try { await ReadPageAsync(db, db.Connection, tenant, first.Cursor, 2); }
        catch (SnapshotChangedException) { changed = true; }
        Expect.True(changed, "Old cursor must not return a page after revoked permission changed.");
        var restarted = await ReadPageAsync(db, db.Connection, tenant, null, 10);
        Expect.True(restarted.Rows.All(row => row.Position != 3), "Restarted page cannot return revoked permission.");
        await db.Connection.ExecuteAsync($"DELETE FROM {db.Table("DirectoryRecords")} WHERE {db.Column("TenantId")}=@Tenant AND {db.Column("Position")}=5", new { Tenant = tenant });
        Expect.True(await GenerationAsync(db, db.Connection, tenant) > restarted.Cursor.Generation, "Physical deletion must also advance the tenant boundary.");
        return "Version-6 tombstone rejected version-5 grant and duplicate event; old cursor rejected, restarted page excluded revocation, physical DELETE invalidated boundary too. Tombstone purge/replay retention remains a later policy decision.";
    }

    private static async Task<string> RestartAsync(ProbeDatabase db)
    {
        var tenant = await SeedTenantAsync(db);
        var first = await ReadPageAsync(db, db.Connection, tenant, null, 2);
        await ChildAsync(db, first.Cursor, "stable");
        await db.Connection.ExecuteAsync($"UPDATE {db.Table("DirectoryRecords")} SET {db.Column("IsDeleted")}={db.Bool(true)}, {db.Column("EventVersion")}=2 WHERE {db.Column("TenantId")}=@Tenant AND {db.Column("Position")}=3", new { Tenant = tenant });
        await ChildAsync(db, first.Cursor, "reject");
        return "Two newly launched processes reused serialized tenant/generation/position: unchanged boundary resumed, committed revocation rejected. No xmin, wall clock, opaque token ordering or process counter was used.";
    }

    private static async Task ChildAsync(ProbeDatabase db, DirectoryCursor cursor, string mode)
    {
        var executable = Environment.ProcessPath ?? throw new ProbeAssertionException("Current executable path is unavailable.");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var argument in new[] { "--restart-generation", "--provider", db.Provider.ToString(), "--schema", db.Schema, "--owner", db.OwnershipId.ToString(), "--tenant", cursor.TenantId.ToString(), "--generation", cursor.Generation.ToString(), "--position", cursor.Position.ToString(), "--restart-mode", mode })
            info.ArgumentList.Add(argument);
        if (mode == "stable")
        {
            info.ArgumentList.Add("--expected-positions");
            info.ArgumentList.Add("3,4");
        }
        // The selected connection was supplied by the task owner through the parent's environment.
        var variable = db.ConnectionEnvironment;
        info.ArgumentList.Add("--connection-env");
        info.ArgumentList.Add(variable);
        using var process = Process.Start(info) ?? throw new ProbeAssertionException("Restart child could not be launched.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new ProbeAssertionException("Restart child exceeded bounded 30-second timeout.");
        }
        Expect.True(process.ExitCode == 0 && (await output).Contains("Passed: Identity.RealProcessRestart", StringComparison.Ordinal), "Restart child must assert the persisted cursor result and exit successfully.");
        Expect.True(string.IsNullOrEmpty(await errors), "Restart child produced unexpected stderr.");
    }
}
