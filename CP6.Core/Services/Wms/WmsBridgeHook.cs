using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Wms;

/// <summary>
/// <see cref="IWmsBridgeHook"/> の標準実装。<see cref="IOutboundService"/> を呼び出す。
/// </summary>
/// <remarks>
/// Phase 6: <see cref="BridgeHookBase"/> 継承で IntegrationEvent 持久化を追加。
/// 外部インタフェース・既存業務ロジックは変更なし。
/// </remarks>
public class WmsBridgeHook : BridgeHookBase, IWmsBridgeHook
{
    private readonly IOutboundService _outbound;
    private readonly IInboundService _inbound;

    public WmsBridgeHook(
        CP6Context db,
        IOutboundService outbound,
        IInboundService inbound,
        ILogger<WmsBridgeHook> logger)
        : base(db, logger)
    {
        _outbound = outbound;
        _inbound = inbound;
    }

    public async Task<WmsBridgeResult> OnWorkOrderIssuedAsync(string workOrderNo, string? userName)
    {
        var corrId = Guid.NewGuid();
        var payload = new { workOrderNo, userName };
        try
        {
            var no = await _outbound.CreateFromWorkOrderAsync(workOrderNo, userName);
            Logger.LogInformation("[WMS-Bridge] WO {Wo} → 材料出庫指示 {OutboundNo} 自動生成", workOrderNo, no);
            await PersistEventAsync("MES", "WMS", nameof(OnWorkOrderIssuedAsync),
                workOrderNo, no, IntegrationEventStatus.Success, null, corrId, payload);
            return WmsBridgeResult.Ok(no);
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning("[WMS-Bridge] WO {Wo} 自動展開スキップ: {Msg}", workOrderNo, ex.Message);
            await PersistEventAsync("MES", "WMS", nameof(OnWorkOrderIssuedAsync),
                workOrderNo, null, IntegrationEventStatus.Skipped, ex.Message, corrId, payload);
            return WmsBridgeResult.Skipped(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[WMS-Bridge] WO {Wo} 自動展開で予期せぬエラー", workOrderNo);
            await PersistEventAsync("MES", "WMS", nameof(OnWorkOrderIssuedAsync),
                workOrderNo, null, IntegrationEventStatus.Failed, ex.ToString(), corrId, payload);
            return WmsBridgeResult.Failed(ex.Message);
        }
    }

    public async Task<WmsBridgeResult> OnOrderCreatedAsync(string webOrderNo, string? userName)
    {
        var corrId = Guid.NewGuid();
        var payload = new { webOrderNo, userName };
        try
        {
            var no = await _outbound.CreateFromOrderAsync(webOrderNo, userName);
            Logger.LogInformation("[WMS-Bridge] 受注 {Order} → 出荷指示 {OutboundNo} 自動生成", webOrderNo, no);
            await PersistEventAsync("ERP", "WMS", nameof(OnOrderCreatedAsync),
                webOrderNo, no, IntegrationEventStatus.Success, null, corrId, payload);
            return WmsBridgeResult.Ok(no);
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning("[WMS-Bridge] 受注 {Order} 自動展開スキップ: {Msg}", webOrderNo, ex.Message);
            await PersistEventAsync("ERP", "WMS", nameof(OnOrderCreatedAsync),
                webOrderNo, null, IntegrationEventStatus.Skipped, ex.Message, corrId, payload);
            return WmsBridgeResult.Skipped(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[WMS-Bridge] 受注 {Order} 自動展開で予期せぬエラー", webOrderNo);
            await PersistEventAsync("ERP", "WMS", nameof(OnOrderCreatedAsync),
                webOrderNo, null, IntegrationEventStatus.Failed, ex.ToString(), corrId, payload);
            return WmsBridgeResult.Failed(ex.Message);
        }
    }

    public async Task<WmsBridgeResult> OnProductionCompletedAsync(string workOrderNo, decimal goodQty, string? userName)
    {
        var corrId = Guid.NewGuid();
        var payload = new { workOrderNo, goodQty, userName };
        try
        {
            var no = await _inbound.CreateFinishedGoodsFromWorkOrderAsync(workOrderNo, goodQty, userName);
            Logger.LogInformation("[WMS-Bridge] 指図 {Wo} 完了 → 完成品入庫 {ReceiptNo} 自動生成（良品 {Qty}）", workOrderNo, no, goodQty);
            await PersistEventAsync("MES", "WMS", nameof(OnProductionCompletedAsync),
                workOrderNo, no, IntegrationEventStatus.Success, null, corrId, payload);
            return WmsBridgeResult.Ok(no);
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning("[WMS-Bridge] 指図 {Wo} 完成品入庫スキップ: {Msg}", workOrderNo, ex.Message);
            await PersistEventAsync("MES", "WMS", nameof(OnProductionCompletedAsync),
                workOrderNo, null, IntegrationEventStatus.Skipped, ex.Message, corrId, payload);
            return WmsBridgeResult.Skipped(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[WMS-Bridge] 指図 {Wo} 完成品入庫で予期せぬエラー", workOrderNo);
            await PersistEventAsync("MES", "WMS", nameof(OnProductionCompletedAsync),
                workOrderNo, null, IntegrationEventStatus.Failed, ex.ToString(), corrId, payload);
            return WmsBridgeResult.Failed(ex.Message);
        }
    }
}
