using CP6.Core.EFDbContext;
using CP6.Core.Services.Mes;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 成本归集实现（章06 §2/§3 + A2 §4.4）。料：WorkOrderMaterial.ActualQty（MES 真实消耗）× ProductMaterial.SupplyPrice
/// （BOM 受給単価，按 制品+工序+材料 匹配）；标准料 = 计划用量×同单价 → 差额即料用量差异。
/// 工/费做真：逐工序 工时×费率（人工=人工工时×LaborRate，制费=机时×OverheadRate；缺实绩→标准回退）。
/// 双模式：StrictCostRate=true 缺工序/费率即失败；false（默认迁移）整单回退传入的标准估算。
/// 错误码：401 工单不存在 / 402 成本单已结转不可重归集 / E-A2-COST-001 无工序 / E-A2-RATE-002 无费率。
/// </summary>
public class CostCollectService : ICostCollectService
{
    private readonly CP6Context _db;
    private readonly IFinSequenceService _seq;
    private readonly IProcessCostRateService _rateSvc;
    private const string SeqKey = "CS";

    /// <summary>
    /// 严格费率模式（A2 §4.4）。true=缺工序/费率即失败（生产收尾/TDD）；
    /// false=迁移模式，缺工序/费率整单回退传入的标准估算（默认，兼容历史无工时数据）。
    /// </summary>
    public bool StrictCostRate { get; set; } = false;

    public CostCollectService(CP6Context db, IFinSequenceService seq, IProcessCostRateService rateSvc)
    {
        _db = db;
        _seq = seq;
        _rateSvc = rateSvc;
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
        // ── 工/费做真：逐工序 工时×费率（spec §4.4）──
        // 人工 = ActualLaborHour × LaborRate（缺则回退标准人工工时）；制费 = ActualMachineHour × OverheadRate（缺回退标准机时）。
        // 标准机时 = SetupHour + 完工数×CycleTime；标准人工工时 = 标准机时×标准人数。费率按基准日（实绩完工日→计划开始日→建单日）解析。
        decimal laborAct = 0m, laborStdAmt = 0m, ohAct = 0m, ohStd = 0m;
        var baseDate = wo.ActualEndDate ?? wo.PlanStartDate ?? wo.CreateDate;
        var calcQty = wo.CompletedQty;

        var wops = await _db.Set<WorkOrderProcess>()
            .Where(p => p.WorkOrderNo == workOrderNo && !p.IsDeleted).ToListAsync();
        var ppList = await _db.Set<ProductProcess>()
            .Where(p => p.ProductCd == wo.ProductCd && !p.IsDeleted).ToListAsync();
        ProductProcess? PP(WorkOrderProcess w) =>
            ppList.FirstOrDefault(p => p.ProcessCd == w.ProcessCd && p.TaskCd == w.TaskCd)
            ?? ppList.FirstOrDefault(p => p.ProcessCd == w.ProcessCd);

        bool legacyFallback = false;
        if (wops.Count == 0)
        {
            if (StrictCostRate) return FinResult.Fail("E-A2-COST-001");
            legacyFallback = true;
        }
        else
        {
            foreach (var w in wops)
            {
                var pp = PP(w);
                var stdMachine = (pp?.SetupHour ?? 0m) + calcQty * (pp?.CycleTime ?? 0m);
                var stdLabor = stdMachine * (pp?.StandardCrewSize ?? 1m);
                var rate = await _rateSvc.ResolveAsync(w.WgCd ?? "", baseDate);
                if (rate == null)
                {
                    if (StrictCostRate) return FinResult.Fail("E-A2-RATE-002");
                    legacyFallback = true; break;   // 全单回退，不混算
                }

                var actMachine = w.ActualMachineHour ?? stdMachine;
                var actLabor = w.ActualLaborHour ?? stdLabor;
                var fallbackHour = w.ActualMachineHour == null || w.ActualLaborHour == null;

                var laborActLine = Math.Round(actLabor * rate.LaborRate, 2, MidpointRounding.AwayFromZero);
                var laborStdLine = Math.Round(stdLabor * rate.LaborRate, 2, MidpointRounding.AwayFromZero);
                var ohActLine = Math.Round(actMachine * rate.OverheadRate, 2, MidpointRounding.AwayFromZero);
                var ohStdLine = Math.Round(stdMachine * rate.OverheadRate, 2, MidpointRounding.AwayFromZero);
                laborAct += laborActLine; laborStdAmt += laborStdLine; ohAct += ohActLine; ohStd += ohStdLine;

                lines.Add(new CostSheetLine
                {
                    LineNo = ln++, Element = CostElement.Labor, ProcessCd = w.ProcessCd, TaskCd = w.TaskCd, WgCd = w.WgCd,
                    Hours = actLabor, StandardHours = stdLabor, UnitPrice = rate.LaborRate, ActualAmount = laborActLine, StandardAmount = laborStdLine,
                    RateValidFrom = rate.ValidFrom, HourSource = fallbackHour ? "StandardFallback" : "Derived",
                    WarningCode = fallbackHour ? "W-A2-COST-001" : null, CalcNote = fallbackHour ? "实绩工时缺失，按标准工时计" : null,
                });
                lines.Add(new CostSheetLine
                {
                    LineNo = ln++, Element = CostElement.Overhead, ProcessCd = w.ProcessCd, TaskCd = w.TaskCd, WgCd = w.WgCd,
                    Hours = actMachine, StandardHours = stdMachine, UnitPrice = rate.OverheadRate, ActualAmount = ohActLine, StandardAmount = ohStdLine,
                    RateValidFrom = rate.ValidFrom, HourSource = fallbackHour ? "StandardFallback" : "Derived",
                    WarningCode = fallbackHour ? "W-A2-COST-001" : null,
                });
            }
        }

        if (legacyFallback)
        {
            laborAct = laborStdAmt = laborStd; ohAct = ohStd = overheadStd;
            lines.RemoveAll(l => l.Element is CostElement.Labor or CostElement.Overhead);   // 清部分行，整单估算
            lines.Add(new CostSheetLine
            {
                LineNo = ln++, Element = CostElement.Labor, ActualAmount = laborStd, StandardAmount = laborStd,
                HourSource = "LegacyFallback", WarningCode = "W-A2-COST-002", CalcNote = "缺费率/工序，整单回退传入估算",
            });
            lines.Add(new CostSheetLine
            {
                LineNo = ln++, Element = CostElement.Overhead, ActualAmount = overheadStd, StandardAmount = overheadStd,
                HourSource = "LegacyFallback", WarningCode = "W-A2-COST-002",
            });
        }

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
        // A2 §4.4：工/费按工时×费率做真（迁移模式缺工序/费率时整单回退传入估算，见上）
        sheet.LaborActual = laborAct;
        sheet.LaborStandard = laborStdAmt;
        sheet.OverheadActual = ohAct;
        sheet.OverheadStandard = ohStd;
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
