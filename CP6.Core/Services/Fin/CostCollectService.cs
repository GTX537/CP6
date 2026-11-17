using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
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
public partial class CostCollectService : ICostCollectService
{
    private readonly CP6Context _db;
    private readonly IFinSequenceService _seq;
    private readonly IProcessCostRateService _rateSvc;
    private readonly OperationsOwnerV1? _owner;
    private readonly ManufacturingFinanceScopeV1? _financeScope;
    private readonly CostPolicySourcesV1? _policySources;
    private const string SeqKey = "CS";

    /// <summary>
    /// 严格费率模式（A2 §4.4）。true=缺工序/费率即失败（生产收尾/TDD）；
    /// false=迁移模式，缺工序/费率整单回退传入的标准估算（默认，兼容历史无工时数据）。
    /// </summary>
    public bool StrictCostRate { get; set; } = false;

    public CostCollectService(CP6Context db, IFinSequenceService seq, IProcessCostRateService rateSvc,
        OperationsOwnerV1? owner = null, ManufacturingFinanceScopeV1? financeScope = null, CostPolicySourcesV1? policySources = null)
    {
        _db = db;
        _seq = seq;
        _rateSvc = rateSvc;
        _owner = owner;
        _financeScope = financeScope;
        _policySources = policySources;
    }

    public Task<FinResult> CollectAsync(string workOrderNo, decimal laborStd, decimal overheadStd, string user)
        => _db.Database.IsNpgsql() ? CollectOwnedAsync(workOrderNo) : CollectLegacyAsync(workOrderNo, laborStd, overheadStd, user);

    private async Task<FinResult> CollectLegacyAsync(string workOrderNo, decimal laborStd, decimal overheadStd, string user)
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

        var lines = new List<CostSheetLine>();
        var ln = 1;
        foreach (var m in woMats)
        {
            var price = priceByKey.GetValueOrDefault((m.ProcessCd, m.MaterialCd), 0m);
            var planQty = m.PlanQty ?? 0m;
            var actualAmt = Math.Round(m.ActualQty * price, 2, MidpointRounding.AwayFromZero);
            var stdAmt = Math.Round(planQty * price, 2, MidpointRounding.AwayFromZero);
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

        return await SaveCollectionAsync(wo, sheet, lines, user, default);
    }

    private async Task<FinResult> SaveCollectionAsync(WorkOrder wo, CostSheet? sheet, List<CostSheetLine> lines,
        string actor, CancellationToken ct,
        (DateTime Date, CostCollectionBasisV1.RateUseV1[] Rates, Guid? LedgerId, Guid? CenterId)? rateBasis = null,
        ManufacturingCenterUseV1.Use? centerUse = null, StandardBasisReferenceV1? standardBasis = null)
    {
        var groups = lines.ToLookup(line => line.Element);
        (decimal Actual, decimal Standard) Totals(CostElement element) =>
            (groups[element].Sum(line => line.ActualAmount), groups[element].Sum(line => line.StandardAmount));
        var material = Totals(CostElement.Material);
        var labor = Totals(CostElement.Labor);
        var overhead = Totals(CostElement.Overhead);
        if (_db.Database.IsNpgsql() && (!AmountFits(material.Actual) || !AmountFits(material.Standard)
            || !AmountFits(labor.Actual) || !AmountFits(labor.Standard)
            || !AmountFits(overhead.Actual) || !AmountFits(overhead.Standard)))
            return FinResult.Fail("COST_VALUE_OUT_OF_RANGE");

        var now = DateTime.Now;
        if (sheet == null)
        {
            sheet = new CostSheet
            {
                Id = Guid.NewGuid(),
                No = await _seq.NextAsync(SeqKey, now),
                WorkOrderNo = wo.WorkOrderNo,
                Creator = actor,
                CreateDate = now,
            };
            _db.CostSheets.Add(sheet);
        }
        else
        {
            _db.CostSheetLines.RemoveRange(sheet.Lines);   // 重归集：清旧明细重算
            sheet.Lines.Clear();
            sheet.Modifier = actor;
            sheet.ModifyDate = now;
        }

        sheet.ProductCd = wo.ProductCd;
        if (rateBasis is { LedgerId: not null } adopted)
            sheet.CostCenterId = adopted.CenterId;
        sheet.CompletedQty = wo.CompletedQty;
        (sheet.MaterialActual, sheet.MaterialStandard) = material;
        (sheet.LaborActual, sheet.LaborStandard) = labor;
        (sheet.OverheadActual, sheet.OverheadStandard) = overhead;
        sheet.HasApprovedStandardBasis = standardBasis != null;
        sheet.Status = CostSheetStatus.Collected;
        foreach (var l in lines) { l.CostSheetId = sheet.Id; sheet.Lines.Add(l); }
        _db.CostSheetLines.AddRange(lines);

        long? basisVersion = null;
        Guid? useEffect = null;
        if (centerUse != null)
        {
            basisVersion = await CostCollectionBasisStoreV1.NextAsync(_db, sheet.Id, ct);
            useEffect = await ManufacturingCenterUseV1.EnlistAsync(_db, centerUse, ManufacturingCenterUseV1.CostCollection,
                sheet.Id, basisVersion.Value, ct);
        }
        await _db.SaveChangesAsync(ct);
        if (rateBasis is { } basis)
            await CostCollectionBasisStoreV1.AppendAsync(_db, sheet, basis.Date, basis.Rates, basis.LedgerId,
                basisVersion, useEffect, standardBasis, ct);
        return FinResult.Pass();
    }

