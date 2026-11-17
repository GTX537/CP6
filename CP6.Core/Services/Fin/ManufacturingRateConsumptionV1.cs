using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

internal sealed class ManufacturingRateConsumptionV1(CP6Context db, CostPolicySourcesV1? policySources = null)
{
    internal sealed record Selection(Guid LedgerId, DateTime Date, string Currency, IQueryable<ExecutionRate> Rates,
        CostCollectionBasisV1.RateUseV1[] Evidence, Guid? CenterId,
        Dictionary<Guid, ManufacturingCenterUseV1.Attribution> ManagedCenters,
        Dictionary<Guid, CostCollectionBasisV1.RateUseV1> RatesByStep);

    internal async Task<Selection> SelectAsync(WorkOrder order, IQueryable<WorkOrderProcess> steps, int stepCount,
        LedgerRow ledger, OperationsAccessV1 rateAccess, CancellationToken ct, string requestedClass = "ACTUAL")
    {
        var route = await (from step in steps
            join center in db.WorkCenters.AsNoTracking().Where(center => !center.IsDeleted) on step.WgCd equals center.WgCd
            join profile in db.ProductProcesses.AsNoTracking().Where(profile => profile.ProductCd == order.ProductCd && !profile.IsDeleted)
                on new { step.ProcessCd, step.TaskCd } equals new { profile.ProcessCd, profile.TaskCd }
            select new { StepId = step.Id, Code = step.WgCd, WorkCenterId = center.Id, OperationId = profile.Id }).ToArrayAsync(ct);
        if (route.Length != stepCount || route.Select(row => row.StepId).Distinct().Count() != stepCount)
            throw Error("OPERATION_PROFILE_UNAVAILABLE", 409);

        var day = ledger.BusinessDate;
        var centers = route.Select(row => row.WorkCenterId).Distinct().ToArray();
        var operations = route.Select(row => row.OperationId).Distinct().ToArray();
        var candidates = await db.Database.SqlQuery<PublishedRate>($"""
            SELECT r.id AS "RateId",r.work_center_id AS "WorkCenterId",r.operation_id AS "OperationId",
                r.center_id AS "CenterId",r.center_version_id AS "CenterVersionId",r.policy_version_id AS "PolicyVersionId",
                r.basis_id AS "BasisId",r.maker AS "Maker",(v.submission_id IS NOT NULL) AS "ManagedCenter",
                r.labor_rate AS "LaborRate",r.overhead_rate AS "OverheadRate",r.valid_from AS "From",r.to_exclusive AS "ToExclusive",
                (r.state='RELEASED' AND p.enabled AND b.enabled AND (v.operation_id IS NULL OR v.operation_id=r.operation_id)
                 AND p.currency={ledger.Currency}
                 AND v.effective_dates @> {day}
                 AND daterange(p.valid_from,p.to_exclusive,'[)') @> {day}
                 AND daterange(b.valid_from,b.to_exclusive,'[)') @> {day}
                 AND NOT EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.center_calendar alternate
                     WHERE alternate.tenant_id=v.tenant_id AND alternate.ledger_id=v.ledger_id AND alternate.root_id=v.root_id
                       AND alternate.version_id<>v.version_id AND alternate.enabled
                       AND alternate.effective_dates @> {day})) AS "Eligible",
                (p.currency_scale=2 AND p.rounding='HALF_UP' AND p.rate_date_basis='APPROVED_ACCOUNTING_DATE'
                 AND p.valuation='STANDARD_OUTPUT_WITH_VARIANCE' AND p.variance='VAR_TO_EXPENSE') AS "PolicySupported"
            FROM cp6_fin_mfg_v1.timeline t
            JOIN cp6_fin_mfg_v1.rate_versions r ON (r.tenant_id,r.ledger_id,r.id)=(t.tenant_id,t.ledger_id,t.rate_id)
            JOIN cp6_fin_mfg_v1.policies p ON (p.tenant_id,p.ledger_id,p.version_id)=(r.tenant_id,r.ledger_id,r.policy_version_id)
            JOIN cp6_fin_mfg_v1.rate_bases b ON (b.tenant_id,b.ledger_id,b.id,b.policy_version_id,b.work_center_id,b.operation_id)
                =(r.tenant_id,r.ledger_id,r.basis_id,r.policy_version_id,r.work_center_id,r.operation_id)
            JOIN cp6_fin_mfg_v1.current_center_versions v ON (v.tenant_id,v.ledger_id,v.root_id,v.version_id)
                =(r.tenant_id,r.ledger_id,r.center_id,r.center_version_id)
            WHERE t.tenant_id={db.CurrentTenantId} AND t.ledger_id={ledger.Id}
              AND t.work_center_id=ANY({centers}) AND t.operation_id=ANY({operations})
              AND daterange(t.valid_from,t.to_exclusive,'[)') @> {day}
            """).ToArrayAsync(ct);

        var execution = new List<ExecutionRate>(route.Length);
        var evidence = new Dictionary<Guid, CostCollectionBasisV1.RateUseV1>();
        var managedCenters = new Dictionary<Guid, ManufacturingCenterUseV1.Attribution>();
        var ratesByStep = new Dictionary<Guid, CostCollectionBasisV1.RateUseV1>();
        var policies = new Dictionary<Guid, CostPolicyAdoptionV1?>();
        foreach (var entry in route)
        {
            var matched = candidates.Where(row => row.WorkCenterId == entry.WorkCenterId && row.OperationId == entry.OperationId).ToArray();
            if (matched.Length != 1 || !matched[0].Eligible)
                throw Error("PUBLISHED_RATE_UNAVAILABLE", 409);
            var rate = matched[0];
            await rateAccess.RequirePublishedRateCenterAsync(rate.WorkCenterId, rate.Maker, ct);
            if (!rate.PolicySupported)
                throw Error("COST_POLICY_ADOPTION_REQUIRED", 409);
            if (!policies.TryGetValue(rate.PolicyVersionId, out var policy))
            {
                policy = await new CostPolicyOriginalV1(db, policySources).AdoptAsync(ledger, rate.PolicyVersionId,
                    rateAccess, requestedClass, ct);
                policies.Add(rate.PolicyVersionId, policy);
            }
            // The current A2 unit-price column is numeric(21,8). Keep the full released rate in its own store.
            if (rate.LaborRate > 9999999999999.99999999m || rate.OverheadRate > 9999999999999.99999999m)
                throw Error("COST_VALUE_OUT_OF_RANGE", 422);

            var from = rate.From.ToDateTime(TimeOnly.MinValue);
            execution.Add(new() { StepId = entry.StepId, LaborRate = rate.LaborRate, OverheadRate = rate.OverheadRate, ValidFrom = from });
            if (rate.ManagedCenter)
                managedCenters.Add(entry.StepId, new(rate.RateId, rate.CenterId, rate.CenterVersionId, rate.OperationId));
            var use = new CostCollectionBasisV1.RateUseV1(rate.RateId, rate.WorkCenterId, entry.Code, from,
                rate.ToExclusive?.ToDateTime(TimeOnly.MinValue), rate.LaborRate, rate.OverheadRate, null, ledger.Id,
                rate.CenterId, rate.CenterVersionId, rate.PolicyVersionId, rate.BasisId, rate.OperationId, rate.Maker, policy);
            ratesByStep.Add(entry.StepId, use);
            evidence.TryAdd(rate.RateId, use);
        }

        var ids = evidence.Values.Select(row => row.CostCenterId).Distinct().ToArray();
        var json = JsonSerializer.Serialize(execution);
        var rates = db.Database.SqlQuery<ExecutionRate>($"""
            SELECT * FROM jsonb_to_recordset(CAST({json} AS jsonb)) AS result
                ("StepId" uuid, "LaborRate" numeric, "OverheadRate" numeric, "ValidFrom" timestamp without time zone)
            """);
        return new(ledger.Id, day.ToDateTime(TimeOnly.MinValue), ledger.Currency, rates,
            evidence.Values.OrderBy(row => row.RateId).ToArray(), ids.Length == 1 ? ids[0] : null, managedCenters, ratesByStep);
    }

    internal sealed class ExecutionRate
    {
        public Guid StepId { get; set; }
        public decimal LaborRate { get; set; }
        public decimal OverheadRate { get; set; }
        public DateTime ValidFrom { get; set; }
    }

    internal sealed class PublishedRate
    {
        public Guid RateId { get; set; }
        public Guid WorkCenterId { get; set; }
        public Guid OperationId { get; set; }
        public Guid CenterId { get; set; }
        public Guid CenterVersionId { get; set; }
        public Guid PolicyVersionId { get; set; }
        public Guid BasisId { get; set; }
        public Guid Maker { get; set; }
        public decimal LaborRate { get; set; }
        public decimal OverheadRate { get; set; }
        public DateOnly From { get; set; }
        public DateOnly? ToExclusive { get; set; }
        public bool Eligible { get; set; }
        public bool PolicySupported { get; set; }
        public bool ManagedCenter { get; set; }
    }
}
