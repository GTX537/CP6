using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>RMA 返品管理サービス実装</summary>
public class RmaService : IRmaService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private readonly IStockMovementService _stock;
    private const string Prefix = "RMA";

    public RmaService(CP6Context db, IWmsSequenceService seq, IStockMovementService stock)
    {
        _db = db;
        _seq = seq;
        _stock = stock;
    }

    public async Task<List<RmaHeader>> SearchAsync(RmaSearchQuery q)
    {
        var query = _db.RmaHeaders.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.RmaNo)) query = query.Where(x => x.RmaNo.Contains(q.RmaNo));
        if (!string.IsNullOrWhiteSpace(q.CustomerCd)) query = query.Where(x => x.CustomerCd == q.CustomerCd);
        if (!string.IsNullOrWhiteSpace(q.OriginalShippingNo)) query = query.Where(x => x.OriginalShippingNo == q.OriginalShippingNo);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        if (q.AppliedFrom.HasValue) query = query.Where(x => x.AppliedDate >= q.AppliedFrom.Value);
        if (q.AppliedTo.HasValue) query = query.Where(x => x.AppliedDate <= q.AppliedTo.Value);
        return await query.OrderByDescending(x => x.AppliedDate)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
    }

    public async Task<RmaDto?> GetAsync(string rmaNo)
    {
        var h = await _db.RmaHeaders.AsNoTracking().FirstOrDefaultAsync(x => x.RmaNo == rmaNo && !x.IsDeleted);
        if (h == null) return null;
        var details = await _db.RmaDetails.AsNoTracking()
            .Where(d => d.RmaNo == rmaNo && !d.IsDeleted)
            .OrderBy(d => d.LineNo).ToListAsync();
        return new RmaDto
        {
            RmaNo = h.RmaNo, CustomerCd = h.CustomerCd, CustomerName = h.CustomerName,
            OriginalShippingNo = h.OriginalShippingNo, ReturnReason = h.ReturnReason,
            AppliedDate = h.AppliedDate, WarehouseCd = h.WarehouseCd, Status = h.Status,
            OperatorCd = h.OperatorCd, Remarks = h.Remarks,
            Details = details.Select(d => new RmaDetailDto
            {
                LineNo = d.LineNo, ProductCd = d.ProductCd, ProductName = d.ProductName,
                LotNo = d.LotNo, Qty = d.Qty, UnitCd = d.UnitCd,
                ConditionLevel = d.ConditionLevel, Judgement = d.Judgement,
                DestLocationCd = d.DestLocationCd,
                InboundTxnNo = d.InboundTxnNo, DispositionTxnNo = d.DispositionTxnNo,
                Remarks = d.Remarks,
            }).ToList(),
        };
    }

    public async Task<string> CreateAsync(RmaDto dto, string? userName)
    {
        if (string.IsNullOrWhiteSpace(dto.CustomerCd))
            throw new InvalidOperationException("客先CDは必須です");
        if (string.IsNullOrWhiteSpace(dto.WarehouseCd))
            throw new InvalidOperationException("入庫倉庫CDは必須です");
        if (dto.Details.Count == 0)
            throw new InvalidOperationException("WM-MSG-020: 返品明細がありません");

        var no = await _seq.NextAsync(Prefix);
        _db.RmaHeaders.Add(new RmaHeader
        {
            RmaNo = no, CustomerCd = dto.CustomerCd, CustomerName = dto.CustomerName,
            OriginalShippingNo = dto.OriginalShippingNo, ReturnReason = dto.ReturnReason,
            AppliedDate = dto.AppliedDate == default ? DateTime.Today : dto.AppliedDate,
            WarehouseCd = dto.WarehouseCd,
            Status = RmaStatus.Authorized, // 申請＝受付即発行
            OperatorCd = dto.OperatorCd ?? userName,
            Remarks = dto.Remarks, Creator = userName,
        });
        int line = 1;
        foreach (var d in dto.Details)
        {
            _db.RmaDetails.Add(new RmaDetail
            {
                RmaNo = no, LineNo = line++,
                ProductCd = d.ProductCd, ProductName = d.ProductName,
                LotNo = d.LotNo ?? "", Qty = d.Qty, UnitCd = d.UnitCd,
                ConditionLevel = d.ConditionLevel ?? RmaCondition.Open,
                Remarks = d.Remarks, Creator = userName,
            });
        }
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task ReceiveAsync(string rmaNo, string? userName)
    {
        var h = await _db.RmaHeaders.FirstOrDefaultAsync(x => x.RmaNo == rmaNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (h.Status != RmaStatus.Authorized)
            throw new InvalidOperationException("WM-MSG-043: 発行済のみ返品入庫可");

        var details = await _db.RmaDetails
            .Where(d => d.RmaNo == rmaNo && !d.IsDeleted).ToListAsync();

        // 各明細を IN（保留区分のロケーションへ）
        foreach (var d in details)
        {
            if (!string.IsNullOrWhiteSpace(d.InboundTxnNo)) continue; // 二重 IN 防止
            var txnNo = await _stock.ApplyAsync(new StockMovementRequest
            {
                TxnType = WmsTxnType.IN,
                WarehouseCd = h.WarehouseCd,
                LocationCd = $"{h.WarehouseCd}-RMA-HOLD",
                ProductCd = d.ProductCd,
                LotNo = string.IsNullOrWhiteSpace(d.LotNo) ? $"RMA-{rmaNo}-{d.LineNo}" : d.LotNo,
                Qty = d.Qty,
                UnitCd = d.UnitCd,
                RelatedNo = rmaNo,
                RelatedType = "RMA",
                OperatorCd = userName,
                Remark = $"RMA返品受入 {rmaNo} 行{d.LineNo}",
            });
            d.InboundTxnNo = txnNo;
            d.Modifier = userName; d.ModifyDate = DateTime.Now;
        }

        h.Status = RmaStatus.Received;
        h.Modifier = userName; h.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task StartInspectionAsync(string rmaNo, string? userName)
    {
        var h = await _db.RmaHeaders.FirstOrDefaultAsync(x => x.RmaNo == rmaNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (h.Status != RmaStatus.Received)
            throw new InvalidOperationException("WM-MSG-043: 返品入庫済のみ検査開始可");
        h.Status = RmaStatus.Inspecting;
        h.Modifier = userName; h.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task JudgeAndDisposeAsync(string rmaNo, List<RmaDispositionInput> inputs, string? userName)
    {
        var h = await _db.RmaHeaders.FirstOrDefaultAsync(x => x.RmaNo == rmaNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (h.Status != RmaStatus.Inspecting && h.Status != RmaStatus.Received)
            throw new InvalidOperationException("WM-MSG-043: 返品入庫済 or 検査中のみ判定可");

        var details = await _db.RmaDetails
            .Where(d => d.RmaNo == rmaNo && !d.IsDeleted).ToListAsync();
        var byLine = details.ToDictionary(d => d.LineNo);

        foreach (var input in inputs)
        {
            if (!byLine.TryGetValue(input.LineNo, out var d)) continue;
            if (string.IsNullOrWhiteSpace(input.Judgement))
                throw new InvalidOperationException($"WM-MSG-152: 行{input.LineNo} の判定理由を入力してください");
            d.Judgement = input.Judgement;
            d.DestLocationCd = input.DestLocationCd;

            // 振分処理：保留ロケから先のロケーションへ MOVE、または ADJ で除去
            var holdLoc = $"{h.WarehouseCd}-RMA-HOLD";
            var lotNo = string.IsNullOrWhiteSpace(d.LotNo) ? $"RMA-{rmaNo}-{d.LineNo}" : d.LotNo;

            switch (input.Judgement)
            {
                case RmaJudgement.Resell:
                case RmaJudgement.Repair:
                    if (string.IsNullOrWhiteSpace(input.DestLocationCd))
                        throw new InvalidOperationException($"WM-MSG-152: 行{input.LineNo} の振分先ロケが未指定");
                    // 保留 → 振分先 MOVE（OUT + IN）
                    await _stock.ApplyAsync(new StockMovementRequest
                    {
                        TxnType = WmsTxnType.MOVE,
                        WarehouseCd = h.WarehouseCd, LocationCd = holdLoc,
                        ProductCd = d.ProductCd, LotNo = lotNo, Qty = -d.Qty,
                        RelatedNo = rmaNo, RelatedType = "RMA",
                        OperatorCd = userName,
                        Remark = $"RMA {input.Judgement} 振分 元",
                    });
                    var inTxn = await _stock.ApplyAsync(new StockMovementRequest
                    {
                        TxnType = WmsTxnType.MOVE,
                        WarehouseCd = h.WarehouseCd, LocationCd = input.DestLocationCd,
                        ProductCd = d.ProductCd, LotNo = lotNo, Qty = d.Qty,
                        RelatedNo = rmaNo, RelatedType = "RMA",
                        OperatorCd = userName,
                        Remark = $"RMA {input.Judgement} 振分 先",
                    });
                    d.DispositionTxnNo = inTxn;
                    break;

                case RmaJudgement.Scrap:
                case RmaJudgement.SupplierReturn:
                    // 廃棄/仕入先返品：保留から ADJ -Qty で除去
                    var adjTxn = await _stock.ApplyAsync(new StockMovementRequest
                    {
                        TxnType = WmsTxnType.ADJ,
                        WarehouseCd = h.WarehouseCd, LocationCd = holdLoc,
                        ProductCd = d.ProductCd, LotNo = lotNo, Qty = -d.Qty,
                        RelatedNo = rmaNo, RelatedType = "RMA",
                        OperatorCd = userName,
                        Remark = $"RMA {input.Judgement}",
                    });
                    d.DispositionTxnNo = adjTxn;
                    break;

                default:
                    throw new InvalidOperationException($"不明な判定: {input.Judgement}");
            }
            d.Modifier = userName; d.ModifyDate = DateTime.Now;
        }

        h.Status = RmaStatus.Judged;
        h.Modifier = userName; h.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task CloseAsync(string rmaNo, string? userName)
    {
        var h = await _db.RmaHeaders.FirstOrDefaultAsync(x => x.RmaNo == rmaNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (h.Status != RmaStatus.Judged)
            throw new InvalidOperationException("WM-MSG-043: 判定済のみクローズ可");
        h.Status = RmaStatus.Closed;
        h.Modifier = userName; h.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task CancelAsync(string rmaNo, string? userName)
    {
        var h = await _db.RmaHeaders.FirstOrDefaultAsync(x => x.RmaNo == rmaNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (h.Status == RmaStatus.Closed)
            throw new InvalidOperationException("WM-MSG-043: クローズ済は取消不可");
        h.Status = RmaStatus.Cancelled;
        h.Modifier = userName; h.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
