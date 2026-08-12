namespace CP6.Space.Contracts;

public sealed record AcquireSpaceEditLeaseRequest(Guid ClientInstanceId);

public sealed record TakeoverSpaceEditLeaseRequest(
    Guid ClientInstanceId,
    string Reason);

public sealed record SpaceEditLeaseDto(
    Guid ModelVersionId,
    Guid FloorLogicalId,
    Guid? LeaseId,
    Guid? OwnerUserId,
    Guid? ClientInstanceId,
    DateTime? ExpiresAtUtc,
    DateTime? LastRenewedAtUtc,
    bool IsAvailable,
    bool IsOwnedByCurrentActor,
    string? RowVersion);
