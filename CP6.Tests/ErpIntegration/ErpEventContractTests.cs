using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.ErpIntegration;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;

namespace CP6.Tests.ErpIntegration;

public class ErpEventContractTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "contracts/events/erp");
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Request = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Account = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Opportunity = Guid.Parse("44444444-4444-4444-8444-444444444444");

    private static Cp6ContractBundle Bundle() => Cp6ContractBundle.Load(Root);
    private static ErpEventValidator Validator() => new(Bundle());

    [Fact]
    public void Frozen_bundle_contains_six_event_types_with_hashed_compatibility_examples()
    {
        Assert.True(File.Exists(Path.Combine(Root, "contract-bundle.v1.json")), "ERP contract bundle must ship with the application.");
        var bundle = Cp6ContractBundle.Load(Root);
        Assert.Equal(6, bundle.Entries.Count);
        var validator = new Cp6CloudEventValidator(bundle);
        foreach (var entry in bundle.Entries)
        {
            Assert.Equal(Cp6EventContractIdentity.Parse(entry.EventType).SchemaId.ToString(), entry.SchemaId.ToString());
            Assert.Equal(new[] { "missing-required", "pii-negative", "unknown-optional", "valid", "wrong-type" },
                entry.Examples.Select(x => x.Name).Order().ToArray());
            foreach (var example in entry.Examples)
                Assert.Equal(example.Valid, validator.Validate(File.ReadAllBytes(bundle.GetAssetPath(example.Path))).IsValid);
        }
    }

    [Fact]
    public void Domain_validator_enforces_the_entire_published_compatibility_matrix()
    {
        var bundle = Bundle();
        var validator = new ErpEventValidator(bundle);
        foreach (var entry in bundle.Entries)
            foreach (var example in entry.Examples)
                Assert.Equal(example.Valid, validator.IsValid(File.ReadAllBytes(bundle.GetAssetPath(example.Path))));
    }

    [Theory]
    [InlineData("source", "urn:cp6:erp")]
    [InlineData("type", "com.gtx537.crm.erp.order-requested.v2")]
    [InlineData("dataschema", "https://contracts.cp6.uk/events/erp/order-created/v1/schema.json")]
    [InlineData("subject", "tenants/11111111-1111-4111-8111-111111111111/other")]
    [InlineData("tenantid", "22222222-2222-4222-8222-222222222222")]
    [InlineData("aggregateid", "33333333-3333-4333-8333-333333333333")]
    [InlineData("data.tenantId", "22222222-2222-4222-8222-222222222222")]
    [InlineData("data.opportunityId", "33333333-3333-4333-8333-333333333333")]
    [InlineData("data.accountId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("data.requestId", "AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA")]
    [InlineData("data.requestId", "22222222222242228222222222222222")]
    [InlineData("id", "with space")]
    public void Rejects_malformed_or_inconsistent_envelope_identity(string field, string value)
    {
        var node = Example(ErpEventContracts.OrderRequested);
        Set(node, field, JsonValue.Create(value));
        Assert.False(Validator().IsValid(Bytes(node)));
    }

    [Fact]
    public void Business_partner_aggregate_is_the_account_identity()
    {
        var node = Example(ErpEventContracts.BusinessPartnerRequested);
        node["aggregateid"] = Opportunity.ToString("D");
        node["subject"] = $"tenants/{Tenant:D}/{Opportunity:D}";
        Assert.False(Validator().IsValid(Bytes(node)));
    }

    [Theory]
    [InlineData(ErpEventContracts.BusinessPartnerSynchronized)]
    [InlineData(ErpEventContracts.BusinessPartnerFailed)]
    [InlineData(ErpEventContracts.OrderCreated)]
    [InlineData(ErpEventContracts.OrderFailed)]
    public void Results_require_a_positive_version_equal_to_the_cloud_event_revision(string type)
    {
        foreach (var version in new[] { 0, -1, 3 })
        {
            var node = Example(type);
            node["data"]!["resultVersion"] = version;
            Assert.False(Validator().IsValid(Bytes(node)));
        }
    }

    [Theory]
    [InlineData(ErpEventContracts.BusinessPartnerRequested)]
    [InlineData(ErpEventContracts.OrderRequested)]
    public void Request_revision_is_independent_of_positive_crm_aggregate_revision(string type)
    {
        var node = Example(type);
        node["aggregateversion"] = 19;
        node["data"]!["requestVersion"] = 2;
        Assert.True(Validator().IsValid(Bytes(node)));
        node["aggregateversion"] = 0;
        Assert.False(Validator().IsValid(Bytes(node)));
    }

    [Theory]
    [InlineData("requestId")]
    [InlineData("accountId")]
    [InlineData("requestVersion")]
    [InlineData("opportunityId")]
    [InlineData("businessPartnerKey")]
    [InlineData("quotationKey")]
    [InlineData("quotationVersion")]
    [InlineData("expectedAmount")]
    [InlineData("currency")]
    public void Order_requests_cannot_omit_identity_or_financial_binding(string field)
    {
        var node = Example(ErpEventContracts.OrderRequested);
        node["data"]!.AsObject().Remove(field);
        Assert.False(Validator().IsValid(Bytes(node)));
    }

    [Theory]
    [InlineData("quotationVersion", "\"AQ==\"")]
    [InlineData("quotationVersion", "\"AAAAAAAAAAE=\\n\"")]
    [InlineData("quotationVersion", "\"AAAAAAAAAAF=\"")]
    [InlineData("expectedAmount", "-0.01")]
    [InlineData("expectedAmount", "1e50")]
    [InlineData("expectedAmount", "\"1250.50\"")]
    [InlineData("currency", "\"usd\"")]
    [InlineData("businessPartnerKey", "\"BP value\"")]
    [InlineData("businessPartnerKey", "\"BP-0001\\n\"")]
    [InlineData("quotationKey", "\"123456789012345678901\"")]
    [InlineData("requestVersion", "2147483648")]
    [InlineData("requestVersion", "0")]
    public void Order_requests_reject_invalid_financial_values(string field, string json)
    {
        var node = Example(ErpEventContracts.OrderRequested);
        node["data"]![field] = JsonNode.Parse(json);
        Assert.False(Validator().IsValid(Bytes(node)));
    }

    [Theory]
    [InlineData("\"requestVersion\":1,\"requestVersion\":1")]
    [InlineData("\"requestVersion\":1,\"RequestVersion\":2")]
    [InlineData("\"future\":{\"value\":1,\"value\":2},\"requestVersion\":1")]
    [InlineData("\"future\":[{\"value\":1,\"value\":2}],\"requestVersion\":1")]
    public void Rejects_duplicate_properties_at_any_depth(string replacement)
    {
        var json = Example(ErpEventContracts.OrderRequested).ToJsonString().Replace("\"requestVersion\":1", replacement);
        Assert.False(Validator().IsValid(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("Email")]
    [InlineData("legal_name")]
    [InlineData("tax-number")]
    [InlineData("access_token")]
    [InlineData("PhoneNumber")]
    [InlineData("billingAddress")]
    [InlineData("price")]
    [InlineData("prices")]
    [InlineData("unit_price")]
    [InlineData("orderLines")]
    [InlineData("quotation_lines")]
    public void Rejects_recursive_pii_secrets_and_pricing_extensions(string name)
    {
        var node = Example(ErpEventContracts.OrderRequested);
        node["data"]!["future"] = new JsonObject { ["list"] = new JsonArray(new JsonObject { [name] = "private-value" }) };
        Assert.False(Validator().IsValid(Bytes(node)));
    }

    [Fact]
    public void Accepts_harmless_unknown_optional_data_without_blocking_required_amount_or_content_type()
    {
        var node = Example(ErpEventContracts.OrderRequested);
        node["data"]!["future"] = new JsonObject { ["code"] = "next", ["values"] = new JsonArray(1, 2) };
        Assert.True(Validator().IsValid(Bytes(node)));
    }

    [Theory]
    [InlineData("business rule failure")]
    [InlineData("C03_QUOTATION_EXPIRED\n")]
    [InlineData("c03_lowercase")]
    [InlineData("C03_")]
    public void Failure_events_only_accept_bounded_stable_error_codes(string error)
    {
        var node = Example(ErpEventContracts.OrderFailed);
        node["data"]!["errorCode"] = error;
        Assert.False(Validator().IsValid(Bytes(node)));
    }

    [Fact]
    public void All_typed_factories_produce_valid_inbox_and_outbox_contracts()
    {
        foreach (var envelope in FactoryEnvelopes())
        {
            Assert.True(Validator().Validate(envelope).IsValid);
            Assert.True(Validator().Validate(Delivery(envelope)).IsValid);
        }
    }

    [Theory]
    [InlineData("message")]
    [InlineData("tenant")]
    [InlineData("topic")]
    [InlineData("partition")]
    [InlineData("aggregate")]
    [InlineData("version")]
    [InlineData("correlation")]
    [InlineData("causation")]
    [InlineData("null-correlation")]
    [InlineData("null-causation")]
    public void Transport_metadata_cannot_disagree_with_the_valid_cloud_event(string field)
    {
        foreach (var original in FactoryEnvelopes())
        {
            var changed = field switch
            {
                "message" => original with { MessageId = "other-event" },
                "tenant" => original with { TenantId = Request },
                "topic" => original with { TopicName = original.TopicName == ErpEventContracts.RequestTopic ? ErpEventContracts.ResultTopic : ErpEventContracts.RequestTopic },
                "partition" => original with { PartitionKey = "wrong" },
                "aggregate" => original with { AggregateId = Request.ToString("D") },
                "version" => original with { AggregateVersion = 100 },
                "correlation" => original with { CorrelationId = "other-correlation" },
                "causation" => original with { CausationId = "other-cause" },
                "null-correlation" => original with { CorrelationId = null! },
                _ => original with { CausationId = null! }
            };
            var result = Validator().Validate(changed);
            Assert.False(result.IsValid);
            Assert.StartsWith("C03_", result.ErrorCode);
            if (field is not ("correlation" or "causation" or "null-correlation" or "null-causation"))
                Assert.False(Validator().Validate(Delivery(changed)).IsValid);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    public void Malformed_payloads_are_rejected_without_throwing(string json)
        => Assert.False(Validator().IsValid(Encoding.UTF8.GetBytes(json)));

    private static IEnumerable<Cp6OutboxEnvelope> FactoryEnvelopes()
    {
        yield return Make(Account, 7, ErpEventContracts.BusinessPartnerRequested, new ErpBusinessPartnerRequested(Tenant, Request, Account, 1));
        yield return Make(Account, 2, ErpEventContracts.BusinessPartnerSynchronized, new ErpBusinessPartnerSynchronized(Tenant, Request, Account, 1, 2, "BP-0001"));
        yield return Make(Account, 2, ErpEventContracts.BusinessPartnerFailed, new ErpBusinessPartnerFailed(Tenant, Request, Account, 1, 2, "C03_BP_REQUIRED", false));
        yield return Make(Opportunity, 7, ErpEventContracts.OrderRequested, new ErpOrderRequested(Tenant, Request, Account, 1, Opportunity, "BP-0001", "Q-0001", "AAAAAAAAAAE=", 1250.50m, "USD"));
        yield return Make(Opportunity, 2, ErpEventContracts.OrderCreated, new ErpOrderCreated(Tenant, Request, Account, 1, Opportunity, 2, "SO-0001", "Q-0001", "BP-0001", 1250.50m, "USD"));
        yield return Make(Opportunity, 2, ErpEventContracts.OrderFailed, new ErpOrderFailed(Tenant, Request, Account, 1, Opportunity, 2, "CRM_ERP_QUOTATION_EXPIRED", false));
    }

    private static Cp6OutboxEnvelope Make<T>(Guid aggregate, int version, string type, T data)
        => ErpEventContracts.Create(Tenant, aggregate.ToString("D"), version, type, data, "us", "correlation-1", "command-1");

    private static Cp6InboxDelivery Delivery(Cp6OutboxEnvelope envelope)
        => new("erp-contract-tests", envelope.MessageId, envelope.TenantId, envelope.TopicName,
            envelope.PartitionKey, envelope.Payload, envelope.AggregateId, envelope.AggregateVersion);

    private static JsonObject Example(string type)
    {
        var bundle = Bundle();
        return JsonNode.Parse(File.ReadAllBytes(bundle.GetAssetPath(bundle.Entries.Single(x => x.EventType == type)
            .Examples.Single(x => x.Name == "valid").Path)))!.AsObject();
    }

    private static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());

    private static void Set(JsonObject node, string field, JsonNode? value)
    {
        if (field.StartsWith("data.")) node["data"]![field[5..]] = value;
        else node[field] = value;
    }
}
