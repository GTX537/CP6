using System.Data.Common;
using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<(OutboundService Service, string Number)> GenerateLinkedOutbound(Harness h)
    {
        await GrantOrderReads(h); var owner = await GrantOperations(h);
        await h.Db.OrderDetails.ExecuteUpdateAsync(s => s.SetProperty(x => x.ProductCd, "SAME")
            .SetProperty(x => x.QtyUnit, "EA").SetProperty(x => x.UnitPriceUnit, "BOX")
            .SetProperty(x => x.Quantity, 2.12345678m));
        var service = OperationsWms(h, owner);
        return (service, await service.CreateFromOrderAsync(h.Number, null));
    }
    private static async Task<Guid> CompleteLinkedSourceFixture(Harness h, string number)
    {
        // This fixture supplies physical shipment facts; generation and lineage are real service writes.
        await h.Db.OutboundOrders.Where(x => x.OutboundNo == number).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, OutboundOrderStatus.Completed));
        await h.Db.OutboundOrderDetails.Where(x => x.OutboundNo == number).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.AllocatedQty, x => x.RequiredQty).SetProperty(x => x.ShippedQty, x => x.RequiredQty)
            .SetProperty(x => x.ShipTxnNo, "OUT-FIXTURE"));
        await GrantRma(h);
        return await h.Db.OutboundOrders.Where(x => x.OutboundNo == number).Select(x => x.Id).SingleAsync();
    }
    private static Task<int> LineageCount(Harness h) => h.Db.Database.SqlQuery<int>($"""
        SELECT count(*)::integer AS "Value" FROM cp6_outbound_lineage_v1.order_lines WHERE tenant_id={h.Tenant}
        """).SingleAsync();

    [CurrentPermissionPostgresFact]
    public async Task OutboundLineage_generation_distinguishes_same_product_lines_and_freezes_actual_quantity_unit()
    {
        await using var h = await Seed(); var generated = await GenerateLinkedOutbound(h);
        var order = await h.Db.Orders.AsNoTracking().SingleAsync();
        var originals = await h.Db.OrderDetails.AsNoTracking().OrderBy(x => x.WebOrderDetailNo).ToArrayAsync();
        Assert.All((await generated.Service.GetOrderAsync(generated.Number))!.Details, x => Assert.Equal("EA", x.UnitCd));
        var id = await CompleteLinkedSourceFixture(h, generated.Number); var source = await RmaApplications(h).SourceAsync(id);
        Assert.Equal(2, source.Lines.Count);
        for (var i = 0; i < originals.Length; i++)
        {
            var line = source.Lines[i].OrderLine!;
            Assert.Equal(originals[i].Id, line.OrderLineId); Assert.Equal(order.Id, line.OrderId);
            Assert.Equal(originals[i].WebOrderDetailNo, line.OrderLineNumber);
            Assert.Equal("2.12345678", line.Quantity); Assert.Equal("EA", line.QuantityUnit);
        }
        Assert.NotEqual(source.Lines[0].OrderLine!.SnapshotId, source.Lines[1].OrderLine!.SnapshotId);
        await h.Db.OrderDetails.ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, 99m).SetProperty(x => x.QtyUnit, "KG"));
        Assert.Equal(source.Version, (await RmaApplications(h).SourceAsync(id)).Version);
        Assert.Equal("2.12345678", (await RmaApplications(h).SourceAsync(id)).Lines[0].OrderLine!.Quantity);
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_outbound_lineage_v1.order_lines WHERE tenant_id={h.Tenant}"));
    }

    [CurrentPermissionPostgresFact]
    public async Task OutboundLineage_edit_uses_explicit_line_identity_and_preserves_removed_history()
    {
        await using var h = await Seed(); var generated = await GenerateLinkedOutbound(h);
        var original = await h.Db.OutboundOrderDetails.AsNoTracking().OrderBy(x => x.LineNo).ToArrayAsync();
        var dto = (await generated.Service.GetOrderAsync(generated.Number))!;
        dto.Details.Reverse(); dto.Details[0].RequiredQty = 1.00000001m;
        await generated.Service.UpdateOrderAsync(generated.Number, dto, null);
        var rows = await h.Db.OutboundOrderDetails.AsNoTracking().OrderBy(x => x.LineNo).ToArrayAsync();
        Assert.Equal(original.Select(x => x.Id), rows.Select(x => x.Id));
        Assert.Equal(2.12345678m, rows[0].RequiredQty); Assert.Equal(1.00000001m, rows[1].RequiredQty);
        dto.Details.RemoveAt(1); // Keep source line 2 even though it is now the only line in the request.
        await generated.Service.UpdateOrderAsync(generated.Number, dto, null);
        Assert.Equal(original[1].Id, (await h.Db.OutboundOrderDetails.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(2, await LineageCount(h));
        var id = await CompleteLinkedSourceFixture(h, generated.Number);
        Assert.Equal(2, Assert.Single((await RmaApplications(h).SourceAsync(id)).Lines).OrderLine!.OrderLineNumber);
    }

    [TenantManagementPostgresTheory]
    [InlineData("product")][InlineData("unit")][InlineData("order")][InlineData("line")][InlineData("duplicate")]
    public async Task OutboundLineage_edit_rejects_source_reassignment_without_changing_stored_rows(string change)
    {
        await using var h = await Seed(); var generated = await GenerateLinkedOutbound(h);
        var dto = (await generated.Service.GetOrderAsync(generated.Number))!;
        switch (change)
        {
            case "product": dto.Details[0].ProductCd = "OTHER"; break;
            case "unit": dto.Details[0].UnitCd = "KG"; break;
            case "order": dto.WebOrderNo = null; break;
            case "line": dto.Details[0].LineNo = 99; break;
            case "duplicate": dto.Details[1].LineNo = dto.Details[0].LineNo; break;
        }
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => generated.Service.UpdateOrderAsync(generated.Number, dto, null));
        Assert.Equal(h.Number, (await generated.Service.GetOrderAsync(generated.Number))!.WebOrderNo);
        Assert.All(await h.Db.OutboundOrderDetails.AsNoTracking().ToArrayAsync(), x => { Assert.Equal("SAME", x.ProductCd); Assert.Equal("EA", x.UnitCd); });
        Assert.Equal(2, await LineageCount(h));
    }

    private sealed class FailSecondOrderLineage : DbCommandInterceptor
    {
        public int Inserts;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO cp6_outbound_lineage_v1.order_lines", StringComparison.Ordinal) && ++Inserts == 2)
                throw new InvalidOperationException("INJECTED_LINEAGE_FAILURE");
            return ValueTask.FromResult(result);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task OutboundLineage_snapshot_failure_rolls_back_the_entire_actual_generation_transaction()
    {
        var failure = new FailSecondOrderLineage(); await using var h = await Seed(failure);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => GenerateLinkedOutbound(h));
        Assert.Equal("INJECTED_LINEAGE_FAILURE", error.Message); Assert.Equal(2, failure.Inserts);
        Assert.Empty(await h.Db.OutboundOrders.ToArrayAsync()); Assert.Empty(await h.Db.OutboundOrderDetails.ToArrayAsync());
        Assert.Empty(await h.Db.WmsSequences.ToArrayAsync()); Assert.Equal(0, await LineageCount(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task OutboundLineage_old_unlinked_source_keeps_original_version_and_is_not_guessed_from_SKU()
    {
        await using var h = await Seed(); var c = await ApplicationCommand(h); var source = await RmaApplications(h).SourceAsync(c.OutboundId);
        var oldLines = source.Lines.Select(x => new { x.Id, x.LineNumber, x.ProductCode, x.ShippedQuantity, x.UnitCode,
            x.WarehouseCode, x.LocationCode, x.LotNumber, x.ShipmentTransaction }).ToArray();
        var oldVersion = MainOwnerOperationJournalV1.Digest(MainOwnerOperationJournalV1.Encode(new
        { Id = source.Id, OutboundNo = source.Number, CustomerCd = source.CustomerCode, WebOrderNo = source.OrderNumber, Lines = oldLines }));
        Assert.Equal(oldVersion, source.Version); Assert.All(source.Lines, x => Assert.Null(x.OrderLine));
        await RmaApplications(h).CreateAsync(c); Assert.Equal(0, await LineageCount(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task OutboundLineage_actual_HTTP_return_source_preserves_links_and_current_authorization()
    {
        await using var h = await Seed(); var generated = await GenerateLinkedOutbound(h);
        var id = await CompleteLinkedSourceFixture(h, generated.Number);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        var path = $"api/wms/rma/native-applications-v1/sources/{id}";
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path)); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var lines = json.RootElement.GetProperty("data").GetProperty("lines");
        Assert.Equal(2, lines.GetArrayLength()); Assert.Equal("EA", lines[0].GetProperty("orderLine").GetProperty("quantityUnit").GetString());
        Assert.NotEqual(lines[0].GetProperty("orderLine").GetProperty("orderLineId").GetGuid(), lines[1].GetProperty("orderLine").GetProperty("orderLineId").GetGuid());
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await using var foreign = await Seed(); await GrantRma(foreign);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => RmaApplications(foreign).SourceAsync(id))).Status);
    }
}
