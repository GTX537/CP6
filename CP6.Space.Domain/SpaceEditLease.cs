namespace CP6.Space.Domain;

public sealed class SpaceEditLease : SpaceTenantEntity
{
    private SpaceEditLease()
    {
    }

    public Guid ModelVersionId { get; private set; }
    public Guid FloorLogicalId { get; private set; }
    public Guid LeaseId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string HolderDisplayName { get; private set; } = string.Empty;
    public Guid ClientInstanceId { get; private set; }
    public DateTime AcquiredAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime LastRenewedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsExpired(DateTime nowUtc) => ExpiresAtUtc <= nowUtc;

    public bool IsOwnedBy(Guid actorId, Guid clientInstanceId) =>
        OwnerUserId == actorId && ClientInstanceId == clientInstanceId;

    public static SpaceEditLease Create(
        Guid tenantId,
        Guid modelVersionId,
        Guid floorLogicalId,
        Guid ownerUserId,
        string holderDisplayName,
        Guid clientInstanceId,
        DateTime nowUtc,
        TimeSpan duration)
    {
        RequireIdentity(modelVersionId, nameof(modelVersionId));
        RequireIdentity(floorLogicalId, nameof(floorLogicalId));
        RequireIdentity(ownerUserId, nameof(ownerUserId));
        RequireIdentity(clientInstanceId, nameof(clientInstanceId));
        ValidateTime(nowUtc, duration);

        var lease = new SpaceEditLease
        {
            ModelVersionId = modelVersionId,
            FloorLogicalId = floorLogicalId,
        };
        lease.SetTenant(tenantId);
        lease.Assign(
            ownerUserId,
            holderDisplayName,
            clientInstanceId,
            nowUtc,
            duration);
        return lease;
    }

    public void Reassign(
        Guid ownerUserId,
        string holderDisplayName,
        Guid clientInstanceId,
        DateTime nowUtc,
        TimeSpan duration)
    {
        RequireIdentity(ownerUserId, nameof(ownerUserId));
        RequireIdentity(clientInstanceId, nameof(clientInstanceId));
        ValidateTime(nowUtc, duration);
        Assign(
            ownerUserId,
            holderDisplayName,
            clientInstanceId,
            nowUtc,
            duration);
    }

    public void Renew(
        Guid leaseId,
        Guid ownerUserId,
        Guid clientInstanceId,
        DateTime nowUtc,
        TimeSpan duration)
    {
        ValidateTime(nowUtc, duration);
        if (LeaseId != leaseId ||
            OwnerUserId != ownerUserId ||
            ClientInstanceId != clientInstanceId ||
            IsExpired(nowUtc))
            throw new InvalidOperationException("The edit lease is no longer owned.");

        LastRenewedAtUtc = nowUtc;
        ExpiresAtUtc = nowUtc.Add(duration);
    }

    public void Release(
        Guid leaseId,
        Guid ownerUserId,
        Guid clientInstanceId,
        DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Lease time must be UTC.", nameof(nowUtc));
        if (LeaseId != leaseId ||
            OwnerUserId != ownerUserId ||
            ClientInstanceId != clientInstanceId)
            throw new InvalidOperationException("The edit lease is no longer owned.");

        LastRenewedAtUtc = nowUtc;
        ExpiresAtUtc = nowUtc;
    }

    private void Assign(
        Guid ownerUserId,
        string holderDisplayName,
        Guid clientInstanceId,
        DateTime nowUtc,
        TimeSpan duration)
    {
        LeaseId = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        HolderDisplayName = NormalizeDisplayName(holderDisplayName, ownerUserId);
        ClientInstanceId = clientInstanceId;
        AcquiredAtUtc = nowUtc;
        LastRenewedAtUtc = nowUtc;
        ExpiresAtUtc = nowUtc.Add(duration);
    }

    private static string NormalizeDisplayName(string? value, Guid ownerUserId)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            normalized = ownerUserId.ToString("D");
        return normalized.Length <= 200 ? normalized : normalized[..200];
    }

    private static void RequireIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Identity is required.", parameterName);
    }

    private static void ValidateTime(DateTime nowUtc, TimeSpan duration)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Lease time must be UTC.", nameof(nowUtc));
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));
    }
}

public sealed class SpaceEditLeaseTakeoverAudit : SpaceTenantEntity
{
    private SpaceEditLeaseTakeoverAudit()
    {
    }

    public Guid ModelVersionId { get; private set; }
    public Guid FloorLogicalId { get; private set; }
    public Guid PreviousLeaseId { get; private set; }
    public Guid PreviousOwnerUserId { get; private set; }
    public Guid NewLeaseId { get; private set; }
    public Guid TakenOverByUserId { get; private set; }
    public Guid ClientInstanceId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid CorrelationId { get; private set; }
    public string RequestSource { get; private set; } = string.Empty;
    public DateTime TakenOverAtUtc { get; private set; }

    public static SpaceEditLeaseTakeoverAudit Create(
        Guid tenantId,
        Guid modelVersionId,
        Guid floorLogicalId,
        Guid previousLeaseId,
        Guid previousOwnerUserId,
        Guid newLeaseId,
        Guid takenOverByUserId,
        Guid clientInstanceId,
        string reason,
        Guid correlationId,
        string requestSource,
        DateTime takenOverAtUtc)
    {
        var normalizedReason = reason?.Trim();
        if (new[]
            {
                modelVersionId,
                floorLogicalId,
                previousLeaseId,
                previousOwnerUserId,
                newLeaseId,
                takenOverByUserId,
                clientInstanceId,
                correlationId,
            }.Any(value => value == Guid.Empty))
        {
            throw new ArgumentException("Takeover audit identities are required.");
        }
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 500)
            throw new ArgumentException("A takeover reason of at most 500 characters is required.");
        var normalizedSource = requestSource?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSource) || normalizedSource.Length > 500)
            throw new ArgumentException("A takeover request source of at most 500 characters is required.");
        if (takenOverAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Takeover time must be UTC.", nameof(takenOverAtUtc));

        var audit = new SpaceEditLeaseTakeoverAudit
        {
            ModelVersionId = modelVersionId,
            FloorLogicalId = floorLogicalId,
            PreviousLeaseId = previousLeaseId,
            PreviousOwnerUserId = previousOwnerUserId,
            NewLeaseId = newLeaseId,
            TakenOverByUserId = takenOverByUserId,
            ClientInstanceId = clientInstanceId,
            Reason = normalizedReason!,
            CorrelationId = correlationId,
            RequestSource = normalizedSource!,
            TakenOverAtUtc = takenOverAtUtc,
        };
        audit.SetTenant(tenantId);
        return audit;
    }
}
