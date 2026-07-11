using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Fin;

/// <summary>
/// <see cref="IFinBridgeHook"/> 标准实现（BridgeHookBase，Phase6 持久化）。出货确认→AR 自动开票，
/// 出货取消→红冲。Best-Effort：异常握住并落 IntegrationEvent（失败可重试），不阻断出货主操作。
/// </summary>
public class FinBridgeHook : BridgeHookBase, IFinBridgeHook
{
    private readonly IArInvoiceService _ar;
    private readonly ICostCollectService _cost;
    private readonly ICostSettleService _settle;

    public FinBridgeHook(CP6Context db, IArInvoiceService ar, ICostCollectService cost, ICostSettleService settle, ILogger<FinBridgeHook> logger)
        : base(db, logger)
    {
        _ar = ar;
        _cost = cost;
        _settle = settle;
    }

    public async Task<FinBridgeResult> OnShipmentConfirmedAsync(FinShipmentInvoiceRequest request, string? userName)
    {
        var corrId = Guid.NewGuid();
        try
        {
            var (r, _, no) = await _ar.CreateFromShipmentAsync(request, userName ?? "system");
            if (!r.Ok)
            {
                await PersistEventAsync("WMS", "FIN", nameof(OnShipmentConfirmedAsync),
                    request.ShipmentId, null, IntegrationEventStatus.Failed, r.Code, corrId, request);
                return FinBridgeResult.Failed(r.Code ?? "fail");
            }
            Logger.LogInformation("[FIN-Bridge] 出货 {Ship} → AR 发票 {No} 自动开票", request.ShipmentId, no);
            await PersistEventAsync("WMS", "FIN", nameof(OnShipmentConfirmedAsync),
                request.ShipmentId, no, IntegrationEventStatus.Success, null, corrId, request);
            return FinBridgeResult.Ok(no);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[FIN-Bridge] 出货 {Ship} 自动开票异常", request.ShipmentId);
            await PersistEventAsync("WMS", "FIN", nameof(OnShipmentConfirmedAsync),
                request.ShipmentId, null, IntegrationEventStatus.Failed, ex.ToString(), corrId, request);
            return FinBridgeResult.Failed(ex.Message);
        }
    }

    public async Task<FinBridgeResult> OnShipmentCancelledAsync(string shipmentId, string? userName)
    {
        var corrId = Guid.NewGuid();
        var payload = new { shipmentId, userName };
        try
        {
            var inv = await Db.ArInvoices.FirstOrDefaultAsync(x => x.ShipmentId == shipmentId && !x.IsCreditMemo);
            if (inv == null)
            {
                await PersistEventAsync("WMS", "FIN", nameof(OnShipmentCancelledAsync),
                    shipmentId, null, IntegrationEventStatus.Skipped, "no invoice for shipment", corrId, payload);
                return FinBridgeResult.Skipped("no invoice for shipment");
            }
            var r = await _ar.ReverseAsync(inv.Id, userName ?? "system", "出货取消");
            var status = r.Ok ? IntegrationEventStatus.Success : IntegrationEventStatus.Failed;
            await PersistEventAsync("WMS", "FIN", nameof(OnShipmentCancelledAsync),
                shipmentId, inv.No, status, r.Ok ? null : r.Code, corrId, payload);
            return r.Ok ? FinBridgeResult.Ok(inv.No) : FinBridgeResult.Failed(r.Code ?? "fail");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[FIN-Bridge] 出货 {Ship} 取消红冲异常", shipmentId);
            await PersistEventAsync("WMS", "FIN", nameof(OnShipmentCancelledAsync),
                shipmentId, null, IntegrationEventStatus.Failed, ex.ToString(), corrId, payload);
            return FinBridgeResult.Failed(ex.Message);
        }
    }

    public async Task<FinBridgeResult> OnWorkOrderCompletedAsync(string workOrderNo, string? userName)
    {
        var corrId = Guid.NewGuid();
        var user = userName ?? "system";
        try
        {
            // ① 成本归集：工费留 0（自动归集只吃料真实反冲消耗，工费/制费由财务补录标准估算后再结转）。
            var collect = await _cost.CollectAsync(workOrderNo, 0m, 0m, user);
            if (!collect.Ok)
            {
                // E-FIN-402 = 成本单已结转（重复完工事件）→ 幂等跳过，不重复归集/结转。
                if (collect.Code == "E-FIN-402")
                {
                    await PersistEventAsync("MES", "FIN", nameof(OnWorkOrderCompletedAsync),
                        workOrderNo, null, IntegrationEventStatus.Skipped, collect.Code, corrId,
                        new { workOrderNo, userName, step = "collect", note = "already settled" });
                    return FinBridgeResult.Skipped("cost already settled");
                }
                await PersistEventAsync("MES", "FIN", nameof(OnWorkOrderCompletedAsync),
                    workOrderNo, null, IntegrationEventStatus.Failed, collect.Code, corrId,
                    new { workOrderNo, userName, step = "collect" });
                return FinBridgeResult.Failed(collect.Code ?? "collect fail");
            }

            // ② 完工结转：料工费→WIP + WIP→FG 两凭证（借贷/科目照 CostSettle 既有实现）。归集成功才结转。
            var settle = await _settle.SettleAsync(workOrderNo, user);
            var status = settle.Ok ? IntegrationEventStatus.Success : IntegrationEventStatus.Failed;
            await PersistEventAsync("MES", "FIN", nameof(OnWorkOrderCompletedAsync),
                workOrderNo, null, status, settle.Ok ? null : settle.Code, corrId,
                new { workOrderNo, userName, step = settle.Ok ? "settled" : "settle" });
            if (settle.Ok)
                Logger.LogInformation("[FIN-Bridge] 工单 {Wo} 完工 → 成本归集+结转（料真实消耗→WIP→FG）", workOrderNo);
            else
                Logger.LogWarning("[FIN-Bridge] 工单 {Wo} 成本已归集但结转失败 {Code}（成本单停在 Collected 待重试）", workOrderNo, settle.Code);
            return settle.Ok ? FinBridgeResult.Ok(workOrderNo) : FinBridgeResult.Failed(settle.Code ?? "settle fail");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[FIN-Bridge] 工单 {Wo} 成本归集/结转异常", workOrderNo);
            await PersistEventAsync("MES", "FIN", nameof(OnWorkOrderCompletedAsync),
                workOrderNo, null, IntegrationEventStatus.Failed, ex.ToString(), corrId, new { workOrderNo, userName });
            return FinBridgeResult.Failed(ex.Message);
        }
    }
}
