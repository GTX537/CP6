using CP6.Core.EFDbContext;
using CP6.Core.Services.Pur.Contracts;
using CP6.Entity.DomainModels.Pur;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur;

/// <summary>外注加工服务实现（采购 章07）。发支給材（委托 WMS 出库 + 追踪 IssuedQty）；成本核算/对账见后续任务。</summary>
public class SubcontractService : ISubcontractService
{
    private const int SubcontractPoType = 2;   // 外注委托（PurchaseOrder.Type）

    private readonly CP6Context _db;
    private readonly IWmsIssueService _wmsIssue;
    private readonly IFinCostService _finCost;

    public SubcontractService(CP6Context db, IWmsIssueService wmsIssue, IFinCostService finCost)
    {
        _db = db;
        _wmsIssue = wmsIssue;
        _finCost = finCost;
    }

    /// <inheritdoc />
    public async Task<List<PoConsignMaterial>> AddConsignAsync(string poNo, int lineNo, IEnumerable<ConsignMaterialDto> items, string? userName)
    {
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNo == poNo && !p.IsDeleted)
                 ?? throw new InvalidOperationException("E-PUR-071");   // 外注 PO 不存在
        if (po.Type != SubcontractPoType) throw new InvalidOperationException("E-PUR-072"); // 非外注 PO（Type≠2）

        var lineExists = await _db.PurchaseOrderLines
            .AnyAsync(l => l.PoNo == poNo && l.LineNo == lineNo && !l.IsDeleted && l.Status != 9);
        if (!lineExists) throw new InvalidOperationException("E-PUR-073");  // 外注成品行不存在

        var now = DateTime.Now;
        var existing = await _db.PoConsignMaterials
            .Where(c => c.PoNo == poNo && c.LineNo == lineNo && !c.IsDeleted).ToListAsync();

        foreach (var dto in items ?? Enumerable.Empty<ConsignMaterialDto>())
        {
            if (dto.ConsignQty <= 0) throw new InvalidOperationException("E-PUR-074"); // 应发数量须>0

            var row = existing.FirstOrDefault(c => c.ConsignItemId == dto.ConsignItemId);
            if (row == null)
            {
                row = new PoConsignMaterial
                {
                    PoNo = poNo,
                    LineNo = lineNo,
                    ConsignItemId = dto.ConsignItemId,
                    IssuedQty = 0m,
                    Creator = userName,
                    CreateDate = now,
                };
                _db.PoConsignMaterials.Add(row);
                existing.Add(row);
            }
            else
            {
                row.Modifier = userName;
                row.ModifyDate = now;
            }
            row.ConsignQty = dto.ConsignQty;            // upsert：应发量/成本更新（实发 IssuedQty 不动）
            row.ConsignUnitCost = dto.ConsignUnitCost;
        }

