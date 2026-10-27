using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Entity.Contracts.Wire;
using static CP6.Core.Services.Oa.ApprovalSubmissionWireV2;

namespace CP6.Core.Services.Oa;

public sealed record ApprovalApplicationReceiptV2(string Schema, ApprovalScopeV2 Scope, Guid ReceiptId,
    Guid ReceiptStreamId, string ReceiptRevision, Guid ReceiptEventId, string ProducerOwner, Guid SubmissionId,
    Guid InstanceId, Guid DecisionId, ApprovalDigestV2 DecisionPayloadDigest, ApprovalSubjectV2 Subject,
    string Action, string Application, string OutcomeCode, DateTimeOffset? AppliedAt, Guid? ActionActorId,
    ApprovalRefV2? SystemExecutionRef, ApprovalRefV2? BusinessResultRef, string? BusinessResultVersion,
    string CurrentSubjectRevision, ApprovalRefV2 AuditRef, Guid? PreviousReceiptId, DateTimeOffset RecordedAt,
    ApprovalDigestV2 PayloadDigest);

internal static class ApprovalApplicationReceiptWireV2
{
    internal static ApprovalApplicationReceiptV2 Seal(ApprovalApplicationReceiptV2 value)
    {
        var node = JsonNode.Parse(ApprovalCanonicalV2.Bytes(value))!.AsObject(); node.Remove("payloadDigest");
        return value with { PayloadDigest = ApprovalCanonicalV2.Digest(ApprovalCanonicalV2.Bytes(node)) };
    }
    private static void Instant(JsonElement value)
    {
        if (!DateTimeOffset.TryParseExact(value.GetString(), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out _)) throw new JsonException();
    }
    internal static ApprovalApplicationReceiptV2 Parse(byte[] bytes)
    {
        try
        {
            if (bytes.Length > 1048576) throw new JsonException();
            _ = new UTF8Encoding(false, true).GetString(bytes);
            using var document = WorkflowCommandJsonV1.Parse(bytes); var r = document.RootElement;
            Shape(r, "schema", "scope", "receiptId", "receiptStreamId", "receiptRevision", "receiptEventId", "producerOwner",
                "submissionId", "instanceId", "decisionId", "decisionPayloadDigest", "subject", "action", "application", "outcomeCode",
                "appliedAt", "actionActorId", "systemExecutionRef", "businessResultRef", "businessResultVersion", "currentSubjectRevision",
                "auditRef", "previousReceiptId", "recordedAt", "payloadDigest");
            if (r.GetProperty("schema").GetString() != "oa.app/2.0") throw new JsonException();
            var scope = r.GetProperty("scope"); Shape(scope, "environmentId", "tenantId"); Key(scope.GetProperty("environmentId")); Id(scope.GetProperty("tenantId"));
            foreach (var n in new[] { "receiptId", "receiptStreamId", "receiptEventId", "submissionId", "instanceId", "decisionId" }) Id(r.GetProperty(n));
            foreach (var n in new[] { "producerOwner", "action", "outcomeCode", "currentSubjectRevision" }) Key(r.GetProperty(n), n == "currentSubjectRevision" ? 128 : 100);
            foreach (var n in new[] { "actionActorId", "previousReceiptId" }) Id(r.GetProperty(n), true);
            foreach (var n in new[] { "systemExecutionRef", "businessResultRef" }) if (r.GetProperty(n).ValueKind != JsonValueKind.Null) Ref(r.GetProperty(n));
            Ref(r.GetProperty("auditRef")); Digest(r.GetProperty("decisionPayloadDigest")); Digest(r.GetProperty("payloadDigest"));
            var subject = r.GetProperty("subject"); Shape(subject, "businessOwner", "bizType", "subjectKind", "subjectId", "businessRevision", "contentDigest");
            foreach (var p in subject.EnumerateObject()) if (p.Name == "contentDigest") Digest(p.Value); else Key(p.Value, p.Name == "businessRevision" ? 128 : 100);
            var revision = r.GetProperty("receiptRevision").GetString();
            if (!long.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out var nrev) || nrev < 1 || revision != nrev.ToString(CultureInfo.InvariantCulture)) throw new JsonException();
            Instant(r.GetProperty("recordedAt")); if (r.GetProperty("appliedAt").ValueKind != JsonValueKind.Null) Instant(r.GetProperty("appliedAt"));
            var value = r.Deserialize<ApprovalApplicationReceiptV2>(ApprovalBindingOwnerV2.Json)!;
            if (value.ProducerOwner != value.Subject.BusinessOwner || value.Application is not ("APPLIED" or "STALE" or "CONFLICT" or "BLOCKED")
                || (value.ActionActorId == null) == (value.SystemExecutionRef == null)
                || (nrev == 1) != (value.PreviousReceiptId == null)
                || value.BusinessResultRef?.Version != value.BusinessResultVersion
                || value.Application == "APPLIED" && (value.AppliedAt == null || value.BusinessResultRef == null)
                || value.Application != "APPLIED" && value.AppliedAt != null) throw new JsonException();
            if (value.BusinessResultVersion != null) Key(r.GetProperty("businessResultVersion"));
            if (Seal(value).PayloadDigest != value.PayloadDigest) throw new JsonException();
            return value;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or FormatException or KeyNotFoundException)
        { throw Error("APPLICATION_RECEIPT_INVALID", 422); }
    }
}
