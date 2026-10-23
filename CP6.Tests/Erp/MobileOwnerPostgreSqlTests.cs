using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using System.Net;
using System.Text.Json;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task SeedMobileOwner(Harness h, string number = "MOB-OWNER", string creator = "price-owner", int version = 2)
    {
        if (!await h.Db.Locations.AnyAsync(l => l.WarehouseCd == "W1"))
        h.Db.Locations.AddRange(new Location { WarehouseCd = "W1", LocationCd = "FROM", AreaCd = "SOURCE" },
            new Location { WarehouseCd = "W1", LocationCd = "TO", AreaCd = "TARGET" });
        h.Db.MobileTasks.Add(new() { MobileTaskNo = number, TaskType = MobileTaskType.Move, ContractVersion = version,
            WarehouseCd = "W1", AreaCd = "TARGET", FromLocationCd = "FROM", ToLocationCd = "TO", ProductCd = "P1",
            Qty = 12.12345678m, Creator = creator, AssignedTo = "private assignee", Instruction = "private instruction",
            Remarks = "private remark", ExceptionDescription = "private exception", RelatedNo = "private source", RelatedType = "MANUAL" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }

    [TenantManagementPostgresTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Mobile_owner_missing_authority_cannot_read_independent_legacy_v1_v2(int version)
    {
        await using var h = await Seed(); await SeedMobileOwner(h);
        Func<Task> read = version switch
        {
            0 => () => new MobileService(h.Db, Mock.Of<IWmsSequenceService>(), Mock.Of<IStockMovementService>()).GetTasksAsync(new()),
            1 => () => new MobileTaskV1Service(h.Db, Mock.Of<IWmsSequenceService>(), Mock.Of<IStockMovementService>(), Mock.Of<IMobileService>()).GetTasksAsync(new()),
            _ => () => new MobileTaskV2Service(h.Db, Mock.Of<IWmsSequenceService>(), Mock.Of<IStockMovementService>(), new FixedWmsAccessScopeProvider(new(false, [new("W1", "TARGET")]))).GetTasksAsync(new())
        };
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(read);
    }

    [CurrentPermissionPostgresFact]
    public async Task Mobile_owner_missing_authority_cannot_cancel_with_forged_actor()
    {
        await using var h = await Seed(); await SeedMobileOwner(h);
        var service = new MobileService(h.Db, Mock.Of<IWmsSequenceService>(), Mock.Of<IStockMovementService>());
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CancelAsync("MOB-OWNER", "forged-admin"));
        Assert.Equal(MobileTaskStatus.Pending, await h.Db.MobileTasks.Select(r => r.Status).SingleAsync());
    }

    private static async Task<OperationsOwnerV1> GrantMobile(Harness h, int scope = 5)
    {
        var menu = await h.Db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuId == 461);
        if (menu == null) h.Db.Sys_Menus.Add(new() { MenuId = 461, MenuKey = "wms-mobile", MenuName = "Mobile" });
        else menu.MenuKey = "wms-mobile";
        foreach (var action in new[] { "view", "analytics", "add", "assign", "claim", "start", "pause", "release", "takeover", "exception", "scan", "complete", "cancel" })
        {
            if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == 461 && a.ActionCode == action))
                h.Db.Sys_MenuActions.Add(new() { MenuId = 461, ActionCode = action, ActionName = action });
            h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = 461, ActionCode = action });
        }
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "wms-mobile", ScopeType = scope });
        h.Db.WmsRoleScopes.Add(new() { RoleId = 72, WarehouseCd = "W1" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var catalog = new AuthorizationCatalogStoreV1(); catalog.RegisterScope("wms-mobile", "Mobile", [1, 2, 3, 4, 5], 1);
        catalog.RegisterFields("wms-mobile", [new("AssignedTo", "Assignee"), new("ExceptionDescription", "Exception"), new("Instruction", "Instruction"), new("Remarks", "Remarks")]); catalog.Seal();
        return new(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static MobileService MobileLegacy(Harness h, OperationsOwnerV1 owner) => new(h.Db, Mock.Of<IWmsSequenceService>(), Mock.Of<IStockMovementService>(), owner);
    private static MobileTaskV1Service MobileV1(Harness h, OperationsOwnerV1 owner) => new(h.Db, Mock.Of<IWmsSequenceService>(), Mock.Of<IStockMovementService>(), Mock.Of<IMobileService>(), owner: owner);
    private static MobileTaskV2Service MobileV2(Harness h, OperationsOwnerV1 owner) => new(h.Db, Mock.Of<IWmsSequenceService>(), Mock.Of<IStockMovementService>(), new FixedWmsAccessScopeProvider(WmsAccessScope.All), owner: owner);

    [CurrentPermissionPostgresFact]
    public async Task Mobile_owner_requires_both_actual_locations_despite_forged_task_area_and_unions_current_grants()
    {
        await using var h = await Seed(); await SeedMobileOwner(h); await SeedMobileOwner(h, "MOB-V1", version: 1); var owner = await GrantMobile(h);
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync(); h.Db.WmsRoleScopes.Add(new() { RoleId = 72, WarehouseCd = "W1", AreaCd = "TARGET" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Empty(await MobileLegacy(h, owner).GetTasksAsync(new())); Assert.Equal(0, (await MobileV1(h, owner).GetTasksAsync(new())).Total);
        var v2 = MobileV2(h, owner); Assert.Equal(0, (await v2.GetTasksAsync(new())).Total); Assert.Null(await v2.GetAsync("MOB-OWNER"));
        Assert.Equal(0, (await v2.GetAnalyticsAsync(new())).Created);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => v2.GetEventsAsync("MOB-OWNER"))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => v2.CancelAsync("MOB-OWNER", new(), "forged"))).Status);
        h.Db.WmsRoleScopes.Add(new() { RoleId = 72, WarehouseCd = "W1", AreaCd = "SOURCE" }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal(12.12345678m, Assert.Single((await v2.GetTasksAsync(new() { Status = 0 })).Items).Qty);
        await h.Db.Locations.Where(l => l.LocationCd == "FROM").ExecuteUpdateAsync(s => s.SetProperty(l => l.IsDeleted, true));
        Assert.Null(await v2.GetAsync("MOB-OWNER")); Assert.Empty(await MobileLegacy(h, owner).GetTasksAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Mobile_owner_tenant_creator_and_protocol_scope_precede_counts_and_paging()
    {
        await using var h = await Seed(); await SeedMobileOwner(h); await SeedMobileOwner(h, "MOB-V1", version: 1);
        await SeedMobileOwner(h, "MOB-OTHER", "other"); await using var foreign = await Seed(); await SeedMobileOwner(foreign, "MOB-FOREIGN");
        var owner = await GrantMobile(h, 1); var input = new MobileTaskV2Query { Page = 0, PageSize = 1, Status = 0 };
        var page = await MobileV2(h, owner).GetTasksAsync(input); Assert.Equal(1, page.Total); Assert.Equal("MOB-OWNER", Assert.Single(page.Items).TaskNo); Assert.Equal(0, input.Page);
        Assert.Null(await MobileV2(h, owner).GetAsync("MOB-FOREIGN")); Assert.Null(await MobileV2(h, owner).GetAsync("MOB-V1"));
        Assert.Equal(2, (await MobileV1(h, owner).GetTasksAsync(new())).Total);
        Assert.Equal("MOB-OWNER", (await MobileV1(h, owner).GetAsync("MOB-OWNER"))!.TaskNo);
        Assert.Equal(2, (await MobileLegacy(h, owner).GetTasksAsync(new())).Count);
        await h.Db.MobileTasks.Where(t => t.MobileTaskNo == "MOB-OWNER").ExecuteUpdateAsync(s => s.SetProperty(t => t.Creator, (string?)null));
        Assert.Equal(0, (await MobileV2(h, owner).GetAnalyticsAsync(new())).Created);
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Mobile_owner_department_requires_native_explicit_binding_and_current_healthy_ancestry(int scope)
    {
        await using var h = await Seed(); await SeedMobileOwner(h); var owner = await GrantMobile(h); var admin = await MappingAdmin(h);
        await StandaloneScope(h, admin, scope); var v2 = MobileV2(h, owner); Assert.Empty((await v2.GetTasksAsync(new())).Items);
        var id = await h.Db.MobileTasks.Select(t => t.Id).SingleAsync(); await BindStandalone(h, admin, "mobile-task", id);
        Assert.Single((await v2.GetTasksAsync(new())).Items); Assert.Single(await MobileLegacy(h, owner).GetTasksAsync(new()));
        Assert.Equal(1, (await v2.GetAnalyticsAsync(new())).Created);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE object_id={id}");
        Assert.Empty((await v2.GetTasksAsync(new())).Items);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=true WHERE object_id={id}");
        await h.Db.Sys_Depts.Where(d => d.Id == admin.Root).ExecuteUpdateAsync(s => s.SetProperty(d => d.Enable, false));
        if (scope == 4) Assert.Empty((await v2.GetTasksAsync(new())).Items);
        else await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => v2.GetTasksAsync(new()));
    }

    [CurrentPermissionPostgresFact]
    public async Task Mobile_owner_native_binding_rejects_foreign_objects_and_department_and_preserves_CAS_original_audit()
    {
        await using var h = await Seed(); await SeedMobileOwner(h); var admin = await MappingAdmin(h); var id = await h.Db.MobileTasks.Select(t => t.Id).SingleAsync();
        Assert.Equal(id, Assert.Single(await StandaloneAdmin(h, admin).FindObjectsAsync("mobile-task", "MOB-", _ => Task.FromResult(h.User), default)).Id);
        var command = await StandaloneCommand(h, admin, "mobile-task", id); var saved = await SaveStandalone(h, admin, command);
        Assert.Equal(saved, await SaveStandalone(h, admin, command));
        Assert.Single((await h.Db.Sys_SecurityLogs.Select(x => x.ContextJson).ToArrayAsync()).Where(x => x?.Contains(command.OperationId.ToString()) == true));
        var stale = await StandaloneCommand(h, admin, "mobile-task", id, saved.EntityId);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE id={saved.EntityId}");
        Assert.Equal(412, (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(() => SaveStandalone(h, admin, stale))).Status);
        await using var foreign = await Seed(); await SeedMobileOwner(foreign); var other = await foreign.Db.MobileTasks.Select(t => t.Id).SingleAsync(); var otherAdmin = await MappingAdmin(foreign);
        Assert.Equal(409, (await Assert.ThrowsAsync<IamAdministrationExceptionV1>(() => BindStandalone(h, admin, "mobile-task", other))).Status);
        var objectError = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
            VALUES({Guid.NewGuid()},{h.Tenant},'mobile-task',{other},{admin.Child},true)
            """)); Assert.Equal("23514", objectError.SqlState);
        var departmentError = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_operations_owner_v1.department_mappings SET department_id={otherAdmin.Child} WHERE id={saved.EntityId}
            """)); Assert.Equal("23514", departmentError.SqlState);
    }

    [CurrentPermissionPostgresFact]
    public async Task Mobile_owner_current_fields_history_and_source_projection_do_not_expose_hidden_identity_or_modify_originals()
    {
        await using var h = await Seed(); await SeedMobileOwner(h); await SeedMobileOwner(h, "MOB-V1", version: 1); var owner = await GrantMobile(h); var v2 = MobileV2(h, owner);
        h.Db.MobileTaskEvents.Add(new() { TaskNo = "MOB-OWNER", EventType = "Created", UserName = "private actor", DeviceId = "private device", DataJson = "{\"secret\":true}" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var list = Assert.Single((await v2.GetTasksAsync(new())).Items); Assert.Null(list.Instruction); Assert.Null(list.Remarks); Assert.Null(list.SourceNo);
        Assert.Equal("private remark", (await v2.GetAsync("MOB-OWNER"))!.Remarks);
        foreach (var field in new[] { "AssignedTo", "Instruction", "Remarks", "ExceptionDescription" })
            h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-mobile", FieldName = field, Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var detail = (await v2.GetAsync("MOB-OWNER"))!; Assert.Null(detail.AssignedTo); Assert.Null(detail.Instruction); Assert.Null(detail.Remarks); Assert.Null(detail.ExceptionDescription);
        var legacy = (await MobileLegacy(h, owner).GetAsync("MOB-OWNER"))!; Assert.Null(legacy.Creator); Assert.Null(legacy.Modifier); Assert.Null(legacy.RelatedNo);
        Assert.Null((await MobileV1(h, owner).GetAsync("MOB-V1"))!.Instruction);
        var ev = Assert.Single(await v2.GetEventsAsync("MOB-OWNER")); Assert.Null(ev.UserName); Assert.Null(ev.DeviceId); Assert.Null(ev.DataJson);
        Assert.Equal("private remark", await h.Db.MobileTasks.Where(t => t.MobileTaskNo == "MOB-OWNER").Select(t => t.Remarks).SingleAsync());
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => v2.GetTasksAsync(new() { AssignedTo = "private assignee" }))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => v2.GetAnalyticsAsync(new() { AssignedTo = "private assignee" }))).Status);
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => v2.GetSourceTasksAsync("MANUAL", "private source"))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Mobile_owner_nonmove_history_refuses_unproven_blind_count_projection_and_retains_old_quantities()
    {
        await using var h = await Seed(); await SeedMobileOwner(h); var owner = await GrantMobile(h);
        await h.Db.MobileTasks.ExecuteUpdateAsync(s => s.SetProperty(t => t.TaskType, MobileTaskType.Count));
        var legacy = MobileLegacy(h, owner); var v2 = MobileV2(h, owner);
        Assert.Equal("MOBILE_KIND_READ_UNPROVEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => legacy.GetTasksAsync(new()))).Message);
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => legacy.GetAsync("MOB-OWNER"))).Status);
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => v2.GetAsync("MOB-OWNER"))).Status);
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => v2.GetAnalyticsAsync(new()))).Status);
        Assert.Equal(12.12345678m, await h.Db.MobileTasks.Select(t => t.Qty).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Mobile_owner_analytics_reports_legacy_cohort_elapsed_samples_without_claiming_physical_results()
    {
        await using var h = await Seed(); await SeedMobileOwner(h); var v2 = MobileV2(h, await GrantMobile(h));
        var empty = await v2.GetAnalyticsAsync(new()); Assert.Equal(0, empty.DurationSampleCount); Assert.False(empty.PhysicalResultsVerified);
        var at = DateTime.UtcNow.AddHours(-1);
        await h.Db.MobileTasks.ExecuteUpdateAsync(s => s.SetProperty(t => t.StartedAt, at).SetProperty(t => t.DoneAt, at.AddMinutes(15)).SetProperty(t => t.Status, MobileTaskStatus.Completed));
        var result = await v2.GetAnalyticsAsync(new()); Assert.Equal("LEGACY_CREATED_AT", result.TimeBasis); Assert.Equal("LEGACY_ELAPSED", result.DurationBasis);
        Assert.Equal(1, result.DurationSampleCount); Assert.Equal(15d, result.AverageMinutes); Assert.Equal(1, result.Completed); Assert.False(result.PhysicalResultsVerified);
        Assert.Equal(0, (await v2.GetAnalyticsAsync(new() { From = DateTime.UtcNow.AddDays(1) })).Created);
    }

    [CurrentPermissionPostgresFact]
    public async Task Mobile_owner_all_unadopted_commands_reject_before_stock_sequence_capacity_task_receipts_or_notifications()
    {
        await using var h = await Seed(); await SeedMobileOwner(h); await SeedMobileOwner(h, "MOB-V1", version: 1); var owner = await GrantMobile(h);
        var seq = new Mock<IWmsSequenceService>(MockBehavior.Strict); var stock = new Mock<IStockMovementService>(MockBehavior.Strict); var old = new Mock<IMobileService>(MockBehavior.Strict);
        var notify = new Mock<IMobileTaskNotifier>(MockBehavior.Strict); var stockNotify = new Mock<IWmsNotifier>(MockBehavior.Strict); var scope = new Mock<IWmsAccessScopeProvider>(MockBehavior.Strict);
        var legacy = new MobileService(h.Db, seq.Object, stock.Object, owner); var v1 = new MobileTaskV1Service(h.Db, seq.Object, stock.Object, old.Object, notify.Object, stockNotify.Object, owner);
        var v2 = new MobileTaskV2Service(h.Db, seq.Object, stock.Object, scope.Object, notify.Object, owner);
        var before = await h.Db.MobileTasks.Select(t => new { t.Id, t.Status, t.Qty, t.ReservedSourceQty, t.ReservedTargetCapacityQty, t.Modifier }).OrderBy(t => t.Id).ToArrayAsync();
        var audit = await h.Db.Sys_FieldAuditLogs.CountAsync(); var security = await h.Db.Sys_SecurityLogs.CountAsync();
        Func<Task>[] calls = [() => legacy.CreateAsync(new(), "forged"), () => legacy.StartAsync("MOB-OWNER", "forged"), () => legacy.ScanAsync(new() { Barcode = "FROM" }),
            () => legacy.CompleteAsync("MOB-OWNER", new(), "forged"), () => legacy.CancelAsync("MOB-OWNER", "forged"),
            () => v1.CreateAsync(new(), "forged"), () => v1.AssignAsync("MOB-V1", new(), "forged"), () => v1.ClaimAsync("MOB-V1", new(), "forged"),
            () => v1.StartAsync("MOB-V1", new(), "forged"), () => v1.ScanAsync("MOB-V1", new()), () => v1.CompleteAsync("MOB-V1", new(), "forged"), () => v1.CancelAsync("MOB-V1", new(), "forged"),
            () => v2.CreateAsync(new(), "forged"), () => v2.AssignAsync("MOB-OWNER", new(), "forged"), () => v2.ClaimAsync("MOB-OWNER", new(), "forged"),
            () => v2.StartAsync("MOB-OWNER", new(), "forged"), () => v2.PauseAsync("MOB-OWNER", new(), "forged"), () => v2.ReleaseAsync("MOB-OWNER", new(), "forged"),
            () => v2.TakeoverAsync("MOB-OWNER", new(), "forged"), () => v2.RaiseExceptionAsync("MOB-OWNER", new(), "forged"), () => v2.ResolveExceptionAsync("MOB-OWNER", new(), "forged"),
            () => v2.ScanAsync("MOB-OWNER", new(), "forged"), () => v2.CompleteAsync("MOB-OWNER", new(), "forged"), () => v2.CancelAsync("MOB-OWNER", new(), "forged"),
            () => v2.SynchronizePendingSourceTaskAsync("MOB-OWNER", new(), "forged"), () => v2.CancelPendingSourceTasksAsync("REPLENISH", "private source", "forged"), () => v2.GetScanProfileAsync("MOB-OWNER")];
        foreach (var call in calls) { var denied = await Assert.ThrowsAsync<OrderPriceCorrectionException>(call); Assert.Equal(503, denied.Status); Assert.Equal("MOBILE_OWNER_ADOPTION_UNPROVEN", denied.Message); }
        seq.VerifyNoOtherCalls(); stock.VerifyNoOtherCalls(); old.VerifyNoOtherCalls(); notify.VerifyNoOtherCalls(); stockNotify.VerifyNoOtherCalls(); scope.VerifyNoOtherCalls();
        Assert.Equal(before, await h.Db.MobileTasks.Select(t => new { t.Id, t.Status, t.Qty, t.ReservedSourceQty, t.ReservedTargetCapacityQty, t.Modifier }).OrderBy(t => t.Id).ToArrayAsync());
        Assert.Equal(0, await h.Db.MobileTaskEvents.CountAsync()); Assert.Equal(0, await h.Db.MobileTaskScanLogs.CountAsync()); Assert.Equal(0, await h.Db.TaskCommandReceipts.CountAsync());
        Assert.Equal(0, await h.Db.WmsSequences.CountAsync()); Assert.Equal(0, await h.Db.StockTransactions.CountAsync()); Assert.Equal(audit, await h.Db.Sys_FieldAuditLogs.CountAsync()); Assert.Equal(security, await h.Db.Sys_SecurityLogs.CountAsync());
        Assert.False(h.Db.ChangeTracker.HasChanges()); Assert.False((await v2.GetCapabilitiesAsync()).WritesAvailable);
    }

    [CurrentPermissionPostgresFact]
    public async Task Mobile_owner_rechecks_revoked_actions_and_final_field_failure_without_false_empty_success()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await SeedMobileOwner(h); var v2 = MobileV2(h, await GrantMobile(h)); var reads = 0;
        hook.Before = command => { if (command.CommandText.Contains("WITH field_subject") && ++reads == 2) throw new IOException("final authority unavailable"); return Task.CompletedTask; };
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => v2.GetTasksAsync(new()))).Status); hook.Before = null;
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 461 && a.ActionCode == "view").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => v2.GetCapabilitiesAsync())).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => v2.GetEventsAsync("MOB-OWNER"))).Status);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 461 && a.ActionCode == "cancel").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => v2.CancelAsync("MOB-OWNER", new(), "forged"))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Mobile_owner_native_fence_holds_task_until_current_authority_finishes()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); await SeedMobileOwner(h); var v2 = MobileV2(h, await GrantMobile(h, 1)); var attempted = false;
        hook.Before = async command =>
        {
            if (attempted || !command.CommandText.Contains("FROM \"T_MobileTask\"")) return; attempted = true;
            await using var writer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); await using var tx = await writer.Database.BeginTransactionAsync();
            await writer.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '200ms'");
            var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.MobileTasks.ExecuteUpdateAsync(s => s.SetProperty(t => t.Creator, "other")));
            Assert.Equal("55P03", Assert.IsType<PostgresException>(denied.InnerException).SqlState); await tx.RollbackAsync();
        };
        Assert.Single((await v2.GetTasksAsync(new())).Items); Assert.True(attempted); hook.Before = null;
        await h.Db.MobileTasks.ExecuteUpdateAsync(s => s.SetProperty(t => t.Creator, "other")); Assert.Empty((await v2.GetTasksAsync(new())).Items);
    }

    [CurrentPermissionPostgresFact]
    public async Task Mobile_owner_rejects_wrong_context_tracked_business_and_outer_transaction_without_discarding_caller_work()
    {
        await using var h = await Seed(); await SeedMobileOwner(h); var owner = await GrantMobile(h); await using var other = await Seed();
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => MobileV2(other, owner).GetTasksAsync(new()))).Status);
        var row = await h.Db.MobileTasks.SingleAsync(); Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => MobileV2(h, owner).GetTasksAsync(new()))).Status);
        row.Remarks = "caller pending"; Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => MobileLegacy(h, owner).CancelAsync(row.MobileTaskNo, "forged"))).Status);
        Assert.True(h.Db.ChangeTracker.HasChanges()); h.Db.ChangeTracker.Clear();
        await using var tx = await h.Db.Database.BeginTransactionAsync(); Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => MobileV1(h, owner).GetTasksAsync(new()))).Status); await tx.RollbackAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Mobile_owner_actual_Program_login_three_HTTP_versions_analytics_and_current_denials()
    {
        await using var h = await Seed(); await SeedMobileOwner(h); await SeedMobileOwner(h, "MOB-V1", version: 1); await GrantMobile(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        foreach (var path in new[] { "api/wms/mobile/task?assignedTo=private%20assignee", "api/v1/wms/mobile/tasks", "api/v2/wms/tasks", "api/v2/wms/task-analytics", "api/v2/wms/tasks/capabilities" })
        {
            using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path)); Assert.True(response.IsSuccessStatusCode, await OperationsFailure(response));
            Assert.True(response.Headers.CacheControl?.NoStore); Assert.DoesNotContain("private instruction", await response.Content.ReadAsStringAsync());
        }
        using (var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/wms/mobile/task/MOB-OWNER/cancel")))
        { Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode); Assert.Contains("MOBILE_OWNER_ADOPTION_UNPROVEN", await denied.Content.ReadAsStringAsync()); }
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 461 && a.ActionCode == "view").ExecuteDeleteAsync();
        foreach (var path in new[] { "api/wms/mobile/task/MOB-OWNER", "api/v1/wms/mobile/tasks/MOB-V1", "api/v2/wms/tasks/MOB-OWNER/events" })
        { using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); }
        Assert.All(await h.Db.MobileTasks.Select(t => t.Status).ToArrayAsync(), status => Assert.Equal(MobileTaskStatus.Pending, status));
    }
}
