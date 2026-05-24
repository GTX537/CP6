namespace CP6.Core.Services.Wms;

/// <summary>
/// WMS リアルタイム通知契約（依存逆転 — CP6.Core は SignalR に依存しない）。
/// </summary>
/// <remarks>
/// 実装は CP6.WebApi/Services/SignalRWmsNotifier に存在。
/// テスト時は <see cref="NoOpWmsNotifier"/> を注入する。
/// </remarks>
public interface IWmsNotifier
{
    /// <summary>在庫変動（IN/OUT/MOVE/ADJ/RSV/UNRSV）</summary>
    Task NotifyStockChangedAsync(StockChangedEvent evt);

    /// <summary>入庫実績確定</summary>
    Task NotifyInboundReceivedAsync(string receiptNo, string warehouseCd);

    /// <summary>出庫指示出荷確定</summary>
    Task NotifyOutboundShippedAsync(string outboundNo, string? packageNo);

    /// <summary>棚卸完了（ADJ 反映済）</summary>
    Task NotifyStockTakeCompletedAsync(string stockTakeNo, int diffLines);
}

/// <summary>在庫変動イベント payload</summary>
public class StockChangedEvent
{
    public string TxnNo { get; set; } = string.Empty;
    public string TxnType { get; set; } = string.Empty; // IN/OUT/MOVE/ADJ/RSV/UNRSV
    public DateTime TxnAt { get; set; } = DateTime.Now;
    public string WarehouseCd { get; set; } = string.Empty;
    public string LocationCd { get; set; } = string.Empty;
    public string ProductCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public string? RelatedNo { get; set; }
    public string? OperatorCd { get; set; }
}

/// <summary>テスト・SignalR 未配線時の no-op 実装</summary>
public class NoOpWmsNotifier : IWmsNotifier
{
    public Task NotifyStockChangedAsync(StockChangedEvent evt) => Task.CompletedTask;
    public Task NotifyInboundReceivedAsync(string receiptNo, string warehouseCd) => Task.CompletedTask;
    public Task NotifyOutboundShippedAsync(string outboundNo, string? packageNo) => Task.CompletedTask;
    public Task NotifyStockTakeCompletedAsync(string stockTakeNo, int diffLines) => Task.CompletedTask;
}
