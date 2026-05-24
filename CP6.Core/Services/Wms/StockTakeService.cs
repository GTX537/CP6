using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 棚卸サービス実装。
/// </summary>
/// <remarks>
/// 仕様書 §10。
/// 採番：ST（YYYYMMDD-NNN 形式）。3 桁の月内連番。
/// 在庫調整は StockMovementService.ApplyAsync(ADJ, Qty=diff) 経由。
/// 第一版では「棚卸中フラグ」（Stock 凍結）は未実装。運用上の注意として
/// 棚卸期間中の出庫はカウント結果に影響する旨をオペレータに周知する。
/// </remarks>
public class StockTakeService : IStockTakeService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private readonly IStockMovementService _stock;

    private const string Prefix = "ST";

    public StockTakeService(CP6Context db, IWmsSequenceService seq, IStockMovementService stock)
    {
        _db = db;
        _seq = seq;
        _stock = stock;
    }

    // ───────── 検索・参照 ─────────

    public async Task<List<StockTake>> SearchAsync(StockTakeSearchQuery q)
    {
        var query = _db.StockTakes.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.StockTakeNo)) query = query.Where(x => x.StockTakeNo.Contains(q.StockTakeNo));
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        if (q.StockTakeType.HasValue) query = query.Where(x => x.StockTakeType == q.StockTakeType.Value);
        if (!string.IsNullOrWhiteSpace(q.TargetWarehouseCd)) query = query.Where(x => x.TargetWarehouseCd == q.TargetWarehouseCd);
        if (q.PlannedFrom.HasValue) query = query.Where(x => x.PlannedDate >= q.PlannedFrom.Value);
        if (q.PlannedTo.HasValue) query = query.Where(x => x.PlannedDate <= q.PlannedTo.Value);

        return await query
            .OrderByDescending(x => x.PlannedDate)
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .ToListAsync();
    }

    public async Task<StockTakeDto?> GetAsync(string stockTakeNo)
    {
        var h = await _db.StockTakes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.StockTakeNo == stockTakeNo && !x.IsDeleted);
        if (h == null) return null;

        var details = await _db.StockTakeDetails.AsNoTracking()
            .Where(d => d.StockTakeNo == stockTakeNo && !d.IsDeleted)
            .OrderBy(d => d.LineNo)
            .ToListAsync();

        return new StockTakeDto
        {
            StockTakeNo = h.StockTakeNo,
            StockTakeType = h.StockTakeType,
            PlannedDate = h.PlannedDate,
            ActualDate = h.ActualDate,
            CompletedDate = h.CompletedDate,
            Status = h.Status,
            TargetWarehouseCd = h.TargetWarehouseCd,
            TargetLocationPrefix = h.TargetLocationPrefix,
            TargetProductCd = h.TargetProductCd,
            ApprovalThresholdAmount = h.ApprovalThresholdAmount,
            ApproverCd = h.ApproverCd,
            Remarks = h.Remarks,
            Details = details.Select(ToDetailDto).ToList(),
        };
    }

    private static StockTakeDetailDto ToDetailDto(StockTakeDetail d) => new()
    {
        LineNo = d.LineNo,
        StockId = d.StockId,
        WarehouseCd = d.WarehouseCd,
        LocationCd = d.LocationCd,
        ProductCd = d.ProductCd,
        LotNo = d.LotNo,
        BookQty = d.BookQty,
        CountedQty = d.CountedQty,
        DiffQty = d.DiffQty,
        DiffAmount = d.DiffAmount,
        UnitPrice = d.UnitPrice,
        DiffReasonCd = d.DiffReasonCd,
        DiffReasonText = d.DiffReasonText,
        ApprovalStatus = d.ApprovalStatus,
        AdjustTxnNo = d.AdjustTxnNo,
        CountedByCd = d.CountedByCd,
        Remarks = d.Remarks,
    };

    // ───────── 計画 ─────────

    public async Task<string> CreatePlanAsync(StockTakePlanDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.TargetWarehouseCd))
            throw new InvalidOperationException("対象倉庫CDは必須です");

        // スナップショット対象 Stock の抽出
        var snapshotQuery = _db.Stocks.AsNoTracking()
            .Where(s => s.WarehouseCd == dto.TargetWarehouseCd && !s.IsDeleted);

        if (!string.IsNullOrWhiteSpace(dto.TargetLocationPrefix))
            snapshotQuery = snapshotQuery.Where(s => s.LocationCd.StartsWith(dto.TargetLocationPrefix));
        if (!string.IsNullOrWhiteSpace(dto.TargetProductCd))
            snapshotQuery = snapshotQuery.Where(s => s.ProductCd == dto.TargetProductCd);

        var snapshot = await snapshotQuery
            .OrderBy(s => s.LocationCd).ThenBy(s => s.ProductCd).ThenBy(s => s.LotNo)
            .ToListAsync();

        if (snapshot.Count == 0)
            throw new InvalidOperationException("対象 Stock が 0 件です。範囲を見直してください");

        var no = await _seq.NextAsync(Prefix);

        _db.StockTakes.Add(new StockTake
        {
            StockTakeNo = no,
            StockTakeType = dto.StockTakeType == 0 ? 1 : dto.StockTakeType,
            PlannedDate = dto.PlannedDate == default ? DateTime.Today : dto.PlannedDate,
            TargetWarehouseCd = dto.TargetWarehouseCd,
            TargetLocationPrefix = dto.TargetLocationPrefix,
            TargetProductCd = dto.TargetProductCd,
            ApprovalThresholdAmount = dto.ApprovalThresholdAmount,
            Remarks = dto.Remarks,
            Status = StockTakeStatus.Planned,
            Creator = userName,
        });

        int line = 1;
        foreach (var s in snapshot)
        {
            _db.StockTakeDetails.Add(new StockTakeDetail
            {
                StockTakeNo = no,
                LineNo = line++,
                StockId = s.Id,
                WarehouseCd = s.WarehouseCd,
                LocationCd = s.LocationCd,
                ProductCd = s.ProductCd,
                LotNo = s.LotNo,
                BookQty = s.PhysicalQty,
                UnitPrice = s.UnitPrice,
                ApprovalStatus = StockTakeDetailApproval.Pending,
                Creator = userName,
            });
        }

        await _db.SaveChangesAsync();
        return no;
    }

    // ───────── カウント ─────────

    public async Task StartCountAsync(string stockTakeNo, string? userName)
    {
        var h = await GetHeaderAsync(stockTakeNo);
        if (h.Status != StockTakeStatus.Planned)
            throw new InvalidOperationException("WM-MSG-043: 計画状態でないためカウント開始できません");

        h.Status = StockTakeStatus.Counting;
        h.ActualDate = DateTime.Today;
        h.Modifier = userName;
        h.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task UpdateCountsAsync(string stockTakeNo, List<StockTakeCountInput> inputs, string? userName)
    {
        var h = await GetHeaderAsync(stockTakeNo);
        if (h.Status != StockTakeStatus.Counting && h.Status != StockTakeStatus.DiffReview
            && h.Status != StockTakeStatus.AwaitingApproval)
            throw new InvalidOperationException("WM-MSG-043: カウント中・差異確認中・承認待ちのみ更新可");

        var detailMap = (await _db.StockTakeDetails
            .Where(d => d.StockTakeNo == stockTakeNo && !d.IsDeleted)
            .ToListAsync()).ToDictionary(d => d.LineNo, d => d);

        foreach (var input in inputs)
        {
            if (!detailMap.TryGetValue(input.LineNo, out var d)) continue;

            d.CountedQty = input.CountedQty;
            d.DiffQty = input.CountedQty - d.BookQty;
            d.DiffAmount = d.UnitPrice.HasValue ? (decimal?)(d.DiffQty * d.UnitPrice.Value) : null;
            d.CountedByCd = input.CountedByCd ?? userName;
            d.DiffReasonCd = input.DiffReasonCd;
            d.DiffReasonText = input.DiffReasonText;
            d.Remarks = input.Remarks;
            d.Modifier = userName;
            d.ModifyDate = DateTime.Now;
        }

        await _db.SaveChangesAsync();
    }

    public async Task SubmitForReviewAsync(string stockTakeNo, string? userName)
    {
        var h = await GetHeaderAsync(stockTakeNo);
        if (h.Status != StockTakeStatus.Counting)
            throw new InvalidOperationException("WM-MSG-043: カウント中でないため差異確認移行不可");

        var details = await _db.StockTakeDetails
            .Where(d => d.StockTakeNo == stockTakeNo && !d.IsDeleted)
            .ToListAsync();

        // 未カウントが残っているかチェック
        var uncounted = details.Count(d => d.CountedQty == null);
        if (uncounted > 0)
            throw new InvalidOperationException($"WM-MSG-060: 未カウント明細が {uncounted} 件あります");

        // 差異 0 を自動承認、差異理由 必須チェック
        decimal maxAbsDiffAmount = 0m;
        foreach (var d in details)
        {
            if (d.DiffQty == 0m)
            {
                d.ApprovalStatus = StockTakeDetailApproval.AutoApproved;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(d.DiffReasonCd))
                    throw new InvalidOperationException($"WM-MSG-061: 行 {d.LineNo} に差異理由コードが未入力です");
                d.ApprovalStatus = StockTakeDetailApproval.Pending;
                var abs = Math.Abs(d.DiffAmount ?? 0m);
                if (abs > maxAbsDiffAmount) maxAbsDiffAmount = abs;
            }
            d.Modifier = userName;
            d.ModifyDate = DateTime.Now;
        }

        // 閾値超なら 承認待ち（3）、そうでなければ差異確認（2）
        bool needsApproval = h.ApprovalThresholdAmount.HasValue
                          && maxAbsDiffAmount > h.ApprovalThresholdAmount.Value;
        h.Status = needsApproval ? StockTakeStatus.AwaitingApproval : StockTakeStatus.DiffReview;
        h.Modifier = userName;
        h.ModifyDate = DateTime.Now;

        await _db.SaveChangesAsync();
    }

    // ───────── 承認 + ADJ 反映 ─────────

    public async Task ApproveAndApplyAsync(string stockTakeNo, string? userName)
    {
        var h = await GetHeaderAsync(stockTakeNo);
        if (h.Status != StockTakeStatus.DiffReview && h.Status != StockTakeStatus.AwaitingApproval)
            throw new InvalidOperationException("WM-MSG-043: 差異確認 or 承認待ちでないため承認できません");

        var details = await _db.StockTakeDetails
            .Where(d => d.StockTakeNo == stockTakeNo && !d.IsDeleted)
            .ToListAsync();

        foreach (var d in details)
        {
            if (d.ApprovalStatus == StockTakeDetailApproval.Rejected) continue;

            var diff = d.DiffQty ?? 0m;
            if (diff != 0m)
            {
                // ADJ で差異分を反映（正なら IN 効果、負なら OUT 効果）
                var txnNo = await _stock.ApplyAsync(new StockMovementRequest
                {
                    TxnType = WmsTxnType.ADJ,
                    WarehouseCd = d.WarehouseCd,
                    LocationCd = d.LocationCd,
                    ProductCd = d.ProductCd,
                    LotNo = d.LotNo,
                    Qty = diff, // 符号付き
                    UnitPrice = d.UnitPrice,
                    RelatedNo = stockTakeNo,
                    RelatedType = "STOCKTAKE",
                    OperatorCd = userName,
                    Remark = $"棚卸差異調整 {stockTakeNo} 行 {d.LineNo} ({(diff > 0 ? "+" : "")}{diff})",
                });
                d.AdjustTxnNo = txnNo;
            }
            d.ApprovalStatus = StockTakeDetailApproval.Approved;
            d.Modifier = userName;
            d.ModifyDate = DateTime.Now;
        }

        h.Status = StockTakeStatus.Completed;
        h.CompletedDate = DateTime.Today;
        h.ApproverCd = userName;
        h.Modifier = userName;
        h.ModifyDate = DateTime.Now;

        await _db.SaveChangesAsync();
    }

    public async Task CancelAsync(string stockTakeNo, string? userName)
    {
        var h = await GetHeaderAsync(stockTakeNo);
        if (h.Status == StockTakeStatus.Completed)
            throw new InvalidOperationException("WM-MSG-043: 完了済の棚卸は取消不可");

        h.Status = StockTakeStatus.Cancelled;
        h.Modifier = userName;
        h.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    // ───────── 内部ヘルパー ─────────

    private async Task<StockTake> GetHeaderAsync(string no)
        => await _db.StockTakes.FirstOrDefaultAsync(x => x.StockTakeNo == no && !x.IsDeleted)
           ?? throw new InvalidOperationException("WM-MSG-070: 棚卸が見つかりません");
}
