using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NSec.Cryptography;

namespace CP6.WebApi.Services;

/// <summary>Explicit trusted host configuration. No key generation or request-supplied trust.</summary>
public sealed class InquiryPermissionSigningOptionsV1
{
    public bool Enabled { get; set; }
    public string Issuer { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string PrivateKeyBase64 { get; set; } = "";
    public string PublicKeyBase64 { get; set; } = "";
    public DateTimeOffset ActiveFromUtc { get; set; }
    public DateTimeOffset? ActiveToUtc { get; set; }
    public bool Revoked { get; set; }
}

public sealed partial class InquiryPermissionSigningKeyV1 : IDisposable
{
    public const string CatalogVersion = "crm-permission-catalog-1";
    private readonly Key? key;
    private readonly string issuer = "", keyId = "";
    private readonly DateTimeOffset from;
    private readonly DateTimeOffset? until;
    private readonly bool revoked;
    public bool Enabled { get; }

    public InquiryPermissionSigningKeyV1(InquiryPermissionSigningOptionsV1 options, CrmOidcOptions oidc)
    {
        Enabled = options.Enabled;
        if (!Enabled) return;
        if (!oidc.Enabled || string.IsNullOrEmpty(options.Issuer) || string.IsNullOrEmpty(options.KeyId)
            || options.Issuer != oidc.Issuer || options.Issuer.Length > 200
            || !Regex.IsMatch(options.Issuer, @"\Ahttps://iam\.[a-z0-9.-]+\z", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(options.KeyId, @"\A[A-Za-z0-9._-]{8,80}\z", RegexOptions.CultureInvariant)
            || options.ActiveFromUtc == default || options.ActiveFromUtc.Offset != TimeSpan.Zero
            || options.ActiveToUtc is { } end && (end.Offset != TimeSpan.Zero || end <= options.ActiveFromUtc))
            throw new InvalidOperationException("INQUIRY_SIGNING_CONFIGURATION_INVALID");
        byte[]? privateBytes = null;
        try
        {
            privateBytes = Convert.FromBase64String(options.PrivateKeyBase64);
            var publicBytes = Convert.FromBase64String(options.PublicKeyBase64);
            if (privateBytes.Length != 32 || publicBytes.Length != 32
                || Convert.ToBase64String(privateBytes) != options.PrivateKeyBase64
                || Convert.ToBase64String(publicBytes) != options.PublicKeyBase64)
                throw new FormatException();
            key = Key.Import(SignatureAlgorithm.Ed25519, privateBytes, KeyBlobFormat.RawPrivateKey);
            if (!CryptographicOperations.FixedTimeEquals(key.PublicKey.Export(KeyBlobFormat.RawPublicKey), publicBytes))
                throw new FormatException();
        }
        catch (Exception error) when (error is FormatException or ArgumentException or CryptographicException)
        { key?.Dispose(); throw new InvalidOperationException("INQUIRY_SIGNING_CONFIGURATION_INVALID"); }
        finally { if (privateBytes is not null) CryptographicOperations.ZeroMemory(privateBytes); }
        issuer = options.Issuer; keyId = options.KeyId; from = options.ActiveFromUtc; until = options.ActiveToUtc; revoked = options.Revoked;
    }

    internal string Sign(InquiryPermissionSubjectV1 subject, InquiryPermissionRequestV1 request, InquiryPermissionFactsV1 facts,
        DateTimeOffset now, DateTimeOffset expires)
    {
        if (!Enabled || key is null || revoked || now < from || until is { } end && now >= end)
            throw new InquiryPermissionExceptionV1(503, "inquiry_signer_unavailable");
        if (!WireId(subject.TenantId) || !WireId(subject.SubjectId) || !WireId(request.SiteId) || !WireId(request.FormId)
            || facts.Departments.Any(x => !WireId(x)))
            throw new InquiryPermissionExceptionV1(422, "inquiry_projection_identity_not_representable");
        if (expires <= now || expires - now > TimeSpan.FromMinutes(5))
            throw new InquiryPermissionExceptionV1(401, "invalid_token");
        var body = new JsonObject
        {
            ["schemaVersion"] = "crm-iam-projection-v1", ["issuer"] = issuer, ["keyId"] = keyId,
            ["algorithm"] = "Ed25519", ["catalogVersion"] = CatalogVersion, ["decision"] = "Allow",
            ["organizationId"] = subject.TenantId.ToString("D"), ["subjectId"] = subject.SubjectId.ToString("D"),
            ["purpose"] = request.Purpose, ["issuedAtUtc"] = Wire(now), ["expiresAtUtc"] = Wire(expires),
            ["permissions"] = new JsonArray(facts.Permissions.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["fieldPermissions"] = new JsonArray(facts.Fields.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["managedDepartmentIds"] = new JsonArray(facts.Departments.Select(x => (JsonNode?)JsonValue.Create(x.ToString("D"))).ToArray()),
            ["scopes"] = new JsonObject { ["siteIds"] = new JsonArray(request.SiteId.ToString("D")), ["formIds"] = new JsonArray(request.FormId.ToString("D")) },
            ["isSupervisor"] = facts.Supervisor
        };
        // Exact frozen v1 canonicalization: ordinal object keys, unchanged array order and
        // Utf8JsonWriter's standard string escaping. Signature is omitted while signing.
        static JsonNode? Sort(JsonNode? node) => node switch
        {
            JsonObject obj => new JsonObject(obj.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new KeyValuePair<string, JsonNode?>(x.Key, Sort(x.Value)))),
            JsonArray array => new JsonArray(array.Select(Sort).ToArray()),
            _ => node?.DeepClone()
        };
        var canonical = Encoding.UTF8.GetBytes(Sort(body)!.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        body["signature"] = Convert.ToBase64String(SignatureAlgorithm.Ed25519.Sign(key, canonical)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return body.ToJsonString();
    }
    private static bool WireId(Guid value)
    { var s = value.ToString("D"); return s[14] == '4' && "89ab".Contains(s[19]); }
    private static string Wire(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    public override string ToString() => nameof(InquiryPermissionSigningKeyV1);
    public void Dispose() => key?.Dispose();
}
