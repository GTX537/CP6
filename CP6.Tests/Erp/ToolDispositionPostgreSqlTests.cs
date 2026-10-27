using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static ToolDispositionOwnerV1 Disposition(Harness h) => new(h.Db, RegistrationAuthority(h));
    private static async Task<ToolDispositionResultV1> DispositionRequest(ToolDispositionOwnerV1 owner, Guid id)
    { var c = await owner.ContextAsync(id); return await owner.RequestAsync(id, c.Etag, new(Guid.NewGuid(), "DAMAGE", "原始处置申请 e\u0301", c.Obligations.Version)); }
    private static async Task<ToolDispositionResultV1> DispositionBlock(ToolDispositionOwnerV1 owner, Guid id)
    { var c = await owner.ContextAsync(id); return await owner.BlockAsync(id, c.Etag, new(Guid.NewGuid(), c.Request!.Reference, "停止新使用，保留修复与真实记录", c.Obligations.Version)); }
    [CurrentPermissionPostgresFact]
    public async Task Tool_disposition_invalid_original_unicode_is_rejected_before_serialization_for_request_and_block()
    {
        await using var h = await Seed(); var id = await MaintenanceSetup(h); await EngineeringAction(h, "wms-plate-mold", "dispose"); var owner = Disposition(h);
        var c = await owner.ContextAsync(id); var operation = Guid.NewGuid();
        Assert.Equal("TOOL_DISPOSITION_INPUT_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.RequestAsync(id, c.Etag, new(operation, "WEAR", "invalid\uD800", c.Obligations.Version)))).Message);
        Assert.Null((await owner.ContextAsync(id)).Request); Assert.Equal("UNKNOWN", (await owner.OperationAsync(operation, false)).State);
        await DispositionRequest(owner, id); c = await owner.ContextAsync(id); operation = Guid.NewGuid();
        Assert.Equal("TOOL_DISPOSITION_INPUT_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.BlockAsync(id, c.Etag, new(operation, c.Request!.Reference, "invalid\uDC00", c.Obligations.Version)))).Message);
        Assert.False((await owner.ContextAsync(id)).NewUseBlocked); Assert.Equal("UNKNOWN", (await owner.OperationAsync(operation, false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_disposition_request_and_explicit_block_preserve_native_state_and_unknown_external_obligations()
    {
        await using var h = await Seed(); var id = await MaintenanceSetup(h); var owner = Disposition(h);
        await EngineeringAction(h, "wms-plate-mold", "dispose"); var before = await ToolInstances(h).ReadAsync(id);
        var c = await owner.ContextAsync(id); Assert.Empty(c.Obligations.Local); Assert.Equal(4, c.Obligations.UnknownOwners.Length); Assert.False(c.Obligations.CanFinalize);
        var request = await DispositionRequest(owner, id); c = await owner.ContextAsync(id);
        Assert.Equal("REQUESTED", c.State); Assert.False(c.NewUseBlocked); Assert.Equal("原始处置申请 e\u0301", c.Request!.Fact.Reason);
        Assert.Equal("UNKNOWN", (await Registrations(h).ReadAsync(id))!.CurrentEligibility);
        var block = await DispositionBlock(owner, id); c = await owner.ContextAsync(id);
        Assert.Equal(request.Fact, c.Block!.Fact.Request); Assert.Equal("BLOCKED_PENDING_DISPOSITION", block.Outcome); Assert.True(c.NewUseBlocked);
        Assert.Equal("NOT_CONNECTED", c.ApprovalKnowledge); Assert.False(c.IsDiscarded); Assert.False(c.IsUsePermit); Assert.Equal(before.Reference, (await ToolInstances(h).ReadAsync(id)).Reference);
        Assert.Equal("RESTRICTED", (await Registrations(h).ReadAsync(id))!.CurrentEligibility); Assert.True((await Registrations(h).ReadAsync(id))!.NewUseBlocked);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => DispositionBlock(owner, id))).Status);
        Assert.Equal(0, await h.Db.StockTransactions.CountAsync()); Assert.Equal(0, await h.Db.Stocks.CountAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync("DELETE FROM cp6_tool_disposition_v1.facts"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync("TRUNCATE cp6_tool_disposition_v1.facts"));
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_disposition_block_prevents_reset_but_preserves_original_maintenance_and_actual_fact_work()
    {
        await using var h = await Seed(); var id = await ActualSetup(h); await EngineeringAction(h, "wms-plate-mold", "dispose");
        var owner = Disposition(h); await DispositionRequest(owner, id); await DispositionBlock(owner, id);
        var (c, cmd) = await MaintenanceResetReady(h, id); var mid = c.Maintenance.Single().MaintenanceId;
        Assert.Equal("TOOL_NEW_USE_BLOCKED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Maintenance(h).ResetAsync(id, mid, c.Etag, cmd))).Message);
        var observed = await ActualObserve(ActualUsage(h), id); var dc = await owner.ContextAsync(id);
        Assert.Contains(dc.Obligations.Local, x => x.Kind == "MAINTENANCE" && x.State == "COMPLETED_PASS_PENDING_RESET");
        Assert.Contains(dc.Obligations.Local, x => x.Id == observed.Observation.Fact.Id.ToString() && x.State == "AWAITING_RECOGNITION_OR_APPLICATION");
        Assert.Equal("RESTRICTED", (await ActualUsage(h).ContextAsync(id)).CurrentEligibility);
        Assert.Equal("1", (await Maintenance(h).ContextAsync(id)).Cycle.Number); Assert.Equal("0", (await Maintenance(h).ContextAsync(id)).Cycle.Shots);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_disposition_obligation_head_change_stales_intent_and_block_never_claims_an_empty_set_is_closed()
    {
        await using var h = await Seed(); var id = await MaintenanceSetup(h); await EngineeringAction(h, "wms-plate-mold", "dispose"); var owner = Disposition(h);
        var old = await owner.ContextAsync(id); var start = await MaintenanceStart(Maintenance(h), id);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.RequestAsync(id, old.Etag, new(Guid.NewGuid(), "WEAR", "Observed wear", old.Obligations.Version)))).Status);
        var request = await DispositionRequest(owner, id); var c = await owner.ContextAsync(id);
        await MaintenanceReport(Maintenance(h), id, start.MaintenanceId!.Value, null);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.BlockAsync(id, c.Etag, new(Guid.NewGuid(), request.Fact, "Stop", c.Obligations.Version)))).Status);
        c = await owner.ContextAsync(id); Assert.NotEqual(old.Obligations.Version, c.Obligations.Version); Assert.Single(c.Obligations.Local); Assert.False(c.Obligations.CanFinalize);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.BlockAsync(id, c.Etag, new(Guid.NewGuid(), request.Fact with { Owner = "OA" }, "Stop", c.Obligations.Version)))).Status);
        await DispositionBlock(owner, id);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_disposition_lost_ACK_recovers_after_write_revocation_and_closed_key_never_creates_second_intent()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var id = await MaintenanceSetup(h); await EngineeringAction(h, "wms-plate-mold", "dispose"); var owner = Disposition(h);
        var c = await owner.ContextAsync(id); var cmd = new ToolDispositionRequestV1(Guid.NewGuid(), "OBSOLETE", "No longer required", c.Obligations.Version);
        fault.Armed = true; Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.RequestAsync(id, c.Etag, cmd))).Message);
        var saved = (await owner.OperationAsync(cmd.OperationId, false)).Result!;
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 443 && x.ActionCode == "dispose").ExecuteDeleteAsync();
        Assert.Equal(saved, await owner.RequestAsync(id, c.Etag, cmd));
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.RequestAsync(id, c.Etag, cmd with { Reason = "Changed" }))).Message);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => DispositionBlock(owner, id))).Status);
        var key = Guid.NewGuid(); Assert.Equal("CLOSED", (await owner.OperationAsync(key, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.RequestAsync(id, c.Etag, cmd with { OperationId = key }))).Message);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 443 && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(cmd.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_disposition_current_scope_and_foreign_operation_identity_fail_closed()
    {
        await using var h = await Seed(); var id = await ActualSetup(h); await EngineeringAction(h, "wms-plate-mold", "dispose"); var owner = Disposition(h);
        var saved = await DispositionRequest(owner, id); var reviewer = await ActualReviewer(h);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Disposition(reviewer).OperationAsync(saved.OperationId, false))).Status);
        await using var foreign = server.Context(new CP6.Core.Services.Common.TenantContext { CurrentTenantId = Guid.NewGuid() });
        var foreignOwner = new ToolDispositionOwnerV1(foreign, RegistrationAuthority(h, foreign));
        Assert.True((await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => foreignOwner.ContextAsync(id))).Status is 401 or 403 or 404 or 503);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_disposition_final_native_change_rolls_back_intent_and_original_operation()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var id = await MaintenanceSetup(h); await EngineeringAction(h, "wms-plate-mold", "dispose"); var owner = Disposition(h);
        var c = await owner.ContextAsync(id); var cmd = new ToolDispositionRequestV1(Guid.NewGuid(), "DAMAGE", "Crack seen", c.Obligations.Version);
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("SELECT body_json AS")) return;
            await using var probe = command.Connection!.CreateCommand(); probe.Transaction = command.Transaction;
            probe.CommandText = "SELECT EXISTS(SELECT FROM cp6_tool_disposition_v1.facts WHERE tool_id=@id)"; probe.Parameters.Add(new Npgsql.NpgsqlParameter("id", id));
            if (!(bool)(await probe.ExecuteScalarAsync())!) return; hook.Before = null;
            await using var mutate = command.Connection.CreateCommand(); mutate.Transaction = command.Transaction;
            mutate.CommandText = "UPDATE public.\"T_PlateMoldStock\" SET \"UsedShots\"=19 WHERE \"Id\"=@id"; mutate.Parameters.Add(new Npgsql.NpgsqlParameter("id", id)); await mutate.ExecuteNonQueryAsync();
        };
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.RequestAsync(id, c.Etag, cmd))).Status);
        Assert.Null((await owner.ContextAsync(id)).Request); Assert.Equal("UNKNOWN", (await owner.OperationAsync(cmd.OperationId, false)).State);
        Assert.Equal(0, (await ToolInstances(h).ReadAsync(id)).Source.GetProperty("usedShots").GetInt32());
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_disposition_actual_HTTP_strict_input_current_scope_and_ETag_request_block()
    {
        await using var h = await Seed(); var id = await MaintenanceSetup(h); await EngineeringAction(h, "wms-plate-mold", "dispose");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using var get = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"/api/wms/specials/v1/tools/{id}/disposition-context"));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode); Assert.True(get.Headers.CacheControl!.NoStore);
        var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync()).RootElement.GetProperty("data"); var etag = json.GetProperty("etag").GetString()!;
        var body = new { operationId = Guid.NewGuid(), reasonCode = "DAMAGE", reason = "原始报告 e\u0301", expectedObligationVersion = json.GetProperty("obligations").GetProperty("version").GetString() };
        var path = $"/api/wms/specials/v1/tools/{id}/disposition-requests";
        using var invalid = OperationsRequest(cookies, HttpMethod.Post, path); invalid.Headers.TryAddWithoutValidation("If-Match", etag);
        invalid.Content = JsonContent.Create(new { body.operationId, body.reasonCode, body.reason, body.expectedObligationVersion, approved = true });
        using var rejected = await host.Send(invalid); Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var post = OperationsRequest(cookies, HttpMethod.Post, path); post.Headers.TryAddWithoutValidation("If-Match", etag); post.Content = JsonContent.Create(body);
        using var result = await host.Send(post); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var c = await Disposition(h).ContextAsync(id); using var block = OperationsRequest(cookies, HttpMethod.Post, $"/api/wms/specials/v1/tools/{id}/new-use-blocks");
        block.Headers.TryAddWithoutValidation("If-Match", c.Etag); block.Content = JsonContent.Create(new ToolDispositionBlockV1(Guid.NewGuid(), c.Request!.Reference, "Explicit stop", c.Obligations.Version));
        using var blocked = await host.Send(block); Assert.Equal(HttpStatusCode.OK, blocked.StatusCode); Assert.True((await Disposition(h).ContextAsync(id)).NewUseBlocked);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_disposition_same_transaction_block_at_reset_final_gate_rolls_back_reset_and_block()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var id = await MaintenanceSetup(h); await EngineeringAction(h, "wms-plate-mold", "dispose");
        var owner = Disposition(h); await DispositionRequest(owner, id); var dc = await owner.ContextAsync(id); var (c, cmd) = await MaintenanceResetReady(h, id); var mid = c.Maintenance.Single().MaintenanceId;
        hook.Before = async command =>
        {
            if (!await MaintenanceResetHasBeenWritten(command, id)) return; hook.Before = null;
            var fact = new ToolDispositionFactV1(h.Tenant, id, Guid.NewGuid(), "BLOCK", h.User, DateTimeOffset.UtcNow, c.Native, dc.Site,
                dc.Request!.Fact.Authorities, "DAMAGE", "Same UoW stop arrives at final gate", dc.Request.Reference, dc.Obligations);
            var body = JsonSerializer.Serialize(fact, ToolMaintenanceStoreV1.Json); var reference = ToolDispositionStoreV1.Ref(fact);
            await using var insert = command.Connection!.CreateCommand(); insert.Transaction = command.Transaction;
            insert.CommandText = "INSERT INTO cp6_tool_disposition_v1.facts(tenant_id,tool_id,record_id,kind,body_json,body_digest) VALUES(@tenant,@tool,@record,'BLOCK',@body,@digest)";
            foreach (var (key, value) in new (string, object)[] { ("tenant", h.Tenant), ("tool", id), ("record", fact.RecordId), ("body", body), ("digest", reference.Digest) }) insert.Parameters.Add(new Npgsql.NpgsqlParameter(key, value));
            await insert.ExecuteNonQueryAsync();
        };
        Assert.Equal("TOOL_NEW_USE_BLOCKED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Maintenance(h).ResetAsync(id, mid, c.Etag, cmd))).Message);
        Assert.False((await owner.ContextAsync(id)).NewUseBlocked); Assert.Equal("COMPLETED_PASS_PENDING_RESET", (await Maintenance(h).ContextAsync(id)).Maintenance.Single().Status);
        Assert.Equal("1", (await Maintenance(h).ContextAsync(id)).Cycle.Number); Assert.Equal("UNKNOWN", (await Maintenance(h).OperationAsync(cmd.OperationId, false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Tool_disposition_peer_native_write_waits_until_block_transaction_commits()
    {
        var hook = new CommandHook(); await using var h = await Seed(hook); var id = await MaintenanceSetup(h); await EngineeringAction(h, "wms-plate-mold", "dispose");
        var owner = Disposition(h); await DispositionRequest(owner, id); Task<int>? peer = null;
        hook.Before = async command =>
        {
            if (!command.CommandText.Contains("SELECT body_json AS")) return;
            await using var probe = command.Connection!.CreateCommand(); probe.Transaction = command.Transaction;
            probe.CommandText = "SELECT EXISTS(SELECT FROM cp6_tool_disposition_v1.facts WHERE tool_id=@id AND kind='BLOCK')"; probe.Parameters.Add(new Npgsql.NpgsqlParameter("id", id));
            if (!(bool)(await probe.ExecuteScalarAsync())!) return; hook.Before = null;
            peer = Task.Run(async () => { await using var other = server.Context(new CP6.Core.Services.Common.TenantContext { CurrentTenantId = h.Tenant }); return await other.PlateMoldStocks.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedShots, 7)); });
            await using var observer = server.Context(new CP6.Core.Services.Common.TenantContext { CurrentTenantId = h.Tenant }); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!await observer.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE 'UPDATE %T_PlateMoldStock%') AS \"Value\"").SingleAsync(deadline.Token)) await Task.Delay(20, deadline.Token);
            Assert.False(peer.IsCompleted);
        };
        await DispositionBlock(owner, id); Assert.NotNull(peer); Assert.Equal(1, await peer!); Assert.True((await owner.ContextAsync(id)).NewUseBlocked);
    }
}
