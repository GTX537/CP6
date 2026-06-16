using CP6.Core.EFDbContext;
using CP6.Core.Services.Pub;
using CP6.Entity.DomainModels.Pur;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur;

/// <summary>采购申请需求驱动生成服务实现（采购 章05 §3）。缺料反流 + 工单 BOM 缺料 → 自动建 PR。</summary>
public class PrGenerationService : IPrGenerationService
{
    private const string SeqKey = "PR";

    private readonly CP6Context _db;
    private readonly ISupplierPriceService _price;
    private readonly ISeqService _seq;

    public PrGenerationService(CP6Context db, ISupplierPriceService price, ISeqService seq)
    {
        _db = db;
        _price = price;
        _seq = seq;
    }

    /// <inheritdoc />
    public async Task<string?> GenerateFromShortageAsync(Guid shortageId, string? userName)
    {
        var sh = await _db.MaterialShortages.FirstOrDefaultAsync(s => s.Id == shortageId && !s.IsDeleted);
        if (sh == null) return null;

        var shortQty = sh.RequiredQty - sh.AvailableQty;
        if (shortQty <= 0) return null;                          // 无实际缺口，不开 PR

        // 幂等：同一缺料（SourceRefNo=缺料 Id）已生成过 PR → 防重复（MaterialShortage 无 Handled 字段，
        // 不擅自加字段/不复用 WMS Status[OPEN/RESOLVED]，改以 PR 侧 SourceRefNo 为锚保证幂等）
        var refNo = sh.Id.ToString();
        var exists = await _db.PurchaseRequests
            .AnyAsync(p => !p.IsDeleted && p.Source == PrSource.Shortage && p.SourceRefNo == refNo);
        if (exists) return null;

        var now = DateTime.Now;
        var pr = NewHeader(await _seq.NextAsync(SeqKey), PrSource.Shortage, refNo, userName, now);

        var (estPrice, suggestSupplier) = await EstimateAsync(sh.ProductCd, shortQty, sh.DetectedAt == default ? now : sh.DetectedAt);
        pr.Lines.Add(new PurchaseRequestLine
        {
            PrNo = pr.PrNo,
            LineNo = 1,
            ItemId = sh.ProductCd,
            Qty = shortQty,
            RequiredDate = sh.DetectedAt == default ? null : sh.DetectedAt,
            EstPrice = estPrice,
            SuggestSupplierId = suggestSupplier,
            Status = 0,
            Creator = userName,
            CreateDate = now,
        });

        _db.PurchaseRequests.Add(pr);
        _db.PurchaseRequestLines.AddRange(pr.Lines);
        await _db.SaveChangesAsync();
        return pr.PrNo;
    }

    /// <inheritdoc />
    public async Task<string?> GenerateFromWorkOrderAsync(string workOrderNo, string? userName)
    {
        if (string.IsNullOrWhiteSpace(workOrderNo)) return null;

        // 工单 BOM 缺料：SupplyStatus=3（不足）的材料行，缺口=PlanQty−ActualQty
        var shortMaterials = await _db.WorkOrderMaterials
            .Where(m => !m.IsDeleted && m.WorkOrderNo == workOrderNo && m.SupplyStatus == 3)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.ProcessCd).ThenBy(m => m.MaterialCd)
            .ToListAsync();
        if (shortMaterials.Count == 0) return null;

        var now = DateTime.Now;
        // 幂等锚：同工单已生成的 PR（Source=workorder ∧ SourceRefNo=工单号）下已开过料的集合
        var alreadyRequested = await (from l in _db.PurchaseRequestLines
                                      join p in _db.PurchaseRequests on l.PrNo equals p.PrNo
                                      where !l.IsDeleted && !p.IsDeleted
                                            && p.Source == PrSource.WorkOrder && p.SourceRefNo == workOrderNo
                                      select l.ItemId).ToListAsync();
        var requestedSet = alreadyRequested.ToHashSet();

        var pr = NewHeader(await _seq.NextAsync(SeqKey), PrSource.WorkOrder, workOrderNo, userName, now);

        var lineNo = 0;
        foreach (var m in shortMaterials)
        {
            var shortQty = (m.PlanQty ?? 0m) - m.ActualQty;
            if (shortQty <= 0) continue;

            // 幂等（每料）：同工单同料已生成过 PR 行则跳过
            if (requestedSet.Contains(m.MaterialCd)) continue;

            var (estPrice, suggestSupplier) = await EstimateAsync(m.MaterialCd, shortQty, now);
            pr.Lines.Add(new PurchaseRequestLine
            {
                PrNo = pr.PrNo,
                LineNo = ++lineNo,
                ItemId = m.MaterialCd,
                Qty = shortQty,
                UnitCd = m.Unit,
                EstPrice = estPrice,
                SuggestSupplierId = suggestSupplier,
                Status = 0,
                Creator = userName,
                CreateDate = now,
            });
        }

        if (pr.Lines.Count == 0) return null;                    // 全部已生成过 → 不开空 PR

        _db.PurchaseRequests.Add(pr);
        _db.PurchaseRequestLines.AddRange(pr.Lines);
        await _db.SaveChangesAsync();
        return pr.PrNo;
    }

    private static PurchaseRequest NewHeader(string prNo, string source, string? sourceRefNo, string? userName, DateTime now) => new()
    {
        PrNo = prNo,
        RequesterId = userName,
        RequestDate = now,
        Status = PrStatus.Draft,
        Source = source,
        SourceRefNo = sourceRefNo,
        Creator = userName,
        CreateDate = now,
    };

    /// <summary>
    /// 估价 + 建议供应商：先取采购价表（取价档对应供应商）；价表无命中则回退该物料历史最新 PO 行价 + 供应商；
    /// 均无 → (null, null)。估价供审批参考≠成交价（章05 §6）。
    /// </summary>
    private async Task<(decimal? estPrice, string? suggestSupplier)> EstimateAsync(string itemId, decimal qty, DateTime onDate)
    {
        // 1) 价表：取该物料满足量级&有效期、价档供应商最近维护的一档（不限定供应商，遍历候选）
        var tier = await _db.SupplierPrices
            .Where(p => !p.IsDeleted && p.ItemId == itemId
                        && p.MinQty <= qty
                        && p.ValidFrom <= onDate
                        && (p.ValidTo == null || p.ValidTo >= onDate))
            .OrderByDescending(p => p.MinQty)
            .ThenByDescending(p => p.ValidFrom)
            .Select(p => new { p.Price, p.SupplierId })
            .FirstOrDefaultAsync();
        if (tier != null) return (tier.Price, tier.SupplierId);

        // 2) 回退历史 PO：该物料最近一张 PO 行的单价 + 供应商
        var hist = await (from l in _db.PurchaseOrderLines
                          join po in _db.PurchaseOrders on l.PoNo equals po.PoNo
                          where !l.IsDeleted && !po.IsDeleted && l.ItemId == itemId && l.UnitPrice > 0
                          orderby po.OrderDate descending, po.PoNo descending
                          select new { l.UnitPrice, po.SupplierId })
                         .FirstOrDefaultAsync();
        if (hist != null) return (hist.UnitPrice, hist.SupplierId);

        return (null, null);
    }
}
