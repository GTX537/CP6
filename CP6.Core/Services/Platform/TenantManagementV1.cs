using System.Text.Json.Serialization;

namespace CP6.Core.Services.Platform;

// The host validates the original full session under the shared IAM gate. Its final
// check covers clock/logout state outside that gate, not policy invalidation caused
// by this authorized command itself (for example suspending the actor's own tenant).
public sealed record TenantManagementSessionV1(Func<CancellationToken, Task<Guid>> CurrentActor, Func<CancellationToken, Task> Complete);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantConfigurationV1(
    [property: JsonRequired] string Name,
    [property: JsonRequired] DateTime? ExpireDate,
    [property: JsonRequired] string? Remark,
    [property: JsonRequired] string? TimeZoneId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantManagementCommandV1(
    [property: JsonRequired] string OperationRef,
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long ExpectedRevision,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] TenantConfigurationV1? Configuration);

public sealed record TenantManagementSnapshotV1(Guid Id, string TenantCode, string TenantName, bool Enable,
    DateTime? ExpireDate, string? Remark, string? TimeZoneId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Revision);
public sealed record TenantManagementReceiptV1(string OperationRef, string Kind, TenantManagementSnapshotV1 Snapshot, DateTimeOffset CommittedAt);
public sealed class TenantManagementExceptionV1(string code, int status = 409, Exception? inner = null) : InvalidOperationException(code, inner)
{
    public int Status { get; } = status;
}
