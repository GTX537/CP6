using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public static readonly JsonSerializerOptions Json = CreateJsonOptions();

    public static string TokenAggregate(string issuer, string jti) => "token:" + Hash(Encoding.UTF8.GetBytes(issuer)) + ":" + jti;
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static Cp6OutboxEnvelope Create<T>(Guid tenantId, string aggregateId, int version, string type,
        T data, string region, string correlationId, string causationId, TimeProvider? clock = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var descriptor = new Cp6CloudEventDescriptor(id, new Uri("urn:cp6:platform"), type,
            $"tenants/{tenantId:D}/{aggregateId}", (clock ?? TimeProvider.System).GetUtcNow(),
            Cp6EventContractIdentity.Parse(type).SchemaId, tenantId, correlationId, causationId, aggregateId,
            version, "1.0.0", region);
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
public sealed record TokenRevokedIdentityData(Guid TenantId, string Issuer, string Jti, string SubjectId,
    DateTimeOffset ExpiresAtUtc, int Version);
