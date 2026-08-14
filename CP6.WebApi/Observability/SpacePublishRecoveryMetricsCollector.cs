using CP6.Core.Services.Space.Observability;
using CP6.Space.Application;
using Prometheus;

namespace CP6.WebApi.Observability;

/// <summary>
/// Publishes fixed-label, tenant-free recovery gauges for the Space publish
/// saga. Alert thresholds are frozen by the core GA recovery objectives.
/// </summary>
public sealed class SpacePublishRecoveryMetricsCollector
{
    internal const string SnapshotFailureReasonCode =
        "SPACE_PUBLISH_RECOVERY_METRICS_SNAPSHOT_FAILED";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SpacePublishRecoveryMetricsCollector> _logger;
    private readonly CollectorRegistry _registry;
    private readonly Gauge _attempts;
    private readonly Gauge _oldestAgeSeconds;
    private readonly Gauge _sloBreaches;
    private readonly Gauge _targetSeconds;
    private readonly SemaphoreSlim _collectionGate = new(1, 1);
    private int _registered;

    public SpacePublishRecoveryMetricsCollector(
        IServiceScopeFactory scopeFactory,
        ILogger<SpacePublishRecoveryMetricsCollector> logger,
        CollectorRegistry? registry = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _registry = registry ?? Metrics.DefaultRegistry;
        var factory = registry is null
            ? Metrics.DefaultFactory
            : Metrics.WithCustomRegistry(registry);
        var configuration = new GaugeConfiguration
        {
            LabelNames = ["state"],
        };

        _attempts = factory.CreateGauge(
            "cp6_space_publish_recovery_attempts",
            "Active Space publish attempts by fixed recovery state.",
            configuration);
        _oldestAgeSeconds = factory.CreateGauge(
            "cp6_space_publish_recovery_oldest_age_seconds",
            "Age in seconds of the oldest active attempt by recovery state.",
            configuration);
        _sloBreaches = factory.CreateGauge(
            "cp6_space_publish_recovery_slo_breaches",
            "Active attempts older than the frozen recovery target.",
            configuration);
        _targetSeconds = factory.CreateGauge(
            "cp6_space_publish_recovery_target_seconds",
            "Frozen core GA recovery target in seconds by state.",
            configuration);

        Publish(EmptySnapshot());
    }

    public void Register()
    {
        if (Interlocked.CompareExchange(ref _registered, 1, 0) != 0)
            return;

        try
        {
            _registry.AddBeforeCollectCallback(CollectAsync);
        }
        catch
        {
            Volatile.Write(ref _registered, 0);
            throw;
        }
    }

    internal async Task CollectAsync(CancellationToken cancellationToken)
    {
        await _collectionGate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<
                ISpacePublishRecoveryMetricsSnapshotProvider>();
            var snapshot = await provider.GetSnapshotAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Publish(snapshot);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var safe = SpaceErrorSanitizer.Classify(
                exception,
                SnapshotFailureReasonCode);
            _logger.LogWarning(
                "Space publish recovery metrics aggregation failed. " +
                "ReasonCode={ReasonCode} ErrorType={ErrorType} " +
                "Fingerprint={Fingerprint}",
                safe.ReasonCode,
                safe.ExceptionType,
                safe.Fingerprint);
        }
        finally
        {
            _collectionGate.Release();
        }
    }

    private void Publish(SpacePublishRecoveryMetricsSnapshot snapshot)
    {
        foreach (var state in SpacePublishRecoveryMetricStates.All)
        {
            var metrics = snapshot.ByState.TryGetValue(state, out var value)
                ? value
                : new SpacePublishRecoveryStateMetrics(0, 0, 0);
            _attempts.WithLabels(state).Set(metrics.Count);
            _oldestAgeSeconds.WithLabels(state).Set(
                metrics.OldestAgeSeconds);
            _sloBreaches.WithLabels(state).Set(metrics.SloBreachedCount);
            _targetSeconds.WithLabels(state).Set(
                SpacePublishRecoveryMetricStates.TargetFor(state)
                    .TotalSeconds);
        }
    }

    private static SpacePublishRecoveryMetricsSnapshot EmptySnapshot() =>
        new(SpacePublishRecoveryMetricStates.All.ToDictionary(
            state => state,
            _ => new SpacePublishRecoveryStateMetrics(0, 0, 0),
            StringComparer.Ordinal));
}

internal static class SpacePublishRecoveryMetricsRegistration
{
    internal static bool RegisterIfEnabled(
        bool enabled,
        IServiceProvider services)
    {
        if (!enabled)
            return false;

        services
            .GetRequiredService<SpacePublishRecoveryMetricsCollector>()
            .Register();
        return true;
    }
}
