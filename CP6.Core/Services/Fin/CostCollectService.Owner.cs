using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

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
        if (!await product.Products().AnyAsync(row => row.ProductCd == order.ProductCd, ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
        var rates = await access.SourceAsync(OperationsOwnerV1.ProcessCostRate, ct);
        rates.RequireUnmaskedRates();
        var date = order.ActualEndDate ?? order.PlanStartDate ?? order.CreateDate;
        var visibleRates = rates.ProcessCostRates();
        var candidates = await _db.ProcessCostRates.AsNoTracking()
            .Where(row => !row.IsDeleted && centers.Contains(row.WgCd) && row.ValidFrom <= date
                && (row.ValidTo == null || row.ValidTo >= date))
            .OrderByDescending(row => row.ValidFrom)
            .Select(row => new { row.Id, row.WgCd, Visible = visibleRates.Any(visible => visible.Id == row.Id) })
            .ToArrayAsync(ct);
        // 先选工单基准日实际应采用的费率，再核该记录的范围。不可见的费率不能改用另一条。
        var selectedRates = candidates.GroupBy(row => row.WgCd).Select(group => group.First()).ToArray();
        if (selectedRates.Any(row => !row.Visible))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
        if (selectedRates.Length != centers.Distinct().Count())
            return FinResult.Fail("E-A2-RATE-002");

        var selectedIds = selectedRates.Select(row => row.Id).ToArray();
        List<CostSheetLine> lines;
        try
        {
            var steps = await ReadExecutionCostsAsync(order, workSteps, selectedIds, ct);
            if (steps.Length != centers.Length)
                return FinResult.Fail("E-A2-COST-004");
            var materials = await ReadMaterialCostsAsync(order, ct);
            if (materials.Any(row => !row.HasPrice && (row.Line.PlanQty != 0m || row.Line.ActualQty != 0m)))
                return FinResult.Fail("COST_MATERIAL_PRICE_REQUIRED");
            lines = materials.Select(row => row.Line).ToList();
            foreach (var (labor, overhead) in steps)
            {
                lines.Add(labor);
                lines.Add(overhead);
            }
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
        return await SaveCollectionAsync(order, sheet, lines, access.Actor, ct);
    }

    private async Task<(CostSheetLine Labor, CostSheetLine Overhead)[]> ReadExecutionCostsAsync(WorkOrder order,
        IQueryable<WorkOrderProcess> workSteps, Guid[] rateIds, CancellationToken ct)
    {
        var routing = _db.ProductProcesses.AsNoTracking().Where(row => !row.IsDeleted && row.ProductCd == order.ProductCd);
        var rates = _db.ProcessCostRates.AsNoTracking().Where(row => rateIds.Contains(row.Id));
        // 工时乘法和行金额舍入在 PostgreSQL numeric 上计算，先保留完整乘积再舍入。
        var steps = await (from step in workSteps
            join standard in routing on new { step.ProcessCd, step.TaskCd } equals new { standard.ProcessCd, standard.TaskCd }
            join rate in rates on step.WgCd equals rate.WgCd
            let stdMachine = (standard.SetupHour ?? 0m) + order.CompletedQty * (standard.CycleTime ?? 0m)
            let stdLabor = stdMachine * (standard.StandardCrewSize ?? 1m)
            let machine = step.ActualMachineHour ?? stdMachine
            let labor = step.ActualLaborHour ?? stdLabor
            orderby step.SortOrder, step.ProcessCd, step.TaskCd
            select new
            {
                Labor = new CostSheetLine
                {
                    Element = CostElement.Labor, ProcessCd = step.ProcessCd, TaskCd = step.TaskCd, WgCd = step.WgCd,
                    Hours = Math.Round(labor, 8), StandardHours = Math.Round(stdLabor, 8), UnitPrice = rate.LaborRate,
                    ActualAmount = Math.Round(labor * rate.LaborRate, 2), StandardAmount = Math.Round(stdLabor * rate.LaborRate, 2),
                    RateValidFrom = rate.ValidFrom, HourSource = step.ActualLaborHour == null ? "StandardFallback" : step.ActualHourSource ?? "Derived",
                    WarningCode = step.ActualLaborHour == null ? "W-A2-COST-001" : null,
                    CalcNote = step.ActualLaborHour == null ? "实绩人工工时缺失，按标准工时计" : null,
                },
                Overhead = new CostSheetLine
                {
                    Element = CostElement.Overhead, ProcessCd = step.ProcessCd, TaskCd = step.TaskCd, WgCd = step.WgCd,
                    Hours = Math.Round(machine, 8), StandardHours = Math.Round(stdMachine, 8), UnitPrice = rate.OverheadRate,
                    ActualAmount = Math.Round(machine * rate.OverheadRate, 2), StandardAmount = Math.Round(stdMachine * rate.OverheadRate, 2),
                    RateValidFrom = rate.ValidFrom, HourSource = step.ActualMachineHour == null ? "StandardFallback" : step.ActualHourSource ?? "Derived",
                    WarningCode = step.ActualMachineHour == null ? "W-A2-COST-001" : null,
                    CalcNote = step.ActualMachineHour == null ? "实绩机时缺失，按标准工时计" : null,
                },
            }).ToArrayAsync(ct);
        return steps.Select(step => (step.Labor, step.Overhead)).ToArray();
    }

    private async Task<(CostSheetLine Line, bool HasPrice)[]> ReadMaterialCostsAsync(WorkOrder order, CancellationToken ct)
    {
        var bom = _db.ProductMaterials.AsNoTracking().Where(row => !row.IsDeleted && row.ProductCd == order.ProductCd);
        var rows = await (from material in _db.WorkOrderMaterials.AsNoTracking()
            where material.WorkOrderNo == order.WorkOrderNo && !material.IsDeleted
            join source in bom on new { material.ProcessCd, material.MaterialCd } equals new { source.ProcessCd, source.MaterialCd } into matches
            from source in matches.DefaultIfEmpty()
            let price = source.SupplyPrice ?? 0m
            let planned = material.PlanQty ?? 0m
            orderby material.SortOrder, material.ProcessCd, material.MaterialCd
            select new
            {
                HasPrice = source.SupplyPrice != null,
                Line = new CostSheetLine
                {
                    Element = CostElement.Material, ProcessCd = material.ProcessCd, MaterialCd = material.MaterialCd,
                    MaterialName = material.MaterialName, PlanQty = planned, ActualQty = material.ActualQty, UnitPrice = price,
                    ActualAmount = Math.Round(material.ActualQty * price, 2), StandardAmount = Math.Round(planned * price, 2),
                },
            }).ToArrayAsync(ct);
        return rows.Select(row => (row.Line, row.HasPrice)).ToArray();
    }
}
