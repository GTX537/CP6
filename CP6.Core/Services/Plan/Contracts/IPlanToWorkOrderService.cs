using CP6.Entity.DomainModels.Plan;

namespace CP6.Core.Services.Plan.Contracts;

/// <summary>
/// 计划订单 → MES 工单 转单契约（MRP P1 · MP-D4）。MES WorkOrderService 的薄适配；P1 桩。
/// 同步直线委托，把生产类计划订单 + Pegging 传给 MES 生成工单。
/// </summary>
public interface IPlanToWorkOrderService
{
    /// <summary>由计划订单生成 MES 工单，返回工单号。</summary>
    Task<string> CreateWorkOrderFromPlannedOrderAsync(Plan_PlannedOrder plannedOrder, IReadOnlyList<Plan_Pegging> peggings);
}

/// <summary>P1 桩：MES 工单适配真实落地前的占位（返回桩单号，不实建工单）。</summary>
public class PlanToWorkOrderServiceStub : IPlanToWorkOrderService
{
    public Task<string> CreateWorkOrderFromPlannedOrderAsync(Plan_PlannedOrder plannedOrder, IReadOnlyList<Plan_Pegging> peggings)
        => Task.FromResult($"WO-STUB-{plannedOrder.ItemCd}");
}
