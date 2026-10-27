using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static ToolMaintenanceOwnerV1 Maintenance(Harness h, CP6Context? db = null) => new(db ?? h.Db, RegistrationAuthority(h, db));
    private static async Task<Guid> MaintenanceSetup(Harness h)
    {
        await GrantRegistration(h);
        foreach (var action in new[] { "maintenance-policy", "maintenance-record", "maintenance-restore" }) await EngineeringAction(h, "wms-plate-mold", action);
        await EngineeringAction(h, "space-site", "tool-maintenance-assess");
        var command = await NewRegistered(h); await Registrations(h).CommandAsync(command, null); return command.ObjectId;
    }
    private static ToolMaintenancePolicyCommandV1 MaintenancePolicy(ToolMaintenanceContextV1 context, bool independent = false)
        => new(Guid.NewGuid(), "ENABLED", context.Site, ["DIMENSION", "SURFACE"], true, false, independent, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), "Authorized policy after actual condition assessment");
    private static async Task<ToolMaintenanceResultV1> MaintenancePublish(ToolMaintenanceOwnerV1 owner, Guid id, bool independent = false)
    { var c = await owner.ContextAsync(id); return await owner.PolicyAsync(id, c.Etag, MaintenancePolicy(c, independent)); }
    private static async Task<ToolMaintenanceResultV1> MaintenanceStart(ToolMaintenanceOwnerV1 owner, Guid id, Guid? predecessor = null, ToolMaintenanceRefV1? repair = null)
        => await owner.StartAsync(id, (await owner.ContextAsync(id)).Etag, new(Guid.NewGuid(), "DAMAGE", "Physical inspection requested", PredecessorMaintenanceId: predecessor, RepairPolicy: repair));
    private static async Task<ToolMaintenanceResultV1> MaintenanceReport(ToolMaintenanceOwnerV1 owner, Guid id, Guid mid, ToolMaintenanceRefV1? policy, string outcome = "PASS")
        => await owner.ReportAsync(id, mid, (await owner.ContextAsync(id)).Etag, new(Guid.NewGuid(), outcome, DateTimeOffset.UtcNow,
            [new("DIMENSION", "PASS", "Gauge within recorded tolerance"), new("SURFACE", outcome == "PASS" ? "PASS" : "FAIL", "Surface measured")], "Original repair inspection record e\u0301", policy));
    private static async Task<ToolMaintenanceResultV1> MaintenanceComplete(ToolMaintenanceOwnerV1 owner, Guid id, Guid mid, ToolMaintenanceRefV1 report, ToolMaintenanceRefV1 policy)
        => await owner.CompleteAsync(id, mid, (await owner.ContextAsync(id)).Etag, new(Guid.NewGuid(), report, policy));
    private static async Task<(ToolMaintenanceContextV1 Context, ToolMaintenanceResetV1 Command)> MaintenanceResetReady(Harness h, Guid id)
    {
        var owner = Maintenance(h); var p = await MaintenancePublish(owner, id); var start = await MaintenanceStart(owner, id); var mid = start.MaintenanceId!.Value;
        var report = await MaintenanceReport(owner, id, mid, p.Fact); var completed = await MaintenanceComplete(owner, id, mid, report.Fact, p.Fact);
        var c = await owner.ContextAsync(id); var qualification = await new ToolMaintenanceMasterOwnerV1(owner).AssessAsync(id, mid, c.Etag,
            new(Guid.NewGuid(), completed.Fact, p.Fact, "ELIGIBLE", DateTimeOffset.UtcNow.AddHours(1), "Master lifecycle inspection confirms this repair may restart the maintenance cycle"));
        c = await owner.ContextAsync(id); return (c, new(Guid.NewGuid(), completed.Fact, p.Fact, qualification.Fact, c.Cycle.Reference));
    }
    private static async Task<bool> MaintenanceResetHasBeenWritten(System.Data.Common.DbCommand command, Guid tool)
    {
        if (!command.CommandText.Contains("SELECT body_json AS")) return false;
        await using var probe = command.Connection!.CreateCommand(); probe.Transaction = command.Transaction;
        probe.CommandText = "SELECT EXISTS(SELECT FROM cp6_tool_maintenance_v1.facts WHERE tool_id=@tool AND kind='RESET')";
        probe.Parameters.Add(new Npgsql.NpgsqlParameter("tool", tool)); return (bool)(await probe.ExecuteScalarAsync())!;
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_actual_HTTP_registered_instance_context()
    {
        await using var h = await Seed(); await GrantRegistration(h);
        var command = await NewRegistered(h); await Registrations(h).CommandAsync(command, null);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get,
            $"/api/wms/specials/v1/tools/{command.ObjectId}/maintenance-context"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_real_policy_report_completion_master_qualification_and_reset_are_separate_immutable_facts()
    {
        await using var h = await Seed(); var id = await MaintenanceSetup(h); var owner = Maintenance(h);
        var initial = (await Registrations(h).ReadAsync(id))!; var (c, command) = await MaintenanceResetReady(h, id); var mid = c.Maintenance.Single().MaintenanceId;
        Assert.Equal("COMPLETED_PASS_PENDING_RESET", c.Maintenance.Single().Status); Assert.Equal(1, (await ToolInstances(h).ReadAsync(id)).Source.GetProperty("status").GetInt32());
        Assert.Equal("1", c.Cycle.Number); Assert.Equal("0", c.Cycle.LifetimeShots); Assert.Equal("RESTRICTED", c.CurrentEligibility);
        var result = await owner.ResetAsync(id, mid, c.Etag, command); Assert.Equal("COMPLETED_PASS_RELEASED", result.Outcome);
        var after = await owner.ContextAsync(id); Assert.Equal("2", after.Cycle.Number); Assert.Equal("0", after.Cycle.Shots); Assert.Equal("0", after.Cycle.LifetimeShots);
        Assert.NotEqual(c.Cycle.Reference.Id, after.Cycle.Reference.Id); Assert.Equal("UNKNOWN", after.CurrentEligibility); Assert.True(after.NotExecutionPermit);
        Assert.Equal("2", (await Registrations(h).ReadAsync(id))!.CycleNo); Assert.Equal(initial.OriginalJson, (await Registrations(h).ReadAsync(id))!.OriginalJson);
        Assert.Equal(result, await owner.ResetAsync(id, mid, c.Etag, command)); Assert.Equal(result, (await owner.OperationAsync(command.OperationId, false)).Result);
        Assert.Equal(0, await h.Db.StockTransactions.CountAsync()); Assert.Equal(0, await h.Db.Stocks.CountAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync("DELETE FROM cp6_tool_maintenance_v1.facts"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync("TRUNCATE cp6_tool_maintenance_v1.open_maintenance"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_no_policy_can_save_report_but_never_complete_or_reset_and_pending_blocks_second_start()
    {
        await using var h = await Seed(); var id = await MaintenanceSetup(h); var owner = Maintenance(h); var start = await MaintenanceStart(owner, id); var mid = start.MaintenanceId!.Value;
        var report = await MaintenanceReport(owner, id, mid, null); var c = await owner.ContextAsync(id);
        Assert.Equal("RECORDED_MAINTENANCE_REPORT", report.Outcome); Assert.Equal("OPEN", c.Maintenance.Single().Status); Assert.Equal("MISSING", c.PolicyKnowledge);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => MaintenanceStart(owner, id))).Status);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => MaintenanceComplete(owner, id, mid, report.Fact, new("forged", Guid.NewGuid(), "1", new('a', 64))))).Status);
        Assert.Equal("1", (await owner.ContextAsync(id)).Cycle.Number); Assert.Equal(1, (await ToolInstances(h).ReadAsync(id)).Source.GetProperty("status").GetInt32());
        var policy = await MaintenancePublish(owner, id); var formalReport = await MaintenanceReport(owner, id, mid, policy.Fact);
        await MaintenanceComplete(owner, id, mid, formalReport.Fact, policy.Fact);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => MaintenanceStart(owner, id))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => MaintenanceComplete(owner, id, mid, formalReport.Fact, policy.Fact))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_repair_successor_and_unserviceable_require_explicit_new_policy_without_rewriting_outcomes()
    {
        await using var h = await Seed(); var id = await MaintenanceSetup(h); var owner = Maintenance(h); var p = await MaintenancePublish(owner, id);
        var start = await MaintenanceStart(owner, id); var mid = start.MaintenanceId!.Value; var report = await MaintenanceReport(owner, id, mid, p.Fact, "REPAIR_REQUIRED");
        await MaintenanceComplete(owner, id, mid, report.Fact, p.Fact);
        Assert.Equal("TOOL_MAINTENANCE_SUCCESSOR_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => MaintenanceStart(owner, id))).Message);
        var repair = await MaintenanceStart(owner, id, mid); var repairId = repair.MaintenanceId!.Value;
        report = await MaintenanceReport(owner, id, repairId, p.Fact, "UNSERVICEABLE"); await MaintenanceComplete(owner, id, repairId, report.Fact, p.Fact);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => MaintenanceStart(owner, id, repairId, p.Fact))).Status);
        var c = await owner.ContextAsync(id); var newPolicy = await owner.PolicyAsync(id, c.Etag, MaintenancePolicy(c) with { AllowUnserviceableRepair = true });
        await MaintenanceStart(owner, id, repairId, newPolicy.Fact); c = await owner.ContextAsync(id);
        Assert.Equal(new[] { "OPEN", "UNSERVICEABLE", "REPAIR_REQUIRED" }, c.Maintenance.Select(x => x.Status));
        Assert.Equal("1", c.Cycle.Number); Assert.Equal("0", c.Cycle.LifetimeShots);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_current_permission_policy_site_and_native_ABA_fail_closed_but_keep_original_facts()
    {
        await using var h = await Seed(); var id = await MaintenanceSetup(h); var owner = Maintenance(h); var (c, command) = await MaintenanceResetReady(h, id); var mid = c.Maintenance.Single().MaintenanceId;
        await h.Db.Sys_RoleActions.Where(x => x.RoleId == 72 && x.MenuId == 443 && x.ActionCode == "maintenance-policy").ExecuteDeleteAsync();
        Assert.Equal("UNAVAILABLE", (await owner.ContextAsync(id)).PolicyKnowledge);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ResetAsync(id, mid, c.Etag, command))).Status);
        await EngineeringAction(h, "wms-plate-mold", "maintenance-policy");
        Assert.Equal("TOOL_MAINTENANCE_ISSUER_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ResetAsync(id, mid, c.Etag, command))).Message);
        await h.Db.PlateMoldStocks.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedShots, 1));
        await h.Db.PlateMoldStocks.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedShots, 0));
        c = await owner.ContextAsync(id); Assert.Equal("RECONCILIATION_REQUIRED", c.Cycle.Knowledge); Assert.Null(c.Cycle.LifetimeShots);
        Assert.Equal("TOOL_CYCLE_RECONCILIATION_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ResetAsync(id, mid, c.Etag, command))).Message);
        Assert.Equal("COMPLETED_PASS_PENDING_RESET", c.Maintenance.Single().Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_old_policy_or_master_restriction_never_authorizes_reset_and_legacy_lifetime_stays_unknown()
    {
        await using var h = await Seed(); var id = await MaintenanceSetup(h); var owner = Maintenance(h); var (c, command) = await MaintenanceResetReady(h, id); var mid = c.Maintenance.Single().MaintenanceId;
        await new ToolMaintenanceMasterOwnerV1(owner).AssessAsync(id, mid, c.Etag,
            new(Guid.NewGuid(), command.CompletionRef, command.ResetPolicySelectionRef, "RESTRICTED", DateTimeOffset.UtcNow.AddHours(1), "New crack found"));
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ResetAsync(id, mid, c.Etag, command))).Status);
        c = await owner.ContextAsync(id); Assert.Equal("TOOL_MAINTENANCE_QUALIFICATION_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ResetAsync(id, mid, c.Etag, command))).Message);
        await owner.PolicyAsync(id, c.Etag, MaintenancePolicy(c) with { State = "REVOKED" }); c = await owner.ContextAsync(id);
        Assert.Equal("TOOL_MAINTENANCE_POLICY_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ResetAsync(id, mid, c.Etag, command))).Message);
        var (legacy, registration) = await LegacyRegistration(h); await Registrations(h).CommandAsync(registration, legacy.Etag);
        var lc = await owner.ContextAsync(registration.ObjectId); Assert.Null(lc.Cycle.Number); Assert.Null(lc.Cycle.LifetimeShots); Assert.Equal("500", lc.Cycle.Shots);
        var lp = await MaintenancePublish(owner, registration.ObjectId); var ls = await MaintenanceStart(owner, registration.ObjectId);
        var lr = await MaintenanceReport(owner, registration.ObjectId, ls.MaintenanceId!.Value, lp.Fact);
        var done = await MaintenanceComplete(owner, registration.ObjectId, ls.MaintenanceId.Value, lr.Fact, lp.Fact);
        lc = await owner.ContextAsync(registration.ObjectId); Assert.Null(lc.Cycle.LifetimeShots); Assert.Equal("500", lc.Cycle.Shots);
        Assert.Equal(422, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new ToolMaintenanceMasterOwnerV1(owner).AssessAsync(registration.ObjectId, ls.MaintenanceId.Value, lc.Etag,
            new(Guid.NewGuid(), done.Fact, lp.Fact, "ELIGIBLE", DateTimeOffset.UtcNow.AddHours(1), "Cannot invent cycle history")))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_exact_if_match_input_conflict_close_and_lost_commit_ack_preserve_single_effect()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var id = await MaintenanceSetup(h); var owner = Maintenance(h); var c = await owner.ContextAsync(id);
        var cmd = new ToolMaintenanceStartV1(Guid.NewGuid(), "PLANNED", "e\u0301 original reason");
        Assert.Equal(428, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.StartAsync(id, null, cmd))).Status);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.StartAsync(id, "*", cmd))).Status);
        fault.Armed = true; Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.StartAsync(id, c.Etag, cmd))).Message);
        var result = (await owner.OperationAsync(cmd.OperationId, false)).Result!; Assert.Equal("OPEN", result.Outcome);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 443 && x.ActionCode == "maintenance").ExecuteDeleteAsync();
        Assert.Equal(result, await owner.StartAsync(id, c.Etag, cmd));
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.StartAsync(id, c.Etag, cmd with { Reason = "é original reason" }))).Status);
        var closed = Guid.NewGuid(); await owner.OperationAsync(closed, true);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.StartAsync(id, c.Etag, cmd with { OperationId = closed }))).Message);
        Assert.Single((await owner.ContextAsync(id)).Maintenance);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 443 && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(cmd.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_same_transaction_mutation_rolls_back_reset_cycle_receipt_and_native_counter()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var id = await MaintenanceSetup(h); var owner = Maintenance(h); var (c, cmd) = await MaintenanceResetReady(h, id);
        var mid = c.Maintenance.Single().MaintenanceId;
        hook.Before = async command =>
        {
            if (!await MaintenanceResetHasBeenWritten(command, id)) return; hook.Before = null;
            await using var writer = command.Connection!.CreateCommand(); writer.Transaction = command.Transaction;
            writer.CommandText = "UPDATE public.\"T_PlateMoldStock\" SET \"UsedShots\"=7 WHERE \"Id\"=@id";
            writer.Parameters.Add(new Npgsql.NpgsqlParameter("id", id)); await writer.ExecuteNonQueryAsync();
        };
        Assert.Equal("TOOL_INSTANCE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ResetAsync(id, mid, c.Etag, cmd))).Message);
        var after = await owner.ContextAsync(id); Assert.Equal("1", after.Cycle.Number); Assert.Equal("0", after.Cycle.Shots); Assert.Equal("COMPLETED_PASS_PENDING_RESET", after.Maintenance.Single().Status);
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(cmd.OperationId, false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_peer_raw_writer_waits_for_actual_reset_and_lost_ack_does_not_reapply()
    {
        var hook = new CommandHook(); var fault = new CommitFault(); await using var h = await Seed(hook, fault); var id = await MaintenanceSetup(h); var owner = Maintenance(h); var (c, cmd) = await MaintenanceResetReady(h, id);
        var mid = c.Maintenance.Single().MaintenanceId; Task<int>? peerWrite = null;
        hook.Before = async command =>
        {
            if (!await MaintenanceResetHasBeenWritten(command, id)) return; hook.Before = null;
            peerWrite = Task.Run(async () => { await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); return await peer.PlateMoldStocks.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedShots, 7)); });
            await using var observer = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE 'UPDATE %T_PlateMoldStock%') AS "Value"
                """).SingleAsync(deadline.Token)) await Task.Delay(20, deadline.Token);
            Assert.False(peerWrite.IsCompleted);
        };
        fault.Armed = true; Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ResetAsync(id, mid, c.Etag, cmd))).Message);
        Assert.Equal(1, await peerWrite!); var original = (await owner.OperationAsync(cmd.OperationId, false)).Result!; Assert.Equal("COMPLETED_PASS_RELEASED", original.Outcome);
        Assert.Equal(original, await owner.ResetAsync(id, mid, c.Etag, cmd)); var current = await owner.ContextAsync(id);
        Assert.Equal("RECONCILIATION_REQUIRED", current.Cycle.Knowledge); Assert.Equal("COMPLETED_PASS_RELEASED", current.Maintenance.Single().Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_actual_HTTP_csrf_closed_input_permissions_and_context()
    {
        await using var h = await Seed(); var id = await MaintenanceSetup(h); var owner = Maintenance(h); var c = await owner.ContextAsync(id);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        async Task<HttpResponseMessage> Send(object body, bool csrf = true)
        {
            var request = OperationsRequest(cookies, HttpMethod.Post, $"/api/wms/specials/v1/tools/{id}/maintenance"); request.Content = JsonContent.Create(body); request.Headers.TryAddWithoutValidation("If-Match", c.Etag);
            if (!csrf) request.Headers.Remove("X-CSRF-TOKEN"); return await host.Send(request);
        }
        var cmd = new ToolMaintenanceStartV1(Guid.NewGuid(), "PLANNED", "Actual HTTP repair");
        using var denied = await Send(cmd, false); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var injected = await Send(new { cmd.OperationId, cmd.ReasonCode, cmd.Reason, approved = true }); Assert.Equal(HttpStatusCode.BadRequest, injected.StatusCode);
        using var saved = await Send(cmd); Assert.Equal(HttpStatusCode.OK, saved.StatusCode); Assert.True(saved.Headers.CacheControl!.NoStore);
        using var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"/api/wms/tool-maintenance/v1/operations/{cmd.OperationId}")); Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Single((await owner.ContextAsync(id)).Maintenance);
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "wms-plate-mold").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        await h.Db.PlateMoldStocks.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "another"));
        Assert.Equal(404, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ContextAsync(id))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(cmd.OperationId, false))).Status);
        await using var foreign = await Seed(); await MaintenanceSetup(foreign);
        Assert.Equal(404, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Maintenance(foreign).ContextAsync(id))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_independent_inspection_and_original_publisher_authority_are_checked_against_their_own_actor()
    {
        await using var h = await Seed(); var id = await MaintenanceSetup(h);
        var second = new CP6.Entity.DomainModels.Sys.Sys_User { UserName = "maintenance-inspector", Password = "not-a-login-fixture", RoleId = 73 };
        h.Db.Sys_Roles.Add(new() { RoleId = 73, RoleName = "maintenance-inspector" }); h.Db.Sys_Users.Add(second);
        foreach (var grant in await h.Db.Sys_RoleActions.AsNoTracking().Where(x => x.RoleId == 72).ToArrayAsync()) h.Db.Sys_RoleActions.Add(new() { RoleId = 73, MenuId = grant.MenuId, ActionCode = grant.ActionCode });
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 73, ResourceKey = "wms-plate-mold", ScopeType = 5 });
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 73, ResourceKey = "space-site", ScopeType = 5 }); h.Db.WmsRoleScopes.Add(new() { RoleId = 73, WarehouseCd = "*" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var other = h with { User = second.Id, Http = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = CurrentPermissionContextTests.Principal(second.Id, h.Tenant) } };
        var maker = Maintenance(h); var inspector = Maintenance(other); var policy = await MaintenancePublish(maker, id, true); var start = await MaintenanceStart(maker, id); var mid = start.MaintenanceId!.Value;
        var report = await MaintenanceReport(maker, id, mid, policy.Fact);
        Assert.Equal("TOOL_MAINTENANCE_INDEPENDENT_INSPECTOR_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => MaintenanceComplete(maker, id, mid, report.Fact, policy.Fact))).Message);
        var completed = await MaintenanceComplete(inspector, id, mid, report.Fact, policy.Fact);
        Assert.Equal(second.Id, (await inspector.FactAsync(id, completed.Fact.Id)).Fact.ActorId);
        await h.Db.Sys_RoleActions.Where(x => x.RoleId == 72 && x.MenuId == 443 && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal("UNAVAILABLE", (await inspector.ContextAsync(id)).PolicyKnowledge);
        Assert.Equal("COMPLETED_PASS_PENDING_RESET", (await inspector.ContextAsync(id)).Maintenance.Single().Status);
        Assert.Equal(completed, (await inspector.OperationAsync(completed.OperationId, false)).Result);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_actual_HTTP_complete_local_owner_chain_and_read_original_facts()
    {
        await using var h = await Seed(); var id = await MaintenanceSetup(h); var owner = Maintenance(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        async Task<ToolMaintenanceResultV1> Send(string route, object body)
        {
            var c = await owner.ContextAsync(id); var request = OperationsRequest(cookies, HttpMethod.Post, route); request.Headers.TryAddWithoutValidation("If-Match", c.Etag); request.Content = JsonContent.Create(body);
            using var response = await host.Send(request); var text = await response.Content.ReadAsStringAsync(); Assert.True(response.IsSuccessStatusCode, response.StatusCode + ":" + text);
            return JsonDocument.Parse(text).RootElement.GetProperty("data").Deserialize<ToolMaintenanceResultV1>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        var root = $"/api/wms/specials/v1/tools/{id}"; var p = await Send(root + "/maintenance-policies", MaintenancePolicy(await owner.ContextAsync(id)));
        var start = await Send(root + "/maintenance", new ToolMaintenanceStartV1(Guid.NewGuid(), "PLANNED", "HTTP actual repair")); var mid = start.MaintenanceId!.Value;
        var report = await Send(root + $"/maintenance/{mid}/reports", new ToolMaintenanceReportCommandV1(Guid.NewGuid(), "PASS", DateTimeOffset.UtcNow,
            [new("DIMENSION", "PASS", "Measured"), new("SURFACE", "PASS", "Inspected")], "Original local repair evidence", p.Fact));
        var completion = await Send(root + $"/maintenance/{mid}/complete", new ToolMaintenanceCompleteV1(Guid.NewGuid(), report.Fact, p.Fact));
        var qualification = await Send($"/api/master/tool-maintenance/v1/{id}/{mid}/assessments", new ToolMaintenanceAssessmentCommandV1(Guid.NewGuid(), completion.Fact, p.Fact, "ELIGIBLE", DateTimeOffset.UtcNow.AddHours(1), "MASTER checked current lifecycle and site"));
        var reset = await Send(root + $"/maintenance/{mid}/reset-applications", new ToolMaintenanceResetV1(Guid.NewGuid(), completion.Fact, p.Fact, qualification.Fact, (await owner.ContextAsync(id)).Cycle.Reference));
        Assert.Equal("COMPLETED_PASS_RELEASED", reset.Outcome); Assert.Equal("2", (await owner.ContextAsync(id)).Cycle.Number);
        using var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get, root + $"/maintenance-facts/{p.Fact.Id}")); Assert.Equal(HttpStatusCode.OK, read.StatusCode); Assert.True(read.Headers.CacheControl!.NoStore);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_report_policy_period_route_identity_and_site_changes_do_not_grant_restoration()
    {
        await using var h = await Seed(); var id = await MaintenanceSetup(h); var owner = Maintenance(h); var start = await MaintenanceStart(owner, id); var mid = start.MaintenanceId!.Value;
        var occurredBeforePolicy = DateTimeOffset.UtcNow; var policy = await MaintenancePublish(owner, id); var c = await owner.ContextAsync(id);
        var report = new ToolMaintenanceReportCommandV1(Guid.NewGuid(), "PASS", occurredBeforePolicy,
            [new("DIMENSION", "PASS", "Measured"), new("SURFACE", "PASS", "Measured")], "Real repair happened before this policy was issued", policy.Fact);
        var saved = await owner.ReportAsync(id, mid, c.Etag, report);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReportAsync(id, Guid.NewGuid(), c.Etag, report))).Message);
        Assert.Equal("TOOL_MAINTENANCE_EVIDENCE_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => MaintenanceComplete(owner, id, mid, saved.Fact, policy.Fact))).Message);
        Assert.Equal("OPEN", (await owner.ContextAsync(id)).Maintenance.Single().Status);
        var currentReport = await MaintenanceReport(owner, id, mid, policy.Fact); await MaintenanceComplete(owner, id, mid, currentReport.Fact, policy.Fact);
        c = await owner.ContextAsync(id); await h.Db.Space_Sites.Where(x => x.Id == c.Site.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, false));
        c = await owner.ContextAsync(id); Assert.False(c.SiteEnabled); Assert.Equal("UNAVAILABLE", c.PolicyKnowledge);
        Assert.Equal("TOOL_SITE_DISABLED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new ToolMaintenanceMasterOwnerV1(owner).AssessAsync(id, mid, c.Etag,
            new(Guid.NewGuid(), c.Maintenance.Single().Completion!.Reference, policy.Fact, "ELIGIBLE", DateTimeOffset.UtcNow.AddHours(1), "Disabled Site cannot release")))).Message);
        Assert.Equal(saved, (await owner.OperationAsync(report.OperationId, false)).Result);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_maintenance_new_tenant_capability_definitions_do_not_grant_policy_record_restore_or_master_assessment()
    {
        await using var h = await Seed(); await GrantRegistration(h);
        var codes = new[] { "maintenance-policy", "maintenance-record", "maintenance-restore", "tool-maintenance-assess" };
        Assert.False(await h.Db.Sys_MenuActions.AnyAsync(x => codes.Contains(x.ActionCode)));
        CP6.WebApi.Seed.WmsPermissionSeed.EnsureSeeded(h.Db); h.Db.ChangeTracker.Clear();
        var siteMenu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "space-site").Select(x => x.MenuId).SingleAsync();
        Assert.Equal(3, await h.Db.Sys_MenuActions.CountAsync(x => x.MenuId == 443 && codes.Contains(x.ActionCode)));
        Assert.True(await h.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == siteMenu && x.ActionCode == "tool-maintenance-assess"));
        Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x => codes.Contains(x.ActionCode)));
    }
}
