using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class SlottingService
{
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private static OrderPriceCorrectionException Error(string code, int status) => new(code, status);
    private Task<T> LegacyRead<T>() => Owner.ExecuteAsync<T>(OperationsOwnerV1.Slotting, "query", false,
        (_, _) => throw Error("OWNER_READ_VERSION_REQUIRED", 409));
    public Task<List<SlottingPlan>> SearchAsync(string? warehouseCd, int? status) => !_db.Database.IsNpgsql() ? SearchCoreAsync(warehouseCd, status) : LegacyRead<List<SlottingPlan>>();
    public Task<SlottingPlanResult?> GetAsync(string planNo) => !_db.Database.IsNpgsql() ? GetCoreAsync(planNo) : LegacyRead<SlottingPlanResult?>();
    private Task<T> RejectUnadopted<T>(string action, string? no = null) => Owner.ExecuteAsync<T>(OperationsOwnerV1.Slotting, action, false, async (a, ct) =>
    {
        if (no != null && !await a.SlottingPlans().AnyAsync(x => x.SlottingPlanNo == no, ct)) throw Error("OBJECT_NOT_FOUND", 404);
        // Old approval/cancellation does not prove complete task-family closure or physical outcome.
        throw Error("SLOTTING_OWNER_ADOPTION_UNPROVEN", 503);
    });
    public Task<string> AnalyzeAsync(string warehouseCd, int analysisDays, string? userName) => !_db.Database.IsNpgsql() ? AnalyzeCoreAsync(warehouseCd, analysisDays, userName) : RejectUnadopted<string>("analyze");
    public Task<int> ApproveAsync(string planNo, string? userName) => !_db.Database.IsNpgsql() ? ApproveCoreAsync(planNo, userName) : RejectUnadopted<int>("approve", planNo);
    public Task CancelAsync(string planNo, string? userName) => !_db.Database.IsNpgsql() ? CancelCoreAsync(planNo, userName) : RejectUnadopted<bool>("cancel", planNo);
    public Task<SlottingPageV1> SearchOwnerAsync(SlottingQueryV1 input)
    {
        var q = OperationsOwnerV1.Freeze(input);
        return Owner.ExecuteAsync(OperationsOwnerV1.Slotting, "query", false, async (a, ct) =>
        {
            if (q.Page < 1 || q.PageSize < 1 || q.PageSize > 200 || (long)(q.Page - 1) * q.PageSize > int.MaxValue
                || q.Status is not (null or 0 or 1 or 2 or 9) || q.WarehouseCd?.Length > 10) throw Error("SLOTTING_QUERY_INVALID", 400);
            var rows = a.SlottingPlans();
            if (!string.IsNullOrWhiteSpace(q.WarehouseCd)) rows = rows.Where(x => x.WarehouseCd == q.WarehouseCd);
            if (q.Status.HasValue) rows = rows.Where(x => x.Status == q.Status);
            var total = await rows.CountAsync(ct);
            var items = await rows.OrderByDescending(x => x.AnalyzedAt).ThenBy(x => x.Id).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToArrayAsync(ct);
            var snapshots = await a.SlottingSnapshotWarehouses(ct);
            return new SlottingPageV1 { Total = total, Page = q.Page, PageSize = q.PageSize, Items = items.Select(x => a.ProjectSlotting(x, snapshots)).ToList() };
        });
    }
    public Task<SlottingObservationV1?> GetOwnerAsync(string no) => Owner.ExecuteAsync<SlottingObservationV1?>(OperationsOwnerV1.Slotting, "query", false, async (a, ct) =>
    {
        if (string.IsNullOrWhiteSpace(no) || no.Length > 20) throw Error("SLOTTING_QUERY_INVALID", 400);
        var row = await a.SlottingPlans().SingleOrDefaultAsync(x => x.SlottingPlanNo == no, ct);
        return row == null ? null : a.ProjectSlotting(row, await a.SlottingSnapshotWarehouses(ct));
    });
}
