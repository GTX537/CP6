using System.Globalization;
using System.Reflection;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class ReportCenterService : IReportCenterService
{
    private readonly CP6Context _db;
    public ReportCenterService(CP6Context db) => _db = db;

    // ═════════ 在庫月報 ═════════

    public async Task<List<MonthlyStockReportRow>> MonthlyStockReportAsync(string yearMonth, string? warehouseCd = null)
    {
        if (!TryParseYearMonth(yearMonth, out var monthEnd))
            throw new InvalidOperationException("yearMonth format must be 'YYYY-MM' or 'YYYYMM'");

        // 月末時点のスナップショット：T_StockTransaction を月末まで集計
        var txns = _db.StockTransactions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.TxnDateTime <= monthEnd);
        if (!string.IsNullOrWhiteSpace(warehouseCd))
            txns = txns.Where(x => x.WarehouseCd == warehouseCd);

        // IN/ADJ は + / OUT は - / RSV/UNRSV は数量変動なし
        var snapshot = await txns
            .Where(x => x.TxnType == WmsTxnType.IN
                     || x.TxnType == WmsTxnType.ADJ
                     || x.TxnType == WmsTxnType.OUT
                     || x.TxnType == WmsTxnType.MOVE)
            .GroupBy(x => new { x.WarehouseCd, x.ProductCd })
            .Select(g => new
            {
                g.Key.WarehouseCd,
                g.Key.ProductCd,
                // IN/ADJ + → 加算 ; OUT → 減算 ; MOVE → IN行は +、OUT行は -（CounterLocationCd で識別、ここでは符号付きQty が既に正規化されている前提）
                PhysicalQty = g.Sum(x => x.TxnType == WmsTxnType.OUT ? -x.Qty : x.Qty),
                LotCount = g.Select(x => x.LotNo).Distinct().Count(),
            })
            .Where(x => x.PhysicalQty != 0)
            .ToListAsync();

        // 利用可能数は T_Stock 現時点から（簡略化、過去スナップショットの精密な引当は持たない）
        var allocByKey = await _db.Stocks.AsNoTracking()
            .Where(s => !s.IsDeleted)
            .GroupBy(s => new { s.WarehouseCd, s.ProductCd })
            .Select(g => new { g.Key.WarehouseCd, g.Key.ProductCd, Alloc = g.Sum(s => s.AllocatedQty), AvgPrice = g.Average(s => (decimal?)s.UnitPrice) })
            .ToListAsync();

        var allocDict = allocByKey.ToDictionary(x => (x.WarehouseCd, x.ProductCd), x => x);
        return snapshot
            .Select(x =>
            {
                allocDict.TryGetValue((x.WarehouseCd, x.ProductCd), out var info);
                var alloc = info?.Alloc ?? 0m;
                var price = info?.AvgPrice;
                return new MonthlyStockReportRow
                {
                    YearMonth = monthEnd.ToString("yyyy-MM"),
                    WarehouseCd = x.WarehouseCd,
                    ProductCd = x.ProductCd,
                    PhysicalQty = x.PhysicalQty,
                    AllocatedQty = alloc,
                    AvailableQty = x.PhysicalQty - alloc,
                    EstimatedValue = price.HasValue ? x.PhysicalQty * price.Value : null,
                    LotCount = x.LotCount,
                };
            })
            .OrderBy(x => x.WarehouseCd).ThenBy(x => x.ProductCd)
            .ToList();
    }

    // ═════════ ABC 分析 ═════════

    public async Task<List<AbcAnalysisRow>> AbcAnalysisAsync(int analysisDays = 90, string? warehouseCd = null)
    {
        if (analysisDays <= 0) analysisDays = 90;
        var from = DateTime.Today.AddDays(-analysisDays);

        var q = _db.StockTransactions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.TxnType == WmsTxnType.OUT && x.TxnDateTime >= from);
        if (!string.IsNullOrWhiteSpace(warehouseCd))
            q = q.Where(x => x.WarehouseCd == warehouseCd);

        var agg = await q
            .GroupBy(x => x.ProductCd)
            .Select(g => new { ProductCd = g.Key, OutCount = g.Count(), OutQty = g.Sum(x => x.Qty) })
            .ToListAsync();

        if (agg.Count == 0) return new List<AbcAnalysisRow>();

        var ordered = agg.OrderByDescending(x => x.OutQty).ToList();
        var totalQty = ordered.Sum(x => x.OutQty);
        if (totalQty <= 0) return new List<AbcAnalysisRow>();

        var rows = new List<AbcAnalysisRow>(ordered.Count);
        decimal cum = 0m;
        foreach (var r in ordered)
        {
            cum += r.OutQty;
            var ratio = cum / totalQty;
            rows.Add(new AbcAnalysisRow
            {
                ProductCd = r.ProductCd,
                OutCount = r.OutCount,
                OutQty = r.OutQty,
                CumulativeRatio = Math.Round(ratio * 100m, 2),
                AbcRank = ratio <= 0.80m ? "A" : ratio <= 0.95m ? "B" : "C",
            });
        }
        return rows;
    }

    // ═════════ 滞留品 ═════════

    public async Task<List<DeadStockRow>> DeadStockAsync(int idleDays = 90, string? warehouseCd = null)
    {
        if (idleDays <= 0) idleDays = 90;
        var threshold = DateTime.Today.AddDays(-idleDays);

        var stocks = _db.Stocks.AsNoTracking().Where(s => !s.IsDeleted && s.PhysicalQty > 0);
        if (!string.IsNullOrWhiteSpace(warehouseCd))
            stocks = stocks.Where(s => s.WarehouseCd == warehouseCd);

        var stockList = await stocks.ToListAsync();

        // 各 (倉庫, 棚, 製品, ロット) ごとの最新 OUT/MOVE 日時を取得
        var lastMoves = await _db.StockTransactions.AsNoTracking()
            .Where(x => !x.IsDeleted && (x.TxnType == WmsTxnType.OUT || x.TxnType == WmsTxnType.MOVE))
            .GroupBy(x => new { x.WarehouseCd, x.LocationCd, x.ProductCd, x.LotNo })
            .Select(g => new
            {
                g.Key.WarehouseCd, g.Key.LocationCd, g.Key.ProductCd, g.Key.LotNo,
                LastMovedAt = g.Max(x => x.TxnDateTime),
            })
            .ToListAsync();
        var lastDict = lastMoves.ToDictionary(
            x => (x.WarehouseCd, x.LocationCd, x.ProductCd, x.LotNo),
            x => (DateTime?)x.LastMovedAt);

        var result = new List<DeadStockRow>();
        foreach (var s in stockList)
        {
            lastDict.TryGetValue((s.WarehouseCd, s.LocationCd, s.ProductCd, s.LotNo), out var lastMoved);
            // 最終受入日 vs 最終出庫日 — 大きい方を基準に
            DateTime baseline = lastMoved ?? s.ReceiveDate ?? s.CreateDate;
            if (baseline == DateTime.MinValue) continue;
            if (baseline > threshold) continue;

            var idle = (DateTime.Today - baseline.Date).Days;
            result.Add(new DeadStockRow
            {
                WarehouseCd = s.WarehouseCd,
                LocationCd = s.LocationCd,
                ProductCd = s.ProductCd,
                LotNo = s.LotNo,
                PhysicalQty = s.PhysicalQty,
                LastMovedAt = baseline,
                IdleDays = idle,
                EstimatedValue = s.UnitPrice.HasValue ? s.PhysicalQty * s.UnitPrice.Value : null,
            });
        }
        return result.OrderByDescending(x => x.IdleDays).ToList();
    }

    // ═════════ 入庫・出庫実績 ═════════

    public async Task<List<InboundHistoryRow>> InboundHistoryAsync(DateTime fromDate, DateTime toDate, string? warehouseCd = null, string? productCd = null)
    {
        var to = toDate.Date.AddDays(1); // 終日まで含む
        var q = _db.StockTransactions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.TxnType == WmsTxnType.IN
                && x.TxnDateTime >= fromDate.Date && x.TxnDateTime < to);
        if (!string.IsNullOrWhiteSpace(warehouseCd)) q = q.Where(x => x.WarehouseCd == warehouseCd);
        if (!string.IsNullOrWhiteSpace(productCd)) q = q.Where(x => x.ProductCd == productCd);

        return await q.OrderByDescending(x => x.TxnDateTime)
            .Select(x => new InboundHistoryRow
            {
                TxnNo = x.TxnNo, TxnDateTime = x.TxnDateTime,
                WarehouseCd = x.WarehouseCd, LocationCd = x.LocationCd,
                ProductCd = x.ProductCd, LotNo = x.LotNo,
                Qty = x.Qty,
                RelatedNo = x.RelatedNo, RelatedType = x.RelatedType,
                OperatorCd = x.OperatorCd,
            })
            .Take(5000).ToListAsync();
    }

    public async Task<List<OutboundHistoryRow>> OutboundHistoryAsync(DateTime fromDate, DateTime toDate, string? warehouseCd = null, string? productCd = null)
    {
        var to = toDate.Date.AddDays(1);
        var q = _db.StockTransactions.AsNoTracking()
            .Where(x => !x.IsDeleted && x.TxnType == WmsTxnType.OUT
                && x.TxnDateTime >= fromDate.Date && x.TxnDateTime < to);
        if (!string.IsNullOrWhiteSpace(warehouseCd)) q = q.Where(x => x.WarehouseCd == warehouseCd);
        if (!string.IsNullOrWhiteSpace(productCd)) q = q.Where(x => x.ProductCd == productCd);

        return await q.OrderByDescending(x => x.TxnDateTime)
            .Select(x => new OutboundHistoryRow
            {
                TxnNo = x.TxnNo, TxnDateTime = x.TxnDateTime,
                WarehouseCd = x.WarehouseCd, LocationCd = x.LocationCd,
                ProductCd = x.ProductCd, LotNo = x.LotNo,
                Qty = x.Qty,
                RelatedNo = x.RelatedNo, RelatedType = x.RelatedType,
                OperatorCd = x.OperatorCd,
            })
            .Take(5000).ToListAsync();
    }

    // ═════════ CSV エクスポート ═════════

    public byte[] ExportCsv<T>(IEnumerable<T> rows)
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var sb = new StringBuilder();

        // Header
        sb.AppendLine(string.Join(",", props.Select(p => EscapeCsv(p.Name))));

        // Rows
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",", props.Select(p =>
            {
                var v = p.GetValue(r);
                if (v == null) return string.Empty;
                if (v is DateTime dt) return EscapeCsv(dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                if (v is decimal dec) return EscapeCsv(dec.ToString(CultureInfo.InvariantCulture));
                if (v is double dbl) return EscapeCsv(dbl.ToString(CultureInfo.InvariantCulture));
                return EscapeCsv(v.ToString() ?? string.Empty);
            })));
        }
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        return bom.Concat(body).ToArray();
    }

    private static string EscapeCsv(string v)
    {
        if (string.IsNullOrEmpty(v)) return string.Empty;
        var needsQuote = v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r');
        if (!needsQuote) return v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }

    private static bool TryParseYearMonth(string ym, out DateTime monthEnd)
    {
        monthEnd = default;
        if (string.IsNullOrWhiteSpace(ym)) return false;
        var s = ym.Replace("-", "").Replace("/", "");
        if (s.Length != 6 || !int.TryParse(s.Substring(0, 4), out var y) || !int.TryParse(s.Substring(4, 2), out var m))
            return false;
        if (m < 1 || m > 12) return false;
        var daysInMonth = DateTime.DaysInMonth(y, m);
        monthEnd = new DateTime(y, m, daysInMonth, 23, 59, 59);
        return true;
    }
}
