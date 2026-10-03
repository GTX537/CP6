using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

internal static class Wp2ConstraintWriteGates
{
    public static async Task<string> MoneyAndForeignKeyAsync(CP6Context context, DbConnection connection, bool pg)
    {
        Require(ReferenceEquals(context.Database.GetDbConnection(), connection), "Money gate must use the verified actual Core connection.");
        var tenant = Guid.NewGuid(); var entry = Guid.NewGuid(); var line = Guid.NewGuid();
        var now = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Unspecified);
        var entries = Table(pg, "Fin_JournalEntry"); var lines = Table(pg, "Fin_JournalLine");
        await using var transaction = await context.Database.BeginTransactionAsync();
        var native = transaction.GetDbTransaction();
        await connection.ExecuteAsync($"""
            INSERT INTO {entries}({Q(pg,"Id")},{Q(pg,"TenantId")},{Q(pg,"No")},{Q(pg,"VoucherDate")},{Q(pg,"PeriodId")},
              {Q(pg,"Source")},{Q(pg,"Status")},{Q(pg,"Description")},{Q(pg,"MakerId")},{Q(pg,"MakerAt")},{Q(pg,"AutoPosted")},{Q(pg,"CreateDate")})
            VALUES(@entry,@tenant,@number,@now,@period,0,0,@description,@maker,@now,@auto,@now)
            """, new { entry, tenant, number = "WP2money" + Guid.NewGuid().ToString("N")[..16], now, period = Guid.NewGuid(),
                description = "WP2 storage boundary fixture", maker = "WP2-probe", auto = false }, native);
        await connection.ExecuteAsync($"""
            INSERT INTO {lines}({Q(pg,"Id")},{Q(pg,"TenantId")},{Q(pg,"EntryId")},{Q(pg,"LineNo")},{Q(pg,"AccountId")},
              {Q(pg,"Debit")},{Q(pg,"Credit")},{Q(pg,"Memo")},{Q(pg,"CreateDate")})
            VALUES(@line,@tenant,@entry,1,@account,@debit,0,@memo,@now)
            """, new { line, tenant, entry, account = Guid.NewGuid(), debit = 9.995m, memo = "WP2-money", now }, native);
        var values = new { line, tenant };
        var where = $"{Q(pg,"Id")}=@line AND {Q(pg,"TenantId")}=@tenant";
        Require(await connection.QuerySingleAsync<decimal>($"SELECT {Q(pg,"Debit")} FROM {lines} WHERE {where}", values, native) == 10.00m,
            "Actual financial decimal(18,2) must round 9.995 to 10.00.");
        await connection.ExecuteAsync($"UPDATE {lines} SET {Q(pg,"Debit")}=@amount WHERE {where}",
            new { line, tenant, amount = 9999999999999999.99m }, native);
        Require(await connection.QuerySingleAsync<decimal>($"SELECT {Q(pg,"Debit")} FROM {lines} WHERE {where}", values, native) == 9999999999999999.99m,
            "Actual financial decimal(18,2) must preserve its maximum positive value.");
        await RejectAsync(transaction, "money_overflow", () => connection.ExecuteAsync($"UPDATE {lines} SET {Q(pg,"Debit")}=@amount WHERE {where}",
            new { line, tenant, amount = 10000000000000000m }, native), pg, "22003", [8115]);
        await RejectAsync(transaction, "missing_parent", () => connection.ExecuteAsync($"UPDATE {lines} SET {Q(pg,"EntryId")}=@parent WHERE {where}",
            new { line, tenant, parent = Guid.NewGuid() }, native), pg, "23503", [547], "FK_Fin_JournalLine_Fin_JournalEntry_EntryId");
        Require(await connection.QuerySingleAsync<Guid>($"SELECT {Q(pg,"EntryId")} FROM {lines} WHERE {where}", values, native) == entry,
            "Rejected missing financial parent must retain the original link.");
        await transaction.RollbackAsync();
        Require(await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {lines} WHERE {Q(pg,"Id")}=@line", new { line }) == 0
            && await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {entries} WHERE {Q(pg,"Id")}=@entry", new { entry }) == 0,
            "All financial storage-boundary fixtures must roll back.");
        return "Actual financial decimal(18,2) rounding, maximum value and overflow; native missing-parent FK rejection retained original link; fixtures rolled back. Domain posting rules are separate.";
    }

    public static async Task<string> SpaceTenantForeignKeyAndUnicodeAsync(SpaceContext context, DbConnection connection, bool pg)
    {
        Require(ReferenceEquals(context.Database.GetDbConnection(), connection), "Space constraint gate must use the verified actual Space connection.");
        var model = SpaceModel.Create(context.CurrentTenantId, Guid.NewGuid());
        var version = SpaceModelVersion.CreateDraft(context.CurrentTenantId, model.Id, 1, "WP2-boundary");
        await using var transaction = await context.Database.BeginTransactionAsync();
        var native = transaction.GetDbTransaction();
        context.Add(model); context.Add(version);
        await context.SaveChangesAsync();
        var table = Table(pg, "Space_ModelVersion");
        var where = $"{Q(pg,"Id")}=@id";
        var parameters = new { id = version.Id };
        await RejectAsync(transaction, "wrong_tenant", () => connection.ExecuteAsync($"UPDATE {table} SET {Q(pg,"TenantId")}=@tenant WHERE {where}",
            new { id = version.Id, tenant = Guid.NewGuid() }, native), pg, "23503", [547], "FK_Space_ModelVersion_Space_Model_Tenant_Model");
        Require(await connection.QuerySingleAsync<Guid>($"SELECT {Q(pg,"TenantId")} FROM {table} WHERE {where}", parameters, native) == context.CurrentTenantId,
            "A real parent in another tenant must not satisfy the composite Space FK.");
        var boundary = string.Concat(Enumerable.Repeat("😀", 100));
        await connection.ExecuteAsync($"UPDATE {table} SET {Q(pg,"Name")}=@name WHERE {where}", new { id = version.Id, name = boundary }, native);
        Require(await connection.QuerySingleAsync<string>($"SELECT {Q(pg,"Name")} FROM {table} WHERE {where}", parameters, native) == boundary,
            "Actual 200 UTF-16-unit Space name must retain supplementary Unicode without truncation.");
        foreach (var tooLong in new[] { new string('a', 201), boundary + "😀" })
            await RejectAsync(transaction, "name_overflow", () => connection.ExecuteAsync($"UPDATE {table} SET {Q(pg,"Name")}=@name WHERE {where}",
                new { id = version.Id, name = tooLong }, native), pg, "23514", [2628, 8152]);
        Require(await connection.QuerySingleAsync<string>($"SELECT {Q(pg,"Name")} FROM {table} WHERE {where}", parameters, native) == boundary,
            "Rejected Unicode overflow must preserve the last valid name.");
        await transaction.RollbackAsync();
        context.ChangeTracker.Clear();
        Require(!await context.Models.IgnoreQueryFilters().AnyAsync(row => row.Id == model.Id)
            && !await context.Versions.IgnoreQueryFilters().AnyAsync(row => row.Id == version.Id),
            "Actual Space parent/version and failed writes must roll back completely.");
        return "Actual Space composite FK rejected a tenant move despite the valid parent ID; 200 UTF-16 units retained, 201 ASCII/202 supplementary units rejected without truncation; complete rollback.";
    }

    private static async Task RejectAsync(IDbContextTransaction transaction, string savepoint, Func<Task<int>> mutation,
        bool pg, string pgState, int[] sqlNumbers, string? constraint = null)
    {
        await transaction.CreateSavepointAsync(savepoint);
        var rejected = false;
        try { await mutation(); }
        catch (PostgresException exception) when (pg && exception.SqlState == pgState
            && (constraint is null || exception.ConstraintName == constraint)) { rejected = true; }
        catch (SqlException exception) when (!pg && sqlNumbers.Contains(exception.Number)
            && (constraint is null || exception.Message.Contains(constraint, StringComparison.Ordinal))) { rejected = true; }
        finally { await transaction.RollbackToSavepointAsync(savepoint); }
        Require(rejected, "The actual installed storage constraint must reject its invalid fixture with the expected database error.");
    }

    private static string Q(bool pg, string name) => pg ? '"' + name + '"' : '[' + name + ']';
    private static string Table(bool pg, string name) => Q(pg, pg ? "public" : "dbo") + '.' + Q(pg, name);
    private static void Require(bool condition, string detail) { if (!condition) throw new MigrationAssertionException(detail); }
}
