using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static ToolApplicationOwnerV2 Applications(Harness h) => new(h.Db, RegistrationAuthority(h), ScopeFence(h));
    private static Task<Guid> ApplicationTool(Harness h) => h.Db.Database.SqlQuery<Guid>($"SELECT tool_id AS \"Value\" FROM cp6_tool_approval_v1.submissions WHERE tenant_id={h.Tenant}").SingleAsync();
    private static ToolApplicationCommandV2 ApplicationCommand(ToolApplicationContextV2 c) => new(Guid.NewGuid(), c.ReceiptStreamId!.Value, c.Receipt?.ReceiptId, "检查原批准与当前义务 e\u0301");
    private static async Task<ToolApplicationResultV2> ApplicationAttempt(Harness h, Guid tool)
    { var owner = Applications(h); var c = await owner.ContextAsync(tool); return await owner.ApplyAsync(tool, c.Etag, ApplicationCommand(c)); }
    private static async Task ApplicationCounts(Harness h, int expected)
    {
        Assert.Equal(expected, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tool_application_v2.receipts WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(expected, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tool_application_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_application_actual_decision_current_sod_and_missing_obligations_persist_precise_blocked_receipts()
    {
        await using var h = await Seed();
        // Freeze the policy after all approval permissions are registered. The ordinary
        // decision fixture adds these later, which correctly invalidates the issuer pin.
        await EngineeringAction(h, "oa-inbox", "approve"); await EngineeringAction(h, "oa-inbox", "query");
        var menu = await h.Db.Sys_Menus.SingleAsync(x => x.MenuKey == "oa-inbox");
        h.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = menu.MenuId });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var f = await FactSetup(h); var tool = await ApplicationTool(h);
        Assert.Equal("CURRENT", (await ToolApproval(h).ContextAsync(tool)).PolicyKnowledge);
        Assert.Equal("NOT_RECEIVED", (await Applications(h).ContextAsync(tool)).State);
        var ack = FactAck(await f.Owner.ReceiveAsync(f.Payload));
        var own = await ApplicationAttempt(h, tool); Assert.Equal("APPLICATION_SOD_REQUIRED", own.Receipt.OutcomeCode);
        var reviewer = await ActualReviewer(h); var result = await ApplicationAttempt(reviewer, tool);
        Assert.Equal("OBLIGATION_SOURCES_UNPROVEN", result.Receipt.OutcomeCode); Assert.Equal("BLOCKED", result.Receipt.Application);
        Assert.Null(result.Receipt.AppliedAt); Assert.Null(result.Receipt.BusinessResultRef); Assert.Equal(reviewer.User, result.Receipt.ActionActorId);
        Assert.Equal(own.Receipt.ReceiptId, result.Receipt.PreviousReceiptId); Assert.Equal("2", result.Receipt.ReceiptRevision);
        Assert.Equal(f.Decision.PayloadDigest, result.Receipt.DecisionPayloadDigest); Assert.Equal(f.Decision.Subject, result.Receipt.Subject);
        Assert.Equal(result.Receipt, Assert.IsType<ApprovalApplicationFoundV2>(await f.Owner.ApplicationAsync(ack.ReceiptStreamId!.Value)).Receipt);
        Assert.Equal(ack, FactAck(await f.Owner.ReceivedAsync(f.Decision.EventId))); // Immutable transport ACK does not change with later application.
        var bytes = ApprovalCanonicalV2.Bytes(result.Receipt); Assert.Equal(result.Receipt, ApprovalApplicationReceiptWireV2.Parse(bytes));
        var bad = JsonNode.Parse(bytes)!.AsObject(); bad["application"] = "APPLIED";
        Assert.Throws<ScopeAuthorizationExceptionV1>(() => ApprovalApplicationReceiptWireV2.Parse(ApprovalCanonicalV2.Bytes(bad)));
        await ApplicationCounts(h, 2); Assert.False((await Applications(h).ContextAsync(tool)).IsDiscarded); Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        await h.Db.Sys_Roles.Where(x => x.RoleId == 72).ExecuteUpdateAsync(x => x.SetProperty(r => r.RoleName, "changed policy issuer role"));
        Assert.Equal("TOOL_APPROVAL_POLICY_ISSUER_CHANGED", (await ApplicationAttempt(reviewer, tool)).Receipt.OutcomeCode);
        await ApplicationCounts(h, 3);
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_application_rejected_decision_never_becomes_discard_and_current_policy_is_rechecked()
    {
        await using var rejected = await Seed(); var f = await FactSetup(rejected, false); await f.Owner.ReceiveAsync(f.Payload);
        var tool = await ApplicationTool(rejected); var r = await ApplicationAttempt(rejected, tool);
        Assert.Equal("BLOCKED", r.Receipt.Application); Assert.Equal("APPROVAL_REJECTED", r.Receipt.OutcomeCode); Assert.False((await Applications(rejected).ContextAsync(tool)).IsDiscarded);
        await using var h = await Seed(); var approved = await FactSetup(h); await approved.Owner.ReceiveAsync(approved.Payload); tool = await ApplicationTool(h);
        var current = await ToolApproval(h).ContextAsync(tool); await ToolApproval(h).PolicyAsync(tool, current.Etag, current.Policy!.Fact.Body with { OperationId = Guid.NewGuid(), State = "REVOKED" });
        var other = await ActualReviewer(h); var blocked = await ApplicationAttempt(other, tool);
        Assert.Equal("TOOL_APPROVAL_POLICY_CHANGED", blocked.Receipt.OutcomeCode); Assert.Equal("BLOCKED", blocked.Receipt.Application);
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_application_original_commit_recovery_uses_current_read_without_write_or_new_policy()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var f = await FactSetup(h); await f.Owner.ReceiveAsync(f.Payload);
        var tool = await ApplicationTool(h); var owner = Applications(h); var c = await owner.ContextAsync(tool); var command = ApplicationCommand(c); fault.Armed = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ApplyAsync(tool, c.Etag, command))).Message);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 443 && x.ActionCode == "dispose").ExecuteDeleteAsync();
        var recovered = await owner.OperationAsync(command.OperationId, false); Assert.Equal("COMMITTED", recovered.State);
        Assert.Equal(recovered.Result, await owner.ApplyAsync(tool, c.Etag, command)); await ApplicationCounts(h, 1);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ApplyAsync(tool, c.Etag, command with { Reason = "changed original" }))).Status);
        var another = await ActualReviewer(h); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Applications(another).OperationAsync(command.OperationId, false))).Status);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 443 && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(command.OperationId, false))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_application_two_connections_original_command_and_receipt_compare_exchange_prevent_duplicate_append()
    {
        await using var h = await Seed(); var f = await FactSetup(h); await f.Owner.ReceiveAsync(f.Payload); var tool = await ApplicationTool(h);
        await using var peerDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); var peer = h with { Db = peerDb };
        var a = Applications(h); var b = Applications(peer); var c = await a.ContextAsync(tool); var command = ApplicationCommand(c);
        var results = await Task.WhenAll(a.ApplyAsync(tool, c.Etag, command), b.ApplyAsync(tool, c.Etag, command)); Assert.Equal(results[0], results[1]); await ApplicationCounts(h, 1);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => b.ApplyAsync(tool, c.Etag, command with { OperationId = Guid.NewGuid() }))).Status);
        var fresh = await a.ContextAsync(tool); var close = Guid.NewGuid(); Assert.Equal("CLOSED", (await a.OperationAsync(close, true)).State);
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => a.ApplyAsync(tool, fresh.Etag, ApplicationCommand(fresh) with { OperationId = close }))).Status);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_tool_application_v2.receipts WHERE tenant_id={h.Tenant}"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_application_missing_outbox_or_command_and_late_authority_aba_roll_back_receipt_chain()
    {
        var hook = new StartWriteHook(); await using var h = await Seed(hook); var f = await FactSetup(h); await f.Owner.ReceiveAsync(f.Payload); var tool = await ApplicationTool(h);
        foreach (var omitted in new[] { "INSERT INTO cp6_tool_application_v2.outbox", "INSERT INTO cp6_main_owner_v1.operations" })
        {
            hook.Before = c => { if (c.CommandText.Contains(omitted)) c.CommandText = "SELECT 1"; return Task.CompletedTask; };
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => ApplicationAttempt(h, tool)); hook.Before = null; await ApplicationCounts(h, 0);
        }
        hook.Before = async c => {
            if (!c.CommandText.Contains("INSERT INTO cp6_tool_application_v2.outbox")) return; hook.Before = null;
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.subject_grants SET can_query=false WHERE tenant_id={h.Tenant}");
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.subject_grants SET can_query=true WHERE tenant_id={h.Tenant}");
        };
        Assert.Equal(409, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => ApplicationAttempt(h, tool))).Status); await ApplicationCounts(h, 0);
        await ApplicationAttempt(h, tool); await ApplicationCounts(h, 1);
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_application_chain_snapshot_excludes_concurrent_append_and_rechecks_precise_service_read()
    {
        await using var h = await Seed(); var f = await FactSetup(h); var ack = FactAck(await f.Owner.ReceiveAsync(f.Payload)); var tool = await ApplicationTool(h);
        var receiver = new ToolApprovalFactReceiverV2(h.Db, ScopeFence(h), f.Authority, new(h.Db, ScopeFence(h)), new EphemeralDataProtectionProvider());
        var empty = Assert.IsType<ApprovalReceiptChainPageV2>(await receiver.ReceiptChainAsync(ack.ReceiptStreamId!.Value)); Assert.Empty(empty.Items); Assert.Equal("0", empty.HighestRevisionAtSnapshot);
        var one = await ApplicationAttempt(h, tool); var two = await ApplicationAttempt(h, tool);
        var page = Assert.IsType<ApprovalReceiptChainPageV2>(await receiver.ReceiptChainAsync(ack.ReceiptStreamId.Value, limit: 1));
        Assert.Equal("2", page.HighestRevisionAtSnapshot); Assert.Equal(one.Receipt, Assert.Single(page.Items)); Assert.NotNull(page.NextCursor);
        var three = await ApplicationAttempt(h, tool);
        var next = Assert.IsType<ApprovalReceiptChainPageV2>(await receiver.ReceiptChainAsync(ack.ReceiptStreamId.Value, limit: 1, cursor: page.NextCursor));
        Assert.Equal(page.SnapshotToken, next.SnapshotToken); Assert.Equal(two.Receipt, Assert.Single(next.Items)); Assert.Null(next.NextCursor);
        var later = Assert.IsType<ApprovalReceiptChainPageV2>(await receiver.ReceiptChainAsync(ack.ReceiptStreamId.Value, "2")); Assert.Equal(three.Receipt, Assert.Single(later.Items));
        Assert.Equal(409, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => receiver.ReceiptChainAsync(ack.ReceiptStreamId.Value, "1", cursor: page.NextCursor))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => receiver.ReceiptChainAsync(ack.ReceiptStreamId.Value, cursor: "tampered"))).Status);
        Assert.IsType<ApprovalReceiptChainNotObservedV2>(await receiver.ReceiptChainAsync(Guid.NewGuid()));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.subject_grants SET can_receive=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        Assert.Equal("FOUND", (await receiver.ApplicationAsync(ack.ReceiptStreamId.Value)).Kind);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.subject_grants SET can_query=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        Assert.Equal(404, (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => receiver.ReceiptChainAsync(ack.ReceiptStreamId.Value))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Tool_application_actual_program_http_strict_command_current_authority_and_original_result()
    {
        await using var h = await Seed(); var f = await FactSetup(h); await f.Owner.ReceiveAsync(f.Payload); var tool = await ApplicationTool(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, new Dictionary<string, string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString() });
        var cookies = await Login(host, h); var root = $"/api/wms/specials/v1/tools/{tool}";
        using var get = await host.Send(OperationsRequest(cookies, HttpMethod.Get, root + "/application-context")); Assert.Equal(HttpStatusCode.OK, get.StatusCode); Assert.True(get.Headers.CacheControl!.NoStore);
        var c = JsonDocument.Parse(await get.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Deserialize<ToolApplicationContextV2>(ApprovalBindingOwnerV2.Json)!;
        var input = ApplicationCommand(c);
        using var bad = OperationsRequest(cookies, HttpMethod.Post, root + "/approval-applications"); bad.Headers.TryAddWithoutValidation("If-Match", c.Etag);
        bad.Content = JsonContent.Create(new { input.OperationId, input.ReceiptStreamId, input.ExpectedReceiptId, input.Reason, applied = true });
        using var denied = await host.Send(bad); Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using var missing = OperationsRequest(cookies, HttpMethod.Post, root + "/approval-applications"); missing.Content = JsonContent.Create(input);
        using var noEtag = await host.Send(missing); Assert.Equal((HttpStatusCode)428, noEtag.StatusCode);
        using var post = OperationsRequest(cookies, HttpMethod.Post, root + "/approval-applications"); post.Headers.TryAddWithoutValidation("If-Match", c.Etag); post.Content = JsonContent.Create(input);
        using var saved = await host.Send(post); Assert.True(saved.StatusCode == HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var receipt = JsonDocument.Parse(await saved.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("receipt"); Assert.Equal("BLOCKED", receipt.GetProperty("application").GetString());
        using var original = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"/api/wms/tool-application/v2/operations/{input.OperationId}")); Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        await ApplicationCounts(h, 1);
    }
}
