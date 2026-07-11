using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 在庫移動サービス実装。
/// </summary>
/// <remarks>
/// 仕様書 §13.3 ApplyAsync 実装。
/// 1) BeginTransaction → 2) 対象 Stock SELECT (no-tracking) → 3) AllowNegative チェック
/// → 4) Stock 更新（RowVersion で楽観ロック） → 5) StockTransaction INSERT → 6) Commit。
///
/// 並行安全：DbUpdateConcurrencyException が出たら呼び出し側でリトライ。
/// EF Core の InMemory プロバイダはトランザクション・RowVersion を限定的にのみサポートするため、
/// 単体テストでは AllowNegative 判定など論理レベルのみ検証する。
/// </remarks>
public class StockMovementService : IStockMovementService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private readonly IWmsNotifier _notifier;
    private readonly IStockFinBridge _finBridge;
    private const string TxnPrefix = "TXN";

    public StockMovementService(CP6Context db, IWmsSequenceService seq, IWmsNotifier? notifier = null,
        IStockFinBridge? finBridge = null)
    {
        _db = db;
        _seq = seq;
        _notifier = notifier ?? new NoOpWmsNotifier();
        _finBridge = finBridge ?? new NoOpStockFinBridge();
    }

    public async Task<string> ApplyAsync(StockMovementRequest req, CancellationToken ct = default)
    {
        Validate(req);

        // SQL Server のみトランザクションを張る（InMemory は SupportsTransactions=false）
        IDbContextTransaction? tx = null;
        if (_db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
        {
            tx = await _db.Database.BeginTransactionAsync(ct);
        }

        try
        {
            // 在庫行を取得 or 新規（業務 UK で一意）
            var stock = await _db.Stocks
                .FirstOrDefaultAsync(s =>
                    s.WarehouseCd == req.WarehouseCd &&
                    s.LocationCd == req.LocationCd &&
                    s.ProductCd == req.ProductCd &&
                    s.LotNo == req.LotNo &&
                    !s.IsDeleted, ct);

            if (stock == null)
            {
                stock = new Stock
                {
                    WarehouseCd = req.WarehouseCd,
                    LocationCd = req.LocationCd,
                    ProductCd = req.ProductCd,
                    LotNo = req.LotNo,
                    PhysicalQty = 0m,
                    AllocatedQty = 0m,
                    AvailableQty = 0m,
                    UnitCd = req.UnitCd,
                    ReceiveDate = req.ReceiveDate,
                    ExpiryDate = req.ExpiryDate,
                    UnitPrice = req.UnitPrice,
                    OwnerType = req.OwnerType,
                    OwnerCd = req.OwnerCd,
                    PaperRollNo = req.PaperRollNo,
                    Creator = req.OperatorCd,
                };
                _db.Stocks.Add(stock);
            }

            // 在庫数の更新（種別毎にロジック分岐）
            ApplyDelta(stock, req);

            // マイナス在庫チェック（AllowNegative=true の倉庫、または要求で明示的に上書きされた場合は除外）
            var allowNegative = req.AllowNegativeOverride || (await _db.Warehouses
                .Where(w => w.WarehouseCd == req.WarehouseCd && !w.IsDeleted)
                .Select(w => (bool?)w.AllowNegative)
                .FirstOrDefaultAsync(ct) ?? false);

            if (!allowNegative)
            {
                if (stock.PhysicalQty < 0)
                    throw new InsufficientStockException(req.ProductCd, req.LotNo, req.Qty, stock.PhysicalQty + req.Qty);
                if (stock.AvailableQty < 0)
                    throw new InsufficientStockException(req.ProductCd, req.LotNo, req.Qty, stock.AvailableQty + req.Qty);
            }

            stock.Modifier = req.OperatorCd;
            stock.ModifyDate = DateTime.Now;

            // トランザクション履歴を INSERT
            var txnNo = await _seq.NextAsync(TxnPrefix, DateTime.Today);
            var txn = new StockTransaction
            {
                TxnNo = txnNo,
                TxnType = req.TxnType,
                TxnDateTime = DateTime.Now,
                WarehouseCd = req.WarehouseCd,
                LocationCd = req.LocationCd,
                ProductCd = req.ProductCd,
                LotNo = req.LotNo,
                Qty = req.Qty,
                UnitCd = req.UnitCd,
                UnitPrice = req.UnitPrice,
                RelatedNo = req.RelatedNo,
                RelatedType = req.RelatedType,
                OperatorCd = req.OperatorCd,
                Remark = req.Remark,
                OwnerType = req.OwnerType,
                OwnerCd = req.OwnerCd,
                PaperRollNo = req.PaperRollNo,
                Creator = req.OperatorCd,
            };
            _db.StockTransactions.Add(txn);

            await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);

            // WMS→Fin 库存过账桥点火（best-effort、commit 之后・通知の前、失敗しても在庫移動は成功扱い）。
            // 実際に過账するか否かは桥内の (TxnType,RelatedType) 帰属フィルタが判定（採購入庫/盘盈亏/报废のみ）。
            try
            {
                await _finBridge.OnStockMovedAsync(txn, req.RelatedType ?? "", req.OperatorCd);
            }
            catch { /* fin bridge failure must not break stock movement */ }

            // SignalR リアルタイム通知（best-effort、失敗しても本処理は成功扱い）
            try
            {
                await _notifier.NotifyStockChangedAsync(new StockChangedEvent
                {
                    TxnNo = txnNo,
                    TxnType = req.TxnType,
                    TxnAt = txn.TxnDateTime,
                    WarehouseCd = req.WarehouseCd,
                    LocationCd = req.LocationCd,
                    ProductCd = req.ProductCd,
                    LotNo = req.LotNo,
                    Qty = req.Qty,
                    RelatedNo = req.RelatedNo,
                    OperatorCd = req.OperatorCd,
                });
            }
            catch { /* notifier failure must not break stock movement */ }

            return txnNo;
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }
    }

    public async Task<(string OutTxnNo, string InTxnNo)> MoveAsync(StockMoveRequest req, CancellationToken ct = default)
    {
        if (req.Qty <= 0)
            throw new ArgumentException("WM-MSG-011: 移動数量は0より大きい値を指定してください。");
        if (string.Equals(req.FromLocationCd, req.ToLocationCd, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("WM-MSG-010: 移動先は移動元と異なるロケーションを指定してください。");

        // 源 OUT 側：物理在庫を減らす（StockTransaction には MOVE 種別 + Qty=-X で記録）
        var outNo = await ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.MOVE,
            WarehouseCd = req.WarehouseCd,
            LocationCd = req.FromLocationCd,
            ProductCd = req.ProductCd,
            LotNo = req.LotNo,
            Qty = -req.Qty,
            OperatorCd = req.OperatorCd,
            Remark = req.Remark ?? $"MOVE → {req.ToLocationCd}",
            RelatedType = "MOVE",
        }, ct);

        // 先 IN 側：物理在庫を増やす（Qty=+X で記録）
        var inNo = await ApplyAsync(new StockMovementRequest
        {
            TxnType = WmsTxnType.MOVE,
            WarehouseCd = req.WarehouseCd,
            LocationCd = req.ToLocationCd,
            ProductCd = req.ProductCd,
            LotNo = req.LotNo,
            Qty = req.Qty,
            OperatorCd = req.OperatorCd,
            Remark = req.Remark ?? $"MOVE ← {req.FromLocationCd}",
            RelatedType = "MOVE",
            RelatedNo = outNo,
        }, ct);

        return (outNo, inNo);
    }

    // ───────── 内部ヘルパー ─────────

    private static void Validate(StockMovementRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.TxnType))
            throw new ArgumentException("TxnType is required.");
        if (string.IsNullOrWhiteSpace(req.WarehouseCd))
            throw new ArgumentException("WarehouseCd is required.");
        if (string.IsNullOrWhiteSpace(req.LocationCd))
            throw new ArgumentException("LocationCd is required.");
        if (string.IsNullOrWhiteSpace(req.ProductCd))
            throw new ArgumentException("ProductCd is required.");
        if (req.LotNo == null)
            throw new ArgumentException("LotNo cannot be null (use empty string for no-lot).");
        if (req.Qty == 0 && req.TxnType != WmsTxnType.ADJ)
            throw new ArgumentException("WM-MSG-031: 数量は0以外を指定してください。");
        // IN/OUT/RSV/UNRSV は正数のみ（負数で逆操作を不可、誤用防止）
        var requirePositive = req.TxnType is WmsTxnType.IN or WmsTxnType.OUT
                                          or WmsTxnType.RSV or WmsTxnType.UNRSV;
        if (requirePositive && req.Qty <= 0)
            throw new ArgumentException("WM-MSG-021: 数量は0より大きい値を入力してください。");
    }

    /// <summary>種別ごとに Stock の数量列を増減する</summary>
    private static void ApplyDelta(Stock stock, StockMovementRequest req)
    {
        switch (req.TxnType)
        {
            case WmsTxnType.IN:
                stock.PhysicalQty += req.Qty;
                break;
            case WmsTxnType.OUT:
                stock.PhysicalQty -= req.Qty;
                stock.AllocatedQty = Math.Max(0m, stock.AllocatedQty - req.Qty);
                break;
            case WmsTxnType.MOVE:
                // MoveAsync 経由：源 OUT=Qty<0 / 先 IN=Qty>0 を符号で表現
                stock.PhysicalQty += req.Qty;
                if (req.Qty < 0)
                    stock.AllocatedQty = Math.Max(0m, stock.AllocatedQty + req.Qty);
                break;
            case WmsTxnType.ADJ:
                // 棚卸差異等、符号付き差分（+/-）
                stock.PhysicalQty += req.Qty;
                break;
            case WmsTxnType.RSV:
                stock.AllocatedQty += req.Qty;
                break;
            case WmsTxnType.UNRSV:
                stock.AllocatedQty = Math.Max(0m, stock.AllocatedQty - req.Qty);
                break;
            default:
                throw new ArgumentException($"Unsupported TxnType: {req.TxnType}");
        }
        stock.AvailableQty = stock.PhysicalQty - stock.AllocatedQty;
    }
}
