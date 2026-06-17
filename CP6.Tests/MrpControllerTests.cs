using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CP6.WebApi.Controllers.Plan;

namespace CP6.Tests;

/// <summary>
/// MRP 运算入口 MrpController 集成测试（MRP P1 · spec §9）。
/// 起 run（显式需求 / 受注派生）→ 计划订单/净需求可钻取。
/// </summary>
public class MrpControllerTests
{
    private static MrpController CreateController(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        var engine = new MrpEngine(db, new MaterialUsageCalculator(), new LowLevelCodeService(),
            new SupplyService(db), new ItemPlanningPolicyService(db));
        var c = new MrpController(engine, db);
        c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return c;
    }

    private static object? ExtractData(IActionResult r)
    {
        var ok = Assert.IsType<OkObjectResult>(r);
        var val = ok.Value!;
        return val.GetType().GetProperty("data")!.GetValue(val);
    }

    private static Guid RunIdOf(IActionResult r)
    {
        var data = ExtractData(r)!;
        return (Guid)data.GetType().GetProperty("runId")!.GetValue(data)!;
    }

    [Fact]
    public async Task Run_ExplicitDemands_PersistsPlannedOrders()
    {
        var c = CreateController(out var db);
        db.ItemPlanningPolicies.Add(new Plan_ItemPlanningPolicy { ItemCd = "RAW", MakeOrBuy = (int)PlanMakeOrBuy.Buy });
        await db.SaveChangesAsync();

        var runResult = await c.Run(new MrpRunRequestDto
        {
            Demands = new() { new MrpDemandDto { ItemCd = "RAW", Qty = 500, RequiredDate = new DateTime(2026, 7, 1) } },
        });

        var runId = RunIdOf(runResult);

        var poResult = await c.PlannedOrders(runId);
        var pos = Assert.IsAssignableFrom<IEnumerable<Plan_PlannedOrder>>(ExtractData(poResult)!);
        var po = Assert.Single(pos);
        Assert.Equal("RAW", po.ItemCd);
        Assert.Equal(500m, po.Qty);

        var nrResult = await c.NetRequirements(runId);
        var nrs = Assert.IsAssignableFrom<IEnumerable<Plan_NetRequirement>>(ExtractData(nrResult)!);
        Assert.Contains(nrs, x => x.ItemCd == "RAW" && x.Net == 500m);
    }

    [Fact]
    public async Task Run_NoDemand_ReturnsBadRequest()
    {
        var c = CreateController(out _);
        var r = await c.Run(new MrpRunRequestDto { Demands = new() });
        Assert.IsType<BadRequestObjectResult>(r);
    }

    [Fact]
    public async Task Run_FromOpenOrders_DerivesDemandFromOrderDetail()
    {
        var c = CreateController(out var db);
        db.ItemPlanningPolicies.Add(new Plan_ItemPlanningPolicy { ItemCd = "FG", MakeOrBuy = (int)PlanMakeOrBuy.Buy });
        db.Orders.Add(new Order { WebOrderNo = "SO-1", OrderStatus = OrderLifecycleStatus.Confirmed });
        db.OrderDetails.Add(new OrderDetail
        { WebOrderNo = "SO-1", ProductCd = "FG", Quantity = 400, ShippedQty = 100, CustomerDeliveryDate = new DateTime(2026, 7, 1) });
        await db.SaveChangesAsync();

        var runResult = await c.Run(new MrpRunRequestDto { FromOpenOrders = true });
        var runId = RunIdOf(runResult);

        var pos = Assert.IsAssignableFrom<IEnumerable<Plan_PlannedOrder>>(ExtractData(await c.PlannedOrders(runId))!);
        // 开口需求 = Quantity 400 − ShippedQty 100 = 300
        Assert.Equal(300m, Assert.Single(pos).Qty);
    }
}
