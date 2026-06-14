namespace CP6.Core.Services.Wf;

/// <summary>
/// OA 流程实时通知（OA 章04）。依赖倒置：Core 定接口，WebApi 用 SignalR(NotifyHub) 实现
/// （同 IWmsNotifier/IMesNotifier 模式）。引擎建待办时推送给处理人。
/// </summary>
public interface IWfNotifier
{
    /// <summary>新待办产生 → 推送给处理人。</summary>
    Task TodoCreatedAsync(Guid assigneeId, Guid instanceId, Guid taskId, string flowKey);
}

/// <summary>空实现（无 SignalR 环境 / 单测用）。</summary>
public sealed class NullWfNotifier : IWfNotifier
{
    public Task TodoCreatedAsync(Guid assigneeId, Guid instanceId, Guid taskId, string flowKey) => Task.CompletedTask;
}
