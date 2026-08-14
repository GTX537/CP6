namespace CP6.Space.Application;

public static class SpacePublishRecoveryMetricStates
{
    public const string WaitingRetry = "waiting_retry";
    public const string ManualIntervention = "manual_intervention";
    public const string ReconciliationRequired = "reconciliation_required";

    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly(
        [
            WaitingRetry,
            ManualIntervention,
            ReconciliationRequired,
        ]);

    public static TimeSpan TargetFor(string state) => state switch
    {
        WaitingRetry => TimeSpan.FromMinutes(15),
        ManualIntervention or ReconciliationRequired =>
            TimeSpan.FromHours(4),
        _ => throw new ArgumentOutOfRangeException(
            nameof(state),
            state,
            "Unknown publish recovery metric state."),
    };
}

public sealed record SpacePublishRecoveryStateMetrics(
    long Count,
    double OldestAgeSeconds,
    long SloBreachedCount);

public sealed record SpacePublishRecoveryMetricsSnapshot(
    IReadOnlyDictionary<string, SpacePublishRecoveryStateMetrics> ByState);

public interface ISpacePublishRecoveryMetricsSnapshotProvider
{
    Task<SpacePublishRecoveryMetricsSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);
}
