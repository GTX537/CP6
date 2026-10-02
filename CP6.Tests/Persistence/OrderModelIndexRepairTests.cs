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

public sealed class OrderModelIndexRepairTests
{
    private const string OriginalLastMigration = "20260916130000_RetireLegacyCrmModel";
    private const string RepairMigration = "20261002175000_RestoreMissingOrderModelIndexes";
    private static readonly Lazy<string> CompleteChain = new(() => GenerateScript());
    private static readonly Lazy<string> OriginalChain = new(() => GenerateScript(OriginalLastMigration));
    private static readonly Lazy<IModel> FrozenModel = new(() => new CP6ContextModelSnapshot().Model);

    // The unchanged d6074aaa snapshot is the definition oracle, independently
    // checked against these 21 native-catalog omissions from Issue #139.
    public static TheoryData<string, string, string[]> MissingIndexes => new()
    {
        { "T_Order", "IX_T_Order_OrderType_IsDeleted", ["OrderType", "IsDeleted"] },
        { "T_Order", "IX_T_Order_Status_IsDeleted", ["Status", "IsDeleted"] },
        { "T_OrderDetail", "IX_T_OrderDetail_ApprovalStatus_IsDeleted", ["ApprovalStatus", "IsDeleted"] },
        { "T_OrderDetail", "IX_T_OrderDetail_CustomerDeliveryDate_IsDeleted", ["CustomerDeliveryDate", "IsDeleted"] },
        { "T_OrderDetail", "IX_T_OrderDetail_HaibaiNo2_HaibaiNo3", ["HaibaiNo2", "HaibaiNo3"] },
        { "T_OrderDetail", "IX_T_OrderDetail_ItemCd", ["ItemCd"] },
        { "T_OrderDetail", "IX_T_OrderDetail_McTransferFlg_IsDeleted", ["McTransferFlg", "IsDeleted"] },
        { "T_OrderDetail", "IX_T_OrderDetail_ProductCatBig_ProductCatMid_ProductCatSml", ["ProductCatBig", "ProductCatMid", "ProductCatSml"] },
        { "T_OrderMaterial", "IX_T_OrderMaterial_WebOrderNo_WebOrderDetailNo_SortOrder", ["WebOrderNo", "WebOrderDetailNo", "SortOrder"] },
        { "T_OrderProcess", "IX_T_OrderProcess_ProcessCd", ["ProcessCd"] },
        { "T_OrderProcess", "IX_T_OrderProcess_WebOrderNo_WebOrderDetailNo_SortOrder", ["WebOrderNo", "WebOrderDetailNo", "SortOrder"] },
        { "T_PlateMold", "IX_T_PlateMold_BaseCd_IsDeleted", ["BaseCd", "IsDeleted"] },
        { "T_PlateMold", "IX_T_PlateMold_PlaceCd", ["PlaceCd"] },
        { "T_PlateMold", "IX_T_PlateMold_ProcessCd", ["ProcessCd"] },
        { "T_PlateMold", "IX_T_PlateMold_RepresentativeProductCd", ["RepresentativeProductCd"] },
        { "T_PlateMold", "IX_T_PlateMold_StDate_EndDate", ["StDate", "EndDate"] },
        { "T_PlateMold", "IX_T_PlateMold_TypeClass", ["TypeClass"] },
        { "T_SheetUnitPrice", "IX_T_SheetUnitPrice_BaseCd_CustomerCd_IsDeleted", ["BaseCd", "CustomerCd", "IsDeleted"] },
        { "T_SheetUnitPrice", "IX_T_SheetUnitPrice_RevisionDate", ["RevisionDate"] },
        { "T_SheetUnitPriceEstimate", "IX_T_SheetUnitPriceEstimate_BaseCd_CustomerCd_IsDeleted", ["BaseCd", "CustomerCd", "IsDeleted"] },
        { "T_SheetUnitPriceEstimate", "IX_T_SheetUnitPriceEstimate_RevisionDate", ["RevisionDate"] }
    };

    [Theory]
    [MemberData(nameof(MissingIndexes))]
    public void Complete_sql_chain_installs_each_frozen_nonunique_query_index(string table, string name, string[] columns)
    {
        AssertFrozenDefinition(table, name, columns);
        var expected = $"CREATE NONCLUSTERED INDEX [{name}] ON [dbo].[{table}] ({string.Join(", ", columns.Select(column => $"[{column}] ASC"))})";
        Assert.Contains(expected, Regex.Replace(CompleteChain.Value, @"\s+", " "), StringComparison.Ordinal);
    }

