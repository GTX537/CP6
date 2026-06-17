using CP6.Entity.DomainModels.Plan;

namespace CP6.Core.Services.Plan.Contracts;

/// <summary>
/// 计划订单 → 采购PR 转单契约（MRP P1 · MP-D4）。采购模块实现；P1 桩。
/// 同步直线委托（不走 Phase6 事件，跨模块同步），把计划订单 + Pegging 传给采购生成 PR。
/// </summary>
public interface IPlanToPrService
{
    /// <summary>由计划订单生成采购申请 PR，返回 PR 单号。</summary>
    Task<string> CreatePrFromPlannedOrderAsync(Plan_PlannedOrder plannedOrder, IReadOnlyList<Plan_Pegging> peggings);
}

/// <summary>P1 桩：采购模块真实实现落地前的占位（返回桩单号，不实建 PR）。</summary>
public class PlanToPrServiceStub : IPlanToPrService
{
    public Task<string> CreatePrFromPlannedOrderAsync(Plan_PlannedOrder plannedOrder, IReadOnlyList<Plan_Pegging> peggings)
        => Task.FromResult($"PR-STUB-{plannedOrder.ItemCd}");
}
