using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;
using ExecutionRate = CP6.Core.Services.Fin.ManufacturingRateConsumptionV1.ExecutionRate;

namespace CP6.Core.Services.Fin;

public partial class CostCollectService
{
    private const decimal MaxCostAmount = 9999999999999999.99m;
    private const decimal MaxCostHours = 9999999999999.99999999m;

    private static bool AmountFits(decimal amount) => amount is >= -MaxCostAmount and <= MaxCostAmount;

    internal bool Owns(CP6Context context) => ReferenceEquals(_db, context) && _owner?.Owns(context) == true
        && _seq is FinSequenceService sequence && sequence.Owns(context);

    private Task<FinResult> CollectOwnedAsync(string workOrderNo)
    {
        if (!Owns(_db))
            throw new OrderPriceCorrectionException("FINANCE_OWNER_REQUIRED", 503);

        return _owner!.ExecuteAsync(OperationsOwnerV1.FinCost, "collect", true,
            (access, ct) => CollectWithinOwnerAsync(workOrderNo, access, ct));
    }

    internal async Task<FinResult> CollectWithinOwnerAsync(string workOrderNo, OperationsAccessV1 access, CancellationToken ct)
    {
        var workOrders = await access.SourceAsync(OperationsOwnerV1.Mes, ct);
        var order = await workOrders.WorkOrders().SingleOrDefaultAsync(row => row.WorkOrderNo == workOrderNo, ct)
            ?? throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);

        var existing = await _db.CostSheets.Include(row => row.Lines)
            .Where(row => row.WorkOrderNo == order.WorkOrderNo).Take(2).ToArrayAsync(ct);
        if (existing.Length > 1)
            throw new OrderPriceCorrectionException("COST_SHEET_CONFLICT", 409);
        var sheet = existing.SingleOrDefault();
        if (sheet?.Status == CostSheetStatus.Settled)
            return FinResult.Fail("E-FIN-402");

        var workSteps = _db.WorkOrderProcesses.AsNoTracking()
            .Where(row => row.WorkOrderNo == order.WorkOrderNo && !row.IsDeleted);
        var centers = await workSteps.Select(row => row.WgCd).ToArrayAsync(ct);
        if (centers.Length == 0)
            return FinResult.Fail("E-A2-COST-001");

