using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.ErpIntegration;
using CP6.Platform.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CP6.Tests.Erp;

public sealed class CommercialOriginalWireTests
{
    internal static readonly Guid Tenant = Guid.Parse("10000000-0000-4000-8000-000000000101");
    internal static readonly Guid Handoff = Guid.Parse("20000000-0000-4000-8000-000000000101");
    internal static readonly Guid Intent = Guid.Parse("20000000-0000-4000-8000-000000000102");
    internal static readonly Guid Opportunity = Guid.Parse("20000000-0000-4000-8000-000000000103");
    internal static readonly Guid Account = Guid.Parse("20000000-0000-4000-8000-000000000104");
    internal const string LargeVersion = "9007199254740993";
    internal static readonly string Digest = new('a', 64);
    internal static readonly string Previous = new('b', 64);
    internal static readonly Guid FixtureTenant = Guid.Parse("b0000000-0000-4000-8000-000000000001");

    internal static CommercialRouteV1 FixtureRoute(string name) => name switch
    {
        "handoff" => new(FixtureTenant, "MAIN_INTAKE", "MAIN-CN-01", "CRM_HO", "MAIN_INTAKE",
            CommercialOriginalV1.Handoff, "handoff-e1", "route-h03-v1"),
        "intake" => new(FixtureTenant, "MAIN_INTAKE", "MAIN-CN-01", "MAIN_INTAKE", "CRM_OPP",
            CommercialOriginalV1.Intake, "main-intake-e1", "route-intake-v1"),
        "quote" => new(FixtureTenant, "MAIN_QUOTE", "MAIN-CN-01", "MAIN_QUOTE", "CRM_OPP",
            CommercialOriginalV1.Quote, "main-quote-e1", "route-h06-v1"),
        "commercial" => new(FixtureTenant, "MAIN_SALES", "MAIN-CN-01", "MAIN_SALES", "CRM_OPP",
            CommercialOriginalV1.Commercial, "sales-e1", "route-h08-v1"),
        "handoff-supplement" => FixtureRoute("handoff"),
        "intake-correction" => FixtureRoute("intake"),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    internal static byte[] FixtureBytes(string name)
    {
        using var source = typeof(CommercialOriginalWireTests).Assembly.GetManifestResourceStream($"CP6.Tests.CommercialV1.{name}.json")!;
        using var bytes = new MemoryStream();
        source.CopyTo(bytes);
        return bytes.ToArray();
    }

    internal static CommercialOriginalV1 FixtureOriginal(string name)
    {
        var bytes = FixtureBytes(name);
        return CommercialOriginalV1.Capture(FixtureRoute(name), CommercialOriginalV1.Hash(bytes), bytes);
    }

    [Theory]
    [InlineData("handoff", "96820644c483d96deb204c4fc6b59f31959de7f8c3f0cf70b3ef75608a464557", "4c2e2ba0fc1215446e2915fbc5dec55b4d3be762cdc2a4747789dbab23912668")]
    [InlineData("intake", "7d9f66bebc4a67bf86b1037b97507dd951a55af3d0bfb41330b0f1cefd61b7d5", "3ac123411a11f1abe25c04932c94d04efb68bc49e2bed08f15a9ebbdd58c0a98")]
    [InlineData("quote", "9425d0875aa506321e0b3279584fdc985b3d8a65519b2ce0c68f9a3a378146c2", "3794fa824f3c7b51d5601fad63981d25288cf670fc3870fcb2c71825c3be6079")]
    [InlineData("commercial", "b8c677bd1a07eec09a6debb4a7d01454d8739509ef96119ce19c084a3813ecea", "e41d7f89e5d592008f66714bea0d90c7cf820dbf2a762d31967e9da618061527")]
    public async Task Commercial_wire_copies_frozen_body_through_transport_content(string name, string bodyHash, string businessDigest)
    {
        var initial = FixtureBytes(name);
        var original = CommercialOriginalV1.Capture(FixtureRoute(name), bodyHash, initial);
        var server = WebApplication.CreateSlimBuilder();
        server.Logging.ClearProviders();
        server.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = server.Build();
        app.MapPost("/original", async (HttpContext request) =>
        {
            using var received = new MemoryStream();
            await request.Request.Body.CopyToAsync(received, request.RequestAborted);
            try { return Results.Bytes(original.Reopen(received.ToArray()).OriginalBytes(), "application/json"); }
            catch (CommercialProtocolException error) { return Results.Conflict(error.Code); }
        });
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var outbound = new ByteArrayContent(original.OriginalBytes());
        using var received = await client.PostAsync("/original", outbound);
        received.EnsureSuccessStatusCode();
        var roundtrip = original.Reopen(await received.Content.ReadAsByteArrayAsync());
        using var document = JsonDocument.Parse(initial);
        var changed = JsonSerializer.SerializeToUtf8Bytes(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        if (changed.AsSpan().SequenceEqual(initial)) changed = [.. changed, (byte)'\n'];
        using var reencoded = new ByteArrayContent(changed);
        using var rejected = await client.PostAsync("/original", reencoded);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("ORIGINAL_BODY_HASH_MISMATCH", await rejected.Content.ReadAsStringAsync());
        Assert.Equal(initial, roundtrip.OriginalBytes());
        Assert.Equal(bodyHash, roundtrip.BodyHash);
        Assert.Equal(businessDigest, roundtrip.Coordinates.BusinessDigest);
        Assert.Equal(original.PartitionKey(), roundtrip.PartitionKey());
        var copy = roundtrip.OriginalBytes();
        copy[0] = 0;
        Assert.Equal(initial, roundtrip.OriginalBytes());
    }

    internal static CommercialRouteV1 Route(string contract) => contract switch
    {
        "CRM-HO-OPP/1" => new(Tenant, "MainIntake", "CN01", "CRM_HO", "MainIntake", contract, "handoff-epoch", "route-h03-v1"),
        "OPP-INTAKE/1" => new(Tenant, "MainIntake", "CN01", "MAIN_INTAKE", "CRM_OPP", contract, "intake-epoch", "route-intake-v1"),
        "OPP-QUOTE/1" => new(Tenant, "MainQuote", "CN01", "MAIN_QTN", "CRM_OPP", contract, "quote-epoch", "route-h06-v1"),
        _ => new(Tenant, "MainSales", "CN01", "MAIN_SALES", "CRM_OPP", contract, "commercial-epoch", "route-h08-v1")
    };

    [Fact]
    public void Commercial_wire_preserves_original_bytes_opaque_quote_ordinal_and_net()
    {
        var bytes = Quote();
        var bodyHash = CommercialOriginalV1.Hash(bytes);
        var original = CommercialOriginalV1.Capture(Route("OPP-QUOTE/1"), bodyHash, bytes);
        Assert.Equal(bodyHash, original.BodyHash);
        Assert.Equal(bytes, original.OriginalBytes());
        Assert.Equal(LargeVersion, original.Coordinates.Ordinal);
        using var document = JsonDocument.Parse(original.OriginalBytes());
        Assert.Equal("quote-v10", document.RootElement.GetProperty("quoteVersion").GetString());
        Assert.Equal("900719925474099.1234", document.RootElement.GetProperty("net").GetProperty("amount").GetString());

        var reencoded = JsonSerializer.SerializeToUtf8Bytes(document.RootElement);
        Assert.NotEqual(bodyHash, CommercialOriginalV1.Hash(reencoded));
        var failure = Assert.Throws<CommercialProtocolException>(() =>
            CommercialOriginalV1.Capture(Route("OPP-QUOTE/1"), bodyHash, reencoded));
        Assert.Equal("ORIGINAL_BODY_HASH_MISMATCH", failure.Code);
    }

    [Fact]
    public void Commercial_wire_accepts_Int64_max_without_number_coercion_and_refuses_successor()
    {
        const string max = "9223372036854775807";
        const string opaque = " 0000-报价-Ａ ";
        var bytes = Quote(max, opaque);
        var original = CommercialOriginalV1.Capture(Route(CommercialOriginalV1.Quote), CommercialOriginalV1.Hash(bytes), bytes);
        Assert.Equal(max, original.Coordinates.Ordinal);
        Assert.Equal(long.MaxValue, CommercialWireV1.Revision(original.Coordinates.Ordinal));
        Assert.Equal("REVISION_EXHAUSTED", Assert.Throws<CommercialProtocolException>(() => CommercialWireV1.NextRevision(max)).Code);
        var body = JsonNode.Parse(bytes)!;
        Assert.Equal(opaque, JsonNode.Parse(original.OriginalBytes())!["quoteVersion"]!.GetValue<string>());
        body["sequence"]!["ordinal"] = long.MaxValue;
        Reject(Route(CommercialOriginalV1.Quote), body, "DOMAIN_TYPE_MISMATCH");
        body["sequence"]!["ordinal"] = "9223372036854775808";
        Reject(Route(CommercialOriginalV1.Quote), body, "DOMAIN_TYPE_MISMATCH");
        body["sequence"]!["ordinal"] = max;
        body["quoteVersion"] = 10;
        Reject(Route(CommercialOriginalV1.Quote), body, "DOMAIN_TYPE_MISMATCH");
        body["quoteVersion"] = opaque;
        body["source"]!["requestVersion"] = 2147483647;
        Reject(Route(CommercialOriginalV1.Quote), body, "DOMAIN_TYPE_MISMATCH");
    }

    [Fact]
    public void Commercial_wire_preserves_explicitly_null_frozen_forecast_values()
    {
        var original = FixtureOriginal("handoff");
        var candidate = JsonNode.Parse(original.OriginalBytes())!;
        var intent = candidate["intent"]!;
        intent["forecast"]!["estimatedNet"] = null;
        intent["forecast"]!["probabilityPercent"] = null;
        intent["forecast"]!["expectedDecisionDate"] = null;
        candidate["intentDigest"] = CommercialCanonicalV1.Digest("OPP-INTENT-CANON-1", JsonSerializer.SerializeToElement(intent));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(candidate);
        Assert.Equal(bytes, CommercialOriginalV1.Capture(original.Route, CommercialOriginalV1.Hash(bytes), bytes).OriginalBytes());
    }

    [Fact]
    public void Commercial_wire_supports_original_supplement_and_Intake_correction_with_separate_predecessor()
    {
        var supplement = FixtureOriginal("handoff-supplement");
        var correction = FixtureOriginal("intake-correction");
        Assert.Equal("32ac4d8a0cd19aea38c9ac99cc191327b66112eecb835590c44a3603c2f62e44", supplement.BodyHash);
        Assert.Equal("b25a32555b7d7e8a0b9987da2294f8d313c268cc6bac1465a12c41a795b01033", supplement.Coordinates.BusinessDigest);
        Assert.Equal("2", supplement.Coordinates.Source.RequestVersion);
        Assert.Equal("a1fe79f0919f3087e7e02871fe3a41dd931f47473606beb3f1761f4bed5ba260", correction.BodyHash);
        var mismatch = JsonNode.Parse(supplement.OriginalBytes())!;
        mismatch["supersession"]!["fromRequestVersion"] = "2";
        Reject(supplement.Route, mismatch, "DOMAIN_TYPE_MISMATCH");
        mismatch = JsonNode.Parse(correction.OriginalBytes())!;
        mismatch["sourceHandoffIdentity"]!["requestVersion"] = "5";
        Reject(correction.Route, mismatch, "DOMAIN_TYPE_MISMATCH");
    }

    [Fact]
    public void Commercial_wire_frozen_protection_keys_match_archived_contract()
    {
        var prepare = JsonSerializer.Deserialize<IcProtectionPrepareV1>(FixtureBytes("protection-prepare"), CommercialCanonicalV1.Json)!;
        var saved = JsonSerializer.Deserialize<IcProtectionReadV1>(FixtureBytes("protection-grant"), CommercialCanonicalV1.Json)!;
        var grant = Assert.IsType<IcProtectionGrantV1>(saved.Grant);
        var captured = FixtureOriginal("handoff-supplement");
        var subject = CommercialProtectionV1.Subject(captured);
        Assert.Equal(prepare.Subject, subject);
        var participant = new IcParticipantV1(prepare.ParticipantAuthority, subject, prepare.Purpose);
        Assert.Equal(prepare.ProtectionKey, CommercialProtectionV1.ProtectionKey(prepare.Execution, participant));
        Assert.Equal(grant.GrantDigest, CommercialProtectionV1.GrantDigest(grant));
        Assert.Equal(grant.GrantDigest, CommercialProtectionV1.GrantDigest(grant with
        { GrantEvidence = grant.GrantEvidence with { ProofRef = "另一个证明承载" } }));
    }

    [Theory]
    [InlineData("1000000000000000.0000")]
    [InlineData("1e2")]
    [InlineData("100.00001")]
    [InlineData("00.0000")]
    [InlineData("-1.0000")]
    [InlineData("1.0000 ")]
    public void Commercial_wire_rejects_out_of_range_or_modified_decimal(string amount)
    {
        var bytes = Quote(amount: amount);
        var route = Route(CommercialOriginalV1.Quote);
        Assert.Equal("DOMAIN_TYPE_MISMATCH", Assert.Throws<CommercialProtocolException>(() =>
            CommercialOriginalV1.Capture(route, CommercialOriginalV1.Hash(bytes), bytes)).Code);
    }

    [Fact]
    public void Commercial_wire_rejects_numeric_Money_amount()
    {
        var original = FixtureOriginal("quote");
        var candidate = JsonNode.Parse(original.OriginalBytes())!;
        candidate["net"]!["amount"] = 100m;
        Reject(original.Route, candidate, "DOMAIN_TYPE_MISMATCH");
    }

    [Theory]
    [InlineData("tax")]
    [InlineData("gross")]
    public void Commercial_wire_quote_allows_only_net_and_keeps_original_snapshot(string field)
    {
        var original = FixtureOriginal("quote");
        var candidate = JsonNode.Parse(original.OriginalBytes())!;
        candidate[field] = candidate["net"]!.DeepClone();
        Reject(original.Route, candidate, "DOMAIN_TYPE_MISMATCH");
        Assert.Equal(original.Coordinates, original.Reopen(FixtureBytes("quote")).Coordinates);
    }

    [Fact]
    public void Commercial_wire_rejects_owner_tenant_epoch_schema_and_extra_fields()
    {
        var original = FixtureOriginal("handoff");
        var json = JsonNode.Parse(original.OriginalBytes())!;
        json["processingType"] = "ESTIMATE_AND_QUOTE";
        Reject(original.Route, json, "CONTRACT_INVALID");

        var quote = FixtureOriginal("quote");
        foreach (var route in new[] { quote.Route with { Epoch = "future-epoch" }, quote.Route with { Producer = "other-producer" },
                     quote.Route with { Authority = "other-owner" }, quote.Route with { LegalEntityKey = "OTHER-L" },
                     quote.Route with { TenantId = Tenant }, quote.Route with { Contract = "OPP-QUOTE/2" } })
            Assert.Equal("RESULT_GENERATION_UNSUPPORTED", Assert.Throws<CommercialProtocolException>(() =>
                CommercialOriginalV1.Capture(route, quote.BodyHash, quote.OriginalBytes())).Code);
        var duplicate = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(quote.OriginalBytes()).Replace(
            "\"tax\":null", "\"tax\":null,\"tax\":null", StringComparison.Ordinal));
        Assert.Throws<CommercialProtocolException>(() =>
            CommercialOriginalV1.Capture(quote.Route, CommercialOriginalV1.Hash(duplicate), duplicate));
    }

    [Fact]
    public void Commercial_wire_distinguishes_business_digest_and_proof_carrier_from_BodyHash()
    {
        foreach (var name in new[] { "intake", "quote", "commercial" })
        {
            var original = FixtureOriginal(name);
            var changed = JsonNode.Parse(original.OriginalBytes())!;
            changed["eventId"] = "其他运输键";
            var proof = name == "commercial" ? changed["changeProof"]! : changed["ownerEvidence"]!;
            proof["proofRef"] = "轮换承载";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(changed);
            var forwarded = CommercialOriginalV1.Capture(original.Route, CommercialOriginalV1.Hash(bytes), bytes);
            Assert.NotEqual(original.BodyHash, forwarded.BodyHash);
            Assert.Equal(original.Coordinates.BusinessDigest, forwarded.Coordinates.BusinessDigest);
            Assert.Equal(original.PartitionKey(), forwarded.PartitionKey());
        }
    }

    [Fact]
    public void Commercial_wire_group_rejects_incomplete_or_duplicate_coverage_and_excess_minor_digits()
    {
        var original = FixtureOriginal("commercial");
        var candidate = JsonNode.Parse(original.OriginalBytes())!;
        var coverage = candidate["coverage"]!["coveredOrderKeys"]!.AsArray();
        coverage.Clear();
        Reject(original.Route, candidate, "RESPONSIBILITY_COVERAGE_INVALID");
        candidate = JsonNode.Parse(original.OriginalBytes())!;
        var members = candidate["members"]!.AsArray();
        members.Add(members[0]!.DeepClone());
        Reject(original.Route, candidate, "RESPONSIBILITY_COVERAGE_INVALID");
        candidate = JsonNode.Parse(original.OriginalBytes())!;
        candidate["members"]![0]!["confirmedNet"] = "100.0001";
        Reject(original.Route, candidate, "DOMAIN_TYPE_MISMATCH");
    }

    [Fact]
    public void Commercial_wire_preserves_C03_colon_and_platform_slash_profiles()
    {
        var legacy = ErpEventContracts.Create(Tenant, Account.ToString("D"), 1, ErpEventContracts.BusinessPartnerRequested,
            new ErpBusinessPartnerRequested(Tenant, Guid.NewGuid(), Account, 1), "us", "fixture-correlation", "fixture-source");
        var bundle = Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/erp"));
        var validator = new ErpEventValidator(bundle);
        Assert.True(validator.Validate(legacy).IsValid);
        Assert.Equal($"{Tenant:D}:{Account:D}", legacy.PartitionKey);
        var slug = Cp6DaprKafkaConventions.GetTopic(Cp6EventContractIdentity.Parse(ErpEventContracts.BusinessPartnerRequested));
        var slash = Cp6DaprKafkaConventions.GetPartitionKey(Tenant, Account.ToString("D"));
        var platform = new Cp6DaprDeliveryValidator(new Cp6CloudEventValidator(bundle));
        Assert.Equal($"{Tenant:D}/{Account:D}", slash);
        Assert.True(platform.Validate(legacy.Payload, slug, slash).IsValid);
        Assert.Equal(Cp6DaprContractFailure.TopicMismatch, platform.Validate(legacy.Payload, legacy.TopicName, slash).Failure);
        Assert.Equal(Cp6DaprContractFailure.PartitionKeyMismatch, platform.Validate(legacy.Payload, slug, legacy.PartitionKey).Failure);
        Assert.False(validator.Validate(legacy with { PartitionKey = slash }).IsValid);
        foreach (var name in new[] { "handoff", "quote" })
        {
            var original = FixtureOriginal(name);
            using var document = JsonDocument.Parse(original.OriginalBytes());
            var wrong = ErpEventContracts.Create(original.Route.TenantId, Account.ToString("D"), 1,
                ErpEventContracts.OrderRequested, document.RootElement, "us", "fixture-correlation", "fixture-source");
            Assert.False(validator.IsValid(wrong.Payload));
            Assert.Matches("^[0-9a-f]{64}$", original.PartitionKey());
        }
    }

    private static void Reject(CommercialRouteV1 route, JsonNode node, string code)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(node);
        Assert.Equal(code, Assert.Throws<CommercialProtocolException>(() =>
            CommercialOriginalV1.Capture(route, CommercialOriginalV1.Hash(bytes), bytes)).Code);
    }

