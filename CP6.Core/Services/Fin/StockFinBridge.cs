using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Wms;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Fin;

/// <summary>
/// <see cref="IStockFinBridge"/> 标准实现（BridgeHookBase，Phase6 持久化）。库存移动 → 库存类自动凭证。
/// </summary>
/// <remarks>
/// 过账归属过滤（主控裁决——避免与波B出货开票 / 波C反冲双记）：桥内建 <b>显式映射表</b>
/// <c>(TxnType, RelatedType) → EventType 或 Skipped</c>，仅三类真过账：
/// <list type="bullet">
/// <item>采购入库：<c>IN + INBOUND</c> → <c>Inventory.Received</c>（借 INVENTORY / 贷 GRNI 暂估应付）</item>
/// <item>盘盈亏：<c>ADJ + STOCKTAKE</c>，按 <see cref="StockTransaction.Qty"/> 符号 → <c>Inventory.AdjustGain</c>（+）/ <c>Inventory.AdjustLoss</c>（−）</item>
/// <item>报废：<c>RelatedType=SCRAP</c>（现仓未有，后续引入）→ <c>Inventory.Scrapped</c></item>
/// </list>
/// 其余一切（生产领料 ISSUE / 完工 FG / 销售出库 OUTBOUND·SHIP / 库内 MOVE / RMA / 补充 REPLENISH /
/// 套件 KIT_* / 越库 XDOCK_* / 过期废弃 EXPIRY_DISPOSE / 外注 SUBCONTRACT / 波次 WAVE_PICK / 未映射值）
/// 一律 <see cref="FinBridgeResult.Skipped"/>（带 reason）——宁跳过不误记，过账正确性优先。
/// 金额守卫：金额=<c>|Qty×UnitPrice|</c>（方向由 PostingRule 借贷两侧表达）；单价缺失或金额&lt;=0 → Skipped（禁零额凭证）。
/// 幂等：<c>FinBizEvent{Source=Inventory, SourceDocNo=TxnNo}</c>，引擎按 Source+SourceDocNo+Posted 查重。
/// Best-Effort：异常握住并落 IntegrationEvent（失败可重试），不向上抛出、不阻断库存主操作。
/// </remarks>
public class StockFinBridge : BridgeHookBase, IStockFinBridge
{
    private readonly IAutoVoucherEngine _engine;

    public StockFinBridge(CP6Context db, IAutoVoucherEngine engine, ILogger<StockFinBridge> logger)
        : base(db, logger)
    {
        _engine = engine;
    }

