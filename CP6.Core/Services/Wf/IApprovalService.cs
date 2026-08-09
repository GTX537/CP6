namespace CP6.Core.Services.Wf;

/// <summary>
/// 审批服务（OA 章05 §2 ★）。业务模块接入 OA 的<b>唯一入口</b>：业务调 SubmitAsync 起审批
/// （按 Wf_ApprovalBinding 选流程、喂 formSnapshot 作流程变量、防重），OA 终态时反向回调
/// <see cref="IApprovalCallback"/>。业务编译期只依赖本接口 + 回调契约，不依赖 OA 实现细节。
/// </summary>
public interface IApprovalService
{
    /// <summary>
    /// 起业务审批。按 bizType 找启用的绑定 → 起对应流程，bizId/formSnapshot 随实例落库（OA 不回查业务表）。
    /// 同一 (bizType,bizId) 已有进行中审批 → 抛异常（防重复提交）。返回流程实例 Id。
    /// </summary>
    Task<Guid> SubmitAsync(string bizType, string bizId, Guid starterId, object? formSnapshot = null,
        Guid? instanceId = null);

    /// <summary>查业务单据的审批状态（取最近一条实例；无实例 → None）。</summary>
    Task<ApprovalStatus> GetStatusAsync(string bizType, string bizId);
}

/// <summary>审批状态（对齐 FlowInstanceStatus，供业务侧判断）。</summary>
public enum ApprovalStatus
{
    /// <summary>无审批记录</summary>
    None = -1,
    /// <summary>进行中</summary>
    Running = 0,
    /// <summary>已通过</summary>
    Approved = 1,
    /// <summary>已驳回</summary>
    Rejected = 2,
    /// <summary>已撤回</summary>
    Withdrawn = 3,
    /// <summary>挂起待指派</summary>
    Suspended = 4,
}
