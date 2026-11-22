using System.Text.Json;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;

namespace CP6.Core.Services.ErpIntegration;

/// <summary>Validates pinned C03 schemas and the identities shared by data, transport and SQL.</summary>
public sealed class ErpEventValidator : ICp6OutboxEnvelopeValidator, ICp6InboxDeliveryValidator
{
    private readonly Cp6CloudEventValidator platform;
    private readonly Cp6CloudEventValidator? businessPartnerV2;
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "names", "displayname", "username", "nickname", "firstname", "lastname", "fullname",
        "customername", "companyname", "contactname", "legalname", "email", "emails", "emailaddress",
        "phone", "phonenumber", "mobile", "address", "addresses", "billingaddress", "shippingaddress",
        "tax", "taxid", "taxnumber", "taxrate", "vatnumber", "legal", "legalentity",
        "password", "passwordhash", "cookie", "token", "accesstoken", "refreshtoken", "idtoken",
        "secret", "clientsecret", "privatekey", "authorization", "signingkey", "price", "prices",
        "unitprice", "unitprices", "pricelist", "orderlines", "quotationlines", "lines", "bankaccount", "creditcard"
    };

    public ErpEventValidator(Cp6ContractBundle bundle) : this(bundle, null) { }

    private ErpEventValidator(Cp6ContractBundle bundle, Cp6ContractBundle? compatible)
    {
        platform = new(bundle);
        businessPartnerV2 = compatible is null ? null : new(compatible);
    }

    public static ErpEventValidator Load(string root) => new(Cp6ContractBundle.Load(root),
        Cp6ContractBundle.Load(Path.Combine(root, "business-partner-synchronized", "v2"),
            ErpEventContracts.BusinessPartnerV2SchemaVersion, ErpEventContracts.BusinessPartnerV2BundleSha256));

    internal bool SupportsBusinessPartnerSuccessV2 => businessPartnerV2 is not null;

    public bool IsValid(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length is 0 or > 1048576) return false;
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (!SafeProperties(root)) return false;
            var type = root.GetProperty("type").GetString()!;
            var schema = type == ErpEventContracts.BusinessPartnerSynchronizedV2 ? businessPartnerV2 : platform;
            if (schema?.Validate(payload).IsValid != true) return false;
            var topic = ErpEventContracts.TopicFor(type);
            var data = root.GetProperty("data");
            var tenant = root.GetProperty("tenantid").GetString();
            var aggregate = root.GetProperty("aggregateid").GetString();
            var order = type is ErpEventContracts.OrderRequested or ErpEventContracts.OrderCreated or ErpEventContracts.OrderFailed;
            if (root.GetProperty("source").GetString() != (topic == ErpEventContracts.RequestTopic
                    ? ErpEventContracts.RequestSource : ErpEventContracts.ResultSource) ||
                data.GetProperty("tenantId").GetString() != tenant ||
                data.GetProperty(order ? "opportunityId" : "accountId").GetString() != aggregate ||
                root.GetProperty("subject").GetString() != $"tenants/{tenant}/{aggregate}") return false;

            // CRM aggregate revisions and request versions are independent. ERP result revisions must agree.
            if (topic == ErpEventContracts.ResultTopic && data.GetProperty("resultVersion").GetInt32() !=
                root.GetProperty("aggregateversion").GetInt32()) return false;
            if (type == ErpEventContracts.OrderRequested)
            {
                var rowVersion = data.GetProperty("quotationVersion").GetString()!;
                var decoded = Convert.FromBase64String(rowVersion);
                if (decoded.Length != 8 || Convert.ToBase64String(decoded) != rowVersion ||
                    !data.GetProperty("expectedAmount").TryGetDecimal(out var expected) || expected < 0) return false;
            }
            if (type == ErpEventContracts.OrderCreated &&
                (!data.GetProperty("bookedAmount").TryGetDecimal(out var booked) || booked < 0)) return false;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            KeyNotFoundException or FormatException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>按已固定的兼容合同解码原件，保留伙伴键的每个字符。</summary>
    public ErpBusinessPartnerSynchronized? DecodeBusinessPartnerSuccessV2(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length is 0 or > 1048576) return null;
        var bytes = payload.ToArray();
        if (!IsValid(bytes)) return null;
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        return root.GetProperty("type").GetString() == ErpEventContracts.BusinessPartnerSynchronizedV2
            ? root.GetProperty("data").Deserialize<ErpBusinessPartnerSynchronized>(ErpEventContracts.Json)
            : null;
    }

    public Cp6MessageValidationResult Validate(Cp6OutboxEnvelope envelope)
        => ValidateMetadata(envelope.Payload, envelope.MessageId, envelope.TenantId, envelope.TopicName,
            envelope.PartitionKey, envelope.AggregateId, envelope.AggregateVersion,
            true, envelope.CorrelationId, envelope.CausationId);

    public Cp6MessageValidationResult Validate(Cp6InboxDelivery delivery)
        => ValidateMetadata(delivery.Payload, delivery.MessageId, delivery.TenantId, delivery.TopicName,
            delivery.PartitionKey, delivery.AggregateId, delivery.AggregateVersion);

    private Cp6MessageValidationResult ValidateMetadata(ReadOnlyMemory<byte> payload, string id, Guid tenant,
        string topic, string partition, string aggregate, int version, bool checkCorrelation = false,
        string? correlation = null, string? causation = null)
    {
        if (!IsValid(payload)) return Cp6MessageValidationResult.Invalid("C03_ERP_CONTRACT_INVALID");
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        return topic == ErpEventContracts.TopicFor(root.GetProperty("type").GetString()!) &&
            partition == $"{tenant:D}:{aggregate}" && root.GetProperty("id").GetString() == id &&
            root.GetProperty("tenantid").GetString() == tenant.ToString("D") &&
            root.GetProperty("aggregateid").GetString() == aggregate &&
            root.GetProperty("aggregateversion").GetInt32() == version &&
            (!checkCorrelation || (root.GetProperty("correlationid").GetString() == correlation &&
                root.GetProperty("causationid").GetString() == causation))
            ? Cp6MessageValidationResult.Valid : Cp6MessageValidationResult.Invalid("C03_ERP_METADATA_MISMATCH");
    }

    private static bool SafeProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().All(SafeProperties);
        if (value.ValueKind != JsonValueKind.Object) return true;
        // Web JSON decoding is case-insensitive, so differently cased duplicates must also be rejected.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return value.EnumerateObject().All(property => names.Add(property.Name) &&
            !SensitiveNames.Contains(string.Concat(property.Name.Where(char.IsLetterOrDigit))) && SafeProperties(property.Value));
    }
}
