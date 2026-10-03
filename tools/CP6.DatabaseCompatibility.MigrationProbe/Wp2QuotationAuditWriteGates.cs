using System.Data;
using System.Data.Common;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Entity;
using CP6.Entity.DomainModels.Erp;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

internal static class Wp2QuotationAuditWriteGates
{
    public static async Task<string> VerifyAsync(CP6Context context, DbConnection connection, bool pg)
    {
        Require(ReferenceEquals(context.Database.GetDbConnection(), connection) && connection.State == ConnectionState.Open,
            "Quotation audit gate requires the verified open actual Core connection.");
        Require(context.Database.CurrentTransaction is null && !context.ChangeTracker.HasChanges(),
            "Quotation audit gate requires no existing transaction or unsaved caller changes.");
        if (!pg)
        {
            var options = await connection.QuerySingleAsync<int>("SELECT @@OPTIONS");
            Require((options & 8) != 0 && (options & 16384) == 0,
                "Quotation storage checks require ANSI_WARNINGS ON and XACT_ABORT OFF for savepoint isolation.");
        }

        var before = await CountsAsync(connection, pg);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var number = "WP2QA" + suffix;
        var calc = new QuotationCalc
        {
            Id = Guid.NewGuid(), TenantId = context.CurrentTenantId, QtnNo = number, QtnCalcNo = "WP2QC" + suffix
        };
        var detail = new QuotationDetail
        {
            Id = Guid.NewGuid(), TenantId = context.CurrentTenantId, QtnNo = number, DetailNo = 1,
            QtnCalcNo = calc.QtnCalcNo, ItemName1 = "WP2 quotation storage fixture"
        };
        var quotation = new Quotation
        {
            Id = Guid.NewGuid(), TenantId = context.CurrentTenantId, QtnNo = number,
            BaseCd = "WP2-BASE", StaffCd = "WP2-STAFF", CustomerCd = "WP2-CUSTOMER",
            Calcs = [calc], Details = [detail]
        };
        (Type EntityType, string Table, Guid Id)[] rows =
        [
            (typeof(Quotation), "T_Quotation", quotation.Id),
            (typeof(QuotationCalc), "T_QuotationCalc", calc.Id),
            (typeof(QuotationDetail), "T_QuotationDetail", detail.Id)
        ];
        var model = context.GetService<IDesignTimeModel>().Model;
        var columns = rows.SelectMany(row => new[] { nameof(BaseEntity.Creator), nameof(BaseEntity.Modifier) }
            .Select(column => Column(model, row.EntityType, row.Table, row.Id, column, pg))).ToArray();
        Require(columns.Length == 6, "Exactly the six frozen quotation audit columns must be checked.");

        var acceptedValues = new[]
        {
            new string('a', 100),
            string.Concat(Enumerable.Repeat("😀", 50)),
            new string('a', 97) + "   "
        };
        var rejectedValues = new[] { new string('a', 101), acceptedValues[1] + "x" };
        Require(acceptedValues.All(value => value.Length == 100) && rejectedValues.All(value => value.Length == 101),
            "Quotation boundary inputs must represent exact UTF-16 unit counts.");

        var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            context.Add(quotation);
            await context.SaveChangesAsync();
            foreach (var row in rows)
                Require(await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {Table(pg, row.Table)} WHERE {Q(pg, "Id")}=@id",
                    new { id = row.Id }, transaction.GetDbTransaction()) == 1,
                    $"Actual EF must persist the valid quotation graph row in {row.Table}.");
            Require(quotation.RowVersion is { Length: 8 } && calc.RowVersion is { Length: 8 } && detail.RowVersion is { Length: 8 },
                "The three actual EF quotation graph rows must receive database-generated eight-byte tokens.");