    internal static byte[] Quote(string ordinal = LargeVersion, string quoteVersion = "quote-v10",
        string amount = "900719925474099.1234") => Encoding.UTF8.GetBytes($$"""
        {
          "schemaVersion":"OPP-QUOTE/1", "tenantId":"{{Tenant:D}}",
          "eventId":"quote-event-1", "producer":"MAIN_QTN", "epoch":"quote-epoch",
          "source": {{SourceJson()}},
          "sequence":{"ordinal":"{{ordinal}}","previousOrdinal":"{{long.Parse(ordinal, System.Globalization.CultureInfo.InvariantCulture)-1}}","previousBusinessDigest":"{{Previous}}"},
          "authority":"MainQuote","legalEntityKey":"CN01","quoteKey":"quote-key-1",
          "quoteVersion":"{{quoteVersion}}","supersedesQuoteVersion":"quote-v2","quoteStatus":"ISSUED",
          "net":{"amount":"{{amount}}","currency":"CNY","basis":"NET_EX_TAX_AFTER_DISCOUNT","currencyPolicyVersion":"currency-v1"},
          "tax":null,"gross":null,"validUntil":null,"ownerRecordedAt":"2026-11-20T08:00:00.000Z",
          "mainReceiptId":"main-receipt-1","ownerEvidence":{"authority":"MainQuote","key":"quote-proof","version":"erp-v2","proofRef":"证明-甲"}
        }
        """);

    internal static string SourceJson(string version = LargeVersion) => $$"""
        {"handoffId":"{{Handoff:D}}","requestVersion":"{{version}}","intentId":"{{Intent:D}}","intentDigest":"{{Digest}}",
         "opportunityId":"{{Opportunity:D}}","accountId":"{{Account:D}}","requirementVersion":"5","forecastVersion":"7"}
        """;
}
