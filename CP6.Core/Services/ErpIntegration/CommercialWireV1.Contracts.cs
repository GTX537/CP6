using System.Text.Json;

namespace CP6.Core.Services.ErpIntegration;

internal static partial class CommercialWireV1
{
    private static CommercialWireCoordinatesV1 Handoff(CommercialRouteV1 route, JsonElement body)
    {
        Exact(body, "schemaVersion", "tenantId", "eventId", "producer", "handoffId", "requestVersion", "previousRequestVersion",
            "originalIntentId", "intentDigest", "intent", "sourcePermissionEvidence", "supersession", "createdAt");
        var intent = body.GetProperty("intent");
        ReadIntent(route, intent);
        var intentDigest = Digest(body.GetProperty("intentDigest"));
        if (intentDigest != CommercialCanonicalV1.Digest("OPP-INTENT-CANON-1", intent))
            throw new CommercialProtocolException("BUSINESS_DIGEST_MISMATCH");
        Evidence(body.GetProperty("sourcePermissionEvidence"));
        Utc(body.GetProperty("createdAt"));
        var source = new CommercialSourceHandoffV1(Id(body.GetProperty("handoffId")), Rev(body.GetProperty("requestVersion")),
            Id(body.GetProperty("originalIntentId")), intentDigest, Id(intent.GetProperty("opportunityId")), Id(intent.GetProperty("accountId")),
            Rev(intent.GetProperty("requirementVersion")), Rev(intent.GetProperty("forecastVersion")));
        if (source.RequestVersion == "1")
        {
            Null(body.GetProperty("previousRequestVersion"));
            Null(body.GetProperty("supersession"));
        }
        else
        {
            var previous = Rev(body.GetProperty("previousRequestVersion"));
            if (NextRevision(previous) != source.RequestVersion) throw TypeMismatch();
            Supersession(body.GetProperty("supersession"), route, source, previous);
        }
        return new(Id(body.GetProperty("eventId")).ToString("D"), source.HandoffId.ToString("D"), source.RequestVersion, source, intentDigest);
    }

    private static void ReadIntent(CommercialRouteV1 route, JsonElement value)
    {
        Exact(value, "schemaVersion", "tenantId", "opportunityId", "accountId", "opportunitySalesRevision", "requirementVersion",
            "forecastVersion", "stakeholderVersion", "requirement", "forecast", "stakeholders", "evidenceRefs", "targetOwner",
            "targetLegalEntityKey", "processingType", "requestedBy", "requestReason");
        if (Key(value, "schemaVersion") != "OPP-INTENT/1" || Id(value.GetProperty("tenantId")) != route.TenantId ||
            Key(value, "targetOwner") != route.Authority || Key(value, "targetLegalEntityKey") != route.LegalEntityKey) throw RouteMismatch();
        foreach (var field in new[] { "opportunitySalesRevision", "requirementVersion", "forecastVersion", "stakeholderVersion" })
            _ = Rev(value.GetProperty(field));
        _ = Id(value.GetProperty("requestedBy"));
        _ = EnumValue(value.GetProperty("processingType"), "ESTIMATE_AND_QUOTE");
        Text(value.GetProperty("requestReason"), 2000);
        Requirement(value.GetProperty("requirement"));
        var forecast = value.GetProperty("forecast");
        Exact(forecast, "estimatedNet", "probabilityPercent", "expectedDecisionDate");
        var estimated = forecast.GetProperty("estimatedNet");
        if (estimated.ValueKind != JsonValueKind.Null) Money(estimated);
        if (forecast.GetProperty("probabilityPercent").ValueKind != JsonValueKind.Null) _ = Integer(forecast.GetProperty("probabilityPercent"), 100);
        NullableDate(forecast.GetProperty("expectedDecisionDate"));
        foreach (var stakeholder in Items(value.GetProperty("stakeholders")))
        {
            Exact(stakeholder, "contactId", "roleCode", "relation");
            _ = Id(stakeholder.GetProperty("contactId"));
            _ = Key(stakeholder, "roleCode");
            var relation = stakeholder.GetProperty("relation");
            if (relation.ValueKind == JsonValueKind.Null) continue;
            Exact(relation, "relationId", "relationRevision");
            _ = Id(relation.GetProperty("relationId"));
            _ = Rev(relation.GetProperty("relationRevision"));
        }
        foreach (var evidence in Items(value.GetProperty("evidenceRefs")))
        {
            Exact(evidence, "owner", "key", "version", "usePurpose");
            _ = Key(evidence, "owner");
            _ = Key(evidence, "key");
            _ = Key(evidence, "version");
            _ = EnumValue(evidence.GetProperty("usePurpose"), "COMMERCIAL_HANDOFF");
        }
    }

