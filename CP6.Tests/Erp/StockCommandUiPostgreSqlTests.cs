using System.Net;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Stock_ui_actual_http_decimal_strings_original_recovery_and_atomic_outbox()
    {
        await using var h = await Seed(); await GrantStocktakeDashboard(h); await SeedInventory(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        const string root = "api/wms/stock/";
        var adjustId = Guid.NewGuid(); var moveId = Guid.NewGuid(); var negativeId = Guid.NewGuid();
        var adjust = new { operationId = adjustId, txnType = "ADJ", warehouseCd = "W01", locationCd = "L1", productCd = "P1", lotNo = "LOT", qty = "9007199254740.12345678" };
        using (var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, root + "commands/v1/apply", adjust)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(adjustId, json.RootElement.GetProperty("data").GetProperty("operationId").GetGuid());
        }
        Assert.Equal(9007199254750.24691356m, await h.Db.Stocks.Where(s => s.ProductCd == "P1").Select(s => s.PhysicalQty).SingleAsync());
        Assert.Equal(9007199254740.12345678m, await h.Db.StockTransactions.Select(s => s.Qty).SingleAsync());
        Assert.Equal(1, await StockNotifications(h));
        using (var replay = await host.Send(OperationsRequest(cookies, HttpMethod.Post, root + "commands/v1/apply", adjust))) Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using (var move = await host.Send(OperationsRequest(cookies, HttpMethod.Post, root + "commands/v1/move",
            new { operationId = moveId, warehouseCd = "W01", fromLocationCd = "L1", toLocationCd = "L3", productCd = "P1", lotNo = "LOT", qty = "0.12345678" })))
            Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        using (var negative = await host.Send(OperationsRequest(cookies, HttpMethod.Post, root + "commands/v1/apply",
            new { operationId = negativeId, txnType = "ADJ", warehouseCd = "W01", locationCd = "L1", productCd = "P1", lotNo = "LOT", qty = "-0.00000001" })))
            Assert.Equal(HttpStatusCode.OK, negative.StatusCode);
        Assert.Equal(9007199254750.12345677m, await h.Db.Stocks.Where(s => s.ProductCd == "P1" && s.LocationCd == "L1").Select(s => s.PhysicalQty).SingleAsync());
        Assert.Equal(.12345678m, await h.Db.Stocks.Where(s => s.LocationCd == "L3").Select(s => s.PhysicalQty).SingleAsync());
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 403 && a.ActionCode != "query").ExecuteDeleteAsync();
        using (var recovery = await host.Send(OperationsRequest(cookies, HttpMethod.Get, root + $"operations/v1/{moveId}")))
        {
            Assert.Equal(HttpStatusCode.OK, recovery.StatusCode); Assert.True(recovery.Headers.CacheControl?.NoStore);
            using var json = JsonDocument.Parse(await recovery.Content.ReadAsStringAsync()); var data = json.RootElement.GetProperty("data");
            Assert.Equal("COMMITTED", data.GetProperty("state").GetString()); Assert.Equal(moveId, data.GetProperty("result").GetProperty("operationId").GetGuid());
        }
        using (var closed = await host.Send(OperationsRequest(cookies, HttpMethod.Post, root + $"operations/v1/{moveId}/close")))
            Assert.Contains("COMMITTED", await closed.Content.ReadAsStringAsync());
        var unused = Guid.NewGuid();
        using (var unknown = await host.Send(OperationsRequest(cookies, HttpMethod.Get, root + $"operations/v1/{unused}")))
            Assert.Contains("UNKNOWN", await unknown.Content.ReadAsStringAsync());
        using (var closed = await host.Send(OperationsRequest(cookies, HttpMethod.Post, root + $"operations/v1/{unused}/close")))
            Assert.Contains("CLOSED", await closed.Content.ReadAsStringAsync());
        using (var late = await host.Send(OperationsRequest(cookies, HttpMethod.Post, root + "commands/v1/apply", StockApply(unused))))
            Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal(4, await Operations(h)); Assert.Equal(4, await h.Db.StockTransactions.CountAsync()); Assert.Equal(3, await StockNotifications(h));
    }
}
