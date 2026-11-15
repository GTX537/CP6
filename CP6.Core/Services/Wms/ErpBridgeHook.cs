using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Wms;

/// <summary>
/// <see cref="IErpBridgeHook"/> の標準実装。WMS 出荷確定 → ERP 受注 出荷実績回写。
/// </summary>
/// <remarks>
/// Phase 6: <see cref="BridgeHookBase"/> 継承で IntegrationEvent 持久化を追加。
/// 出庫指示NO から OutboundOrder/明細・対応する受注を解決し、
/// 受注明細の累計出荷数・出荷ステータス・最終出荷日を更新、受注ヘッダにロールアップする。
/// 出庫明細↔受注明細の対応は WebOrderNo + ProductCd で照合（同一製品が複数明細にある場合は
/// 未充足の明細へ順次充当）。
/// </remarks>
public partial class ErpBridgeHook : BridgeHookBase, IErpBridgeHook
{
    public ErpBridgeHook(CP6Context db, ILogger<ErpBridgeHook> logger)
        : base(db, logger)
    {
    }

    public async Task<ErpBridgeResult> OnShipmentConfirmedAsync(string outboundNo, string? userName)
    {
        var corrId = Guid.NewGuid();
        var payload = new { outboundNo, userName };
        try
        {
            var result = await ProjectShipmentAsync(outboundNo, userName, null, CancellationToken.None);
            if (result.Status == IntegrationEventStatus.Success)
            {
                await Db.SaveChangesAsync();
                Logger.LogInformation("[ERP-Bridge] 出庫 {Outbound} → 受注 {Order} 出荷実績回写（ShipStatus={Status}）",
                    outboundNo, result.OrderNo, result.ShipStatus);
            }

            await PersistEventAsync("WMS", "ERP", nameof(OnShipmentConfirmedAsync),
                outboundNo, result.OrderNo, result.Status, result.Error, corrId, payload, userName);
            return result.Status == IntegrationEventStatus.Success
                ? ErpBridgeResult.Ok(result.OrderNo!)
                : ErpBridgeResult.Skipped(result.Error!);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[ERP-Bridge] 出庫 {Outbound} の受注回写で予期せぬエラー", outboundNo);
            await PersistEventAsync("WMS", "ERP", nameof(OnShipmentConfirmedAsync),
                outboundNo, null, IntegrationEventStatus.Failed, ex.ToString(), corrId, payload, userName);
            return ErpBridgeResult.Failed(ex.Message);
        }
    }

