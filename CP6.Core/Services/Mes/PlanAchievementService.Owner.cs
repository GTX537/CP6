using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

public partial class PlanAchievementService
{
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner
        : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);

    public Task<PlanAchievementSummaryDto> GetSummaryAsync(PlanAchievementQuery query, CancellationToken ct = default)
    {
        if (!_db.Database.IsNpgsql()) return GetSummaryCoreAsync(query, ct);
        var frozen = OperationsOwnerV1.Freeze(query ?? new());
        return Owner.ExecuteAsync(OperationsOwnerV1.Achievement, "query", false,
            async (a, token) => await GetSummaryCoreAsync(frozen, token, await a.PlanningOrders(token)), ct);
    }

    public Task<byte[]> ExportCsvAsync(PlanAchievementQuery query, CancellationToken ct = default)
    {
        if (!_db.Database.IsNpgsql()) return ExportCsvCoreAsync(query, ct);
        var frozen = OperationsOwnerV1.Freeze(query ?? new());
        return Owner.ExecuteAsync(OperationsOwnerV1.Achievement, "export", false, async (a, token) =>
        {
            await a.SourceAsync(OperationsOwnerV1.Achievement, token); // Export does not invent query authority.
            return await ExportCsvCoreAsync(frozen, token, await a.PlanningOrders(token));
        }, ct);
    }
}
