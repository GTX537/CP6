using CP6.Entity.DTOs;

namespace CP6.Core.Services;

/// <summary>
/// 受注取消の反向級联フック（Phase 6）。
/// </summary>
/// <remarks>
/// 役割：ERP <c>OrderService.CancelAsync</c> から呼ばれ、対称 Bridge 模式で MES/WMS に取消を伝播。
///
/// 設計原則（既存 <see cref="IMesBridgeHook"/> / <see cref="IWmsBridgeHook"/> / <see cref="IErpBridgeHook"/> と対称）：
///   - **Best-Effort**：MES/WMS 側のエラーは ERP の親操作（受注取消）を失敗させない
///   - **二段確認サポート**：<c>force=false</c> なら現状を返すのみで DB 未変更
///   - **冪等**：既に Cancelled の WO/Outbound はスキップ
///   - **設定で無効化可**：appsettings.json の <c>OrderCancelBridge:Enabled=false</c> で
///     <see cref="NoOpOrderCancelBridgeHook"/> に切替
///
/// ERP はこのインタフェースのみ依存し、MES の WorkOrderService / WMS の OutboundService 内部には触れない。
/// </remarks>
public interface IOrderCancelBridgeHook
{
    /// <summary>
    /// 受注取消トリガで関連 WorkOrder / OutboundOrder の取消可否を探査・実施。
    /// </summary>
    /// <param name="webOrderNo">取消対象の受注NO</param>
    /// <param name="force">
    /// false=探査モード：DB 未変更で現状返却。
    /// true=実施モード：AutoCancellable=true の全項目を取消（OutboundOrder 先、WorkOrder 後の順）。
    /// </param>
    /// <param name="userName">操作者</param>
    /// <param name="correlationId">端到端 trace 用 — IntegrationEvent 持久化と串聯</param>
    Task<OrderCancelHookResult> OnOrderCancelledAsync(
        string webOrderNo, bool force, string? userName, Guid correlationId);
}

/// <summary>
/// 設定で無効化されている場合の no-op 実装。
/// </summary>
public class NoOpOrderCancelBridgeHook : IOrderCancelBridgeHook
{
    public Task<OrderCancelHookResult> OnOrderCancelledAsync(
        string webOrderNo, bool force, string? userName, Guid correlationId)
        => Task.FromResult(new OrderCancelHookResult
        {
            Success = false,
            FullyCascaded = false,
            Message = "SKIPPED: OrderCancelBridge:Enabled=false",
        });
}
