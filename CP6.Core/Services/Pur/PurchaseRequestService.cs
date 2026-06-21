using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Pur.Contracts;
using CP6.Entity.DomainModels.Pur;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur;

/// <summary>采购申请服务实现（采购 章05 §4/§5）。手工建单 + 送审 + PR→PO 按建议供应商分组转单。</summary>
public class PurchaseRequestService : IPurchaseRequestService
{
    private const string SeqKey = "PR";
    private const string ApprovalBizType = "PUR_PR";

    private readonly CP6Context _db;
    private readonly ISeqService _seq;
    private readonly IApprovalService _approval;
    private readonly IPurchaseOrderService _po;

    public PurchaseRequestService(CP6Context db, ISeqService seq, IApprovalService approval, IPurchaseOrderService po)
    {
        _db = db;
        _seq = seq;
        _approval = approval;
        _po = po;
    }

    /// <inheritdoc />
    public async Task<PurchaseRequest?> GetAsync(string prNo)
    {
        var pr = await _db.PurchaseRequests.FirstOrDefaultAsync(p => p.PrNo == prNo && !p.IsDeleted);
        if (pr != null)
            pr.Lines = await _db.PurchaseRequestLines
                .Where(l => l.PrNo == prNo && !l.IsDeleted)
                .OrderBy(l => l.LineNo).ToListAsync();
        return pr;
    }

