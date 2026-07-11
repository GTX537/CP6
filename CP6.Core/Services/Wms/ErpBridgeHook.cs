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
public class ErpBridgeHook : BridgeHookBase, IErpBridgeHook
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
            var ob = await Db.OutboundOrders.AsNoTracking()
                .FirstOrDefaultAsync(x => x.OutboundNo == outboundNo && !x.IsDeleted);
            if (ob == null)
            {
                var msg = $"出庫指示 [{outboundNo}] が見つかりません";
                await PersistEventAsync("WMS", "ERP", nameof(OnShipmentConfirmedAsync),
                    outboundNo, null, IntegrationEventStatus.Skipped, msg, corrId, payload, userName);
                return ErpBridgeResult.Skipped(msg);
            }

            // 出荷区分かつ受注紐付きのみ回写対象（材料出庫等は対象外）
            if (ob.OutboundType != OutboundType.Shipping || string.IsNullOrWhiteSpace(ob.WebOrderNo))
            {
                var msg = "出荷区分かつ受注紐付きの出庫ではありません";
                await PersistEventAsync("WMS", "ERP", nameof(OnShipmentConfirmedAsync),
                    outboundNo, null, IntegrationEventStatus.Skipped, msg, corrId, payload, userName);
                return ErpBridgeResult.Skipped(msg);
            }

            var webOrderNo = ob.WebOrderNo!;

            var shippedLines = await Db.OutboundOrderDetails.AsNoTracking()
                .Where(d => d.OutboundNo == outboundNo && !d.IsDeleted && d.ShippedQty > 0)
                .OrderBy(d => d.LineNo)
                .ToListAsync();
            if (shippedLines.Count == 0)
            {
                var msg = "出荷数のある明細がありません";
                await PersistEventAsync("WMS", "ERP", nameof(OnShipmentConfirmedAsync),
                    outboundNo, null, IntegrationEventStatus.Skipped, msg, corrId, payload, userName);
                return ErpBridgeResult.Skipped(msg);
            }

            var order = await Db.Orders
                .FirstOrDefaultAsync(o => o.WebOrderNo == webOrderNo && !o.IsDeleted);
            if (order == null)
            {
                var msg = $"受注 [{webOrderNo}] が見つかりません";
                await PersistEventAsync("WMS", "ERP", nameof(OnShipmentConfirmedAsync),
                    outboundNo, null, IntegrationEventStatus.Skipped, msg, corrId, payload, userName);
                return ErpBridgeResult.Skipped(msg);
            }

            var orderDetails = await Db.OrderDetails
                .Where(d => d.WebOrderNo == webOrderNo && !d.IsDeleted)
                .OrderBy(d => d.WebOrderDetailNo)
                .ToListAsync();

            var now = DateTime.Now;

            // 出庫明細を製品CDで受注明細へ順次充当（同一製品が複数行ある場合は未充足行から埋める）
            foreach (var grp in shippedLines.GroupBy(s => s.ProductCd))
            {
                var remaining = grp.Sum(s => s.ShippedQty);
                var targets = orderDetails
                    .Where(d => d.ProductCd == grp.Key)
                    .OrderBy(d => d.WebOrderDetailNo)
                    .ToList();
                if (targets.Count == 0) continue;

                foreach (var od in targets)
                {
                    if (remaining <= 0) break;
                    var ordered = od.Quantity ?? 0m;
                    var already = od.ShippedQty ?? 0m;
                    var capacity = ordered > 0 ? ordered - already : remaining; // 数量未設定なら全量充当
                    var apply = capacity > 0 ? Math.Min(remaining, capacity) : remaining;
                    if (apply <= 0) continue;

                    od.ShippedQty = already + apply;
                    od.ShipStatus = (ordered > 0 && od.ShippedQty >= ordered) ? 9 : 5;
                    od.LastShipDate = now;
                    od.LastOutboundNo = outboundNo;
                    od.Modifier = userName;
                    od.ModifyDate = now;
                    remaining -= apply;
                }
            }

            // ヘッダ ロールアップ
            bool allShipped = orderDetails.All(d => d.ShipStatus == 9);
            bool anyShipped = orderDetails.Any(d => d.ShipStatus >= 5);
            order.ShipStatus = allShipped ? 9 : (anyShipped ? 5 : order.ShipStatus);
            order.ActualShipDate = now;
            order.Modifier = userName;
            order.ModifyDate = now;

            await Db.SaveChangesAsync();

            Logger.LogInformation("[ERP-Bridge] 出庫 {Outbound} → 受注 {Order} 出荷実績回写（ShipStatus={Status}）",
                outboundNo, webOrderNo, order.ShipStatus);
            await PersistEventAsync("WMS", "ERP", nameof(OnShipmentConfirmedAsync),
                outboundNo, webOrderNo, IntegrationEventStatus.Success, null, corrId, payload, userName);
            return ErpBridgeResult.Ok(webOrderNo);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[ERP-Bridge] 出庫 {Outbound} の受注回写で予期せぬエラー", outboundNo);
            await PersistEventAsync("WMS", "ERP", nameof(OnShipmentConfirmedAsync),
                outboundNo, null, IntegrationEventStatus.Failed, ex.ToString(), corrId, payload, userName);
            return ErpBridgeResult.Failed(ex.Message);
        }
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
