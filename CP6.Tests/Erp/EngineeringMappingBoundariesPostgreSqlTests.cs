using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Entity.DomainModels.Space;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task EngineeringMapping_business_ABA_invalidates_adoption_and_current_revocation_protects_history_and_recovery()
    {
        await using var h = await Seed(); await GrantMappingRead(h); await EngineeringAction(h, "erp-product", "confirm-engineering");
        var service = EngineeringMappings(h); var candidate = await MappingCandidate(h, await MakeEngineering(h));
        var draft = await MakeMapping(h, candidate); var command = MappingAction(draft, "CONFIRM"); await service.CommandAsync(command);
        var confirmed = await service.ReadAsync(draft.Reference.Id, null);
        var code = await h.Db.ProductMasters.Where(row => row.Id == candidate.BusinessRef!.Id).Select(row => row.ProductCd).SingleAsync();
        await h.Db.ProductMasters.Where(row => row.Id == candidate.BusinessRef!.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.ProductCd, "changed"));
        await h.Db.ProductMasters.Where(row => row.Id == candidate.BusinessRef!.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.ProductCd, code));
        Assert.Equal("ENGINEERING_BUSINESS_SOURCE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => ConsumeMapping(h, confirmed.Reference))).Message);
        Assert.Equal(confirmed.OriginalJson, (await service.ReadAsync(confirmed.Reference.Id, confirmed.Reference.Version)).OriginalJson);
        Assert.Equal("COMMITTED", (await service.OperationAsync(command.OperationId, false)).State);
        var menu = await h.Db.Sys_Menus.Where(row => row.MenuKey == "erp-product").Select(row => row.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(row => row.MenuId == menu && row.RoleId == 72 && row.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ReadAsync(confirmed.Reference.Id, "1"))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.OperationAsync(command.OperationId, false))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CommandAsync(command))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringMapping_actual_Site_is_tenant_bound_and_missing_source_capture_fails_closed()
    {
        await using var h = await Seed(); await GrantMappingRead(h); var engineering = await MakeEngineering(h);
        await using var foreign = await Seed(); await GrantMappingRead(foreign); var foreignId = await PhysicalSite(foreign, "FOREIGN");
        var foreignRef = Assert.Single(await EngineeringMappings(foreign).SiteChoicesAsync("FOREIGN")).Reference;
        var service = EngineeringMappings(h); Assert.Empty(await service.SiteChoicesAsync("FOREIGN"));
        Assert.Equal("PHYSICAL_SITE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CommandAsync(MappingDraft(new() { EngineeringRef = engineering.Reference, SiteRef = foreignRef })))).Message);
        var id = await PhysicalSite(h, "OWN"); Assert.NotEqual(id, foreignId);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Space_Site\" DISABLE TRIGGER physical_site_identity_capture");
        try { Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SiteChoicesAsync("OWN"))).Status); }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Space_Site\" ENABLE TRIGGER physical_site_identity_capture"); }
        Assert.Single(await service.SiteChoicesAsync("OWN"));
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringMapping_database_rejects_missing_state_without_publishing_a_new_head()
    {
        await using var h = await Seed(); await GrantEngineering(h); var service = EngineeringMappings(h);
        var draft = await MakeMapping(h, new() { EngineeringRef = (await MakeEngineering(h)).Reference });
        await using (var transaction = await h.Db.Database.BeginTransactionAsync())
        {
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_engineering_product_v1.mapping_heads SET revision=2 WHERE tenant_id={h.Tenant} AND mapping_id={draft.Reference.Id}");
            var error = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_engineering_product_v1.mapping_versions(tenant_id,mapping_id,revision,body_bytes,body_digest)
                SELECT tenant_id,mapping_id,2,body,encode(sha256(body),'hex') FROM (
                  SELECT tenant_id,mapping_id,convert_to((jsonb_set(convert_from(body_bytes,'UTF8')::jsonb-'state',ARRAY['version'],'"2"'::jsonb))::text,'UTF8') body
                  FROM cp6_engineering_product_v1.mapping_versions WHERE tenant_id={h.Tenant} AND mapping_id={draft.Reference.Id} AND revision=1) source
                """));
            Assert.Equal("23514", error.SqlState); await transaction.RollbackAsync();
        }
        Assert.Equal(draft.Reference, (await service.ReadAsync(draft.Reference.Id, null)).Reference);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringMapping_forward_migration_records_original_Site_once_and_grants_no_Owner_actions()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() }; await using var db = server.Context(tenant);
        var name = "w150_engineering_" + Guid.NewGuid().ToString("N");
        await db.Database.ExecuteSqlRawAsync($"CREATE DATABASE {name}");
        db.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { Database = name }.ConnectionString);
        await db.GetService<IMigrator>().MigrateAsync("20261026180000_CurrencyPolicyOwnerV1");
        var site = new Space_Site { SiteCode = "ORIGINAL", SiteName = "Original physical Site", Creator = "original", Enable = false };
        db.Space_Sites.Add(site); db.Sys_Menus.Add(new() { MenuId = int.MaxValue, MenuKey = "historical-max-id", MenuName = "Original menu" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var json = await db.Database.SqlQuery<string>($"""
            SELECT source_json AS "Value" FROM cp6_physical_site_identity_v1.versions WHERE tenant_id={tenant.CurrentTenantId} AND site_id={site.Id} AND revision=1
            """).SingleAsync();
        using var original = JsonDocument.Parse(json); Assert.Equal(site.Id, original.RootElement.GetProperty("siteId").GetGuid()); Assert.False(original.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_physical_site_identity_v1.versions").SingleAsync());
        Assert.Equal("Original physical Site", await db.Space_Sites.Where(row => row.Id == site.Id).Select(row => row.SiteName).SingleAsync());
        var menuId = await db.Sys_Menus.Where(row => row.MenuKey == "plm-engineering-product").Select(row => row.MenuId).SingleAsync();
        Assert.NotEqual(int.MaxValue, menuId); Assert.False(await db.Sys_RoleActions.AnyAsync(row => row.MenuId == menuId || row.ActionCode == "confirm-engineering"));
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_engineering_product_v1.heads").SingleAsync());
    }
}
