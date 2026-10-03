using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Migrations;
using CP6.Core.Persistence;
using CP6.Entity.DomainModels.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CP6.Tests.Persistence;

public sealed class QuotationAuditColumnCapacityRepairTests
{
    private const string OriginalLastMigration = "20260916130000_RetireLegacyCrmModel";
    private const string RepairMigration = "20261002193500_RestoreQuotationAuditColumnCapacity";
    private static readonly Lazy<string> CompleteChain = new(() => GenerateScript());
    private static readonly Lazy<string> OriginalChain = new(() => GenerateScript(OriginalLastMigration));
    private static readonly Lazy<IModel> FrozenModel = new(() => new CP6ContextModelSnapshot().Model);

    public static TheoryData<string, string> AuditColumns => new()
    {
        { "T_Quotation", "Creator" }, { "T_Quotation", "Modifier" },
        { "T_QuotationCalc", "Creator" }, { "T_QuotationCalc", "Modifier" },
        { "T_QuotationDetail", "Creator" }, { "T_QuotationDetail", "Modifier" }
    };

    [Theory]
    [MemberData(nameof(AuditColumns))]
    public void Frozen_model_and_inherited_attribute_require_nullable_unicode_100(string table, string column)
    {
        var entity = Assert.Single(FrozenModel.Value.GetEntityTypes(), entity => entity.GetTableName() == table);
        var property = entity.FindProperty(column)!;
        Assert.Equal("dbo", entity.GetSchema() ?? "dbo");
        Assert.Equal(typeof(string), property.ClrType);
        Assert.True(property.IsNullable);
        Assert.Equal(100, property.GetMaxLength());
        Assert.Equal("nvarchar(100)", property.GetColumnType());
        Assert.Equal(column, property.GetColumnName(StoreObjectIdentifier.Table(table, entity.GetSchema())));
        Assert.False(property.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
        // Snapshot entities are name-based property bags; reflect the actual domain CLR type.
        var clrType = table switch
        {
            "T_Quotation" => typeof(Quotation),
            "T_QuotationCalc" => typeof(QuotationCalc),
            "T_QuotationDetail" => typeof(QuotationDetail),
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        var member = clrType.GetProperty(column);
        Assert.NotNull(member);
        var annotation = member.GetCustomAttribute<MaxLengthAttribute>();
        Assert.NotNull(annotation);
        Assert.Equal(100, annotation.Length);
    }

    [Theory]
    [MemberData(nameof(AuditColumns))]
    public void Complete_chain_restores_each_capacity_while_preserving_native_collation(string table, string column)
        => Assert.Contains($"ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] nvarchar(100) COLLATE ",
            CompleteChain.Value, StringComparison.Ordinal);

    [Fact]
    public void Legacy_136_chain_preserves_the_historical_rebuild_and_has_no_capacity_repair()
    {
        using var context = Context();
        Assert.Equal(136, context.GetService<IMigrationsAssembly>().Migrations.Keys.Count(id => string.CompareOrdinal(id, OriginalLastMigration) <= 0));
        foreach (var table in new[] { "T_Quotation", "T_QuotationCalc", "T_QuotationDetail" })
        {
            Assert.Matches($@"CREATE TABLE \[{table}\] \([\s\S]*?\[Creator\] nvarchar\(max\) NULL,[\s\S]*?\[Modifier\] nvarchar\(max\) NULL", OriginalChain.Value);
            foreach (var column in new[] { "Creator", "Modifier" })
                Assert.DoesNotContain($"ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] nvarchar(100) COLLATE ", OriginalChain.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Public_commands_are_read_only_and_each_targets_exactly_one_frozen_column()
    {
        var statements = QuotationAuditColumnCapacityRepairSql.CreateStatements();
        Assert.Equal(6, statements.Count);
        var mutableView = Assert.IsAssignableFrom<IList<string>>(statements);
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView[0] = "replacement");
        for (var ordinal = 0; ordinal < AuditColumns.Count; ordinal++)
        {
            var table = (string)AuditColumns.ElementAt(ordinal)[0];
            var column = (string)AuditColumns.ElementAt(ordinal)[1];
            Assert.Contains($"ALTER TABLE [dbo].[{table}] ALTER COLUMN [{column}] nvarchar(100) COLLATE ", statements[ordinal], StringComparison.Ordinal);
            Assert.Single(Regex.Matches(statements[ordinal], @"\bALTER\s+TABLE\b"));
        }
    }

    [Fact]
    public void Guards_check_native_metadata_raw_byte_capacity_and_preserve_collation_without_data_rewrites()
    {
        var statements = QuotationAuditColumnCapacityRepairSql.CreateStatements();
        for (var ordinal = 0; ordinal < AuditColumns.Count; ordinal++)
        {
            var table = (string)AuditColumns.ElementAt(ordinal)[0];
            var sql = statements[ordinal];
            foreach (var required in new[] { "FROM sys.tables", "schema_id = SCHEMA_ID(N'dbo')", "FROM sys.columns", "INNER JOIN sys.types",
                "t.name = N'nvarchar'", "t.is_user_defined = 0", "t.is_assembly_type = 0", "c.system_type_id = TYPE_ID(N'nvarchar')",
                "c.is_nullable = 1", "c.is_computed = 0", "c.is_identity = 0", "c.max_length IN (-1, 200)",
                "@cp6_collation IS NULL", "@cp6_collation = N''", "IF @cp6_max_length = -1", "WITH (TABLOCKX, HOLDLOCK)",
                "@cp6_collation COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9_]%'", "FROM sys.fn_helpcollations()",
                "DATALENGTH([", "]) > 200", "THROW 51043", "c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation",
                "DECLARE @cp6_check nvarchar(max) = N'SELECT @overlong", "N'@overlong bit OUTPUT', @overlong = @cp6_overlong OUTPUT",
                "+ @cp6_collation + N' NULL;'", "EXEC sys.sp_executesql @cp6_alter" })
                Assert.Contains(required, sql, StringComparison.Ordinal);
            Assert.DoesNotContain("QUOTENAME(@cp6_collation)", sql, StringComparison.Ordinal);
            Assert.True(sql.IndexOf("IF @cp6_column_id IS NULL", StringComparison.Ordinal) < sql.IndexOf("EXEC sys.sp_executesql @cp6_check", StringComparison.Ordinal));
            Assert.True(sql.IndexOf("FROM sys.fn_helpcollations()", StringComparison.Ordinal) < sql.IndexOf("DECLARE @cp6_alter", StringComparison.Ordinal));
            Assert.True(sql.IndexOf("DATALENGTH([", StringComparison.Ordinal) < sql.IndexOf("DECLARE @cp6_alter", StringComparison.Ordinal));
            var identityGuard = $"IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NULL";
            Assert.Contains(identityGuard, sql, StringComparison.Ordinal);
            Assert.Contains($"OR OBJECT_ID(N'[dbo].[{table}]', N'U') <> @cp6_object_id", sql, StringComparison.Ordinal);
            var dataRead = sql.IndexOf("EXEC sys.sp_executesql @cp6_check", StringComparison.Ordinal);
            var identityCheck = sql.IndexOf(identityGuard, StringComparison.Ordinal);
            var columnCheck = sql.LastIndexOf("IF NOT EXISTS (", StringComparison.Ordinal);
            var alter = sql.IndexOf("DECLARE @cp6_alter", StringComparison.Ordinal);
            Assert.True(dataRead < identityCheck && identityCheck < columnCheck && columnCheck < alter);
            var identitySql = sql[identityCheck..columnCheck];
            Assert.Contains("FROM sys.tables", identitySql, StringComparison.Ordinal);
            Assert.Contains("object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')", identitySql, StringComparison.Ordinal);
            Assert.Contains($"name COLLATE Latin1_General_100_BIN2 = N'{table}' COLLATE Latin1_General_100_BIN2", identitySql, StringComparison.Ordinal);
            Assert.Contains("THROW 51043", identitySql, StringComparison.Ordinal);
            var columnSql = sql[columnCheck..alter];
            Assert.Contains("c.object_id = @cp6_object_id AND c.column_id = @cp6_column_id", columnSql, StringComparison.Ordinal);
            Assert.Contains("c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation", columnSql, StringComparison.Ordinal);
            Assert.Contains("THROW 51043", columnSql, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"\b(?:LEN|SUBSTRING|LEFT)\s*\(", sql);
            Assert.DoesNotMatch(@"\b(?:DROP|TRUNCATE|DELETE|INSERT|UPDATE|TRY|CATCH|COMMIT|ROLLBACK)\b", sql);
            Assert.DoesNotContain("__EFMigrationsHistory", sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Forward_repair_contains_only_six_separate_default_transaction_sql_operations()
    {
        using var context = Context();
        var assembly = context.GetService<IMigrationsAssembly>();
        Assert.Contains(RepairMigration, assembly.Migrations.Keys);
        var migration = assembly.CreateMigration(assembly.Migrations[RepairMigration], "Microsoft.EntityFrameworkCore.SqlServer");
        Assert.Equal(6, migration.UpOperations.Count);
        Assert.All(migration.UpOperations, operation => Assert.False(Assert.IsType<SqlOperation>(operation).SuppressTransaction));
        Assert.Equal(QuotationAuditColumnCapacityRepairSql.CreateStatements(), migration.UpOperations.Cast<SqlOperation>().Select(operation => operation.Sql));
    }

    [Fact]
    public void Repair_rejects_non_sql_providers_and_is_forward_only()
    {
        using var context = Context();
        var assembly = context.GetService<IMigrationsAssembly>();
        var type = assembly.Migrations[RepairMigration];
        Assert.Throws<NotSupportedException>(() => assembly.CreateMigration(type, "Npgsql.EntityFrameworkCore.PostgreSQL").UpOperations);
        Assert.Throws<NotSupportedException>(() => assembly.CreateMigration(type, "Microsoft.EntityFrameworkCore.SqlServer").DownOperations);
    }

    private static string GenerateScript(string? target = null)
    {
        using var context = Context();
        return context.GetService<IMigrator>().GenerateScript(toMigration: target);
    }

    private static CP6Context Context() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseSqlServer("Server=database.cp6.test;Database=fixture;Integrated Security=True;MultipleActiveResultSets=False").Options);
}
