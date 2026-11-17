using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

internal sealed class ManufacturingStandardBasisSourcesV1(CP6Context db, CostPolicySourcesV1? policySources = null)
{
    internal async Task<WorkOrder> RequireOrderAsync(Guid ledger, Guid id, OperationsAccessV1 access, CancellationToken ct)
        => await (await OrdersInLedgerAsync(ledger, access, ct)).SingleOrDefaultAsync(row => row.Id == id, ct)
            ?? throw Error("STANDARD_BASIS_WORK_ORDER_NOT_FOUND", 404);

    internal async Task<IQueryable<WorkOrder>> OrdersInLedgerAsync(Guid ledger, OperationsAccessV1 access, CancellationToken ct)
    {
        var assignments = db.Database.SqlQuery<Guid>($"""
            SELECT work_order_id AS "Value" FROM cp6_fin_mfg_v1.work_orders
            WHERE tenant_id={db.CurrentTenantId} AND ledger_id={ledger}
            """);
        var orders = await access.SourceAsync(OperationsOwnerV1.Mes, ct);
        return orders.WorkOrders().Where(row => assignments.Contains(row.Id));
    }

    internal async Task<StandardBasisBodyV1> BuildAsync(WorkOrder order, LedgerRow ledger, OperationsAccessV1 access,
        StandardBasisSubmissionV1 command, CancellationToken ct)
    {
        var target = StandardBasisBodyV1.Quantity(command.TargetQuantity);
        if (target <= 0m)
            throw Error("STANDARD_BASIS_QUANTITY_INVALID", 422);
        var products = await access.SourceAsync(OperationsOwnerV1.Product, ct);
        var product = await products.Products().SingleOrDefaultAsync(row => row.ProductCd == order.ProductCd, ct)
            ?? throw Error("STANDARD_BASIS_OUTPUT_NOT_FOUND", 404);
        if (string.IsNullOrWhiteSpace(product.QtyUnit) || product.QtyUnit != command.OutputUnit)
            throw Error("STANDARD_BASIS_UNIT_MISMATCH", 422);

        var rates = await access.SourceAsync(OperationsOwnerV1.ProcessCostRate, ct);
        rates.RequireUnmaskedRates();
        var workSteps = db.WorkOrderProcesses.AsNoTracking().Where(row => row.WorkOrderNo == order.WorkOrderNo && !row.IsDeleted);
        var stepCount = await workSteps.CountAsync(ct);
        if (stepCount == 0)
            throw Error("STANDARD_BASIS_SCOPE_MISMATCH", 422);
        var selected = await new ManufacturingRateConsumptionV1(db, policySources).SelectAsync(order, workSteps, stepCount, ledger, rates, ct, "STANDARD");
        if (selected.Evidence.Any(rate => rate.PolicyVersionId != command.PolicyVersionId))
            throw Error("STANDARD_BASIS_POLICY_MISMATCH", 422);

        var useZero = command.ZeroBasisReference != null && command.Materials.Length == 0 && command.Times.Length == 0;
        var materials = BuildMap(command.Materials, item => item.MaterialOccurrenceId);
        var times = BuildMap(command.Times, item => item.WorkStepId);
        if (!useZero && !times.Keys.ToHashSet().SetEquals(selected.RatesByStep.Keys))
            throw Error("STANDARD_BASIS_SCOPE_MISMATCH", 422);
        var pricedMaterials = await (from item in db.WorkOrderMaterials.AsNoTracking()
            where item.WorkOrderNo == order.WorkOrderNo && !item.IsDeleted
            join price in db.ProductMaterials.AsNoTracking().Where(row => row.ProductCd == order.ProductCd && !row.IsDeleted)
                on new { item.ProcessCd, item.MaterialCd } equals new { price.ProcessCd, price.MaterialCd } into joined
            from price in joined.DefaultIfEmpty()
            select new { OccurrenceId = item.Id, item.Unit, PriceUnit = price.UsageUnit, PriceId = (Guid?)price.Id,
                Price = price.SupplyPrice, PriceVersion = price.RowVersion }).ToArrayAsync(ct);
        if (pricedMaterials.Select(item => item.OccurrenceId).Distinct().Count() != pricedMaterials.Length
            || !useZero && !materials.Keys.ToHashSet().SetEquals(pricedMaterials.Select(item => item.OccurrenceId)))
            throw Error("STANDARD_BASIS_SCOPE_MISMATCH", 422);

        var inputs = new List<StandardBasisLineV1>();
        foreach (var material in pricedMaterials.OrderBy(item => item.OccurrenceId))
        {
            var input = useZero ? null : materials[material.OccurrenceId];
            if (string.IsNullOrWhiteSpace(material.Unit) || material.Unit != material.PriceUnit
                || input != null && material.Unit != input.Unit)
                throw Error("STANDARD_BASIS_UNIT_MISMATCH", 422);
            if (material.Price is not >= 0m || material.PriceId == null || material.PriceVersion == null)
                throw Error("STANDARD_BASIS_MATERIAL_PRICE_REQUIRED", 422);
            inputs.Add(new(material.OccurrenceId, CostElement.Material, material.Unit,
                input == null ? 0m : StandardBasisBodyV1.Quantity(input.AllowedQuantity), material.Price.Value,
                0m, material.PriceId.Value, material.PriceVersion));
        }
        foreach (var (stepId, rate) in selected.RatesByStep.OrderBy(row => row.Key))
        {
            var input = useZero ? null : times[stepId];
            inputs.Add(new(stepId, CostElement.Labor, "PERSON_HOUR",
                input == null ? 0m : StandardBasisBodyV1.Quantity(input.LaborHours),
                rate.LaborRate, 0m, rate.RateId, OperationId: rate.OperationProfileId,
                WorkCenterId: rate.WorkCenterId, RateMaker: rate.RateMaker, WorkCenter: rate.WorkCenter));
            inputs.Add(new(stepId, CostElement.Overhead, "MACHINE_HOUR",
                input == null ? 0m : StandardBasisBodyV1.Quantity(input.MachineHours),
                rate.OverheadRate, 0m, rate.RateId, OperationId: rate.OperationProfileId,
                WorkCenterId: rate.WorkCenterId, RateMaker: rate.RateMaker, WorkCenter: rate.WorkCenter));
        }
        if (inputs.Count > 1000)
            throw Error("DEPENDENCY_LIMIT", 422);

        var lines = await new ManufacturingStandardBasisStoreV1(db).PriceAsync(inputs.ToArray(), ct);
        decimal total;
        try { total = lines.Sum(line => line.Amount); }
        catch (OverflowException) { throw Error("STANDARD_BASIS_AMOUNT_OUT_OF_RANGE", 422); }
        if (total > 99999999999999999999.99999999m)
            throw Error("STANDARD_BASIS_AMOUNT_OUT_OF_RANGE", 422);
        if ((total == 0m) != (command.ZeroBasisReference != null))
            throw Error("STANDARD_BASIS_ZERO_APPROVAL_REQUIRED", 422);
        return new(product.Id, product.QtyUnit, target, ledger.Currency, lines, total, command.ZeroBasisReference);
    }

