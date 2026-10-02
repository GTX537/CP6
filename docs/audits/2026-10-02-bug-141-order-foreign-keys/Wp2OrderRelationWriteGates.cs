using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Erp;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

internal static class Wp2OrderRelationWriteGates
{
    public static async Task<string> VerifyAsync(CP6Context context, DbConnection connection, bool pg)
    {
        Require(ReferenceEquals(context.Database.GetDbConnection(), connection), "Order relationship gate must use the verified actual Core connection.");
        var before = await CountsAsync(connection, pg);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var number = "WP2FK" + suffix;
        var product = "WP2-PRODUCT";
        var tenant = context.CurrentTenantId;
        var parent = new Order { Id = Guid.NewGuid(), TenantId = tenant, WebOrderNo = number, CustomerCd = "WP2-CUSTOMER", OrderType = "TEST" };
        var detail = new OrderDetail { Id = Guid.NewGuid(), TenantId = tenant, WebOrderNo = number, WebOrderDetailNo = 1, ProductCd = product };
        // The second detail has no children so the parent-FK negative control
        // cannot instead fail a child FK referencing the first detail's key.
        var emptyDetail = new OrderDetail { Id = Guid.NewGuid(), TenantId = tenant, WebOrderNo = number, WebOrderDetailNo = 2, ProductCd = product };
        var process = new OrderProcess { Id = Guid.NewGuid(), TenantId = tenant, WebOrderNo = number, WebOrderDetailNo = 1,
            ProductCd = product, OperationCd = "WP2-OP", ProcessCd = "WP2-PROCESS" };
        var note = new OrderProcessNote { Id = Guid.NewGuid(), TenantId = tenant, WebOrderNo = number, WebOrderDetailNo = 1,
            ProductCd = product, OperationCd = "WP2-OP", Note1 = "WP2 database relationship fixture" };
        var material = new OrderMaterial { Id = Guid.NewGuid(), TenantId = tenant, WebOrderNo = number, WebOrderDetailNo = 1,
            ProductCd = product, ProcessCd = "WP2-PROCESS", MaterialCd = "WP2-MATERIAL" };
        await using var transaction = await context.Database.BeginTransactionAsync();
        var native = transaction.GetDbTransaction();
        try
        {
            context.AddRange(parent, detail, emptyDetail, process, note, material);
            await context.SaveChangesAsync();
            foreach (var (table, id) in new[] { ("T_Order", parent.Id), ("T_OrderDetail", detail.Id), ("T_OrderDetail", emptyDetail.Id),
                ("T_OrderProcess", process.Id), ("T_OrderProcessNote", note.Id), ("T_OrderMaterial", material.Id) })
                Require(await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {Table(pg, table)} WHERE {Q(pg, "Id")}=@id", new { id }, native) == 1,
                    "Actual EF must persist every valid parent/detail/process/note/material relationship fixture.");

            await RejectAsync(transaction, connection, pg, "T_OrderDetail", emptyDetail.Id, "WebOrderNo", "WP2missing" + suffix,
                Constraint<OrderDetail, Order>(context));
            await RejectAsync(transaction, connection, pg, "T_OrderProcess", process.Id, "ProductCd", "WP2-MISSING",
                Constraint<OrderProcess, OrderDetail>(context));
            await RejectAsync(transaction, connection, pg, "T_OrderProcessNote", note.Id, "ProductCd", "WP2-MISSING",
                Constraint<OrderProcessNote, OrderDetail>(context));
            await RejectAsync(transaction, connection, pg, "T_OrderMaterial", material.Id, "ProductCd", "WP2-MISSING",
                Constraint<OrderMaterial, OrderDetail>(context));

            Require(await connection.ExecuteAsync($"DELETE FROM {Table(pg, "T_OrderDetail")} WHERE {Q(pg, "Id")}=@id", new { id = detail.Id }, native) == 1,
                "The actual detail deletion must execute once.");
            foreach (var table in new[] { "T_OrderProcess", "T_OrderProcessNote", "T_OrderMaterial" })
                Require(await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {Table(pg, table)} WHERE {Q(pg, "WebOrderNo")}=@number", new { number }, native) == 0,
                    "Native detail deletion must cascade to the actual process, note and material rows.");
            Require(await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {Table(pg, "T_OrderDetail")} WHERE {Q(pg, "Id")}=@id", new { id = emptyDetail.Id }, native) == 1,
                "The second detail must still exist before the parent deletion exercises its cascade.");
            Require(await connection.ExecuteAsync($"DELETE FROM {Table(pg, "T_Order")} WHERE {Q(pg, "Id")}=@id", new { id = parent.Id }, native) == 1,
                "The actual order deletion must execute once.");
            Require(await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {Table(pg, "T_OrderDetail")} WHERE {Q(pg, "WebOrderNo")}=@number", new { number }, native) == 0,
                "Native parent deletion must cascade to its remaining detail row.");
        }
        finally
        {
            await transaction.RollbackAsync();
            context.ChangeTracker.Clear();
            var after = await CountsAsync(connection, pg);
            Require(before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var rows) && rows == pair.Value),
                "Every actual table count, including audit rows and histories, must be restored after the Order relationship fixtures.");
        }
        return "Actual EF stored the valid six-row Order graph; each of four native FKs rejected its independently targeted orphan with the exact constraint identity; native detail/parent deletions exercised all four cascades. Entire transaction and all table counts restored; full order business validation remains separate.";
    }

    private static string Constraint<TDependent, TPrincipal>(CP6Context context)
    {
        var model = context.GetService<IDesignTimeModel>().Model;
        return model.FindEntityType(typeof(TDependent))!.GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(TPrincipal)).GetConstraintName()
            ?? throw new MigrationAssertionException("The actual provider model must expose the expected Order FK name.");
    }

    private static async Task RejectAsync(IDbContextTransaction transaction, DbConnection connection, bool pg,
        string table, Guid id, string column, string value, string constraint)
    {
        const string point = "order_fk_orphan";
        await transaction.CreateSavepointAsync(point);
        var rejected = false;
        try
        {
            await connection.ExecuteAsync($"UPDATE {Table(pg, table)} SET {Q(pg, column)}=@value WHERE {Q(pg, "Id")}=@id",
                new { id, value }, transaction.GetDbTransaction());
        }
        catch (PostgresException exception) when (pg && exception.SqlState == "23503" && exception.ConstraintName == constraint) { rejected = true; }
        catch (SqlException exception) when (!pg && exception.Number == 547 && exception.Message.Contains(constraint, StringComparison.Ordinal)) { rejected = true; }
        finally { await transaction.RollbackToSavepointAsync(point); }
        Require(rejected, $"Installed Order relationship {constraint} must reject its orphan fixture with the expected database error.");
    }

    private static async Task<SortedDictionary<string, long>> CountsAsync(DbConnection connection, bool pg)
    {
        var tables = await connection.QueryAsync<TableRow>(pg
            ? "SELECT schemaname AS \"Schema\",tablename AS \"Name\" FROM pg_tables WHERE schemaname NOT LIKE 'pg_%' AND schemaname<>'information_schema'"
            : "SELECT s.name AS [Schema],t.name AS [Name] FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE t.is_ms_shipped=0");
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
            counts[$"{table.Schema}.{table.Name}"] = await connection.QuerySingleAsync<long>($"SELECT {(pg ? "COUNT(*)" : "COUNT_BIG(*)")} FROM {Q(pg, table.Schema)}.{Q(pg, table.Name)}");
        return counts;
    }
    private static string Q(bool pg, string name) => pg ? '"' + name.Replace("\"", "\"\"", StringComparison.Ordinal) + '"' : '[' + name.Replace("]", "]]", StringComparison.Ordinal) + ']';
    private static string Table(bool pg, string name) => Q(pg, pg ? "public" : "dbo") + '.' + Q(pg, name);
    private static void Require(bool condition, string detail) { if (!condition) throw new MigrationAssertionException(detail); }
    private sealed record TableRow(string Schema, string Name);
}