    /// <inheritdoc />
    public async Task<List<PurchaseRequest>> ListAsync(PrStatus? status = null, string? source = null)
    {
        var q = _db.PurchaseRequests.Where(p => !p.IsDeleted);
        if (status.HasValue) q = q.Where(p => p.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(p => p.Source == source);
        var list = await q.OrderByDescending(p => p.RequestDate).ThenByDescending(p => p.PrNo).ToListAsync();
        if (list.Count == 0) return list;

        // 批量载入明细行（避免 N+1），列表行展示「行数」（生产新上下文不走 EF 导航修复）
        var prNos = list.Select(p => p.PrNo).ToList();
        var linesByPr = (await _db.PurchaseRequestLines.Where(l => prNos.Contains(l.PrNo) && !l.IsDeleted).ToListAsync())
            .GroupBy(l => l.PrNo).ToDictionary(g => g.Key, g => g.OrderBy(l => l.LineNo).ToList());
        foreach (var p in list)
            p.Lines = linesByPr.TryGetValue(p.PrNo, out var ls) ? ls : new();
        return list;
    }

    /// <inheritdoc />
    public async Task<PurchaseRequest> CreateAsync(PrCreateDto dto, string? userName)
    {
        if (dto.Lines == null || dto.Lines.Count == 0) throw new InvalidOperationException("E-PUR-050"); // 至少一行

        var now = DateTime.Now;
        var pr = new PurchaseRequest
        {
            PrNo = await _seq.NextAsync(SeqKey),
            RequesterId = string.IsNullOrWhiteSpace(dto.RequesterId) ? userName : dto.RequesterId,
            DeptId = dto.DeptId,
            RequestDate = dto.RequestDate ?? now,
            Status = PrStatus.Draft,
            Source = PrSource.Manual,
            Remarks = dto.Remarks,
            Creator = userName,
            CreateDate = now,
        };

        var lineNo = 0;
        foreach (var ld in dto.Lines)
        {
            if (string.IsNullOrWhiteSpace(ld.ItemId)) throw new InvalidOperationException("E-PUR-051"); // 物料必填
            if (ld.Qty <= 0) throw new InvalidOperationException("E-PUR-055");                           // 数量须>0

            pr.Lines.Add(new PurchaseRequestLine
            {
                PrNo = pr.PrNo,
                LineNo = ++lineNo,
                ItemId = ld.ItemId,
                Qty = ld.Qty,
                UnitCd = ld.UnitCd,
                RequiredDate = ld.RequiredDate,
                EstPrice = ld.EstPrice,
                SuggestSupplierId = ld.SuggestSupplierId,
                Status = 0,
                Creator = userName,
                CreateDate = now,
            });
        }

        _db.PurchaseRequests.Add(pr);
        _db.PurchaseRequestLines.AddRange(pr.Lines);
        await _db.SaveChangesAsync();
        return pr;
    }

    /// <inheritdoc />
    public async Task<PurchaseRequest> SubmitForApprovalAsync(string prNo, string? userName)
    {
        var pr = await _db.PurchaseRequests.FirstOrDefaultAsync(p => p.PrNo == prNo && !p.IsDeleted)
                 ?? throw new InvalidOperationException("E-PUR-056"); // PR 不存在
        if (pr.Status != PrStatus.Draft) throw new InvalidOperationException("E-PUR-052"); // 仅草稿可送审

        var result = await _approval.SubmitAsync(new ApprovalSubmitRequest
        {
            BizType = ApprovalBizType,
            BizKey = pr.PrNo,
            Amount = 0m, // PR 估价仅参考，不作审批金额（成交价在 PO）
            Submitter = userName,
        });

        pr.ApprovalRef = result.ApprovalRef;
        // 桩即时通过 → 直接置已批；真实流程未即时通过则置已提交（送审中），待回调确认
        pr.Status = result.AutoApproved ? PrStatus.Approved : PrStatus.Submitted;
        pr.Modifier = userName;
        pr.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return pr;
    }

    /// <inheritdoc />
    public async Task ApproveFromApprovalAsync(string prNo, string decidedBy)
    {
        var pr = await _db.PurchaseRequests.FirstOrDefaultAsync(p => p.PrNo == prNo && !p.IsDeleted);
        if (pr == null || pr.Status != PrStatus.Submitted) return;   // 幂等：仅送审中可批
        pr.Status = PrStatus.Approved;
        pr.Modifier = decidedBy;
        pr.ModifyDate = DateTime.Now;
        // 不 SaveChanges —— OA 引擎统一持久化
    }

    /// <inheritdoc />
    public async Task RejectFromApprovalAsync(string prNo, string reason)
    {
        var pr = await _db.PurchaseRequests.FirstOrDefaultAsync(p => p.PrNo == prNo && !p.IsDeleted);
        if (pr == null || pr.Status != PrStatus.Submitted) return;   // 幂等
        pr.Status = PrStatus.Draft;   // 回退草稿可重编重送（驳回意见在 OA Wf_FlowHistory 留痕）
        pr.ModifyDate = DateTime.Now;
        // 不 SaveChanges
    }

    /// <inheritdoc />
    public async Task<List<string>> ConvertToPoAsync(string prNo, string? userName)
    {
        var pr = await _db.PurchaseRequests.FirstOrDefaultAsync(p => p.PrNo == prNo && !p.IsDeleted)
                 ?? throw new InvalidOperationException("E-PUR-056"); // PR 不存在
        if (pr.Status != PrStatus.Approved) throw new InvalidOperationException("E-PUR-053"); // 仅已批可转 PO

        var allLines = await _db.PurchaseRequestLines
            .Where(l => l.PrNo == prNo && !l.IsDeleted)
            .OrderBy(l => l.LineNo).ToListAsync();

        // 可转行：有效（Status==0）∧ 有建议供应商 ∧ 未转出。无建议供应商的留待 RFQ（章05 §5）。
        var convertible = allLines
            .Where(l => l.Status == 0
                        && !string.IsNullOrWhiteSpace(l.SuggestSupplierId)
                        && string.IsNullOrEmpty(l.ConvertedPoNo))
            .ToList();
        if (convertible.Count == 0) throw new InvalidOperationException("E-PUR-054"); // 无可转行

        var createdPoNos = new List<string>();

        // 按建议供应商分组拆单：一供应商一张 PO（一 PR 多供应商→拆多 PO）
        foreach (var group in convertible.GroupBy(l => l.SuggestSupplierId!))
        {
            var poDto = new PoCreateDto
            {
                SupplierId = group.Key,
                Type = 1,
                OrderDate = pr.RequestDate,
                Remarks = $"PR {pr.PrNo}",
                Lines = group.Select(l => new PoLineCreateDto
                {
                    ItemId = l.ItemId,
                    Qty = l.Qty,
                    UnitPrice = l.EstPrice, // PR 估价作 PO 单价；null 则 PO 侧按阶梯价解析
                    RequiredDate = l.RequiredDate,
                }).ToList(),
            };

            var po = await _po.CreateAsync(poDto, userName);

            foreach (var l in group)
                l.ConvertedPoNo = po.PoNo;
            createdPoNos.Add(po.PoNo);
        }

        // 状态推进：全行转出（无任何有效未转行）→ Converted；否则保持 Approved（部分转，余行待 RFQ）
        var anyUnconverted = allLines.Any(l => l.Status == 0 && string.IsNullOrEmpty(l.ConvertedPoNo));
        if (!anyUnconverted)
            pr.Status = PrStatus.Converted;

        pr.Modifier = userName;
        pr.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return createdPoNos;
    }
}
