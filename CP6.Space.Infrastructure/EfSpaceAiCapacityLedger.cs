using System.Data;
using CP6.Space.Application;
using CP6.Space.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class EfSpaceAiCapacityLedger : ISpaceAiCapacityLedger
{
    private const int ConcurrencyRetries = 3;

    private readonly SpaceContext _context;
    private readonly ISpaceClock _clock;

    public EfSpaceAiCapacityLedger(
        SpaceContext context,
        ISpaceClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<SpaceAiWorkSlotLease?> TryAcquireWorkSlotAsync(
        Guid runId,
        string leaseOwner,
        int maxConcurrentRuns,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("Run is required.", nameof(runId));
        if (maxConcurrentRuns is < 1 or >
            SpaceTenantAiWorkSlot.PlatformSlotCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentRuns));
        }
        var normalizedOwner = RequireLeaseOwner(leaseOwner);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }
        var tenantId = RequireTenant();

        for (var retry = 0; retry < ConcurrencyRetries; retry++)
        {
            _context.ChangeTracker.Clear();
            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            try
            {
                await EnsureWorkSlotsAsync(tenantId, cancellationToken);
                var now = RequireUtcNow();
                var existing = await _context.TenantAiWorkSlots
                    .FromSqlInterpolated(
                        $"""
                        SELECT *
                        FROM [Space_TenantAiWorkSlot]
                            WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                        WHERE [TenantId] = {tenantId}
                          AND [RunId] = {runId}
                        """)
                    .SingleOrDefaultAsync(cancellationToken);
                if (existing is not null)
                {
                    if (!existing.IsAvailable(now) &&
                        !string.Equals(
                            existing.LeaseOwner,
                            normalizedOwner,
                            StringComparison.Ordinal))
                    {
                        await transaction.CommitAsync(cancellationToken);
                        return null;
                    }

                    existing.Acquire(
                        runId,
                        normalizedOwner,
                        now,
                        leaseDuration);
                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return ToLease(existing);
                }

                var activeCount = await _context.TenantAiWorkSlots
                    .FromSqlInterpolated(
                        $"""
                        SELECT *
                        FROM [Space_TenantAiWorkSlot]
                            WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                        WHERE [TenantId] = {tenantId}
                          AND [RunId] IS NOT NULL
                          AND [LeaseExpiresAtUtc] > {now}
                        """)
                    .CountAsync(cancellationToken);
                if (activeCount >= maxConcurrentRuns)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return null;
                }

                var available = await _context.TenantAiWorkSlots
                    .FromSqlInterpolated(
                        $"""
                        SELECT TOP (1) *
                        FROM [Space_TenantAiWorkSlot]
                            WITH (UPDLOCK, READPAST, ROWLOCK)
                        WHERE [TenantId] = {tenantId}
                          AND [SlotNo] <= {maxConcurrentRuns}
                          AND (
                              [RunId] IS NULL OR
                              [LeaseExpiresAtUtc] <= {now})
                        ORDER BY [SlotNo]
                        """)
                    .ToListAsync(cancellationToken);
                var slot = available.SingleOrDefault();
                if (slot is null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return null;
                }

                slot.Acquire(
                    runId,
                    normalizedOwner,
                    now,
                    leaseDuration);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ToLease(slot);
            }
            catch (DbUpdateException) when (
                retry + 1 < ConcurrencyRetries)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (SqlException exception) when (
                exception.Number == 1205 &&
                retry + 1 < ConcurrencyRetries)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        return null;
    }

    public async Task<SpaceAiWorkSlotLease> RenewWorkSlotAsync(
        SpaceAiWorkSlotLease lease,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        var slot = await LoadFencedSlotAsync(lease, cancellationToken);
        slot.Renew(
            lease.RunId,
            lease.LeaseOwner,
            RequireUtcNow(),
            leaseDuration);
        await SaveLeaseChangesAsync(cancellationToken);
        return ToLease(slot);
    }

    public async Task ReleaseWorkSlotAsync(
        SpaceAiWorkSlotLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var slot = await LoadFencedSlotAsync(lease, cancellationToken);
        slot.Release(lease.RunId, lease.LeaseOwner);
        await SaveLeaseChangesAsync(cancellationToken);
    }

    public async Task<SpaceAiBudgetReservationLease?>
        TryReserveBudgetAsync(
            SpaceAiBudgetReservationRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RunId == Guid.Empty)
        {
            throw new ArgumentException(
                "Run is required.",
                nameof(request));
        }
        if (request.ReservedCostMinor < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request));
        }
        if (request.ReservationDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request));
        }
        var key = RequireHash(
            request.ProviderRequestKey,
            nameof(request.ProviderRequestKey));
        var limits = (request.Limits ??
            throw new ArgumentNullException(nameof(request.Limits)))
            .Validate();
        if (request.ReservedCostMinor > 0 &&
            limits.Currency is null)
        {
            throw new ArgumentException(
                "Currency is required when cost is reserved.",
                nameof(request));
        }
        var tenantId = RequireTenant();

        for (var retry = 0; retry < ConcurrencyRetries; retry++)
        {
            _context.ChangeTracker.Clear();
            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            try
            {
                var now = RequireUtcNow();
                var day = DateOnly.FromDateTime(now);
                var month = day.Year * 100 + day.Month;
                var existing = await _context.AiBudgetReservations
                    .FromSqlInterpolated(
                        $"""
                        SELECT *
                        FROM [Space_AiBudgetReservation]
                            WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                        WHERE [TenantId] = {tenantId}
                          AND [ProviderRequestKey] = {key}
                          AND [IsDeleted] = CAST(0 AS bit)
                        """)
                    .SingleOrDefaultAsync(cancellationToken);
                if (existing is not null)
                {
                    RequireMatchingReservation(
                        existing,
                        request,
                        limits,
                        day,
                        month);
                    await transaction.CommitAsync(cancellationToken);
                    return ToLease(existing);
                }

                if (request.ReservedCostMinor > 0)
                {
                    var holdings = await _context.AiBudgetReservations
                        .FromSqlInterpolated(
                            $"""
                            SELECT *
                            FROM [Space_AiBudgetReservation]
                                WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                            WHERE [TenantId] = {tenantId}
                              AND [Currency] = {limits.Currency}
                              AND [IsDeleted] = CAST(0 AS bit)
                              AND (
                                  [PeriodDay] = {day} OR
                                  [PeriodMonth] = {month})
                            """)
                        .ToListAsync(cancellationToken);
                    foreach (var holding in holdings)
                        holding.ReleaseIfExpired(now);

                    var dailyCost = SumEffectiveCost(
                        holdings.Where(item => item.PeriodDay == day));
                    var monthlyCost = SumEffectiveCost(
                        holdings.Where(item => item.PeriodMonth == month));
                    if (WouldExceed(
                            dailyCost,
                            request.ReservedCostMinor,
                            limits.DailyBudgetMinor) ||
                        WouldExceed(
                            monthlyCost,
                            request.ReservedCostMinor,
                            limits.MonthlyBudgetMinor))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return null;
                    }
                }

                var reservation = SpaceAiBudgetReservation.Create(
                    tenantId,
                    request.RunId,
                    key,
                    day,
                    month,
                    request.ReservedCostMinor,
                    limits.Currency,
                    now.Add(request.ReservationDuration));
                _context.AiBudgetReservations.Add(reservation);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ToLease(reservation);
            }
            catch (DbUpdateException) when (
                retry + 1 < ConcurrencyRetries)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (SqlException exception) when (
                exception.Number == 1205 &&
                retry + 1 < ConcurrencyRetries)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        return null;
    }

    public async Task<SpaceAiBudgetReservationLease>
        MarkBudgetSubmittedAsync(
            SpaceAiBudgetReservationLease reservation,
            CancellationToken cancellationToken = default)
    {
        var entity = await LoadFencedReservationAsync(
            reservation,
            cancellationToken);
        entity.MarkSubmitted();
        await SaveReservationChangesAsync(cancellationToken);
        return ToLease(entity);
    }

    public async Task ReleaseBudgetAsync(
        SpaceAiBudgetReservationLease reservation,
        CancellationToken cancellationToken = default)
    {
        var entity = await LoadFencedReservationAsync(
            reservation,
            cancellationToken);
        entity.Release();
        await SaveReservationChangesAsync(cancellationToken);
    }

    public async Task<SpaceAiBudgetReservationLease> RecordUsageAsync(
        SpaceAiUsageReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.ReservationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Reservation is required.",
                nameof(report));
        }
        if (report.ReservationRowVersion is null ||
            report.ReservationRowVersion.Length == 0)
        {
            throw new ArgumentException(
                "Reservation row version is required.",
                nameof(report));
        }
        var tenantId = RequireTenant();
        _context.ChangeTracker.Clear();
        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        var reservation = await _context.AiBudgetReservations
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM [Space_AiBudgetReservation]
                    WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                WHERE [TenantId] = {tenantId}
                  AND [Id] = {report.ReservationId}
                  AND [IsDeleted] = CAST(0 AS bit)
                """)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException(
                "The AI budget reservation was not found.");
        var existingUsage = await _context.AiUsageRecords
            .SingleOrDefaultAsync(
                usage =>
                    usage.ProviderRequestIdHash ==
                    reservation.ProviderRequestKey,
                cancellationToken);
        if (reservation.Status ==
            SpaceAiBudgetReservationStatus.Reconciled)
        {
            RequireMatchingUsage(
                existingUsage,
                reservation,
                report);
            await transaction.CommitAsync(cancellationToken);
            return ToLease(reservation);
        }

        FenceRowVersion(
            reservation.RowVersion,
            report.ReservationRowVersion);
        if (existingUsage is not null)
        {
            throw new SpaceAiCapacityStateException(
                "Usage exists before its budget reservation is reconciled.");
        }

        reservation.Report(report.ActualCostMinor);
        var usage = SpaceAiUsageRecord.Create(
            tenantId,
            reservation.RunId,
            report.ProviderCode,
            report.ProviderModel,
            reservation.ProviderRequestKey,
            report.InputUnits,
            report.OutputUnits,
            reservation.ReservedCostMinor,
            report.ActualCostMinor,
            reservation.Currency,
            report.LatencyMs,
            report.Outcome,
            report.RecordedAtUtc);
        _context.AiUsageRecords.Add(usage);
        reservation.Reconcile();
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToLease(reservation);
    }

    public async Task<int> ReleaseExpiredBudgetReservationsAsync(
        CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var now = RequireUtcNow();
        _context.ChangeTracker.Clear();
        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        var expired = await _context.AiBudgetReservations
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM [Space_AiBudgetReservation]
                    WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE [TenantId] = {tenantId}
                  AND [Status] = {SpaceAiBudgetReservationStatus.Reserved}
                  AND [ExpiresAtUtc] <= {now}
                  AND [IsDeleted] = CAST(0 AS bit)
                """)
            .ToListAsync(cancellationToken);
        var released = expired.Count(item => item.ReleaseIfExpired(now));
        if (released > 0)
            await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return released;
    }

    private async Task EnsureWorkSlotsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        for (var slotNo = 1;
             slotNo <= SpaceTenantAiWorkSlot.PlatformSlotCount;
             slotNo++)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                IF NOT EXISTS (
                    SELECT 1
                    FROM [Space_TenantAiWorkSlot]
                        WITH (UPDLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [SlotNo] = {slotNo})
                BEGIN
                    INSERT INTO [Space_TenantAiWorkSlot]
                        ([TenantId], [SlotNo], [RunId], [LeaseOwner],
                         [LeaseExpiresAtUtc])
                    VALUES
                        ({tenantId}, {slotNo}, NULL, NULL, NULL)
                END
                """,
                cancellationToken);
        }
    }

    private async Task<SpaceTenantAiWorkSlot> LoadFencedSlotAsync(
        SpaceAiWorkSlotLease lease,
        CancellationToken cancellationToken)
    {
        if (lease.TenantId != RequireTenant())
        {
            throw new SpaceAiCapacityLeaseLostException(
                "The AI work-slot lease belongs to another tenant.");
        }
        _context.ChangeTracker.Clear();
        var slot = await _context.TenantAiWorkSlots.SingleOrDefaultAsync(
                       item => item.SlotNo == lease.SlotNo,
                       cancellationToken)
                   ?? throw new SpaceAiCapacityLeaseLostException(
                       "The AI work-slot lease no longer exists.");
        FenceRowVersion(slot.RowVersion, lease.RowVersion);
        return slot;
    }

    private async Task<SpaceAiBudgetReservation>
        LoadFencedReservationAsync(
            SpaceAiBudgetReservationLease reservation,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        if (reservation.TenantId != RequireTenant())
        {
            throw new SpaceAiCapacityLeaseLostException(
                "The AI budget reservation belongs to another tenant.");
        }
        _context.ChangeTracker.Clear();
        var entity = await _context.AiBudgetReservations
                         .SingleOrDefaultAsync(
                             item =>
                                 item.Id == reservation.ReservationId,
                             cancellationToken)
                     ?? throw new SpaceAiCapacityLeaseLostException(
                         "The AI budget reservation no longer exists.");
        FenceRowVersion(entity.RowVersion, reservation.RowVersion);
        return entity;
    }

    private async Task SaveLeaseChangesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new SpaceAiCapacityLeaseLostException(
                "The AI work-slot lease was concurrently replaced.",
                exception);
        }
    }

    private async Task SaveReservationChangesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new SpaceAiCapacityLeaseLostException(
                "The AI budget reservation was concurrently changed.",
                exception);
        }
    }

    private Guid RequireTenant()
    {
        if (_context.CurrentTenantId == Guid.Empty)
        {
            throw new SpaceTenantScopeException(
                "A verified Space tenant context is required.");
        }
        return _context.CurrentTenantId;
    }

    private DateTime RequireUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException(
                "The Space clock must return UTC.");
        }
        return now;
    }

    private static string RequireLeaseOwner(string leaseOwner)
    {
        var normalized = leaseOwner?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > 128)
        {
            throw new ArgumentException(
                "A lease owner up to 128 characters is required.",
                nameof(leaseOwner));
        }
        return normalized;
    }

    private static string RequireHash(
        string value,
        string parameterName)
    {
        if (value is null ||
            value.Length != 64 ||
            !value.All(character =>
                character is >= '0' and <= '9' ||
                character is >= 'a' and <= 'f'))
        {
            throw new ArgumentException(
                "A lowercase SHA-256 hex value is required.",
                parameterName);
        }
        return value;
    }

    private static long SumEffectiveCost(
        IEnumerable<SpaceAiBudgetReservation> reservations)
    {
        long total = 0;
        foreach (var reservation in reservations)
            total = checked(total + reservation.EffectiveCostMinor);
        return total;
    }

    private static bool WouldExceed(
        long current,
        long requested,
        long? limit) =>
        limit.HasValue &&
        (requested > limit.Value ||
         current > limit.Value - requested);

    private static void RequireMatchingReservation(
        SpaceAiBudgetReservation existing,
        SpaceAiBudgetReservationRequest request,
        SpaceAiBudgetLimits limits,
        DateOnly day,
        int month)
    {
        if (existing.RunId != request.RunId ||
            existing.PeriodDay != day ||
            existing.PeriodMonth != month ||
            existing.ReservedCostMinor != request.ReservedCostMinor ||
            !string.Equals(
                existing.Currency,
                limits.Currency,
                StringComparison.Ordinal))
        {
            throw new SpaceAiCapacityStateException(
                "Provider request key was reused with different budget data.");
        }
    }

    private static void RequireMatchingUsage(
        SpaceAiUsageRecord? usage,
        SpaceAiBudgetReservation reservation,
        SpaceAiUsageReport report)
    {
        if (usage is null ||
            usage.RunId != reservation.RunId ||
            usage.InputUnits != report.InputUnits ||
            usage.OutputUnits != report.OutputUnits ||
            usage.ActualCostMinor != report.ActualCostMinor ||
            usage.LatencyMs != report.LatencyMs ||
            usage.Outcome != report.Outcome ||
            !string.Equals(
                usage.ProviderCode,
                report.ProviderCode.Trim(),
                StringComparison.Ordinal) ||
            !string.Equals(
                usage.ProviderModel,
                report.ProviderModel.Trim(),
                StringComparison.Ordinal))
        {
            throw new SpaceAiCapacityStateException(
                "Provider usage replay does not match the recorded charge.");
        }
    }

    private static void FenceRowVersion(
        byte[] current,
        byte[] expected)
    {
        if (expected is null ||
            current is null ||
            !current.AsSpan().SequenceEqual(expected))
        {
            throw new SpaceAiCapacityLeaseLostException(
                "The AI capacity lease row version is stale.");
        }
    }

    private static SpaceAiWorkSlotLease ToLease(
        SpaceTenantAiWorkSlot slot) =>
        new(
            slot.TenantId,
            slot.SlotNo,
            slot.RunId!.Value,
            slot.LeaseOwner!,
            slot.LeaseExpiresAtUtc!.Value,
            slot.RowVersion.ToArray());

    private static SpaceAiBudgetReservationLease ToLease(
        SpaceAiBudgetReservation reservation) =>
        new(
            reservation.TenantId,
            reservation.Id,
            reservation.RunId,
            reservation.ProviderRequestKey,
            reservation.PeriodDay,
            reservation.PeriodMonth,
            reservation.ReservedCostMinor,
            reservation.ActualCostMinor,
            reservation.Currency,
            reservation.Status,
            reservation.ExpiresAtUtc,
            reservation.RowVersion.ToArray());
}
