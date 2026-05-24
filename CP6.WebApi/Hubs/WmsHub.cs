using Microsoft.AspNetCore.SignalR;

namespace CP6.WebApi.Hubs;

/// <summary>
/// WMS SignalR Hub — 倉庫リアルタイムイベント
/// </summary>
/// <remarks>
/// 推送事件：
/// - StockChanged：在庫変動（IN/OUT/MOVE/ADJ/RSV/UNRSV）
/// - InboundReceived：入庫実績確定
/// - OutboundShipped：出庫確定 + 梱包NO
/// - StockTakeCompleted：棚卸完了
///
/// クライアント接続：
///   const conn = new HubConnectionBuilder().withUrl('/hubs/wms').build()
///   conn.on('StockChanged', payload => { ... })
///
/// 購読グループ：
///   - wh:{warehouseCd}      倉庫別の絞り込み
///   - product:{productCd}    製品別の絞り込み
/// </remarks>
public class WmsHub : Hub
{
    private readonly ILogger<WmsHub> _logger;
    public WmsHub(ILogger<WmsHub> logger) => _logger = logger;

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("WMS Hub 接続: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("WMS Hub 切断: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>クライアントが特定倉庫の更新を購読</summary>
    public Task SubscribeWarehouse(string warehouseCd)
        => Groups.AddToGroupAsync(Context.ConnectionId, $"wh:{warehouseCd}");

    public Task UnsubscribeWarehouse(string warehouseCd)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"wh:{warehouseCd}");

    /// <summary>クライアントが特定製品の更新を購読</summary>
    public Task SubscribeProduct(string productCd)
        => Groups.AddToGroupAsync(Context.ConnectionId, $"product:{productCd}");

    public Task UnsubscribeProduct(string productCd)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"product:{productCd}");
}
