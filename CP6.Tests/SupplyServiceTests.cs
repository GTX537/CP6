using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DomainModels.Mes;

namespace CP6.Tests;

/// <summary>
/// MRP 供给汇总 SupplyService 测试（MRP P1 · spec §5.1）。
///
/// 某 Item × 日桶 供给 = WMS 现库存 + 在途(采购未到) + 在制(已下达工单) + 已确认/已转单计划订单(scheduled receipt)。
/// 建议态计划订单不计；草稿/取消的入库/工单不计。
/// </summary>
public class SupplyServiceTests
{
    private static SupplyService CreateService(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        return new SupplyService(db);
    }

    private static readonly DateTime Bucket = new(2026, 7, 1);

    [Fact]
    public async Task GetSupply_IncludesFirmPlannedOrders_NotSuggestions()
    {
        var svc = CreateService(out var db);
        // 库存 100
        db.Stocks.Add(new Stock { WarehouseCd = "W1", LocationCd = "L1", ProductCd = "RAW-01", LotNo = "", AvailableQty = 100 });
        // 在途 50（确定済入库）
        db.InboundOrders.Add(new InboundOrder { InboundNo = "IN-1", Status = 1, WarehouseCd = "W1", ExpectedArrivalDate = Bucket });
        db.InboundOrderDetails.Add(new InboundOrderDetail { InboundNo = "IN-1", LineNo = 1, ProductCd = "RAW-01", ExpectedQty = 50, ReceivedQty = 0 });
        // 已确认计划订单 200（到货日落桶）
        db.PlannedOrders.Add(new Plan_PlannedOrder { ItemCd = "RAW-01", Qty = 200, RequiredDate = Bucket, Status = (int)PlannedOrderStatus.Confirmed });
        // 建议计划订单 300（不计）
        db.PlannedOrders.Add(new Plan_PlannedOrder { ItemCd = "RAW-01", Qty = 300, RequiredDate = Bucket, Status = (int)PlannedOrderStatus.Suggested });
        await db.SaveChangesAsync();

        var supply = await svc.GetSupplyAsync("RAW-01", Bucket);

        Assert.Equal(350m, supply);   // 100 + 50 + 200（建议 300 不计）
    }

    [Fact]
    public async Task GetSupply_ExcludesCancelledInboundAndDraftWorkOrder()
    {
        var svc = CreateService(out var db);
        // 取消的入库（Status=9）不计在途
        db.InboundOrders.Add(new InboundOrder { InboundNo = "IN-X", Status = 9, WarehouseCd = "W1", ExpectedArrivalDate = Bucket });
        db.InboundOrderDetails.Add(new InboundOrderDetail { InboundNo = "IN-X", LineNo = 1, ProductCd = "RAW-02", ExpectedQty = 99, ReceivedQty = 0 });
        // 草稿工单（Status=0 Draft）不计在制
        db.WorkOrders.Add(new WorkOrder { WorkOrderNo = "WO-D", Status = WorkOrderStatus.Draft, ProductCd = "RAW-02", ProductionQty = 88 });
        await db.SaveChangesAsync();

        Assert.Equal(0m, await svc.GetSupplyAsync("RAW-02", Bucket));
    }

    [Fact]
    public async Task GetSupply_InTransitNetsReceived_InWipNetsCompleted()
    {
        var svc = CreateService(out var db);
        // 在途 = ExpectedQty 60 − ReceivedQty 20 = 40（入庫中 Status=2）
        db.InboundOrders.Add(new InboundOrder { InboundNo = "IN-2", Status = 2, WarehouseCd = "W1", ExpectedArrivalDate = Bucket });
        db.InboundOrderDetails.Add(new InboundOrderDetail { InboundNo = "IN-2", LineNo = 1, ProductCd = "FG-01", ExpectedQty = 60, ReceivedQty = 20 });
        // 在制 = ProductionQty 100 − CompletedQty 30 = 70（发行済 Issued）
        db.WorkOrders.Add(new WorkOrder { WorkOrderNo = "WO-1", Status = WorkOrderStatus.Issued, ProductCd = "FG-01", ProductionQty = 100, CompletedQty = 30 });
        await db.SaveChangesAsync();

        Assert.Equal(110m, await svc.GetSupplyAsync("FG-01", Bucket));   // 40 + 70
    }

    [Fact]
    public async Task GetSupply_FirmPlannedBeyondBucket_NotCounted()
    {
        var svc = CreateService(out var db);
        // 已确认计划订单到货日在桶之后 → 不计入本桶供给
        db.PlannedOrders.Add(new Plan_PlannedOrder { ItemCd = "RAW-03", Qty = 500, RequiredDate = Bucket.AddDays(10), Status = (int)PlannedOrderStatus.Confirmed });
        await db.SaveChangesAsync();

        Assert.Equal(0m, await svc.GetSupplyAsync("RAW-03", Bucket));
    }
}
