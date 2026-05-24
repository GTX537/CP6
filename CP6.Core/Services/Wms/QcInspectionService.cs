using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>入荷検品サービス実装</summary>
public class QcInspectionService : IQcInspectionService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private readonly IInboundService _inbound;
    private const string Prefix = "QC";

    public QcInspectionService(CP6Context db, IWmsSequenceService seq, IInboundService inbound)
    {
        _db = db;
        _seq = seq;
        _inbound = inbound;
    }

    public async Task<List<QcInspection>> SearchAsync(QcInspectionSearchQuery q)
    {
        var query = _db.QcInspections.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q.InspectionNo)) query = query.Where(x => x.InspectionNo.Contains(q.InspectionNo));
        if (!string.IsNullOrWhiteSpace(q.InboundNo)) query = query.Where(x => x.InboundNo == q.InboundNo);
        if (!string.IsNullOrWhiteSpace(q.SupplierCd)) query = query.Where(x => x.SupplierCd == q.SupplierCd);
        if (q.Status.HasValue) query = query.Where(x => x.Status == q.Status.Value);
        if (!string.IsNullOrWhiteSpace(q.FinalJudgement)) query = query.Where(x => x.FinalJudgement == q.FinalJudgement);
        if (q.DateFrom.HasValue) query = query.Where(x => x.ArrivalDateTime >= q.DateFrom.Value);
        if (q.DateTo.HasValue) query = query.Where(x => x.ArrivalDateTime <= q.DateTo.Value);

        return await query
            .OrderByDescending(x => x.ArrivalDateTime)
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .ToListAsync();
    }

    public async Task<QcInspectionDto?> GetAsync(string inspectionNo)
    {
        var h = await _db.QcInspections.AsNoTracking().FirstOrDefaultAsync(x => x.InspectionNo == inspectionNo && !x.IsDeleted);
        if (h == null) return null;
        var items = await _db.QcInspectionItems.AsNoTracking()
            .Where(d => d.InspectionNo == inspectionNo && !d.IsDeleted)
            .OrderBy(d => d.LineNo).ToListAsync();
        return new QcInspectionDto
        {
            InspectionNo = h.InspectionNo,
            InboundNo = h.InboundNo,
            SupplierCd = h.SupplierCd,
            SupplierName = h.SupplierName,
            ArrivalDateTime = h.ArrivalDateTime,
            InspectorCd = h.InspectorCd,
            Status = h.Status,
            FinalJudgement = h.FinalJudgement,
            JudgementReason = h.JudgementReason,
            GeneratedReceiptNo = h.GeneratedReceiptNo,
            PhotoUrls = h.PhotoUrls,
            Remarks = h.Remarks,
            Items = items.Select(d => new QcInspectionItemDto
            {
                LineNo = d.LineNo, ProductCd = d.ProductCd, ProductName = d.ProductName,
                ExpectedQty = d.ExpectedQty, ReceivedQty = d.ReceivedQty,
                AcceptedQty = d.AcceptedQty, RejectedQty = d.RejectedQty, PendingQty = d.PendingQty,
                DefectReasonCd = d.DefectReasonCd, Remarks = d.Remarks,
            }).ToList(),
        };
    }

    public async Task<string> CreateFromInboundAsync(string inboundNo, string? userName)
    {
        var inbound = await _db.InboundOrders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.InboundNo == inboundNo && !x.IsDeleted)
            ?? throw new InvalidOperationException($"入庫予定 [{inboundNo}] が見つかりません");
        if (inbound.Status != InboundOrderStatus.Confirmed && inbound.Status != InboundOrderStatus.PartialReceived)
            throw new InvalidOperationException("WM-MSG-043: 入庫予定は確定済 or 入庫中である必要があります");

        var details = await _db.InboundOrderDetails.AsNoTracking()
            .Where(d => d.InboundNo == inboundNo && !d.IsDeleted)
            .OrderBy(d => d.LineNo).ToListAsync();
        if (details.Count == 0) throw new InvalidOperationException("WM-MSG-020: 入庫明細がありません");

        var no = await _seq.NextAsync(Prefix);
        _db.QcInspections.Add(new QcInspection
        {
            InspectionNo = no,
            InboundNo = inboundNo,
            SupplierCd = inbound.SupplierCd,
            SupplierName = inbound.SupplierName,
            ArrivalDateTime = DateTime.Now,
            InspectorCd = userName,
            Status = QcInspectionStatus.Created,
            Creator = userName,
        });

        int line = 1;
        foreach (var d in details)
        {
            _db.QcInspectionItems.Add(new QcInspectionItem
            {
                InspectionNo = no, LineNo = line++,
                ProductCd = d.ProductCd, ProductName = d.ProductName,
                ExpectedQty = d.ExpectedQty,
                ReceivedQty = d.ExpectedQty, // 既定は予定数（後で訂正）
                AcceptedQty = 0, RejectedQty = 0, PendingQty = 0,
                Creator = userName,
            });
        }

        await _db.SaveChangesAsync();
        return no;
    }

    public async Task<string> CreateDirectAsync(QcInspectionDto dto, string? userName)
    {
        if (dto.Items.Count == 0) throw new InvalidOperationException("WM-MSG-020: 明細がありません");
        var no = await _seq.NextAsync(Prefix);
        _db.QcInspections.Add(new QcInspection
        {
            InspectionNo = no,
            InboundNo = null,
            SupplierCd = dto.SupplierCd, SupplierName = dto.SupplierName,
            ArrivalDateTime = dto.ArrivalDateTime == default ? DateTime.Now : dto.ArrivalDateTime,
            InspectorCd = dto.InspectorCd ?? userName,
            Status = QcInspectionStatus.Created,
            PhotoUrls = dto.PhotoUrls, Remarks = dto.Remarks,
            Creator = userName,
        });
        int line = 1;
        foreach (var d in dto.Items)
        {
            _db.QcInspectionItems.Add(new QcInspectionItem
            {
                InspectionNo = no, LineNo = line++,
                ProductCd = d.ProductCd, ProductName = d.ProductName,
                ExpectedQty = d.ExpectedQty, ReceivedQty = d.ReceivedQty,
                Creator = userName,
            });
        }
        await _db.SaveChangesAsync();
        return no;
    }

    public async Task SaveItemsAsync(string inspectionNo, List<QcInspectionItemDto> items, string? userName)
    {
        var h = await _db.QcInspections.FirstOrDefaultAsync(x => x.InspectionNo == inspectionNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (h.Status != QcInspectionStatus.Created && h.Status != QcInspectionStatus.Inspecting)
            throw new InvalidOperationException("WM-MSG-043: 検品中以外は明細を更新できません");

        // status 0→1 自動遷移
        if (h.Status == QcInspectionStatus.Created)
        {
            h.Status = QcInspectionStatus.Inspecting;
            h.Modifier = userName;
            h.ModifyDate = DateTime.Now;
        }

        var existing = await _db.QcInspectionItems
            .Where(d => d.InspectionNo == inspectionNo && !d.IsDeleted).ToListAsync();
        var byLine = existing.ToDictionary(d => d.LineNo);

        foreach (var input in items)
        {
            if (!byLine.TryGetValue(input.LineNo, out var d)) continue;
            d.ReceivedQty = input.ReceivedQty;
            d.AcceptedQty = input.AcceptedQty;
            d.RejectedQty = input.RejectedQty;
            d.PendingQty = input.PendingQty;
            d.DefectReasonCd = input.DefectReasonCd;
            d.Remarks = input.Remarks;
            d.Modifier = userName;
            d.ModifyDate = DateTime.Now;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<JudgeResult> JudgeAsync(string inspectionNo, JudgeRequest req, string? userName)
    {
        var h = await _db.QcInspections.FirstOrDefaultAsync(x => x.InspectionNo == inspectionNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (h.Status != QcInspectionStatus.Inspecting && h.Status != QcInspectionStatus.Created)
            throw new InvalidOperationException("WM-MSG-043: 検品中以外は判定できません");

        if (string.IsNullOrWhiteSpace(req.FinalJudgement))
            throw new InvalidOperationException("WM-MSG-102: 判定理由を入力してください");

        h.FinalJudgement = req.FinalJudgement;
        h.JudgementReason = req.Reason;
        h.Status = QcInspectionStatus.Judged;
        h.Modifier = userName;
        h.ModifyDate = DateTime.Now;

        string? receiptNo = null;

        // 合格時：InboundReceipt を自動生成（在庫を計上）
        if (req.FinalJudgement == QcInspectionJudgement.Pass)
        {
            var items = await _db.QcInspectionItems
                .Where(d => d.InspectionNo == inspectionNo && !d.IsDeleted && d.AcceptedQty > 0)
                .OrderBy(d => d.LineNo).ToListAsync();
            if (items.Count > 0)
            {
                var warehouseCd = req.AcceptWarehouseCd
                    ?? (h.InboundNo != null
                        ? (await _db.InboundOrders.AsNoTracking()
                            .Where(x => x.InboundNo == h.InboundNo).Select(x => x.WarehouseCd).FirstOrDefaultAsync())
                        : null);
                if (string.IsNullOrWhiteSpace(warehouseCd))
                    throw new InvalidOperationException("自動入庫先倉庫CDが特定できません");

                var receiptDto = new InboundReceiptDto
                {
                    InboundNo = h.InboundNo,
                    SourceType = InboundSourceType.Purchase,
                    ReceiveDateTime = DateTime.Now,
                    OperatorCd = userName,
                    WarehouseCd = warehouseCd,
                    Remarks = $"QC合格自動入庫 from {inspectionNo}",
                    Details = items.Select((d, i) => new InboundReceiptDetailDto
                    {
                        LineNo = i + 1,
                        ProductCd = d.ProductCd,
                        ProductName = d.ProductName,
                        LotNo = $"QC{DateTime.Today:yyyyMMdd}-{i + 1:D3}",
                        ReceivedQty = d.AcceptedQty,
                        LocationCd = req.AcceptLocations != null && i < req.AcceptLocations.Count
                            ? req.AcceptLocations[i] : $"{warehouseCd}-RCV",
                    }).ToList(),
                };
                receiptNo = await _inbound.ConfirmReceiptAsync(receiptDto, userName);
                h.GeneratedReceiptNo = receiptNo;
            }
        }

        await _db.SaveChangesAsync();
        return new JudgeResult { FinalJudgement = req.FinalJudgement, GeneratedReceiptNo = receiptNo };
    }

    public async Task CancelAsync(string inspectionNo, string? userName)
    {
        var h = await _db.QcInspections.FirstOrDefaultAsync(x => x.InspectionNo == inspectionNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (h.Status == QcInspectionStatus.Judged)
            throw new InvalidOperationException("WM-MSG-043: 判定済は取消不可");
        h.Status = QcInspectionStatus.Cancelled;
        h.Modifier = userName;
        h.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
