using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;
using static CP6.Core.Services.Fin.StandardBasisBodyV1;

namespace CP6.Core.Services.Fin;

public sealed class ManufacturingCostMaintenanceV1(CP6Context db, OperationsOwnerV1 owner, ManufacturingFinanceScopeV1 scope)
{
    private const int PageSize = 100;

    public Task<object> RatesAsync(Guid context, string? search, DateOnly? businessDate, CancellationToken ct)
        => scope.WithContextAsync<object>(owner, context, "view", false, async (ledger, access, token) =>
        {
            var keyword = search?.Trim() ?? string.Empty;
            if (keyword.Length > 100)
                throw Error("COST_MAINTENANCE_FILTER_INVALID", 422);
            var date = businessDate ?? ledger.BusinessDate;
            var rateAccess = await access.SourceAsync(OperationsOwnerV1.ProcessCostRate, token);
            rateAccess.RequireUnmaskedRates();
            var productAccess = await access.SourceAsync(OperationsOwnerV1.Product, token);
            var workCenters = rateAccess.PublishedRateCenters();
            var operations = from step in db.ProductProcesses.AsNoTracking()
                             join product in productAccess.Products() on step.ProductCd equals product.ProductCd
                             where !step.IsDeleted
                             select step;
            var mine = rateAccess.ActorId;
            var ownRates = rateAccess.PublishedRatesOwnedOnly;
            var versions = await (from rate in new ManufacturingRateStoreV1(db).Versions(ledger.Id)
                join center in workCenters on rate.WorkCenterId equals center.Id
                join step in operations on rate.OperationId equals step.Id
                where (!ownRates || rate.Maker == mine) && (center.WgCd.Contains(keyword) || step.ProductCd.Contains(keyword)
                    || rate.Id.ToString().Contains(keyword))
                orderby rate.From descending, rate.Id
                select new { Rate = rate, center.WgCd, step.ProductCd, step.ProcessCd, step.TaskCd }).Take(PageSize + 1).ToArrayAsync(token);
            var basisRows = db.Database.SqlQuery<RateBasisChoice>($"""
                SELECT b.id AS "Id",b.work_center_id AS "WorkCenterId",b.operation_id AS "OperationId",
                    b.policy_version_id AS "PolicyVersionId", (p.allow_zero_rate AND b.zero_evidenced) AS "ZeroAllowed"
                FROM cp6_fin_mfg_v1.rate_bases b JOIN cp6_fin_mfg_v1.policies p
                    ON (b.tenant_id,b.ledger_id,b.policy_version_id)=(p.tenant_id,p.ledger_id,p.version_id)
                WHERE b.tenant_id={db.CurrentTenantId} AND b.ledger_id={ledger.Id} AND b.enabled AND p.enabled AND p.currency={ledger.Currency}
                    AND daterange(b.valid_from,b.to_exclusive,'[)') @> {date}
                    AND daterange(p.valid_from,p.to_exclusive,'[)') @> {date}
                """);
            var choices = await (from basis in basisRows
                join center in workCenters on basis.WorkCenterId equals center.Id
                join step in operations on basis.OperationId equals step.Id
                where center.WgCd.Contains(keyword) || step.ProductCd.Contains(keyword)
                orderby center.WgCd, step.ProductCd, step.TaskCd, basis.Id
                select new { basis.Id, basis.WorkCenterId, basis.OperationId, basis.PolicyVersionId, basis.ZeroAllowed,
                    center.WgCd, step.ProductCd, step.ProcessCd, step.TaskCd }).Take(PageSize + 1).ToArrayAsync(token);
            var centers = await scope.ListCentersAsync(ledger.Id, access.ActorId, date, token);
            var centerVersions = centers.Select(center => center.Locator.VersionId).ToArray();
            var centerOperations = await db.Database.SqlQuery<CenterOperation>($"""
                SELECT version_id AS "VersionId",operation_id AS "OperationId"
                FROM cp6_fin_mfg_v1.current_center_versions
                WHERE tenant_id={db.CurrentTenantId} AND ledger_id={ledger.Id} AND version_id=ANY({centerVersions})
                """).ToDictionaryAsync(row => row.VersionId, row => row.OperationId, token);
            return new
            {
                ledger.Currency, businessDate = date,
                truncated = versions.Length > PageSize || choices.Length > PageSize,
                centers = centers.Select(center => new { center.Locator, center.Code, center.Name,
                    operationId = centerOperations[center.Locator.VersionId] }),
                choices = choices.Take(PageSize),
                versions = versions.Take(PageSize).Select(item => new
                {
                    rateId = item.Rate.Id, item.Rate.State, item.Rate.Revision, item.WgCd, item.ProductCd, item.ProcessCd, item.TaskCd,
                    item.Rate.WorkCenterId, item.Rate.OperationId, item.Rate.CenterId, item.Rate.CenterVersionId,
                    item.Rate.From, item.Rate.ToExclusive, laborRate = DecimalText(item.Rate.LaborRate),
                    overheadRate = DecimalText(item.Rate.OverheadRate), madeByCurrentUser = item.Rate.Maker == mine,
                }),
            };
        }, ct);