    private async Task<ShipmentProjectionResult> ProjectShipmentAsync(string outboundNo, string? userName,
        ShipmentProjectionPayload? original, CancellationToken ct)
    {
        var outbound = await Db.OutboundOrders.AsNoTracking()
            .FirstOrDefaultAsync(row => row.OutboundNo == outboundNo && !row.IsDeleted, ct);
        if (outbound is null)
            return ShipmentProjectionResult.Skipped($"出庫指示 [{outboundNo}] が見つかりません");
        if (outbound.OutboundType != OutboundType.Shipping || string.IsNullOrWhiteSpace(outbound.WebOrderNo))
            return ShipmentProjectionResult.Skipped("出荷区分かつ受注紐付きの出庫ではありません");

        var orderNo = outbound.WebOrderNo;
        if (original is not null && (outbound.Status != OutboundOrderStatus.Completed
            || (original.Version == 1 && (original.OutboundId != outbound.Id || original.OrderNo != orderNo))))
            return ShipmentProjectionResult.Failed("DISPATCH-400");

        var shipped = await Db.OutboundOrderDetails.AsNoTracking()
            .Where(row => row.OutboundNo == outboundNo && !row.IsDeleted && row.ShippedQty > 0)
            .OrderBy(row => row.LineNo).ToListAsync(ct);
        if (shipped.Count == 0)
            return ShipmentProjectionResult.Skipped("出荷数のある明細がありません");

        var order = await Db.Orders.FirstOrDefaultAsync(row => row.WebOrderNo == orderNo && !row.IsDeleted, ct);
        if (order is null)
            return ShipmentProjectionResult.Skipped($"受注 [{orderNo}] が見つかりません");

        var lines = await Db.OrderDetails.Where(row => row.WebOrderNo == orderNo && !row.IsDeleted)
            .OrderBy(row => row.WebOrderDetailNo).ToListAsync(ct);
        if (original is { Version: null })
        {
            // Legacy shipment projection updated the order and its lines in one SaveChanges.
            if (lines.Any(line => line.LastOutboundNo == outboundNo)
                || await Db.IntegrationEvents.AsNoTracking().AnyAsync(evt => evt.SourceModule == "WMS"
                    && evt.TargetModule == "ERP" && evt.HookName == nameof(OnShipmentConfirmedAsync)
                    && evt.SourceNo == outboundNo && evt.TargetNo == orderNo && evt.Status == IntegrationEventStatus.Success, ct))
                return new(IntegrationEventStatus.Success, orderNo, null, order.ShipStatus);

            // An equal or later write may have superseded the original line markers.
            if (order.ActualShipDate is DateTime last
                && (outbound.ModifyDate is not DateTime confirmation || last >= confirmation))
                return ShipmentProjectionResult.Failed("ERP_SHIPMENT_OUTCOME_UNKNOWN");
        }

        var appliedAt = original is null ? DateTime.Now : outbound.ModifyDate ?? DateTime.Now;

        // Keep the order bridge allocation policy for multiple lines sharing a product.
        foreach (var group in shipped.GroupBy(row => row.ProductCd))
        {
            var remaining = group.Sum(row => row.ShippedQty);
            foreach (var line in lines.Where(row => row.ProductCd == group.Key))
            {
                if (remaining <= 0) break;
                var ordered = line.Quantity ?? 0m;
                var already = line.ShippedQty ?? 0m;
                var capacity = ordered > 0 ? ordered - already : remaining;
                var applied = capacity > 0 ? Math.Min(remaining, capacity) : remaining;
                if (applied <= 0) continue;

                line.ShippedQty = already + applied;
                line.ShipStatus = (ordered > 0 && line.ShippedQty >= ordered) ? 9 : 5;
                line.LastShipDate = appliedAt;
                line.LastOutboundNo = outboundNo;
                line.Modifier = userName;
                line.ModifyDate = appliedAt;
                remaining -= applied;
            }
        }

        var allShipped = lines.All(line => line.ShipStatus == 9);
        var anyShipped = lines.Any(line => line.ShipStatus >= 5);
        order.ShipStatus = allShipped ? 9 : (anyShipped ? 5 : order.ShipStatus);
        order.ActualShipDate = appliedAt;
        order.Modifier = userName;
        order.ModifyDate = appliedAt;
        return new(IntegrationEventStatus.Success, orderNo, null, order.ShipStatus);
    }

