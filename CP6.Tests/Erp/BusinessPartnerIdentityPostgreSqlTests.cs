using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 PartnerAuthority(Harness h, CP6Context? db = null)
    {
        var c = new AuthorizationCatalogStoreV1(); c.RegisterScope("erp-business-partner", "Native partner identity", [1, 2, 3, 4, 5], 1);
        c.RegisterFields("erp-business-partner", [new FieldRegistry.Field("BpName", "Name")]); c.Seal();
        return new(db ?? h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), c.Capture);
    }
    private static BusinessPartnerIdentityOwnerV1 Partners(Harness h) => new(h.Db, PartnerAuthority(h));
    private static async Task GrantBusinessPartnerIdentity(Harness h)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == "erp-business-partner");
        if (menu == null) { menu = new() { MenuId = 212, MenuKey = "erp-business-partner", MenuName = "Business partner" }; h.Db.Sys_Menus.Add(menu); }
        if (!await h.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == menu.MenuId && x.ActionCode == "query"))
            h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "query", ActionName = "Query" });
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = menu.MenuId, ActionCode = "query" });
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "erp-business-partner", ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }

    private static async Task<Guid> SeedIdentityPartner(Harness h, string creator = "price-owner")
    {
        var row = new BusinessPartner { BpCd = "C-" + Guid.NewGuid().ToString("N")[..12], BpName = "Original e\u0301 客户", BpAbbrev = "C", BaseCd = "B1",
            Status = 1, CustomerFlg = true, Creator = creator, CreditLimit = 123456.78m, CustomerContact = "Private contact" };
        h.Db.BusinessPartners.Add(row); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return row.Id;
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_identity_actual_http_reads_native_uuid_and_rechecks_revocation()
    {
        await using var h = await Seed(); await GrantBusinessPartnerIdentity(h); var id = await SeedIdentityPartner(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/business-partners/identity-owner-v1/{id:D}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var text = await response.Content.ReadAsStringAsync(); using var doc = JsonDocument.Parse(text); var data = doc.RootElement.GetProperty("data");
        Assert.Equal("Main.Erp.BusinessPartnerIdentity.v1", data.GetProperty("reference").GetProperty("owner").GetString());
        Assert.Equal(id, data.GetProperty("reference").GetProperty("id").GetGuid());
        Assert.Equal("1", data.GetProperty("reference").GetProperty("version").GetString());
        Assert.Equal("Original e\u0301 客户", data.GetProperty("source").GetProperty("bpName").GetString());
        Assert.Equal("NOT_ATTESTED", data.GetProperty("tradingEligibility").GetString());
        Assert.DoesNotContain("creditLimit", text, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("Private contact", text);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "erp-business-partner", FieldName = "BpName", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        using var list = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/business-partners/identity-owner-v1?customersOnly=true&pageSize=1"));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode); Assert.True(list.Headers.CacheControl?.NoStore);
        using var listDoc = JsonDocument.Parse(await list.Content.ReadAsStringAsync()); var item = listDoc.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("NAME_REDACTED", item.GetProperty("projection").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("source").GetProperty("bpName").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("originalJson").ValueKind);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 212 && x.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"api/business-partners/identity-owner-v1/{id:D}?version=1"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.True(denied.Headers.CacheControl?.NoStore);
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_identity_versions_preserve_codepoints_ABA_and_rollback_and_prohibit_history_rewriting()
    {
        await using var h = await Seed(); await GrantBusinessPartnerIdentity(h); var id = await SeedIdentityPartner(h); var owner = Partners(h);
        var first = await owner.ReadAsync(id); Assert.Equal("1", first.Reference.Version);
        await h.Db.BusinessPartners.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.BpName, "Original é 客户"));
        var second = await owner.ReadAsync(id); Assert.NotEqual(first.Reference.Digest, second.Reference.Digest);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_WebBusinessPartner\" SET \"BpName\"={"Original e\u0301 客户"} WHERE \"Id\"={id}");
        var third = await owner.ReadAsync(id); Assert.Equal("3", third.Reference.Version); Assert.Equal(first.Reference.Digest, third.Reference.Digest);
        Assert.Equal(first.OriginalJson, (await owner.ReadAsync(id, "1")).OriginalJson); Assert.False((await owner.ReadAsync(id, "1")).IsCurrent);
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            await h.Db.BusinessPartners.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsFrozen, true));
            await tx.RollbackAsync();
        }
        Assert.Equal(third.Reference, (await owner.ReadAsync(id)).Reference);
        foreach (var sql in new[] { "UPDATE cp6_business_partner_identity_v1.versions SET source_present=false", "DELETE FROM cp6_business_partner_identity_v1.versions",
            "DELETE FROM cp6_business_partner_identity_v1.heads", "UPDATE cp6_business_partner_identity_v1.heads SET revision=1",
            "TRUNCATE cp6_business_partner_identity_v1.versions CASCADE", "TRUNCATE public.\"T_WebBusinessPartner\" CASCADE" })
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql))).SqlState);
        foreach (var invalid in new[] { "0", "01", "+1", "1 ", "9223372036854775808" })
            Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(id, invalid))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(id, "9"))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_identity_legacy_service_create_update_delete_use_native_identity_and_preserve_uninvented_facts()
    {
        await using var h = await Seed(); await GrantBusinessPartnerIdentity(h);
        var svc = new BusinessPartnerService(h.Db);
        var dto = new BusinessPartnerDto { BpCd = "S-" + Guid.NewGuid().ToString("N")[..12], BpName = "Legacy original", BpAbbrev = "C", BaseCd = "B",
            CustomerFlg = true, AccountsReceivableCd = "AR", SalesStaffCd = "SA", BusinessStaffCd = "BS" };
        await svc.CreateAsync(dto, "price-owner", preRegister: true); h.Db.ChangeTracker.Clear();
        var id = await h.Db.BusinessPartners.Where(x => x.BpCd == dto.BpCd).Select(x => x.Id).SingleAsync(); var owner = Partners(h);
        var first = await owner.ReadAsync(id); Assert.Equal(0, first.Source.GetProperty("status").GetInt32());
        dto = (await svc.GetByCdAsync(dto.BpCd))!; dto.BpName = "Changed"; await svc.UpdateAsync(dto.BpCd, dto, "price-owner"); h.Db.ChangeTracker.Clear();
        var second = await owner.ReadAsync(id); Assert.Equal("2", second.Reference.Version); Assert.Equal("Changed", second.Source.GetProperty("bpName").GetString());
        await svc.DeleteAsync(dto.BpCd, null, "price-owner"); h.Db.ChangeTracker.Clear();
        var deleted = await owner.ReadAsync(id); Assert.True(deleted.Source.GetProperty("isDeleted").GetBoolean()); Assert.Equal(9, deleted.Source.GetProperty("status").GetInt32());
        Assert.Empty((await owner.ListAsync(new())).Items); Assert.Equal(first.OriginalJson, (await owner.ReadAsync(id, "1")).OriginalJson);
        Assert.Equal("NOT_ATTESTED", deleted.TradingEligibility);
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_identity_hard_delete_and_reinsert_never_reuses_history_or_discloses_absent_object()
    {
        await using var h = await Seed(); await GrantBusinessPartnerIdentity(h); var id = await SeedIdentityPartner(h); var owner = Partners(h);
        var first = await owner.ReadAsync(id); var native = await h.Db.BusinessPartners.AsNoTracking().SingleAsync(x => x.Id == id);
        await h.Db.BusinessPartners.Where(x => x.Id == id).ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(id, "1"))).Status);
        h.Db.BusinessPartners.Add(native); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal("3", (await owner.ReadAsync(id)).Reference.Version); Assert.False((await owner.ReadAsync(id, "2")).SourcePresent);
        Assert.Equal(first.OriginalJson, (await owner.ReadAsync(id, "1")).OriginalJson);
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Business_partner_identity_department_scope_uses_explicit_native_uuid_and_rechecks_disabled_binding(int type)
    {
        await using var h = await Seed(); await GrantBusinessPartnerIdentity(h); var id = await SeedIdentityPartner(h); var other = await SeedIdentityPartner(h);
        var admin = await MappingAdmin(h); await StandaloneScope(h, admin, type); var owner = Partners(h);
        Assert.Equal(0, (await owner.ListAsync(new())).Total);
        var code = await h.Db.BusinessPartners.Where(x => x.Id == id).Select(x => x.BpCd).SingleAsync();
        Assert.Equal(id, Assert.Single(await StandaloneAdmin(h, admin).FindObjectsAsync("business-partner", code, _ => Task.FromResult(h.User), default)).Id);
        var binding = await BindStandalone(h, admin, "business-partner", id);
        var page = await owner.ListAsync(new(PageSize: 1)); Assert.Equal(1, page.Total); Assert.Equal(id, Assert.Single(page.Items).Reference.Id);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(other));
        var disable = await StandaloneCommand(h, admin, "business-partner", id, binding.EntityId); await SaveStandalone(h, admin, disable with { Enabled = false });
        Assert.Equal(0, (await owner.ListAsync(new())).Total); await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(id, "1"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_identity_name_mask_applies_to_current_history_and_fenced_consumer_without_mutating_original()
    {
        await using var h = await Seed(); await GrantBusinessPartnerIdentity(h); var id = await SeedIdentityPartner(h); var owner = Partners(h); var first = await owner.ReadAsync(id);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "erp-business-partner", FieldName = "BpName", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        foreach (var version in new string?[] { null, "1" })
        {
            var hidden = await owner.ReadAsync(id, version); Assert.Equal(first.Reference, hidden.Reference); Assert.Null(hidden.OriginalJson);
            Assert.Equal(JsonValueKind.Null, hidden.Source.GetProperty("bpName").ValueKind); Assert.Equal("NAME_REDACTED", hidden.Projection);
        }
        var held = await PartnerAuthority(h).ExecuteAsync("erp-business-partner", "query", false,
            (a, t) => BusinessPartnerIdentityOwnerV1.HoldExactAsync(h.Db, a, first.Reference, t));
        Assert.Null(held.OriginalJson); Assert.Equal(JsonValueKind.Null, held.Source.GetProperty("bpName").ValueKind);
        await h.Db.Sys_RoleFieldPerms.Where(x => x.ResourceKey == "erp-business-partner").ExecuteDeleteAsync();
        Assert.Equal(first.OriginalJson, (await owner.ReadAsync(id, "1")).OriginalJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_identity_current_creator_tenant_action_and_context_cannot_be_bypassed()
    {
        await using var h = await Seed(); await GrantBusinessPartnerIdentity(h); var id = await SeedIdentityPartner(h); var other = await SeedIdentityPartner(h, "other");
        await using var foreign = await Seed(); var foreignId = await SeedIdentityPartner(foreign);
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "erp-business-partner").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        var owner = Partners(h); Assert.Equal(id, Assert.Single((await owner.ListAsync(new())).Items).Reference.Id);
        foreach (var denied in new[] { other, foreignId }) Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(denied, "1"))).Status);
        var tracked = await h.Db.BusinessPartners.SingleAsync(x => x.Id == id);
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(id))).Status); Assert.Same(tracked, h.Db.ChangeTracker.Entries<BusinessPartner>().Single().Entity);
        h.Db.ChangeTracker.Clear(); await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new BusinessPartnerIdentityOwnerV1(peer, PartnerAuthority(h)).ReadAsync(id))).Status);
        var expected = (await owner.ReadAsync(id)).Reference;
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => PartnerAuthority(h).ExecuteAsync("erp-business-partner", "query", false,
            (a, t) => BusinessPartnerIdentityOwnerV1.HoldExactAsync(peer, a, expected, t)))).Status);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 212 && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ListAsync(new()))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_identity_fence_blocks_real_native_writer_and_rejects_stale_or_same_transaction_changes()
    {
        await using var h = await Seed(); await GrantBusinessPartnerIdentity(h); var id = await SeedIdentityPartner(h); var owner = Partners(h); var first = await owner.ReadAsync(id);
        Task<int>? writer = null; var waited = false;
        try
        {
            await PartnerAuthority(h).ExecuteAsync("erp-business-partner", "query", false, async (a, t) =>
            {
                await BusinessPartnerIdentityOwnerV1.HoldExactAsync(h.Db, a, first.Reference, t);
                writer = Task.Run(async () => { await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
                    return await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_WebBusinessPartner\" SET \"IsFrozen\"=true WHERE \"Id\"={id}"); });
                await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                while (!await observer.Database.SqlQueryRaw<bool>("""
                    SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                      AND query LIKE 'UPDATE public."T_WebBusinessPartner"%') AS "Value"
                    """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
                waited = true; Assert.False(writer.IsCompleted); return true;
            });
        }
        finally { if (writer != null) await writer; }
        Assert.True(waited); var next = await owner.ReadAsync(id); Assert.True(next.Source.GetProperty("isFrozen").GetBoolean());
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => PartnerAuthority(h).ExecuteAsync("erp-business-partner", "query", false,
            (a, t) => BusinessPartnerIdentityOwnerV1.HoldExactAsync(h.Db, a, first.Reference, t)))).Status);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => PartnerAuthority(h).ExecuteAsync("erp-business-partner", "query", true, async (a, t) =>
        {
            await BusinessPartnerIdentityOwnerV1.HoldExactAsync(h.Db, a, next.Reference, t);
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"T_WebBusinessPartner\" SET \"IsFrozen\"=false WHERE \"Id\"={id}", t); return true;
        }))).Status);
        Assert.Equal(next.Reference, (await owner.ReadAsync(id)).Reference);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => PartnerAuthority(h).ExecuteAsync("erp-business-partner", "query", true, async (a, t) =>
        {
            await BusinessPartnerIdentityOwnerV1.HoldExactAsync(h.Db, a, next.Reference, t);
            await h.Db.Sys_RoleActions.Where(x => x.MenuId == 212 && x.ActionCode == "query").ExecuteDeleteAsync(t); return true;
        }))).Status);
        Assert.Equal(next.Reference, (await owner.ReadAsync(id)).Reference);
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_identity_missing_capture_or_native_drift_is_unavailable_even_for_history()
    {
        await using var h = await Seed(); await GrantBusinessPartnerIdentity(h); var id = await SeedIdentityPartner(h); var owner = Partners(h);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_WebBusinessPartner\" DISABLE TRIGGER business_partner_identity_capture");
        try
        {
            Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(id, "1"))).Status);
            await h.Db.BusinessPartners.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsFrozen, true));
        }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"T_WebBusinessPartner\" ENABLE TRIGGER business_partner_identity_capture"); }
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(id, "1"))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_identity_forward_migration_observes_existing_rows_once_without_role_grants()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() }; await using var db = server.Context(tenant);
        var name = "w163_partner_" + Guid.NewGuid().ToString("N"); await db.Database.ExecuteSqlRawAsync($"CREATE DATABASE {name}");
        db.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { Database = name }.ConnectionString);
        await db.GetService<IMigrator>().MigrateAsync("20261027220000_ToolRegistrationV1");
        db.Sys_Tenants.Add(new() { Id = tenant.CurrentTenantId, TenantCode = "W163", TenantName = "W163", Enable = true });
        db.Sys_Menus.Add(new() { MenuId = 212, MenuKey = "erp-business-partner", MenuName = "Partner" });
        var row = new BusinessPartner { BpCd = "BEFORE", BpName = "Before e\u0301", BpAbbrev = "B", BaseCd = "B", Status = 9, IsFrozen = true };
        db.BusinessPartners.Add(row); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.Database.MigrateAsync(); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var json = await db.Database.SqlQuery<string>($"""
            SELECT source_json AS "Value" FROM cp6_business_partner_identity_v1.versions WHERE tenant_id={tenant.CurrentTenantId} AND partner_id={row.Id} AND revision=1
            """).SingleAsync();
        using var original = JsonDocument.Parse(json); Assert.Equal(9, original.RootElement.GetProperty("status").GetInt32()); Assert.True(original.RootElement.GetProperty("isFrozen").GetBoolean());
        Assert.Equal("Before e\u0301", original.RootElement.GetProperty("bpName").GetString());
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_business_partner_identity_v1.versions").SingleAsync());
        Assert.Single(await db.Sys_MenuActions.Where(x => x.MenuId == 212 && x.ActionCode == "query").ToArrayAsync());
        Assert.Empty(await db.Sys_RoleActions.Where(x => x.MenuId == 212 && x.ActionCode == "query").ToArrayAsync());
    }
}
