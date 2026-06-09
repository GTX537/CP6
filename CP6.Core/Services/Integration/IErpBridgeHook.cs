namespace CP6.Core.Services.Integration;

/// <summary>
/// WMS から ERP（PA 受注）への逆方向フック。
/// </summary>
/// <remarks>
/// 目的：
///   - WMS <c>OutboundService.ShipAsync</c>（出荷確定）成功時に、対応する ERP 受注へ出荷実績を回写
///     （受注ヘッダ／明細の出荷ステータス・累計出荷数・実出荷日を更新）。
///
/// 設計原則（<see cref="IWmsBridgeHook"/> と対称）：
///   - **Best-Effort**：ERP 側のエラーは WMS の親操作（出荷確定）を失敗させない
///   - **冪等**：明細の累計出荷数で管理し、同一出庫の二重反映を防止
///   - **設定で無効化可**：appsettings.json の <c>ErpBridge:Enabled=false</c> で <see cref="NoOpErpBridgeHook"/> に切替
///
/// WMS はこのインタフェースのみ依存し、ERP の OrderService 内部には触れない（分層保持）。
/// </remarks>
public interface IErpBridgeHook
{
    /// <summary>
    /// WMS 出荷確定後の ERP 受注 出荷実績回写。
    /// 出庫指示NO を渡すと、Hook 側で出庫明細・受注を解決して回写する。
    /// 出荷区分（OutboundType=Shipping）かつ WebOrderNo 紐付きでない場合は Skipped。
    /// </summary>
    Task<ErpBridgeResult> OnShipmentConfirmedAsync(string outboundNo, string? userName);

    /// <summary>
    /// WMS RMA confirmed -> ERP CreditNote back-write.
    /// </summary>
    Task<ErpBridgeResult> OnReturnConfirmedAsync(string rmaNo, string? userName);
}

/// <summary>
/// ERP 回写フック実行結果。
/// </summary>
public class ErpBridgeResult
{
    public bool Success { get; init; }
    public string? WebOrderNo { get; init; }
    public string? Message { get; init; }

    public static ErpBridgeResult Ok(string webOrderNo) =>
        new() { Success = true, WebOrderNo = webOrderNo };
    public static ErpBridgeResult Skipped(string reason) =>
        new() { Success = false, Message = $"SKIPPED: {reason}" };
    public static ErpBridgeResult Failed(string reason) =>
        new() { Success = false, Message = reason };
}

/// <summary>
/// 設定で無効化されている場合の no-op 実装。
/// </summary>
public class NoOpErpBridgeHook : IErpBridgeHook
{
    public Task<ErpBridgeResult> OnShipmentConfirmedAsync(string outboundNo, string? userName)
        => Task.FromResult(ErpBridgeResult.Skipped("ErpBridge:Enabled=false"));

    public Task<ErpBridgeResult> OnReturnConfirmedAsync(string rmaNo, string? userName)
        => Task.FromResult(ErpBridgeResult.Skipped("ErpBridge:Enabled=false"));
}
