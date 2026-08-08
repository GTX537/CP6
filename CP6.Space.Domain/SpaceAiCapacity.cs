namespace CP6.Space.Domain;

public sealed class SpaceTenantAiWorkSlot
{
    public const int PlatformSlotCount = 3;

    private SpaceTenantAiWorkSlot()
    {
    }

    public Guid TenantId { get; private set; }
    public int SlotNo { get; private set; }
    public Guid? RunId { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsAvailable(DateTime nowUtc)
    {
        SpaceGenerationRun.RequireUtc(nowUtc, nameof(nowUtc));
        return RunId is null || LeaseExpiresAtUtc <= nowUtc;
    }

    public static SpaceTenantAiWorkSlot CreateAvailable(
        Guid tenantId,
        int slotNo)
    {
        RequireTenant(tenantId);
        RequireSlot(slotNo);
        return new SpaceTenantAiWorkSlot
        {
            TenantId = tenantId,
            SlotNo = slotNo,
        };
    }

    public void Acquire(
        Guid runId,
        string leaseOwner,
        DateTime nowUtc,
        TimeSpan leaseDuration)
    {
        RequireRun(runId);
        var owner = RequireLeaseOwner(leaseOwner);
        RequireLeaseWindow(nowUtc, leaseDuration);
        if (!IsAvailable(nowUtc) &&
            (RunId != runId ||
             !string.Equals(LeaseOwner, owner, StringComparison.Ordinal)))
        {
            throw new SpaceAiCapacityStateException(
                "The AI work slot is already leased.");
        }

        RunId = runId;
        LeaseOwner = owner;
        LeaseExpiresAtUtc = nowUtc.Add(leaseDuration);
    }

    public void Renew(
        Guid runId,
        string leaseOwner,
        DateTime nowUtc,
        TimeSpan leaseDuration)
    {
        RequireLeaseWindow(nowUtc, leaseDuration);
        Fence(runId, leaseOwner);
        if (LeaseExpiresAtUtc <= nowUtc)
        {
            throw new SpaceAiCapacityLeaseLostException(
                "The AI work-slot lease expired.");
        }

        LeaseExpiresAtUtc = nowUtc.Add(leaseDuration);
    }

    public void Release(Guid runId, string leaseOwner)
    {
        Fence(runId, leaseOwner);
        RunId = null;
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
    }

    private void Fence(Guid runId, string leaseOwner)
    {
        RequireRun(runId);
        var owner = RequireLeaseOwner(leaseOwner);
        if (RunId != runId ||
            !string.Equals(LeaseOwner, owner, StringComparison.Ordinal))
        {
            throw new SpaceAiCapacityLeaseLostException(
                "The AI work-slot lease belongs to another worker.");
        }
    }

    private static void RequireTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant is required.", nameof(tenantId));
    }

    private static void RequireRun(Guid runId)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("Run is required.", nameof(runId));
    }

    private static void RequireSlot(int slotNo)
    {
        if (slotNo is < 1 or > PlatformSlotCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotNo),
                $"AI work slot must be between 1 and {PlatformSlotCount}.");
        }
    }

    private static string RequireLeaseOwner(string leaseOwner) =>
        SpaceGenerationRun.RequireText(
            leaseOwner,
            128,
            nameof(leaseOwner));

    private static void RequireLeaseWindow(
        DateTime nowUtc,
        TimeSpan leaseDuration)
    {
        SpaceGenerationRun.RequireUtc(nowUtc, nameof(nowUtc));
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                "Lease duration must be positive.");
        }
    }
}

public sealed class SpaceAiBudgetReservation : SpaceTenantEntity
{
    private SpaceAiBudgetReservation()
    {
    }

    public Guid RunId { get; private set; }
    public string ProviderRequestKey { get; private set; } = string.Empty;
    public DateOnly PeriodDay { get; private set; }
    public int PeriodMonth { get; private set; }
    public long ReservedCostMinor { get; private set; }
    public long? ActualCostMinor { get; private set; }
    public string? Currency { get; private set; }
    public SpaceAiBudgetReservationStatus Status { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public long EffectiveCostMinor =>
        Status == SpaceAiBudgetReservationStatus.Released
            ? 0
            : Status is SpaceAiBudgetReservationStatus.Reported
                or SpaceAiBudgetReservationStatus.Reconciled
                ? ActualCostMinor ?? ReservedCostMinor
                : ReservedCostMinor;

    public static SpaceAiBudgetReservation Create(
        Guid tenantId,
        Guid runId,
        string providerRequestKey,
        DateOnly periodDay,
        int periodMonth,
        long reservedCostMinor,
        string? currency,
        DateTime expiresAtUtc)
    {
        if (runId == Guid.Empty)
            throw new ArgumentException("Run is required.", nameof(runId));
        if (reservedCostMinor < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reservedCostMinor));
        }
        RequirePeriod(periodDay, periodMonth);
        SpaceGenerationRun.RequireUtc(
            expiresAtUtc,
            nameof(expiresAtUtc));
        var normalizedCurrency = NormalizeCurrency(currency);
        if (reservedCostMinor > 0 && normalizedCurrency is null)
        {
            throw new ArgumentException(
                "Currency is required when cost is reserved.",
                nameof(currency));
        }

