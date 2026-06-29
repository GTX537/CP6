namespace CP6.Core.Services.Wf;

/// <summary>流程实例状态（Wf_FlowInstance.Status）。</summary>
public static class FlowInstanceStatus
{
    public const int Running = 0;     // 进行中
    public const int Approved = 1;    // 通过（走到 end）
    public const int Rejected = 2;    // 驳回（会签判否）
    public const int Withdrawn = 3;   // 撤回（发起人主动）
    public const int Suspended = 4;   // 挂起待指派（审批人算不出）
    /// <summary>草稿（暫存）：有实例、无 token、未进流程（umbrella R2）。提交即 StartDraftAsync 就地起 token。</summary>
    public const int Draft = 5;
}

/// <summary>流程任务状态（Wf_FlowTask.Status）。!=Pending 即"已办"，是幂等闸门依据。</summary>
public static class FlowTaskStatus
{
    public const int Pending = 0;     // 待办
    public const int Approved = 1;    // 同意
    public const int Rejected = 2;    // 驳回
    public const int Cancelled = 3;   // 作废（节点已决/实例撤回/退回时清在途）
    public const int Suspended = 4;   // 挂起（前加签时原审批人临时挂起，加签人审完再激活，章07 §3）
}

/// <summary>流程令牌状态（Wf_FlowToken.Status）。Active 才参与流转 / join 计数。</summary>
public static class FlowTokenStatus
{
    public const int Active = 0;
    public const int Consumed = 1;
    public const int Cancelled = 2;
}

/// <summary>传签履历关卡状态（Wf_FlowFormTo.Status）。</summary>
public static class FlowFormToStatus
{
    public const int Pending = 0;     // 待签
    public const int Approved = 1;    // 同意
    public const int Rejected = 2;    // 驳回
    public const int Transferred = 3; // 转交
    public const int AddSigned = 4;   // 加签
    public const int Skipped = 5;     // 跳过 / 会签未轮到
    public const int Voided = 6;      // 作废（驳回连坐 / 退回清场）
    public const int SentBack = 7;    // 退回上一档(区别于普通作废 Voided=6)
}
