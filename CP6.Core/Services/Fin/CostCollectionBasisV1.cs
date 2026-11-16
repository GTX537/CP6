using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

// Exact inputs and results of the current A2 calculation. Each successful collection appends one version.
internal sealed record CostCollectionBasisV1(string PolicyVersion, DateTime RateDate,
    CostCollectionBasisV1.CostAmountsV1 Amounts, CostCollectionBasisV1.RateUseV1[] Rates)
{
    internal const string Policy = "CP6.A2.v1";
    internal const int MoneyScale = 2;

    internal sealed record RateUseV1(Guid RateId, Guid WorkCenterId, string WorkCenter, DateTime ValidFrom,
        DateTime? ValidTo, decimal LaborRate, decimal OverheadRate, byte[]? SourceRowVersion)
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

    internal static Task AppendAsync(CP6Context db, CostSheet sheet, DateTime date,
        CostCollectionBasisV1.RateUseV1[] rates, CancellationToken ct)
    {
        var basis = new CostCollectionBasisV1(CostCollectionBasisV1.Policy, date,
            CostCollectionBasisV1.CostAmountsV1.Capture(sheet), rates);
        var json = JsonSerializer.Serialize(basis);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_cost_basis_v1.versions (tenant_id, cost_sheet_id, version, basis)
            SELECT {db.CurrentTenantId}, {sheet.Id}, COALESCE(MAX(version),0)+1, CAST({json} AS jsonb)
            FROM cp6_fin_cost_basis_v1.versions
            WHERE tenant_id={db.CurrentTenantId} AND cost_sheet_id={sheet.Id}
            """, ct);
    }

    internal static async Task<Version?> ReadLatestAsync(CP6Context db, Guid sheetId)
    {
        var row = await db.Database.SqlQuery<BasisRow>($"""
            SELECT version AS "Number", basis::text AS "Json"
            FROM cp6_fin_cost_basis_v1.versions
            WHERE tenant_id={db.CurrentTenantId} AND cost_sheet_id={sheetId}
            ORDER BY version DESC LIMIT 1
            """).SingleOrDefaultAsync();
        return row is null ? null : new(row.Number, JsonSerializer.Deserialize<CostCollectionBasisV1>(row.Json)!);
    }

    internal static IQueryable<Guid> VersionedSheetIds(CP6Context db) => db.Database.SqlQuery<Guid>($"""
        SELECT DISTINCT cost_sheet_id AS "Value"
        FROM cp6_fin_cost_basis_v1.versions WHERE tenant_id={db.CurrentTenantId}
        """);

    internal static IQueryable<RateIdentity> CurrentRates(CP6Context db) => db.Database.SqlQuery<RateIdentity>($"""
        SELECT cost_sheet_id AS "CostSheetId", rate_id AS "RateId", work_center_id AS "WorkCenterId", work_center AS "WorkCenter"
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
    }
}
