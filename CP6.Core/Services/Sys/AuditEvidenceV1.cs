using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Auth;

namespace CP6.Core.Services.Sys;

public sealed record AuditEvidenceV1(
    string Source, string Reliability, string ActorKind, Guid? ActualActor = null, Guid? EffectiveActor = null,
    string? Service = null, string? PermissionVersion = null, Guid? OperationId = null,
    string? Outcome = null, string? TraceId = null, string? EntityVersion = null, DateTimeOffset? RecordedAtUtc = null,
    string? TransactionRef = null, string? DataPolicy = null, string? Action = null, string? OriginalOperationDigest = null)
{
    public const string HttpItem = "CP6.AuditEvidence.v1";
    public const string PermissionVersionItem = "CP6.AuditPermissionVersion.v1";
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public int SchemaVersion => 1;
    public string Encode() => JsonSerializer.Serialize(this, Format);

    internal static AuditEvidenceV1 RequiredPlatform(PlatformAdminAuthorization.Actor actor, string action, DateTimeOffset at,
        string? originalOperationDigest = null) => new("PLATFORM_OWNER_UTC", "REQUIRED_AUDIT", "USER", actor.Id, actor.Id,
        PermissionVersion: AuthenticationPolicyServiceV1.Hash(new { Policy = "PLATFORM_ADMIN_CURRENT_V1", actor.Id, actor.TenantId,
            actor.AuthenticationVersion, actor.MustChangePassword }), Outcome: "OWNER_RECORDED", RecordedAtUtc: at,
        DataPolicy: "PLATFORM_SECURITY_V1", Action: action, OriginalOperationDigest: originalOperationDigest);

    internal static AuditEvidenceV1? Decode(string? value)
    {
        if (value is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(value);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("schemaVersion", out var version)
                || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
                return null;
            return doc.RootElement.Deserialize<AuditEvidenceV1>(Format);
        }
        catch (JsonException) { return null; }
    }

    internal static string PermissionDecisionVersion(Guid tenant, UserPermissionContext current)
        => AuthenticationPolicyServiceV1.Hash(new
        {
            tenant, current.UserId, current.DeptId, current.DeptPath,
            roles = current.RoleIds.Order().ToArray(),
            actions = current.ActionKeys.Order(StringComparer.Ordinal).ToArray(),
            scopes = current.DataScopes.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            departments = current.CustomDeptIds.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new { x.Key, ids = x.Value.Order().ToArray() }).ToArray(),
            fields = current.FieldPerms.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new { x.Key, fields = x.Value.OrderBy(y => y.Key, StringComparer.Ordinal).ToArray() }).ToArray()
        });
}
