using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Workspace_dashboard_eight_metrics_use_current_owner_scope_and_independent_drilldown()
    {
        await using var a = await Seed();
        await using var b = await Seed();
        var day = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
        var month = new DateTime(day.Year, day.Month, 1);
        var orderMenu = await WorkspaceQueryGrant(a, "order-list");
        foreach (var key in new[] { "mes-work-order", "wms-outbound-order", "wms-stock", "wms-stocktake", "erp-product" })
            await WorkspaceQueryGrant(a, key);
        foreach (var key in new[] { "mes-work-order", "wms-outbound-order", "wms-stock", "wms-stocktake", "product" })
            a.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = key, ScopeType = 5 });
        a.Db.WmsRoleScopes.Add(new() { RoleId = 72, WarehouseCd = "W1" });
        a.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "order", FieldName = "UnitPrice", Access = 3 });
        a.Db.Orders.Add(new() { WebOrderNo = "WS-REMOVED", CreateDate = day, OrderType = "0100", IsDeleted = true });
        a.Db.Orders.Add(new() { WebOrderNo = "WS-OLD", CreateDate = month.AddDays(-1), OrderType = "0100" });
        a.Db.WorkOrders.AddRange(
            new() { WorkOrderNo = "WS-MES-1", ProductCd = "P1", Status = 1 },
            new() { WorkOrderNo = "WS-MES-4", ProductCd = "P1", Status = 4, ActualEndDate = month },
            new() { WorkOrderNo = "WS-MES-6", ProductCd = "P1", Status = 6, ActualEndDate = day },
            new() { WorkOrderNo = "WS-MES-9", ProductCd = "P1", Status = 9 });
        foreach (var i in new[] { 1, 2, 3, 4, 5 })
            a.Db.OutboundOrders.Add(new() { OutboundNo = "WS-OUT-" + i, WarehouseCd = i == 5 ? "W2" : "W1", Status = i == 5 ? 1 : i });
        a.Db.Stocks.AddRange(
            new() { WarehouseCd = "W1", LocationCd = "L1", ProductCd = "P1", LotNo = "W0", AvailableQty = 0m },
            new() { WarehouseCd = "W1", LocationCd = "L2", ProductCd = "P1", LotNo = "W1", PhysicalQty = 1m, AvailableQty = 1m },
            new() { WarehouseCd = "W2", LocationCd = "L3", ProductCd = "P1", LotNo = "W2", AvailableQty = 0m });
        a.Db.StockTakes.AddRange(
            new() { StockTakeNo = "WS-TAKE-1", TargetWarehouseCd = "W1", Status = 3 },
            new() { StockTakeNo = "WS-TAKE-2", TargetWarehouseCd = "W2", Status = 3 });
        foreach (var i in new[] { 0, 1, 2 })
            a.Db.ProductMasters.Add(new() { ProductCd = "WS-P" + i, ItemCd = "WS-P" + i, Status = i == 0 ? 0 : 1 });
        await a.Db.SaveChangesAsync();
        a.Db.ChangeTracker.Clear();
        var heldOrder = await a.Db.Orders.Where(o => o.WebOrderNo == a.Number).Select(o => o.Id).SingleAsync();
        // 模拟 Owner 已处理的匿名化业务记录，指标只使用允许的日期、对象及版本。
        await a.Db.Orders.Where(o => o.Id == heldOrder).ExecuteUpdateAsync(set => set.SetProperty(o => o.CustomerCd, "MASKED"));
        await using var host = await AuthenticationApiProcess.Start(a.Db.Database.GetConnectionString()!);
        var good = await Login(host, a);
        var foreign = await Login(host, b);
        const string root = "api/dashboard/scoped-v1/";
        async Task<JsonElement> Read(Dictionary<string, string> cookies, string path)
        {
            using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, root + path));
            Assert.True(response.IsSuccessStatusCode, await OperationsFailure(response));
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        var expected = new Dictionary<string, int>
        {
            ["todayOrders"] = 1, ["monthOrders"] = 1, ["activeWorkOrders"] = 2, ["monthCompleted"] = 2,
            ["pendingOutbound"] = 3, ["stockWarnings"] = 1, ["pendingApprovals"] = 2, ["totalProducts"] = 3
        };
        var observed = new Dictionary<string, JsonElement>();
        foreach (var (key, count) in expected)
        {
            var reply = await Read(good, key);
            Assert.True(reply.GetProperty("availability").GetString() == "READY", key + ": " + reply.GetRawText());
            Assert.True(reply.GetProperty("value").GetInt32() == count, key + ": " + reply.GetRawText());
            Assert.Equal("1", reply.GetProperty("definition").GetProperty("version").GetString());
            observed.Add(key, reply);
        }
        await DeviceEvidence("wp02-18-dashboard-ready-observations.json", observed);
        // 所有钻取均以卡片的原版本和原时间窗口查询。产品和盘点保留各自的来源标识。
        foreach (var (key, count) in expected)
        {
            var fromCard = observed[key].GetProperty("queryRef");
            var anchor = Uri.EscapeDataString(fromCard.GetProperty("asOfUtc").GetString()!);
            var fromTarget = await Read(good, $"{key}/drilldown?version=1&asOfUtc={anchor}");
            Assert.True(fromTarget.GetProperty("availability").GetString() == "READY", fromTarget.GetRawText());
            Assert.Equal(count, fromTarget.GetProperty("value").GetInt32());
            Assert.Equal(count, fromTarget.GetProperty("rows").GetArrayLength());
            Assert.Equal(observed[key].GetProperty("windowStart").GetRawText(), fromTarget.GetProperty("windowStart").GetRawText());
            foreach (var drillRow in fromTarget.GetProperty("rows").EnumerateArray())
            {
                var ownerRef = drillRow.GetProperty("reference");
                Assert.False(string.IsNullOrEmpty(ownerRef.GetProperty("owner").GetString()));
                Assert.NotEqual(Guid.Empty, ownerRef.GetProperty("id").GetGuid());
                Assert.False(string.IsNullOrEmpty(ownerRef.GetProperty("version").GetString()));
            }
        }

        var reference = observed["todayOrders"].GetProperty("queryRef");
        var exact = Uri.EscapeDataString(reference.GetProperty("asOfUtc").GetString()!);
        var detailPath = "todayOrders/drilldown?version=1&asOfUtc=" + exact;
        var detail = await Read(good, detailPath);
        Assert.True(detail.GetProperty("availability").GetString() == "READY", detail.GetRawText());
        Assert.Equal(1, detail.GetProperty("value").GetInt32());
        var row = Assert.Single(detail.GetProperty("rows").EnumerateArray());
        Assert.Equal(heldOrder, row.GetProperty("reference").GetProperty("id").GetGuid());
        Assert.Equal("Main.ERP.Order", row.GetProperty("reference").GetProperty("owner").GetString());
        Assert.False(string.IsNullOrEmpty(row.GetProperty("reference").GetProperty("version").GetString()));
        Assert.False(row.TryGetProperty("customerCd", out _));
        Assert.False(row.TryGetProperty("unitPrice", out _));
        Assert.DoesNotContain("MASKED", detail.GetRawText());
        var unrelated = await Read(good, detailPath + "&customerCd=UNRELATED");
        Assert.Equal(1, unrelated.GetProperty("value").GetInt32());
        Assert.Equal(row.GetRawText(), Assert.Single(unrelated.GetProperty("rows").EnumerateArray()).GetRawText());
        Assert.Equal("HIDDEN", (await Read(foreign, "todayOrders")).GetProperty("availability").GetString());

        using (var denied = await host.Send(OperationsRequest(foreign, HttpMethod.Get, root + detailPath)))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var expired = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-5).ToString("o", CultureInfo.InvariantCulture));
        Assert.Equal("STALE", (await Read(good, "todayOrders/drilldown?version=1&asOfUtc=" + expired)).GetProperty("availability").GetString());
        await WorkspaceQueryGrant(b, "order-list");
        await b.Db.Orders.ExecuteUpdateAsync(set => set.SetProperty(x => x.CreateDate, month.AddDays(-1)));
        var authorisedZero = await Read(foreign, "todayOrders");
        Assert.Equal("READY", authorisedZero.GetProperty("availability").GetString());
        Assert.Equal(0, authorisedZero.GetProperty("value").GetInt32());

        // 模拟订单授权要求部门但当前主体未完成部门绑定，其他 Owner 的可用性独立。
        var orderScopes = a.Db.Sys_RoleDataScopes.Where(x => x.RoleId == 72 && x.ResourceKey == "order");
        await orderScopes.ExecuteUpdateAsync(set => set.SetProperty(x => x.ScopeType, 2));
        try
        {
            var unavailable = await Read(good, "todayOrders");
            Assert.Equal("UNAVAILABLE", unavailable.GetProperty("availability").GetString());
            Assert.Equal(JsonValueKind.Null, unavailable.GetProperty("value").ValueKind);
            var healthy = await Read(good, "totalProducts");
            Assert.True(healthy.GetProperty("availability").GetString() == "READY", healthy.GetRawText());
            Assert.Equal(3, healthy.GetProperty("value").GetInt32());
        }
        finally { await orderScopes.ExecuteUpdateAsync(set => set.SetProperty(x => x.ScopeType, 5)); }
        Assert.Equal(1, (await Read(good, "todayOrders")).GetProperty("value").GetInt32());

        await a.Db.Sys_RoleActions.Where(x => x.MenuId == orderMenu && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal("HIDDEN", (await Read(good, "todayOrders")).GetProperty("availability").GetString());
        using (var denied = await host.Send(OperationsRequest(good, HttpMethod.Get, root + detailPath)))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await DeviceEvidence("wp02-18-dashboard-owner-values.json", new
        {
            Results = observed.ToDictionary(x => x.Key, x => x.Value.GetProperty("value").GetInt32()),
            TargetReadTenant = a.Tenant, OtherTenant = b.Tenant, OriginalId = heldOrder,
            DrillTotal = detail.GetProperty("value").GetInt32(),
            OtherTenantDenied = true, PostRevocationDenied = true, StaleDistinct = true,
            AuthorisedZero = 0, SingleSourceUnavailableAndRestored = true, AllEightDrilled = true, AnonymousFieldWithheld = true
        });
    }

    private static async Task<int> WorkspaceQueryGrant(Harness fixture, string key)
    {
        var menu = await fixture.Db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == key);
        if (menu is null)
        {
            menu = new() { MenuId = Random.Shared.Next(520000000, 590000000), MenuKey = key, MenuName = key, Enable = true };
            fixture.Db.Sys_Menus.Add(menu);
        }
        if (!await fixture.Db.Sys_MenuActions.AnyAsync(x => x.MenuId == menu.MenuId && x.ActionCode == "query"))
            fixture.Db.Sys_MenuActions.Add(new() { MenuId = menu.MenuId, ActionCode = "query", ActionName = "查询" });
        fixture.Db.Sys_RoleMenus.Add(new() { MenuId = menu.MenuId, RoleId = 72 });
        fixture.Db.Sys_RoleActions.Add(new() { MenuId = menu.MenuId, RoleId = 72, ActionCode = "query" });
        await fixture.Db.SaveChangesAsync();
        return menu.MenuId;
    }
}
