using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    private bool AllowsOpaqueSlottingSource => authority.Scope == 5 && authority.Fields.Fields.Values.All(f => f.CanRead);
    internal IQueryable<SlottingPlan> SlottingPlans()
    {
        var rows = db.SlottingPlans.AsNoTracking().Where(x => !x.IsDeleted);
        if (authority.Scope == 1) rows = rows.Where(x => x.Creator == Actor);
        else if (authority.Scope != 5)
        {
            var ids = StandaloneDepartmentScopeV1.Objects(db, authority, "slotting-plan");
            rows = rows.Where(x => ids.Contains(x.Id));
        }
        rows = rows.Where(x => db.Warehouses.Any(w => !w.IsDeleted && w.WarehouseCd == x.WarehouseCd));
        if (!authority.Actor.RoleIds.Contains(1))
        {
            var grants = db.WmsRoleScopes.Where(g => authority.Actor.RoleIds.Contains(g.RoleId));
            rows = rows.Where(x => grants.Any(g => g.WarehouseCd == "*" || g.WarehouseCd == x.WarehouseCd));
        }
        return rows;
    }
    internal async Task<HashSet<string>> SlottingSnapshotWarehouses(CancellationToken ct)
    {
        // Opaque historical warehouse aggregates have no per-row source identity. A partial current
        // set cannot prove historical disclosure. Require complete source scope and fields instead.
        if (!authority.Fields.Fields["RecommendationsJson"].CanRead) return [];
        foreach (var resource in new[] { OperationsOwnerV1.Stock, OperationsOwnerV1.Outbound, OperationsOwnerV1.Mobile })
        {
            OperationsAccessV1 source;
            try { source = await SourceAsync(resource, ct); }
            catch (OrderPriceCorrectionException e) when (e.Message == "ACTION_FORBIDDEN") { return []; }
            if (!source.AllowsOpaqueSlottingSource) return [];
        }
        var warehouses = db.Warehouses.AsNoTracking().Where(w => !w.IsDeleted);
        if (!authority.Actor.RoleIds.Contains(1))
        {
            var grants = db.WmsRoleScopes.Where(g => authority.Actor.RoleIds.Contains(g.RoleId));
            warehouses = warehouses.Where(w => grants.Any(g => (g.WarehouseCd == "*" || g.WarehouseCd == w.WarehouseCd)
                && (g.AreaCd == null || g.AreaCd == "" || g.AreaCd == "*")));
        }
        return (await warehouses.Select(w => w.WarehouseCd).ToArrayAsync(ct)).ToHashSet(StringComparer.Ordinal);
    }
    internal SlottingObservationV1 ProjectSlotting(SlottingPlan row, HashSet<string> snapshots)
    {
        var readable = snapshots.Contains(row.WarehouseCd);
        var state = "NOT_DISCLOSED";
        if (readable)
        {
            state = "NOT_RECORDED";
            if (row.RecommendationsJson != null)
            {
                try
                {
                    using var document = JsonDocument.Parse(row.RecommendationsJson);
                    state = document.RootElement.ValueKind == JsonValueKind.Array ? "JSON_ARRAY" : "LEGACY_PARSE_ERROR";
                }
                catch (JsonException) { state = "LEGACY_PARSE_ERROR"; }
            }
        }
        bool Read(string f) => authority.Fields.Fields[f].CanRead;
        return new() { Id = row.Id, SlottingPlanNo = row.SlottingPlanNo, WarehouseCd = row.WarehouseCd,
            AnalysisDays = row.AnalysisDays, LegacyStatus = row.Status, AnalyzedAt = row.AnalyzedAt, CreateDate = row.CreateDate,
            SnapshotReadable = readable, SnapshotState = readable ? "AUTHORIZED_LEGACY_SNAPSHOT" : "SOURCE_DISCLOSURE_UNAVAILABLE",
            TxnSampleCount = readable ? row.TxnSampleCount : null, RecommendationCount = readable ? row.RecommendationCount : null,
            RecommendationsJson = readable ? row.RecommendationsJson : null, JsonState = state,
            ApproverCdReadable = Read("ApproverCd"), RemarksReadable = Read("Remarks"),
            ApproverCd = Read("ApproverCd") ? row.ApproverCd : null, Remarks = Read("Remarks") ? row.Remarks : null };
    }
}
