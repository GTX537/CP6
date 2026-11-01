using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms.Transfer;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<(TransferFixture Fixture, TransferRequestViewV1 Request, Guid Stock)> TransferStageSetup(Harness h, string amount = "10.00000000")
    {
        var f = await TransferRequestSetup(h);
        foreach (var action in new[] { "prepare", "execute", "close-stage" }) await EngineeringAction(h, "wms-transfer-request", action);
        var o = Requests(h, f.Options); var v = (await o.CreateAsync(f.Create with { Lines = [f.Create.Lines[0] with { InputQuantity = amount }] })).Request;
        v = (await o.ActionAsync(v.Id, "SUBMIT", RequestAction(v))).Request;
        v = (await Requests(f.Reviewer, f.Options).ActionAsync(v.Id, "APPROVE", RequestAction(v))).Request;
        var product = await h.Db.ProductMasters.Where(x => x.Id == v.Lines[0].Product.ProductRef.Id).Select(x => x.ProductCd).SingleAsync();
        var unit = await Units(h).ReadAsync(Guid.Parse(v.Lines[0].Product.BaseUomRef.Id), null);
        var stock = new Stock { WarehouseCd = "TR1", LocationCd = "FROM", ProductCd = product, LotNo = "LOT-A", UnitCd = unit.Profile.Code,
            PhysicalQty = 100, AllocatedQty = 20, AvailableQty = 80, OwnerType = "SELF", QcStatus = StockQcStatus.Passed, UnitPrice = 1.2345m, ReceiveDate = new DateTime(2026, 10, 1), Creator = "price-owner" };
        h.Db.Stocks.Add(stock); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); return (f, v, stock.Id);
    }
    private static TransferStagePrepareV1 Prepare(TransferRequestViewV1 v, Guid pool, string quantity = "10.00000000", string from = "0.00000000")
        => new(Guid.NewGuid(), v.Revision, v.Etag, v.ContentDigest, "SIMULATED-SCANNER", Guid.NewGuid().ToString(), [new(v.Lines[0].Id, pool, from, quantity)], "模拟现场调拨验收");
    private static TransferStageDecisionV1 StageDecision(TransferRequestViewV1 v, bool close = false)
        => new(Guid.NewGuid(), v.Revision, v.Etag, v.Stages![0].Digest, close, "现场执行或确认未发生");

    [CurrentPermissionPostgresFact]
    public async Task TransferStage_actual_double_leg_partial_then_complete_preserves_reservations_metadata_and_original()
    {
        await using var h = await Seed(); var f = await TransferStageSetup(h); var o = Requests(h, f.Fixture.Options);
        h.Db.Locations.Add(new() { WarehouseCd = "TR1", LocationCd = "ROOT", CapacityQty = 100 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await h.Db.Locations.Where(x => x.LocationCd == "FROM" || x.LocationCd == "TO").ExecuteUpdateAsync(x => x.SetProperty(l => l.ParentLocationCd, "ROOT"));
        Assert.Equal(f.Stock, Assert.Single(await o.StockChoicesAsync(f.Request.Id)).StockId);
        var prepare = Prepare(f.Request, f.Stock, "4.00000000"); var prepared = (await o.PrepareStageAsync(f.Request.Id, prepare)).Request;
        Assert.Equal("PREPARED", prepared.Stages![0].State); Assert.False(await h.Db.StockTransactions.AnyAsync());
        var command = StageDecision(prepared); var result = await o.DecideStageAsync(prepared.Id, prepared.Stages[0].Id, true, command);
        Assert.Equal("PARTIALLY_MOVED", result.Request.ExecutionState); Assert.Equal(TransferJson(result), TransferJson(await o.DecideStageAsync(prepared.Id, prepared.Stages[0].Id, true, command)));
        var source = await h.Db.Stocks.AsNoTracking().SingleAsync(x => x.Id == f.Stock); var target = await h.Db.Stocks.AsNoTracking().SingleAsync(x => x.LocationCd == "TO");
        Assert.Equal(96, source.PhysicalQty); Assert.Equal(20, source.AllocatedQty); Assert.Equal(76, source.AvailableQty);
        Assert.Equal(4, target.PhysicalQty); Assert.Equal(source.UnitPrice, target.UnitPrice); Assert.Equal(source.ReceiveDate, target.ReceiveDate); Assert.Equal(source.QcStatus, target.QcStatus);
        Assert.Equal(2, await h.Db.StockTransactions.CountAsync()); Assert.Equal(0, await h.Db.StockTransactions.SumAsync(x => x.Qty));
        var next = (await o.PrepareStageAsync(result.Request.Id, Prepare(result.Request, f.Stock, "6.00000000", "4.00000000"))).Request;
        var second = next.Stages!.Last(); var end = await o.DecideStageAsync(next.Id, second.Id, true, StageDecision(next) with { StageDigest = second.Digest });
        Assert.Equal("COMPLETED", end.Request.ExecutionState); Assert.Equal(100, await h.Db.Stocks.SumAsync(x => x.PhysicalQty));
        Assert.Equal(4, await h.Db.StockTransactions.CountAsync()); Assert.Equal(TransferJson(result), TransferJson((await o.OperationAsync(command.OperationId, false)).Result!));
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferStage_ranges_scan_uniqueness_close_and_posted_cannot_be_cancelled()
    {
        await using var h = await Seed(); var f = await TransferStageSetup(h); var o = Requests(h, f.Fixture.Options);
        var c = Prepare(f.Request, f.Stock); var v = (await o.PrepareStageAsync(f.Request.Id, c)).Request;
        Assert.Equal("TRANSFER_REQUEST_RANGE_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.PrepareStageAsync(v.Id, Prepare(v, f.Stock)))).Message);
        Assert.Equal("TRANSFER_PHYSICAL_NO_EFFECT_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.DecideStageAsync(v.Id, v.Stages![0].Id, false, StageDecision(v)))).Message);
        var closed = (await o.DecideStageAsync(v.Id, v.Stages![0].Id, false, StageDecision(v, true))).Request;
        Assert.Equal("CLOSED_NO_EFFECT", closed.Stages![0].State); Assert.False(await h.Db.StockTransactions.AnyAsync());
        Assert.Equal("TRANSFER_SCAN_ALREADY_BOUND", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.PrepareStageAsync(v.Id, c with { OperationId = Guid.NewGuid(), ExpectedRevision = closed.Revision, Etag = closed.Etag }))).Message);
        var next = (await o.PrepareStageAsync(closed.Id, Prepare(closed, f.Stock))).Request; var stage = next.Stages!.Last();
        var done = (await o.DecideStageAsync(next.Id, stage.Id, true, StageDecision(next) with { StageDigest = stage.Digest })).Request;
        Assert.Equal("TRANSFER_STAGE_TERMINAL", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.DecideStageAsync(done.Id, stage.Id, false, StageDecision(done, true) with { StageDigest = stage.Digest }))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferStage_second_leg_and_journal_failures_rollback_stock_stage_history_and_sequences()
    {
        var hook = new StartWriteHook(); await using var h = await Seed(hook); var f = await TransferStageSetup(h); var o = Requests(h, f.Fixture.Options);
        var v = (await o.PrepareStageAsync(f.Request.Id, Prepare(f.Request, f.Stock))).Request;
        // A destination with a different owner cannot absorb another party's stock.
        var source = await h.Db.Stocks.AsNoTracking().SingleAsync();
        h.Db.Stocks.Add(new() { WarehouseCd = "TR1", LocationCd = "TO", ProductCd = source.ProductCd, LotNo = source.LotNo, UnitCd = source.UnitCd, OwnerType = "CUSTOMER", OwnerCd = "OTHER" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal("STOCK_MOVE_METADATA_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.DecideStageAsync(v.Id, v.Stages![0].Id, true, StageDecision(v)))).Message);
        await h.Db.Stocks.Where(x => x.LocationCd == "TO").ExecuteDeleteAsync();
        hook.Before = command => command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations", StringComparison.Ordinal)
            ? throw new InvalidOperationException("simulated-journal-failure") : Task.CompletedTask;
        var c = StageDecision(v); await Assert.ThrowsAsync<InvalidOperationException>(() => o.DecideStageAsync(v.Id, v.Stages![0].Id, true, c)); hook.Before = null;
        Assert.Equal(100, await h.Db.Stocks.SumAsync(x => x.PhysicalQty)); Assert.False(await h.Db.StockTransactions.AnyAsync());
        Assert.Equal("PREPARED", (await o.ReadAsync(v.Id)).Stages![0].State); Assert.Equal("UNKNOWN", (await o.OperationAsync(c.OperationId, false)).State);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_stock_return_v1.movement_sequences WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferStage_capacity_reservation_quality_and_approver_revocation_fail_at_current_decision()
    {
        await using var h = await Seed(); var f = await TransferStageSetup(h); var o = Requests(h, f.Fixture.Options);
        var v = (await o.PrepareStageAsync(f.Request.Id, Prepare(f.Request, f.Stock))).Request;
        await h.Db.Locations.Where(x => x.LocationCd == "TO").ExecuteUpdateAsync(x => x.SetProperty(l => l.CapacityQty, 10).SetProperty(l => l.ReservedCapacityQty, 1));
        Assert.Equal("TRANSFER_LOCATION_CAPACITY_EXCEEDED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.DecideStageAsync(v.Id, v.Stages![0].Id, true, StageDecision(v)))).Message);
        await h.Db.Locations.Where(x => x.LocationCd == "TO").ExecuteUpdateAsync(x => x.SetProperty(l => l.ReservedCapacityQty, 0));
        await h.Db.Stocks.ExecuteUpdateAsync(x => x.SetProperty(s => s.AllocatedQty, 95).SetProperty(s => s.AvailableQty, 5));
        Assert.Equal("TRANSFER_STOCK_QUANTITY_UNAVAILABLE", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.DecideStageAsync(v.Id, v.Stages![0].Id, true, StageDecision(v)))).Message);
        await h.Db.Stocks.ExecuteUpdateAsync(x => x.SetProperty(s => s.AllocatedQty, 20).SetProperty(s => s.AvailableQty, 80).SetProperty(s => s.RecallFlag, true));
        Assert.Equal("TRANSFER_STOCK_QUALITY_BLOCKED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.DecideStageAsync(v.Id, v.Stages![0].Id, true, StageDecision(v)))).Message);
        await h.Db.Sys_Users.Where(x => x.Id == f.Fixture.Reviewer.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.Enable, false));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.DecideStageAsync(v.Id, v.Stages![0].Id, true, StageDecision(v)));
        Assert.False(await h.Db.StockTransactions.AnyAsync());
        // Closing an unexecuted stage remains possible after approval/source revocation.
        Assert.Equal("CLOSED_NO_EFFECT", (await o.DecideStageAsync(v.Id, v.Stages![0].Id, false, StageDecision(v, true))).Request.Stages![0].State);
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferStage_lost_commit_response_recovers_original_after_execute_revocation_without_second_move()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var f = await TransferStageSetup(h); var o = Requests(h, f.Fixture.Options);
        var v = (await o.PrepareStageAsync(f.Request.Id, Prepare(f.Request, f.Stock))).Request; var c = StageDecision(v);
        fault.Armed = true; Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.DecideStageAsync(v.Id, v.Stages![0].Id, true, c))).Message);
        await RevokeTransferAction(h, "wms-transfer-request", "execute"); var original = await o.OperationAsync(c.OperationId, false);
        Assert.Equal("COMMITTED", original.State); Assert.Equal("COMPLETED", original.Result!.Request.ExecutionState);
        Assert.Equal(TransferJson(original.Result), TransferJson(await o.DecideStageAsync(v.Id, v.Stages![0].Id, true, c))); Assert.Equal(2, await h.Db.StockTransactions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferStage_concurrent_execute_and_close_have_one_atomic_winner()
    {
        await using var h = await Seed(); var f = await TransferStageSetup(h); var o = Requests(h, f.Fixture.Options);
        var v = (await o.PrepareStageAsync(f.Request.Id, Prepare(f.Request, f.Stock))).Request;
        await using var otherDb = server.Context(new CP6.Core.Services.Common.TenantContext { CurrentTenantId = h.Tenant });
        var peer = Requests(h with { Db = otherDb }, f.Fixture.Options);
        async Task<bool> Decide(TransferRequestOwnerV1 owner, bool execute)
        {
            try { await owner.DecideStageAsync(v.Id, v.Stages![0].Id, execute, StageDecision(v, !execute)); return true; }
            catch (OrderPriceCorrectionException e) when (e.Message == "TRANSFER_REQUEST_CHANGED") { return false; }
        }
        Assert.Single(await Task.WhenAll(Decide(o, true), Decide(peer, false)), x => x);
        var current = await o.ReadAsync(v.Id); Assert.Equal(current.Stages![0].State == "POSTED" ? 2 : 0, await h.Db.StockTransactions.CountAsync());
        Assert.Equal(100, await h.Db.Stocks.SumAsync(x => x.PhysicalQty));
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferStage_multi_segment_precision_current_scope_and_second_write_failure_are_atomic()
    {
        var hook = new StartWriteHook(); await using var h = await Seed(hook); var f = await TransferStageSetup(h, "0.00000003"); var o = Requests(h, f.Fixture.Options);
        var c = Prepare(f.Request, f.Stock, "0.00000001"); c = c with { Lines = [.. c.Lines, c.Lines[0] with { RequestFrom = "0.00000001", Quantity = "0.00000002" }] };
        var v = (await o.PrepareStageAsync(f.Request.Id, c)).Request;
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(x => x.SetProperty(s => s.AreaCd, "A"));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => o.DecideStageAsync(v.Id, v.Stages![0].Id, true, StageDecision(v)))).Status);
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(x => x.SetProperty(s => s.AreaCd, "*"));
        var writes = 0;
        hook.Before = command =>
        {
            if (command.CommandText.Contains("INSERT INTO cp6_stock_return_v1.movement_sequences", StringComparison.Ordinal))
                if (++writes == 2) throw new InvalidOperationException("simulated-second-leg-failure");
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => o.DecideStageAsync(v.Id, v.Stages![0].Id, true, StageDecision(v))); hook.Before = null;
        Assert.Equal(2, writes); Assert.False(await h.Db.StockTransactions.AnyAsync()); Assert.Equal(100, await h.Db.Stocks.SumAsync(x => x.PhysicalQty));
        var moved = (await o.DecideStageAsync(v.Id, v.Stages![0].Id, true, StageDecision(v))).Request;
        Assert.Equal("COMPLETED", moved.ExecutionState); Assert.Equal(0.00000003m, await h.Db.Stocks.Where(x => x.LocationCd == "TO").Select(x => x.PhysicalQty).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferStage_actual_HTTP_uses_authenticated_performer_and_returns_real_movement_ids()
    {
        await using var h = await Seed(); var f = await TransferStageSetup(h); var settings = await DocumentHostSettings(new());
        settings["QualitySpecification__EvidenceMode"] = "DEMO"; settings["TransferEnvironment__Mode"] = "SIMULATION"; settings["TransferEnvironment__SandboxScopeId"] = TransferSandbox.ToString();
        settings["TransferRequests__WarehouseSites__0__WarehouseId"] = f.Fixture.Options.WarehouseSites[0].WarehouseId.ToString();
        settings["TransferRequests__WarehouseSites__0__SiteId"] = f.Fixture.Options.WarehouseSites[0].SiteId.ToString();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, settings); var cookies = await Login(host, h);
        var root = "/api/wms/transfer/native-requests-v1/" + f.Request.Id; var c = Prepare(f.Request, f.Stock);
        var forged = JsonSerializer.SerializeToNode(c, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject(); forged["performerId"] = f.Fixture.Reviewer.User;
        using (var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, root + "/stages", forged))) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        TransferRequestViewV1 v;
        using (var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, root + "/stages", c))) v = (await DocumentHttpData<TransferRequestResultV1>(response)).Request;
        Assert.Equal(h.User, v.Stages![0].PerformerId);
        var incomplete = JsonSerializer.SerializeToNode(StageDecision(v), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject(); incomplete.Remove("physicalNotOccurred");
        using (var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, root + "/stages/" + v.Stages[0].Id + "/execute", incomplete))) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, root + "/stages/" + v.Stages[0].Id + "/execute", StageDecision(v))))
        {
            var result = await DocumentHttpData<TransferRequestResultV1>(response); Assert.Equal("COMPLETED", result.Request.ExecutionState);
            var movement = Assert.Single(result.Request.Stages![0].Movements);
            Assert.Equal(2, await h.Db.StockTransactions.CountAsync(x => x.Id == movement.OutMovementId || x.Id == movement.InMovementId));
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task TransferStage_cross_warehouse_two_large_lines_are_one_atomic_group_without_total_narrowing()
    {
        await using var h = await Seed(); var f = await TransferRequestSetup(h);
        foreach (var action in new[] { "prepare", "execute" }) await EngineeringAction(h, "wms-transfer-request", action);
        var warehouse = new Warehouse { WarehouseCd = "TR2", WarehouseName = "模拟目的仓" };
        var location = new Location { WarehouseCd = "TR2", LocationCd = "REMOTE", AreaCd = "B" };
        h.Db.Warehouses.Add(warehouse); h.Db.Locations.Add(location); h.Db.WmsRoleScopes.Add(new() { RoleId = 72, WarehouseCd = "TR2", AreaCd = "*" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var options = new TransferRequestOptionsV1 { WarehouseSites = [.. f.Options.WarehouseSites, new(warehouse.Id, f.Options.WarehouseSites[0].SiteId)] };
        var o = Requests(h, options); const string a = "9999999999999.12345678", b = "9000000000000.00000001";
        var first = f.Create.Lines[0] with { DestinationWarehouseId = warehouse.Id, DestinationLocationId = location.Id, InputQuantity = a };
        var v = (await o.CreateAsync(f.Create with { Lines = [first, first with { ClientLineKey = "second", InputQuantity = b }] })).Request;
        v = (await o.ActionAsync(v.Id, "SUBMIT", RequestAction(v))).Request; v = (await Requests(f.Reviewer, options).ActionAsync(v.Id, "APPROVE", RequestAction(v))).Request;
        var product = await h.Db.ProductMasters.Where(x => x.Id == v.Lines[0].Product.ProductRef.Id).Select(x => x.ProductCd).SingleAsync();
        var unit = await Units(h).ReadAsync(Guid.Parse(v.Lines[0].Product.BaseUomRef.Id), null);
        foreach (var (lot, amount) in new[] { ("LARGE-A", 9999999999999.12345678m), ("LARGE-B", 9000000000000.00000001m) })
            h.Db.Stocks.Add(new() { WarehouseCd = "TR1", LocationCd = "FROM", ProductCd = product, LotNo = lot, UnitCd = unit.Profile.Code, PhysicalQty = amount, AvailableQty = amount, QcStatus = StockQcStatus.Passed });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); var pools = await h.Db.Stocks.AsNoTracking().ToDictionaryAsync(x => x.LotNo);
        var prepare = new TransferStagePrepareV1(Guid.NewGuid(), v.Revision, v.Etag, v.ContentDigest, "SIM-WIDE", "one-occurrence",
            v.Lines.Select(x => new TransferStageSelectionV1(x.Id, pools[x.BaseQuantity == a ? "LARGE-A" : "LARGE-B"].Id, "0.00000000", x.BaseQuantity)).ToArray(), "模拟大数量跨仓验收");
        v = (await o.PrepareStageAsync(v.Id, prepare)).Request; v = (await o.DecideStageAsync(v.Id, v.Stages![0].Id, true, StageDecision(v))).Request;
        Assert.Equal("COMPLETED", v.ExecutionState); Assert.Equal(4, await h.Db.StockTransactions.CountAsync());
        Assert.Equal(18999999999999.12345679m, await h.Db.Stocks.Where(x => x.WarehouseCd == "TR2").SumAsync(x => x.PhysicalQty));
        Assert.Equal(0, await h.Db.Stocks.Where(x => x.WarehouseCd == "TR1").SumAsync(x => x.PhysicalQty));
    }
}
