using CP6.Core.Services.Wms;
using CP6.Core.Utilities;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CP6.WebApi.Services;

/// <summary>
/// <see cref="IWmsNotifier"/> の SignalR 実装。
///
/// 二層の通知を使い分ける：
///  - WmsHub への直接配信 … リアルタイム・揮発の WMS ダッシュボード更新（StockChanged 等）。
///  - INotificationPublisher(RabbitMQ) … 「人が気づくべき業務イベント」を確実配信の
///    通知/アラートセンターへ流す（出荷完了・棚卸差異・入庫完了）。後でメール等にも振り分け可能。
/// </summary>
public class SignalRWmsNotifier : IWmsNotifier
{
    private readonly IHubContext<WmsHub> _hub;
    private readonly INotificationPublisher _notifier;
    private readonly ILogger<SignalRWmsNotifier> _logger;

    public SignalRWmsNotifier(
        IHubContext<WmsHub> hub,
        INotificationPublisher notifier,
        ILogger<SignalRWmsNotifier> logger)
    {
        _hub = hub;
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>業務通知を best-effort で発行（失敗しても本処理・SignalR 配信は止めない）。</summary>
    private async Task PublishBusinessAsync(BusinessNotification notice)
    {
        try
        {
            if (_notifier.IsConnected)
                await _notifier.PublishNotificationAsync(notice);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "業務通知の発行に失敗（旁路、無視）: {Event}", notice.EventType);
        }
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

    public async Task NotifyInboundReceivedAsync(string receiptNo, string warehouseCd)
    {
        var payload = new { receiptNo, warehouseCd, at = DateTime.Now };
        await Task.WhenAll(
            _hub.Clients.All.SendAsync("InboundReceived", payload),
            _hub.Clients.Group($"wh:{warehouseCd}").SendAsync("InboundReceived", payload)
        );

        await PublishBusinessAsync(new BusinessNotification
        {
            EventType = "InboundReceived",
            Level = "Info",
            Title = "入庫完了",
            Message = $"入庫受領 {receiptNo}（倉庫 {warehouseCd}）が完了しました。",
            Source = "WMS",
            RefNo = receiptNo
        });
    }

    public async Task NotifyOutboundShippedAsync(string outboundNo, string? packageNo)
    {
        var payload = new { outboundNo, packageNo, at = DateTime.Now };
        await _hub.Clients.All.SendAsync("OutboundShipped", payload);

        await PublishBusinessAsync(new BusinessNotification
        {
            EventType = "OutboundShipped",
            Level = "Info",
            Title = "出荷完了",
            Message = $"出荷指示 {outboundNo} の出荷が完了しました" +
                      (string.IsNullOrEmpty(packageNo) ? "。" : $"（荷姿 {packageNo}）。"),
            Source = "WMS",
            RefNo = outboundNo
        });
    }

    public async Task NotifyStockTakeCompletedAsync(string stockTakeNo, int diffLines)
    {
        var payload = new { stockTakeNo, diffLines, at = DateTime.Now };
        await _hub.Clients.All.SendAsync("StockTakeCompleted", payload);

        // 差異ありは注意喚起 → Warning、差異なしは Info
        await PublishBusinessAsync(new BusinessNotification
        {
            EventType = "StockTakeCompleted",
            Level = diffLines > 0 ? "Warning" : "Info",
            Title = diffLines > 0 ? "棚卸差異あり" : "棚卸完了",
            Message = $"棚卸 {stockTakeNo} が完了しました（差異 {diffLines} 件）。",
            Source = "WMS",
            RefNo = stockTakeNo
        });
    }
}
