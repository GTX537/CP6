using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using CP6.Core.Services.Oa;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StandardDocumentCreateV1(Guid CommandId, Guid WorkOrderId, string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StandardDocumentPrepareV1(Guid CommandId, [property: JsonRequired] string? ExpectedVersion,
    Guid StandardBasisVersionId, string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StandardDocumentFreezeV1(Guid CommandId, [property: JsonRequired] string? ExpectedVersion, Guid PlanId, string Reason);

public sealed record StandardDocumentReplyV1(Guid CommandId, Guid ReceiptId, string Operation, Guid DocumentId,
    string? Version, Guid? PlanId, DateTimeOffset? ExpiresAt, bool Replayed)
{
    public string Kind => "COMMITTED";
}

internal sealed record StandardDocumentBinding(Guid CommandId, string Route, byte[] BodyDigest, byte[] ExpectedDigest,
    byte[] R2BodyDigest, byte[] R2ExpectedDigest)
{
    internal const string Documents = "api/fin/cost/owner-v1/standard-documents";
    internal const string Commands = "api/fin/cost/owner-v1/standard-document-commands";

    internal static StandardDocumentBinding Create(Guid id, string route, object request, object expected, string? reason)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            throw Error("STANDARD_DOCUMENT_COMMAND_INVALID", 422);
        return new(id, route, Digest(request), Digest(expected),
            FinanceCostCommandCanonicalV1.Digest(request), FinanceCostCommandCanonicalV1.Digest(expected));
    }

    // Numeric quantities and revision numbers on this wire are strings.
    private static byte[] Digest(object value)
        => SHA256.HashData([.."CP6/STANDARD-DOCUMENT/1\0"u8, ..ApprovalCanonicalV2.Bytes(value)]);

    internal static string? VersionText(long? number) => number?.ToString(CultureInfo.InvariantCulture);

    internal static long? ParseVersion(string? value)
    {
        if (value == null)
            return null;
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number <= 0 || value != VersionText(number))
            throw Error("STANDARD_DOCUMENT_EXPECTED_INVALID", 422);
        return number;
    }
}
