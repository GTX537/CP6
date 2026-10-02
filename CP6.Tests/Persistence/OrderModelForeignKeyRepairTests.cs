using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Migrations;
using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace CP6.Tests.Persistence;

public sealed class OrderModelForeignKeyRepairTests
{
    private const string OriginalLastMigration = "20260916130000_RetireLegacyCrmModel";
    private const string RepairMigration = "20261002184500_RestoreMissingOrderModelForeignKeys";
    private static readonly Lazy<string> CompleteChain = new(() => GenerateScript());
    private static readonly Lazy<string> OriginalChain = new(() => GenerateScript(OriginalLastMigration));
    private static readonly Lazy<IModel> FrozenModel = new(() => new CP6ContextModelSnapshot().Model);

    public static TheoryData<string, string, string, string[]> MissingForeignKeys => new()
    {
        { "T_OrderDetail", "FK_T_OrderDetail_T_Order_WebOrderNo", "T_Order", ["WebOrderNo"] },
        { "T_OrderMaterial", "FK_T_OrderMaterial_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd", "T_OrderDetail", ["WebOrderNo", "WebOrderDetailNo", "ProductCd"] },
        { "T_OrderProcess", "FK_T_OrderProcess_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd", "T_OrderDetail", ["WebOrderNo", "WebOrderDetailNo", "ProductCd"] },
        { "T_OrderProcessNote", "FK_T_OrderProcessNote_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd", "T_OrderDetail", ["WebOrderNo", "WebOrderDetailNo", "ProductCd"] }
    };

    [Theory]
    [MemberData(nameof(MissingForeignKeys))]
    public void Frozen_snapshot_has_each_required_global_business_key_cascade(string table, string name, string principal, string[] columns)
    {
        var entity = Assert.Single(FrozenModel.Value.GetEntityTypes(), entity => entity.GetTableName() == table);
        var foreignKey = Assert.Single(entity.GetForeignKeys(), key => key.GetConstraintName() == name);
        Assert.Equal("dbo", entity.GetSchema() ?? "dbo");
        Assert.Equal(principal, foreignKey.PrincipalEntityType.GetTableName());
        Assert.Equal("dbo", foreignKey.PrincipalEntityType.GetSchema() ?? "dbo");
        Assert.True(foreignKey.IsRequired);
        Assert.False(foreignKey.IsUnique);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.False(foreignKey.PrincipalKey.IsPrimaryKey());
        var childStore = StoreObjectIdentifier.Table(table, entity.GetSchema());
        var principalStore = StoreObjectIdentifier.Table(principal, foreignKey.PrincipalEntityType.GetSchema());
        Assert.Equal(columns, foreignKey.Properties.Select(property => property.GetColumnName(childStore)));
        Assert.Equal(columns, foreignKey.PrincipalKey.Properties.Select(property => property.GetColumnName(principalStore)));
        Assert.DoesNotContain(foreignKey.Properties, property => property.Name == "TenantId");
    }

    [Theory]
    [MemberData(nameof(MissingForeignKeys))]
    public void Complete_chain_creates_each_missing_fk_with_check_and_frozen_actions(string table, string name, string principal, string[] columns)
    {
        var keys = string.Join(", ", columns.Select(column => $"[{column}]"));
        var expected = $"ALTER TABLE [dbo].[{table}] WITH CHECK ADD CONSTRAINT [{name}] FOREIGN KEY ({keys}) REFERENCES [dbo].[{principal}] ({keys}) ON DELETE CASCADE ON UPDATE NO ACTION";
        Assert.Contains(expected, Regex.Replace(CompleteChain.Value, @"\s+", " "), StringComparison.Ordinal);
    }

    [Fact]
    public void Original_136_chain_does_not_create_any_of_the_four_fks()
    {
        using var context = Context();
        Assert.Equal(136, context.GetService<IMigrationsAssembly>().Migrations.Keys.Count(id => string.CompareOrdinal(id, OriginalLastMigration) <= 0));
        foreach (var row in MissingForeignKeys)
            Assert.DoesNotMatch($@"ADD\s+CONSTRAINT\s+\[{Regex.Escape((string)row[1])}\]\s+FOREIGN\s+KEY", OriginalChain.Value);
    }

    [Fact]
    public void Forward_repair_has_exactly_four_transactional_sql_operations_and_no_model_changes()
    {
        using var context = Context();
        var assembly = context.GetService<IMigrationsAssembly>();
        Assert.Contains(RepairMigration, assembly.Migrations.Keys);
        var migration = assembly.CreateMigration(assembly.Migrations[RepairMigration], "Microsoft.EntityFrameworkCore.SqlServer");
        Assert.Equal(4, migration.UpOperations.Count);
        Assert.All(migration.UpOperations, operation => Assert.False(Assert.IsType<SqlOperation>(operation).SuppressTransaction));
    }

    [Fact]
    public void Existing_object_guard_validates_each_fk_without_rewriting_data_or_constraints()
    {
        var statements = OrderModelForeignKeyRepairSql.CreateStatements();
        Assert.Equal(4, statements.Count);
        Assert.All(statements, sql =>
        {
            foreach (var required in new[] { "FROM sys.objects", "schema_id = SCHEMA_ID(N'dbo')", "fk.parent_object_id = @cp6_child_object_id",
                "fk.referenced_object_id = @cp6_principal_object_id", "fk.delete_referential_action = 1", "fk.update_referential_action = 0",
                "fk.is_disabled = 0", "fk.is_not_trusted = 0", "fk.is_not_for_replication = 0", "fkc.constraint_object_id = fk.object_id",
                "fkc.constraint_column_id = expected.ordinal", "child.column_id IS NULL OR principal.column_id IS NULL", "COLLATE Latin1_General_100_BIN2", "THROW 51041" })
                Assert.Contains(required, sql, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"\b(?:DROP|NOCHECK|REBUILD|DISABLE|CREATE\s+UNIQUE|DELETE\s+FROM|INSERT\s+INTO)\b", sql);
            Assert.DoesNotContain("TenantId", sql, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(sql, @"\bALTER\s+TABLE\b"));
            Assert.DoesNotContain("ALTER TABLE", sql[(sql.IndexOf("ELSE IF NOT EXISTS", StringComparison.Ordinal))..], StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Repair_rejects_other_providers_before_operations_and_rejects_down()
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
