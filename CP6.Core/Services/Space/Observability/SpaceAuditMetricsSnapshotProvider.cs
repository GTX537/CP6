using CP6.Core.EFDbContext;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space.Observability;

/// <summary>
/// Aggregates the Space audit ledger across all tenants for operational
/// metrics. Metrics transport concerns remain in the WebApi layer.
/// </summary>
public sealed class SpaceAuditMetricsSnapshotProvider :
    ISpaceAuditMetricsSnapshotProvider
{
    private static readonly string[] FixedOutcomes =
    [
        SpaceAuditOutcome.Started,
        SpaceAuditOutcome.Succeeded,
        SpaceAuditOutcome.Failed,
        SpaceAuditOutcome.Denied,
    ];

    private readonly CP6Context _db;

    public SpaceAuditMetricsSnapshotProvider(CP6Context db)
    {
        _db = db;
    }

    public async Task<SpaceAuditMetricsSnapshot> GetSnapshotAsync(
        CancellationToken ct = default)
    {
        var groups = await _db.SpaceAuditEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .GroupBy(audit => audit.Outcome)
            .Select(group => new OutcomeCount(
                group.Key,
                group.LongCount()))
            .ToListAsync(ct);

        var byOutcome = FixedOutcomes.ToDictionary(
            outcome => outcome,
            _ => 0L,
            StringComparer.Ordinal);
        var total = 0L;

        foreach (var group in groups)
        {
            total = checked(total + group.Count);
            if (group.Outcome is not null &&
                byOutcome.ContainsKey(group.Outcome))
                byOutcome[group.Outcome] = group.Count;
        }

        return new SpaceAuditMetricsSnapshot(total, byOutcome);
    }

    private sealed record OutcomeCount(string? Outcome, long Count);
}
