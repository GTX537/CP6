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
    private static async Task VerifyOrdinaryLegacyStockApplyAsync(Harness h, Guid stockId, NpgsqlConnection control,
        string login, AuthenticationApiProcess host, Dictionary<string, string> cookies)
    {
        const decimal quantity = .87654321m;
        var before = await Quantity();
        var movements = await h.Db.StockTransactions.CountAsync();
        var originals = await Operations(h);
        var outbox = await StockNotifications(h);
        using (var denied = await Apply())
        {
            Assert.True(denied.StatusCode == HttpStatusCode.Forbidden, await OperationsFailure(denied));
            Assert.Contains("FIELD_WRITE_FORBIDDEN", await denied.Content.ReadAsStringAsync());
        }
        await h.Db.Sys_RoleFieldPerms.Where(x => x.RoleId == 72 && x.ResourceKey == "wms-stock" &&
            x.FieldName == "UnitPrice" && x.Access == 3).ExecuteDeleteAsync();

        await CurrentPermissionPostgresFixture.Sql(control, $"""REVOKE UPDATE ("PhysicalQty") ON public."T_Stock" FROM {login}""");
        try
        {
            using var missingWrite = await Apply();
            Assert.True(missingWrite.StatusCode == HttpStatusCode.ServiceUnavailable, await OperationsFailure(missingWrite));
        }
        finally
        {
            await CurrentPermissionPostgresFixture.Sql(control, $"""GRANT UPDATE ("PhysicalQty") ON public."T_Stock" TO {login}""");
        }
        Assert.Equal(before, await Quantity());
        Assert.Equal(movements, await h.Db.StockTransactions.CountAsync());

        using var committed = await Apply();
        Assert.True(committed.StatusCode == HttpStatusCode.OK, await OperationsFailure(committed));
        var body = await committed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("WM-MSG-071", body.GetProperty("message").GetString());
        var number = body.GetProperty("data").GetProperty("txnNo").GetString();
        var movement = await h.Db.StockTransactions.AsNoTracking().SingleAsync(x => x.TxnNo == number);
        Assert.Equal(quantity, movement.Qty);
        Assert.Equal("price-owner", movement.OperatorCd);
        Assert.Equal(before + quantity, await Quantity());
        Assert.Equal(movements + 1, await h.Db.StockTransactions.CountAsync());
        Assert.Equal(originals, await Operations(h));
        Assert.Equal(outbox, await StockNotifications(h));

        Task<HttpResponseMessage> Apply() => host.Send(OperationsRequest(cookies, HttpMethod.Post, "api/wms/stock/apply",
            new StockMovementRequest
            {
                TxnType = WmsTxnType.ADJ, WarehouseCd = "W01", LocationCd = "L1", ProductCd = "P1", LotNo = "LOT",
                Qty = quantity, UnitPrice = 3.125m, UnitCd = "EA", OwnerType = "CUSTOMER", OwnerCd = "CUST-A", OperatorCd = "forged"
            }));
        Task<decimal> Quantity() => h.Db.Stocks.Where(x => x.Id == stockId).Select(x => x.PhysicalQty).SingleAsync();
    }
}
