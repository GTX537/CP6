using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

public class BridgeHealthService : IBridgeHealthService
{
    private readonly CP6Context _db;

    public BridgeHealthService(CP6Context db)
    {
        _db = db;
    }

    public async Task<BridgeHealthMetricsDto> GetMetricsAsync(DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var windowEnd = nowUtc ?? DateTime.UtcNow;
        var windowStart = windowEnd.AddHours(-24);

        var windowEvents = await _db.IntegrationEvents
            .AsNoTracking()
            .Where(e => !e.IsDeleted && e.CreateDate >= windowStart && e.CreateDate <= windowEnd)
            .ToListAsync(ct);

        var hooks = windowEvents
            .GroupBy(e => new { e.HookName, e.SourceModule, e.TargetModule })
            .Select(g =>
            {
                var success = g.Count(e => e.Status == IntegrationEventStatus.Success);
                var total = g.Count();
                return new BridgeHookStatsDto
                {
                    HookName = g.Key.HookName,
                    SourceModule = g.Key.SourceModule,
                    TargetModule = g.Key.TargetModule,
                    TotalCount = total,
                    SuccessCount = success,
                    SkippedCount = g.Count(e => e.Status == IntegrationEventStatus.Skipped),
                    FailedCount = g.Count(e => e.Status == IntegrationEventStatus.Failed),
                    DeadLetterCount = g.Count(e => e.Status == IntegrationEventStatus.DeadLetter),
                    SuccessRate = total == 0 ? 0m : Math.Round(success / (decimal)total, 4),
                };
            })
            .OrderByDescending(h => h.DeadLetterCount)
            .ThenByDescending(h => h.FailedCount)
            .ThenBy(h => h.HookName)
            .ToList();

        var queueDepth = await _db.IntegrationEvents
            .AsNoTracking()
            .CountAsync(e => !e.IsDeleted && e.Status == IntegrationEventStatus.Failed, ct);

        var deadLetterQuery = _db.IntegrationEvents
            .AsNoTracking()
            .Where(e => !e.IsDeleted && e.Status == IntegrationEventStatus.DeadLetter);

        var deadLetterCount = await deadLetterQuery.CountAsync(ct);
        var deadLetters = await deadLetterQuery
            .OrderByDescending(e => e.CreateDate)
            .ThenByDescending(e => e.Id)
            .Take(10)
            .Select(e => new DeadLetterItemDto
            {
                EventId = e.Id,
                HookName = e.HookName,
                SourceModule = e.SourceModule,
                TargetModule = e.TargetModule,
                SourceNo = e.SourceNo,
                TargetNo = e.TargetNo,
                Attempts = e.Attempts,
                LastError = e.LastError,
                CreateDate = e.CreateDate,
            })
            .ToListAsync(ct);

        return new BridgeHealthMetricsDto
        {
            WindowStartUtc = windowStart,
            WindowEndUtc = windowEnd,
            Hooks = hooks,
            QueueDepth = queueDepth,
            DeadLetterCount = deadLetterCount,
            DeadLetters = deadLetters,
        };
    }

    public async Task<bool> CompensateAsync(Guid eventId, string? userName = null, CancellationToken ct = default)
    {
        var evt = await _db.IntegrationEvents
            .FirstOrDefaultAsync(e => e.Id == eventId && !e.IsDeleted && e.Status == IntegrationEventStatus.DeadLetter, ct);
        if (evt == null)
        {
            return false;
        }

        evt.Status = IntegrationEventStatus.Compensated;
        evt.Modifier = userName;
        evt.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
