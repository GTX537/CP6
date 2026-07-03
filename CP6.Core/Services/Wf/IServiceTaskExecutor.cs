namespace CP6.Core.Services.Wf;

/// <summary>服务任务执行器契约（§11「泛化 IApprovalCallback」落地）。
/// 实现按 Key 注册到引擎执行器字典，按 Kind/VisibleInDesigner 暴露到服务目录（P1-6）。</summary>
public interface IServiceTaskExecutor
{
    string Key { get; }                 // webApi→"webApi"; dataWriteback→动作名
    string Kind { get; }                // "dataWriteback" | "webApi" | "internal"（P1-6：服务目录据此过滤）
    bool VisibleInDesigner { get; }     // dataWriteback 动作=true; WebApiExecutor=false（不当回写动作暴露）
    string DisplayName { get; }         // 设计器目录显示（可 i18n 键）

    /// <summary>执行。实现<b>必须幂等</b>（async at-least-once，崩溃可能重投；用 ctx.JobId 作幂等键）。</summary>
    Task<ServiceTaskResult> ExecuteAsync(ServiceTaskContext ctx);
}

/// <summary>执行器运行上下文（P1-2：补 JobId/AttemptNo/ActorId/NowUtc）。</summary>
public sealed class ServiceTaskContext
{
    public required Guid InstanceId { get; init; }
    public required Guid TokenId { get; init; }
    public required string NodeId { get; init; }
    public required Guid StarterId { get; init; }
    public required Guid JobId { get; init; }       // 幂等键来源（async）；sync 内联用 Guid.Empty
    public required int AttemptNo { get; init; }     // 第几次执行（1-based）
    public required Guid ActorId { get; init; }      // SystemActor（async/timer）或发起人（sync）
    public required DateTime NowUtc { get; init; }
    public string? VarsJson { get; init; }           // 表单数据，供参数模板求值
    public string? ActionRefJson { get; init; }      // 固化动作绑定快照（§3.5）
    // executor 通过注入服务（DB/HttpClient）干活，不直接持有 FlowEngine
}

/// <summary>执行结果。用静态工厂 Ok / Fail 构造。</summary>
public sealed class ServiceTaskResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public Dictionary<string, object?>? OutputVars { get; init; }   // §3.6 合并规则

    public static ServiceTaskResult Ok(Dictionary<string, object?>? outputVars = null)
        => new() { Success = true, OutputVars = outputVars };

    public static ServiceTaskResult Fail(string error)
        => new() { Success = false, Error = error };
}
