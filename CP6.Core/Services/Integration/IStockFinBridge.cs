using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Integration;

/// <summary>
/// WMS 库存移动 → 财务（Fin）自动过账桥（章05 §5 / F1 波A）。
/// 库存事务落库后（best-effort，事务提交之后）据 <c>(TxnType, RelatedType)</c> 显式映射表决定是否生成
/// 库存类自动凭证（<c>Inventory.Received/AdjustGain/AdjustLoss/Scrapped</c>，<see cref="Fin.VoucherSource.Inventory"/>）。
/// 过账归属过滤：仅采购入库/盘盈亏/报废三类真过账；生产领料·完工·销售出库·库内移库等一律 Skipped
/// （由波B开票 / 波C反冲各自过账，桥不参与，避免双记）。幂等键 = <c>(Inventory, TxnNo)</c>，复用引擎 Source+SourceDocNo 查重。
/// </summary>
public interface IStockFinBridge
{
    /// <summary>库存移动后自动过账（幂等 TxnNo）。不属过账范围返回 <see cref="FinBridgeResult.Skipped"/>。</summary>
    Task<FinBridgeResult> OnStockMovedAsync(StockTransaction txn, string relatedType, string? userName);
}

/// <summary>配置 StockFinBridge:Enabled=false 时的 no-op 实现。</summary>
public class NoOpStockFinBridge : IStockFinBridge
{
    public Task<FinBridgeResult> OnStockMovedAsync(StockTransaction txn, string relatedType, string? userName)
        => Task.FromResult(FinBridgeResult.Skipped("StockFinBridge:Enabled=false"));
}
