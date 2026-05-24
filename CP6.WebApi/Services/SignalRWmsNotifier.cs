using CP6.Core.Services.Wms;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CP6.WebApi.Services;

/// <summary>
/// <see cref="IWmsNotifier"/> の SignalR 実装。
/// </summary>
public class SignalRWmsNotifier : IWmsNotifier
{
    private readonly IHubContext<WmsHub> _hub;
    private readonly ILogger<SignalRWmsNotifier> _logger;

    public SignalRWmsNotifier(IHubContext<WmsHub> hub, ILogger<SignalRWmsNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyStockChangedAsync(StockChangedEvent evt)
    {
        // 全クライアント + 倉庫グループ + 製品グループの 3 経路に配信
        await _hub.Clients.All.SendAsync("StockChanged", evt);
        if (!string.IsNullOrEmpty(evt.WarehouseCd))
            await _hub.Clients.Group($"wh:{evt.WarehouseCd}").SendAsync("StockChanged", evt);
        if (!string.IsNullOrEmpty(evt.ProductCd))
            await _hub.Clients.Group($"product:{evt.ProductCd}").SendAsync("StockChanged", evt);
    }

    public Task NotifyInboundReceivedAsync(string receiptNo, string warehouseCd)
    {
        var payload = new { receiptNo, warehouseCd, at = DateTime.Now };
        return Task.WhenAll(
            _hub.Clients.All.SendAsync("InboundReceived", payload),
            _hub.Clients.Group($"wh:{warehouseCd}").SendAsync("InboundReceived", payload)
        );
    }

    public Task NotifyOutboundShippedAsync(string outboundNo, string? packageNo)
    {
        var payload = new { outboundNo, packageNo, at = DateTime.Now };
        return _hub.Clients.All.SendAsync("OutboundShipped", payload);
    }

    public Task NotifyStockTakeCompletedAsync(string stockTakeNo, int diffLines)
    {
        var payload = new { stockTakeNo, diffLines, at = DateTime.Now };
        return _hub.Clients.All.SendAsync("StockTakeCompleted", payload);
    }
}
