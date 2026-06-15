using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 成本归集实现（章06 §2/§3）。料：WorkOrderMaterial.ActualQty（MES 真实消耗）× ProductMaterial.SupplyPrice
/// （BOM 受給単価，按 制品+工序+材料 匹配）；标准料 = 计划用量×同单价 → 差额即料用量差异。
/// 工/费按标准估算传入（F3-D1）。错误码：401 工单不存在 / 402 成本单已结转不可重归集。
/// </summary>
public class CostCollectService : ICostCollectService
{
    private readonly CP6Context _db;
    private readonly IFinSequenceService _seq;
    private const string SeqKey = "CS";

    public CostCollectService(CP6Context db, IFinSequenceService seq)
    {
        _db = db;
        _seq = seq;
    }

    public async Task<FinResult> CollectAsync(string workOrderNo, decimal laborStd, decimal overheadStd, string user)
    {
        var wo = await _db.Set<WorkOrder>().FirstOrDefaultAsync(w => w.WorkOrderNo == workOrderNo && !w.IsDeleted);
        if (wo == null) return FinResult.Fail("E-FIN-401");

        var sheet = await _db.CostSheets.Include(s => s.Lines).FirstOrDefaultAsync(s => s.WorkOrderNo == workOrderNo);
        if (sheet is { Status: CostSheetStatus.Settled }) return FinResult.Fail("E-FIN-402");

        // 料：实际/计划用量 × BOM 供给单价（制品+工序+材料 匹配）
        var woMats = await _db.Set<WorkOrderMaterial>()
            .Where(m => m.WorkOrderNo == workOrderNo && !m.IsDeleted).ToListAsync();
        var priceByKey = (await _db.Set<ProductMaterial>()
                .Where(p => p.ProductCd == wo.ProductCd && !p.IsDeleted).ToListAsync())
            .GroupBy(p => (p.ProcessCd, p.MaterialCd))
            .ToDictionary(g => g.Key, g => g.First().SupplyPrice ?? 0m);

        decimal matActual = 0m, matStd = 0m;
        var lines = new List<CostSheetLine>();
        var ln = 1;
        foreach (var m in woMats)
        {
            var price = priceByKey.GetValueOrDefault((m.ProcessCd, m.MaterialCd), 0m);
            var planQty = m.PlanQty ?? 0m;
            var actualAmt = Math.Round(m.ActualQty * price, 2, MidpointRounding.AwayFromZero);
            var stdAmt = Math.Round(planQty * price, 2, MidpointRounding.AwayFromZero);
            matActual += actualAmt;
            matStd += stdAmt;
            lines.Add(new CostSheetLine
            {
                LineNo = ln++, Element = CostElement.Material,
                ProcessCd = m.ProcessCd, MaterialCd = m.MaterialCd, MaterialName = m.MaterialName,
                PlanQty = planQty, ActualQty = m.ActualQty, UnitPrice = price,
                ActualAmount = actualAmt, StandardAmount = stdAmt,
            });
        }
        // 工/费标准估算行（无 MES 工时，按传入额；用量/单价留空）
        if (laborStd != 0m)
            lines.Add(new CostSheetLine { LineNo = ln++, Element = CostElement.Labor, ActualAmount = laborStd, StandardAmount = laborStd });
        if (overheadStd != 0m)
            lines.Add(new CostSheetLine { LineNo = ln++, Element = CostElement.Overhead, ActualAmount = overheadStd, StandardAmount = overheadStd });

        var now = DateTime.Now;
        if (sheet == null)
        {
            sheet = new CostSheet
            {
                Id = Guid.NewGuid(),
                No = await _seq.NextAsync(SeqKey, now),
                WorkOrderNo = workOrderNo,
                Creator = user,
                CreateDate = now,
            };
            _db.CostSheets.Add(sheet);
        }
        else
        {
            _db.CostSheetLines.RemoveRange(sheet.Lines);   // 重归集：清旧明细重算
            sheet.Lines.Clear();
            sheet.Modifier = user;
            sheet.ModifyDate = now;
        }

        sheet.ProductCd = wo.ProductCd;
        sheet.CostCenterId ??= null;
        sheet.CompletedQty = wo.CompletedQty;
        sheet.MaterialActual = matActual;
        sheet.MaterialStandard = matStd;
        sheet.LaborStd = laborStd;
        sheet.OverheadStd = overheadStd;
        sheet.Status = CostSheetStatus.Collected;
        foreach (var l in lines) { l.CostSheetId = sheet.Id; sheet.Lines.Add(l); }

        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public Task<CostSheet?> GetByWorkOrderAsync(string workOrderNo)
        => _db.CostSheets.Include(s => s.Lines).FirstOrDefaultAsync(s => s.WorkOrderNo == workOrderNo);

    public async Task<List<CostSheet>> ListAsync(CostSheetStatus? status = null)
    {
        var q = _db.CostSheets.AsNoTracking().AsQueryable();
        if (status is CostSheetStatus s) q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.CreateDate).ToListAsync();
    }
}
