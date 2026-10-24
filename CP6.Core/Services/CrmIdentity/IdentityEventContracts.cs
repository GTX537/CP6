using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;

namespace CP6.Core.Services.CrmIdentity;

public static class IdentityEventContracts
{
    public const string Topic = "cp6.platform.events.v1";
    public const string TenantChanged = "com.gtx537.platform.tenant.changed.v1";
    public const string UserChanged = "com.gtx537.platform.user.changed.v1";
    public const string DepartmentChanged = "com.gtx537.platform.department.changed.v1";
    public const string PermissionChanged = "com.gtx537.platform.permission.changed.v1";
    public const string TokenRevoked = "com.gtx537.platform.token.revoked.v1";
    public const string UserChangedV2 = "com.gtx537.platform.user.changed.v2";
    public const string PermissionChangedV2 = "com.gtx537.platform.permission.changed.v2";
    public const string PermissionChangedV3 = "com.gtx537.platform.permission.changed.v3";
    public const string TokenRevokedV2 = "com.gtx537.platform.token.revoked.v2";
    public static readonly JsonSerializerOptions Json = CreateJsonOptions();

    public static bool IsUserChanged(string? type) => type is UserChanged or UserChangedV2;
    public static bool IsPermissionChanged(string? type) => type is PermissionChanged or PermissionChangedV2 or PermissionChangedV3;
    public static bool IsTokenRevoked(string? type) => type is TokenRevoked or TokenRevokedV2;
    public static bool IsFullIntegerRoleVersion(string? type) => type is UserChangedV2 or PermissionChangedV2 or PermissionChangedV3 or TokenRevokedV2;
    public static bool IsSupported(string? type) => type is TenantChanged or DepartmentChanged
        || IsUserChanged(type) || IsPermissionChanged(type) || IsTokenRevoked(type);
    public static string RoleSubject(int roleId) => "role:" + roleId.ToString(CultureInfo.InvariantCulture);
    public static bool TryRoleSubject(string subject, out int roleId)
    {
        roleId = 0;
        return subject.StartsWith("role:", StringComparison.Ordinal)
            && int.TryParse(subject.AsSpan(5), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out roleId)
            && subject == RoleSubject(roleId);
    }

    public static string TokenAggregate(string issuer, string jti) => "token:" + Hash(Encoding.UTF8.GetBytes(issuer)) + ":" + jti;
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static Cp6OutboxEnvelope Create<T>(Guid tenantId, string aggregateId, int version, string type,
        T data, string region, string correlationId, string causationId, TimeProvider? clock = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var descriptor = new Cp6CloudEventDescriptor(id, new Uri("urn:cp6:platform"), type,
            $"tenants/{tenantId:D}/{aggregateId}", (clock ?? TimeProvider.System).GetUtcNow(),
            Cp6EventContractIdentity.Parse(type).SchemaId, tenantId, correlationId, causationId, aggregateId,
            version, Cp6EventContractIdentity.Parse(type).MajorVersion.ToString(CultureInfo.InvariantCulture) + ".0.0", region);
        var bytes = Cp6CloudEventCodec.EncodeStructured(Cp6CloudEventCodec.Create(descriptor,
            JsonSerializer.SerializeToElement(data, Json)));
        return new(id, tenantId, Topic, $"{tenantId:D}:{aggregateId}", bytes, correlationId, causationId, aggregateId, version);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new UtcDateTimeOffsetConverter());
        return options;
    }

    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => reader.GetDateTimeOffset().ToUniversalTime();
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.UtcDateTime.ToString("O"));
    }
}

public sealed record TenantIdentityData(Guid TenantId, bool Enabled, int Version, DateTimeOffset? ExpiresAtUtc);
public sealed record UserIdentityData(Guid TenantId, Guid UserId, Guid? DeptId, bool Enabled, int Version,
    string[] Roles, Guid? ManagerId, Guid AuthenticationEpoch, bool MustChangePassword,
    DateTimeOffset? ValidAfterUtc, DateTimeOffset? PasswordExpiresAtUtc, DateTimeOffset? LockedUntilUtc);
public sealed record DepartmentIdentityData(Guid TenantId, Guid DeptId, Guid? ParentId, string Path,
    bool Enabled, int Version, Guid? LeaderId);
public sealed record IdentityFieldPermission(string Field, int Access);
public sealed record IdentityGrant(string Resource, string Action, int Scope, Guid[] DeptIds, IdentityFieldPermission[] Fields);
public sealed record PermissionIdentityData(Guid TenantId, string SubjectId, bool Enabled, int Version, IdentityGrant[] Grants);
public sealed record InquirySiteFormScopeV1(Guid SiteId, Guid FormId);
public sealed record PermissionIdentityDataV3(Guid TenantId, string SubjectId, bool Enabled, int Version,
    IdentityGrant[] Grants, InquirySiteFormScopeV1[] SiteFormScopes);
public sealed record TokenRevokedIdentityData(Guid TenantId, string Issuer, string Jti, string SubjectId,
    DateTimeOffset ExpiresAtUtc, int Version);
