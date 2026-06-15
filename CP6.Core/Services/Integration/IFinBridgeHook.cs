using CP6.Core.Services.Fin;

namespace CP6.Core.Services.Integration;

/// <summary>
/// WMS 出货 → 财务（Fin）的自动开票/红冲钩子（章05 §5 / F2-D4）。
/// 出货确认→AR 自动开票（收入+成本双凭证），出货取消→红冲两凭证。异步、复用 Phase6 持久化/重试基建，
/// 与 IWmsBridgeHook 对称。出货链上游（OutboundService）据出货/订单构造 <see cref="FinShipmentInvoiceRequest"/>。
/// </summary>
public interface IFinBridgeHook
{
    /// <summary>出货确认后自动开应收发票并过账（幂等 ShipmentId）。</summary>
    Task<FinBridgeResult> OnShipmentConfirmedAsync(FinShipmentInvoiceRequest request, string? userName);

    /// <summary>出货取消后红冲对应应收发票的收入+成本凭证。</summary>
    Task<FinBridgeResult> OnShipmentCancelledAsync(string shipmentId, string? userName);

    /// <summary>MES 工单完工后自动归集成本（料吃真实消耗，工费留 0 待财务补录后结转，章06 A-3）。幂等。</summary>
    Task<FinBridgeResult> OnWorkOrderCompletedAsync(string workOrderNo, string? userName);
}

/// <summary>Fin 钩子执行结果（对称 WmsBridgeResult）。</summary>
public class FinBridgeResult
{
    public bool Success { get; init; }
    public string? InvoiceNo { get; init; }
    public string? Message { get; init; }

    public static FinBridgeResult Ok(string? invoiceNo) => new() { Success = true, InvoiceNo = invoiceNo };
    public static FinBridgeResult Skipped(string reason) => new() { Success = false, Message = $"SKIPPED: {reason}" };
    public static FinBridgeResult Failed(string reason) => new() { Success = false, Message = reason };
}

/// <summary>配置 FinBridge:Enabled=false 时的 no-op 实现。</summary>
public class NoOpFinBridgeHook : IFinBridgeHook
{
    public Task<FinBridgeResult> OnShipmentConfirmedAsync(FinShipmentInvoiceRequest request, string? userName)
        => Task.FromResult(FinBridgeResult.Skipped("FinBridge:Enabled=false"));

    public Task<FinBridgeResult> OnShipmentCancelledAsync(string shipmentId, string? userName)
        => Task.FromResult(FinBridgeResult.Skipped("FinBridge:Enabled=false"));

    public Task<FinBridgeResult> OnWorkOrderCompletedAsync(string workOrderNo, string? userName)
        => Task.FromResult(FinBridgeResult.Skipped("FinBridge:Enabled=false"));
}
