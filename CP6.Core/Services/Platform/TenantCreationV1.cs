using System.Text.Json.Serialization;

namespace CP6.Core.Services.Platform;

public sealed record TenantCreationTemplateV1(string Version, int[] MenuIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TenantCreationCommandV1(
    [property: JsonRequired] string OperationRef,
    [property: JsonRequired] string ExpectedTemplateVersion,
    [property: JsonRequired] string Code,
    [property: JsonRequired] string Name,
    [property: JsonRequired] DateTime? ExpireDate,
    [property: JsonRequired] string? Remark,
    [property: JsonRequired] string AdminUserName);
// This is historical creation evidence, never a current tenant/credential status or a password store.
public sealed record TenantCreationReceiptV1(string OperationRef, Guid TenantId, string TenantCode,
    Guid AdminUserId, string AdminUserName, TenantCreationTemplateV1 Template, DateTimeOffset CommittedAt);
public sealed record TenantCreationResultV1(TenantCreationReceiptV1 Receipt, string? TempPassword, bool NeedsCredentialReset);
