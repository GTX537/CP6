using System.Net;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static RmaApplicationOwnerV1 RmaApplications(Harness h, CP6Context? db = null)
    {
        var catalog = new AuthorizationCatalogStoreV1(); catalog.RegisterScope("wms-rma", "RMA", [1,2,3,4,5], 1);
        catalog.RegisterFields("wms-rma", [new("CustomerName","CustomerName"),new("OperatorCd","OperatorCd"),new("Remarks","Remarks"),new("ReturnReason","ReturnReason")]); catalog.Seal();
        return new(db ?? h.Db, new OperationsOwnerV1(db ?? h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture));
    }
    private static async Task<RmaApplicationCreateV1> ApplicationCommand(Harness h, decimal shipped = 3.12345678m)
    {
        await GrantRma(h);
        var outbound = new OutboundOrder { OutboundNo = "RETURN-SOURCE", OutboundType = OutboundType.Shipping,
            Status = OutboundOrderStatus.Completed, CustomerCd = "CUSTOMER-1", WarehouseCd = "W1", WebOrderNo = h.Number, Creator = "price-owner" };
        var line = new OutboundOrderDetail { OutboundNo = outbound.OutboundNo, LineNo = 1, ProductCd = "P1",
            RequiredQty = shipped, AllocatedQty = shipped, ShippedQty = shipped, ShipTxnNo = "OUT-SOURCE", UnitCd = "EA", LocationCd = "L1", Creator = "price-owner" };
        h.Db.OutboundOrders.Add(outbound); h.Db.OutboundOrderDetails.Add(line);
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var source = await RmaApplications(h).SourceAsync(outbound.Id);
        return new(Guid.NewGuid(), Guid.NewGuid(), outbound.Id, source.Version, "Damaged packaging", "RETURN_ONLY", [new(line.Id, "1.12345678")], "Private request");
    }
    private static Task<int> ApplicationCount(Harness h) => h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_rma_application_v1.heads WHERE tenant_id={h.Tenant}").SingleAsync();

    [CurrentPermissionPostgresFact]
    public async Task RmaApplication_create_submit_keeps_immutable_versions_exact_quantity_and_no_authorization_effects()
    {
        await using var h = await Seed(); var c = await ApplicationCommand(h); var owner = RmaApplications(h);
        var created = await owner.CreateAsync(c); Assert.Equal("DRAFT", created.State);
        var v1 = await owner.ReadAsync(c.ApplicationId); Assert.Equal("1.12345678", Assert.Single(v1.Lines).RequestedQuantity);
        Assert.Equal("3.12345678", v1.Lines[0].Source.ShippedQuantity); Assert.Equal(h.User, v1.RequestedBy);
        var command = new RmaApplicationSubmitV1(Guid.NewGuid(), 1);
        var submitted = await owner.SubmitAsync(c.ApplicationId, command, created.Etag);
        Assert.Equal("SUBMITTED", submitted.State); Assert.Equal(2, submitted.Version);
        Assert.Equal("NOT_GRANTED", (await owner.ReadAsync(c.ApplicationId)).Authorization);
        var historical = await owner.ReadAsync(c.ApplicationId, 1);
        Assert.Equal("DRAFT", historical.State); Assert.Null(historical.SubmittedAt); Assert.Equal(v1.RequestedAt, historical.RequestedAt);
        Assert.Equal(created, await owner.CreateAsync(c)); Assert.Equal(submitted, await owner.SubmitAsync(c.ApplicationId, command, created.Etag));
        Assert.Single(await owner.ListAsync()); Assert.Equal(1, await ApplicationCount(h));
        Assert.Empty(await h.Db.RmaHeaders.ToArrayAsync()); Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
        Assert.Empty(await h.Db.WmsSequences.ToArrayAsync());
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CreateAsync(c with { Reason = "Changed" }))).Message);
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_rma_application_v1.versions SET state='SUBMITTED' WHERE tenant_id={h.Tenant}"));
    }
    [TenantManagementPostgresTheory]
    [InlineData("0")][InlineData("-1")][InlineData("1.123456789")][InlineData("10000000000000")][InlineData("1e0")]
    public async Task RmaApplication_invalid_quantity_fails_before_any_application(string quantity)
    {
        await using var h = await Seed(); var c = await ApplicationCommand(h); var line = c.Lines[0] with { Quantity = quantity };
        Assert.Equal("RMA_QUANTITY_INVALID", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => RmaApplications(h).CreateAsync(c with { Lines = [line] }))).Message);
        Assert.Equal(0, await ApplicationCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaApplication_missing_or_stale_source_and_excess_quantity_fail_with_zero_effect()
    {
        await using var h = await Seed(); var c = await ApplicationCommand(h); var o = RmaApplications(h);
        Assert.Equal("RMA_SOURCE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(c with { SourceVersion = "stale" }))).Message);
        Assert.Equal("RMA_QUANTITY_EXCEEDS_SHIPMENT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(c with { Lines = [c.Lines[0] with { Quantity = "4" }] }))).Message);
        await h.Db.OutboundOrderDetails.ExecuteUpdateAsync(s => s.SetProperty(x => x.UnitCd, (string?)null));
        Assert.Equal("RMA_SOURCE_LINE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(c))).Message);
        Assert.Equal(0, await ApplicationCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaApplication_full_decimal_range_is_not_narrowed_to_legacy_precision()
    {
        await using var h = await Seed(); var c = await ApplicationCommand(h, 9999999999999.99999999m);
        c = c with { Lines = [c.Lines[0] with { Quantity = "9999999999999.99999999" }] };
        await RmaApplications(h).CreateAsync(c);
        Assert.Equal("9999999999999.99999999", (await RmaApplications(h).ReadAsync(c.ApplicationId)).Lines[0].RequestedQuantity);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaApplication_current_scope_and_fields_apply_to_lists_reads_and_original_results()
    {
        await using var h = await Seed(); var c = await ApplicationCommand(h); var o = RmaApplications(h); await o.CreateAsync(c);
        foreach (var field in new[] { "ReturnReason", "Remarks", "OperatorCd" })
            h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "wms-rma", FieldName = field, Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var view = await o.ReadAsync(c.ApplicationId); Assert.Null(view.Reason); Assert.Null(view.Remarks); Assert.Null(view.RequestedBy);
        Assert.Equal("COMMITTED", (await o.OperationAsync(c.OperationId)).State);
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();
        Assert.Empty(await o.ListAsync());
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.OperationAsync(c.OperationId))).Status);
        await using var foreign = await Seed(); await GrantRma(foreign);
        Assert.Equal(404, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => RmaApplications(foreign).ReadAsync(c.ApplicationId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaApplication_submit_checks_current_source_and_compare_and_swap()
    {
        await using var h = await Seed(); var c = await ApplicationCommand(h); var o = RmaApplications(h); var created = await o.CreateAsync(c);
        var command = new RmaApplicationSubmitV1(Guid.NewGuid(), 1);
        Assert.Equal(428, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.SubmitAsync(c.ApplicationId, command, null))).Status);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.SubmitAsync(c.ApplicationId, command, "W/" + created.Etag))).Status);
        await h.Db.OutboundOrderDetails.ExecuteUpdateAsync(s => s.SetProperty(x => x.ShippedQty, 2m));
        Assert.Equal("RMA_SOURCE_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.SubmitAsync(c.ApplicationId, command, created.Etag))).Message);
        Assert.Equal("DRAFT", (await o.ReadAsync(c.ApplicationId)).State);
        Assert.Equal("UNKNOWN", (await o.OperationAsync(command.OperationId)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaApplication_concurrent_submit_commits_once_and_loser_keeps_unknown_original()
    {
        await using var h = await Seed(); var c = await ApplicationCommand(h); var first = RmaApplications(h); var created = await first.CreateAsync(c);
        await using var other = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); var second = RmaApplications(h, other);
        var a = new RmaApplicationSubmitV1(Guid.NewGuid(), 1); var b = new RmaApplicationSubmitV1(Guid.NewGuid(), 1);
        var outcomes = await Task.WhenAll(Record.ExceptionAsync(() => first.SubmitAsync(c.ApplicationId, a, created.Etag)),
            Record.ExceptionAsync(() => second.SubmitAsync(c.ApplicationId, b, created.Etag)));
        Assert.Single(outcomes, x => x == null); Assert.Equal(412, Assert.IsType<OrderPriceCorrectionException>(Assert.Single(outcomes, x => x != null)).Status);
        Assert.Equal(2, (await first.ReadAsync(c.ApplicationId)).Version);
        var states = new[] { (await first.OperationAsync(a.OperationId)).State, (await first.OperationAsync(b.OperationId)).State };
        Assert.Contains("COMMITTED", states); Assert.Contains("UNKNOWN", states);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaApplication_lost_commit_ack_recovers_original_after_add_revocation()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var c = await ApplicationCommand(h); var o = RmaApplications(h);
        fault.Armed = true; Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(c))).Message);
        await RevokeTransferAction(h, "wms-rma", "add");
        var result = (await o.OperationAsync(c.OperationId)).Result; Assert.NotNull(result); Assert.Equal(result, await o.CreateAsync(c));
        Assert.Equal(1, await ApplicationCount(h));
        await RevokeTransferAction(h, "wms-rma", "query");
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.OperationAsync(c.OperationId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaApplication_journal_failure_rolls_back_application_and_version_together()
    {
        var hook = new StartWriteHook(); await using var h = await Seed(hook); var c = await ApplicationCommand(h); var o = RmaApplications(h);
        hook.Before = command =>
        {
            if (command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations")) throw new IOException("journal unavailable");
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(c)); hook.Before = null;
        Assert.Equal(0, await ApplicationCount(h));
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_rma_application_v1.versions WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal("UNKNOWN", (await o.OperationAsync(c.OperationId)).State);
        Assert.Equal("DRAFT", (await o.CreateAsync(c)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaApplication_uncommitted_close_prevents_later_write_with_same_operation()
    {
        await using var h = await Seed(); var c = await ApplicationCommand(h); var o = RmaApplications(h);
        Assert.Equal("UNKNOWN", (await o.OperationAsync(c.OperationId)).State);
        Assert.Equal("CLOSED", (await o.OperationAsync(c.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.CreateAsync(c))).Message);
        Assert.Equal(0, await ApplicationCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaApplication_actual_HTTP_rejects_authority_fields_and_submits_without_legacy_effects()
    {
        await using var h = await Seed(); var c = await ApplicationCommand(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        const string path = "api/wms/rma/native-applications-v1";
        var json = JsonSerializer.SerializeToNode(c, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        var bad = json.DeepClone().AsObject(); bad["state"] = "AUTHORIZED";
        using var rejected = await host.Send(OperationsRequest(cookies, HttpMethod.Post, path, bad)); Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var created = await host.Send(OperationsRequest(cookies, HttpMethod.Post, path, json)); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl?.NoStore); Assert.NotNull(created.Headers.ETag);
        var submit = OperationsRequest(cookies, HttpMethod.Post, $"{path}/{c.ApplicationId}/submit", new RmaApplicationSubmitV1(Guid.NewGuid(), 1));
        submit.Headers.IfMatch.Add(created.Headers.ETag!);
        using var submitted = await host.Send(submit); Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        using var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get, $"{path}/{c.ApplicationId}"));
        var text = await read.Content.ReadAsStringAsync(); Assert.Contains("SUBMITTED", text); Assert.Contains("NOT_GRANTED", text);
        Assert.Empty(await h.Db.RmaHeaders.ToArrayAsync()); Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
    }
}