    internal async Task RequirePolicyAsync(Guid ledger, Guid policy, string currency, DateOnly from, DateOnly? toExclusive,
        CancellationToken ct)
    {
        if (!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.policies p
                WHERE (p.tenant_id,p.ledger_id,p.version_id)=({db.CurrentTenantId},{ledger},{policy})
                    AND p.enabled AND p.currency={currency} AND p.currency_scale=2 AND p.rounding='HALF_UP'
                    AND p.rate_date_basis='APPROVED_ACCOUNTING_DATE' AND p.valuation='STANDARD_OUTPUT_WITH_VARIANCE'
                    AND p.variance='VAR_TO_EXPENSE'
                    AND daterange(p.valid_from,p.to_exclusive,'[)') @> daterange({from},{toExclusive},'[)')) AS "Value"
            """).SingleAsync(ct))
            throw Error("STANDARD_BASIS_POLICY_UNAVAILABLE", 409);
    }

    internal static async Task RequireOriginalRatesAsync(OperationsAccessV1 rates, StandardBasisBodyV1 body, CancellationToken ct)
    {
        rates.RequireUnmaskedRates();
        foreach (var rate in body.Lines.Where(line => line.WorkCenterId.HasValue).DistinctBy(line => line.SourceId))
            await rates.RequirePublishedRateCenterAsync(rate.WorkCenterId!.Value, rate.RateMaker!.Value, ct);
    }

    internal static async Task<(StandardBasisBodyV1 Body, string ProductCode, string? CurrentUnit)> ReadVisibleBasisAsync(
        ManufacturingStandardBasisStoreV1.Row row, OperationsAccessV1 access, CancellationToken ct)
    {
        var body = row.ReadBody();
        var products = await access.SourceAsync(OperationsOwnerV1.Product, ct);
        var product = await products.Products().Where(product => product.Id == body.ProductId)
            .Select(product => new { product.ProductCd, product.QtyUnit }).SingleOrDefaultAsync(ct)
            ?? throw Error("STANDARD_BASIS_OUTPUT_NOT_FOUND", 404);
        var rates = await access.SourceAsync(OperationsOwnerV1.ProcessCostRate, ct);
        await RequireOriginalRatesAsync(rates, body, ct);
        return (body, product.ProductCd, product.QtyUnit);
    }

    private static Dictionary<Guid, T> BuildMap<T>(T[] items, Func<T, Guid> key) where T : class
    {
        var map = new Dictionary<Guid, T>();
        foreach (var item in items)
            if (item == null || !map.TryAdd(key(item), item))
                throw Error("STANDARD_BASIS_SCOPE_MISMATCH", 422);
        return map;
    }
}
