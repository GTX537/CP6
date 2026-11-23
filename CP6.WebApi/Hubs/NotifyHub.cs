using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
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
[Authorize]
public class NotifyHub : Hub
{
    private readonly ILogger<NotifyHub> _logger;
    private readonly ITenantContext _tenant;
    private readonly ICurrentPermissionContext _actor;
    private readonly ScopeAuthorizationOptionsV1 _scope;

    public NotifyHub(ILogger<NotifyHub> logger, ITenantContext tenant, ICurrentPermissionContext actor,
        IOptions<ScopeAuthorizationOptionsV1> scope)
    {
        _logger = logger;
        _tenant = tenant;
        _actor = actor;
        _scope = scope.Value;
    }

    internal static string WorkflowRecipientGroup(Guid tenant, Guid environment, Guid user)
        => $"oa-notification:{tenant:N}:{environment:N}:{user:N}";

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("客户端连接: {ConnectionId}", Context.ConnectionId);
        if (_tenant.CurrentTenantId != Guid.Empty && _scope.EnvironmentId != Guid.Empty &&
            Context.User?.HasClaim(x => x.Type == "impersonator_id") == false)
        {
            try
            {
                var current = await _actor.GetAsync();
                await Groups.AddToGroupAsync(Context.ConnectionId,
                    WorkflowRecipientGroup(_tenant.CurrentTenantId, _scope.EnvironmentId, current.UserId), Context.ConnectionAborted);
            }
            catch (CurrentPermissionUnavailableException) { /* 本人范围缺证时不加入通知刷新组。 */ }
        }
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("客户端断开: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
