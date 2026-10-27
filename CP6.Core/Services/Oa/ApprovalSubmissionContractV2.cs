using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CP6.Core.Services.Oa;

// oa.app/2.0 Owner-generated envelope. There is deliberately no public endpoint accepting this from a browser.
public sealed record ApprovalRefV2(string Owner, string Id, string Version);
public sealed record ApprovalDigestV2(string Algorithm, string CanonicalizationVersion, string Value);
public sealed record ApprovalScopeV2(string EnvironmentId, Guid TenantId);
public sealed record ApprovalSubjectV2(string BusinessOwner, string BizType, string SubjectKind, string SubjectId,
    string BusinessRevision, ApprovalDigestV2 ContentDigest);
public sealed record ApprovalPolicyV2(ApprovalRefV2 Ref, string Version, ApprovalDigestV2 Digest);
public sealed record ApprovalSnapshotV2(string Schema, string CanonicalBytesBase64, ApprovalDigestV2 Digest,
    ApprovalRefV2[] ImmutableEvidenceRefs);
public sealed record ApprovalSubmitCommandV2(string Schema, ApprovalScopeV2 Scope, Guid CommandId, Guid SubmissionId,
    ApprovalSubjectV2 Subject, ApprovalPolicyV2 Policy, string? RoundId, Guid? ReplacesSubmissionId,
    string BusinessSubmittedRowVersion, Guid StarterId, ApprovalRefV2 SubmitAuthorizationRef,
    Guid[] SeparationSubjectIds, ApprovalSnapshotV2 Snapshot, JsonElement? Engineering, string Reason,
    Guid OwnerOutboxId, DateTimeOffset OwnerFrozenAt);

/// <summary>Explicit canonical codec: ordinal keys, original Unicode, no whitespace, UTC millisecond instants.
/// Owner-specific digest domains remain explicit and are never relabelled as this codec.</summary>
internal static class ApprovalCanonicalV2
{
    internal const string Codec = "oa.app.canonical/2.0";
    internal static byte[] Bytes(object value)
    {
        var element = JsonSerializer.SerializeToElement(value, ApprovalBindingOwnerV2.Json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) Write(writer, element);
        return stream.ToArray();
    }
    private static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var p in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(p.Name); Write(writer, p.Value); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) Write(writer, item); writer.WriteEndArray(); break;
            default: element.WriteTo(writer); break;
        }
    }
    internal static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    internal static ApprovalDigestV2 Digest(byte[] bytes) => new("sha256", Codec, Hash(bytes));
}
