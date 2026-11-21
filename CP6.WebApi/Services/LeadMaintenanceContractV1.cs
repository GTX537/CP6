using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using NSec.Cryptography;

namespace CP6.WebApi.Services;

public sealed class LeadMaintenanceOptionsV1
{
    public bool Enabled { get; set; }
    public List<LeadMaintenanceProviderKeyV1> ProviderKeys { get; set; } = [];
}

public sealed class LeadMaintenanceProviderKeyV1
{
    public Guid TenantId { get; set; }
    public string TargetKind { get; set; } = "";
    public string Component { get; set; } = "";
    public string Authority { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string PublicKeyBase64 { get; set; } = "";
}

/// <summary>由 Main 宿主明确登记的 Owner 公钥。请求和数据库中的材料都不能添加信任。</summary>
public sealed class LeadMaintenanceTrustV1
{
    private readonly Dictionary<(Guid Tenant, string Kind, string Component, string Authority), (string Id, PublicKey Key)> keys = [];
    public bool Enabled { get; }

    public LeadMaintenanceTrustV1(LeadMaintenanceOptionsV1 options)
    {
        Enabled = options.Enabled;
        if (!Enabled) return;
        try
        {
            if (options.ProviderKeys is not { Count: > 0 }) throw new FormatException();
            foreach (var item in options.ProviderKeys)
            {
                if (item is null || item.TenantId == Guid.Empty || !LeadMaintenanceContractV1.IsKind(item.TargetKind)
                    || !LeadMaintenanceContractV1.IsComponent(item.Component) || !LeadMaintenanceContractV1.IsToken(item.Authority)
                    || !LeadMaintenanceContractV1.IsToken(item.KeyId)) throw new FormatException();
                var bytes = Convert.FromBase64String(item.PublicKeyBase64);
                if (bytes.Length != 32 || Convert.ToBase64String(bytes) != item.PublicKeyBase64) throw new FormatException();
                if (!keys.TryAdd((item.TenantId, item.TargetKind, item.Component, item.Authority),
                        (item.KeyId, PublicKey.Import(SignatureAlgorithm.Ed25519, bytes, KeyBlobFormat.RawPublicKey))))
                    throw new FormatException();
            }
        }
        catch (Exception error) when (error is FormatException or ArgumentException or CryptographicException)
        {
            throw new InvalidOperationException("LEAD_MAINTENANCE_PROVIDER_CONFIGURATION_INVALID");
        }
    }

    internal LeadMaintenanceEvidenceWireV1 Verify(LeadMaintenanceTargetV1 target, string authority, LeadMaintenanceEvidenceRowV1 row)
    {
        if (!keys.TryGetValue((target.TenantId, target.TargetKind, row.Component, authority), out var registered)
            || !SignatureAlgorithm.Ed25519.Verify(registered.Key,
                LeadMaintenanceContractV1.EvidenceInput(row.Component, row.Payload), row.Signature))
            throw LeadMaintenanceContractV1.Unavailable();
        return new(row.Component, registered.Id, LeadMaintenanceContractV1.Sha256(row.Payload),
            WebEncoders.Base64UrlEncode(row.Payload), WebEncoders.Base64UrlEncode(row.Signature));
    }
}

internal static class LeadMaintenanceContractV1
{
    internal const string Version = "cp6-lead-maintenance-context-v1";
    internal const string Inbound = "INDEPENDENT_INBOUND";
    internal const string ManualLead = "MANUAL_LEAD";
    internal const string Source = "SOURCE";
    internal const string Policy = "POLICY";
    internal const string Hold = "HOLD";
    internal const string GetPolicy = "privacy:policy";
    internal const string GetHold = "privacy:hold";
    internal const string Maintain = "privacy:maintain";
    internal const string InboundClass = "H01_QUARANTINED_RAW";
    internal const string InboundPurpose = "CRM_LEAD_INBOUND_RETENTION";
    internal const string LeadClass = "MANUAL_LEAD_PII";
    internal const string LeadPurpose = "CRM_LEAD_RETENTION";
    internal const int LeadRetentionMonths = 24;

    private static readonly Regex Token = new(@"\A[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}\z", RegexOptions.CultureInvariant);
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 12
    };

    internal static bool IsKind(string? value) => value is Inbound or ManualLead;
    internal static bool IsComponent(string? value) => value is Source or Policy or Hold;
    internal static bool IsToken(string? value) => value is { Length: > 0 and <= 128 } && Token.IsMatch(value);
    internal static bool IsDigest(string? value) =>
        value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static bool IsUtc(DateTimeOffset value) => value != default && value.Offset == TimeSpan.Zero;
    internal static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    internal static byte[] EvidenceInput(string component, byte[] payload) =>
        Framed("cp6-lead-maintenance-evidence-v1\n" + component + "\n", payload);
    internal static byte[] ContextInput(string issuer, string keyId, byte[] payload) =>
        Framed(Version + "\n" + issuer + "\n" + keyId + "\n", payload);
    internal static string ScopeDigest(LeadMaintenanceRequestV1 request) => Sha256(Encoding.UTF8.GetBytes(
        FormattableString.Invariant($"cp6-lead-maintenance-scope-v1\n{request.TenantId:D}\n{request.TargetKind}\n{request.Authority}\n{request.ObjectId:D}\n{request.SourceMessageId}\n{request.Purpose}")));

    private static byte[] Framed(string prefix, byte[] payload)
    {
        var header = Encoding.UTF8.GetBytes(prefix);
        var result = new byte[header.Length + payload.Length];
        header.CopyTo(result, 0);
        payload.CopyTo(result, header.Length);
        return result;
    }

