using System.Text.Json;

namespace CP6.Core.Services.ErpIntegration;

internal static partial class CommercialWireV1
{
    private static CommercialWireCoordinatesV1 Group(CommercialRouteV1 route, JsonElement value, string eventId, string ordinal)
    {
        Exact(value, "schemaVersion", "tenantId", "eventId", "producer", "epoch", "binding", "sequence", "resultKey", "effectiveAt",
            "changeKind", "reason", "orders", "members", "retirements", "coverage", "changeProof", "ownerRecordedAt");
        var binding = value.GetProperty("binding");
        GroupBinding(route, binding);
        var minorDigits = Integer(binding.GetProperty("minorDigits"), 4);
        _ = Key(value, "resultKey");
        Utc(value.GetProperty("effectiveAt"));
        Utc(value.GetProperty("ownerRecordedAt"));
        Text(value.GetProperty("reason"), 2000);
        var change = GroupChange(value.GetProperty("changeKind"));
        var orders = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var order in Items(value.GetProperty("orders")))
            if (!orders.TryAdd(Key(order, "orderKey"), Order(order, route))) throw GroupInvalid();

        var allocations = new HashSet<string>(StringComparer.Ordinal);
        var coordinates = new HashSet<(string Order, string Line, string? Schedule, string Slice)>();
        foreach (var member in Items(value.GetProperty("members")))
            Member(member, orders, allocations, coordinates, minorDigits);
        foreach (var retirement in Items(value.GetProperty("retirements")))
        {
            Exact(retirement, "allocationKey", "kind", "evidence");
            var kind = EnumValue(retirement.GetProperty("kind"), "SUPERSEDED", "CORRECTED_OUT");
            if (!allocations.Add(Key(retirement, "allocationKey")) ||
                kind == "SUPERSEDED" && change != "REPLACE" || kind == "CORRECTED_OUT" && change != "CORRECT") throw GroupInvalid();
            Evidence(retirement.GetProperty("evidence"));
        }

        var coverage = value.GetProperty("coverage");
        Exact(coverage, "mode", "economicScopeDigest", "coveredOrderKeys", "proofRef");
        _ = EnumValue(coverage.GetProperty("mode"), "COMPLETE_CURRENT_GROUP");
        _ = Key(coverage, "proofRef");
        if (Digest(coverage.GetProperty("economicScopeDigest")) != Digest(binding.GetProperty("economicScopeDigest")) ||
            !orders.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(Keys(coverage.GetProperty("coveredOrderKeys")))) throw GroupInvalid();

