namespace CP6.Core.Services.Wf;

/// <summary>
/// 业务侧审批回调契约（OA 章05 §2）。<b>依赖单向</b>：OA 引擎只认本接口，不引用任何业务模块；
/// 各业务模块实现本接口（如财务凭证、采购订单）并注册到 DI，OA 终态时由 <see cref="ApprovalDispatcher"/>
/// 按 <see cref="BizType"/> 运行时多态直调。回调里业务走自己的状态机/BridgeHook 落地——OA 不碰业务表。
/// <para>
/// 原子性铁律（OA2-D5）：回调与引擎共享同一 scoped DbContext，分发在引擎最终 SaveChanges 前调用；
/// 回调若抛异常，流程终态与业务变更一并不落库——绝不出现"OA 已通过、业务没落地"。
/// 故回调遇业务失败应<b>抛异常</b>（而非静默返回），以触发整体回滚。
/// </para>
/// </summary>
public interface IApprovalCallback
{
    /// <summary>业务类型标识，与 Wf_ApprovalBinding.BizType / IApprovalService.SubmitAsync 的 bizType 对应。</summary>
    string BizType { get; }

    /// <summary>审批通过终态回调：业务把单据推进到"已确认/已过账"等正态（应幂等）。</summary>
    Task OnApprovedAsync(ApprovalCallbackContext ctx);

    /// <summary>审批驳回终态回调：业务把单据置回"草稿/已驳回"等态（应幂等）。</summary>
    Task OnRejectedAsync(ApprovalCallbackContext ctx);
}

/// <summary>
/// 终态回调上下文（OA 章05）。OA 不解析 BizId 语义，原样回传；附流程实例信息便于业务追溯与
/// 还原审批人（如财务凭证过账需"复核人≠制单人"，用 <see cref="DecidedById"/> 作 checker）。
/// </summary>
public sealed class ApprovalCallbackContext
{
    /// <summary>业务类型（= 触发的回调 BizType）。</summary>
    public string BizType { get; init; } = string.Empty;

    /// <summary>业务关联键（业务单号/Id，OA 原样回传，由业务自行解析为本模块主键类型）。</summary>
    public string BizId { get; init; } = string.Empty;

    /// <summary>OA 流程实例 Id（可回查审批痕迹 Wf_FlowHistory）。</summary>
    public Guid InstanceId { get; init; }

    /// <summary>流程发起人 Sys_User.Id。</summary>
    public Guid StarterId { get; init; }

    /// <summary>终态决策人 Sys_User.Id（末一个办理人：通过=末同意人 / 驳回=驳回人）；自动结束无人决策时为发起人。</summary>
    public Guid? DecidedById { get; init; }

    /// <summary>驳回理由（通过时一般为最后办理意见，可空）。</summary>
    public string? Reason { get; init; }
}
