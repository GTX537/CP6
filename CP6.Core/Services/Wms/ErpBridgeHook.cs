using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Wms;

/// <summary>
/// <see cref="IErpBridgeHook"/> の標準実装。WMS 出荷確定 → ERP 受注 出荷実績回写。
/// </summary>
/// <remarks>
/// 出庫指示NO から OutboundOrder/明細・対応する受注を解決し、
/// 受注明細の累計出荷数・出荷ステータス・最終出荷日を更新、受注ヘッダにロールアップする。
/// 出庫明細↔受注明細の対応は WebOrderNo + ProductCd で照合（同一製品が複数明細にある場合は
/// 未充足の明細へ順次充当）。
/// </remarks>
public class ErpBridgeHook : IErpBridgeHook
{
    private readonly CP6Context _db;
    private readonly ILogger<ErpBridgeHook> _logger;

    public ErpBridgeHook(CP6Context db, ILogger<ErpBridgeHook> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ErpBridgeResult> OnShipmentConfirmedAsync(string outboundNo, string? userName)
    {
        try
        {
            var ob = await _db.OutboundOrders.AsNoTracking()
                .FirstOrDefaultAsync(x => x.OutboundNo == outboundNo && !x.IsDeleted);
            if (ob == null)
                return ErpBridgeResult.Skipped($"出庫指示 [{outboundNo}] が見つかりません");

            // 出荷区分かつ受注紐付きのみ回写対象（材料出庫等は対象外）
            if (ob.OutboundType != OutboundType.Shipping || string.IsNullOrWhiteSpace(ob.WebOrderNo))
                return ErpBridgeResult.Skipped("出荷区分かつ受注紐付きの出庫ではありません");

            var webOrderNo = ob.WebOrderNo!;

            var shippedLines = await _db.OutboundOrderDetails.AsNoTracking()
                .Where(d => d.OutboundNo == outboundNo && !d.IsDeleted && d.ShippedQty > 0)
                .OrderBy(d => d.LineNo)
                .ToListAsync();
            if (shippedLines.Count == 0)
                return ErpBridgeResult.Skipped("出荷数のある明細がありません");

            var order = await _db.Orders
                .FirstOrDefaultAsync(o => o.WebOrderNo == webOrderNo && !o.IsDeleted);
            if (order == null)
                return ErpBridgeResult.Skipped($"受注 [{webOrderNo}] が見つかりません");

            var orderDetails = await _db.OrderDetails
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

            await _db.SaveChangesAsync();

            _logger.LogInformation("[ERP-Bridge] 出庫 {Outbound} → 受注 {Order} 出荷実績回写（ShipStatus={Status}）",
                outboundNo, webOrderNo, order.ShipStatus);
            return ErpBridgeResult.Ok(webOrderNo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ERP-Bridge] 出庫 {Outbound} の受注回写で予期せぬエラー", outboundNo);
            return ErpBridgeResult.Failed(ex.Message);
        }
    }
}
