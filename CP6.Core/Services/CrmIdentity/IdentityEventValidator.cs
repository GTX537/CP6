using System.Text.Json;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;

namespace CP6.Core.Services.CrmIdentity;

/// <summary>Validates local pinned Schema bytes and the identities shared by SQL, transport and data.</summary>
public sealed class IdentityEventValidator : ICp6OutboxEnvelopeValidator, ICp6InboxDeliveryValidator
{
    private readonly Cp6CloudEventValidator platform;
    private readonly string issuer;
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "displayname", "username", "nickname", "email", "emailaddress", "phone", "phonenumber",
        "mobile", "address", "password", "passwordhash", "cookie", "token", "accesstoken", "refreshtoken",
        "clientsecret", "privatekey", "authorization", "secret", "idtoken", "signingkey"
    };

    public IdentityEventValidator(Cp6ContractBundle bundle, string issuer)
    {
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Identity issuer must be an absolute HTTPS URI.", nameof(issuer));
        platform = new(bundle);
        this.issuer = issuer;
    }

    public bool IsValid(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length is 0 or > 1048576) return false;
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (!SafeProperties(root) || !platform.Validate(payload).IsValid) return false;
            var data = root.GetProperty("data");
            var tenant = root.GetProperty("tenantid").GetString();
            var aggregate = root.GetProperty("aggregateid").GetString();
            if (data.GetProperty("tenantId").GetString() != tenant ||
                data.GetProperty("version").GetInt32() != root.GetProperty("aggregateversion").GetInt32() ||
                root.GetProperty("subject").GetString() != $"tenants/{tenant}/{aggregate}") return false;
            var expected = root.GetProperty("type").GetString() switch
            {
                IdentityEventContracts.TenantChanged => "tenant:" + tenant,
                IdentityEventContracts.UserChanged => "user:" + data.GetProperty("userId").GetString(),
                IdentityEventContracts.DepartmentChanged => "department:" + data.GetProperty("deptId").GetString(),
                IdentityEventContracts.PermissionChanged => "permission:" + data.GetProperty("subjectId").GetString(),
                IdentityEventContracts.TokenRevoked when data.GetProperty("issuer").GetString() == issuer
                    => IdentityEventContracts.TokenAggregate(issuer, data.GetProperty("jti").GetString()!),
                _ => null
            };
            if (expected is null || aggregate != expected) return false;
            if (data.TryGetProperty("deleted", out var tombstone) && tombstone.GetBoolean() &&
                (!data.TryGetProperty("enabled", out var enabled) || enabled.GetBoolean())) return false;
            if (data.TryGetProperty("roles", out var roles) && roles.EnumerateArray().Any(r => !ValidSubject(r.GetString()!, allowService: false))) return false;
            if (data.TryGetProperty("subjectId", out var subject) && !ValidSubject(subject.GetString()!, allowService: true)) return false;
            if (root.GetProperty("type").GetString() == IdentityEventContracts.DepartmentChanged)
            {
                var segments = data.GetProperty("path").GetString()!.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Distinct().Count() != segments.Length ||
                    segments[^1] != data.GetProperty("deptId").GetString() ||
                    segments.Any(s => !Guid.TryParseExact(s, "D", out var id) || id == Guid.Empty)) return false;
                var parent = data.GetProperty("parentId");
                if (parent.ValueKind == JsonValueKind.Null ? segments.Length != 1 :
                    segments.Length < 2 || segments[^2] != parent.GetString()) return false;
            }
            if (root.GetProperty("type").GetString() == IdentityEventContracts.PermissionChanged)
            {
                var grants = data.GetProperty("grants").EnumerateArray().ToArray();
                if (grants.Select(g => (g.GetProperty("resource").GetString(), g.GetProperty("action").GetString()))
                    .Distinct().Count() != grants.Length) return false;
                if (!data.GetProperty("enabled").GetBoolean() && grants.Length != 0) return false;
                foreach (var grant in grants)
                {
                    if (grant.GetProperty("scope").GetInt32() != 4 && grant.GetProperty("deptIds").GetArrayLength() != 0) return false;
                    var fields = grant.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("field").GetString()).ToArray();
                    if (fields.Distinct(StringComparer.Ordinal).Count() != fields.Length) return false;
                }
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        { return false; }
    }

    public Cp6MessageValidationResult Validate(Cp6OutboxEnvelope envelope)
        => ValidateMetadata(envelope.Payload, envelope.MessageId, envelope.TenantId, envelope.TopicName,
            envelope.PartitionKey, envelope.AggregateId, envelope.AggregateVersion, envelope.CorrelationId, envelope.CausationId);

    public Cp6MessageValidationResult Validate(Cp6InboxDelivery delivery)
        => ValidateMetadata(delivery.Payload, delivery.MessageId, delivery.TenantId, delivery.TopicName,
            delivery.PartitionKey, delivery.AggregateId, delivery.AggregateVersion);

    private Cp6MessageValidationResult ValidateMetadata(ReadOnlyMemory<byte> payload, string id, Guid tenant,
        string topic, string partition, string aggregate, int version, string? correlation = null, string? causation = null)
    {
        if (topic != IdentityEventContracts.Topic || partition != $"{tenant:D}:{aggregate}" || !IsValid(payload))
            return Cp6MessageValidationResult.Invalid("C02_IDENTITY_CONTRACT_INVALID");
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        return root.GetProperty("id").GetString() == id && root.GetProperty("tenantid").GetString() == tenant.ToString("D") &&
            root.GetProperty("aggregateid").GetString() == aggregate && root.GetProperty("aggregateversion").GetInt32() == version &&
            (correlation is null || root.GetProperty("correlationid").GetString() == correlation) &&
            (causation is null || root.GetProperty("causationid").GetString() == causation)
            ? Cp6MessageValidationResult.Valid : Cp6MessageValidationResult.Invalid("C02_IDENTITY_METADATA_MISMATCH");
    }

    private static bool SafeProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().All(SafeProperties);
        if (value.ValueKind != JsonValueKind.Object) return true;
        var names = new HashSet<string>(StringComparer.Ordinal);
        return value.EnumerateObject().All(p => names.Add(p.Name) &&
            !SensitiveNames.Contains(p.Name.Replace("_", "").Replace("-", "")) && SafeProperties(p.Value));
    }

    private static bool ValidSubject(string subject, bool allowService)
    {
        if (subject.StartsWith("role:", StringComparison.Ordinal)) return int.TryParse(subject[5..], out var role) && role > 0;
        if (subject.StartsWith("user:", StringComparison.Ordinal)) return Guid.TryParseExact(subject[5..], "D", out var user) && user != Guid.Empty;
        return allowService && subject.StartsWith("service:", StringComparison.Ordinal);
    }
}
