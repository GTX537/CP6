using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

// Exact inputs and results of the current A2 calculation. Each successful collection appends one version.
internal sealed record CostCollectionBasisV1(string PolicyVersion, DateTime RateDate,
    CostCollectionBasisV1.CostAmountsV1 Amounts, CostCollectionBasisV1.RateUseV1[] Rates, Guid? LedgerId = null,
    StandardBasisReferenceV1? StandardBasis = null)
{
    internal const string Policy = "CP6.A2.v1";
    internal const int MoneyScale = 2;

    internal sealed record RateUseV1(Guid RateId, Guid WorkCenterId, string WorkCenter, DateTime ValidFrom,
        DateTime? ValidTo, decimal LaborRate, decimal OverheadRate, byte[]? SourceRowVersion, Guid? LedgerId = null,
        Guid? CostCenterId = null, Guid? CostCenterVersionId = null, Guid? PolicyVersionId = null, Guid? RateBasisId = null,
        Guid? OperationProfileId = null, Guid? RateMaker = null)
    {
        internal static RateUseV1 Capture(ProcessCostRate row, Guid centerId)
            => new(row.Id, centerId, row.WgCd, row.ValidFrom, row.ValidTo, row.LaborRate, row.OverheadRate, row.RowVersion);
    }

    internal sealed record CostAmountsV1(decimal CompletedQty, decimal MaterialActual, decimal MaterialStandard,
        decimal LaborActual, decimal LaborStandard, decimal OverheadActual, decimal OverheadStandard)
    {
        internal static CostAmountsV1 Capture(CostSheet sheet)
            => new(sheet.CompletedQty, sheet.MaterialActual, sheet.MaterialStandard, sheet.LaborActual, sheet.LaborStandard,
                sheet.OverheadActual, sheet.OverheadStandard);

        internal decimal TotalActual => MaterialActual + LaborActual + OverheadActual;
        internal decimal StandardCost => MaterialStandard + LaborStandard + OverheadStandard;
        internal decimal FinishedGoods => StandardCost > 0m ? StandardCost : TotalActual;
        internal decimal VarianceToExpense => StandardCost > 0m ? TotalActual - StandardCost : 0m;
    }
}

internal static class CostCollectionBasisStoreV1
{
    internal sealed record Version(long Number, CostCollectionBasisV1 Basis);

    internal static Task<long> NextAsync(CP6Context db, Guid sheetId, CancellationToken ct)
        => db.Database.SqlQuery<long>($"""
            SELECT COALESCE(MAX(version),0)+1 AS "Value" FROM cp6_fin_cost_basis_v1.versions
            WHERE tenant_id={db.CurrentTenantId} AND cost_sheet_id={sheetId}
            """).SingleAsync(ct);

    internal static async Task AppendAsync(CP6Context db, CostSheet sheet, DateTime date,
        CostCollectionBasisV1.RateUseV1[] rates, Guid? ledgerId, long? explicitVersion, Guid? centerUseEffect,
        StandardBasisReferenceV1? standardBasis, CancellationToken ct)
    {
        var basis = new CostCollectionBasisV1(CostCollectionBasisV1.Policy, date,
            CostCollectionBasisV1.CostAmountsV1.Capture(sheet), rates, ledgerId, standardBasis);
        var json = JsonSerializer.Serialize(basis);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_cost_basis_v1.versions (tenant_id, cost_sheet_id, version, basis, center_use_effect_id, standard_basis_id)
            SELECT {db.CurrentTenantId}, {sheet.Id}, COALESCE({explicitVersion},COALESCE(MAX(version),0)+1),
                CAST({json} AS jsonb),{centerUseEffect},{standardBasis?.VersionId}
            FROM cp6_fin_cost_basis_v1.versions
            WHERE tenant_id={db.CurrentTenantId} AND cost_sheet_id={sheet.Id}
            """, ct);
        if (inserted != 1)
            throw new OrderPriceCorrectionException("COST_COLLECTION_VERSION_NOT_WRITTEN", 409);
    }

    internal static Task<Version?> ReadLatestAsync(CP6Context db, Guid sheetId)
        => ReadAsync(db, sheetId, null, default);

    internal static async Task<Version?> ReadAsync(CP6Context db, Guid sheetId, long? number, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<BasisRow>($"""
            SELECT version AS "Number", basis::text AS "Json"
            FROM cp6_fin_cost_basis_v1.versions
            WHERE tenant_id={db.CurrentTenantId} AND cost_sheet_id={sheetId}
                AND version=COALESCE({number}, version)
            ORDER BY version DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);
        return row is null ? null : new(row.Number, JsonSerializer.Deserialize<CostCollectionBasisV1>(row.Json)!);
    }

    internal static Task<Guid[]> SheetsWithApprovedStandardAsync(CP6Context db, Guid[] sheets, CancellationToken ct)
        => db.Database.SqlQuery<Guid>($"""
            SELECT cost_sheet_id AS "Value" FROM (
                SELECT DISTINCT ON (cost_sheet_id) cost_sheet_id, standard_basis_id
                FROM cp6_fin_cost_basis_v1.versions
                WHERE tenant_id={db.CurrentTenantId} AND cost_sheet_id=ANY({sheets})
                ORDER BY cost_sheet_id, version DESC
            ) latest WHERE standard_basis_id IS NOT NULL
            """).ToArrayAsync(ct);

    internal static IQueryable<Guid> VersionedSheetIds(CP6Context db) => db.Database.SqlQuery<Guid>($"""
        SELECT DISTINCT cost_sheet_id AS "Value"
        FROM cp6_fin_cost_basis_v1.versions WHERE tenant_id={db.CurrentTenantId}
        """);

    internal static IQueryable<RateIdentity> CurrentRates(CP6Context db) => db.Database.SqlQuery<RateIdentity>($"""
        SELECT cost_sheet_id AS "CostSheetId", rate_id AS "RateId", work_center_id AS "WorkCenterId",
            work_center AS "WorkCenter", ledger_id AS "LedgerId", rate_maker AS "RateMaker"
        FROM cp6_fin_cost_basis_v1.current_rates WHERE tenant_id={db.CurrentTenantId}
        """);

    internal static Task BindSettlementAsync(CP6Context db, Guid sheetId, long version,
        Guid? wip, Guid? finished, Guid? variance, string actor)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_cost_basis_v1.settlements
                (tenant_id, cost_sheet_id, basis_version, wip_journal_id, fg_journal_id, variance_journal_id, actor)
            VALUES ({db.CurrentTenantId}, {sheetId}, {version}, {wip}, {finished}, {variance}, {actor})
            """);

    internal sealed class BasisRow
    {
        public long Number { get; set; }
        public string Json { get; set; } = null!;
    }

    internal sealed class RateIdentity
    {
        public Guid CostSheetId { get; set; }
        public Guid RateId { get; set; }
        public Guid WorkCenterId { get; set; }
        public string WorkCenter { get; set; } = null!;
        public Guid? LedgerId { get; set; }
        public Guid? RateMaker { get; set; }
    }
}