    private static void Requirement(JsonElement value)
    {
        Exact(value, "summary", "lines", "decisionPath", "noDecisionDateReason");
        Text(value.GetProperty("summary"), 4000);
        NullableText(value.GetProperty("decisionPath"), 2000);
        NullableText(value.GetProperty("noDecisionDateReason"), 1000);
        foreach (var line in Items(value.GetProperty("lines"), 100))
        {
            Exact(line, "lineId", "clientRef", "description", "customerItemCode", "productReference", "quantity", "unitCode", "needByDate");
            NullableId(line.GetProperty("lineId"));
            _ = NullableKey(line.GetProperty("clientRef"));
            Text(line.GetProperty("description"), 1000);
            _ = NullableKey(line.GetProperty("customerItemCode"));
            NullableDate(line.GetProperty("needByDate"));
            var quantity = line.GetProperty("quantity");
            var unit = line.GetProperty("unitCode");
            if (quantity.ValueKind == JsonValueKind.Null) Null(unit);
            else
            {
                _ = FixedAmount(quantity, 6, 13, positive: true);
                _ = Key(unit, 32);
            }
            var product = line.GetProperty("productReference");
            if (product.ValueKind == JsonValueKind.Null) continue;
            Exact(product, "owner", "key", "version");
            foreach (var field in product.EnumerateObject()) _ = Key(field.Value);
        }
    }

    private static void Supersession(JsonElement value, CommercialRouteV1 route, CommercialSourceHandoffV1 source, string previous)
    {
        Exact(value, "tenantId", "opportunityId", "handoffId", "fromRequestVersion", "toRequestVersion", "priorMainReceiptId",
            "priorIntakeOrdinal", "priorIntakeBusinessDigest", "priorIntentDigest", "newIntentId", "newIntentDigest",
            "expectedHandoffSlotVersion", "committedHandoffSlotVersion", "needsInfoEvidence", "hoAuthorizationEvidence",
            "commandKey", "reason", "recordedAt");
        if (Id(value.GetProperty("tenantId")) != route.TenantId || Id(value.GetProperty("opportunityId")) != source.OpportunityId ||
            Id(value.GetProperty("handoffId")) != source.HandoffId || Rev(value.GetProperty("fromRequestVersion")) != previous ||
            Rev(value.GetProperty("toRequestVersion")) != source.RequestVersion || Id(value.GetProperty("newIntentId")) != source.IntentId ||
            Digest(value.GetProperty("newIntentDigest")) != source.IntentDigest ||
            NextRevision(Rev(value.GetProperty("expectedHandoffSlotVersion"))) != Rev(value.GetProperty("committedHandoffSlotVersion")))
            throw TypeMismatch();
        _ = Key(value, "priorMainReceiptId");
        _ = Rev(value.GetProperty("priorIntakeOrdinal"));
        _ = Digest(value.GetProperty("priorIntakeBusinessDigest"));
        _ = Digest(value.GetProperty("priorIntentDigest"));
        Evidence(value.GetProperty("needsInfoEvidence"));
        Evidence(value.GetProperty("hoAuthorizationEvidence"));
        _ = Id(value.GetProperty("commandKey"));
        Text(value.GetProperty("reason"), 2000);
        Utc(value.GetProperty("recordedAt"));
    }

