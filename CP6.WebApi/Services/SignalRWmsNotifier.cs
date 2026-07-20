using CP6.Core.Services.Wms;
using CP6.Core.Utilities;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Localization;

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
    private readonly IStringLocalizer _localizer;

    public SignalRWmsNotifier(
        IHubContext<WmsHub> hub,
        INotificationPublisher notifier,
        ILogger<SignalRWmsNotifier> logger,
        IStringLocalizer localizer)
    {
        _hub = hub;
        _notifier = notifier;
        _logger = logger;
        _localizer = localizer;
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
        // 未过滤客户端走通用组；已订阅客户端只走仓库/产品过滤组，避免重复事件。
        await _hub.Clients.Group(WmsHub.GeneralGroup).SendAsync("StockChanged", evt);
        if (!string.IsNullOrEmpty(evt.WarehouseCd))
            await _hub.Clients.Group($"wh:{evt.WarehouseCd}").SendAsync("StockChanged", evt);
        if (!string.IsNullOrEmpty(evt.ProductCd))
            await _hub.Clients.Group($"product:{evt.ProductCd}").SendAsync("StockChanged", evt);
    }

    public async Task NotifyInboundReceivedAsync(string receiptNo, string warehouseCd)
    {
        var payload = new { receiptNo, warehouseCd, at = DateTime.Now };
        await Task.WhenAll(
            _hub.Clients.Group(WmsHub.GeneralGroup).SendAsync("InboundReceived", payload),
            _hub.Clients.Group($"wh:{warehouseCd}").SendAsync("InboundReceived", payload)
        );

        await PublishBusinessAsync(new BusinessNotification
        {
            EventType = "InboundReceived",
            Level = "Info",
            Title = _localizer["入庫完了"],
            Message = _localizer["入庫受領 {0}（倉庫 {1}）が完了しました。", receiptNo, warehouseCd],
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
            Title = _localizer["出荷完了"],
            Message = string.IsNullOrEmpty(packageNo)
                ? _localizer["出荷指示 {0} の出荷が完了しました。", outboundNo]
                : _localizer["出荷指示 {0} の出荷が完了しました（荷姿 {1}）。", outboundNo, packageNo],
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
            Title = diffLines > 0 ? _localizer["棚卸差異あり"] : _localizer["棚卸完了"],
            Message = _localizer["棚卸 {0} が完了しました（差異 {1} 件）。", stockTakeNo, diffLines],
            Source = "WMS",
            RefNo = stockTakeNo
        });
    }
}
