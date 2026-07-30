using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public sealed record SpaceAiWorkSlotLease(
    Guid TenantId,
    int SlotNo,
    Guid RunId,
    string LeaseOwner,
    DateTime LeaseExpiresAtUtc,
    byte[] RowVersion);

public sealed record SpaceAiBudgetLimits(
    long? DailyBudgetMinor,
    long? MonthlyBudgetMinor,
    string? Currency)
{
    public static SpaceAiBudgetLimits Unpriced { get; } =
        new(null, null, null);

    public SpaceAiBudgetLimits Validate()
    {
        if (DailyBudgetMinor < 0)
            throw new ArgumentOutOfRangeException(nameof(DailyBudgetMinor));
        if (MonthlyBudgetMinor < 0)
            throw new ArgumentOutOfRangeException(nameof(MonthlyBudgetMinor));
        if (DailyBudgetMinor.HasValue &&
            MonthlyBudgetMinor.HasValue &&
            MonthlyBudgetMinor < DailyBudgetMinor)
        {
            throw new ArgumentException(
                "Monthly AI budget cannot be lower than the daily budget.");
        }

        var normalizedCurrency =
            SpaceAiBudgetReservation.NormalizeCurrency(Currency);
        if ((DailyBudgetMinor.HasValue || MonthlyBudgetMinor.HasValue) &&
            normalizedCurrency is null)
        {
            throw new ArgumentException(
                "Currency is required when an AI budget is configured.",
                nameof(Currency));
        }
        return this with { Currency = normalizedCurrency };
    }
}

public sealed record SpaceAiBudgetReservationRequest(
    Guid RunId,
    string ProviderRequestKey,
    long ReservedCostMinor,
    SpaceAiBudgetLimits Limits,
    TimeSpan ReservationDuration);

public sealed record SpaceAiBudgetReservationLease(
    Guid TenantId,
    Guid ReservationId,
    Guid RunId,
    string ProviderRequestKey,
    DateOnly PeriodDay,
    int PeriodMonth,
    long ReservedCostMinor,
    long? ActualCostMinor,
    string? Currency,
    SpaceAiBudgetReservationStatus Status,
    DateTime ExpiresAtUtc,
    byte[] RowVersion);

public sealed record SpaceAiUsageReport(
    Guid ReservationId,
    byte[] ReservationRowVersion,
    string ProviderCode,
    string ProviderModel,
    long InputUnits,
    long OutputUnits,
    long ActualCostMinor,
    long LatencyMs,
    SpaceAiUsageOutcome Outcome,
    DateTime RecordedAtUtc);

public interface ISpaceAiCapacityLedger
{
    Task<SpaceAiWorkSlotLease?> TryAcquireWorkSlotAsync(
        Guid runId,
        string leaseOwner,
        int maxConcurrentRuns,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<SpaceAiWorkSlotLease> RenewWorkSlotAsync(
        SpaceAiWorkSlotLease lease,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task ReleaseWorkSlotAsync(
        SpaceAiWorkSlotLease lease,
        CancellationToken cancellationToken = default);

    Task<SpaceAiBudgetReservationLease?> TryReserveBudgetAsync(
        SpaceAiBudgetReservationRequest request,
        CancellationToken cancellationToken = default);

    Task<SpaceAiBudgetReservationLease> MarkBudgetSubmittedAsync(
        SpaceAiBudgetReservationLease reservation,
        CancellationToken cancellationToken = default);

    Task ReleaseBudgetAsync(
        SpaceAiBudgetReservationLease reservation,
        CancellationToken cancellationToken = default);

    Task<SpaceAiBudgetReservationLease> RecordUsageAsync(
        SpaceAiUsageReport report,
        CancellationToken cancellationToken = default);

    Task<int> ReleaseExpiredBudgetReservationsAsync(
        CancellationToken cancellationToken = default);
}

public sealed class SpaceAiCapacityOptions
{
    public TimeSpan WorkSlotLeaseDuration { get; init; } =
        TimeSpan.FromSeconds(60);

    public TimeSpan BudgetReservationDuration { get; init; } =
        TimeSpan.FromMinutes(15);

    public void Validate()
    {
        if (WorkSlotLeaseDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "AI work-slot lease duration must be positive.");
        }
        if (BudgetReservationDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "AI budget reservation duration must be positive.");
        }
    }
}

public sealed class SpaceAiCapacityCoordinator(
    ISpaceAiCapacityLedger ledger,
    SpaceAiCapacityOptions options)
{
    public async Task<SpaceAiWorkSlotLease> AcquireWorkSlotAsync(
        Guid runId,
        string leaseOwner,
        int maxConcurrentRuns,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        return await ledger.TryAcquireWorkSlotAsync(
                   runId,
                   leaseOwner,
                   maxConcurrentRuns,
                   options.WorkSlotLeaseDuration,
                   cancellationToken)
               ?? throw QuotaExceeded();
    }

    public async Task<SpaceAiBudgetReservationLease> ReserveBudgetAsync(
        Guid runId,
        string providerRequestKey,
        long reservedCostMinor,
        SpaceAiBudgetLimits limits,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        return await ledger.TryReserveBudgetAsync(
                   new SpaceAiBudgetReservationRequest(
                       runId,
                       providerRequestKey,
                       reservedCostMinor,
                       limits,
                       options.BudgetReservationDuration),
                   cancellationToken)
               ?? throw QuotaExceeded();
    }

    private static SpaceProblemException QuotaExceeded() =>
        new(
            SpaceErrorCodes.AiQuotaExceeded,
            429,
            "The tenant AI concurrency or budget quota is unavailable.",
            recoveryAction: "use-rule-only-or-retry-later",
            retryable: true);
}
