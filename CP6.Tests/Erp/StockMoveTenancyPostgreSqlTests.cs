using System.Net;
using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task VerifyOrdinaryStockMoveAsync(Harness h, MappingHarness mapping, NpgsqlConnection control,
        AuthenticationApiProcess host, Dictionary<string, string> cookies, Func<Guid, Task<int>> outboxCount,
        Func<Guid, Task<HttpResponseMessage>> readOriginal)
    {
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(rows => rows.SetProperty(g => g.AreaCd, "A"));
        var source = await h.Db.Stocks.SingleAsync(x => x.ProductCd == "P1" && x.LocationCd == "L1");
        source.OwnerType = "CUSTOMER";
        source.OwnerCd = "CUST-A";
        source.UnitCd = "EA";
        source.UnitPrice = 3.1250m;
        var target = new Stock
        {
            WarehouseCd = source.WarehouseCd, LocationCd = "L3", ProductCd = source.ProductCd, LotNo = source.LotNo,
            OwnerType = source.OwnerType, OwnerCd = source.OwnerCd, UnitCd = source.UnitCd, UnitPrice = source.UnitPrice,
            ReceiveDate = source.ReceiveDate, ExpiryDate = source.ExpiryDate, PaperRollNo = source.PaperRollNo,
            QcStatus = source.QcStatus, RecallFlag = source.RecallFlag, Creator = source.Creator,
            PhysicalQty = 8.87654321m, AvailableQty = 8.87654321m
        };
        h.Db.Stocks.Add(target);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await BindStandalone(h, mapping, "stock", target.Id, mapping.Root);

        const decimal quantity = .12345678m;
        StockMoveCommandV1 Command() => new()
        {
            OperationId = Guid.NewGuid(), WarehouseCd = "W01", FromLocationCd = "L1", ToLocationCd = "L3",
            ProductCd = "P1", LotNo = "LOT", Qty = quantity, OperatorCd = "forged"
        };
        Task<HttpResponseMessage> Move(StockMoveCommandV1 input) => host.Send(
            OperationsRequest(cookies, HttpMethod.Post, "api/wms/stock/commands/v1/move", input));
        Task<decimal[]> Quantities() => h.Db.Stocks.AsNoTracking().Where(x => x.Id == source.Id || x.Id == target.Id)
            .OrderBy(x => x.LocationCd).Select(x => x.PhysicalQty).ToArrayAsync();
        Task<int> NextTransactionNumber() => h.Db.WmsSequences.Where(x => x.Prefix == "TXN")
            .Select(x => x.NextNo).SingleAsync();
        Task<int> InheritanceAudits() => h.Db.Sys_SecurityLogs.CountAsync(x => x.Reason == StockTransactionInheritanceV1.AuditReason);

        var before = await Quantities();
        var command = Command();
        await CurrentPermissionPostgresFixture.Sql(control, "REVOKE EXECUTE ON FUNCTION cp6_stock_adjust_owner_v1.lock_graph(uuid) FROM stock_adjust_a");
        using (var unavailable = await Move(command))
            Assert.True(unavailable.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(unavailable));
        await CurrentPermissionPostgresFixture.Sql(control, "GRANT EXECUTE ON FUNCTION cp6_stock_adjust_owner_v1.lock_graph(uuid) TO stock_adjust_a");
        Assert.Equal(before, await Quantities());

        var targetLocation = h.Db.Locations.Where(x => x.WarehouseCd == "W01" && x.LocationCd == "L3");
        await targetLocation.ExecuteUpdateAsync(rows => rows.SetProperty(x => x.AreaCd, "B"));
        using (var denied = await Move(command))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Contains("OBJECT_FORBIDDEN", await denied.Content.ReadAsStringAsync());
        }
        await targetLocation.ExecuteUpdateAsync(rows => rows.SetProperty(x => x.AreaCd, "A"));

        string originalResult;
        using (var committed = await Move(command))
        {
            Assert.True(committed.StatusCode == HttpStatusCode.OK, await OperationsFailure(committed));
            using var payload = JsonDocument.Parse(await committed.Content.ReadAsStringAsync());
            originalResult = payload.RootElement.GetProperty("data").GetRawText();
        }
        var legs = await h.Db.StockTransactions.AsNoTracking().Where(x => x.TxnType == WmsTxnType.MOVE)
            .OrderBy(x => x.LocationCd).ToArrayAsync();
        Assert.Equal(new[] { -quantity, quantity }, legs.Select(x => x.Qty));
        Assert.Equal(legs[0].TxnNo, legs[1].RelatedNo);
        Assert.All(legs, leg =>
        {
            Assert.Equal("price-owner", leg.OperatorCd);
            Assert.Equal(source.OwnerType, leg.OwnerType);
            Assert.Equal(source.OwnerCd, leg.OwnerCd);
            Assert.Equal(source.UnitCd, leg.UnitCd);
            Assert.Equal(source.UnitPrice, leg.UnitPrice);
        });
        var movementIds = legs.Select(x => x.Id).ToArray();
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_operations_owner_v1.department_mappings
            WHERE tenant_id={h.Tenant} AND object_kind='stock-transaction'
              AND ((object_id={legs[0].Id} AND department_id={mapping.Child}) OR
                   (object_id={legs[1].Id} AND department_id={mapping.Root}))
            """).SingleAsync());
        var expected = new[] { before[0] - quantity, before[1] + quantity };
        Assert.Equal(expected, await Quantities());
        Assert.Equal(1, await outboxCount(command.OperationId));
        using (var recovered = await readOriginal(command.OperationId))
        {
            Assert.True(recovered.StatusCode == HttpStatusCode.OK, await OperationsFailure(recovered));
            using var data = JsonDocument.Parse(await recovered.Content.ReadAsStringAsync());
            Assert.Equal(originalResult, data.RootElement.GetProperty("data").GetProperty("result").GetRawText());
        }

        // The ordinary route can reach this destination, but its existing native writer guard refuses the second leg.
        var environment = Guid.NewGuid();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_stock_move_v1.openings(tenant_id,environment_id,id,body)
            VALUES({h.Tenant},{environment},'fixture-move-open',convert_to('fixture','UTF8'));
            INSERT INTO cp6_stock_move_v1.locations(tenant_id,environment_id,id,native_id,opening_id,body)
            VALUES({h.Tenant},{environment},'fixture-move-destination',{Guid.NewGuid()},'fixture-move-open',convert_to('fixture','UTF8'));
            INSERT INTO cp6_stock_move_v1.writer_bindings(tenant_id,environment_id,location_id,warehouse_code,location_code,product_code)
            VALUES({h.Tenant},{environment},'fixture-move-destination','W01','L3','P1')
            """);
        var failed = Command();
        var nextNumber = await NextTransactionNumber();
        var auditCount = await InheritanceAudits();
        using (var response = await Move(failed))
            Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(response));
        Assert.Equal(expected, await Quantities());
        Assert.Equal(movementIds, await h.Db.StockTransactions.AsNoTracking().Where(x => x.TxnType == WmsTxnType.MOVE)
            .OrderBy(x => x.LocationCd).Select(x => x.Id).ToArrayAsync());
        Assert.Equal(nextNumber, await NextTransactionNumber());
        Assert.Equal(auditCount, await InheritanceAudits());
        Assert.Equal(0, await outboxCount(failed.OperationId));
        using (var missing = await readOriginal(failed.OperationId))
        {
            Assert.True(missing.StatusCode == HttpStatusCode.OK, await OperationsFailure(missing));
            using var data = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
            Assert.Equal("UNKNOWN", data.RootElement.GetProperty("data").GetProperty("state").GetString());
        }
    }
}
