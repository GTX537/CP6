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
    /// act-as 办理：actorId（代理人 me）代 onBehalfOf（被代理人 X）办理其待办。
    /// 办理逻辑与 ActAsync 等价，但履历 ActualHandlerId = actorId、OnBehalfOfId = onBehalfOf。
    /// onBehalfOf = null 时行为同 ActAsync。授权由控制器 AssertActiveGrant 把关，引擎不查委派。
    /// </summary>
    Task ActAsAsync(Guid taskId, Guid actorId, Guid? onBehalfOf, bool approve, string? comment = null);

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

    /// <summary>
    /// 登记审批委派（章07 §5）：委托人在有效期内把审批权委派给代理人。引擎建待办时若原审批人处
    /// 委派期，则把 assignee 替换为代理人并双记痕迹（v1 仅建待办时替换，存量在途待办不转）。返回委派 Id。
    /// </summary>
    Task<Guid> SetDelegateAsync(Guid grantorId, Guid delegateId, DateTime validFrom, DateTime validTo, string? scope = null, string? remark = null);

    /// <summary>就地起草稿：把 Draft 实例推进进流程（spawn 根 token + 进首节点 + 读模型随推进落库）。
    /// 仅发起人可提交；非草稿态/越权 → E-WF-003。幂等性同 SubmitAsync（一次 SaveChanges）。</summary>
    Task StartDraftAsync(Guid instanceId, Guid actorId);

    /// <summary>转交（umbrella §4.5）：把 Pending 待办改派给同租户 toUserId（保 TokenId/NodeId，不流转）。
    /// 履历原行 Transferred + 受让人新 Pending 行 + AddHistory("transfer") + 通知。非待办/目标非法 → E-WF-002。</summary>
    Task TransferAsync(Guid taskId, Guid actorId, Guid toUserId, string? comment = null);
}
