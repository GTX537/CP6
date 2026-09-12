using System.Security.Cryptography;
using System.Text.Json;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;

namespace CP6.Core.Services.ErpIntegration;

/// <summary>The frozen C03 request/result identities shared by CRM and ERP.</summary>
public static class ErpEventContracts
{
    public const string RequestTopic = "cp6.crm.events.v1";
    public const string ResultTopic = "cp6.erp.events.v1";
    public const string RequestSource = "urn:cp6:crm";
    public const string ResultSource = "urn:cp6:erp";
    public const string BusinessPartnerRequested = "com.gtx537.crm.erp.business-partner-requested.v1";
    public const string OrderRequested = "com.gtx537.crm.erp.order-requested.v1";
    public const string BusinessPartnerSynchronized = "com.gtx537.erp.business-partner-synchronized.v1";
    public const string BusinessPartnerFailed = "com.gtx537.erp.business-partner-failed.v1";
    public const string OrderCreated = "com.gtx537.erp.order-created.v1";
    public const string OrderFailed = "com.gtx537.erp.order-failed.v1";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Hash(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static Cp6OutboxEnvelope Create<T>(Guid tenantId, string aggregateId, int version, string type,
        T data, string region, string correlationId, string causationId, TimeProvider? clock = null)
    {
        var topic = TopicFor(type);
        var source = topic == RequestTopic ? RequestSource : ResultSource;
        var id = Guid.NewGuid().ToString("N");
        var descriptor = new Cp6CloudEventDescriptor(id, new Uri(source), type,
            $"tenants/{tenantId:D}/{aggregateId}", (clock ?? TimeProvider.System).GetUtcNow(),
            Cp6EventContractIdentity.Parse(type).SchemaId, tenantId, correlationId, causationId,
            aggregateId, version, "1.0.0", region);
        var bytes = Cp6CloudEventCodec.EncodeStructured(Cp6CloudEventCodec.Create(descriptor,
            JsonSerializer.SerializeToElement(data, Json)));
        return new(id, tenantId, topic, $"{tenantId:D}:{aggregateId}", bytes,
            correlationId, causationId, aggregateId, version);
    }

    public static string TopicFor(string type) => type switch
    {
        BusinessPartnerRequested or OrderRequested => RequestTopic,
        BusinessPartnerSynchronized or BusinessPartnerFailed or OrderCreated or OrderFailed => ResultTopic,
        _ => throw new ArgumentException("Unsupported C03 event type.", nameof(type))
    };
}

public sealed record ErpBusinessPartnerRequested(Guid TenantId, Guid RequestId, Guid AccountId, int RequestVersion);

public sealed record ErpBusinessPartnerSynchronized(Guid TenantId, Guid RequestId, Guid AccountId,
    int RequestVersion, int ResultVersion, string BusinessPartnerKey);

public sealed record ErpBusinessPartnerFailed(Guid TenantId, Guid RequestId, Guid AccountId,
    int RequestVersion, int ResultVersion, string ErrorCode, bool Retryable);

public sealed record ErpOrderRequested(Guid TenantId, Guid RequestId, Guid AccountId, int RequestVersion,
    Guid OpportunityId, string BusinessPartnerKey, string QuotationKey, string QuotationVersion,
    decimal ExpectedAmount, string Currency);

public sealed record ErpOrderCreated(Guid TenantId, Guid RequestId, Guid AccountId, int RequestVersion,
    Guid OpportunityId, int ResultVersion, string OrderKey, string QuotationKey, string BusinessPartnerKey,
    decimal BookedAmount, string Currency);

public sealed record ErpOrderFailed(Guid TenantId, Guid RequestId, Guid AccountId, int RequestVersion,
    Guid OpportunityId, int ResultVersion, string ErrorCode, bool Retryable);
