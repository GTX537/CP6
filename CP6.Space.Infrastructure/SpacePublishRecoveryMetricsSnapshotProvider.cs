using CP6.Space.Application;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

/// <summary>
/// Aggregates active publish recovery states across tenants without exposing
/// tenant, site, version, or attempt labels. The append-only publish audit
/// ledger supplies the state-entry timestamp; StartedAtUtc is a conservative
/// fallback for legacy rows that predate the matching audit event.
/// </summary>
public sealed class SpacePublishRecoveryMetricsSnapshotProvider(
    SpaceContext context,
    ISpaceClock clock) : ISpacePublishRecoveryMetricsSnapshotProvider
{
    public async Task<SpacePublishRecoveryMetricsSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var trackedStatuses = new[]
        {
            SpacePublishAttemptStatus.WaitingRetry,
            SpacePublishAttemptStatus.ManualIntervention,
            SpacePublishAttemptStatus.ReconciliationRequired,
        };
        var rows = await (
                from attempt in context.PublishAttempts
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                where trackedStatuses.Contains(attempt.Status)
                join audit in context.PublishAuditEvents
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                    on new { attempt.TenantId, AttemptId = attempt.Id }
                    equals new { audit.TenantId, audit.AttemptId }
                    into audits
                select new RecoveryRow(
                    attempt.Status,
                    attempt.StartedAtUtc,
                    audits
                        .Where(value =>
                            value.AttemptStatus == attempt.Status)
                        .Max(value => (DateTime?)value.OccurredAtUtc)))
            .ToArrayAsync(cancellationToken);

        var nowUtc = clock.UtcNow;
        var byState = SpacePublishRecoveryMetricStates.All.ToDictionary(
            state => state,
            state => Aggregate(
                rows.Where(value => State(value.Status) == state),
                nowUtc,
                SpacePublishRecoveryMetricStates.TargetFor(state)),
            StringComparer.Ordinal);
        return new SpacePublishRecoveryMetricsSnapshot(byState);
    }

    private static SpacePublishRecoveryStateMetrics Aggregate(
        IEnumerable<RecoveryRow> rows,
        DateTime nowUtc,
        TimeSpan target)
    {
        var ages = rows
            .Select(value => AgeSeconds(
                nowUtc,
                value.EnteredAtUtc ?? value.StartedAtUtc))
            .ToArray();
        return new SpacePublishRecoveryStateMetrics(
            ages.LongLength,
            ages.Length == 0 ? 0d : ages.Max(),
            ages.LongCount(value => value > target.TotalSeconds));
    }

    private static double AgeSeconds(DateTime nowUtc, DateTime enteredAtUtc)
    {
        var age = nowUtc - DateTime.SpecifyKind(enteredAtUtc, DateTimeKind.Utc);
        return Math.Max(0d, age.TotalSeconds);
    }

    private static string State(SpacePublishAttemptStatus status) =>
        status switch
        {
            SpacePublishAttemptStatus.WaitingRetry =>
                SpacePublishRecoveryMetricStates.WaitingRetry,
            SpacePublishAttemptStatus.ManualIntervention =>
                SpacePublishRecoveryMetricStates.ManualIntervention,
            SpacePublishAttemptStatus.ReconciliationRequired =>
                SpacePublishRecoveryMetricStates.ReconciliationRequired,
            _ => throw new InvalidOperationException(
                "Only publish recovery states can be aggregated."),
        };

    private sealed record RecoveryRow(
        SpacePublishAttemptStatus Status,
        DateTime StartedAtUtc,
        DateTime? EnteredAtUtc);
}