    public Task<CostSheet?> GetByWorkOrderAsync(string workOrderNo)
        => ReadHistoryAsync(async (sheets, ct) =>
        {
            var sheet = await sheets.Include(s => s.Lines).FirstOrDefaultAsync(s => s.WorkOrderNo == workOrderNo, ct);
            if (sheet != null)
                await MarkApprovedZeroStandardsAsync([sheet], ct);
            return sheet;
        });

    public Task<List<CostSheet>> ListAsync(CostSheetStatus? status = null)
        => ReadHistoryAsync(async (sheets, ct) =>
        {
            if (status is CostSheetStatus filter) sheets = sheets.Where(sheet => sheet.Status == filter);
            var rows = await sheets.AsNoTracking().OrderByDescending(sheet => sheet.CreateDate).ToListAsync(ct);
            await MarkApprovedZeroStandardsAsync(rows, ct);
            return rows;
        });

    private async Task MarkApprovedZeroStandardsAsync(IReadOnlyCollection<CostSheet> sheets, CancellationToken ct)
    {
        var zero = sheets.Where(sheet => sheet.StandardCost == 0m).ToDictionary(sheet => sheet.Id);
        if (zero.Count == 0 || !_db.Database.IsNpgsql())
            return;
        var approved = await CostCollectionBasisStoreV1.SheetsWithApprovedStandardAsync(_db, zero.Keys.ToArray(), ct);
        foreach (var id in approved)
            zero[id].HasApprovedStandardBasis = true;
    }

    private Task<T> ReadHistoryAsync<T>(Func<IQueryable<CostSheet>, CancellationToken, Task<T>> read)
    {
        var sheets = _db.CostSheets;
        if (!_db.Database.IsNpgsql()) return read(sheets, default);
        var owner = _owner?.Owns(_db) == true ? _owner : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
        return owner.ExecuteAsync(OperationsOwnerV1.FinCost, "view", false, async (access, ct) =>
        {
            var rates = await access.SourceAsync(OperationsOwnerV1.ProcessCostRate, ct);
            rates.RequireUnmaskedRates();
            var scope = _financeScope ?? new ManufacturingFinanceScopeV1(_db, null);
            await scope.GateAsync(false, ct);
            var visible = scope.CostSheetsInScope(sheets.AsNoTracking(), access.ActorId);
            return await read(rates.CostSheetsWithReadableRates(visible), ct);
        }, readOnlyAuthorityFence: true);
    }
}