    [Fact]
    public void Original_136_chain_does_not_install_any_of_the_21_omissions()
    {
        using var context = Context();
        var migrations = context.GetService<IMigrationsAssembly>().Migrations;
        Assert.Equal(136, migrations.Keys.Count(id => string.CompareOrdinal(id, OriginalLastMigration) <= 0));
        foreach (var row in MissingIndexes)
        {
            var name = (string)row[1];
            Assert.DoesNotMatch($@"CREATE\s+(?:NONCLUSTERED\s+)?INDEX\s+\[{Regex.Escape(name)}\]", OriginalChain.Value);
        }
    }

    [Fact]
    public void Forward_repair_is_discovered_and_contains_only_21_transactional_sql_operations()
    {
        using var context = Context();
        var assembly = context.GetService<IMigrationsAssembly>();
        Assert.Contains(RepairMigration, assembly.Migrations.Keys);
        var migration = assembly.CreateMigration(assembly.Migrations[RepairMigration], "Microsoft.EntityFrameworkCore.SqlServer");
        Assert.Equal(21, migration.UpOperations.Count);
        Assert.All(migration.UpOperations, operation => Assert.False(Assert.IsType<SqlOperation>(operation).SuppressTransaction));
        Assert.DoesNotContain("CREATE UNIQUE", CompleteChain.Value[(CompleteChain.Value.IndexOf("DECLARE @cp6_object_id", StringComparison.Ordinal))..], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Same_name_guard_checks_the_complete_definition_without_destructive_ddl()
    {
        var statements = OrderModelIndexRepairSql.CreateStatements();
        Assert.Equal(21, statements.Count);
        Assert.All(statements, sql =>
        {
            foreach (var required in new[] { "i.type = 2", "i.is_unique = 0", "i.has_filter = 0", "i.filter_definition IS NULL",
                "i.is_disabled = 0", "i.is_hypothetical = 0", "i.ignore_dup_key = 0", "ic.key_ordinal > 0",
                "ic.is_included_column = 1", "ic.is_descending_key <> 0", "expected.key_ordinal", "c.name COLLATE Latin1_General_100_BIN2", "THROW 51039" })
                Assert.Contains(required, sql, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"\b(?:DROP|REBUILD|DISABLE|DELETE|UPDATE)\b", sql);
        });
        Assert.DoesNotContain(statements, sql => sql.Contains("IX_T_Order_WebOrderNo", StringComparison.Ordinal));
        Assert.DoesNotContain(statements, sql => sql.Contains("UX_OrderDetail_OrderProduct", StringComparison.Ordinal));
    }

    [Fact]
    public void Repair_rejects_other_providers_before_creating_operations_and_rejects_down()
    {
        using var context = Context();
        var assembly = context.GetService<IMigrationsAssembly>();
        var type = assembly.Migrations[RepairMigration];
        var wrongProvider = assembly.CreateMigration(type, "Npgsql.EntityFrameworkCore.PostgreSQL");
        Assert.Throws<NotSupportedException>(() => wrongProvider.UpOperations);
        var sqlServer = assembly.CreateMigration(type, "Microsoft.EntityFrameworkCore.SqlServer");
        Assert.Throws<NotSupportedException>(() => sqlServer.DownOperations);
    }

    private static void AssertFrozenDefinition(string table, string name, string[] columns)
    {
        var entity = Assert.Single(FrozenModel.Value.GetEntityTypes(), entity => entity.GetTableName() == table);
        var index = Assert.Single(entity.GetIndexes(), index => index.GetDatabaseName() == name);
        Assert.False(index.IsUnique);
        Assert.Null(index.GetFilter());
        Assert.Null(index.FindAnnotation("SqlServer:Include"));
        Assert.True(index.IsDescending is null || index.IsDescending.All(descending => !descending));
        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
        Assert.Equal(columns, index.Properties.Select(property => property.GetColumnName(store)));
    }

    private static string GenerateScript(string? target = null)
    {
        using var context = Context();
        return context.GetService<IMigrator>().GenerateScript(toMigration: target);
    }

    private static CP6Context Context() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseSqlServer("Server=database.cp6.test;Database=fixture;Integrated Security=True;MultipleActiveResultSets=False").Options);
}
