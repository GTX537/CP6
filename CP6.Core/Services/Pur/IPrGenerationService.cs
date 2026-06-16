using CP6.Entity.DomainModels.Pur;

namespace CP6.Core.Services.Pur;

/// <summary>
/// 采购申请需求驱动生成服务（采购 章05 §3）。把"缺料/工单 → 自动开 PR"这根线接上，
/// 让采购从"被动下单"变成"需求驱动"。复用 Phase9 缺料检测与 MES 工单材料，不重建检测逻辑。
/// </summary>
public interface IPrGenerationService
{
    /// <summary>
    /// 缺料反流自动建 PR（章05 §3）：复用 Phase9 <c>MaterialShortage</c>。
    /// 缺口量 = RequiredQty − AvailableQty；估价取价表/历史；建议供应商按历史。
    /// 幂等：同一缺料已生成过 PR（按 SourceRefNo 锚）则返回 null，防重复生成。
    /// </summary>
    /// <returns>新建 PR 号；缺料不存在/缺口非正/已生成过 → null。</returns>
    Task<string?> GenerateFromShortageAsync(Guid shortageId, string? userName);

    /// <summary>
    /// 工单 BOM 缺料自动建 PR（章05 §3）：扫工单材料 <c>SupplyStatus=3（不足）</c> 的行，
    /// 缺口量 = PlanQty − ActualQty；同缺料路径估价/建议供应商。
    /// 幂等：同工单同料已生成过 PR 行则跳过，全部已生成 → 返回 null。
    /// </summary>
    /// <returns>新建 PR 号；无缺料材料/全部已生成 → null。</returns>
    Task<string?> GenerateFromWorkOrderAsync(string workOrderNo, string? userName);
}
