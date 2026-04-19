using Microsoft.AspNetCore.SignalR;

namespace CP6.WebApi.Hubs;

/// <summary>
/// SignalR 通知中心 — 服务端向所有连接的客户端推送实时消息
///
/// 面试要点：
/// 1. SignalR 是 .NET 的实时通信框架，底层自动选择最佳传输方式：
///    - WebSocket（首选，全双工）
///    - Server-Sent Events（次选）
///    - Long Polling（兜底）
/// 2. Hub 是服务端的"消息中心"，客户端连接到 Hub
/// 3. Clients.All.SendAsync() = 广播给所有连接的客户端
/// 4. MOM 系统场景：设备状态变化 → SignalR 推送 → 大屏实时刷新
/// </summary>
public class NotifyHub : Hub
{
    private readonly ILogger<NotifyHub> _logger;

    public NotifyHub(ILogger<NotifyHub> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("客户端连接: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("客户端断开: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
