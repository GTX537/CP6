using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Plan;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Plan;

/// <inheritdoc/>
public class SupplyService : ISupplyService
{
    private readonly CP6Context _db;

    public SupplyService(CP6Context db)
    {
        _db = db;
    }

    public async Task<decimal> GetSupplyAsync(string itemCd, DateTime bucket)
        => (await GetSupplyBreakdownAsync(itemCd, bucket)).Total;

    public async Task<SupplyBreakdown> GetSupplyBreakdownAsync(string itemCd, DateTime bucket)
    {
        // ① 现库存：公司级汇总可用量（剔除召回；P1 不细分仓库位 D7）
        var onHand = await _db.Stocks.AsNoTracking()
            .Where(s => s.ProductCd == itemCd && !s.IsDeleted && !s.RecallFlag)
            .SumAsync(s => s.AvailableQty);

        // ② 在途：未关闭入库预定（确定済/入庫中）的未到货量 = 予定 − 已入
        var inTransit = await (
            from d in _db.InboundOrderDetails.AsNoTracking()
            join h in _db.InboundOrders.AsNoTracking() on d.InboundNo equals h.InboundNo
            where d.ProductCd == itemCd && !d.IsDeleted && !h.IsDeleted
                  && (h.Status == 1 || h.Status == 2)        // 1=確定済 2=入庫中（部分入庫）
                  && d.ExpectedQty > d.ReceivedQty
            select (decimal?)(d.ExpectedQty - d.ReceivedQty)).SumAsync() ?? 0m;

        // ③ 在制：已下达且未完工/未取消工单的剩余产出 = 生产数 − 完成数
        var inWip = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.ProductCd == itemCd && !w.IsDeleted
                        && (w.Status == WorkOrderStatus.Confirmed
                            || w.Status == WorkOrderStatus.Issued
                            || w.Status == WorkOrderStatus.InProgress
                            || w.Status == WorkOrderStatus.Interrupted
                            || w.Status == WorkOrderStatus.Inspected)
                        && w.ProductionQty > w.CompletedQty)
            .SumAsync(w => w.ProductionQty - w.CompletedQty);

        // ④ 已确认/已转单计划订单：scheduled receipt，按需求日 ≤ 日桶落桶（建议态不计）
        var firmPlanned = await _db.PlannedOrders.AsNoTracking()
            .Where(p => p.ItemCd == itemCd && !p.IsDeleted
                        && (p.Status == (int)PlannedOrderStatus.Confirmed
                            || p.Status == (int)PlannedOrderStatus.Converted)
                        && p.RequiredDate <= bucket)
            .SumAsync(p => p.Qty);

        return new SupplyBreakdown(onHand, inTransit, inWip, firmPlanned);
    }
}