    public Task<object> WorkOrderAsync(Guid context, string workOrderNo, CancellationToken ct)
        => scope.WithContextAsync<object>(owner, context, "view", false, async (ledger, access, token) =>
        {
            await access.ActionAsync(OperationsOwnerV1.FinCost, "basis-view", token);
            if (string.IsNullOrWhiteSpace(workOrderNo) || workOrderNo.Length > 20)
                throw Error("COST_MAINTENANCE_FILTER_INVALID", 422);
            var sources = new ManufacturingStandardBasisSourcesV1(db);
            var orders = await sources.OrdersInLedgerAsync(ledger.Id, access, token);
            var order = await orders.SingleOrDefaultAsync(row => row.WorkOrderNo == workOrderNo, token)
                ?? throw Error("STANDARD_BASIS_WORK_ORDER_NOT_FOUND", 404);
            var productAccess = await access.SourceAsync(OperationsOwnerV1.Product, token);
            var product = await productAccess.Products().Where(row => row.ProductCd == order.ProductCd)
                .Select(row => new { row.Id, row.QtyUnit }).SingleOrDefaultAsync(token)
                ?? throw Error("STANDARD_BASIS_OUTPUT_NOT_FOUND", 404);

            var rateAccess = await access.SourceAsync(OperationsOwnerV1.ProcessCostRate, token);
            rateAccess.RequireUnmaskedRates();
            var centers = (await rateAccess.PublishedRateCenters().Select(center => center.Id).ToArrayAsync(token)).ToHashSet();
            var ownOnly = rateAccess.PublishedRatesOwnedOnly;
            var actualActor = access.ActorId;

            bool Visible(ManufacturingStandardBasisStoreV1.Row row)
            {
                var body = row.ReadBody();
                return body.ProductId == product.Id && body.Lines.All(line => line.WorkCenterId == null
                    || centers.Contains(line.WorkCenterId.Value) && (!ownOnly || line.RateMaker == actualActor));
            }

            var bases = new ManufacturingStandardBasisStoreV1(db);
            var versions = await bases.Versions(ledger.Id).Where(row => row.WorkOrderId == order.Id)
                .OrderByDescending(row => row.Selected).ThenByDescending(row => row.Sequence).Take(PageSize).ToArrayAsync(token);
            var documents = new ManufacturingStandardDocumentStoreV1(db);
            var document = await documents.FindByWorkOrderAsync(ledger.Id, order.Id, token);
            if (document?.Version is long number)
            {
                var edition = await documents.RequireEditionAsync(ledger.Id, document.Id, number, token);
                if (!Visible(await bases.ReadAsync(ledger.Id, edition.StandardBasisId, token)))
                    document = null;
            }

            var steps = db.WorkOrderProcesses.AsNoTracking().Where(row => row.WorkOrderNo == order.WorkOrderNo && !row.IsDeleted);
            var stepInputs = await steps.OrderBy(row => row.SortOrder).Select(row => new
                { row.Id, row.ProcessCd, row.TaskCd, row.WgCd }).ToArrayAsync(token);
            Guid? policyVersionId = null;
            string? sourceIssue = null;
            if (stepInputs.Length > 0)
            {
                try
                {
                    var selected = await new ManufacturingRateConsumptionV1(db).SelectAsync(order, steps, stepInputs.Length, ledger, rateAccess, token);
                    var policies = selected.Evidence.Select(row => row.PolicyVersionId).Distinct().ToArray();
                    if (policies.Length == 1)
                        policyVersionId = policies[0];
                    else
                        sourceIssue = "STANDARD_BASIS_POLICY_MISMATCH";
                }
                catch (OrderPriceCorrectionException error) when (error.Status is 403 or 409 or 422)
                {
                    sourceIssue = error.Message;
                }
            }
            else
                sourceIssue = "OPERATION_PROFILE_UNAVAILABLE";

            var materials = await db.WorkOrderMaterials.AsNoTracking().Where(row => row.WorkOrderNo == order.WorkOrderNo && !row.IsDeleted)
                .OrderBy(row => row.SortOrder).Select(row => new { row.Id, row.MaterialCd, row.MaterialName, row.Unit }).ToArrayAsync(token);
            var head = versions.FirstOrDefault(row => row.Selected);
            return new
            {
                workOrderId = order.Id, order.WorkOrderNo, order.ProductCd, outputUnit = product.QtyUnit,
                completedQuantity = DecimalText(order.CompletedQty), ledger.BusinessDate, ledger.Currency,
                policyVersionId, sourceIssue, materials, steps = stepInputs,
                basisInputsAvailable = policyVersionId.HasValue && (head == null || Visible(head)),
                basisVersions = versions.Where(Visible).Select(row => new
                {
                    versionId = row.Id, sequence = StandardDocumentBinding.VersionText(row.Sequence), row.State, row.Revision, row.Selected,
                    row.From, row.ToExclusive, madeByCurrentUser = row.Maker == actualActor,
                }),
                document = document == null ? null : new { document.Id, version = StandardDocumentBinding.VersionText(document.Version) },
            };
        }, ct);

    internal sealed class RateBasisChoice
    {
        public Guid Id { get; set; }
        public Guid WorkCenterId { get; set; }
        public Guid OperationId { get; set; }
        public Guid PolicyVersionId { get; set; }
        public bool ZeroAllowed { get; set; }
    }

    internal sealed class CenterOperation
    {
        public Guid VersionId { get; set; }
        public Guid? OperationId { get; set; }
    }
}