        var reservation = new SpaceAiBudgetReservation
        {
            RunId = runId,
            ProviderRequestKey = SpaceGenerationRun.RequireHash(
                providerRequestKey,
                nameof(providerRequestKey)),
            PeriodDay = periodDay,
            PeriodMonth = periodMonth,
            ReservedCostMinor = reservedCostMinor,
            Currency = normalizedCurrency,
            Status = SpaceAiBudgetReservationStatus.Reserved,
            ExpiresAtUtc = expiresAtUtc,
        };
        reservation.SetTenant(tenantId);
        return reservation;
    }

    public void MarkSubmitted()
    {
        if (Status == SpaceAiBudgetReservationStatus.Submitted)
            return;
        RequireStatus(SpaceAiBudgetReservationStatus.Reserved, "be submitted");
        Status = SpaceAiBudgetReservationStatus.Submitted;
    }

    public void Report(long actualCostMinor)
    {
        if (actualCostMinor < 0)
            throw new ArgumentOutOfRangeException(nameof(actualCostMinor));
        if (Status is SpaceAiBudgetReservationStatus.Reported
            or SpaceAiBudgetReservationStatus.Reconciled)
        {
            if (ActualCostMinor != actualCostMinor)
            {
                throw new SpaceAiCapacityStateException(
                    "Reported AI cost cannot be overwritten.");
            }
            return;
        }

        RequireStatus(SpaceAiBudgetReservationStatus.Submitted, "be reported");
        ActualCostMinor = actualCostMinor;
        Status = SpaceAiBudgetReservationStatus.Reported;
    }

    public void Reconcile()
    {
        if (Status == SpaceAiBudgetReservationStatus.Reconciled)
            return;
        RequireStatus(
            SpaceAiBudgetReservationStatus.Reported,
            "be reconciled");
        Status = SpaceAiBudgetReservationStatus.Reconciled;
    }

    public void Release()
    {
        if (Status == SpaceAiBudgetReservationStatus.Released)
            return;
        RequireStatus(SpaceAiBudgetReservationStatus.Reserved, "be released");
        Status = SpaceAiBudgetReservationStatus.Released;
    }

    public bool ReleaseIfExpired(DateTime nowUtc)
    {
        SpaceGenerationRun.RequireUtc(nowUtc, nameof(nowUtc));
        if (Status != SpaceAiBudgetReservationStatus.Reserved ||
            ExpiresAtUtc > nowUtc)
        {
            return false;
        }

        Status = SpaceAiBudgetReservationStatus.Released;
        return true;
    }

    public static string? NormalizeCurrency(string? currency)
    {
        if (currency is null)
            return null;
        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3 ||
            !normalized.All(character =>
                character is >= 'A' and <= 'Z'))
        {
            throw new ArgumentException(
                "Currency must be an ISO 4217 alpha code.",
                nameof(currency));
        }
        return normalized;
    }

    private static void RequirePeriod(DateOnly day, int month)
    {
        if (month != day.Year * 100 + day.Month)
        {
            throw new ArgumentException(
                "Budget month must match the budget day.",
                nameof(month));
        }
    }

    private void RequireStatus(
        SpaceAiBudgetReservationStatus expected,
        string action)
    {
        if (Status != expected)
        {
            throw new SpaceAiCapacityStateException(
                $"AI budget reservation cannot {action} from {Status}.");
        }
    }
}

public sealed class SpaceAiCapacityStateException : InvalidOperationException
{
    public SpaceAiCapacityStateException(string message)
        : base(message)
    {
    }
}

public sealed class SpaceAiCapacityLeaseLostException :
    InvalidOperationException
{
    public SpaceAiCapacityLeaseLostException(string message)
        : base(message)
    {
    }

    public SpaceAiCapacityLeaseLostException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
