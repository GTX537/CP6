using CP6.Core.EFDbContext;

namespace CP6.Core.Services.Space.Observability;

public static class SpaceAuditOutcome
{
    public const string Started = "Started";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Denied = "Denied";

    internal static bool IsValid(string? value) =>
        value is Started or Succeeded or Failed or Denied;
}

public sealed record SpaceAuditEvidence(
    string? PermissionCode = null,
    string? AuthorizationResult = null,
    int? ItemCount = null,
    string? Status = null,
    string? ExceptionType = null,
    string? ErrorFingerprint = null);

public sealed record SpaceAuditEventInput(
    string Action,
    string ResourceType,
    string? ResourceId,
    string Outcome,
    string? ReasonCode = null,
    Guid? SiteId = null,
    Guid? VersionId = null,
    Guid? FloorId = null,
    SpaceAuditEvidence? Evidence = null,
    string? BeforeHash = null,
    string? AfterHash = null,
    int? AttemptNo = null,
    string? ClientType = null,
    string? IpAddress = null,
    string? UserAgent = null);

public interface ISpaceAuditWriter
{
    Task<bool> TryAppendAsync(
        SpaceAuditEventInput input,
        CancellationToken ct = default);
}

public interface ISpaceAuditDbContextFactory
{
    CP6Context CreateDbContext();
}

public sealed record SpaceRetryFinalizationInput(
    Guid EventId,
    Guid TenantId,
    Guid RetryLeaseId,
    int ExpectedAttempts,
    string Status,
    string? LastError,
    DateTime? NextRetryAt,
    SpaceAuditEventInput Audit,
    Guid AuditId = default,
    Guid? ExpectedCompletionLeaseId = null,
    bool? ExpectedCompletionSucceeded = null);

public enum SpaceRetryFinalizationResult
{
    Committed,
    LostLease,
    AuditUnavailable,
}

public interface ISpaceRetryFinalizer
{
    Task<SpaceRetryFinalizationResult> TryFinalizeAsync(
        SpaceRetryFinalizationInput input,
        CancellationToken ct = default);
}
