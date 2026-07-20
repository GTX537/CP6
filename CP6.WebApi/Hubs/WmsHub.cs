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
    public const string GeneralGroup = "wms:all";
    private const string FilterGroupsKey = "wms:filter-groups";
    private readonly ILogger<WmsHub> _logger;
    public WmsHub(ILogger<WmsHub> logger) => _logger = logger;

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("WMS Hub 接続: {ConnectionId}", Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GeneralGroup);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("WMS Hub 切断: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>クライアントが特定倉庫の更新を購読</summary>
    public Task SubscribeWarehouse(string warehouseCd)
        => SubscribeFilterAsync($"wh:{warehouseCd}");

    public Task UnsubscribeWarehouse(string warehouseCd)
        => UnsubscribeFilterAsync($"wh:{warehouseCd}");

    /// <summary>クライアントが特定製品の更新を購読</summary>
    public Task SubscribeProduct(string productCd)
        => SubscribeFilterAsync($"product:{productCd}");

    public Task UnsubscribeProduct(string productCd)
        => UnsubscribeFilterAsync($"product:{productCd}");

    private HashSet<string> FilterGroups
    {
        get
        {
            if (Context.Items.TryGetValue(FilterGroupsKey, out var value)
                && value is HashSet<string> groups)
                return groups;
            var created = new HashSet<string>(StringComparer.Ordinal);
            Context.Items[FilterGroupsKey] = created;
            return created;
        }
    }

    private async Task SubscribeFilterAsync(string group)
    {
        var groups = FilterGroups;
        if (!groups.Add(group)) return;
        if (groups.Count == 1)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GeneralGroup);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    private async Task UnsubscribeFilterAsync(string group)
    {
        var groups = FilterGroups;
        if (!groups.Remove(group)) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        if (groups.Count == 0)
            await Groups.AddToGroupAsync(Context.ConnectionId, GeneralGroup);
    }
}
