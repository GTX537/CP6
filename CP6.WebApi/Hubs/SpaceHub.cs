using Microsoft.AspNetCore.SignalR;

namespace CP6.WebApi.Hubs;

/// <summary>
/// Space SignalR Hub — 库位ライフサイクルのリアルタイムイベント。
/// </summary>
/// <remarks>
/// 推送事件：
/// - LocationPublished：库位発布/停用が完了（{ batchNo, count, status }）
///
/// クライアント接続：
///   const conn = new HubConnectionBuilder().withUrl('/hubs/space').build()
///   conn.on('LocationPublished', payload => { /* events 页 reload */ })
///
/// 購読グループ：無し ── Space イベントは低頻・全播で十分（YAGNI）。
/// </remarks>
public class SpaceHub : Hub
{
    private readonly ILogger<SpaceHub> _logger;
    public SpaceHub(ILogger<SpaceHub> logger) => _logger = logger;

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("Space Hub 接続: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Space Hub 切断: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
