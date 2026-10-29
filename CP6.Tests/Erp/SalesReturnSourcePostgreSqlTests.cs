using System.Data.Common;
using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static SalesReturnSourceReaderV1 SalesReturnSource(Harness h)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        catalog.RegisterScope("wms-rma", "RMA", [1,2,3,4,5], 1);
        catalog.RegisterFields("wms-rma", [new("CustomerName","Name"),new("OperatorCd","Actor"),new("Remarks","Remarks"),new("ReturnReason","Reason")]);
        catalog.RegisterScope("order", "Order", [1,2,3,4,5], 5);
        catalog.RegisterFields("order", [new("UnitPrice","Price"),new("Amount","Amount")]); catalog.Seal();
        return new(h.Db, new(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture));
    }
    [CurrentPermissionPostgresFact]
    public async Task SalesReturnSource_actual_ship_preserves_exact_originals_per_line_and_reports_current_changes()
    {
        await using var h = await Seed(); var f = await FormalShipment(h, ship: false);
        await h.Db.Orders.ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrencyCd, "USD").SetProperty(x => x.FxRate, 1.234567m));
        await h.Db.OrderDetails.Where(x => x.WebOrderDetailNo == 1).ExecuteUpdateAsync(s => s.SetProperty(x => x.IndividualUnitPrice, 9999999999999.12345678m)
            .SetProperty(x => x.Amount, 9999999999999.87654321m).SetProperty(x => x.SetUnitPrice, (decimal?)null));
        await h.Db.OrderDetails.Where(x => x.WebOrderDetailNo == 2).ExecuteUpdateAsync(s => s.SetProperty(x => x.IndividualUnitPrice, 7.12345678m).SetProperty(x => x.Amount, (decimal?)null));
        await f.Service.ShipAsync(f.Number, new() { CaptureReturnApplicationSource = true }, null);
        var fulfilment = await RmaApplications(h).FulfilmentAsync(f.Id); var reader = SalesReturnSource(h);
        var originals = new List<SalesReturnSourceV1>();
        foreach (var line in fulfilment.Lines)
        {
            var value = await reader.ReadAsync(f.Id, Guid.Parse(line.ShipmentLineId)); originals.Add(value);
            Assert.Equal(line.CommercialSnapshotRef, value.Reference); Assert.Equal(line.OrderLineId, value.Original.OrderLineId);
            Assert.Equal("TERMS_UNCHANGED", value.CurrentState); Assert.Equal("USD", value.Original.Terms.CurrencyCode); Assert.Equal("1.234567", value.Original.Terms.FxRate);
        }
        var first = originals.Single(x => x.Original.OrderLineNumber == 1); var second = originals.Single(x => x.Original.OrderLineNumber == 2);
        Assert.Equal(first.Original.ProductCode, second.Original.ProductCode); Assert.NotEqual(first.Original.OrderLineId, second.Original.OrderLineId);
        Assert.Equal("9999999999999.12345678", first.Original.Terms.IndividualUnitPrice); Assert.Equal("9999999999999.87654321", first.Original.Terms.Amount);
        Assert.Null(first.Original.Terms.SetUnitPrice); Assert.Null(second.Original.Terms.Amount); Assert.Equal("7.12345678", second.Original.Terms.IndividualUnitPrice);
        Assert.DoesNotContain(fulfilment.Facts, x => x.Reference.Owner == "Main.Erp.SalesReturnSource.v1");
        Assert.DoesNotContain("9999999999999.12345678", FormalJson(fulfilment));
        await h.Db.OrderDetails.ExecuteUpdateAsync(s => s.SetProperty(x => x.IndividualUnitPrice, 11m).SetProperty(x => x.IsDeleted, true));
        var cancelledAt = new DateTime(2026, 10, 29, 1, 2, 3, DateTimeKind.Unspecified);
        await h.Db.Orders.ExecuteUpdateAsync(s => s.SetProperty(x => x.CancelledAt, cancelledAt));
        var changed = await reader.ReadAsync(f.Id, Guid.Parse(first.Original.ShipmentLineId));
        Assert.Equal(FormalJson(first.Original), FormalJson(changed.Original)); Assert.Equal(first.Reference, changed.Reference);
        Assert.Equal("TERMS_CHANGED", changed.CurrentState); Assert.Equal("11", changed.Current!.Terms.IndividualUnitPrice);
        Assert.True(changed.Current.LineDeleted); Assert.NotNull(changed.Current.CancelledAt);
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_sales_return_source_v1.snapshots WHERE tenant_id={h.Tenant}"));
    }
    [TenantManagementPostgresTheory]
    [InlineData("UnitPrice")]
    [InlineData("Amount")]
    public async Task SalesReturnSource_current_field_denial_cannot_leak_prices_through_fulfilment(string field)
    {
        await using var h = await Seed(); var f = await FormalShipment(h); var fulfilment = await RmaApplications(h).FulfilmentAsync(f.Id);
        var line = fulfilment.Lines[0];
        Assert.NotNull(await SalesReturnSource(h).ReadAsync(f.Id, Guid.Parse(line.ShipmentLineId)));
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "order", FieldName = field, Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_READ_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => SalesReturnSource(h).ReadAsync(f.Id, Guid.Parse(line.ShipmentLineId)))).Message);
        var withoutPrices = await RmaApplications(h).FulfilmentAsync(f.Id);
        Assert.Equal(line.CommercialSnapshotRef, withoutPrices.Lines[0].CommercialSnapshotRef);
        Assert.DoesNotContain("individualUnitPrice", FormalJson(withoutPrices));
    }
    [CurrentPermissionPostgresFact]
    public async Task SalesReturnSource_missing_capture_and_missing_line_are_explicit_and_never_backfilled()
    {
        await using var h = await Seed(); var f = await FormalShipment(h, customer: false);
        var line = (await RmaApplications(h).FulfilmentAsync(f.Id)).Lines[0]; Assert.Null(line.CommercialSnapshotRef);
        Assert.Equal("SALES_RETURN_SOURCE_NOT_CAPTURED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => SalesReturnSource(h).ReadAsync(f.Id, Guid.Parse(line.ShipmentLineId)))).Message);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_sales_return_source_v1.snapshots WHERE tenant_id={h.Tenant}").SingleAsync());
        await using var captured = await Seed(); var actual = await FormalShipment(captured);
        var original = (await RmaApplications(captured).FulfilmentAsync(actual.Id)).Lines[0];
        await captured.Db.OrderDetails.Where(x => x.Id == Guid.Parse(original.OrderLineId)).ExecuteDeleteAsync();
        var result = await SalesReturnSource(captured).ReadAsync(actual.Id, Guid.Parse(original.ShipmentLineId));
        Assert.Equal("LINE_MISSING", result.CurrentState); Assert.Null(result.Current); Assert.Equal(original.OrderLineId, result.Original.OrderLineId);
        var actor = await captured.Db.Sys_Users.Where(x => x.Id == captured.User).Select(x => x.UserName).SingleAsync();
        await captured.Db.Orders.ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, actor));
        await captured.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "order").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => SalesReturnSource(captured).ReadAsync(actual.Id, Guid.Parse(original.ShipmentLineId)))).Status);
    }
    private sealed class FailCommercialCapture : DbCommandInterceptor
    {
        public bool Reached;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO cp6_sales_return_source_v1.snapshots", StringComparison.Ordinal))
            { Reached = true; throw new InvalidOperationException("INJECTED_COMMERCIAL_CAPTURE_FAILURE"); }
            return ValueTask.FromResult(result);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task SalesReturnSource_capture_failure_rolls_back_actual_stock_package_and_fulfilment()
    {
        var fault = new FailCommercialCapture(); await using var h = await Seed(fault); var f = await FormalShipment(h, ship: false);
        Assert.Equal("INJECTED_COMMERCIAL_CAPTURE_FAILURE", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Service.ShipAsync(f.Number, new() { CaptureReturnApplicationSource = true }, null))).Message);
        Assert.True(fault.Reached); await AssertFulfilmentUnshipped(h);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_sales_return_source_v1.snapshots WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task SalesReturnSource_actual_HTTP_respects_current_order_warehouse_and_tenant_authority()
    {
        await using var h = await Seed(); var f = await FormalShipment(h); var line = (await RmaApplications(h).FulfilmentAsync(f.Id)).Lines[0];
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        var path = $"api/erp/sales-return-sources/v1/{f.Id}/{line.ShipmentLineId}";
        using (var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path)))
        {
            Assert.True(response.IsSuccessStatusCode, await OperationsFailure(response)); Assert.True(response.Headers.CacheControl?.NoStore);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = json.RootElement.GetProperty("data"); Assert.Equal(line.CommercialSnapshotRef!.Digest, data.GetProperty("reference").GetProperty("digest").GetString());
            Assert.Equal(JsonValueKind.String, data.GetProperty("original").GetProperty("terms").GetProperty("individualUnitPrice").ValueKind);
        }
        await h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "order").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        await h.Db.Orders.ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "different-user"));
        using (var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => SalesReturnSource(h).ReadAsync(f.Id, Guid.Parse(line.ShipmentLineId)))).Status);
        await using var foreign = await Seed(); await GrantRma(foreign);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => SalesReturnSource(foreign).ReadAsync(f.Id, Guid.Parse(line.ShipmentLineId)))).Status);
    }
}
