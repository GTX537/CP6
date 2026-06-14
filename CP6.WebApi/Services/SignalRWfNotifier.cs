using CP6.Core.Services.Wf;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CP6.WebApi.Services;

/// <summary>
/// OA 流程通知 SignalR 实现（OA 章04）。复用现有 NotifyHub(/hubs/notify)。
/// 阶段1：广播 + 客户端按 assigneeId 过滤（同既有 Clients.All 用法）；TODO：定向 Clients.User 推送。
/// </summary>
public class SignalRWfNotifier : IWfNotifier
{
    private readonly IHubContext<NotifyHub> _hub;
    public SignalRWfNotifier(IHubContext<NotifyHub> hub) => _hub = hub;

    public Task TodoCreatedAsync(Guid assigneeId, Guid instanceId, Guid taskId, string flowKey)
        => _hub.Clients.All.SendAsync("WfTodoCreated", new { assigneeId, instanceId, taskId, flowKey });
}
