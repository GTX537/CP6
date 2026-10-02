using System.Data.Common;
using System.Security.Cryptography;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace CP6.DatabaseCompatibility.Probe;

internal static class TokenExperiments
{
    public static Task RunNegativeControlAsync(ProbeDatabase db, ProbeReport report) => report.CheckAsync("NegativeControl.MissingStaleWritePredicateMustFail", async () =>
    {
        var (id, old) = await SeedAsync(db);
        await ConditionalWriteAsync(db, db.Connection, id, old, "winner");
        var accepted = await db.Connection.ExecuteAsync($"UPDATE {db.Table("DatabaseTokenRows")} SET {db.Column("Value")}=@Value WHERE {db.Column("Id")}=@Id", new { Id = id, Value = "negative control stale overwrite" });
        Expect.True(accepted == 0, "Expected negative-control failure: a raw writer that omits the stale token predicate accepts an old caller and loses the winner's update.");
        return "This result must never pass while the negative fixture omits the stale predicate.";
    });

    public static async Task RunAsync(ProbeDatabase db, ProbeReport report)
    {
        await report.CheckAsync("DatabaseToken.InsertAndEfUpdateReadback", () => InsertAndUpdateAsync(db));
        await report.CheckAsync("DatabaseToken.StaleEfUpdateAndDelete", () => StaleEfAsync(db));
        await report.CheckAsync("DatabaseToken.DapperConditionalAndRawNoOp", () => DapperAndRawAsync(db));
        await report.CheckAsync("DatabaseToken.ExecuteUpdateAndExplicitStalePredicate", () => ExecuteUpdateAsync(db));
        await report.CheckAsync("DatabaseToken.ConcurrentConditionalLeaseClaim", () => LeaseRaceAsync(db));
        await report.CheckAsync("DatabaseToken.TriggerAndMaintenanceWriter", () => TriggerWriteAsync(db));
        await report.CheckAsync("DatabaseToken.ReplayAuditAndRollback", () => AuditAndRollbackAsync(db));
        if (db.Provider == ProbeProvider.PostgreSql)
        {
            await report.CheckAsync("ApplicationToken.CoveredEfAndDapper", () => ApplicationCoveredAsync(db));
            var checksBefore = report.Checks.Count;
            await report.CheckAsync("ApplicationToken.DemonstrateRawAndExecuteUpdateGaps", () => ApplicationGapsAsync(db));
            if (report.Checks[checksBefore].Status == "Passed")
                report.Add("ApplicationToken.Candidate", "Rejected", "SaveChanges-only generation permits stale overwrite after raw SQL and ExecuteUpdate; a complete explicit writer audit/rewrite would be required.");
        }
        else report.Add("ApplicationToken.Candidate", "NotApplicable", "SQL Server retains native rowversion; the application-managed alternative is a PostgreSQL experiment.");
    }

    private static DatabaseTokenContext Context(ProbeDatabase db, DbConnection connection) => new(connection, db.Provider, db.Schema);

    private static async Task<(Guid Id, byte[] Token)> SeedAsync(ProbeDatabase db, string value = "seed")
    {
        await using var context = Context(db, db.Connection);
        var row = new TokenRow { Id = Guid.NewGuid(), Value = value };
        context.Rows.Add(row);
        await context.SaveChangesAsync();
        Expect.Token(row.RowVersion);
        return (row.Id, row.RowVersion.ToArray());
    }

    private static async Task<string> InsertAndUpdateAsync(ProbeDatabase db)
    {
        var (id, inserted) = await SeedAsync(db);
        Expect.True(inserted.SequenceEqual(await db.ReadTokenAsync("DatabaseTokenRows", id)), "EF insert must hydrate the database-generated token.");
        await using var context = Context(db, db.Connection);
        var row = await context.Rows.SingleAsync(row => row.Id == id);
        row.Value = "EF update";
        await context.SaveChangesAsync();
        Expect.Changed(inserted, row.RowVersion);
        Expect.True(row.RowVersion.SequenceEqual(await db.ReadTokenAsync("DatabaseTokenRows", id)), "EF update must hydrate the replacement database-generated token.");
        return "EF INSERT and UPDATE returned 8-byte tokens; exact bytes matched direct database reads and Base64 round-trip.";
    }