        await _db.SaveChangesAsync();
        return await GetConsignAsync(poNo, lineNo);
    }

    /// <inheritdoc />
    public async Task<List<PurchaseOrder>> ListOrdersAsync()
    {
        var pos = await _db.PurchaseOrders
            .Where(p => p.Type == SubcontractPoType && !p.IsDeleted)
            .OrderByDescending(p => p.OrderDate).ThenByDescending(p => p.PoNo).ToListAsync();
        if (pos.Count == 0) return pos;

        var poNos = pos.Select(p => p.PoNo).ToList();
        var linesByPo = (await _db.PurchaseOrderLines
                .Where(l => poNos.Contains(l.PoNo) && !l.IsDeleted && l.Status != 9).ToListAsync())
            .GroupBy(l => l.PoNo).ToDictionary(g => g.Key, g => g.OrderBy(l => l.LineNo).ToList());
        foreach (var p in pos)
            p.Lines = linesByPo.TryGetValue(p.PoNo, out var ls) ? ls : new List<PurchaseOrderLine>();
        return pos;
    }

    /// <inheritdoc />
    public async Task<List<PoConsignMaterial>> GetConsignAsync(string poNo, int? lineNo = null)
    {
        var q = _db.PoConsignMaterials.Where(c => c.PoNo == poNo && !c.IsDeleted);
        if (lineNo.HasValue) q = q.Where(c => c.LineNo == lineNo.Value);
        return await q.OrderBy(c => c.LineNo).ThenBy(c => c.ConsignItemId).ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<PoConsignMaterial>> IssueConsignAsync(string poNo, int lineNo, IEnumerable<ConsignIssueDto>? issuances, string? userName)
    {
        var consigns = await _db.PoConsignMaterials
            .Where(c => c.PoNo == poNo && c.LineNo == lineNo && !c.IsDeleted).ToListAsync();
        if (consigns.Count == 0) throw new InvalidOperationException("E-PUR-075"); // 该行无支給材可发料

        // 分批模式：仅发指定支給材的指定量；null → 各支給材一次发齐剩余应发量
        var batch = issuances?.ToList();
        if (batch != null)
            foreach (var b in batch)
                if (b.Qty <= 0) throw new InvalidOperationException("E-PUR-076"); // 发料量须>0

        var now = DateTime.Now;
        foreach (var c in consigns)
        {
            decimal qty;
            if (batch != null)
            {
                var pick = batch.FirstOrDefault(b => b.ConsignItemId == c.ConsignItemId);
                if (pick == null) continue;             // 本批未指定此料 → 跳过
                qty = pick.Qty;
            }
            else
            {
                qty = c.ConsignQty - c.IssuedQty;        // 剩余应发
                if (qty <= 0) continue;                  // 已发齐 → 跳过
            }

            var wms = await _wmsIssue.IssueAsync(new WmsIssueRequest
            {
                ItemId = c.ConsignItemId,
                Qty = qty,
                Purpose = "subcontract",                 // ★标明用途=外注支給，非销售出库/生产领料
                RefNo = $"{poNo}-{lineNo}",
            }, userName);

            c.IssuedQty += wms.IssuedQty;                // ★按实出累加（防吞料的锚）
            c.WmsIssueNo = wms.IssueNo;
            c.Modifier = userName;
            c.ModifyDate = now;
        }

        await _db.SaveChangesAsync();
        return await GetConsignAsync(poNo, lineNo);
    }

    /// <inheritdoc />
    public async Task<SubcontractCostResult> CalcFinishedCostAsync(string poNo, int lineNo, decimal finishedQty, string? userName)
    {
        if (finishedQty <= 0) throw new InvalidOperationException("E-PUR-077"); // 成品数量须>0

        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNo == poNo && !p.IsDeleted)
                 ?? throw new InvalidOperationException("E-PUR-071");   // 外注 PO 不存在
        if (po.Type != SubcontractPoType) throw new InvalidOperationException("E-PUR-072"); // 非外注 PO（Type≠2）

        var poLine = await _db.PurchaseOrderLines
            .FirstOrDefaultAsync(l => l.PoNo == poNo && l.LineNo == lineNo && !l.IsDeleted && l.Status != 9)
            ?? throw new InvalidOperationException("E-PUR-073");        // 外注成品行不存在

        var consigns = await _db.PoConsignMaterials
            .Where(c => c.PoNo == poNo && c.LineNo == lineNo && !c.IsDeleted).ToListAsync();

        var processingFee = poLine.UnitPrice * finishedQty;                 // ① 加工费（PO 行单价 × 成品数）
        var consignCost = consigns.Sum(c => c.ConsignQty * c.ConsignUnitCost); // ② 支給材成本（并入非另付）
        var finishedCost = processingFee + consignCost;                    // 成品成本 = 加工费 + 支給材成本

        var posted = await _finCost.PostSubcontractCostAsync(new SubcontractCostDto
        {
            PoNo = poNo,
            LineNo = lineNo,
            FinishedItemId = poLine.ItemId,
            FinishedQty = finishedQty,
            ProcessingFee = processingFee,
            ConsignCost = consignCost,
            FinishedCost = finishedCost,
        }, userName);
        if (!posted.Ok) throw new InvalidOperationException("E-PUR-078"); // 财务成本入账失败

        return new SubcontractCostResult
        {
            ProcessingFee = processingFee,
            ConsignCost = consignCost,
            FinishedCost = finishedCost,
            CostVoucherNo = posted.CostVoucherNo,
        };
    }

    /// <inheritdoc />
    public async Task<ConsignReconcileResult> ReconcileConsignAsync(string poNo, int lineNo, decimal finishedQty, decimal tolerancePct, string? userName)
    {
        if (finishedQty <= 0) throw new InvalidOperationException("E-PUR-077"); // 成品数量须>0

        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.PoNo == poNo && !p.IsDeleted)
                 ?? throw new InvalidOperationException("E-PUR-071");   // 外注 PO 不存在
        if (po.Type != SubcontractPoType) throw new InvalidOperationException("E-PUR-072"); // 非外注 PO（Type≠2）

        var poLine = await _db.PurchaseOrderLines
            .FirstOrDefaultAsync(l => l.PoNo == poNo && l.LineNo == lineNo && !l.IsDeleted && l.Status != 9)
            ?? throw new InvalidOperationException("E-PUR-073");        // 外注成品行不存在
        if (poLine.Qty <= 0) throw new InvalidOperationException("E-PUR-079"); // 计划成品数为 0，无法反推单耗

        var consigns = await _db.PoConsignMaterials
            .Where(c => c.PoNo == poNo && c.LineNo == lineNo && !c.IsDeleted)
            .OrderBy(c => c.ConsignItemId).ToListAsync();

        var tol = tolerancePct < 0 ? 0m : tolerancePct;
        var lines = new List<ConsignReconcileLine>();
        var hasAnomaly = false;
        foreach (var c in consigns)
        {
            // 单耗 = 计划应发 ÷ 计划成品数；应耗 = 单耗 × 实收成品数（成品反推）
            var unitUsage = c.ConsignQty / poLine.Qty;
            var expected = unitUsage * finishedQty;
            var variance = c.IssuedQty - expected;               // 正=多发（含合理损耗）
            var allowed = expected * tol;
            var anomaly = variance > allowed;                    // 多领超损耗 → 私吞/多领未用
            if (anomaly) hasAnomaly = true;

            lines.Add(new ConsignReconcileLine
            {
                ConsignItemId = c.ConsignItemId,
                IssuedQty = c.IssuedQty,
                ExpectedQty = expected,
                Variance = variance,
                AllowedVariance = allowed,
                IsAnomaly = anomaly,
            });
        }

        return new ConsignReconcileResult
        {
            PoNo = poNo,
            LineNo = lineNo,
            FinishedQty = finishedQty,
            TolerancePct = tol,
            HasAnomaly = hasAnomaly,
            Lines = lines,
        };
    }
}
