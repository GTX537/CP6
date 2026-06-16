namespace CP6.Core.Services.Wf;

/// <summary>
/// 流程引擎（OA 章03 ★）。状态机解释器：实例 = 状态载体，一次 tick = (当前节点, 动作) → 下一节点 + 副作用。
/// 全状态落库、幂等可重放。审批人委托 IApproverResolver，条件流转委托 ConditionEvaluator。
/// </summary>
public interface IFlowEngine
{
    /// <summary>起流程：建实例 → 进首节点（建待办/挂起/直达 end）。返回实例 Id。</summary>
    Task<Guid> SubmitAsync(string flowKey, Guid starterId, string varsJson, string? bizType = null, string? bizId = null);

    /// <summary>办理任务（同意/驳回）。幂等：已办任务再办无效，不重复流转。</summary>
    Task ActAsync(Guid taskId, Guid actorId, bool approve, string? comment = null);

    /// <summary>
    /// 退回（章07 §2）：从当前任务退回到目标节点。作废本实例所有在途待办（含挂起的前加签）、
    /// CurrentNode 回退到目标节点并重建其待办；FlowHistory 追加 sendback（不删历史）。
    /// 三落点（上一步/指定节点/发起人）只是 targetNodeId 不同。幂等：任务已办则无效。
    /// </summary>
    Task SendBackAsync(Guid taskId, Guid actorId, string targetNodeId, string? comment = null);

    /// <summary>
    /// 加签（章07 §3）：在当前任务所在节点加一个实例级临时审批人（不改 FlowDef）。
    /// after=后加签（与原审批人并存，节点未流转直到加签人也审完）；before=前加签（发起加签的原任务
    /// 挂起，加签人审完再激活原任务）。加签待办计入会签计票。超加签层数上限抛异常。返回新任务 Id。
    /// </summary>
    Task<Guid> AddSignAsync(Guid taskId, Guid actorId, Guid addSigneeId, string source, string? comment = null);
}
