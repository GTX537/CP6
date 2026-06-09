namespace CP6.Core.Services.Integration;

/// <summary>
/// ERP（PA 受注）から MES（製造指図）への前方向フック。
/// </summary>
/// <remarks>
/// 目的：
///   - ERP <c>OrderService.CreateAsync</c>（受注作成）成功時に、対応する受注を
///     MES 製造指図へ自動展開（<c>WorkOrderService.ExpandFromOrderAsync</c>）。
///
/// 設計原則（<see cref="IWmsBridgeHook"/> / <see cref="IErpBridgeHook"/> と対称）：
///   - **Best-Effort**：MES 側のエラーは ERP の親操作（受注作成）を失敗させない
///   - **冪等**：ExpandFromOrderAsync 側で同一手配NOの既存指図を重複チェック（ME-MSG-005）
///   - **設定で無効化可**：appsettings.json の <c>MesBridge:Enabled</c> で
///     <see cref="NoOpMesBridgeHook"/> に切替（既定は無効＝手動展開を既定動作として維持）
///
/// ERP はこのインタフェースのみ依存し、MES の WorkOrderService 内部には触れない（分層保持）。
/// </remarks>
public interface IMesBridgeHook
{
    /// <summary>
    /// ERP 受注作成後の MES 製造指図 自動展開。
    /// WebOrderNo を渡すと、Hook 側で受注明細を解決して指図を展開する。
    /// 既に指図がある／対象明細が無い等は Skipped。
    /// </summary>
    Task<MesBridgeResult> OnOrderCreatedAsync(string webOrderNo, string? userName);
}

/// <summary>
/// MES 展開フック実行結果。
/// </summary>
public class MesBridgeResult
{
    public bool Success { get; init; }
    /// <summary>生成された指図NO（複数明細展開時は先頭、件数は <see cref="CreatedCount"/>）</summary>
    public string? WorkOrderNo { get; init; }
    public int CreatedCount { get; init; }
    public string? Message { get; init; }

    public static MesBridgeResult Ok(IReadOnlyList<string> workOrderNos) =>
        new() { Success = true, WorkOrderNo = workOrderNos.Count > 0 ? workOrderNos[0] : null, CreatedCount = workOrderNos.Count };
    public static MesBridgeResult Skipped(string reason) =>
        new() { Success = false, Message = $"SKIPPED: {reason}" };
    public static MesBridgeResult Failed(string reason) =>
        new() { Success = false, Message = reason };
}

/// <summary>
/// 設定で無効化されている場合の no-op 実装（既定）。
/// </summary>
public class NoOpMesBridgeHook : IMesBridgeHook
{
    public Task<MesBridgeResult> OnOrderCreatedAsync(string webOrderNo, string? userName)
        => Task.FromResult(MesBridgeResult.Skipped("MesBridge:Enabled=false"));
}
