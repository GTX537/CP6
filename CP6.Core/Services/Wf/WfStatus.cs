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
    /// <summary>剪枝（分支驳回不连坐，spec §2.3）。区别 Cancelled=清场类作废：Pruned 是分支死亡，join 动态计票时天然从等待集消失。</summary>
    public const int Pruned = 3;
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

/// <summary>服务任务异步停泊作业状态（Wf_ServiceJob.Status,§2.4）。</summary>
public static class ServiceJobStatus
{
    public const int Pending = 0;    // 待执行(入队/等待 DueAt)
    public const int Running = 1;    // 执行中(已抢占 lease)
    public const int Succeeded = 2;  // 成功(终态)
    public const int Failed = 3;     // 失败耗尽重试(终态)
    public const int Cancelled = 4;  // 已取消(实例驳回/撤回,终态)
}

/// <summary>服务任务节点种类(§2.4)。对应 FlowNode.ServiceKind。</summary>
public static class ServiceKind
{
    public const string DataWriteback = "dataWriteback";
    public const string WebApi = "webApi";
    public const string Timer = "timer";
}

/// <summary>服务任务执行模式(§2.4)。对应 FlowNode.ServiceMode。</summary>
public static class ServiceMode
{
    public const string Sync = "sync";
    public const string Async = "async";
}

/// <summary>流程触发器类型（事件触发 start 增量，spec §2.1）。</summary>
public static class WfTriggerType
{
    public const int Timer = 0;
    public const int Event = 1;
    public const int Message = 2;
}

/// <summary>Wf_ServiceJob.Kind 的内部值域扩展（子流程 spec §3.2）。不进 <see cref="ServiceKind"/>：
/// 设计器目录（GetServiceCatalog）、节点校验（FlowSchemaValidator.KnownServiceKinds）永不见此值；
/// 扫描 worker 在 lease 后按 Kind 短路分派，不进 executor 注册表。</summary>
public static class WfJobKind
{
    public const string SubFlowResume = "subFlowResume";
}

/// <summary>子流程完成策略（spec §2.1 SubCompletionPolicy 值域）。</summary>
public static class SubFlowCompletionPolicy
{
    public const string All = "all";   // 默认:全部 Approved 才过;任一 Rejected/Withdrawn → 错误处置
    public const string Any = "any";   // 首个 Approved 即过(级联撤回其余);全驳/撤才错误处置
}

/// <summary>子流程守卫常量（spec §3.1/§5）。</summary>
public static class SubFlowLimits
{
    /// <summary>嵌套深度上限（E-WF-026）：保存时 DFS 与运行时 ParentInstanceId 链上溯同口径。</summary>
    public const int MaxDepth = 8;
    /// <summary>多实例 N 上限缺省（app 配置键 Wfs:SubFlowMaxInstances 可覆盖）。</summary>
    public const int DefaultMaxInstances = 100;
}
