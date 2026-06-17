namespace CP6.Core.Services.Plan;

/// <summary>
/// 计划订单人确认转单服务（MRP P1 · spec §9）。建议 → 确认（进供给）→ 转单（PR/工单）/ 忽略。
/// </summary>
public interface IPlanConvertService
{
    /// <summary>确认计划订单（建议 → 已确认；此后计入 scheduled receipt 供给、复算保留）。</summary>
    Task ConfirmAsync(Guid plannedOrderId, string? user);

    /// <summary>转单：采购类 → PR、生产类 → 工单；回填 ConvertedDocNo、状态置已转单。返回下游单号。</summary>
    Task<string> ConvertAsync(Guid plannedOrderId, string? user);

    /// <summary>忽略计划订单（人工放弃，置已忽略；不计供给）。</summary>
    Task IgnoreAsync(Guid plannedOrderId, string? user);
}