            foreach (var column in columns)
            {
                foreach (var value in acceptedValues)
                {
                    Require(await UpdateAsync(connection, transaction, pg, column, value) == 1,
                        $"Native quotation boundary update must target exactly one {column.Table}.{column.Name} fixture.");
                    await RequireValueAsync(connection, transaction, pg, column, value);
                }
                foreach (var value in rejectedValues)
                    await RejectAsync(connection, transaction, pg, column, value);
                await ObserveTrailingOverflowAsync(connection, transaction, pg, column);
            }
        }
        finally
        {
            try { await transaction.RollbackAsync(); }
            finally
            {
                context.ChangeTracker.Clear();
                await transaction.DisposeAsync();
            }
            var after = await CountsAsync(connection, pg);
            Require(before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var count) && count == pair.Value),
                "Every actual table count, including field audits and migration histories, must be restored after quotation fixtures.");
        }

        var tailScope = pg
            ? "The separate 101-unit input with only its final space beyond capacity was rejected by each exact managed UTF-16 check."
            : "The separate 101-unit input with only its final space beyond capacity was truncated to 100 units by native SQL Server with ANSI_WARNINGS ON.";
        return "Actual EF stored the valid three-row quotation graph. All six Creator/Modifier columns retained 100 ASCII units, 50 supplementary characters and a 100-unit value with trailing spaces exactly; 101 ASCII and 50 supplementary characters plus x were rejected by the expected native storage errors and savepoints retained the previous value. "
            + tailScope + " This excess-trailing-space behavior is outside the shared provider equivalence promise. Entire transaction and all table counts restored; full quotation business acceptance remains separate.";
    }

    private static AuditColumn Column(IModel model, Type entityType, string table, Guid id, string name, bool pg)
    {
        var entity = model.FindEntityType(entityType)
            ?? throw new MigrationAssertionException("The actual provider design model must contain each quotation entity.");
        Require(entity.GetTableName() == table && (entity.GetSchema() ?? (pg ? "public" : "dbo")) == (pg ? "public" : "dbo"),
            $"The quotation fixture must use the frozen {table} table and default provider schema.");
        var property = entity.FindProperty(name)
            ?? throw new MigrationAssertionException($"Actual quotation model property {table}.{name} is missing.");
        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
        Require(property.ClrType == typeof(string) && property.GetMaxLength() == 100 && property.GetColumnName(store) == name,
            $"Actual quotation model {table}.{name} must retain its 100-unit string contract.");
        string? constraint = null;
        if (pg)
        {
            Require(property.GetColumnType() == "bpchar", $"Actual quotation model {table}.{name} must expose the raw non-trimming text storage.");
            var expected = $"CK_{table}_{name}_Utf16Capacity";
            var checks = entity.GetCheckConstraints().Where(check => check.Name == expected
                && check.FindAnnotation("CP6:GeneratedCapacity")?.Value is true).ToArray();
            Require(checks.Length == 1, $"The actual provider design model must expose exactly the managed check {expected}.");
            constraint = checks[0].Name;
        }
        return new(table, id, name, constraint);
    }

    private static Task<int> UpdateAsync(DbConnection connection, IDbContextTransaction transaction, bool pg, AuditColumn column, string value)
    {
        // Native Dapper binding has no EF MaxLength/Size=100 facet; the complete
        // 101-unit negative input reaches the database instead of being truncated by EF.
        return connection.ExecuteAsync($"UPDATE {Table(pg, column.Table)} SET {Q(pg, column.Name)}=@value WHERE {Q(pg, "Id")}=@id",
            new { id = column.Id, value }, transaction.GetDbTransaction());
    }

    private static async Task RequireValueAsync(DbConnection connection, IDbContextTransaction transaction, bool pg, AuditColumn column, string expected)
    {
        var value = await ReadAsync(connection, transaction, pg, column);
        Require(string.Equals(value.Value, expected, StringComparison.Ordinal) && value.Value.Length == expected.Length
            && value.Utf16Units == expected.Length && value.StorageBytes == (pg ? Encoding.UTF8.GetByteCount(expected) : expected.Length * 2),
            $"Native {column.Table}.{column.Name} must preserve the exact raw Unicode value, trailing spaces and storage/UTF-16 lengths.");
    }

    private static async Task<ColumnValue> ReadAsync(DbConnection connection, IDbContextTransaction transaction, bool pg, AuditColumn column)
    {
        var raw = pg ? $"convert_from(pg_catalog.bpcharsend({Q(pg, column.Name)}), 'UTF8')" : $"CONVERT(nvarchar(max), {Q(pg, column.Name)})";
        // Count four-byte UTF-8 code points separately rather than treating PG's
        // character count as the SQL Server/.NET UTF-16 unit count.
        var units = pg
            ? $"(char_length({raw}) + (SELECT count(*) FROM regexp_split_to_table({raw} COLLATE \"C\", '') AS cp(value) WHERE octet_length(cp.value)=4))::integer"
            : $"CONVERT(int, DATALENGTH({Q(pg, column.Name)})/2)";
        var bytes = pg ? $"octet_length({raw})" : $"CONVERT(int, DATALENGTH({Q(pg, column.Name)}))";
        return await connection.QuerySingleAsync<ColumnValue>($"SELECT {raw} AS {Q(pg, "Value")}, {units} AS {Q(pg, "Utf16Units")}, {bytes} AS {Q(pg, "StorageBytes")} FROM {Table(pg, column.Table)} WHERE {Q(pg, "Id")}=@id",
            new { id = column.Id }, transaction.GetDbTransaction());
    }

    private static async Task RejectAsync(DbConnection connection, IDbContextTransaction transaction, bool pg, AuditColumn column, string value)
    {
        var previous = await ReadAsync(connection, transaction, pg, column);
        const string point = "quotation_audit_overflow";
        await transaction.CreateSavepointAsync(point);
        var rejected = false;
        try { await UpdateAsync(connection, transaction, pg, column, value); }
        catch (PostgresException exception) when (ExpectedCheck(exception, pg, column)) { rejected = true; }
        catch (SqlException exception) when (!pg && exception.Number is 2628 or 8152) { rejected = true; }
        finally { await transaction.RollbackToSavepointAsync(point); }
        Require(await ReadAsync(connection, transaction, pg, column) == previous,
            $"Rejected {column.Table}.{column.Name} overflow must retain the exact previous raw value after savepoint rollback.");
        Require(rejected, $"Installed {column.Table}.{column.Name} must reject the full 101-unit non-space overflow with its exact native storage error.");
    }

    private static async Task ObserveTrailingOverflowAsync(DbConnection connection, IDbContextTransaction transaction, bool pg, AuditColumn column)
    {
        var previous = await ReadAsync(connection, transaction, pg, column);
        const string point = "quotation_audit_tail";
        await transaction.CreateSavepointAsync(point);
        var rejected = false;
        try
        {
            var affected = await UpdateAsync(connection, transaction, pg, column, new string('a', 100) + " ");
            Require(!pg && affected == 1,
                $"PostgreSQL {column.Table}.{column.Name} must reject overflow even when the excess unit is a trailing space.");
            await RequireValueAsync(connection, transaction, pg, column, new string('a', 100));
        }
        catch (PostgresException exception) when (ExpectedCheck(exception, pg, column)) { rejected = true; }
        finally { await transaction.RollbackToSavepointAsync(point); }
        Require(rejected == pg, $"The explicitly separate trailing-space overflow observation for {column.Table}.{column.Name} must match native provider behavior.");
        Require(await ReadAsync(connection, transaction, pg, column) == previous,
            $"The separate trailing-space overflow observation for {column.Table}.{column.Name} must restore the prior raw value.");
    }

    private static bool ExpectedCheck(PostgresException exception, bool pg, AuditColumn column) => pg && exception.SqlState == "23514"
        && exception.SchemaName == "public" && exception.TableName == column.Table && exception.ConstraintName == column.Constraint;

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
    private sealed record AuditColumn(string Table, Guid Id, string Name, string? Constraint);
    private sealed record ColumnValue(string Value, int Utf16Units, int StorageBytes);
    private sealed record TableRow(string Schema, string Name);
}
