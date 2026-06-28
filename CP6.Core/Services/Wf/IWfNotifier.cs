namespace CP6.Core.Services.Wf;

/// <summary>
/// OA 流程实时通知（OA 章04）。依赖倒置：Core 定接口，WebApi 用 SignalR(NotifyHub) 实现
/// （同 IWmsNotifier/IMesNotifier 模式）。引擎建待办时推送给处理人。
/// </summary>
public interface IWfNotifier
{
    /// <summary>新待办产生 → 推送给处理人。</summary>
    Task TodoCreatedAsync(Guid assigneeId, Guid instanceId, Guid taskId, string flowKey);

    /// <summary>流程签核完成（整体通过）→ 推送给发起人（OA Phase D-1）。</summary>
    Task FlowApprovedAsync(Guid starterId, Guid instanceId, string flowKey);

    /// <summary>流程被驳回 → 推送给发起人（OA Phase D-1）。</summary>
    Task FlowRejectedAsync(Guid starterId, Guid instanceId, string flowKey, string? comment);
}

/// <summary>空实现（无 SignalR 环境 / 单测用）。</summary>
public sealed class NullWfNotifier : IWfNotifier
{
    public Task TodoCreatedAsync(Guid assigneeId, Guid instanceId, Guid taskId, string flowKey) => Task.CompletedTask;
    public Task FlowApprovedAsync(Guid starterId, Guid instanceId, string flowKey) => Task.CompletedTask;
    public Task FlowRejectedAsync(Guid starterId, Guid instanceId, string flowKey, string? comment) => Task.CompletedTask;
}
