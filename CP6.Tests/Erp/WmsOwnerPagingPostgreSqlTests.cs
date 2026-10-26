using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(true)] [InlineData(false)]
    public async Task Wms_owner_page_http_reaches_beyond_500_and_counts_only_current_visible_rows(bool replenish)
    {
        await using var h = await Seed();
        if (replenish)
        {
            await SeedReplenish(h); await GrantReplenish(h, 1);
            h.Db.ReplenishOrders.AddRange(Enumerable.Range(0, 503).Select(i => new ReplenishOrder {
                ReplenishNo = $"RPL-PAGE-{i:D4}", WarehouseCd = i == 500 ? "HIDDEN" : "W1",
                FromLocationCd = "FROM", ToLocationCd = "TO", ProductCd = "P1", Qty = 1,
                Creator = i == 501 ? "other" : "price-owner", Status = i == 502 ? 9 : 0,
                Remarks = "private page note" }));
        }
        else
        {
            await SeedRma(h); await GrantRma(h, 1);
            await h.Db.RmaHeaders.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, 0));
            h.Db.RmaHeaders.AddRange(Enumerable.Range(0, 503).Select(i => new RmaHeader {
                RmaNo = $"RMA-PAGE-{i:D4}", WarehouseCd = i == 500 ? "HIDDEN" : "W1",
                CustomerCd = "CUSTOMER-1", Creator = i == 501 ? "other" : "price-owner",
                Status = i == 502 ? 9 : 0, Remarks = "private page note" }));
        }
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await using var foreign = await Seed();
        if (replenish) await SeedReplenish(foreign, "FOREIGN"); else await SeedRma(foreign, "FOREIGN");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h); var path = "api/wms/" + (replenish ? "replenish" : "rma");
        async Task<JsonElement> Read(string query)
        {
            using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path + "/pages/v1?" + query));
            Assert.True(response.IsSuccessStatusCode, await OperationsFailure(response));
            Assert.True(response.Headers.CacheControl?.NoStore);
            var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("private", text);
            Assert.DoesNotContain("FOREIGN", text); Assert.DoesNotContain("HIDDEN", text);
            using var json = JsonDocument.Parse(text); return json.RootElement.GetProperty("data").Clone();
        }
        var first = await Read("status=0&page=1&pageSize=500");
        var last = await Read("status=0&page=11&pageSize=50");
        Assert.Equal(501, first.GetProperty("total").GetInt32()); Assert.Equal(500, first.GetProperty("items").GetArrayLength());
        Assert.Equal(501, last.GetProperty("total").GetInt32()); Assert.Equal(11, last.GetProperty("page").GetInt32());
        var lastItem = Assert.Single(last.GetProperty("items").EnumerateArray());
        var key = replenish ? "replenishNo" : "rmaNo";
        Assert.DoesNotContain(first.GetProperty("items").EnumerateArray(), x => x.GetProperty(key).GetString() == lastItem.GetProperty(key).GetString());
        var past = await Read("status=0&page=2147483647&pageSize=50");
        Assert.Equal(501, past.GetProperty("total").GetInt32()); Assert.Equal(0, past.GetProperty("items").GetArrayLength());
        var empty = await Read("status=7&page=-1&pageSize=0");
        Assert.Equal(0, empty.GetProperty("total").GetInt32()); Assert.Equal(0, empty.GetProperty("items").GetArrayLength());
        Assert.Equal(1, empty.GetProperty("page").GetInt32()); Assert.Equal(1, empty.GetProperty("pageSize").GetInt32());
        using (var compatibility = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path + "?status=0&page=11&pageSize=50")))
        {
            Assert.True(compatibility.IsSuccessStatusCode, await OperationsFailure(compatibility));
            using var body = JsonDocument.Parse(await compatibility.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("data").ValueKind);
            Assert.Equal(lastItem.GetProperty(key).GetString(), Assert.Single(body.RootElement.GetProperty("data").EnumerateArray()).GetProperty(key).GetString());
        }
        using (var ordered = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path + "?status=0&page=1&pageSize=500")))
        {
            Assert.True(ordered.IsSuccessStatusCode, await OperationsFailure(ordered));
            using var body = JsonDocument.Parse(await ordered.Content.ReadAsStringAsync());
            Assert.Equal(body.RootElement.GetProperty("data").EnumerateArray().Select(x => x.GetProperty(key).GetString()),
                first.GetProperty("items").EnumerateArray().Select(x => x.GetProperty(key).GetString()));
        }
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == (replenish ? 423 : 426) && a.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path + "/pages/v1?page=11"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
