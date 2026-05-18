using Microsoft.AspNetCore.SignalR;

namespace CP6.WebApi.Hubs;

/// <summary>
/// MES SignalR Hub — 製造現場リアルタイムイベント
/// </summary>
/// <remarks>
/// 推送事件：
/// - ProductionReported：実績入力 → 大屏 / OEE 即時更新
/// - DefectIssued：不良起票 → アラート
/// - MachineStatusChanged：設備状態変化 → 設備グリッド即時反映
/// - WorkOrderStatusChanged：指図ステータス遷移
/// - DowntimeRegistered：停止記録 → 設備カードに表示
///
/// クライアント接続：
/// const conn = new HubConnectionBuilder().withUrl('/hubs/mes').build()
/// conn.on('ProductionReported', payload => { ... })
/// </remarks>
public class MesHub : Hub
{
    private readonly ILogger<MesHub> _logger;
    public MesHub(ILogger<MesHub> logger) => _logger = logger;

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("MES Hub 接続: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("MES Hub 切断: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>クライアントが特定指図の更新を購読</summary>
    public Task SubscribeWorkOrder(string workOrderNo)
        => Groups.AddToGroupAsync(Context.ConnectionId, $"wo:{workOrderNo}");

    public Task UnsubscribeWorkOrder(string workOrderNo)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"wo:{workOrderNo}");

    /// <summary>クライアントが特定設備の更新を購読</summary>
    public Task SubscribeMachine(string machineCd)
        => Groups.AddToGroupAsync(Context.ConnectionId, $"machine:{machineCd}");

    public Task UnsubscribeMachine(string machineCd)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"machine:{machineCd}");
}