    private static CommercialWireCoordinatesV1 Intake(CommercialRouteV1 route, JsonElement value, string eventId, string ordinal)
    {
        string[] common = ["schemaVersion", "tenantId", "eventId", "producer", "epoch", "source", "sequence", "mainReceiptId",
            "intakeId", "outcome", "missingFieldCodes", "errorCode", "retryableTechnical", "ownerRecordedAt"];
        var correction = value.TryGetProperty("kind", out var kind);
        Exact(value, correction
            ? [.. common, "kind", "sourceHandoffIdentity", "replacesOutcome", "predecessorResultId", "correctionRevision",
                "mainEvidenceRef", "resolvedConflicts", "reason", "ownerDecision"]
            : [.. common, "ownerEvidence"]);
        var source = ReadSource(value.GetProperty("source"));
        var receipt = Key(value, "mainReceiptId");
        var intake = NullableKey(value.GetProperty("intakeId"));
        var outcome = EnumValue(value.GetProperty("outcome"), "ACCEPTED", "NEEDS_INFO", "REJECTED_FINAL");
        var codes = Keys(value.GetProperty("missingFieldCodes"), 100);
        var error = NullableKey(value.GetProperty("errorCode"));
        if (value.GetProperty("retryableTechnical").ValueKind != JsonValueKind.False ||
            (intake is not null) != (outcome == "ACCEPTED") ||
            (codes.Length != 0) != (outcome == "NEEDS_INFO") || (error is not null) != (outcome == "REJECTED_FINAL"))
            throw TypeMismatch();
        Utc(value.GetProperty("ownerRecordedAt"));
        if (correction)
        {
            _ = EnumValue(kind, "INTAKE_CORRECTION");
            _ = EnumValue(value.GetProperty("replacesOutcome"), "REJECTED_FINAL");
            if (outcome != "ACCEPTED" || ordinal == "1" || Rev(value.GetProperty("correctionRevision")) != "1" ||
                ReadSource(value.GetProperty("sourceHandoffIdentity")) != source || Key(value, "predecessorResultId") == receipt)
                throw TypeMismatch();
            ConfirmationEvidence(value.GetProperty("mainEvidenceRef"), route, source);
            foreach (var item in Items(value.GetProperty("resolvedConflicts")))
            {
                Exact(item, "conflictId", "conflictKeyHash", "incomingDigest");
                _ = Id(item.GetProperty("conflictId"));
                _ = Digest(item.GetProperty("conflictKeyHash"));
                _ = Digest(item.GetProperty("incomingDigest"));
            }
            Text(value.GetProperty("reason"), 1000);
            Evidence(value.GetProperty("ownerDecision"));
        }
        else Evidence(value.GetProperty("ownerEvidence"));
        return Result("OPP-INTAKE-BUSINESS-1", value, eventId, receipt, ordinal, source);
    }

    private static void ConfirmationEvidence(JsonElement value, CommercialRouteV1 route, CommercialSourceHandoffV1 source)
    {
        Exact(value, "authority", "legalEntityKey", "groupKey", "stream", "ordinal", "resultId", "businessDigest",
            "bindingDigest", "source", "ownerEvidence");
        if (Key(value, "legalEntityKey") != route.LegalEntityKey || ReadSource(value.GetProperty("source")) != source)
            throw RouteMismatch();
        _ = Key(value, "authority");
        _ = Key(value, "groupKey");
        _ = Rev(value.GetProperty("ordinal"));
        _ = Key(value, "resultId");
        _ = Digest(value.GetProperty("businessDigest"));
        _ = Digest(value.GetProperty("bindingDigest"));
        Evidence(value.GetProperty("ownerEvidence"));
        var stream = value.GetProperty("stream");
        Stream(stream);
        _ = EnumValue(stream.GetProperty("contractVersion"), CommercialOriginalV1.Commercial);
    }

    private static CommercialWireCoordinatesV1 Quote(CommercialRouteV1 route, JsonElement value, string eventId, string ordinal)
    {
        Exact(value, "schemaVersion", "tenantId", "eventId", "producer", "epoch", "source", "sequence", "authority", "legalEntityKey",
            "quoteKey", "quoteVersion", "supersedesQuoteVersion", "quoteStatus", "net", "tax", "gross", "validUntil",
            "ownerRecordedAt", "mainReceiptId", "ownerEvidence");
        RequireOwnerRoute(route, value);
        var version = Key(value, "quoteVersion");
        if (NullableKey(value.GetProperty("supersedesQuoteVersion")) == version) throw TypeMismatch();
        _ = EnumValue(value.GetProperty("quoteStatus"), "DRAFT", "ISSUED", "ACCEPTED", "REJECTED", "WITHDRAWN");
        Money(value.GetProperty("net"));
        Null(value.GetProperty("tax"));
        Null(value.GetProperty("gross"));
        NullableUtc(value.GetProperty("validUntil"));
        Utc(value.GetProperty("ownerRecordedAt"));
        _ = Key(value, "mainReceiptId");
        Evidence(value.GetProperty("ownerEvidence"));
        return Result("OPP-QUOTE-BUSINESS-1", value, eventId, Key(value, "quoteKey"), ordinal, ReadSource(value.GetProperty("source")));
    }

    private static void RequireOwnerRoute(CommercialRouteV1 route, JsonElement value)
    {
        if (Key(value, "authority") != route.Authority || Key(value, "legalEntityKey") != route.LegalEntityKey) throw RouteMismatch();
    }

    private static void Stream(JsonElement value)
    {
        Exact(value, "producer", "contractVersion", "epoch");
        foreach (var field in value.EnumerateObject()) _ = Key(field.Value);
    }

    private static CommercialWireCoordinatesV1 Result(string domain, JsonElement body, string eventId, string key, string ordinal,
        CommercialSourceHandoffV1 source) => new(eventId, key, ordinal, source,
            CommercialCanonicalV1.Digest(domain, body, removeProof: true, omitRoot: ["eventId"]));
}
