using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static CommercialProductIdentityReader ProductIdentity(Harness h)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        catalog.RegisterScope("product", "Product", [1, 2, 3, 4, 5], 5); catalog.RegisterFields("product", []); catalog.Seal();
        return new(h.Db, new OperationsOwnerV1(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture));
    }
    private static async Task GrantProductIdentity(Harness h)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == "erp-product");
        if (menu == null) { menu = new() { MenuId = 9144, MenuKey = "erp-product", MenuName = "Product" }; h.Db.Sys_Menus.Add(menu); }
        if (!await h.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == menu.MenuId && x.ActionCode == "query"))
            h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "query", ActionName = "Query" });
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "query" });
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "product", ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }

    private static async Task<Guid> SeedIdentityProduct(Harness h, string code = "P-NATIVE", string creator = "price-owner")
    {
        // The existing alternate key is globally unique; this task does not migrate that legacy contract.
        code = code[..Math.Min(code.Length, 6)] + "-" + Guid.NewGuid().ToString("N")[..8];
        var product = new ProductMaster { ProductCd = code, ItemCd = code, CustomerCd = "C1", SetProductCd = code,
            SetProductName = "Same label", Creator = creator, QtyUnit = null, Status = 1 };
        h.Db.ProductMasters.Add(product); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return product.Id;
    }

    [CurrentPermissionPostgresFact]
    public async Task Commercial_product_identity_actual_http_preserves_native_identity_without_inventing_qualification()
    {
        await using var h = await Seed(); await GrantProductIdentity(h); var id = await SeedIdentityProduct(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/products/identity-owner-v1/{id:D}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var data = doc.RootElement.GetProperty("data");
        Assert.Equal(id, data.GetProperty("productId").GetGuid()); Assert.Equal("1", data.GetProperty("version").GetString());
        Assert.Equal("REQUIRED_FENCED", data.GetProperty("formalAdoption").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("source").GetProperty("qtyUnitCode").ValueKind);
        await h.Db.Sys_RoleActions.Where(x => x.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/products/identity-owner-v1/{id:D}?version=1"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.True(denied.Headers.CacheControl?.NoStore);
    }

    [CurrentPermissionPostgresFact]
    public async Task Commercial_product_identity_native_versions_survive_ABA_soft_delete_and_exact_history_reads()
    {
        await using var h = await Seed(); await GrantProductIdentity(h); var id = await SeedIdentityProduct(h);
        var reader = ProductIdentity(h); var original = await reader.ReadAsync(id);
        await h.Db.ProductMasters.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.QtyUnit, "BOX"));
        var second = await reader.ReadAsync(id); Assert.Equal("2", second.Version); Assert.Equal("BOX", second.Source.GetProperty("qtyUnitCode").GetString());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_ProductMaster\" SET \"QtyUnit\"=NULL WHERE \"Id\"={id}");
        var third = await reader.ReadAsync(id); Assert.Equal("3", third.Version); Assert.Equal(original.Digest, third.Digest);
        var historical = await reader.ReadAsync(id, "1"); Assert.False(historical.IsCurrent); Assert.Equal(original.OriginalJson, historical.OriginalJson);
        await h.Db.ProductMasters.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
        Assert.True((await reader.ReadAsync(id)).Source.GetProperty("isDeleted").GetBoolean());
        Assert.Equal(original.OriginalJson, (await reader.ReadAsync(id, "1")).OriginalJson);
        Assert.Equal("PRODUCT_IDENTITY_VERSION_NOT_FOUND", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => reader.ReadAsync(id, "9"))).Message);
        foreach (var invalid in new[] { "0", "01", "+1", "1 ", "9223372036854775808" })
            Assert.Equal("PRODUCT_IDENTITY_INPUT_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => reader.ReadAsync(id, invalid))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Commercial_product_identity_legacy_service_writes_and_copies_use_the_original_master_and_distinct_ids()
    {
        await using var h = await Seed(); await GrantProductIdentity(h);
        var service = new ProductService(h.Db);
        var code = await service.CreateAsync(new ProductDto { BasicInfo = new() { CustomerCd = "C", SetProductName = "Equal labels", QtyUnit = "BOX" } }, "price-owner");
        h.Db.ChangeTracker.Clear(); var id = await h.Db.ProductMasters.Where(x => x.ProductCd == code).Select(x => x.Id).SingleAsync();
        var reader = ProductIdentity(h); Assert.Equal("BOX", (await reader.ReadAsync(id)).Source.GetProperty("qtyUnitCode").GetString());
        var dto = (await service.GetByCdAsync(code))!; dto.BasicInfo.QtyUnit = "KG";
        await service.UpdateAsync(code, dto, "price-owner"); h.Db.ChangeTracker.Clear();
        Assert.Equal("2", (await reader.ReadAsync(id)).Version); Assert.Equal("BOX", (await reader.ReadAsync(id, "1")).Source.GetProperty("qtyUnitCode").GetString());
        var copied = await service.CopyAsync(code, "price-owner"); h.Db.ChangeTracker.Clear();
        var copyId = await h.Db.ProductMasters.Where(x => x.ProductCd == copied).Select(x => x.Id).SingleAsync();
        Assert.NotEqual(id, copyId); Assert.NotEqual(code, copied); Assert.Equal("1", (await reader.ReadAsync(copyId)).Version);
    }

    [CurrentPermissionPostgresFact]
    public async Task Commercial_product_identity_source_and_frontier_rollback_together_and_originals_reject_rewriting()
    {
        await using var h = await Seed(); await GrantProductIdentity(h); var id = await SeedIdentityProduct(h);
        var reader = ProductIdentity(h); var original = await reader.ReadAsync(id);
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            await h.Db.ProductMasters.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, 9));
            await tx.RollbackAsync();
        }
        var rolledBack = await reader.ReadAsync(id); Assert.Equal(original.Version, rolledBack.Version); Assert.Equal(original.OriginalJson, rolledBack.OriginalJson);
        foreach (var sql in new[] {
            "UPDATE cp6_product_identity_v1.versions SET source_present=false",
            "DELETE FROM cp6_product_identity_v1.versions",
            "DELETE FROM cp6_product_identity_v1.heads",
            "UPDATE cp6_product_identity_v1.heads SET revision=1",
            "TRUNCATE public.\"T_ProductMaster\" CASCADE" })
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql))).SqlState);
        Assert.Equal(original.OriginalJson, (await reader.ReadAsync(id)).OriginalJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task Commercial_product_identity_hard_delete_and_reinsert_keep_history_and_never_reuse_a_version()
    {
        await using var h = await Seed(); await GrantProductIdentity(h); var id = await SeedIdentityProduct(h);
        var reader = ProductIdentity(h); var original = await reader.ReadAsync(id);
        var native = await h.Db.ProductMasters.AsNoTracking().SingleAsync(x => x.Id == id);
        await h.Db.ProductMasters.Where(x => x.Id == id).ExecuteDeleteAsync();
        Assert.Equal("OBJECT_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => reader.ReadAsync(id, "1"))).Message);
        Assert.False(await h.Db.Database.SqlQuery<bool>($"""
            SELECT source_present AS "Value" FROM cp6_product_identity_v1.versions
            WHERE tenant_id={h.Tenant} AND product_id={id} AND revision=2
            """).SingleAsync());
        h.Db.ProductMasters.Add(native); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal("3", (await reader.ReadAsync(id)).Version); Assert.False((await reader.ReadAsync(id, "2")).SourcePresent);
        Assert.Equal(original.OriginalJson, (await reader.ReadAsync(id, "1")).OriginalJson);
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Commercial_product_identity_department_scope_requires_real_binding_and_rechecks_current_mapping(int scope)
    {
        await using var h = await Seed(); await GrantProductIdentity(h); var id = await SeedIdentityProduct(h);
        var admin = await MappingAdmin(h); await StandaloneScope(h, admin, scope); var reader = ProductIdentity(h);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => reader.ReadAsync(id));
        var code = await h.Db.ProductMasters.Where(p => p.Id == id).Select(p => p.ProductCd).SingleAsync();
        Assert.Equal(id, Assert.Single(await StandaloneAdmin(h, admin).FindObjectsAsync("product-master", code, _ => Task.FromResult(h.User), default)).Id);
        var binding = await BindStandalone(h, admin, "product-master", id);
        Assert.Equal(id, (await reader.ReadAsync(id)).ProductId);
        var disable = await StandaloneCommand(h, admin, "product-master", id, binding.EntityId);
        await SaveStandalone(h, admin, disable with { Enabled = false });
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => reader.ReadAsync(id, "1"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Commercial_product_identity_isolated_tenants_creator_scope_and_required_source_cannot_be_bypassed()
    {
        await using var h = await Seed(); await GrantProductIdentity(h); var own = await SeedIdentityProduct(h);
        var hidden = await SeedIdentityProduct(h, "P-OTHER", "someone-else");
        await using var foreign = await Seed(); var foreignId = await SeedIdentityProduct(foreign);
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "product").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        var reader = ProductIdentity(h); Assert.Equal(own, (await reader.ReadAsync(own)).ProductId);
        foreach (var id in new[] { hidden, foreignId }) await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => reader.ReadAsync(id, "1"));
        var tracked = await h.Db.ProductMasters.SingleAsync(x => x.Id == own);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => reader.ReadAsync(own));
        Assert.Same(tracked, h.Db.ChangeTracker.Entries<ProductMaster>().Single().Entity); h.Db.ChangeTracker.Clear();
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_ProductMaster\" DISABLE TRIGGER product_identity_capture");
        try { Assert.Equal("PRODUCT_IDENTITY_SOURCE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => reader.ReadAsync(own))).Message); }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_ProductMaster\" ENABLE TRIGGER product_identity_capture"); }
    }

    [CurrentPermissionPostgresFact]
    public async Task Commercial_product_identity_real_source_update_waits_for_current_read_transaction()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await GrantProductIdentity(h); var id = await SeedIdentityProduct(h);
        Task? writer = null; var observed = false;
        hook.After = async command =>
        {
            if (!command.CommandText.Contains("WITH field_subject")) return; hook.After = null;
            writer = Task.Run(async () => { await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_ProductMaster\" SET \"QtyUnit\"='KG' WHERE \"Id\"={id}"); });
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                  AND query LIKE 'UPDATE public."T_ProductMaster"%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            observed = true; Assert.False(writer.IsCompleted);
        };
        CommercialProductIdentityV1? before = null;
        try { before = await ProductIdentity(h).ReadAsync(id); } finally { if (writer != null) await writer; }
        Assert.True(observed); Assert.Equal("1", before!.Version); Assert.Equal("2", (await ProductIdentity(h).ReadAsync(id)).Version);
    }

    [CurrentPermissionPostgresFact]
    public async Task Commercial_product_identity_forward_migration_records_existing_native_state_without_fabricating_prior_history()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() }; await using var db = server.Context(tenant);
        var name = "w144_product_" + Guid.NewGuid().ToString("N"); await db.Database.ExecuteSqlRawAsync($"CREATE DATABASE {name}");
        db.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { Database = name }.ConnectionString);
        await db.GetService<IMigrator>().MigrateAsync("20261026114000_TransferLocalDemandBudgetV1");
        var native = new ProductMaster { ProductCd = "BEFORE", ItemCd = "BEFORE", CustomerCd = "C", SetProductCd = "BEFORE", QtyUnit = null, Status = 9 };
        db.ProductMasters.Add(native); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.Database.MigrateAsync(); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var json = await db.Database.SqlQuery<string>($"""
            SELECT source_json AS "Value" FROM cp6_product_identity_v1.versions WHERE tenant_id={tenant.CurrentTenantId} AND product_id={native.Id} AND revision=1
            """).SingleAsync();
        using var original = JsonDocument.Parse(json); Assert.Equal(9, original.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.Null, original.RootElement.GetProperty("qtyUnitCode").ValueKind);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_product_identity_v1.versions").SingleAsync());
    }
}
