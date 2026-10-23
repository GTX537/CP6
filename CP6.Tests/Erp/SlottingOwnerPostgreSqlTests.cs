using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Moq;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Entity.DTOs.Wms;
using CP6.Tests.Platform;
using System.Net;
using System.Text.Json;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static SlottingService LegacySlotting(Harness h) => new(h.Db, new WmsSequenceService(h.Db),
        new Mock<IMobileTaskV2Service>().Object, new FixedWmsAccessScopeProvider(WmsAccessScope.All));
    [CurrentPermissionPostgresFact]
    public async Task Slotting_owner_missing_authority_cannot_read_even_empty()
    {
        await using var h = await Seed(); var service = LegacySlotting(h);
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchAsync(null, null));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.GetAsync("missing"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Slotting_owner_missing_authority_cannot_analyze_with_forged_actor()
    {
        await using var h = await Seed();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => LegacySlotting(h).AnalyzeAsync("W01", 30, "forged"));
        Assert.False(await h.Db.SlottingPlans.AnyAsync()); Assert.False(await h.Db.WmsSequences.AnyAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Slotting_owner_missing_authority_cannot_cancel_legacy_plan()
    {
        await using var h = await Seed(); var no = "S" + h.Tenant.ToString("N")[..12];
        h.Db.SlottingPlans.Add(new() { SlottingPlanNo = no, WarehouseCd = "W01", Status = SlottingStatus.Recommended, Creator = "other", RecommendationsJson = "broken-original" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => LegacySlotting(h).CancelAsync(no, "forged"));
        Assert.Equal(SlottingStatus.Recommended, await h.Db.SlottingPlans.Select(x => x.Status).SingleAsync());
    }

    private static OperationsOwnerV1 SlottingOwner(Harness h)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var (key, fields) in new[] { ("wms-slotting", new[] { "ApproverCd", "RecommendationsJson", "Remarks" }),
            ("wms-stock", new[] { "UnitPrice" }), ("wms-outbound-order", new[] { "UnitPrice" }),
            ("wms-mobile", new[] { "AssignedTo", "ExceptionDescription", "Instruction", "Remarks" }),
            ("mes-work-order", Array.Empty<string>()), ("order", new[] { "UnitPrice", "Amount" }) })
        { catalog.RegisterScope(key, key, [1, 2, 3, 4, 5], 1); catalog.RegisterFields(key, fields.Select(f => new FieldRegistry.Field(f, f))); }
        catalog.Seal(); return new(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static async Task<OperationsOwnerV1> GrantSlotting(Harness h)
    {
        await GrantOperations(h);
        foreach (var (key, id, actions) in new[] { ("wms-slotting", 422, new[] { "query", "analyze", "approve", "cancel" }),
            ("wms-stock", 403, new[] { "query" }), ("wms-mobile", 461, new[] { "query" }) })
        {
            if (!await h.Db.Sys_Menus.AnyAsync(m => m.MenuId == id)) h.Db.Sys_Menus.Add(new() { MenuId = id, MenuKey = key, MenuName = key });
            foreach (var action in actions)
            {
                if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == id && a.ActionCode == action)) h.Db.Sys_MenuActions.Add(new() { MenuId = id, ActionCode = action, ActionName = action });
                h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = id, ActionCode = action });
            }
            h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = key, ScopeType = 5 });
        }
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return SlottingOwner(h);
    }
    private const string SlottingJson = "[ { \"ProductCd\": \"MAT\", \"OutQty\": 9999999999999.99999999, \"MoveQty\": 1.00000001, \"MobileTaskNo\": \"LEGACY-REF\" } ]";
    private static async Task SeedSlotting(Harness h, int count = 2)
    {
        var key = "S" + h.Tenant.ToString("N")[..8]; h.Db.Warehouses.Add(new() { WarehouseCd = key, WarehouseName = key });
        for (var i = 0; i < count; i++) h.Db.SlottingPlans.Add(new() { SlottingPlanNo = key + i.ToString("D4"), WarehouseCd = key,
            RecommendationsJson = SlottingJson, Status = SlottingStatus.Approved, AnalysisDays = 90, TxnSampleCount = 18, RecommendationCount = 1,
            Creator = "price-owner", ApproverCd = "old-approver", Remarks = "old remarks", AnalyzedAt = new(2026, 1, 1) });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }
    private static SlottingService SlottingServiceFor(Harness h, OperationsOwnerV1 owner) => new(h.Db, new WmsSequenceService(h.Db),
        new Mock<IMobileTaskV2Service>(MockBehavior.Strict).Object, new FixedWmsAccessScopeProvider(WmsAccessScope.All), owner);

    [CurrentPermissionPostgresFact]
    public async Task Slotting_owner_exact_original_snapshot_and_actual_paging_preserve_unknown_physical_outcome()
    {
        await using var h = await Seed(); var owner = await GrantSlotting(h); await SeedSlotting(h, 203); var service = SlottingServiceFor(h, owner);
        var first = await service.SearchOwnerAsync(new() { PageSize = 200 }); var second = await service.SearchOwnerAsync(new() { Page = 2, PageSize = 200 });
        Assert.Equal(203, first.Total); Assert.Equal(200, first.Items.Count); Assert.Equal(3, second.Items.Count);
        Assert.Equal(203, first.Items.Concat(second.Items).Select(x => x.Id).Distinct().Count()); Assert.False(first.WritesAvailable);
        var row = first.Items[0]; Assert.True(row.SnapshotReadable); Assert.Equal(SlottingJson, row.RecommendationsJson); Assert.Equal("JSON_ARRAY", row.JsonState);
        Assert.Equal("UNKNOWN", row.Knowledge); Assert.Equal("LEGACY_ROW_COUNT", row.StatisticsMethod); Assert.Equal(2, row.LegacyStatus); Assert.Equal(18, row.TxnSampleCount);
        Assert.Equal(SlottingJson, (await service.GetOwnerAsync(row.SlottingPlanNo))!.RecommendationsJson);
        var json = JsonSerializer.Serialize(row); Assert.DoesNotContain("TenantId", json); Assert.DoesNotContain("Creator", json); Assert.DoesNotContain("PhysicalQty", json);
        Assert.Equal(first.Items.Select(x => x.Id), (await service.SearchOwnerAsync(new() { PageSize = 200 })).Items.Select(x => x.Id));
    }
    [CurrentPermissionPostgresFact]
    public async Task Slotting_owner_missing_partial_source_or_area_cannot_disclose_opaque_counts_or_json()
    {
        await using var h = await Seed(); var owner = await GrantSlotting(h); await SeedSlotting(h, 1); var service = SlottingServiceFor(h, owner);
        async Task Hidden()
        {
            var row = Assert.Single((await service.SearchOwnerAsync(new())).Items); Assert.False(row.SnapshotReadable);
            Assert.Null(row.TxnSampleCount); Assert.Null(row.RecommendationCount); Assert.Null(row.RecommendationsJson); Assert.Equal("NOT_DISCLOSED", row.JsonState);
            Assert.Equal("SOURCE_DISCLOSURE_UNAVAILABLE", row.SnapshotState);
        }
        foreach (var key in new[] { "wms-stock", "wms-outbound-order", "wms-mobile" })
        {
            await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == key).ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1)); await Hidden();
            await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == key).ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 5));
        }
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd, "A")); await Hidden();
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd, "*"));
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-mobile", FieldName = "Instruction", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); await Hidden();
        await h.Db.Sys_RoleFieldPerms.Where(x => x.ResourceKey == "wms-mobile").ExecuteDeleteAsync();
        Assert.True(Assert.Single((await service.SearchOwnerAsync(new())).Items).SnapshotReadable);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 408 && x.ActionCode == "query").ExecuteDeleteAsync(); await Hidden();
        Assert.Equal(SlottingJson, await h.Db.SlottingPlans.Select(x => x.RecommendationsJson).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Slotting_owner_tenant_self_warehouse_and_deleted_plan_filter_before_total()
    {
        await using var h = await Seed(); var owner = await GrantSlotting(h); await SeedSlotting(h); var service = SlottingServiceFor(h, owner);
        await using var foreign = await Seed(); await SeedSlotting(foreign); Assert.Null(await service.GetOwnerAsync(await foreign.Db.SlottingPlans.Select(x => x.SlottingPlanNo).FirstAsync()));
        var id = await h.Db.SlottingPlans.Select(x => x.Id).FirstAsync(); await h.Db.SlottingPlans.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "other"));
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "wms-slotting").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        var one = Assert.Single((await service.SearchOwnerAsync(new())).Items); Assert.NotEqual(id, one.Id);
        await h.Db.SlottingPlans.Where(x => x.Id == one.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)); Assert.Empty((await service.SearchOwnerAsync(new())).Items);
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "wms-slotting").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 5));
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.WarehouseCd, "WRONG")); Assert.Empty((await service.SearchOwnerAsync(new())).Items);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.WarehouseCd, "*")); await h.Db.Warehouses.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
        Assert.Empty((await service.SearchOwnerAsync(new())).Items);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Slotting_owner_actual_department_uuid_requires_current_enabled_binding(int scope)
    {
        await using var h = await Seed(); var owner = await GrantSlotting(h); await SeedSlotting(h); var admin = await MappingAdmin(h);
        await StandaloneScope(h, admin, scope); var service = SlottingServiceFor(h, owner); Assert.Empty((await service.SearchOwnerAsync(new())).Items);
        var row = await h.Db.SlottingPlans.AsNoTracking().FirstAsync();
        Assert.Equal(row.Id, Assert.Single(await StandaloneAdmin(h, admin).FindObjectsAsync("slotting-plan", row.SlottingPlanNo, _ => Task.FromResult(h.User), default)).Id);
        var mapping = await BindStandalone(h, admin, "slotting-plan", row.Id); Assert.Equal(row.Id, Assert.Single((await service.SearchOwnerAsync(new())).Items).Id);
        await SaveStandalone(h, admin, (await StandaloneCommand(h, admin, "slotting-plan", row.Id, mapping.EntityId)) with { Enabled = false });
        Assert.Empty((await service.SearchOwnerAsync(new())).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task Slotting_owner_bad_or_missing_json_and_current_fields_never_replace_original_with_empty()
    {
        await using var h = await Seed(); var owner = await GrantSlotting(h); await SeedSlotting(h, 1); var service = SlottingServiceFor(h, owner);
        foreach (var (raw, state) in new (string?, string)[] { ("{broken 9999999999999.99999999", "LEGACY_PARSE_ERROR"), ("", "LEGACY_PARSE_ERROR"), ("null", "LEGACY_PARSE_ERROR"), (null, "NOT_RECORDED"), ("[]", "JSON_ARRAY") })
        {
            await h.Db.SlottingPlans.ExecuteUpdateAsync(s => s.SetProperty(x => x.RecommendationsJson, raw));
            var row = Assert.Single((await service.SearchOwnerAsync(new())).Items); Assert.Equal(raw, row.RecommendationsJson); Assert.Equal(state, row.JsonState);
        }
        foreach (var f in new[] { "ApproverCd", "Remarks", "RecommendationsJson" }) h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-slotting", FieldName = f, Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); var hidden = Assert.Single((await service.SearchOwnerAsync(new())).Items);
        Assert.Null(hidden.ApproverCd); Assert.Null(hidden.Remarks); Assert.False(hidden.SnapshotReadable); Assert.Null(hidden.TxnSampleCount);
        Assert.Equal("[]", await h.Db.SlottingPlans.Select(x => x.RecommendationsJson).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Slotting_owner_unadopted_writes_have_zero_sequence_task_and_plan_effects()
    {
        await using var h = await Seed(); var owner = await GrantSlotting(h); await SeedSlotting(h, 1);
        var seq = new Mock<IWmsSequenceService>(MockBehavior.Strict); var tasks = new Mock<IMobileTaskV2Service>(MockBehavior.Strict);
        var service = new SlottingService(h.Db, seq.Object, tasks.Object, new FixedWmsAccessScopeProvider(WmsAccessScope.All), owner);
        var no = await h.Db.SlottingPlans.Select(x => x.SlottingPlanNo).SingleAsync();
        foreach (var command in new Func<Task>[] { () => service.AnalyzeAsync("W01", 30, "forged"), () => service.ApproveAsync(no, "forged"), () => service.CancelAsync(no, "forged") })
            Assert.Equal("SLOTTING_OWNER_ADOPTION_UNPROVEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(command)).Message);
        Assert.Empty(seq.Invocations); Assert.Empty(tasks.Invocations); Assert.False(await h.Db.WmsSequences.AnyAsync()); Assert.False(await h.Db.MobileTasks.AnyAsync()); Assert.False(await h.Db.StockTransactions.AnyAsync());
        var row = await h.Db.SlottingPlans.AsNoTracking().SingleAsync(); Assert.Equal(2, row.Status); Assert.Equal(SlottingJson, row.RecommendationsJson); Assert.Equal("old-approver", row.ApproverCd);
        Assert.Equal("OWNER_READ_VERSION_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.GetAsync(no))).Message);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 422 && x.ActionCode == "approve").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ApproveAsync(no, "forged"))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Slotting_owner_frozen_query_final_unavailable_and_outer_or_pretracked_context_reject()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantSlotting(h); await SeedSlotting(h, 3); var service = SlottingServiceFor(h, owner);
        var query = new SlottingQueryV1 { PageSize = 1 };
        hook.Before = c => { if (c.CommandText.Contains("WITH field_subject")) { hook.Before = null; query.WarehouseCd = "WRONG"; query.Page = 20; } return Task.CompletedTask; };
        var page = await service.SearchOwnerAsync(query); Assert.Single(page.Items); Assert.Equal(1, page.Page); Assert.Equal(3, page.Total);
        var reads = 0; hook.Before = c => { if (c.CommandText.Contains("WITH field_subject") && ++reads == 5) throw new IOException("final slotting authority unavailable"); return Task.CompletedTask; };
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchOwnerAsync(new()))).Message); hook.Before = null;
        await using (var tx = await h.Db.Database.BeginTransactionAsync()) await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchOwnerAsync(new()));
        await h.Db.SlottingPlans.FirstAsync(); await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchOwnerAsync(new())); h.Db.ChangeTracker.Clear();
    }
    [CurrentPermissionPostgresFact]
    public async Task Slotting_owner_native_binding_query_bounds_and_catalog_replay_grant_no_roles()
    {
        await using var h = await Seed(); var owner = await GrantSlotting(h); await SeedSlotting(h); var admin = await MappingAdmin(h); var service = SlottingServiceFor(h, owner);
        foreach (var q in new[] { new SlottingQueryV1 { Page = 0 }, new() { PageSize = 201 }, new() { Page = int.MaxValue }, new() { Status = 3 }, new() { WarehouseCd = new string('W', 11) } })
            Assert.Equal("SLOTTING_QUERY_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchOwnerAsync(q))).Message);
        await using var foreign = await Seed(); await SeedSlotting(foreign); var id = await foreign.Db.SlottingPlans.Select(x => x.Id).FirstAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
            VALUES({Guid.NewGuid()},{h.Tenant},'slotting-plan',{id},{admin.Child},true)
            """)); Assert.Equal("23514", error.SqlState);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 422 && x.ActionCode == "query").ExecuteDeleteAsync();
        await h.Db.Sys_MenuActions.Where(x => x.MenuId == 422 && x.ActionCode == "query").ExecuteDeleteAsync();
        foreach (var op in new CP6.Persistence.PostgreSql.Migrations.Core.SlottingDepartmentOwnerV1().UpOperations.Cast<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
        { await h.Db.Database.ExecuteSqlRawAsync(op.Sql); await h.Db.Database.ExecuteSqlRawAsync(op.Sql); }
        Assert.Equal(1, await h.Db.Sys_MenuActions.CountAsync(x => x.MenuId == 422 && x.ActionCode == "query")); Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x => x.MenuId == 422 && x.ActionCode == "query"));
        Assert.Equal("ACTION_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SearchOwnerAsync(new()))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task Slotting_owner_actual_program_http_denies_old_effects_and_preserves_exact_current_read()
    {
        await using var h = await Seed(); await GrantSlotting(h); await SeedSlotting(h, 1);
        var no = await h.Db.SlottingPlans.Select(x => x.SlottingPlanNo).SingleAsync(); await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/slotting/owner-v1")); Assert.Equal(HttpStatusCode.OK, read.StatusCode); Assert.True(read.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync()); var data = json.RootElement.GetProperty("data"); Assert.False(data.GetProperty("writesAvailable").GetBoolean());
        Assert.Equal(SlottingJson, data.GetProperty("items")[0].GetProperty("recommendationsJson").GetString());
        using var detail = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/slotting/owner-v1/" + no)); Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        foreach (var route in new[] { "api/wms/slotting/analyze", "api/wms/slotting/" + no + "/approve", "api/wms/slotting/" + no + "/cancel" })
        {
            using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, route, new { warehouseCd = "W01", analysisDays = 30 }));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.Contains("SLOTTING_OWNER_ADOPTION_UNPROVEN", await response.Content.ReadAsStringAsync());
        }
        using var missing = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/slotting/owner-v1/missing")); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 422 && x.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/wms/slotting/owner-v1")); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(2, await h.Db.SlottingPlans.Select(x => x.Status).SingleAsync()); Assert.False(await h.Db.WmsSequences.AnyAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Slotting_owner_native_fence_keeps_raw_legacy_writer_out_of_the_read_cut()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var owner = await GrantSlotting(h); await SeedSlotting(h, 1);
        Task<int>? writer = null; var blocked = false;
        hook.After = async command =>
        {
            if (!command.CommandText.Contains("WITH field_subject")) return; hook.After = null;
            writer = Task.Run(async () =>
            {
                await using var other = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
                return await other.SlottingPlans.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SlottingStatus.Cancelled));
            });
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                    AND query LIKE 'UPDATE %T_SlottingPlan%') AS "Value"
                """).SingleAsync(timeout.Token)) await Task.Delay(20, timeout.Token);
            blocked = true; Assert.False(writer.IsCompleted);
        };
        SlottingPageV1? page = null;
        try { page = await SlottingServiceFor(h, owner).SearchOwnerAsync(new()); }
        finally { if (writer != null) await writer; }
        Assert.True(blocked); Assert.Equal(2, Assert.Single(page!.Items).LegacyStatus);
        Assert.Equal(9, await h.Db.SlottingPlans.Select(x => x.Status).SingleAsync());
    }
}
