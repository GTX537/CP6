using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

public partial class PlanningBoardService
{
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner
        : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);

    private static PlanningBoardQuery FreezeQuery(PlanningBoardQuery query)
    {
        var result = OperationsOwnerV1.Freeze(query);
        if (result.DateFrom is DateTime from) result.DateFrom = DateTime.SpecifyKind(from, DateTimeKind.Unspecified);
        if (result.DateTo is DateTime to) result.DateTo = DateTime.SpecifyKind(to, DateTimeKind.Unspecified);
        return result;
    }

    public Task<List<PlanningBarDto>> GetBarsAsync(PlanningBoardQuery query)
    {
        if (!_db.Database.IsNpgsql()) return GetBarsCoreAsync(query);
        var frozen = FreezeQuery(query);
        return Owner.ExecuteAsync(OperationsOwnerV1.Planning, "query", false,
            async (a, ct) => await GetBarsCoreAsync(frozen, await a.PlanningOrders(ct), ct));
    }

    public async Task<PlanningKpiDto> GetKpiAsync(PlanningBoardQuery query)
        => !_db.Database.IsNpgsql() ? await GetKpiCoreAsync(query) : (await CaptureAsync(query)).Kpi;

    /// <summary>A single current-authority read of the legacy board; not a persisted formal SCHED BoardCapture.</summary>
    public async Task<PlanningBoardCaptureDto> CaptureAsync(PlanningBoardQuery query)
    {
        if (!_db.Database.IsNpgsql()) return new() { Bars = await GetBarsCoreAsync(query), Kpi = await GetKpiCoreAsync(query),
            Mode = "LEGACY_COMPATIBILITY", CanApplySchedule = true, ReadAt = DateTimeOffset.UtcNow };
        var frozen = FreezeQuery(query);
        return await Owner.ExecuteAsync(OperationsOwnerV1.Planning, "query", false, async (a, ct) =>
        {
            var orders = await a.PlanningOrders(ct);
            var bars = await GetBarsCoreAsync(frozen, orders, ct);
            var numbers = bars.Select(b => b.WorkOrderNo).Distinct().ToArray();
            var parents = await orders.Where(w => numbers.Contains(w.WorkOrderNo) && w.Status != 9)
                .Select(w => new { w.WorkOrderNo, w.Status, w.PlanEndDate }).ToListAsync(ct);
            var included = parents.Select(w => w.WorkOrderNo).ToHashSet();
            var processes = bars.Where(p => included.Contains(p.WorkOrderNo)).ToArray();
            var today = DateTime.Today;
            var delayed = parents.Where(w => w.PlanEndDate < today && w.Status != 4 && w.Status != 6).ToArray();
            return new PlanningBoardCaptureDto
            {
                ReadAt = DateTimeOffset.UtcNow, Bars = bars,
                Kpi = new() { TotalOrders = parents.Count, InProgressOrders = parents.Count(w => w.Status == 3),
                    CompletedOrders = parents.Count(w => w.Status == 4 || w.Status == 6), DelayedOrders = delayed.Length,
                    AvgDelayDays = delayed.Length == 0 ? 0m : Math.Round(delayed.Average(w => (decimal)(today - w.PlanEndDate!.Value.Date).Days), 1),
                    CompletionRate = processes.Length == 0 ? 0m : Math.Round(100m * processes.Count(p => p.ProcessStatus == 2) / processes.Length, 2) }
            };
        });
    }

    public Task RescheduleAsync(RescheduleRequest request, string? userName)
    {
        if (!_db.Database.IsNpgsql()) return RescheduleCoreAsync(request, userName);
        var frozen = OperationsOwnerV1.Freeze(request);
        return Owner.ExecuteAsync<bool>(OperationsOwnerV1.Planning, "reschedule", false, async (a, ct) =>
        {
            var orders = await a.PlanningOrders(ct);
            if (!await _db.WorkOrderProcesses.AnyAsync(p => p.Id == frozen.Id && !p.IsDeleted
                && orders.Any(w => w.WorkOrderNo == p.WorkOrderNo), ct)) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
            // Accepted SCHED R2: missing Snapshot/Occurrence is LEGACY_READ_ONLY. A process status or
            // machine code cannot stand in for the complete Root/WO/RES/EXEC adoption and common UoW.
            throw new OrderPriceCorrectionException("SCHED_OWNER_PROTOCOL_NOT_READY", 503);
        });
    }

    public Task<int> AutoArrangeAsync(AutoArrangeRequest request, string? userName)
    {
        if (!_db.Database.IsNpgsql()) return AutoArrangeCoreAsync(request, userName);
        return Owner.ExecuteAsync<int>(OperationsOwnerV1.Planning, "arrange", false, async (a, ct) =>
        {
            await a.PlanningOrders(ct);
            throw new OrderPriceCorrectionException("SCHED_OWNER_PROTOCOL_NOT_READY", 503);
        });
    }
}
