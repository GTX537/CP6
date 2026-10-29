using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record FulfilmentFixture(OutboundService Service, string Number, Guid Id, Guid Product, UomProfileVersionV1 Unit);
    private async Task<FulfilmentFixture> AllocateFulfilment(Harness h, string numerator = "1", string denominator = "1", bool binding = true,
        decimal quantity = 2.12345678m, int scale = 18, bool onlyLastLine = false, bool manualSource = false, string? formalCustomer = null)
    {
        await GrantOrderReads(h); await GrantOperations(h); await GrantUomProfile(h); await GrantProductUnitEdit(h);
        var unit = await MakeUnit(Units(h), BaseUnit(scale: scale));
        if (numerator != "1" || denominator != "1") unit = await MakeUnit(Units(h), BaseUnit("BOX") with
            { Mode = "RATIO", BaseRef = unit.Reference, Numerator = numerator, Denominator = denominator });
        var product = await SeedIdentityProduct(h);
        await h.Db.ProductMasters.Where(x => x.Id == product).ExecuteUpdateAsync(s => s.SetProperty(x => x.QtyUnit, unit.Profile.Code));
        var identity = await ProductIdentity(h).ReadAsync(product);
        if (binding) await ProductUnits(h).BindAsync(new() { OperationId = Guid.NewGuid(), ProductId = product,
            ExpectedBindingVersion = "absent", ProductVersion = identity.Version, ProfileRef = unit.Reference });
        var code = await h.Db.ProductMasters.Where(x => x.Id == product).Select(x => x.ProductCd).SingleAsync();
        await h.Db.OrderDetails.ExecuteUpdateAsync(s => s.SetProperty(x => x.ProductCd, code).SetProperty(x => x.QtyUnit, unit.Profile.Code)
            .SetProperty(x => x.Quantity, quantity));
        if (formalCustomer != null) await h.Db.Orders.ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerCd, formalCustomer));
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var resource in new[] { "order", "wms-outbound-order", "product", "uom-profile", "erp-business-partner" })
        {
            catalog.RegisterScope(resource, resource, [1,2,3,4,5], 5);
            catalog.RegisterFields(resource, resource == "order" ? [new("UnitPrice","Price"), new("Amount","Amount")]
                : resource == "wms-outbound-order" ? [new("UnitPrice","Price")] : resource == "erp-business-partner" ? [new("BpName","Name")] : []);
        }
        catalog.Seal(); var service = OperationsWms(h, new(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture));
        h.Db.Locations.Add(new() { WarehouseCd = "W01", LocationCd = "L" });
        h.Db.Stocks.Add(new() { WarehouseCd = "W01", LocationCd = "L", ProductCd = code, LotNo = "LOT-A", UnitCd = unit.Profile.Code,
            PhysicalQty = 10m, AvailableQty = 10m, UnitPrice = 3m });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await BindStandalone(h, await MappingAdmin(h), "stock", await h.Db.Stocks.Select(x => x.Id).SingleAsync());
        var number = manualSource ? await service.CreateOrderAsync(new() { OutboundType = OutboundType.Shipping,
            WebOrderNo = h.Number, CustomerCd = "C", WarehouseCd = "W01", Details = [new() { LineNo = 1,
                ProductCd = code, UnitCd = unit.Profile.Code, RequiredQty = quantity }] }, null)
            : await service.CreateFromOrderAsync(h.Number, null);
        if (onlyLastLine)
        {
            var dto = (await service.GetOrderAsync(number))!; dto.Details.RemoveAt(0);
            await service.UpdateOrderAsync(number, dto, null);
        }
        await service.ConfirmOrderAsync(number, null); await service.AllocateAsync(number, null);
        await GrantRma(h);
        return new(service, number, await h.Db.OutboundOrders.Where(x => x.OutboundNo == number).Select(x => x.Id).SingleAsync(), product, unit);
    }
    private static Task<int> FulfilmentCount(Harness h) => h.Db.Database.SqlQuery<int>($"""
        SELECT count(*)::integer AS "Value" FROM cp6_outbound_fulfilment_v1.snapshots WHERE tenant_id={h.Tenant}
        """).SingleAsync();
    private static async Task AssertFulfilmentUnshipped(Harness h)
    {
        Assert.Equal(0, await FulfilmentCount(h)); Assert.Empty(await h.Db.ShippingPackages.ToArrayAsync());
        Assert.Equal(OutboundOrderStatus.Allocated, await h.Db.OutboundOrders.Select(x => x.Status).SingleAsync());
        Assert.All(await h.Db.OutboundOrderDetails.AsNoTracking().ToArrayAsync(), x => { Assert.Equal(0, x.ShippedQty); Assert.Null(x.ShipTxnNo); });
        Assert.Equal(10m, await h.Db.Stocks.Select(x => x.PhysicalQty).SingleAsync());
        Assert.Equal(2, await h.Db.StockTransactions.CountAsync()); // Only the real reservations survive.
    }

    [CurrentPermissionPostgresFact]
    public async Task OutboundFulfilment_integer_unit_accepts_database_trailing_zeros_and_exposes_only_shipped_order_lines()
    {
        await using var h = await Seed(); var f = await AllocateFulfilment(h, quantity: 2m, scale: 0, onlyLastLine: true);
        await f.Service.ShipAsync(f.Number, new() { CaptureReturnSource = true }, null);
        var snapshot = await RmaApplications(h).FulfilmentAsync(f.Id); var line = Assert.Single(snapshot.Lines);
        Assert.Equal("2", line.BaseQty.Value);
        using var order = JsonDocument.Parse(snapshot.Facts.Single(x => x.Reference == line.OrderRef).OriginalJson);
        var sources = order.RootElement.GetProperty("lines"); Assert.Equal(1, sources.GetArrayLength());
        Assert.Equal(2, sources[0].GetProperty("number").GetInt32()); Assert.Equal(line.OrderLineId, sources[0].GetProperty("id").GetString());
    }
    [CurrentPermissionPostgresFact]
    public async Task OutboundFulfilment_manual_order_number_cannot_substitute_for_exact_lineage()
    {
        await using var h = await Seed(); var f = await AllocateFulfilment(h, manualSource: true);
        Assert.Equal("FULFILMENT_ORDER_LINE_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            f.Service.ShipAsync(f.Number, new() { CaptureReturnSource = true }, null))).Message);
        Assert.Equal(0, await FulfilmentCount(h)); Assert.Equal(10m, await h.Db.Stocks.Select(x => x.PhysicalQty).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task OutboundFulfilment_real_shipping_captures_exact_sources_ranges_lot_and_immutable_history()
    {
        await using var h = await Seed(); var f = await AllocateFulfilment(h, numerator: "12");
        var package = await f.Service.ShipAsync(f.Number, new() { CaptureReturnSource = true }, null);
        Assert.NotNull(package); var result = await RmaApplications(h).FulfilmentAsync(f.Id);
        Assert.Equal("PARTIAL", result.Coverage); Assert.Contains("INVOICE_LINE", result.Unobserved);
        Assert.Equal(2, result.Lines.Count); Assert.NotEqual(result.Lines[0].OrderLineId, result.Lines[1].OrderLineId);
        Assert.All(result.Lines, x => {
            Assert.Equal("25.48148136", x.BaseQty.Value); Assert.Equal("EA", x.BaseQty.Uom.Code);
            Assert.Equal(new FulfilmentRangeV1("0", "25.48148136"), Assert.Single(x.SourceShareRanges));
            Assert.Equal(f.Product.ToString(), x.ProductRef.Id); Assert.Null(x.InvoiceLineRef);
            Assert.Equal("Main.Wms.ShipmentLot.v1", Assert.Single(x.LotRefs).Owner);
        });
        var actual = await h.Db.OutboundOrderDetails.AsNoTracking().OrderBy(x => x.LineNo).ToArrayAsync();
        Assert.Equal(actual.Select(x => x.Id.ToString()), result.Lines.Select(x => x.ShipmentLineId));
        Assert.Equal(5.75308644m, await h.Db.Stocks.Select(x => x.PhysicalQty).SingleAsync()); Assert.Equal(4, await h.Db.StockTransactions.CountAsync());
        foreach (var fact in result.Facts)
            Assert.Equal(fact.Reference.Digest, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fact.OriginalJson))).ToLowerInvariant());
        var shipment = result.Facts.Single(x => x.Reference == result.Lines[0].ShipmentRef);
        Assert.All(actual, x => Assert.Contains(x.ShipTxnNo!, shipment.OriginalJson));
        var original = JsonSerializer.Serialize(result);
        await Units(h).CommandAsync(Revise(f.Unit, f.Unit.Profile with { Enabled = false }));
        await h.Db.OrderDetails.ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, 99m));
        Assert.Equal(original, JsonSerializer.Serialize(await RmaApplications(h).FulfilmentAsync(f.Id)));
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_outbound_fulfilment_v1.snapshots WHERE tenant_id={h.Tenant}"));
    }

    private sealed class FailFulfilmentInsert : DbCommandInterceptor
    {
        public bool Reached;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO cp6_outbound_fulfilment_v1.snapshots", StringComparison.Ordinal))
            { Reached = true; throw new InvalidOperationException("INJECTED_FULFILMENT_FAILURE"); }
            return ValueTask.FromResult(result);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task OutboundFulfilment_capture_failure_rolls_back_stock_package_lines_and_sequences()
    {
        var failure = new FailFulfilmentInsert(); await using var h = await Seed(failure); var f = await AllocateFulfilment(h);
        var sequences = await h.Db.WmsSequences.AsNoTracking().ToArrayAsync();
        Assert.Equal("INJECTED_FULFILMENT_FAILURE", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.ShipAsync(f.Number, new() { CaptureReturnSource = true }, null))).Message);
        Assert.True(failure.Reached); await AssertFulfilmentUnshipped(h);
        Assert.Equal(JsonSerializer.Serialize(sequences), JsonSerializer.Serialize(await h.Db.WmsSequences.AsNoTracking().ToArrayAsync()));
    }

    [TenantManagementPostgresTheory]
    [InlineData("product", "PRODUCT_UNIT_BINDING_CHANGED")]
    [InlineData("unit", "UOM_PROFILE_CHANGED")]
    [InlineData("order", "FULFILMENT_ORDER_LINE_CHANGED")]
    [InlineData("stock", "FULFILMENT_STOCK_UNIT_MISMATCH")]
    public async Task OutboundFulfilment_changed_current_sources_fail_before_shipping(string change, string code)
    {
        await using var h = await Seed(); var f = await AllocateFulfilment(h);
        switch (change)
        {
            case "product": await h.Db.ProductMasters.Where(x => x.Id == f.Product).ExecuteUpdateAsync(s => s.SetProperty(x => x.QtyUnit, "KG")); break;
            case "unit": await Units(h).CommandAsync(Revise(f.Unit, f.Unit.Profile with { Name = "Revised" })); break;
            case "order": await h.Db.OrderDetails.ExecuteUpdateAsync(s => s.SetProperty(x => x.ProductCd, "CHANGED")); break;
            case "stock": await h.Db.Stocks.ExecuteUpdateAsync(s => s.SetProperty(x => x.UnitCd, "KG")); break;
        }
        Assert.Equal(code, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            f.Service.ShipAsync(f.Number, new() { CaptureReturnSource = true }, null))).Message);
        await AssertFulfilmentUnshipped(h);
    }
    [TenantManagementPostgresTheory]
    [InlineData("1", "10", "FULFILMENT_QUANTITY_NOT_REPRESENTABLE")]
    [InlineData("1", "3", "UOM_CONVERSION_NOT_EXACT")]
    [InlineData("10000000000000", "1", "FULFILMENT_QUANTITY_NOT_REPRESENTABLE")]
    public async Task OutboundFulfilment_does_not_round_or_truncate_quantity_at_the_formal_boundary(string numerator, string denominator, string code)
    {
        await using var h = await Seed(); var f = await AllocateFulfilment(h, numerator, denominator);
        Assert.Equal(code, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            f.Service.ShipAsync(f.Number, new() { CaptureReturnSource = true }, null))).Message);
        await AssertFulfilmentUnshipped(h);
    }
    [CurrentPermissionPostgresFact]
    public async Task OutboundFulfilment_capture_is_explicit_and_missing_binding_never_becomes_a_successful_capture()
    {
        await using var h = await Seed(); var f = await AllocateFulfilment(h, binding: false);
        Assert.Equal("PRODUCT_UNIT_BINDING_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            f.Service.ShipAsync(f.Number, new() { CaptureReturnSource = true }, null))).Message);
        await AssertFulfilmentUnshipped(h);
        await f.Service.ShipAsync(f.Number, new(), null);
        Assert.Equal(4, await h.Db.StockTransactions.CountAsync()); Assert.Equal(0, await FulfilmentCount(h));
        Assert.Equal("FULFILMENT_NOT_CAPTURED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => RmaApplications(h).FulfilmentAsync(f.Id))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task OutboundFulfilment_actual_HTTP_shipment_and_source_read_observe_current_authority_and_tenant()
    {
        await using var h = await Seed(); var f = await AllocateFulfilment(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using (var ship = await host.Send(OperationsRequest(cookies, HttpMethod.Post, $"api/wms/outbound-order/{f.Number}/ship", new { captureReturnSource = true })))
            Assert.True(ship.IsSuccessStatusCode, await OperationsFailure(ship));
        var path = $"api/wms/rma/native-applications-v1/sources/{f.Id}/fulfilment";
        using (var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path)))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode); Assert.True(read.Headers.CacheControl?.NoStore);
            using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
            Assert.Equal("2.12345678", json.RootElement.GetProperty("data").GetProperty("lines")[0].GetProperty("baseQty").GetProperty("value").GetString());
        }
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await using var foreign = await Seed(); await GrantRma(foreign);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => RmaApplications(foreign).FulfilmentAsync(f.Id))).Status);
    }
}