        var proof = value.GetProperty("changeProof");
        Exact(proof, "authority", "decisionKey", "decisionVersion", "kind", "bindingDigest", "priorBusinessDigest",
            "candidateStateDigest", "affectedAllocationKeys", "correctsResultKeys", "proofRef");
        _ = Key(proof, "authority");
        _ = Key(proof, "decisionKey");
        _ = Key(proof, "decisionVersion");
        _ = Key(proof, "proofRef");
        _ = Keys(proof.GetProperty("affectedAllocationKeys"));
        _ = Keys(proof.GetProperty("correctsResultKeys"));
        if (GroupChange(proof.GetProperty("kind")) != change ||
            proof.GetProperty("priorBusinessDigest").GetString() != value.GetProperty("sequence").GetProperty("previousBusinessDigest").GetString() ||
            Digest(proof.GetProperty("bindingDigest")) != CommercialCanonicalV1.Digest("OPP-GROUP-BINDING-1", binding, removeProof: true) ||
            Digest(proof.GetProperty("candidateStateDigest")) != CommercialCanonicalV1.Digest("OPP-GROUP-STATE-1", value, removeProof: true,
                rootFields: ["binding", "orders", "members", "retirements", "effectiveAt", "changeKind", "reason", "ownerRecordedAt"]))
            throw GroupInvalid();
        return Result("OPP-GROUP-BUSINESS-1", value, eventId, Key(binding, "groupKey"), ordinal,
            ReadSource(binding.GetProperty("originalSource")));
    }

    private static CommercialProtocolException GroupInvalid() => new("RESPONSIBILITY_COVERAGE_INVALID");

    private static string GroupChange(JsonElement value) => EnumValue(value, "OBSERVE", "CONFIRM", "AMEND", "CANCEL", "CORRECT", "REPLACE");

    private static void GroupBinding(CommercialRouteV1 route, JsonElement value)
    {
        Exact(value, "authority", "legalEntityKey", "groupKey", "customerIntentKey", "originalSource", "businessPartnerKey",
            "currency", "minorDigits", "amountBasis", "currencyPolicyVersion", "canonicalStream", "economicScopeDigest",
            "ownerBindingEvidence", "crmAttributionEvidence");
        RequireOwnerRoute(route, value);
        foreach (var field in new[] { "groupKey", "customerIntentKey", "businessPartnerKey", "currencyPolicyVersion" }) _ = Key(value, field);
        _ = Currency(value.GetProperty("currency"));
        _ = Integer(value.GetProperty("minorDigits"), 4);
        _ = EnumValue(value.GetProperty("amountBasis"), Net);
        _ = Digest(value.GetProperty("economicScopeDigest"));
        Evidence(value.GetProperty("ownerBindingEvidence"));
        Evidence(value.GetProperty("crmAttributionEvidence"));
        var stream = value.GetProperty("canonicalStream");
        Stream(stream);
        if (Key(stream, "producer") != route.Producer || Key(stream, "contractVersion") != route.Contract ||
            Key(stream, "epoch") != route.Epoch) throw RouteMismatch();
    }

    private static string Order(JsonElement value, CommercialRouteV1 route)
    {
        Exact(value, "orderKey", "orderVersion", "documentState", "confirmationCoverage", "pendingCommercialDecision", "quote");
        _ = Key(value, "orderVersion");
        var state = EnumValue(value.GetProperty("documentState"), "DRAFT", "CREATED", "PARTIALLY_CONFIRMED", "CONFIRMED", "AMENDED", "CANCELLED");
        var coverage = EnumValue(value.GetProperty("confirmationCoverage"), "NONE", "PARTIAL", "COMPLETE", "UNKNOWN");
        var pending = value.GetProperty("pendingCommercialDecision");
        Boolean(pending);
        if (state is "DRAFT" or "CREATED" && (!pending.GetBoolean() || coverage is "PARTIAL" or "COMPLETE")) throw GroupInvalid();
        var quote = value.GetProperty("quote");
        if (quote.ValueKind != JsonValueKind.Null)
        {
            Exact(quote, "authority", "legalEntityKey", "quoteKey", "quoteVersion");
            if (Key(quote, "legalEntityKey") != route.LegalEntityKey) throw RouteMismatch();
            foreach (var field in quote.EnumerateObject()) _ = Key(field.Value);
        }
        return state;
    }

    private static void Member(JsonElement value, IReadOnlyDictionary<string, string> orders, HashSet<string> allocations,
        HashSet<(string Order, string Line, string? Schedule, string Slice)> coordinates, int minorDigits)
    {
        Exact(value, "allocationKey", "economicSliceKey", "orderKey", "lineKey", "scheduleKey", "requirementLineId",
            "confirmation", "confirmedNet", "cancelledNet", "replacementShares");
        var order = Key(value, "orderKey");
        if (!allocations.Add(Key(value, "allocationKey")) || !orders.TryGetValue(order, out var orderState) ||
            !coordinates.Add((order, Key(value, "lineKey"), NullableKey(value.GetProperty("scheduleKey")), Key(value, "economicSliceKey"))))
            throw GroupInvalid();
        NullableId(value.GetProperty("requirementLineId"));
        var confirmed = GroupAmount(value.GetProperty("confirmedNet"), minorDigits);
        var cancelled = GroupAmount(value.GetProperty("cancelledNet"), minorDigits);
        var confirmation = value.GetProperty("confirmation");
        if (cancelled > confirmed || confirmed > 0 && (confirmation.ValueKind == JsonValueKind.Null || orderState is "DRAFT" or "CREATED"))
            throw GroupInvalid();
        if (confirmation.ValueKind != JsonValueKind.Null)
        {
            Exact(confirmation, "confirmationId", "confirmedAt", "ownerEvidence");
            _ = Key(confirmation, "confirmationId");
            Utc(confirmation.GetProperty("confirmedAt"));
            Evidence(confirmation.GetProperty("ownerEvidence"));
        }
        var shares = Items(value.GetProperty("replacementShares"));
        decimal sumConfirmed = 0, sumCancelled = 0;
        var priors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var share in shares)
        {
            Exact(share, "priorAllocationKey", "confirmedNet", "cancelledNet");
            if (!priors.Add(Key(share, "priorAllocationKey"))) throw GroupInvalid();
            var c = GroupAmount(share.GetProperty("confirmedNet"), minorDigits);
            var x = GroupAmount(share.GetProperty("cancelledNet"), minorDigits);
            if (x > c) throw GroupInvalid();
            sumConfirmed += c;
            sumCancelled += x;
        }
        if (shares.Length > 0 && (sumConfirmed != confirmed || sumCancelled != cancelled)) throw GroupInvalid();
    }

    private static decimal GroupAmount(JsonElement value, int minorDigits)
    {
        var amount = FixedAmount(value, 4, 15);
        if (decimal.Truncate(amount * PowersOfTen[minorDigits]) != amount * PowersOfTen[minorDigits]) throw TypeMismatch();
        return amount;
    }

    private static readonly decimal[] PowersOfTen = [1, 10, 100, 1000, 10000];
}
