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

    public SubcontractService(CP6Context db, IWmsIssueService wmsIssue)
    {
        _db = db;
        _wmsIssue = wmsIssue;
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
}