    private static async Task<string> StaleEfAsync(ProbeDatabase db)
    {
        var (id, _) = await SeedAsync(db);
        await using var staleConnection = db.NewConnection();
        await using var staleContext = Context(db, staleConnection);
        var stale = await staleContext.Rows.SingleAsync(row => row.Id == id);
        await using (var winner = Context(db, db.Connection))
        {
            var live = await winner.Rows.SingleAsync(row => row.Id == id);
            live.Value = "winner";
            await winner.SaveChangesAsync();
        }
        stale.Value = "stale loser";
        var updateRejected = false;
        try { await staleContext.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { updateRejected = true; }
        Expect.True(updateRejected, "Stale EF update must throw DbUpdateConcurrencyException.");
        staleContext.ChangeTracker.Clear();
        var detached = new TokenRow { Id = id, RowVersion = stale.RowVersion.ToArray() };
        staleContext.Attach(detached);
        staleContext.Remove(detached);
        var deleteRejected = false;
        try { await staleContext.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { deleteRejected = true; }
        Expect.True(deleteRejected, "Stale EF delete must throw DbUpdateConcurrencyException.");
        var value = await db.Connection.QuerySingleAsync<string>($"SELECT {db.Column("Value")} FROM {db.Table("DatabaseTokenRows")} WHERE {db.Column("Id")}=@Id", new { Id = id });
        Expect.True(value == "winner", "Stale operations must not lose the winning data.");
        return "Independent contexts read the same version; losing update and delete rejected; winner retained.";
    }

    internal static async Task<byte[]?> ConditionalWriteAsync(ProbeDatabase db, DbConnection connection, Guid id, byte[] old, string value, DbTransaction? transaction = null)
    {
        var prefix = $"UPDATE {db.Table("DatabaseTokenRows")} SET {db.Column("Value")}=@Value";
        var where = $"WHERE {db.Column("Id")}=@Id AND {db.Column("RowVersion")}=@Old";
        var sql = db.Provider == ProbeProvider.PostgreSql
            ? $"{prefix} {where} RETURNING {db.Column("RowVersion")}"
            : $"{prefix} OUTPUT INSERTED.{db.Column("RowVersion")} {where}";
        return await connection.QuerySingleOrDefaultAsync<byte[]>(sql, new { Id = id, Old = old, Value = value }, transaction);
    }

    private static async Task<string> DapperAndRawAsync(ProbeDatabase db)
    {
        var (id, inserted) = await SeedAsync(db);
        var replaced = await ConditionalWriteAsync(db, db.Connection, id, inserted, "Dapper") ?? throw new ProbeAssertionException("Conditional Dapper write unexpectedly affected zero rows.");
        Expect.Changed(inserted, replaced);
        Expect.True(await ConditionalWriteAsync(db, db.Connection, id, inserted, "stale") is null, "Stale conditional Dapper write must return no row.");
        var count = await db.Connection.ExecuteAsync($"UPDATE {db.Table("DatabaseTokenRows")} SET {db.Column("Value")}={db.Column("Value")} WHERE {db.Column("Id")}=@Id", new { Id = id });
        Expect.True(count == 1, "Raw no-op write should affect one row.");
        var refreshed = await db.ReadTokenAsync("DatabaseTokenRows", id);
        Expect.Changed(replaced, refreshed);
        return "Dapper OUTPUT/RETURNING hydrated replacement bytes; stale conditional update returned zero rows; raw same-value UPDATE still replaced token.";
    }

    private static async Task<string> ExecuteUpdateAsync(ProbeDatabase db)
    {
        var (id, inserted) = await SeedAsync(db);
        await using var context = Context(db, db.Connection);
        var affected = await context.Rows.Where(row => row.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "bulk"));
        Expect.True(affected == 1, "ExecuteUpdate must affect one row.");
        var refreshed = await db.ReadTokenAsync("DatabaseTokenRows", id);
        Expect.Changed(inserted, refreshed);
        var stale = await context.Rows.Where(row => row.Id == id && row.RowVersion == inserted)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "stale bulk"));
        Expect.True(stale == 0, "Explicit stale token predicate on ExecuteUpdate must affect zero rows.");
        var live = await context.Rows.Where(row => row.Id == id && row.RowVersion == refreshed)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "conditional bulk"));
        Expect.True(live == 1, "ExecuteUpdate with current token must affect one row.");
        Expect.Changed(refreshed, await db.ReadTokenAsync("DatabaseTokenRows", id));
        return "ExecuteUpdate replaced token; concurrency is explicit for bulk writers: stale predicate=0 and current predicate=1.";
    }

    private static async Task<string> LeaseRaceAsync(ProbeDatabase db)
    {
        var (id, old) = await SeedAsync(db);
        await using var a = db.NewConnection();
        await using var b = db.NewConnection();
        await Task.WhenAll(a.OpenAsync(), b.OpenAsync());
        var sql = $"UPDATE {db.Table("DatabaseTokenRows")} SET {db.Column("LeaseOwner")}=@Owner WHERE {db.Column("Id")}=@Id AND {db.Column("LeaseOwner")} IS NULL AND {db.Column("RowVersion")}=@Old";
        var results = await Task.WhenAll(a.ExecuteAsync(sql, new { Id = id, Old = old, Owner = "worker-a" }), b.ExecuteAsync(sql, new { Id = id, Old = old, Owner = "worker-b" }));
        Expect.True(results.Sum() == 1 && results.Contains(0) && results.Contains(1), "Exactly one independent connection may acquire the same conditional lease.");
        Expect.Changed(old, await db.ReadTokenAsync("DatabaseTokenRows", id));
        var staleRelease = await a.ExecuteAsync($"UPDATE {db.Table("DatabaseTokenRows")} SET {db.Column("LeaseOwner")}=NULL WHERE {db.Column("Id")}=@Id AND {db.Column("RowVersion")}=@Old", new { Id = id, Old = old });
        Expect.True(staleRelease == 0, "Stale lease token cannot release the acquired lease.");
        return "Two actual connections raced from the same token: exactly one acquired lease and old token could not release it.";
    }

    private static async Task<string> TriggerWriteAsync(ProbeDatabase db)
    {
        var (id, old) = await SeedAsync(db);
        await db.Connection.ExecuteAsync($"INSERT INTO {db.Table("TriggerSourceRows")} ({db.Column("Id")}, {db.Column("TargetId")}, {db.Column("Value")}) VALUES (@Id, @Target, @Value)",
            new { Id = Guid.NewGuid(), Target = id, Value = "trigger writer" });
        var generated = await db.ReadTokenAsync("DatabaseTokenRows", id);
        Expect.Changed(old, generated);
        await using var maintenance = db.NewConnection();
        await maintenance.OpenAsync();
        await maintenance.ExecuteAsync($"UPDATE {db.Table("DatabaseTokenRows")} SET {db.Column("Value")}=@Value WHERE {db.Column("Id")}=@Id", new { Id = id, Value = "separate maintenance connection" });
        Expect.Changed(generated, await db.ReadTokenAsync("DatabaseTokenRows", id));
        return "Write through an AFTER trigger and separate raw maintenance connection both changed the database token.";
    }

    private static async Task<string> AuditAndRollbackAsync(ProbeDatabase db)
    {
        var (id, old) = await SeedAsync(db);
        var auditId = Guid.NewGuid();
        await db.Connection.ExecuteAsync($"INSERT INTO {db.Table("AuditRecords")} ({db.Column("Id")}, {db.Column("BusinessId")}, {db.Column("RowVersion")}, {db.Column("Value")}) VALUES (@Id,@Business,@Token,@Value)",
            new { Id = auditId, Business = id, Token = old, Value = Convert.ToBase64String(old) });
        var stored = await db.Connection.QuerySingleAsync<byte[]>($"SELECT {db.Column("RowVersion")} FROM {db.Table("AuditRecords")} WHERE {db.Column("Id")}=@Id", new { Id = auditId });
        Expect.True(stored.SequenceEqual(old), "Replay audit must retain the exact 8-byte token.");
        await using (var transaction = await db.Connection.BeginTransactionAsync())
        {
            var uncommitted = await ConditionalWriteAsync(db, db.Connection, id, old, "rolled back", transaction);
            Expect.True(uncommitted is not null, "Rollback fixture must really perform a conditional update.");
            Expect.Changed(old, uncommitted!);
            await transaction.RollbackAsync();
        }
        Expect.True(old.SequenceEqual(await db.ReadTokenAsync("DatabaseTokenRows", id)), "Rolled-back update must preserve the committed token.");
        return "Replay audit bytea/binary(8) and Base64 preserved exact bytes; update rollback restored data and committed token. Sequence gaps are not treated as identity cursor boundaries.";
    }

    private static async Task<string> ApplicationCoveredAsync(ProbeDatabase db)
    {
        await using var context = new ApplicationTokenContext(db.Connection, db.Schema);
        var row = new ApplicationTokenRow { Id = Guid.NewGuid(), Value = "application seed" };
        context.Rows.Add(row);
        await context.SaveChangesAsync();
        var original = row.RowVersion.ToArray();
        row.Value = "application saved";
        await context.SaveChangesAsync();
        Expect.Changed(original, row.RowVersion);
        var replacement = RandomNumberGenerator.GetBytes(8);
        var affected = await db.Connection.ExecuteAsync($"UPDATE {db.Table("ApplicationTokenRows")} SET {db.Column("Value")}=@Value, {db.Column("RowVersion")}=@New WHERE {db.Column("Id")}=@Id AND {db.Column("RowVersion")}=@Old",
            new { Id = row.Id, Old = row.RowVersion, New = replacement, Value = "explicit application Dapper" });
        Expect.True(affected == 1, "Explicit application Dapper writer must update one row.");
        row.Value = "stale application EF";
        var rejected = false;
        try { await context.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { rejected = true; }
        Expect.True(rejected, "EF must reject stale row after correctly implemented application Dapper token replacement.");
        return "Application candidate works for SaveChanges and a raw writer that explicitly supplies old/new token and checks affected rows.";
    }

    private static async Task<string> ApplicationGapsAsync(ProbeDatabase db)
    {
        foreach (var writer in new[] { "raw SQL", "ExecuteUpdate" })
        {
            await using var stale = new ApplicationTokenContext(db.Connection, db.Schema);
            var row = new ApplicationTokenRow { Id = Guid.NewGuid(), Value = "application stale fixture" };
            stale.Rows.Add(row);
            await stale.SaveChangesAsync();
            var old = row.RowVersion.ToArray();
            await using var bypassConnection = db.NewConnection();
            await bypassConnection.OpenAsync();
            if (writer == "raw SQL")
                await bypassConnection.ExecuteAsync($"UPDATE {db.Table("ApplicationTokenRows")} SET {db.Column("Value")}=@Value WHERE {db.Column("Id")}=@Id", new { Id = row.Id, Value = "bypass committed" });
            else
            {
                await using var bypass = new ApplicationTokenContext(bypassConnection, db.Schema);
                var count = await bypass.Rows.Where(item => item.Id == row.Id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Value, "bypass committed"));
                Expect.True(count == 1, "Application candidate bypass fixture must modify a real row.");
            }
            Expect.True(old.SequenceEqual(await db.ReadTokenAsync("ApplicationTokenRows", row.Id)), "SaveChanges-only candidate should expose unchanged token after bypass writer.");
            row.Value = "stale overwrite accepted";
            var accepted = await stale.SaveChangesAsync();
            Expect.True(accepted == 1, "Negative control must demonstrate a stale EF overwrite actually accepted after bypass.");
            var value = await bypassConnection.QuerySingleAsync<string>($"SELECT {db.Column("Value")} FROM {db.Table("ApplicationTokenRows")} WHERE {db.Column("Id")}=@Id", new { Id = row.Id });
            Expect.True(value == "stale overwrite accepted", "Negative control must prove the bypass data was lost.");
        }
        return "Expected negative control observed twice: raw SQL and ExecuteUpdate left token unchanged and stale EF SaveChanges overwrote their committed data.";
    }
}
