using System.Net;
using CP6.Core.Services.Erp;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_auxiliary_credit_requires_amount_and_its_price_dependency()
    {
        await using var h = await Seed(); await GrantOrderReads(h);
        await h.Db.OrderDetails.ExecuteUpdateAsync(s => s.SetProperty(d => d.HaibaiNo2, "CREDIT"));
        foreach (var field in new[] { "Amount", "UnitPrice" })
        {
            h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "order", FieldName = field, Access = 3 });
            await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
            var denied = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.CheckCreditAsync("credit ", 1m));
            Assert.Equal("FIELD_READ_FORBIDDEN", denied.Message);
            await h.Db.Sys_RoleFieldPerms.ExecuteDeleteAsync();
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_auxiliary_credit_keeps_complete_balance_precision_and_current_authority()
    {
        await using var h = await Seed(); await GrantOrderReads(h);
        await h.Db.OrderDetails.ExecuteUpdateAsync(s => s.SetProperty(d => d.HaibaiNo2, "CREDIT").SetProperty(d => d.Amount, 1234567890123.12345678m));
        await using var other = await Seed();
        await other.Db.OrderDetails.ExecuteUpdateAsync(s => s.SetProperty(d => d.HaibaiNo2, "CREDIT"));
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "order", FieldName = "Amount", Access = 2 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var credit = await h.Service.CheckCreditAsync("credit ", 1m);
        Assert.Equal(2469135780246.24691356m, credit.Balance); Assert.True(credit.IsOver);
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        await h.Db.OrderDetails.Where(d => d.WebOrderDetailNo == 2).ExecuteUpdateAsync(s => s.SetProperty(d => d.Creator, "another"));
        Assert.Equal("OBJECT_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.CheckCreditAsync("CREDIT", 1m))).Message);
        Assert.Equal(0m, (await h.Service.CheckCreditAsync("MISSING", 1m)).Balance);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 32622 && a.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.CheckCreditAsync("MISSING", 1m))).Status);
        var missing = new OrderService(h.Db, h.Workflow, new NoOpWmsBridgeHook());
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => missing.CheckCreditAsync("CREDIT", 1m))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_auxiliary_consigned_check_preserves_missing_rows_but_rejects_foreign_objects()
    {
        await using var h = await Seed(); await GrantOrderReads(h);
        Assert.True(await h.Service.CheckConsignedSalesQtyAsync(h.Number.ToLowerInvariant() + " ", 1, 2m));
        Assert.False(await h.Service.CheckConsignedSalesQtyAsync(h.Number, 1, 3m));
        Assert.True(await h.Service.CheckConsignedSalesQtyAsync("MISSING", 1, 3m));
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
        await h.Db.OrderDetails.Where(d => d.WebOrderDetailNo == 2).ExecuteUpdateAsync(s => s.SetProperty(d => d.Creator, "another"));
        Assert.Equal("OBJECT_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.CheckConsignedSalesQtyAsync(h.Number, 2, 3m))).Message);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 32622 && a.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => h.Service.CheckConsignedSalesQtyAsync("MISSING", 1, 3m))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Ordinary_order_auxiliary_actual_program_rechecks_hidden_fields_and_revoked_query()
    {
        await using var h = await Seed(); await GrantOrderReads(h);
        await h.Db.OrderDetails.ExecuteUpdateAsync(s => s.SetProperty(d => d.HaibaiNo2, "CREDIT"));
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using (var allowed = await host.Send(BoundRequest(cookies, "api/orders/credit-check?customerCd=CREDIT&amount=1")))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        h.Db.Sys_RoleFieldPerms.Add(new() { RoleId = 72, ResourceKey = "order", FieldName = "Amount", Access = 3 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        using (var denied = await host.Send(BoundRequest(cookies, "api/orders/credit-check?customerCd=CREDIT&amount=1")))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == 32622 && a.ActionCode == "query").ExecuteDeleteAsync();
        using var revoked = await host.Send(BoundRequest(cookies, "api/orders/consigned-check?webOrderNo=" + h.Number + "&detailNo=1&qty=3"));
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }
}
