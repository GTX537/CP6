namespace CP6.Core.Services;

/// <summary>
/// MES / PA から WMS への自動展開フック。
/// </summary>
/// <remarks>
/// 目的：
///   - MES <c>WorkOrderService.IssueAsync</c>（発行）成功時に、対応する材料出庫指示を WMS に自動生成
///   - PA <c>OrderService.CreateAsync</c>（受注作成）成功時に、対応する出荷指示を WMS に自動生成
///
/// 設計原則：
///   - **Best-Effort**：WMS 側のエラーは MES/PA の親操作を失敗させない（try/catch 内で握り潰す）
///   - **冪等**：WMS 側で二重展開チェック（既存 OutboundOrder があれば例外）を信頼してリトライ安全
///   - **設定で無効化可**：appsettings.json の <c>WmsBridge:Enabled=false</c> で <see cref="NoOpWmsBridgeHook"/> に切替
///
/// MES/PA はこのインタフェースのみ依存し、CP6.Core.Services.Wms.IOutboundService には触れない（分層保持）。
/// </remarks>
public interface IWmsBridgeHook
{
    /// <summary>
    /// MES 製造指図発行後の WMS 材料出庫指示自動生成。
    /// 失敗時は例外を握り潰して false を返す。成功時は採番された OutboundNo を返す。
    /// </summary>
    Task<WmsBridgeResult> OnWorkOrderIssuedAsync(string workOrderNo, string? userName);

    /// <summary>
    /// PA 受注作成後の WMS 出荷指示自動生成。
    /// </summary>
    Task<WmsBridgeResult> OnOrderCreatedAsync(string webOrderNo, string? userName);
}

/// <summary>
/// Hook 実行結果。
/// </summary>
public class WmsBridgeResult
{
    public bool Success { get; init; }
    public string? OutboundNo { get; init; }
    public string? Message { get; init; }

    public static WmsBridgeResult Ok(string outboundNo) =>
        new() { Success = true, OutboundNo = outboundNo };
    public static WmsBridgeResult Skipped(string reason) =>
        new() { Success = false, Message = $"SKIPPED: {reason}" };
    public static WmsBridgeResult Failed(string reason) =>
        new() { Success = false, Message = reason };
}

/// <summary>
/// 設定で無効化されている場合の no-op 実装。
/// </summary>
public class NoOpWmsBridgeHook : IWmsBridgeHook
{
    public Task<WmsBridgeResult> OnWorkOrderIssuedAsync(string workOrderNo, string? userName)
        => Task.FromResult(WmsBridgeResult.Skipped("WmsBridge:Enabled=false"));

    public Task<WmsBridgeResult> OnOrderCreatedAsync(string webOrderNo, string? userName)
        => Task.FromResult(WmsBridgeResult.Skipped("WmsBridge:Enabled=false"));
}
