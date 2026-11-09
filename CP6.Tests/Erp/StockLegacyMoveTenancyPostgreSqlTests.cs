using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task VerifyOrdinaryLegacyStockMoveAsync(Harness h, MappingHarness mapping, NpgsqlConnection control,
        string login, AuthenticationApiProcess host, Dictionary<string, string> cookies)
    {
        const decimal quantity = .12345678m;
        var source = await h.Db.Stocks.SingleAsync(x => x.ProductCd == "P2" && x.LocationCd == "L2");
        source.OwnerType = "CUSTOMER";
        source.OwnerCd = "CUST-B";
        source.UnitCd = "BOX";
        source.UnitPrice = 4.0625m;
        var target = new Stock
        {
            WarehouseCd = "W01", LocationCd = "L3", ProductCd = "P2", LotNo = "LOT", Creator = "price-owner",
            OwnerType = source.OwnerType, OwnerCd = source.OwnerCd, UnitCd = source.UnitCd, UnitPrice = source.UnitPrice,
            PhysicalQty = 3.87654321m, AvailableQty = 3.87654321m
        };
        h.Db.Stocks.Add(target);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await BindStandalone(h, mapping, "stock", source.Id, mapping.Child);
        await BindStandalone(h, mapping, "stock", target.Id, mapping.Root);
        var before = await Quantities();
        var originals = await Operations(h);
        var notifications = await StockNotifications(h);

        var location = h.Db.Locations.Where(x => x.WarehouseCd == "W01" && x.LocationCd == "L3");
        await location.ExecuteUpdateAsync(x => x.SetProperty(l => l.AreaCd, "B"));
        using (var denied = await Move())
        {
            Assert.True(denied.StatusCode == HttpStatusCode.Forbidden, await OperationsFailure(denied));
            Assert.Contains("OBJECT_FORBIDDEN", await denied.Content.ReadAsStringAsync());
        }
        await location.ExecuteUpdateAsync(x => x.SetProperty(l => l.AreaCd, "A"));

        const string graph = "cp6_stock_adjust_owner_v1.lock_graph(uuid)";
        await CurrentPermissionPostgresFixture.Sql(control, $"REVOKE EXECUTE ON FUNCTION {graph} FROM {login}");
        try
        {
            using var unavailable = await Move();
            Assert.True(unavailable.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(unavailable));
        }
        finally
        {
            await CurrentPermissionPostgresFixture.Sql(control, $"GRANT EXECUTE ON FUNCTION {graph} TO {login}");
        }
        Assert.Equal(before, await Quantities());
        Assert.False(await Legs().AnyAsync());

        using var moved = await Move();
        Assert.True(moved.StatusCode == HttpStatusCode.OK, await OperationsFailure(moved));
        var body = await moved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("WM-MSG-071", body.GetProperty("message").GetString());
        var result = body.GetProperty("data");
        var legs = await Legs().OrderBy(x => x.LocationCd).ToArrayAsync();
        Assert.Equal(new[] { -quantity, quantity }, legs.Select(x => x.Qty));
        Assert.Equal(legs[0].TxnNo, result.GetProperty("outTxnNo").GetString());
        Assert.Equal(legs[1].TxnNo, result.GetProperty("inTxnNo").GetString());
        Assert.Equal(legs[0].TxnNo, legs[1].RelatedNo);
        Assert.All(legs, leg =>
        {
            Assert.Equal("price-owner", leg.OperatorCd);
            Assert.Equal((source.OwnerType, source.OwnerCd, source.UnitCd, source.UnitPrice),
                (leg.OwnerType, leg.OwnerCd, leg.UnitCd, leg.UnitPrice));
        });
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_operations_owner_v1.department_mappings
            WHERE tenant_id={h.Tenant} AND object_kind='stock-transaction' AND (
                object_id={legs[0].Id} AND department_id={mapping.Child} OR object_id={legs[1].Id} AND department_id={mapping.Root})
            """).SingleAsync());
        Assert.Equal(new[] { before[0] - quantity, before[1] + quantity }, await Quantities());
        Assert.Equal(originals, await Operations(h));
        Assert.Equal(notifications, await StockNotifications(h));

        Task<HttpResponseMessage> Move() => host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/wms/stock/move",
            new StockMoveRequest
            {
                WarehouseCd = "W01", FromLocationCd = "L2", ToLocationCd = "L3",
                ProductCd = "P2", LotNo = "LOT", Qty = quantity, OperatorCd = "forged"
            }));
        Task<decimal[]> Quantities() => h.Db.Stocks.Where(x => x.Id == source.Id || x.Id == target.Id)
            .OrderBy(x => x.LocationCd).Select(x => x.PhysicalQty).ToArrayAsync();
        IQueryable<StockTransaction> Legs() => h.Db.StockTransactions.AsNoTracking().Where(x => x.ProductCd == "P2");
    }
}