    public async Task<ErpBridgeResult> OnReturnConfirmedAsync(string rmaNo, string? userName)
    {
        var corrId = Guid.NewGuid();
        var payload = new { rmaNo, userName };
        try
        {
            var rma = await Db.RmaHeaders.AsNoTracking()
                .FirstOrDefaultAsync(x => x.RmaNo == rmaNo && !x.IsDeleted);
            if (rma == null)
            {
                const string msg = "WM-MSG-RMA-404";
                await PersistEventAsync("WMS", "ERP", nameof(OnReturnConfirmedAsync),
                    rmaNo, null, IntegrationEventStatus.Skipped, msg, corrId, payload, userName);
                return ErpBridgeResult.Skipped(msg);
            }

            var details = await Db.RmaDetails.AsNoTracking()
                .Where(d => d.RmaNo == rmaNo && !d.IsDeleted)
                .OrderBy(d => d.LineNo)
                .ToListAsync();
            if (details.Count == 0)
                throw new InvalidOperationException("WM-MSG-020");

            var webOrderNo = await ResolveWebOrderNoAsync(rma);
            var now = DateTime.Now;
            var firstCreditNoteNo = string.Empty;

            foreach (var detail in details)
            {
                OrderDetail? orderDetail = null;
                if (!string.IsNullOrWhiteSpace(webOrderNo))
                {
                    orderDetail = await Db.OrderDetails
                        .Where(d => d.WebOrderNo == webOrderNo && d.ProductCd == detail.ProductCd && !d.IsDeleted)
                        .OrderBy(d => d.WebOrderDetailNo)
                        .FirstOrDefaultAsync();
                }

                if (orderDetail == null)
                {
                    Logger.LogWarning(
                        "[ERP-Bridge] RMA {RmaNo} OrderDetail not found for WebOrderNo={WebOrderNo}, ProductCd={ProductCd}, LotNo={LotNo}",
                        rmaNo, webOrderNo, detail.ProductCd, detail.LotNo);
                }
                else
                {
                    orderDetail.ReturnedQty = (orderDetail.ReturnedQty ?? 0m) + detail.Qty;
                    orderDetail.Modifier = userName;
                    orderDetail.ModifyDate = now;
                }

                var creditNoteNo = NewCreditNoteNo();
                if (string.IsNullOrEmpty(firstCreditNoteNo))
                    firstCreditNoteNo = creditNoteNo;

                Db.CreditNotes.Add(new CreditNote
                {
                    CreditNoteNo = creditNoteNo,
                    WebOrderNo = webOrderNo,
                    RmaNo = rmaNo,
                    Type = CreditNoteType.Refund,
                    CustomerCd = rma.CustomerCd,
                    ProductCd = detail.ProductCd,
                    LotNo = detail.LotNo,
                    Qty = detail.Qty,
                    Reason = rma.ReturnReason,
                    IssueDate = DateTime.Today,
                    Creator = userName,
                    CreateDate = now,
                });
            }

            await Db.SaveChangesAsync();

            Logger.LogInformation(
                "[ERP-Bridge] RMA {RmaNo} -> {Count} credit notes generated, returned qty back-written",
                rmaNo, details.Count);
            await PersistEventAsync("WMS", "ERP", nameof(OnReturnConfirmedAsync),
                rmaNo, string.IsNullOrEmpty(firstCreditNoteNo) ? null : firstCreditNoteNo,
                IntegrationEventStatus.Success, null, corrId, payload, userName);
            return ErpBridgeResult.Ok(rmaNo);
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning("[ERP-Bridge] RMA {RmaNo} credit-note bridge skipped: {Msg}", rmaNo, ex.Message);
            await PersistEventAsync("WMS", "ERP", nameof(OnReturnConfirmedAsync),
                rmaNo, null, IntegrationEventStatus.Skipped, ex.Message, corrId, payload, userName);
            return ErpBridgeResult.Skipped(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[ERP-Bridge] RMA {RmaNo} credit-note bridge failed", rmaNo);
            await PersistEventAsync("WMS", "ERP", nameof(OnReturnConfirmedAsync),
                rmaNo, null, IntegrationEventStatus.Failed, ex.ToString(), corrId, payload, userName);
            return ErpBridgeResult.Failed(ex.Message);
        }
    }

    private async Task<string?> ResolveWebOrderNoAsync(RmaHeader rma)
    {
        if (string.IsNullOrWhiteSpace(rma.OriginalShippingNo))
            return null;

        var outbound = await Db.OutboundOrders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OutboundNo == rma.OriginalShippingNo && !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(outbound?.WebOrderNo))
            return outbound.WebOrderNo;

        return rma.OriginalShippingNo.StartsWith("WEB", StringComparison.OrdinalIgnoreCase)
            ? rma.OriginalShippingNo
            : null;
    }

    private static string NewCreditNoteNo()
        => $"CN{DateTime.Today:yyyyMMdd}-{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}";
}
