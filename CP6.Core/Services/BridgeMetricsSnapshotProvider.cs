using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services;

/// <inheritdoc />
public class BridgeMetricsSnapshotProvider : IBridgeMetricsSnapshotProvider
{
    private readonly CP6Context _db;

    public BridgeMetricsSnapshotProvider(CP6Context db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<BridgeMetricsSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var grouped = await _db.IntegrationEvents
            .AsNoTracking()
            .Where(e => !e.IsDeleted)
            .GroupBy(e => new { e.HookName, e.Status })
            .Select(g => new BridgeHookStatusCount
            {
                Hook = g.Key.HookName,
                Status = g.Key.Status,
                Count = g.Count(),
            })
            .ToListAsync(ct);

        return new BridgeMetricsSnapshot
        {
            HookStatusCounts = grouped,
            RetryQueueDepth = grouped
                .Where(x => x.Status == IntegrationEventStatus.Failed)
                .Sum(x => x.Count),
            DeadLetterCount = grouped
                .Where(x => x.Status == IntegrationEventStatus.DeadLetter)
                .Sum(x => x.Count),
        };
    }
}
