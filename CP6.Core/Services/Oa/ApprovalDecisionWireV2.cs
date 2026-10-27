using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CP6.Core.Services.Sys;
using CP6.Entity.Contracts.Wire;
using static CP6.Core.Services.Oa.ApprovalSubmissionWireV2;

namespace CP6.Core.Services.Oa;

public sealed record ApprovalDecisionEnvelopeV2(string Schema, ApprovalScopeV2 Scope, Guid EventId, Guid DecisionId,
    Guid StreamId, string Sequence, string ProducerOwner, string ConsumerOwner, Guid SubmissionId, Guid InstanceId,
    ApprovalSubjectV2 Subject, ApprovalPolicyV2 Policy, Guid BindingRevisionId, Guid BindingTimelineRevisionId,
    Guid FlowDefVersionId, string? RoundId, JsonElement? Engineering, string Decision, string DecisionMode,
    DateTimeOffset DecidedAt, JsonElement TerminalActor, ApprovalRefV2? DecisionEvidenceSetRef,
    ApprovalDigestV2? DecisionEvidenceDigest, string[] SatisfiedRequirementIds, string? Reason,
    ApprovalRefV2 ResultRef, ApprovalDigestV2 PayloadDigest);

internal static class ApprovalDecisionWireV2
{
    internal sealed record Parsed(ApprovalDecisionEnvelopeV2 Value, byte[] Canonical, string Full, string Core);
    private static void Instant(JsonElement e)
    {
        if (!DateTimeOffset.TryParseExact(e.GetString(), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out _)) throw new JsonException();
    }
    private static void Human(JsonElement a, string principalName)
    {
        foreach (var name in new[] { principalName, "actualHandlerId" }) Id(a.GetProperty(name));
        Id(a.GetProperty("onBehalfOfId"), true); Id(a.GetProperty("grantId"), true);
        var principal = a.GetProperty(principalName).GetGuid(); var actual = a.GetProperty("actualHandlerId").GetGuid();
        if (principal == actual)
        {
            if (new[] { "onBehalfOfId", "grantId", "grantRevision" }.Any(n => a.GetProperty(n).ValueKind != JsonValueKind.Null)) throw new JsonException();
        }
        else
        {
            if (a.GetProperty("onBehalfOfId").GetGuid() != principal || a.GetProperty("grantId").ValueKind == JsonValueKind.Null) throw new JsonException();
            Key(a.GetProperty("grantRevision"));
        }
    }
    private static void Actor(JsonElement a)
    {
        switch (a.GetProperty("kind").GetString())
        {
            case "HUMAN_TASK":
                Shape(a, "kind", "decisionPrincipalId", "actualHandlerId", "onBehalfOfId", "taskId", "roleKey", "grantId", "grantRevision", "permissionEvidenceRef", "sodEvidenceRef", "occurredAt");
                Human(a, "decisionPrincipalId"); Id(a.GetProperty("taskId")); Key(a.GetProperty("roleKey"));
                Ref(a.GetProperty("permissionEvidenceRef")); Ref(a.GetProperty("sodEvidenceRef")); break;
            case "SYSTEM_EXECUTION":
                Shape(a, "kind", "serviceId", "executionId", "trigger", "executionAuthorizationRef", "policyRef", "occurredAt");
                Key(a.GetProperty("serviceId")); Id(a.GetProperty("executionId"));
                if (a.GetProperty("trigger").GetString() is not ("TIMEOUT" or "RESUME" or "RULE")) throw new JsonException();
                Ref(a.GetProperty("executionAuthorizationRef")); Ref(a.GetProperty("policyRef")); break;
            case "SUBMITTER_WITHDRAWAL":
                Shape(a, "kind", "withdrawCommandId", "requesterId", "actualHandlerId", "onBehalfOfId", "grantId", "grantRevision", "ownerWithdrawalRef", "withdrawAuthorizationRef", "occurredAt");
                Human(a, "requesterId"); Id(a.GetProperty("withdrawCommandId"));
                Ref(a.GetProperty("ownerWithdrawalRef")); Ref(a.GetProperty("withdrawAuthorizationRef")); break;
            default: throw new JsonException();
        }
        Instant(a.GetProperty("occurredAt"));
    }
    internal static Parsed Parse(byte[] raw)
    {
        if (raw.Length > 1048576) throw Error("DECISION_SHAPE_INVALID");
        StrictJsonDocument document;
        try { _ = new UTF8Encoding(false, true).GetString(raw); document = WorkflowCommandJsonV1.Parse(raw); }
        catch (Exception e) when (e is JsonException or DecoderFallbackException) { throw Error("MALFORMED_JSON", 400); }
        using (document) try
        {
            var r = document.RootElement;
            Shape(r, "schema", "scope", "eventId", "decisionId", "streamId", "sequence", "producerOwner", "consumerOwner", "submissionId", "instanceId", "subject", "policy",
                "bindingRevisionId", "bindingTimelineRevisionId", "flowDefVersionId", "roundId", "engineering", "decision", "decisionMode", "decidedAt", "terminalActor",
                "decisionEvidenceSetRef", "decisionEvidenceDigest", "satisfiedRequirementIds", "reason", "resultRef", "payloadDigest");
            if (r.GetProperty("schema").GetString() != "oa.app/2.0") throw Error("UNSUPPORTED_SCHEMA");
            var scope = r.GetProperty("scope"); Shape(scope, "environmentId", "tenantId"); Key(scope.GetProperty("environmentId")); Id(scope.GetProperty("tenantId"));
            foreach (var name in new[] { "eventId", "decisionId", "streamId", "submissionId", "instanceId", "bindingRevisionId", "bindingTimelineRevisionId", "flowDefVersionId" }) Id(r.GetProperty(name));
            Key(r.GetProperty("sequence")); if (!Regex.IsMatch(r.GetProperty("sequence").GetString()!, @"\A[1-9][0-9]*\z")) throw new JsonException();
            if (r.GetProperty("producerOwner").GetString() != "Main Workflow" || r.GetProperty("streamId").GetGuid() != r.GetProperty("submissionId").GetGuid()) throw new JsonException();
            Key(r.GetProperty("consumerOwner")); var subject = r.GetProperty("subject");
            Shape(subject, "businessOwner", "bizType", "subjectKind", "subjectId", "businessRevision", "contentDigest");
            foreach (var p in subject.EnumerateObject()) if (p.Name == "contentDigest") Digest(p.Value); else Key(p.Value, p.Name == "businessRevision" ? 128 : 100);
            if (subject.GetProperty("businessOwner").GetString() != r.GetProperty("consumerOwner").GetString()) throw new JsonException();
            var policy = r.GetProperty("policy"); Shape(policy, "ref", "version", "digest"); Ref(policy.GetProperty("ref")); Key(policy.GetProperty("version")); Digest(policy.GetProperty("digest"));
            if (policy.GetProperty("version").GetString() != policy.GetProperty("ref").GetProperty("version").GetString()) throw new JsonException();
            if (r.GetProperty("roundId").ValueKind != JsonValueKind.Null) Key(r.GetProperty("roundId")); Engineering(r.GetProperty("engineering"));
            Actor(r.GetProperty("terminalActor")); Instant(r.GetProperty("decidedAt")); Ref(r.GetProperty("resultRef")); Digest(r.GetProperty("payloadDigest"));
            var requirements = r.GetProperty("satisfiedRequirementIds");
            if (requirements.GetArrayLength() > 1000) throw new JsonException(); foreach (var x in requirements.EnumerateArray()) Key(x);
            var reason = r.GetProperty("reason"); if (reason.ValueKind != JsonValueKind.Null && (reason.ValueKind != JsonValueKind.String || reason.GetString()!.EnumerateRunes().Count() > 500)) throw new JsonException();
            var value = r.Deserialize<ApprovalDecisionEnvelopeV2>(ApprovalBindingOwnerV2.Json)!;
            var kind = value.TerminalActor.GetProperty("kind").GetString();
            if (value.Decision == "WITHDRAWN")
            {
                if (value.DecisionMode != "WITHDRAWAL" || kind != "SUBMITTER_WITHDRAWAL" || value.DecisionEvidenceSetRef != null || value.DecisionEvidenceDigest != null || value.SatisfiedRequirementIds.Length != 0 || string.IsNullOrWhiteSpace(value.Reason)) throw new JsonException();
            }
            else
            {
                if (value.Decision is not ("APPROVED" or "REJECTED") || value.DecisionMode is not ("HUMAN" or "SYSTEM_POLICY") || kind == "SUBMITTER_WITHDRAWAL") throw new JsonException();
                Ref(r.GetProperty("decisionEvidenceSetRef")); Digest(r.GetProperty("decisionEvidenceDigest"));
                if (value.DecisionMode == "SYSTEM_POLICY" && kind != "SYSTEM_EXECUTION") throw new JsonException();
                if (value.DecisionMode == "HUMAN" && (value.SatisfiedRequirementIds.Length == 0 || kind == "SYSTEM_EXECUTION" && value.TerminalActor.GetProperty("trigger").GetString() == "RULE")) throw new JsonException();
                if (value.Decision == "REJECTED" && string.IsNullOrWhiteSpace(value.Reason)) throw new JsonException();
            }
            var canonical = ApprovalCanonicalV2.Bytes(r); var node = JsonNode.Parse(canonical)!.AsObject(); node.Remove("payloadDigest");
            if (value.PayloadDigest != ApprovalCanonicalV2.Digest(ApprovalCanonicalV2.Bytes(node))) throw Error("PAYLOAD_DIGEST_MISMATCH");
            node.Remove("eventId");
            return new(value, canonical, ApprovalCanonicalV2.Hash(canonical), ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(node)));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException)
        { if (e is ScopeAuthorizationExceptionV1) throw; throw Error("DECISION_SHAPE_INVALID"); }
    }
}