        var product = await access.SourceAsync(OperationsOwnerV1.Product, ct);
        var output = await product.Products().Where(row => row.ProductCd == order.ProductCd)
            .Select(row => new { row.Id, row.QtyUnit }).SingleOrDefaultAsync(ct)
            ?? throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
        var rates = await access.SourceAsync(OperationsOwnerV1.ProcessCostRate, ct);
        rates.RequireUnmaskedRates();
        var date = order.ActualEndDate ?? order.PlanStartDate ?? order.CreateDate;
        var financeScope = _financeScope ?? new ManufacturingFinanceScopeV1(_db, null);
        await financeScope.GateAsync(false, ct);
        var ledger = await financeScope.AssignedLedgerAsync(order.Id, access.ActorId, ct);
        var published = ledger == null ? null : await new ManufacturingRateConsumptionV1(_db, _policySources)
            .SelectAsync(order, workSteps, centers.Length, ledger, rates, ct);
        var comparison = published == null ? null : await ManufacturingStandardBasisComparisonV1.SelectAsync(_db, order,
            output.Id, output.QtyUnit, published, access, rates, ct);
        IQueryable<ExecutionRate> executionRates;
        CostCollectionBasisV1.RateUseV1[] evidence;
        if (published != null)
        {
            if (sheet != null && (await CostCollectionBasisStoreV1.ReadLatestAsync(_db, sheet.Id))?.Basis.LedgerId != published.LedgerId)
                return FinResult.Fail("COST_SCOPE_CUTOVER_REQUIRED");
            date = published.Date;
            executionRates = published.Rates;
            evidence = published.Evidence;
        }
        else
        {
            var selectedRates = await ProcessCostRateService.SelectEffectiveAsync(_db, centers, date, rates, ct);
            if (selectedRates.Any(row => !row.Visible))
                throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
            if (selectedRates.Any(row => row.Ambiguous))
                throw new OrderPriceCorrectionException("RATE_PERIOD_CONFLICT", 409);
            if (selectedRates.Length != centers.Distinct().Count())
                return FinResult.Fail("E-A2-RATE-002");
            var ids = selectedRates.Select(row => row.Rate.Id).ToArray();
            executionRates = from step in workSteps
                join rate in _db.ProcessCostRates.AsNoTracking().Where(row => ids.Contains(row.Id)) on step.WgCd equals rate.WgCd
                select new ExecutionRate
                {
                    StepId = step.Id, LaborRate = rate.LaborRate, OverheadRate = rate.OverheadRate, ValidFrom = rate.ValidFrom,
                };
            evidence = selectedRates.OrderBy(row => row.Rate.WgCd, StringComparer.Ordinal)
                .Select(row => CostCollectionBasisV1.RateUseV1.Capture(row.Rate, row.WorkCenterId)).ToArray();
        }
        List<CostSheetLine> lines;
        var centerItems = new List<ManufacturingCenterUseV1.Item>();
        try
        {
            var steps = await ReadExecutionCostsAsync(order, workSteps, executionRates, comparison != null, ct);
            if (steps.Length != centers.Length)
                return FinResult.Fail("E-A2-COST-004");
            if (evidence.Any(rate => rate.FullPolicy != null) && steps.Any(step => !step.HasActualHours))
                return FinResult.Fail("COST_ACTUAL_HOURS_REQUIRED");
            var materials = await ReadMaterialCostsAsync(order, comparison != null, ct);
            if (materials.Any(row => !row.HasPrice && (row.Line.PlanQty != 0m || row.Line.ActualQty != 0m)))
                return FinResult.Fail("COST_MATERIAL_PRICE_REQUIRED");
            lines = materials.Select(row => row.Line).ToList();
            if (comparison != null)
                foreach (var (occurrence, unit, priceUnit, line, _) in materials)
                {
                    if (line.ActualQty != 0m && unit != priceUnit)
                        throw new OrderPriceCorrectionException("STANDARD_BASIS_UNIT_MISMATCH", 409);
                    comparison.ApplyMaterial(occurrence, unit, line);
                }
            foreach (var (stepId, hasActualHours, labor, overhead) in steps)
            {
                if (comparison != null)
                {
                    if (!hasActualHours) return FinResult.Fail("STANDARD_BASIS_ACTUAL_HOURS_REQUIRED");
                    var actualRate = published!.RatesByStep[stepId];
                    comparison.ApplyTime(stepId, actualRate, labor);
                    comparison.ApplyTime(stepId, actualRate, overhead);
                }
                if (published?.ManagedCenters.TryGetValue(stepId, out var attribution) == true)
                {
                    labor.Id = Guid.NewGuid();
                    overhead.Id = Guid.NewGuid();
                    centerItems.Add(attribution.For(labor.Id));
                    centerItems.Add(attribution.For(overhead.Id));
                }
                lines.Add(labor);
                lines.Add(overhead);
            }
            comparison?.RequireComplete();
        }
        catch (OverflowException)
        {
            // 超出已采用的数值表示时在任何成本写入前拒绝。
            return FinResult.Fail("COST_VALUE_OUT_OF_RANGE");
        }
        if (lines.Any(line => !AmountFits(line.ActualAmount) || !AmountFits(line.StandardAmount)
            || line.Hours is > MaxCostHours or < -MaxCostHours
            || line.StandardHours is > MaxCostHours or < -MaxCostHours))
            return FinResult.Fail("COST_VALUE_OUT_OF_RANGE");

