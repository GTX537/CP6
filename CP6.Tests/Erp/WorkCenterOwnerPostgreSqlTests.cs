using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Core.Services.Fin;
using CP6.Entity.DTOs.Mes;
using CP6.Tests.Platform;
using Moq;
using Npgsql;
using System.Net;
using System.Text.Json;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static AuthorizationCatalogStoreV1 CostCatalog()
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var resource in new[] { "mes-work-center", "mes-process-cost-rate" })
        {
            catalog.RegisterScope(resource, resource, [1, 2, 3, 4, 5], 1);
            catalog.RegisterFields(resource, resource == "mes-work-center" ? [] : [new("LaborRate", "Labor"), new("OverheadRate", "Overhead")]);
        }
        catalog.Seal(); return catalog;
    }
    private static OperationsOwnerV1 CostOwner(Harness h) => new(h.Db, new OperationsHttp { HttpContext = h.Http },
        _ => Task.FromResult(h.User), CostCatalog().Capture);
    private static async Task<OperationsOwnerV1> GrantCost(Harness h)
    {
        foreach (var (resource, id) in new[] { ("mes-work-center", 314), ("mes-process-cost-rate", 315) })
        {
            var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuId == id);
            if (menu == null) h.Db.Sys_Menus.Add(new() { MenuId = id, MenuKey = resource, MenuName = resource }); else menu.MenuKey = resource;
            foreach (var action in new[] { "query", "edit", "del" })
            {
                if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == id && a.ActionCode == action))
                    h.Db.Sys_MenuActions.Add(new() { MenuId = id, ActionCode = action, ActionName = action });
                h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = id, ActionCode = action });
            }
            h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = resource, ScopeType = 5 });
        }
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return CostOwner(h);
    }
    private static async Task SeedCost(Harness h, string code = "WC1", string creator = "price-owner")
    {
        h.Db.WorkCenters.Add(new() { WgCd = code, WgName = "Center " + code, Creator = creator, DailyCapacityHours = 1234567890123.12345678m });
        h.Db.ProcessCostRates.Add(new() { WgCd = code, LaborRate = 1234567890123.12345678m, OverheadRate = 0.00000001m,
            ValidFrom = new(2026, 1, 1), Creator = creator });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_center_owner_missing_authority_cannot_read_independent_entries()
    {
        await using var h = await Seed();
        h.Db.WorkCenters.Add(new() { WgCd = "WC1", Creator = "price-owner" });
        h.Db.ProcessCostRates.Add(new() { WgCd = "WC1", LaborRate = 9m, OverheadRate = 8m, ValidFrom = new(2026, 1, 1) });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new WorkCenterService(h.Db).ListAsync(null));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new ProcessCostRateService(h.Db).ResolveAsync("WC1", new(2026, 2, 1)));
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_center_owner_missing_authority_cannot_write_with_forged_actor()
    {
        await using var h = await Seed();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new WorkCenterService(h.Db)
            .UpsertAsync(new WorkCenter { WgCd = "FORGED" }, "forged-admin"));
        Assert.False(await h.Db.WorkCenters.AnyAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_center_owner_exact_values_versions_identity_and_audit_are_atomic()
    {
        await using var h = await Seed(); var owner = await GrantCost(h);
        var centers = new WorkCenterService(h.Db, owner); var rates = new ProcessCostRateService(h.Db, owner);
        await centers.UpsertAsync(new() { WgCd = "WC1", Creator = "forged", TenantId = Guid.NewGuid(), DailyCapacityHours = 9999999999999.99999999m }, "forged");
        var wc = (await centers.GetOwnerAsync("WC1"))!;
        Assert.Equal("9999999999999.99999999", wc.DailyCapacityHours); Assert.NotEmpty(wc.RowVersion!);
        Assert.Equal("price-owner", await h.Db.WorkCenters.Select(x => x.Creator).SingleAsync());
        await rates.SaveOwnerAsync(new() { WgCd = "WC1", LaborRate = "1234567890123.12345678", OverheadRate = "0.00000001", ValidFrom = new(2026, 1, 1) });
        var rate = Assert.Single(await rates.ListOwnerAsync(null));
        Assert.Equal("1234567890123.12345678", rate.LaborRate); Assert.Equal("0.00000001", rate.OverheadRate);
        var old = wc.RowVersion!.ToArray(); wc.WgName = "changed"; await centers.SaveOwnerAsync(wc);
        Assert.Equal("ROW_VERSION_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => centers.DeleteOwnerAsync(wc.Id!.Value, old))).Message);
        wc = (await centers.GetOwnerAsync("WC1"))!; wc.WgCd = "MOVED";
        Assert.Equal("OBJECT_IDENTITY_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => centers.SaveOwnerAsync(wc))).Message);
        rate.WgCd = "MOVED";
        Assert.Equal("OBJECT_IDENTITY_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => rates.SaveOwnerAsync(rate))).Message);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => centers.SaveOwnerAsync(new() { WgCd = "WC1" }));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => rates.DeleteOwnerAsync(rate.Id!.Value, null));
        var audits = await h.Db.Sys_FieldAuditLogs.CountAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => centers.UpsertAsync(new() { WgCd = "ROUND", DailyCapacityHours = 0.123456789m }, "forged"));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => centers.SaveOwnerAsync(new() { WgCd = "BIG", DailyCapacityHours = "10000000000000" }));
        Assert.False(await h.Db.WorkCenters.AnyAsync(x => x.WgCd != "WC1")); Assert.Equal(audits, await h.Db.Sys_FieldAuditLogs.CountAsync());
        rate = Assert.Single(await rates.ListOwnerAsync(null)); await rates.DeleteOwnerAsync(rate.Id!.Value, rate.RowVersion);
        wc = (await centers.GetOwnerAsync("WC1"))!; await centers.DeleteOwnerAsync(wc.Id!.Value, wc.RowVersion);
        Assert.Empty(await centers.ListAsync(null)); Assert.Empty(await rates.ListAsync(null));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => centers.SaveOwnerAsync(new() { WgCd = "WC1" }));
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_center_owner_hidden_and_read_only_rates_are_not_zero_or_writeable()
    {
        await using var h = await Seed(); var owner = await GrantCost(h); await SeedCost(h);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "mes-process-cost-rate", FieldName = "LaborRate", Access = 3 });
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "mes-process-cost-rate", FieldName = "OverheadRate", Access = 2 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var rates = new ProcessCostRateService(h.Db, owner); var row = Assert.Single(await rates.ListOwnerAsync(null));
        Assert.Null(row.LaborRate); Assert.False(row.LaborRateWritable); Assert.False(row.OverheadRateWritable);
        Assert.Equal("0.00000001", row.OverheadRate);
        Assert.Equal("FIELD_READ_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => rates.ResolveAsync("WC1", new(2026, 2, 1)))).Message);
        row.OverheadRate = null; row.ValidTo = new(2026, 12, 31); await rates.SaveOwnerAsync(row);
        Assert.Equal(1234567890123.12345678m, await h.Db.ProcessCostRates.Select(x => x.LaborRate).SingleAsync());
        row = Assert.Single(await rates.ListOwnerAsync(null)); row.LaborRate = "0";
        Assert.Equal("FIELD_WRITE_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => rates.SaveOwnerAsync(row))).Message);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => rates.DeleteOwnerAsync(row.Id!.Value, row.RowVersion));
        // Accepted contract: MIN over stored rows, then default for a missing field. Explicit 1 is permissive.
        h.Db.Sys_Roles.Add(new() { RoleId = 73, RoleName = "permissive second role" });
        h.Db.Sys_UserRoles.Add(new() { UserId = h.User, RoleId = 73 });
        foreach (var field in new[] { "LaborRate", "OverheadRate" })
            h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 73, ResourceKey = "mes-process-cost-rate", FieldName = field, Access = 1 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        row = Assert.Single(await rates.ListOwnerAsync(null)); Assert.True(row.LaborRateWritable); Assert.True(row.OverheadRateWritable);
        Assert.Equal("1234567890123.12345678", row.LaborRate);
        await h.Db.Sys_RoleFieldPerms.Where(x => x.RoleId == 73).ExecuteDeleteAsync();
        row = Assert.Single(await rates.ListOwnerAsync(null)); Assert.Null(row.LaborRate); Assert.False(row.OverheadRateWritable);
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Work_center_owner_department_scope_uses_actual_parent_uuid_and_current_healthy_ancestry(int scope)
    {
        await using var h = await Seed(); var owner = await GrantCost(h); await SeedCost(h); await SeedCost(h, "OTHER");
        var admin = await MappingAdmin(h); await StandaloneScope(h, admin, scope);
        var centers = new WorkCenterService(h.Db, owner); var rates = new ProcessCostRateService(h.Db, owner);
        Assert.Empty(await centers.ListOwnerAsync(null)); Assert.Empty(await rates.ListOwnerAsync(null));
        var id = await h.Db.WorkCenters.Where(x => x.WgCd == "WC1").Select(x => x.Id).SingleAsync();
        Assert.Single(await StandaloneAdmin(h, admin).FindObjectsAsync("work-center", "WC1", _ => Task.FromResult(h.User), default));
        await BindStandalone(h, admin, "work-center", id);
        Assert.Equal("WC1", Assert.Single(await centers.ListOwnerAsync(null)).WgCd);
        var rate = Assert.Single(await rates.ListOwnerAsync(null)); Assert.Equal("WC1", rate.WgCd);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => centers.SaveOwnerAsync(new() { WgCd = "UNBOUND" }));
        Assert.False(await h.Db.WorkCenters.AnyAsync(x => x.WgCd == "UNBOUND"));
        var other = await h.Db.ProcessCostRates.AsNoTracking().SingleAsync(x => x.WgCd == "OTHER");
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => rates.DeleteOwnerAsync(other.Id, other.RowVersion));
        await h.Db.Sys_Depts.Where(x => x.Id == admin.Root).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, false));
        if (scope == 4) { Assert.Empty(await centers.ListOwnerAsync(null)); Assert.Empty(await rates.ListOwnerAsync(null)); }
        else { await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => centers.ListOwnerAsync(null)); await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => rates.ListOwnerAsync(null)); }
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_center_owner_self_tenant_and_missing_parent_boundaries_do_not_infer_ownership()
    {
        await using var h = await Seed(); var owner = await GrantCost(h); await SeedCost(h); await SeedCost(h, "OTHER", "other");
        await using var foreign = await Seed(); await SeedCost(foreign, "FOREIGN");
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey != "order").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        var centers = new WorkCenterService(h.Db, owner); var rates = new ProcessCostRateService(h.Db, owner);
        Assert.Equal("WC1", Assert.Single(await centers.ListAsync(null)).WgCd); Assert.Equal("WC1", Assert.Single(await rates.ListAsync(null)).WgCd);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => centers.GetAsync("OTHER"));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => rates.SaveOwnerAsync(new() { WgCd = "FOREIGN", LaborRate = "1", OverheadRate = "1", ValidFrom = new(2026, 1, 1) }));
        await h.Db.WorkCenters.Where(x => x.WgCd == "WC1").ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
        Assert.Empty(await rates.ListOwnerAsync(null)); Assert.Null(await rates.ResolveAsync("WC1", new(2026, 2, 1)));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Work_center_owner_final_failure_rolls_back_business_and_audit_but_lost_commit_is_unknown(bool lostAck)
    {
        var hook = new CommandHook(); var commit = new CommitFault(); await using var h = await Seed(hook, commit);
        var owner = await GrantCost(h); var audits = await h.Db.Sys_FieldAuditLogs.CountAsync(); var reads = 0;
        if (lostAck) commit.Armed = true;
        else hook.Before = c => { if (c.CommandText.Contains("WITH field_subject") && ++reads == 2) throw new IOException("final authority unavailable"); return Task.CompletedTask; };
        var service = new WorkCenterService(h.Db, owner);
        var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SaveOwnerAsync(new() { WgCd = "WC1", DailyCapacityHours = "0.12345678" }));
        hook.Before = null; Assert.Equal(lostAck ? "OPERATIONS_OUTCOME_UNKNOWN" : "AUTHORITY_FENCE_UNAVAILABLE", error.Message);
        Assert.Equal(lostAck ? 1 : 0, await h.Db.WorkCenters.CountAsync());
        if (!lostAck) Assert.Equal(audits, await h.Db.Sys_FieldAuditLogs.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_center_owner_native_binding_rejects_foreign_uuid_and_forward_catalog_never_grants_roles()
    {
        await using var h = await Seed(); await GrantCost(h); await SeedCost(h); var admin = await MappingAdmin(h);
        await using var foreign = await Seed(); await SeedCost(foreign); var id = await foreign.Db.WorkCenters.Select(x => x.Id).SingleAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
            VALUES({Guid.NewGuid()},{h.Tenant},'work-center',{id},{admin.Child},true)
            """)); Assert.Equal("23514", error.SqlState);
        var migration = new CP6.Persistence.PostgreSql.Migrations.Core.WorkCenterDepartmentOwnerV1();
        foreach (var op in migration.UpOperations.Cast<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>()) await h.Db.Database.ExecuteSqlRawAsync(op.Sql);
        Assert.Equal(2, await foreign.Db.Sys_MenuActions.CountAsync(x => (x.MenuId == 314 || x.MenuId == 315) && x.ActionCode == "query"));
        Assert.False(await foreign.Db.Sys_RoleActions.AnyAsync(x => x.MenuId == 314 || x.MenuId == 315));
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_center_owner_finance_legacy_fallback_is_rejected_before_cost_or_sequence_effects()
    {
        await using var h = await Seed(); var sequence = new Mock<IFinSequenceService>(MockBehavior.Strict);
        var service = new CostCollectService(h.Db, sequence.Object, new ProcessCostRateService(h.Db)) { StrictCostRate = false };
        Assert.Equal("FINANCE_OWNER_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CollectAsync("ANY", 1m, 1m, "forged"))).Message);
        Assert.Empty(sequence.Invocations); Assert.False(await h.Db.CostSheets.AnyAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_center_owner_finance_http_reports_unavailable_instead_of_legacy_success_or_generic_error()
    {
        await using var h = await Seed();
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == "fin-cost");
        if (menu == null) { menu = new() { MenuId = 9460, MenuKey = "fin-cost", MenuName = "Cost" }; h.Db.Sys_Menus.Add(menu); }
        h.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "collect", ActionName = "Collect" });
        h.Db.Sys_RoleActions.Add(new() { MenuId = menu.MenuId, RoleId = 72, ActionCode = "collect" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/fin/cost/ANY/collect", new { laborStd = 1, overheadStd = 1 }));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("FINANCE_OWNER_REQUIRED", await response.Content.ReadAsStringAsync());
        Assert.False(await h.Db.CostSheets.AnyAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_center_owner_actual_program_http_preserves_exact_strings_versions_fields_and_current_revocation()
    {
        await using var h = await Seed(); await GrantCost(h); await SeedCost(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using var get = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/mes/process-cost-rate/owner-v1"));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode); Assert.True(get.Headers.CacheControl?.NoStore);
        using var payload = JsonDocument.Parse(await get.Content.ReadAsStringAsync()); var row = payload.RootElement.GetProperty("data")[0];
        Assert.Equal("1234567890123.12345678", row.GetProperty("laborRate").GetString());
        var dto = row.Deserialize<ProcessCostRateOwnerV1>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!; dto.OverheadRate = "0.00000002";
        using var saved = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/mes/process-cost-rate/owner-v1/upsert", dto));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var stale = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/mes/process-cost-rate/owner-v1/upsert", dto));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.True(stale.Headers.CacheControl?.NoStore);
        var audit = await h.Db.Sys_FieldAuditLogs.AsNoTracking().SingleAsync(x => x.EntityName == "ProcessCostRate" && x.Operation == 2);
        Assert.Equal(h.User, audit.UserId); Assert.Equal("price-owner", audit.UserName);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 315 && x.ActionCode == "query").ExecuteDeleteAsync();
        foreach (var path in new[] { "api/mes/process-cost-rate", "api/mes/process-cost-rate/resolve?wgCd=WC1&onDate=2026-02-01", "api/mes/process-cost-rate/owner-v1" })
        { using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); }
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 314 && x.ActionCode == "query").ExecuteDeleteAsync();
        using var deniedCenter = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/mes/work-center/owner-v1"));
        Assert.Equal(HttpStatusCode.Forbidden, deniedCenter.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_center_owner_serializes_real_concurrent_period_inserts_without_double_writes()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantCost(h); await SeedCost(h);
        await h.Db.ProcessCostRates.ExecuteUpdateAsync(s => s.SetProperty(x => x.ValidTo, new DateTime(2026, 12, 31)));
        ProcessCostRateOwnerV1 Request() => new() { WgCd = "WC1", LaborRate = "1.00000001", OverheadRate = "2", ValidFrom = new(2027, 1, 1) };
        Task<string>? second = null; var blocked = false;
        hook.After = async command =>
        {
            if (!command.CommandText.Contains("WITH field_subject")) return; hook.After = null;
            second = Task.Run(async () =>
            {
                await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
                try { await new ProcessCostRateService(db, CostOwner(h with { Db = db })).SaveOwnerAsync(Request()); return "COMMITTED"; }
                catch (OrderPriceCorrectionException error) { return error.Message; }
            });
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                    AND query LIKE '%cp6_authentication_v1.acquire()%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            blocked = true; Assert.False(second.IsCompleted);
        };
        string? outcome = null;
        try { await new ProcessCostRateService(h.Db, owner).SaveOwnerAsync(Request()); }
        finally { if (second != null) outcome = await second; }
        Assert.True(blocked); Assert.Equal("RATE_PERIOD_CONFLICT", outcome);
        Assert.Equal(2, await h.Db.ProcessCostRates.CountAsync());
        Assert.Equal(1.00000001m, await h.Db.ProcessCostRates.Where(x => x.ValidFrom == new DateTime(2027, 1, 1)).Select(x => x.LaborRate).SingleAsync());
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Work_center_owner_current_raw_revocation_is_ordered_before_or_after_the_complete_write(bool before)
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantCost(h);
        Task? revoker = null; var observed = false;
        async Task Revoke()
        {
            await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Roles\" SET \"Enable\"=false WHERE \"TenantId\"={h.Tenant} AND \"RoleId\"=72");
        }
        if (before) hook.Before = async c => { if (!c.CommandText.Contains("cp6_authentication_v1.acquire()")) return; hook.Before = null; await Revoke(); observed = true; };
        else hook.After = async c =>
        {
            if (!c.CommandText.Contains("WITH field_subject")) return; hook.After = null; revoker = Task.Run(Revoke);
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                    AND query LIKE 'UPDATE public."Sys_Roles"%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            observed = true; Assert.False(revoker.IsCompleted);
        };
        var service = new WorkCenterService(h.Db, owner);
        try
        {
            if (before) await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SaveOwnerAsync(new() { WgCd = "WC1" }));
            else await service.SaveOwnerAsync(new() { WgCd = "WC1" });
        }
        finally { if (revoker != null) await revoker; }
        Assert.True(observed); Assert.Equal(before ? 0 : 1, await h.Db.WorkCenters.CountAsync());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ListOwnerAsync(null));
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_center_owner_parent_fence_blocks_native_delete_and_final_rate_failure_rolls_back()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantCost(h); await SeedCost(h);
        var attempted = false; var service = new ProcessCostRateService(h.Db, owner);
        hook.Before = async command =>
        {
            if (attempted || !command.CommandText.Contains("T_WorkCenter")) return; attempted = true;
            await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); await using var tx = await writer.Database.BeginTransactionAsync();
            await writer.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '200ms'");
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WorkCenters.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)));
            Assert.Equal("55P03", Assert.IsType<PostgresException>(failure.InnerException).SqlState); await tx.RollbackAsync();
        };
        var row = Assert.Single(await service.ListOwnerAsync(null)); Assert.True(attempted); hook.Before = null;
        var audits = await h.Db.Sys_FieldAuditLogs.CountAsync(); var reads = 0;
        hook.Before = c => { if (c.CommandText.Contains("WITH field_subject") && ++reads == 2) throw new IOException("final rate authority failure"); return Task.CompletedTask; };
        row.LaborRate = "7.00000001"; row.ValidTo = new(2026, 12, 31);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SaveOwnerAsync(row)); hook.Before = null;
        var stored = await h.Db.ProcessCostRates.AsNoTracking().SingleAsync();
        Assert.Equal(1234567890123.12345678m, stored.LaborRate); Assert.Null(stored.ValidTo); Assert.Equal(row.RowVersion, stored.RowVersion);
        Assert.Equal(audits, await h.Db.Sys_FieldAuditLogs.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_center_owner_original_session_context_and_independent_actions_cannot_be_bypassed()
    {
        await using var h = await Seed(); var owner = await GrantCost(h); await SeedCost(h);
        var service = new WorkCenterService(h.Db, owner);
        var tracked = await h.Db.WorkCenters.SingleAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ListOwnerAsync(null));
        Assert.Same(tracked, h.Db.ChangeTracker.Entries<WorkCenter>().Single().Entity); h.Db.ChangeTracker.Clear();
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        { await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ListOwnerAsync(null)); Assert.Same(tx, h.Db.Database.CurrentTransaction); await tx.RollbackAsync(); }
        h.Http.User = CurrentPermissionContextTests.Principal(h.User, Guid.NewGuid());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ListOwnerAsync(null));
        h.Http.User = CurrentPermissionContextTests.Principal(h.User, h.Tenant);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 314 && x.ActionCode == "edit").ExecuteDeleteAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.UpsertAsync(new() { WgCd = "OTHER" }, "forged"));
        Assert.Single(await service.ListOwnerAsync(null));
    }

    [CurrentPermissionPostgresFact]
    public async Task Work_center_owner_retains_existing_native_timestamp_extremes_and_inclusive_intervals()
    {
        await using var h = await Seed(); var owner = await GrantCost(h);
        await new WorkCenterService(h.Db, owner).SaveOwnerAsync(new() { WgCd = "TIME" });
        var service = new ProcessCostRateService(h.Db, owner);
        await service.SaveOwnerAsync(new() { WgCd = "TIME", LaborRate = "1", OverheadRate = "2", ValidFrom = DateTime.MinValue, ValidTo = DateTime.MaxValue });
        var row = Assert.Single(await service.ListOwnerAsync(null)); Assert.Equal(DateTime.MinValue, row.ValidFrom); Assert.Equal(DateTime.MaxValue, row.ValidTo);
        Assert.NotNull(await service.ResolveOwnerAsync("TIME", DateTime.MinValue)); Assert.NotNull(await service.ResolveOwnerAsync("TIME", DateTime.MaxValue));
        row.ValidFrom = new(2026, 1, 1); row.ValidTo = new(2026, 2, 1); await service.SaveOwnerAsync(row);
        Assert.NotNull(await service.ResolveOwnerAsync("TIME", new(2026, 2, 1)));
        Assert.Null(await service.ResolveOwnerAsync("TIME", new DateTime(2026, 2, 1).AddSeconds(1)));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SaveOwnerAsync(new() { WgCd = "TIME", LaborRate = "3", OverheadRate = "4", ValidFrom = new(2026, 2, 1) }));
        row = Assert.Single(await service.ListOwnerAsync(null)); row.ValidTo = new(2025, 1, 1);
        Assert.Equal("RATE_PERIOD_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SaveOwnerAsync(row))).Message);
    }
}