    internal static T Read<T>(JsonElement element)
    {
        RejectDuplicateMembers(element);
        return element.Deserialize<T>(Json) ?? throw new JsonException();
    }

    internal static T ReadEvidence<T>(LeadMaintenanceEvidenceRowV1 row, LeadMaintenanceTargetV1 target)
        where T : LeadMaintenanceOwnerEvidenceV1
    {
        using var document = JsonDocument.Parse(row.Payload, new JsonDocumentOptions { MaxDepth = Json.MaxDepth });
        var evidence = Read<T>(document.RootElement);
        if (evidence.Target != target || !IsToken(evidence.ProviderAuthority) || !IsToken(evidence.Version)
            || evidence.Sequence != row.Sequence.ToString(CultureInfo.InvariantCulture)) throw Unavailable();
        return evidence;
    }

    private static void RejectDuplicateMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in element.EnumerateObject())
            {
                if (!names.Add(member.Name)) throw new JsonException();
                RejectDuplicateMembers(member.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateMembers(item);
    }

    internal static InquiryPermissionExceptionV1 Unavailable() => new(503, "lead_maintenance_authority_unavailable");
    internal static InquiryPermissionExceptionV1 Denied() => new(403, "insufficient_scope");
}

internal sealed class LeadMaintenanceRequestV1
{
    public required Guid TenantId { get; init; }
    public required string TargetKind { get; init; }
    public required string Authority { get; init; }
    public required Guid ObjectId { get; init; }
    public required string? SourceMessageId { get; init; }
    public required string Purpose { get; init; }
    public required string RequestNonce { get; init; }
    internal LeadMaintenanceTargetV1 Target => new(TenantId, TargetKind, Authority, ObjectId);

    internal void Validate()
    {
        if (TenantId == Guid.Empty || ObjectId == Guid.Empty || !LeadMaintenanceContractV1.IsKind(TargetKind)
            || !LeadMaintenanceContractV1.IsToken(Authority) || RequestNonce is null
            || !InquiryPermissionSigningKeyV1.ValidServiceNonce(RequestNonce)
            || (TargetKind == LeadMaintenanceContractV1.Inbound ? !LeadMaintenanceContractV1.IsToken(SourceMessageId) : SourceMessageId is not null))
            throw new InquiryPermissionExceptionV1(400, "invalid_request");
        if (Purpose is not (LeadMaintenanceContractV1.GetPolicy or LeadMaintenanceContractV1.GetHold or LeadMaintenanceContractV1.Maintain))
            throw new InquiryPermissionExceptionV1(400, "unsupported_projection_purpose");
    }
}

internal sealed record LeadMaintenanceTargetV1(Guid TenantId, string TargetKind, string Authority, Guid ObjectId);

internal abstract class LeadMaintenanceOwnerEvidenceV1
{
    public required LeadMaintenanceTargetV1 Target { get; init; }
    public required string ProviderAuthority { get; init; }
    public required string Version { get; init; }
    public required string Sequence { get; init; }
}

internal sealed class LeadMaintenanceSourceV1 : LeadMaintenanceOwnerEvidenceV1
{
    public required string? SourceMessageId { get; init; }
    public required DateTimeOffset ReceivedAtUtc { get; init; }
    public required string PolicyAuthority { get; init; }
}

internal sealed class LeadMaintenancePolicyV1 : LeadMaintenanceOwnerEvidenceV1
{
    public required string SourceSha256 { get; init; }
    public required string ContentDigest { get; init; }
    public required string PayloadClass { get; init; }
    public required string Purpose { get; init; }
    public required DateTimeOffset EffectiveFromUtc { get; init; }
    public required DateTimeOffset? EffectiveToUtc { get; init; }
    public required int? RetainSeconds { get; init; }
    public required int? RetainCalendarMonths { get; init; }
    public required string HoldAuthority { get; init; }
    public required string KeyPolicyVersion { get; init; }
}

internal sealed class LeadMaintenanceHoldV1 : LeadMaintenanceOwnerEvidenceV1
{
    public required string SourceSha256 { get; init; }
    public required string PolicySha256 { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required DateTimeOffset ValidUntilUtc { get; init; }
    public required LeadMaintenanceHoldWindowV1[] Windows { get; init; }
}

internal sealed class LeadMaintenanceHoldWindowV1
{
    public required DateTimeOffset FromUtc { get; init; }
    public required DateTimeOffset? ToUtc { get; init; }
}

internal sealed record LeadMaintenanceEvidenceRowV1(string Component, long Sequence, byte[] Payload, byte[] Signature,
    string? ObjectAuthority, string? SourceMessageId);
internal sealed record LeadMaintenanceEvidenceWireV1(string Component, string KeyId, string Sha256, string Payload, string Signature);

public sealed partial class InquiryPermissionSigningKeyV1
{
    internal DateTimeOffset LeadMaintenanceExpiry(DateTimeOffset now, DateTimeOffset maxExpiry)
    {
        if (!Enabled || key is null || revoked || now < from || until is { } end && now >= end)
            throw LeadMaintenanceContractV1.Unavailable();
        return until is { } deadline && deadline < maxExpiry ? deadline : maxExpiry;
    }

    internal string SignLeadMaintenance(byte[] payload)
    {
        var proof = SignatureAlgorithm.Ed25519.Sign(key!, LeadMaintenanceContractV1.ContextInput(issuer, keyId, payload));
        return JsonSerializer.Serialize(new
        {
            schemaVersion = LeadMaintenanceContractV1.Version, issuer, keyId, algorithm = "Ed25519",
            payload = WebEncoders.Base64UrlEncode(payload), signature = WebEncoders.Base64UrlEncode(proof)
        }, LeadMaintenanceContractV1.Json);
    }
}
