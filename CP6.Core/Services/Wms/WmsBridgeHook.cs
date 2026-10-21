using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
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

    public Task<WmsBridgeResult> OnWorkOrderIssuedAsync(string workOrderNo,string? userName)=>OnWorkOrderIssuedCore(workOrderNo,userName,null);
    internal Task<WmsBridgeResult> OnWorkOrderIssuedOwnedAsync(OperationsDelegationV1 ticket)=>OnWorkOrderIssuedCore(ticket.Number,ticket.Actor,ticket);
    private async Task<WmsBridgeResult> OnWorkOrderIssuedCore(string workOrderNo,string? userName,OperationsDelegationV1? ticket)
    {
        var corrId = Guid.NewGuid();
        var payload = new { workOrderNo, userName };
        try
        {
            var no = ticket==null?await _outbound.CreateFromWorkOrderAsync(workOrderNo,userName)
                :await (_outbound as OutboundService ?? throw new InvalidOperationException("DELEGATION_OWNER_UNAVAILABLE")).CreateDelegatedAsync(ticket);
            Logger.LogInformation("[WMS-Bridge] WO {Wo} → 材料出庫指示 {OutboundNo} 自動生成", workOrderNo, no);
            await PersistEventAsync("MES", "WMS", nameof(OnWorkOrderIssuedAsync),
                workOrderNo, no, IntegrationEventStatus.Success, null, corrId, payload, userName);
            return WmsBridgeResult.Ok(no);
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning("[WMS-Bridge] WO {Wo} 自動展開スキップ: {Msg}", workOrderNo, ex.Message);
            await PersistEventAsync("MES", "WMS", nameof(OnWorkOrderIssuedAsync),
                workOrderNo, null, IntegrationEventStatus.Skipped, ex.Message, corrId, payload, userName);
            return WmsBridgeResult.Skipped(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[WMS-Bridge] WO {Wo} 自動展開で予期せぬエラー", workOrderNo);
            await PersistEventAsync("MES", "WMS", nameof(OnWorkOrderIssuedAsync),
                workOrderNo, null, IntegrationEventStatus.Failed, ex.ToString(), corrId, payload, userName);
            return WmsBridgeResult.Failed(ex.Message);
        }
    }

    public Task<WmsBridgeResult> OnOrderCreatedAsync(string webOrderNo,string? userName)=>OnOrderCreatedCore(webOrderNo,userName,null);
    internal Task<WmsBridgeResult> OnOrderCreatedOwnedAsync(OperationsDelegationV1 ticket)=>OnOrderCreatedCore(ticket.Number,ticket.Actor,ticket);
    private async Task<WmsBridgeResult> OnOrderCreatedCore(string webOrderNo,string? userName,OperationsDelegationV1? ticket)
    {
        var corrId = Guid.NewGuid();
        var payload = new { webOrderNo, userName };
        try
        {
            var no = ticket==null?await _outbound.CreateFromOrderAsync(webOrderNo,userName)
                :await (_outbound as OutboundService ?? throw new InvalidOperationException("DELEGATION_OWNER_UNAVAILABLE")).CreateDelegatedAsync(ticket);
            Logger.LogInformation("[WMS-Bridge] 受注 {Order} → 出荷指示 {OutboundNo} 自動生成", webOrderNo, no);
            await PersistEventAsync("ERP", "WMS", nameof(OnOrderCreatedAsync),
                webOrderNo, no, IntegrationEventStatus.Success, null, corrId, payload, userName);
            return WmsBridgeResult.Ok(no);
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning("[WMS-Bridge] 受注 {Order} 自動展開スキップ: {Msg}", webOrderNo, ex.Message);
            await PersistEventAsync("ERP", "WMS", nameof(OnOrderCreatedAsync),
                webOrderNo, null, IntegrationEventStatus.Skipped, ex.Message, corrId, payload, userName);
            return WmsBridgeResult.Skipped(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[WMS-Bridge] 受注 {Order} 自動展開で予期せぬエラー", webOrderNo);
            await PersistEventAsync("ERP", "WMS", nameof(OnOrderCreatedAsync),
                webOrderNo, null, IntegrationEventStatus.Failed, ex.ToString(), corrId, payload, userName);
            return WmsBridgeResult.Failed(ex.Message);
        }
    }

    public Task<WmsBridgeResult> OnProductionCompletedAsync(string workOrderNo, decimal goodQty, string? userName)
        =>OnProductionCompletedCore(workOrderNo,goodQty,userName,null);
    internal Task<WmsBridgeResult> OnProductionCompletedOwnedAsync(OperationsDelegationV1 ticket)
        =>OnProductionCompletedCore(ticket.Number,ticket.CompletedQuantity??0,ticket.Actor,ticket);
    private async Task<WmsBridgeResult> OnProductionCompletedCore(string workOrderNo,decimal goodQty,string? userName,OperationsDelegationV1? ticket)
    {
        var corrId = Guid.NewGuid();
        var payload = new { workOrderNo, goodQty, userName };
        try
        {
            if(ticket!=null && !ReferenceEquals(Db,ticket.Context))throw new InvalidOperationException("DELEGATION_OWNER_UNAVAILABLE");
            var no = ticket==null ? await _inbound.CreateFinishedGoodsFromWorkOrderAsync(workOrderNo, goodQty, userName)
                : await (_inbound as InboundService ?? throw new InvalidOperationException("DELEGATION_OWNER_UNAVAILABLE")).ReceiveCompletedAsync(ticket);
            Logger.LogInformation("[WMS-Bridge] 指図 {Wo} 完了 → 完成品入庫 {ReceiptNo} 自動生成（良品 {Qty}）", workOrderNo, no, goodQty);
            await PersistEventAsync("MES", "WMS", nameof(OnProductionCompletedAsync),
                workOrderNo, no, IntegrationEventStatus.Success, null, corrId, payload, userName);
            return WmsBridgeResult.Ok(no);
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning("[WMS-Bridge] 指図 {Wo} 完成品入庫スキップ: {Msg}", workOrderNo, ex.Message);
            await PersistEventAsync("MES", "WMS", nameof(OnProductionCompletedAsync),
                workOrderNo, null, IntegrationEventStatus.Skipped, ex.Message, corrId, payload, userName);
            return WmsBridgeResult.Skipped(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[WMS-Bridge] 指図 {Wo} 完成品入庫で予期せぬエラー", workOrderNo);
            await PersistEventAsync("MES", "WMS", nameof(OnProductionCompletedAsync),
                workOrderNo, null, IntegrationEventStatus.Failed, ex.ToString(), corrId, payload, userName);
            return WmsBridgeResult.Failed(ex.Message);
        }
    }
}