    public async Task<FinBridgeResult> OnStockMovedAsync(StockTransaction txn, string relatedType, string? userName)
    {
        var corrId = Guid.NewGuid();
        var txnNo = txn?.TxnNo ?? "";
        var payload = new { txnNo, txn?.TxnType, relatedType, txn?.Qty, txn?.UnitPrice };
        try
        {
            var (eventType, skipReason) = Resolve(txn!.TxnType, relatedType, txn.Qty);
            if (eventType == null)
                return await SkipAsync(txnNo, skipReason ?? "unmapped", corrId, payload);

            // —— 金额守卫：|Qty×UnitPrice|；单价缺失或非正额 → 跳过（禁零额凭证）——
            if (txn.UnitPrice is not decimal unit)
                return await SkipAsync(txnNo, "单价缺失，无法计价过账", corrId, payload);

            var amount = Math.Abs(Math.Round(txn.Qty * unit, 2, MidpointRounding.AwayFromZero));
            if (amount <= 0m)
                return await SkipAsync(txnNo, "计价金额<=0，跳过（禁零额凭证）", corrId, payload);

            var evt = new FinBizEvent
            {
                EventType = eventType,
                Source = VoucherSource.Inventory,
                SourceDocNo = txnNo,   // 幂等键 (Inventory, TxnNo)
                BizDate = txn.TxnDateTime,
                Description = $"库存移动自动过账 {txn.TxnType}/{relatedType} {txnNo}",
            };
            evt.HeaderAmounts["Amount"] = amount;

            var r = await _engine.GenerateAsync(evt);
            var status = r.Ok ? IntegrationEventStatus.Success : IntegrationEventStatus.Failed;
            await PersistEventAsync("WMS", "FIN", nameof(OnStockMovedAsync),
                txnNo, r.Ok ? eventType : null, status, r.Ok ? null : r.Code, corrId, payload);
            if (r.Ok) Logger.LogInformation("[Stock-Fin-Bridge] 库存移动 {Txn} {Evt} → 自动过账", txnNo, eventType);
            return r.Ok ? FinBridgeResult.Ok(txnNo) : FinBridgeResult.Failed(r.Code ?? "fail");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Stock-Fin-Bridge] 库存移动 {Txn} 自动过账异常", txnNo);
            await PersistEventAsync("WMS", "FIN", nameof(OnStockMovedAsync),
                txnNo, null, IntegrationEventStatus.Failed, ex.ToString(), corrId, payload);
            return FinBridgeResult.Failed(ex.Message);
        }
    }

    private async Task<FinBridgeResult> SkipAsync(string txnNo, string reason, Guid corrId, object payload)
    {
        await PersistEventAsync("WMS", "FIN", nameof(OnStockMovedAsync),
            txnNo, null, IntegrationEventStatus.Skipped, reason, corrId, payload);
        return FinBridgeResult.Skipped(reason);
    }

    /// <summary>
    /// 显式映射表：<c>(TxnType, RelatedType)</c> → EventType 或 (null, skipReason)。
    /// 逐值来自 RelatedType 实际取值清单（INBOUND/OUTBOUND/MOVE/STOCKTAKE/RMA/REPLENISH/KIT_*/XDOCK_*/
    /// EXPIRY_DISPOSE/SUBCONTRACT/WAVE_PICK + 波C引入 ISSUE/FG + 报废 SCRAP）。
    /// </summary>
    private static (string? eventType, string? skipReason) Resolve(string? txnType, string? relatedType, decimal qty)
    {
        var tt = (txnType ?? "").Trim().ToUpperInvariant();
        var rt = (relatedType ?? "").Trim().ToUpperInvariant();

        // ① 报废：任意移动 RelatedType=SCRAP（现仓未有，波A/后续引入）
        if (rt == "SCRAP") return ("Inventory.Scrapped", null);

        // ② 采购入库暂估：IN + INBOUND
        if (tt == WmsTxnType.IN && rt == "INBOUND") return ("Inventory.Received", null);

        // ③ 盘盈/盘亏：ADJ + STOCKTAKE，按数量符号（金额取绝对值，方向由规则借贷表达）
        if (tt == WmsTxnType.ADJ && rt == "STOCKTAKE")
        {
            if (qty > 0m) return ("Inventory.AdjustGain", null);
            if (qty < 0m) return ("Inventory.AdjustLoss", null);
            return (null, "STOCKTAKE 差异数量为0，无过账");
        }

        // ④ 其余一律 Skipped（各模块自行过账 / 无GL影响 / 未映射）——宁跳过不误记
        return (null, SkipReasonFor(rt));
    }

    private static string SkipReasonFor(string rt) => rt switch
    {
        "OUTBOUND" => "销售/材料出库由波B开票或波C反冲过账，桥跳过避免双记",
        "SHIP" => "出货由波B开票过账，桥跳过避免双记",
        "ISSUE" => "生产领料由波C反冲过账，桥跳过避免双记",
        "FG" => "完工入库由波C成本结转过账，桥跳过避免双记",
        "INBOUND-FG" => "生产完工入库（PRODUCTION 源）由波C成本结转过账（借FG/贷WIP），桥跳过避免双记＋防GRNI虚增",
        "MOVE" => "库内移库无GL影响，跳过",
        "REPLENISH" => "补充移库无GL影响，跳过",
        "RMA" => "退品返品暂不自动过账，跳过",
        "KIT_ASSEMBLE" or "KIT_DISASSEMBLE" => "套件组立/拆解暂不自动过账，跳过",
        "XDOCK_IN" or "XDOCK_OUT" => "越库暂不自动过账，跳过",
        "EXPIRY_DISPOSE" => "过期废弃暂不自动过账（非SCRAP口径），跳过",
        "SUBCONTRACT" => "外注支给发料由采购模块过账，桥跳过",
        "WAVE_PICK" => "波次拣货无独立GL影响，跳过",
        "" => "无关联伝票区分，未映射，跳过",
        _ => $"未映射的关联伝票区分 {rt}，宁跳过不误记",
    };
}
