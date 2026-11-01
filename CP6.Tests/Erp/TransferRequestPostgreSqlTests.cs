using System.Data.Common;
using System.Net;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Wms.Transfer;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record TransferFixture(Harness Reviewer, TransferRequestOptionsV1 Options, TransferRequestCreateV1 Create);
    private static TransferRequestOwnerV1 Requests(Harness h, TransferRequestOptionsV1 options)
        => new(h.Db, InventoryIdentityAuthority(h), ScopeFence(h), Options.Create(TransferOptions()), Options.Create(options), new("DEMO"), TransferSources(h), new(h.Db, new WmsSequenceService(h.Db)));
    private async Task<TransferFixture> TransferRequestSetup(Harness h, bool ratio = false)
    {
        await GrantTransfer(h); await TransferEnvironment(h).SaveAsync(RegisterTransfer());
        var f = await InventoryProductSetup(h, ratio, registerScope: false); var product = await ApprovedIdentity(h, f.Reviewer, f.Definition);
        foreach (var action in new[] { "query", "add", "edit", "submit", "approve", "withdraw" }) await EngineeringAction(h, "wms-transfer-request", action);
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "wms-transfer-request", ScopeType = 5 });
        var warehouse = new Warehouse { WarehouseCd = "TR1", WarehouseName = "模拟调拨仓库" };
        var from = new Location { WarehouseCd = "TR1", LocationCd = "FROM", LocationName = "模拟来源", AreaCd = "A" };
        var to = new Location { WarehouseCd = "TR1", LocationCd = "TO", LocationName = "模拟目标", AreaCd = "B" };
        h.Db.Warehouses.Add(warehouse); h.Db.Locations.AddRange(from, to);
        h.Db.WmsRoleScopes.Add(new() { RoleId = 72, WarehouseCd = "TR1", AreaCd = "*" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var site = await h.Db.Space_Sites.Select(x => x.Id).SingleAsync();
        var inputUnit = await Units(h).ReadAsync(ratio ? (await h.Db.Database.SqlQuery<Guid>($"""
            SELECT profile_id AS "Value" FROM cp6_uom_profile_v1.heads WHERE tenant_id={h.Tenant} AND code='HF'
            """).SingleAsync()) : f.Unit.Reference.Id, null);
        var line = new TransferRequestLineInputV1("原始行", product.Reference, inputUnit.Reference, "10.00000000", warehouse.Id, from.Id, warehouse.Id, to.Id);
        return new(f.Reviewer, new() { WarehouseSites = [new(warehouse.Id, site)] }, new(Guid.NewGuid(), "simulated-request-1", null, [line], "模拟数据生产验收"));
    }
    private static TransferRequestActionV1 RequestAction(TransferRequestViewV1 v)
        => new(Guid.NewGuid(), v.Revision, v.Etag, v.ContentDigest, "执行已授权的调拨请求动作");
    private static string TransferJson(object v) => JsonSerializer.Serialize(v);

    [CurrentPermissionPostgresFact]
    public async Task TransferRequest_create_original_nochange_edit_and_real_budget_replacement_are_atomic()
    {
        await using var h = await Seed(); var f = await TransferRequestSetup(h, ratio: true); var o = Requests(h, f.Options);
        var context = await o.ContextAsync(); Assert.Equal(2, context.Locations.Length); Assert.Single(context.Products);
        var first = await o.CreateAsync(f.Create); var v = first.Request; Assert.Equal("5.00000000", Assert.Single(v.Lines).BaseQuantity);
        Assert.Equal(TransferJson(first), TransferJson(await o.CreateAsync(f.Create)));
        Assert.Equal("TRANSFER_DRAFT_KEY_EXISTS", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(f.Create with { OperationId = Guid.NewGuid() }))).Message);
        var edit = new TransferRequestEditV1(Guid.NewGuid(), v.Revision, v.Etag, f.Create.Lines, "不改变内容");
        var same = await o.EditAsync(v.Id, edit); Assert.False(same.Changed); Assert.Equal(v.Version, same.Request.Version);
        var changed = await o.EditAsync(v.Id, edit with { OperationId = Guid.NewGuid(), Lines = [f.Create.Lines[0] with { InputQuantity = "8.00000000" }] });
        Assert.Equal("2", changed.Request.Version); Assert.Equal("4.00000000", changed.Request.Lines[0].BaseQuantity);
        Assert.Equal(v.Lines[0].Id, changed.Request.Lines[0].Id);
        Assert.Equal("CLOSED", await h.Db.Database.SqlQuery<string>($"""
            SELECT state AS "Value" FROM cp6_transfer_local_v1.bindings WHERE tenant_id={h.Tenant} AND id={v.Lines[0].BindingId}
            """).SingleAsync());
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM cp6_transfer_local_v1.bindings WHERE tenant_id={h.Tenant} AND state='ACTIVE'""").SingleAsync());
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM cp6_transfer_request_v1.events WHERE tenant_id={h.Tenant}""").SingleAsync());
        Assert.Equal("TRANSFER_REQUEST_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.EditAsync(v.Id, edit with { OperationId = Guid.NewGuid() }))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferRequest_independent_approval_binds_exact_content_without_moving_stock()
    {
        await using var h = await Seed(); var f = await TransferRequestSetup(h); var o = Requests(h, f.Options);
        var v = (await o.CreateAsync(f.Create)).Request;
        v = (await o.ActionAsync(v.Id, "SUBMIT", RequestAction(v))).Request;
        Assert.Equal("TRANSFER_INDEPENDENT_APPROVER_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.ActionAsync(v.Id, "APPROVE", RequestAction(v)))).Message);
        var approved = (await Requests(f.Reviewer, f.Options).ActionAsync(v.Id, "APPROVE", RequestAction(v))).Request;
        Assert.Equal("APPROVED", approved.State); Assert.Equal(v.Version, approved.Approval!.RequestVersion);
        Assert.Equal(v.ContentDigest, approved.Approval.ContentDigest); Assert.Equal(f.Reviewer.User, approved.Approval.ActorId);
        Assert.Equal("NOT_DISPATCHED", approved.ExecutionState); Assert.False(await h.Db.StockTransactions.AnyAsync());
        Assert.Equal("TRANSFER_WITHDRAWAL_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.ActionAsync(v.Id, "WITHDRAW", RequestAction(approved)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferRequest_withdraw_reject_close_demand_and_current_two_location_scope_is_enforced()
    {
        await using var h = await Seed(); var f = await TransferRequestSetup(h); var o = Requests(h, f.Options);
        var v = (await o.CreateAsync(f.Create)).Request;
        v = (await o.ActionAsync(v.Id, "SUBMIT", RequestAction(v))).Request;
        var closed = (await o.ActionAsync(v.Id, "WITHDRAW", RequestAction(v))).Request; Assert.Equal("WITHDRAWN", closed.State);
        Assert.Equal("CLOSED", await h.Db.Database.SqlQuery<string>($"""
            SELECT state AS "Value" FROM cp6_transfer_local_v1.demand_versions WHERE tenant_id={h.Tenant} AND budget_key={v.Lines[0].BudgetKey} AND version=2
            """).SingleAsync());
        var other = (await o.CreateAsync(f.Create with { OperationId = Guid.NewGuid(), DraftKey = "other" })).Request;
        other = (await o.ActionAsync(other.Id, "SUBMIT", RequestAction(other))).Request;
        Assert.Equal("REJECTED", (await Requests(f.Reviewer, f.Options).ActionAsync(other.Id, "REJECT", RequestAction(other))).Request.State);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(x => x.SetProperty(v => v.AreaCd, "A"));
        Assert.Empty(await o.ListAsync()); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.ReadAsync(closed.Id))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(f.Create with { OperationId = Guid.NewGuid(), DraftKey = "forbidden" }))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferRequest_lost_ack_original_survives_write_revocation_and_unknown_close_is_terminal()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var f = await TransferRequestSetup(h); var o = Requests(h, f.Options);
        fault.Armed = true; Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(f.Create))).Message);
        await RevokeTransferAction(h, "wms-transfer-request", "add"); var saved = await o.OperationAsync(f.Create.OperationId, false);
        Assert.Equal("COMMITTED", saved.State); Assert.Equal(TransferJson(saved.Result!), TransferJson(await o.CreateAsync(f.Create)));
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(f.Create with { Reason = "changed" }))).Message);
        var unknown = Guid.NewGuid(); Assert.Equal("UNKNOWN", (await o.OperationAsync(unknown, false)).State);
        Assert.Equal("CLOSED", (await o.OperationAsync(unknown, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(f.Create with { OperationId = unknown }))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferRequest_journal_failure_and_final_permission_revocation_rollback_every_effect()
    {
        var hook = new StartWriteHook(); await using var h = await Seed(hook); var f = await TransferRequestSetup(h); var o = Requests(h, f.Options);
        hook.Before = command =>
        {
            if (command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations", StringComparison.Ordinal)) throw new InvalidOperationException("simulated-journal-failure");
            return Task.CompletedTask;
        };
        Assert.Equal("simulated-journal-failure", (await Assert.ThrowsAsync<InvalidOperationException>(() => o.CreateAsync(f.Create))).Message);
        hook.Before = null;
        foreach (var table in new[] { "cp6_transfer_request_v1.heads", "cp6_transfer_request_v1.versions", "cp6_transfer_request_v1.events", "cp6_transfer_local_v1.bindings", "cp6_transfer_local_v1.budgets" })
            Assert.Equal(0, await h.Db.Database.SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM {table} WHERE tenant_id=@tenant", new NpgsqlParameter("tenant", h.Tenant)).SingleAsync());
        Assert.Equal("UNKNOWN", (await o.OperationAsync(f.Create.OperationId, false)).State);
        var injected = false;
        hook.Before = async command =>
        {
            if (injected || !command.CommandText.Contains("INSERT INTO cp6_transfer_request_v1.events", StringComparison.Ordinal)) return;
            injected = true; await using var revoke = command.Connection!.CreateCommand(); revoke.Transaction = command.Transaction;
            revoke.CommandText = "DELETE FROM public.\"Sys_RoleActions\" WHERE \"TenantId\"=@tenant AND \"ActionCode\"='add' AND \"MenuId\"=(SELECT \"MenuId\" FROM public.\"Sys_Menus\" WHERE \"MenuKey\"='wms-transfer-request')";
            var parameter = revoke.CreateParameter(); parameter.ParameterName = "tenant"; parameter.Value = h.Tenant; revoke.Parameters.Add(parameter); await revoke.ExecuteNonQueryAsync();
        };
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(f.Create)); hook.Before = null; Assert.True(injected);
        Assert.Empty(await o.ListAsync()); Assert.Equal("UNKNOWN", (await o.OperationAsync(f.Create.OperationId, false)).State);
        Assert.Equal("DRAFT", (await o.CreateAsync(f.Create)).Request.State);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferRequest_concurrent_draft_key_and_state_decisions_have_one_winner()
    {
        await using var h = await Seed(); var f = await TransferRequestSetup(h); var first = Requests(h, f.Options);
        await using var peerDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); var second = Requests(h with { Db = peerDb }, f.Options);
        async Task<TransferRequestResultV1?> Create(TransferRequestOwnerV1 o)
        { try { return await o.CreateAsync(f.Create with { OperationId = Guid.NewGuid() }); } catch (OrderPriceCorrectionException e) when (e.Message == "TRANSFER_DRAFT_KEY_EXISTS") { return null; } }
        var created = Assert.Single(await Task.WhenAll(Create(first), Create(second)), x => x != null)!;
        var submitted = (await first.ActionAsync(created.Request.Id, "SUBMIT", RequestAction(created.Request))).Request;
        async Task<bool> Decide(TransferRequestOwnerV1 o, string action)
        { try { await o.ActionAsync(submitted.Id, action, RequestAction(submitted)); return true; } catch (OrderPriceCorrectionException e) when (e.Message == "TRANSFER_REQUEST_CHANGED") { return false; } }
        var approved = Requests(f.Reviewer with { Db = peerDb }, f.Options);
        var outcomes = await Task.WhenAll(Decide(first, "WITHDRAW"), Decide(approved, "APPROVE")); Assert.Single(outcomes, x => x);
        var final = await first.ReadAsync(submitted.Id);
        Assert.Contains(final.State, new[] { "WITHDRAWN", "APPROVED" });
        Assert.Equal(final.State == "APPROVED" ? "ACTIVE" : "CLOSED", await h.Db.Database.SqlQuery<string>($"""
            SELECT state AS "Value" FROM cp6_transfer_local_v1.bindings WHERE tenant_id={h.Tenant} AND id={final.Lines[0].BindingId}
            """).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferRequest_actual_HTTP_context_create_submit_approval_and_closed_input_contract()
    {
        await using var h = await Seed(); var f = await TransferRequestSetup(h); var settings = await DocumentHostSettings(new());
        settings["QualitySpecification__EvidenceMode"] = "DEMO"; settings["TransferEnvironment__Mode"] = "SIMULATION"; settings["TransferEnvironment__SandboxScopeId"] = TransferSandbox.ToString();
        settings["TransferRequests__WarehouseSites__0__WarehouseId"] = f.Options.WarehouseSites[0].WarehouseId.ToString();
        settings["TransferRequests__WarehouseSites__0__SiteId"] = f.Options.WarehouseSites[0].SiteId.ToString();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, settings); var cookies = await Login(host, h);
        const string root = "/api/wms/transfer/native-requests-v1";
        using (var response = await host.Send(new HttpRequestMessage(HttpMethod.Get, root))) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using (var request = OperationsRequest(cookies, HttpMethod.Post, root, f.Create))
        { request.Headers.Remove("X-CSRF-Token"); using var response = await host.Send(request); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); }
        var forged = JsonSerializer.SerializeToNode(f.Create, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject(); forged["approverId"] = f.Reviewer.User;
        using (var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, root, forged))) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, root + "/context")))
        { Assert.True(response.Headers.CacheControl?.NoStore); Assert.Equal(2, (await DocumentHttpData<TransferRequestContextV1>(response)).Locations.Length); }
        TransferRequestViewV1 v;
        using (var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, root, f.Create))) v = (await DocumentHttpData<TransferRequestResultV1>(response)).Request;
        using (var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, root + $"/{v.Id}/decisions/SUBMIT", RequestAction(v)))) v = (await DocumentHttpData<TransferRequestResultV1>(response)).Request;
        using (var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, root + $"/{v.Id}/decisions/APPROVE", RequestAction(v)))) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var reviewer = await DocumentReviewerLogin(host, h, f.Reviewer.User);
        using (var response = await host.Send(OperationsRequest(reviewer, HttpMethod.Post, root + $"/{v.Id}/decisions/APPROVE", RequestAction(v)))) Assert.Equal("APPROVED", (await DocumentHttpData<TransferRequestResultV1>(response)).Request.State);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task TransferRequest_department_binding_is_created_in_transaction_and_revocation_hides_original(int type)
    {
        await using var h = await Seed(); var f = await TransferRequestSetup(h); var o = Requests(h, f.Options);
        var hidden = (await o.CreateAsync(f.Create)).Request; var admin = await MappingAdmin(h);
        Assert.Equal(hidden.Id, Assert.Single(await StandaloneAdmin(h, admin).FindObjectsAsync("transfer-request", f.Create.DraftKey, _ => Task.FromResult(h.User), default)).Id);
        await StandaloneScope(h, admin, type);
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey != "wms-transfer-request" && x.ResourceKey != "order").ExecuteUpdateAsync(x => x.SetProperty(v => v.ScopeType, 5));
        Assert.Empty(await o.ListAsync());
        var command = f.Create with { OperationId = Guid.NewGuid(), DraftKey = "department" };
        Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(command))).Message);
        command = command with { DepartmentId = admin.Child }; var created = (await o.CreateAsync(command)).Request;
        Assert.Equal(created.Id, Assert.Single(await o.ListAsync()).Id);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false
            WHERE tenant_id={h.Tenant} AND object_kind='transfer-request' AND object_id={created.Id}
            """);
        Assert.Empty(await o.ListAsync()); Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.OperationAsync(command.OperationId, false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferRequest_exact_extremes_immutable_history_and_stale_sources_do_not_round_or_commit()
    {
        await using var h = await Seed(); var f = await TransferRequestSetup(h); var o = Requests(h, f.Options);
        var c = f.Create with { Lines = [f.Create.Lines[0] with { InputQuantity = "9999999999999.12345678" }] };
        var first = (await o.CreateAsync(c)).Request; Assert.Equal(c.Lines[0].InputQuantity, first.Lines[0].BaseQuantity);
        var small = (await o.EditAsync(first.Id, new(Guid.NewGuid(), first.Revision, first.Etag,
            [c.Lines[0] with { InputQuantity = "0.00000001" }], "最小数量"))).Request;
        Assert.Equal("0.00000001", small.Lines[0].BaseQuantity); Assert.Equal(TransferJson(first), TransferJson(await o.HistoryAsync(first.Id, first.Revision)));
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_transfer_request_v1.versions SET body=body WHERE tenant_id={h.Tenant} AND id={first.Id}
            """));
        await h.Db.Locations.Where(x => x.Id == c.Lines[0].DestinationLocationId).ExecuteUpdateAsync(x => x.SetProperty(v => v.IsBlocked, true));
        Assert.Equal("TRANSFER_LOCATION_BLOCKED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.ActionAsync(small.Id, "SUBMIT", RequestAction(small)))).Message);
        Assert.Equal("DRAFT", (await o.ReadAsync(small.Id)).State);
        Assert.Equal("WITHDRAWN", (await o.ActionAsync(small.Id, "WITHDRAW", RequestAction(small))).Request.State);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferRequest_tenant_and_creator_UUID_scope_prevent_cross_identity_disclosure()
    {
        await using var h = await Seed(); var f = await TransferRequestSetup(h); var o = Requests(h, f.Options); var v = (await o.CreateAsync(f.Create)).Request;
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "wms-transfer-request").ExecuteUpdateAsync(x => x.SetProperty(s => s.ScopeType, 1));
        await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(x => x.SetProperty(s => s.UserName, "renamed-transfer-author"));
        Assert.Equal(v.Id, (await o.ReadAsync(v.Id)).Id);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => Requests(f.Reviewer, f.Options).ReadAsync(v.Id))).Status);
        await using var foreign = await Seed(); await GrantTransfer(foreign);
        await EngineeringAction(foreign, "wms-transfer-request", "query");
        foreign.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "wms-transfer-request", ScopeType = 5 });
        await foreign.Db.SaveChangesAsync(); foreign.Db.ChangeTracker.Clear(); var other = Requests(foreign, f.Options);
        Assert.Equal("UNKNOWN", (await other.OperationAsync(f.Create.OperationId, false)).State);
        Assert.Equal(404, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => other.ReadAsync(v.Id))).Status);
    }
}
