using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Wms;

/// <summary>
/// <see cref="IWmsBridgeHook"/> の標準実装。<see cref="IOutboundService"/> を呼び出す。
/// </summary>
public class WmsBridgeHook : IWmsBridgeHook
{
    private readonly IOutboundService _outbound;
    private readonly IInboundService _inbound;
    private readonly ILogger<WmsBridgeHook> _logger;

    public WmsBridgeHook(IOutboundService outbound, IInboundService inbound, ILogger<WmsBridgeHook> logger)
    {
        _outbound = outbound;
        _inbound = inbound;
        _logger = logger;
    }

    public async Task<WmsBridgeResult> OnWorkOrderIssuedAsync(string workOrderNo, string? userName)
    {
        try
        {
            var no = await _outbound.CreateFromWorkOrderAsync(workOrderNo, userName);
            _logger.LogInformation("[WMS-Bridge] WO {Wo} → 材料出庫指示 {OutboundNo} 自動生成", workOrderNo, no);
            return WmsBridgeResult.Ok(no);
        }
        catch (InvalidOperationException ex)
        {
            // 既存・対象なし等の業務エラー（既に展開済 / 材料 0 件等）
            _logger.LogWarning("[WMS-Bridge] WO {Wo} 自動展開スキップ: {Msg}", workOrderNo, ex.Message);
            return WmsBridgeResult.Skipped(ex.Message);
        }
        catch (Exception ex)
        {
            // 想定外（DB 接続不可等）— 親の Confirm/Issue は失敗させない
            _logger.LogError(ex, "[WMS-Bridge] WO {Wo} 自動展開で予期せぬエラー", workOrderNo);
            return WmsBridgeResult.Failed(ex.Message);
        }
    }

    public async Task<WmsBridgeResult> OnOrderCreatedAsync(string webOrderNo, string? userName)
    {
        try
        {
            var no = await _outbound.CreateFromOrderAsync(webOrderNo, userName);
            _logger.LogInformation("[WMS-Bridge] 受注 {Order} → 出荷指示 {OutboundNo} 自動生成", webOrderNo, no);
            return WmsBridgeResult.Ok(no);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[WMS-Bridge] 受注 {Order} 自動展開スキップ: {Msg}", webOrderNo, ex.Message);
            return WmsBridgeResult.Skipped(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WMS-Bridge] 受注 {Order} 自動展開で予期せぬエラー", webOrderNo);
            return WmsBridgeResult.Failed(ex.Message);
        }
    }

    public async Task<WmsBridgeResult> OnProductionCompletedAsync(string workOrderNo, decimal goodQty, string? userName)
    {
        try
        {
            var no = await _inbound.CreateFinishedGoodsFromWorkOrderAsync(workOrderNo, goodQty, userName);
            _logger.LogInformation("[WMS-Bridge] 指図 {Wo} 完了 → 完成品入庫 {ReceiptNo} 自動生成（良品 {Qty}）", workOrderNo, no, goodQty);
            return WmsBridgeResult.Ok(no);
        }
        catch (InvalidOperationException ex)
        {
            // 既に入庫済 / 良品 0 等の業務エラー
            _logger.LogWarning("[WMS-Bridge] 指図 {Wo} 完成品入庫スキップ: {Msg}", workOrderNo, ex.Message);
            return WmsBridgeResult.Skipped(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WMS-Bridge] 指図 {Wo} 完成品入庫で予期せぬエラー", workOrderNo);
            return WmsBridgeResult.Failed(ex.Message);
        }
    }
}