        for (var index = 0; index < lines.Count; index++)
            lines[index].LineNo = index + 1;
        var centerUse = published != null && centerItems.Count != 0
            ? new ManufacturingCenterUseV1.Use(published.LedgerId, access.ActorId, DateOnly.FromDateTime(date),
                published.Currency, centerItems.ToArray()) : null;
        return await SaveCollectionAsync(order, sheet, lines, access.Actor, ct,
            (date, evidence, published?.LedgerId, published?.CenterId), centerUse, comparison?.Reference);
    }

    private async Task<(Guid StepId, bool HasActualHours, CostSheetLine Labor, CostSheetLine Overhead)[]> ReadExecutionCostsAsync(WorkOrder order,
        IQueryable<WorkOrderProcess> workSteps, IQueryable<ExecutionRate> rates, bool approvedStandard, CancellationToken ct)
    {
        var routing = _db.ProductProcesses.AsNoTracking().Where(row => !row.IsDeleted && row.ProductCd == order.ProductCd);
        // 工时乘法和行金额舍入在 PostgreSQL numeric 上计算，先保留完整乘积再舍入。
        var steps = await (from step in workSteps
            join standard in routing on new { step.ProcessCd, step.TaskCd } equals new { standard.ProcessCd, standard.TaskCd }
            join rate in rates on step.Id equals rate.StepId
            let stdMachine = approvedStandard ? 0m : (standard.SetupHour ?? 0m) + order.CompletedQty * (standard.CycleTime ?? 0m)
            let stdLabor = stdMachine * (standard.StandardCrewSize ?? 1m)
            let machine = step.ActualMachineHour ?? stdMachine
            let labor = step.ActualLaborHour ?? stdLabor
            orderby step.SortOrder, step.ProcessCd, step.TaskCd
            select new
            {
                StepId = step.Id,
                HasActualHours = step.ActualLaborHour != null && step.ActualMachineHour != null,
                Labor = new CostSheetLine
                {
                    Element = CostElement.Labor, ProcessCd = step.ProcessCd, TaskCd = step.TaskCd, WgCd = step.WgCd,
                    Hours = Math.Round(labor, 8), StandardHours = Math.Round(stdLabor, 8), UnitPrice = rate.LaborRate,
                    ActualAmount = Math.Round(labor * rate.LaborRate, CostCollectionBasisV1.MoneyScale),
                    StandardAmount = Math.Round(stdLabor * rate.LaborRate, CostCollectionBasisV1.MoneyScale),
                    RateValidFrom = rate.ValidFrom, HourSource = step.ActualLaborHour == null ? "StandardFallback" : step.ActualHourSource ?? "Derived",
                    WarningCode = step.ActualLaborHour == null ? "W-A2-COST-001" : null,
                    CalcNote = step.ActualLaborHour == null ? "实绩人工工时缺失，按标准工时计" : null,
                },
                Overhead = new CostSheetLine
                {
                    Element = CostElement.Overhead, ProcessCd = step.ProcessCd, TaskCd = step.TaskCd, WgCd = step.WgCd,
                    Hours = Math.Round(machine, 8), StandardHours = Math.Round(stdMachine, 8), UnitPrice = rate.OverheadRate,
                    ActualAmount = Math.Round(machine * rate.OverheadRate, CostCollectionBasisV1.MoneyScale),
                    StandardAmount = Math.Round(stdMachine * rate.OverheadRate, CostCollectionBasisV1.MoneyScale),
                    RateValidFrom = rate.ValidFrom, HourSource = step.ActualMachineHour == null ? "StandardFallback" : step.ActualHourSource ?? "Derived",
                    WarningCode = step.ActualMachineHour == null ? "W-A2-COST-001" : null,
                    CalcNote = step.ActualMachineHour == null ? "实绩机时缺失，按标准工时计" : null,
                },
            }).ToArrayAsync(ct);
        return steps.Select(step => (step.StepId, step.HasActualHours, step.Labor, step.Overhead)).ToArray();
    }

    private async Task<(Guid Occurrence, string? Unit, string? PriceUnit, CostSheetLine Line, bool HasPrice)[]>
        ReadMaterialCostsAsync(WorkOrder order, bool approvedStandard, CancellationToken ct)
    {
        var bom = _db.ProductMaterials.AsNoTracking().Where(row => !row.IsDeleted && row.ProductCd == order.ProductCd);
        var rows = await (from material in _db.WorkOrderMaterials.AsNoTracking()
            where material.WorkOrderNo == order.WorkOrderNo && !material.IsDeleted
            join source in bom on new { material.ProcessCd, material.MaterialCd } equals new { source.ProcessCd, source.MaterialCd } into matches
            from source in matches.DefaultIfEmpty()
            let price = source.SupplyPrice ?? 0m
            let planned = approvedStandard ? 0m : material.PlanQty ?? 0m
            orderby material.SortOrder, material.ProcessCd, material.MaterialCd
            select new
            {
                Occurrence = material.Id, material.Unit, PriceUnit = source.UsageUnit,
                HasPrice = source.SupplyPrice != null,
                Line = new CostSheetLine
                {
                    Element = CostElement.Material, ProcessCd = material.ProcessCd, MaterialCd = material.MaterialCd,
                    MaterialName = material.MaterialName, PlanQty = planned, ActualQty = material.ActualQty, UnitPrice = price,
                    ActualAmount = Math.Round(material.ActualQty * price, CostCollectionBasisV1.MoneyScale),
                    StandardAmount = Math.Round(planned * price, CostCollectionBasisV1.MoneyScale),
                },
            }).ToArrayAsync(ct);
        return rows.Select(row => (row.Occurrence, row.Unit, row.PriceUnit, row.Line, row.HasPrice)).ToArray();
    }
}
