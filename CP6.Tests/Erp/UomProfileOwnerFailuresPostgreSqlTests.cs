using System.Data.Common;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class InterruptUomCommit : DbTransactionInterceptor
    {
        internal bool Interrupt;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Interrupt) { Interrupt = false; throw new InvalidOperationException("Injected local pre-commit interruption"); }
            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }
    private static CommercialProductUnitOwnerV1 ProductUnits(Harness h)
        => new(h.Db, UnitAuthority(h));
    private static async Task GrantProductUnitEdit(Harness h)
    {
        await GrantProductIdentity(h);
        var menu = await h.Db.Sys_Menus.SingleAsync(row => row.MenuKey == "erp-product");
        if (!await h.Db.Sys_MenuActions.AnyAsync(row => row.MenuId == menu.MenuId && row.ActionCode == "edit"))
            h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "edit", ActionName = "Edit" });
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "edit" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }

    [CurrentPermissionPostgresFact]
    public async Task UomProfile_commit_failure_rolls_back_version_binding_audit_and_original_together()
    {
        var failure = new InterruptUomCommit();
        await using var h = await Seed(failure); await GrantUomProfile(h); var owner = Units(h);
        var admin = await MappingAdmin(h);
        var command = await NewUnit(owner, department: admin.Child);
        failure.Interrupt = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(command))).Message);
        Assert.Empty(await owner.ListAsync(null));
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(command.OperationId, false)).State);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_operations_owner_v1.department_mappings
            WHERE tenant_id={h.Tenant} AND object_kind='uom-profile' AND object_id={command.ProfileId}
            """).SingleAsync());
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_main_owner_v1.operations
            WHERE tenant_id={h.Tenant} AND owner='Main.Erp.UomProfile.v1' AND operation_id={command.OperationId}
            """).SingleAsync());
        Assert.Equal("CLOSED", (await owner.OperationAsync(command.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(command))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task UomProfile_competing_native_CAS_writers_commit_one_successor_and_one_original()
    {
        await using var h = await Seed(); await GrantUomProfile(h); var owner = Units(h);
        var original = await MakeUnit(owner);
        await using var other = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var peer = Units(h, other);
        async Task<string> Save(UomProfileOwnerV1 service, string name)
        {
            try { await service.CommandAsync(Revise(original, original.Profile with { Name = name })); return "COMMITTED"; }
            catch (OrderPriceCorrectionException error) { Assert.Equal(412, error.Status); return error.Message; }
        }
        var results = await Task.WhenAll(Save(owner, "Writer A"), Save(peer, "Writer B"));
        Assert.Single(results, result => result == "COMMITTED"); Assert.Single(results, result => result == "UOM_VERSION_CHANGED");
        Assert.Equal("2", (await owner.ReadAsync(original.Reference.Id, null)).Reference.Version);
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_main_owner_v1.operations
            WHERE tenant_id={h.Tenant} AND owner='Main.Erp.UomProfile.v1' AND state='COMMITTED'
            """).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task UomProfile_product_adoption_has_no_legacy_code_fallback_and_requires_both_current_authorities()
    {
        await using var h = await Seed(); await GrantUomProfile(h); await GrantProductUnitEdit(h);
        var units = Units(h); var each = await MakeUnit(units); var product = await SeedIdentityProduct(h);
        var source = await ProductIdentity(h).ReadAsync(product); var owner = ProductUnits(h);
        var unconfigured = new CommercialProductUnitCommandV1 { OperationId = Guid.NewGuid(), ProductId = product,
            ExpectedBindingVersion = "absent", ProductVersion = source.Version, ProfileRef = each.Reference };
        Assert.Equal("PRODUCT_UNIT_CODE_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.BindAsync(unconfigured))).Message);
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(unconfigured.OperationId, false)).State);
        await h.Db.ProductMasters.Where(row => row.Id == product).ExecuteUpdateAsync(update => update.SetProperty(row => row.QtyUnit, "EA"));
        source = await ProductIdentity(h).ReadAsync(product);
        var command = unconfigured with { OperationId = Guid.NewGuid(), ProductVersion = source.Version };
        var receipt = await owner.BindAsync(command); Assert.Equal(receipt, await owner.BindAsync(command));
        var current = await owner.ReadAsync(product, null); Assert.True(current.CurrentApplicable);
        Assert.Equal(each.Reference, current.ProfileRef); Assert.Equal("REQUIRED_FENCED", current.FormalAdoption);
        Assert.Contains("DUAL_OWNER_ENGINEERING_MAPPING", current.Missing!);
        await units.CommandAsync(Revise(each, each.Profile with { Enabled = false }));
        Assert.Equal("PRODUCT_UNIT_BINDING_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(product, null))).Message);
        var historical = await owner.ReadAsync(product, "1"); Assert.False(historical.CurrentApplicable);
        Assert.Equal(current.OriginalJson, historical.OriginalJson);
        Assert.Equal(receipt, (await owner.OperationAsync(command.OperationId, false)).Result);
        var menu = await h.Db.Sys_Menus.Where(row => row.MenuKey == "erp-uom-profile").Select(row => row.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(row => row.MenuId == menu && row.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(product, "1"))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(command.OperationId, false))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task UomProfile_native_sources_reject_another_Owner_context_or_an_unheld_store_fence()
    {
        await using var h = await Seed(); await GrantUomProfile(h); await GrantProductUnitEdit(h);
        var units = Units(h); var profile = await MakeUnit(units); var product = await SeedIdentityProduct(h);
        var authority = UnitAuthority(h);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => authority.ExecuteAsync(
            OperationsOwnerV1.UomProfile, "query", false, (access, token) => CommercialProductIdentityReader.ReadOwnedAsync(h.Db, access, product, null, token)))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => authority.ExecuteAsync(
            OperationsOwnerV1.Product, "query", false, async (access, token) =>
            { var store = new UomProfileStoreV1(h.Db); await store.Fence(false, token); return await UomProfileOwnerV1.CurrentExact(access, store, profile.Reference, token); }))).Status);
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => authority.ExecuteAsync(
            OperationsOwnerV1.Product, "query", false, (access, token) => CommercialProductIdentityReader.ReadOwnedAsync(peer, access, product, null, token)))).Status);
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => authority.ExecuteAsync(
            OperationsOwnerV1.UomProfile, "query", false, (_, token) => new UomProfileStoreV1(h.Db).Read(profile.Reference.Id, null, token)))).Status);
    }

    [TenantManagementPostgresTheory]
    [InlineData(13)] [InlineData(18)] [InlineData(1024)]
    public async Task UomProfile_native_precision_is_persisted_and_consumed_without_CLR_decimal_limits(int scale)
    {
        await using var h = await Seed(); await GrantUomProfile(h); var owner = Units(h);
        var profile = await MakeUnit(owner, BaseUnit(scale: scale));
        var quantity = "0." + new string('0', scale - 1) + "1";
        Assert.Equal(quantity, (await owner.ConvertAsync(new(profile.Reference, profile.Reference, quantity))).ConvertedQuantity);
        Assert.Equal(scale, (await owner.ReadAsync(profile.Reference.Id, "1")).Profile.QuantityScale);
    }

    [CurrentPermissionPostgresFact]
    public async Task UomProfile_forward_migration_preserves_existing_codes_max_menu_ID_and_requires_explicit_grants()
    {
        await using var h = await Seed();
        var settings = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { Database = "postgres" };
        var databaseName = "uom_forward_" + Guid.NewGuid().ToString("N");
        await using (var root = new NpgsqlConnection(settings.ConnectionString))
        {
            await root.OpenAsync();
            // Identifier consists solely of this fixed prefix and fresh lowercase hexadecimal digits.
            await using var command = new NpgsqlCommand("CREATE DATABASE " + databaseName, root); await command.ExecuteNonQueryAsync();
        }
        settings.Database = databaseName;
        var database = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var profile = DatabaseMigrationProfile.For(database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), database, settings.ConnectionString,
            profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        await using var db = new CP6Context(options.Options, new TenantContext());
        await db.GetService<IMigrator>().MigrateAsync("20261026130000_CommercialProductIdentityV1");
        db.Sys_Menus.Add(new() { MenuId = int.MaxValue, MenuKey = "historical-max-id", MenuName = "Original menu" });
        db.MasterGenericCodes.Add(new() { Code = "EA", GroupCode = "Unit", Name = "Original unit code" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.Database.MigrateAsync();
        Assert.Equal("Original menu", await db.Sys_Menus.Where(row => row.MenuId == int.MaxValue).Select(row => row.MenuName).SingleAsync());
        Assert.Equal("Original unit code", await db.MasterGenericCodes.Where(row => row.GroupCode == "Unit" && row.Code == "EA").Select(row => row.Name).SingleAsync());
        var menuId = await db.Sys_Menus.Where(row => row.MenuKey == "erp-uom-profile").Select(row => row.MenuId).SingleAsync();
        Assert.NotEqual(int.MaxValue, menuId);
        Assert.False(await db.Sys_RoleActions.AnyAsync(row => row.MenuId == menuId));
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM cp6_uom_profile_v1.heads").SingleAsync());
    }
}
