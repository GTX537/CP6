using CP6.Core.Services.Space.Observability;
using CP6.Entity.DTOs.Space;
using Prometheus;

namespace CP6.WebApi.Observability;

/// <summary>
/// Publishes low-cardinality, tenant-free Space audit ledger gauges.
/// </summary>
public sealed class SpaceAuditMetricsCollector
{
    internal const string SnapshotFailureReasonCode =
        "SPACE_AUDIT_METRICS_SNAPSHOT_FAILED";

    private static readonly string[] FixedOutcomes =
    [
        SpaceAuditOutcome.Started,
        SpaceAuditOutcome.Succeeded,
        SpaceAuditOutcome.Failed,
        SpaceAuditOutcome.Denied,
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SpaceAuditMetricsCollector> _logger;
    private readonly CollectorRegistry _registry;
    private readonly Gauge _total;
    private readonly Gauge _byOutcome;
    private readonly SemaphoreSlim _collectionGate = new(1, 1);
    private int _registered;

    public SpaceAuditMetricsCollector(
        IServiceScopeFactory scopeFactory,
        ILogger<SpaceAuditMetricsCollector> logger,
        CollectorRegistry? registry = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _registry = registry ?? Metrics.DefaultRegistry;
        var factory = registry is null
            ? Metrics.DefaultFactory
            : Metrics.WithCustomRegistry(registry);

        _total = factory.CreateGauge(
            "cp6_space_audit_event_total",
            "Total number of persisted Space audit ledger events.");
        _byOutcome = factory.CreateGauge(
            "cp6_space_audit_event_by_outcome",
            "Persisted Space audit ledger events by fixed outcome.",
            new GaugeConfiguration
            {
                LabelNames = ["outcome"],
            });

        _total.Set(0);
        foreach (var outcome in FixedOutcomes)
            _byOutcome.WithLabels(outcome).Set(0);
    }

    /// <summary>
    /// Registers exactly one callback even when startup code calls this method
    /// repeatedly or concurrently.
    /// </summary>
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

    internal async Task CollectAsync(CancellationToken ct)
    {
        await _collectionGate.WaitAsync(ct);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var provider = scope.ServiceProvider
                .GetRequiredService<ISpaceAuditMetricsSnapshotProvider>();
            var snapshot = await provider.GetSnapshotAsync(ct);
            ct.ThrowIfCancellationRequested();

            Publish(snapshot);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var safe = SpaceErrorSanitizer.Classify(
                exception,
                SnapshotFailureReasonCode);
            _logger.LogWarning(
                "Space audit metrics aggregation failed. " +
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

    private void Publish(SpaceAuditMetricsSnapshot snapshot)
    {
        _total.Set(snapshot.Total);
        foreach (var outcome in FixedOutcomes)
        {
            var count = snapshot.ByOutcome.TryGetValue(
                outcome,
                out var value)
                ? value
                : 0L;
            _byOutcome.WithLabels(outcome).Set(count);
        }
    }
}

internal static class SpaceAuditMetricsRegistration
{
    internal static bool RegisterIfEnabled(
        bool enabled,
        IServiceProvider services)
    {
        if (!enabled)
            return false;

        services
            .GetRequiredService<SpaceAuditMetricsCollector>()
            .Register();
        return true;
    }
}
