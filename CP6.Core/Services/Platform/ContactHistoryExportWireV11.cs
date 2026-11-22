using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Core.Services.Platform;

public sealed record ContactHistoryIntentV11(
    [property: JsonRequired] string SchemaVersion, [property: JsonRequired] string DatasetSchemaVersion,
    [property: JsonRequired] Guid TenantId, [property: JsonRequired] Guid ContactId,
    [property: JsonRequired] Guid RequestedBy, [property: JsonRequired] Guid ReaderSubjectId,
    [property: JsonRequired] string Dataset, [property: JsonRequired] string View,
    [property: JsonRequired] string[] FieldIds, [property: JsonRequired] string ReadPurpose,
    [property: JsonRequired] string RecordedThrough, [property: JsonRequired] string? OccurrenceFromUtc,
    [property: JsonRequired] string? OccurrenceToUtc, [property: JsonRequired] string Format);

public sealed record ContactHistoryRequestV11(
    [property: JsonRequired] string ContractVersion, [property: JsonRequired] Guid EventId, [property: JsonRequired] Guid RequestId,
    [property: JsonRequired] string SourceOwner, [property: JsonRequired] string Canonicalization,
    [property: JsonRequired] string IntentDigest, [property: JsonRequired] ContactHistoryIntentV11 Intent,
    [property: JsonRequired] string SourcePermissionWatermark);

public sealed record ContactHistorySubjectV11([property: JsonRequired] Guid TenantId, [property: JsonRequired] Guid ReaderSubjectId,
    [property: JsonRequired] Guid ContactId, [property: JsonRequired] string ReadPurpose, [property: JsonRequired] string[] FieldIds,
    [property: JsonRequired] string CurrentAccessEvidence);

public sealed record ContactHistoryArtifactV11([property: JsonRequired] Guid RequestId, [property: JsonRequired] string JobId,
    [property: JsonRequired] string ArtifactId, [property: JsonRequired] string ArtifactVersion, [property: JsonRequired] string IntentDigest,
    [property: JsonRequired] string ContentSha256, [property: JsonRequired] string Bytes, [property: JsonRequired] string Format);

public sealed record ContactHistoryReadV11([property: JsonRequired] ContactHistorySubjectV11 Subject,
    [property: JsonRequired] ContactHistoryArtifactV11 Artifact);

public sealed record ContactHistoryTrustV11([property: JsonRequired] Guid TenantId, [property: JsonRequired] string Producer,
    [property: JsonRequired] string Authority, [property: JsonRequired] string EvidenceRef, [property: JsonRequired] DateTimeOffset VerifiedAtUtc);

public sealed record ContactHistoryCheckpointV11([property: JsonRequired] string Authority, [property: JsonRequired] string Reference,
    [property: JsonRequired] string Revision, [property: JsonRequired] string StreamIdentityDigest,
    [property: JsonRequired] string CoversThrough, [property: JsonRequired] string[] ResolvesConflictIds, [property: JsonRequired] string Proof);

public sealed record ContactHistoryCallerV11(Guid TenantId, string ClientId, string Issuer, string TokenId, DateTimeOffset ExpiresAt, bool CanReconcile);

public sealed class ContactHistoryExportExceptionV11(string code, int status) : InvalidOperationException(code)
{
    public int Status { get; } = status;
}

public static class ContactHistoryExportWireV11
{
    public const string Contract = "CONTACT-HISTORY-EXPORT/1.1";
    public const string Dataset = "CONTACT_HISTORY";
    public const string Schema = "CONTACT-HISTORY-SCHEMA/1";
    public const string Format = "CSV_VIEW";
    public const string Producer = "Main.PRIV.FILE.ContactHistory";
    public const string Authority = "Main.PRIV.FILE.ContactHistory/1";
    public const int MaximumBytes = 2 * 1024 * 1024;
    public static IReadOnlyList<string> FieldIds { get; } = Array.AsReadOnly(new[]
        { "contact.displayName", "contact.lifecycle", "relationships.periods", "activities.summary" });

    internal const string Canonicalization = "CONTACT-EXPORT-CANON-1";
    internal const string DenialDomain = "BEFORE_JOB_ALLOCATION";
    private const string UtcPattern = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    internal static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new WireId() }
    };

    private sealed class WireId : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && Guid.TryParseExact(reader.GetString(), "D", out var id) && id != Guid.Empty
                && reader.GetString() == id.ToString("D")) return id;
            throw new JsonException("CONTACT_EXPORT_ID_INVALID");
        }

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }

    public static T Read<T>(JsonElement body) where T : class
    {
        VerifyUniqueMembers(body);
        return body.Deserialize<T>(WireJson) ?? throw Invalid();
    }

    private static void VerifyUniqueMembers(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid();
                VerifyUniqueMembers(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var member in value.EnumerateArray()) VerifyUniqueMembers(member);
    }

    public static string Key(string? value, int maximum = 256) =>
        value is { Length: > 0 } && value.Length <= maximum && !value.Any(char.IsControl) && !string.IsNullOrWhiteSpace(value)
            ? value : throw Invalid();

    public static string Sha(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower) ? value : throw Invalid();

    internal static long Sequence(string? value, bool allowZero = false) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
        && revision >= (allowZero ? 0 : 1) && Text(revision) == value ? revision : throw Invalid();

    internal static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
    internal static string Time(DateTimeOffset utc) => utc.UtcDateTime.ToString(UtcPattern, CultureInfo.InvariantCulture);

    internal static DateTimeOffset ParseTime(string value) => DateTimeOffset.TryParseExact(value,
        [UtcPattern, "yyyy-MM-dd'T'HH:mm:ss'Z'"], CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time) ? time : throw Invalid();

    internal static void RequireFields(string[]? fields)
    {
        if (fields is null || fields.Length is < 1 or > 4 || fields.Distinct(StringComparer.Ordinal).Count() != fields.Length ||
            fields.Any(field => !FieldIds.Contains(field, StringComparer.Ordinal))) throw Invalid();
    }

    internal static ContactHistoryRequestV11 Validate(ContactHistoryRequestV11 request, Guid tenant)
    {
        if (request is not { ContractVersion: Contract, Canonicalization: Canonicalization, SourceOwner: "CRM_CONTACT", Intent: { } intent } ||
            request.EventId == Guid.Empty || request.RequestId == Guid.Empty) throw Invalid();
        if (intent.TenantId != tenant) throw Denied();
        if (intent.SchemaVersion != "CONTACT-HISTORY-INTENT/1" || intent.DatasetSchemaVersion != Schema || intent.Dataset != Dataset ||
            intent.Format != Format || intent.View is not ("AS_RECORDED" or "CORRECTED_INTERPRETATION") || intent.ContactId == Guid.Empty ||
            intent.RequestedBy == Guid.Empty || intent.RequestedBy != intent.ReaderSubjectId) throw Invalid();
        RequireFields(intent.FieldIds);
        _ = Key(request.SourcePermissionWatermark);
        var cut = ParseTime(intent.RecordedThrough);
        var from = intent.OccurrenceFromUtc is { } start ? ParseTime(start) : (DateTimeOffset?)null;
        var to = intent.OccurrenceToUtc is { } end ? ParseTime(end) : (DateTimeOffset?)null;
        if (from >= to) throw Invalid();
        intent = intent with { RecordedThrough = Time(cut), OccurrenceFromUtc = from.HasValue ? Time(from.Value) : null,
            OccurrenceToUtc = to.HasValue ? Time(to.Value) : null, ReadPurpose = Key(intent.ReadPurpose, 64).Normalize(NormalizationForm.FormC) };
        if (Sha(request.IntentDigest) != IntentDigest(intent)) throw Conflict("CONTACT_EXPORT_INTENT_MISMATCH");
        return request with { Intent = intent };
    }

    internal static void Validate(ContactHistoryReadV11 read, Guid tenant)
    {
        if (read is not { Subject: { } subject, Artifact: { } artifact }) throw Invalid();
        if (subject.TenantId != tenant) throw Denied();
        RequireFields(subject.FieldIds);
        _ = Key(subject.ReadPurpose, 64);
        _ = Key(subject.CurrentAccessEvidence);
        if (subject.ReaderSubjectId == Guid.Empty || subject.ContactId == Guid.Empty || artifact.RequestId == Guid.Empty ||
            artifact.Format != Format || Sequence(artifact.Bytes) > MaximumBytes) throw Invalid();
        _ = Key(artifact.JobId);
        _ = Key(artifact.ArtifactId);
        _ = Key(artifact.ArtifactVersion);
        _ = Sha(artifact.IntentDigest);
        _ = Sha(artifact.ContentSha256);
    }

    public static string IntentDigest(ContactHistoryIntentV11 intent) => Hash([.. "CONTACT-EXPORT-CANON-1\n"u8, .. Canonical(intent)]);
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, WireJson);

    // CRM 1.1 使用 Ordinal 键序、保序数组和同一 JSON 编码器。日期及 NFC 在意图入口规范化。
    internal static byte[] Canonical<T>(T value)
    {
        using var data = new MemoryStream();
        using (var writer = new Utf8JsonWriter(data, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            WriteCanonical(writer, JsonSerializer.SerializeToElement(value, WireJson));
        return data.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in item.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (item.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var value in item.EnumerateArray()) WriteCanonical(writer, value);
            writer.WriteEndArray();
        }
        else item.WriteTo(writer);
    }

    internal static ContactHistoryExportExceptionV11 Invalid() => new("CONTACT_EXPORT_SCHEMA_INVALID", 400);
    internal static ContactHistoryExportExceptionV11 Denied() => new("CONTACT_EXPORT_ACCESS_DENIED", 403);
    internal static ContactHistoryExportExceptionV11 Missing() => new("CONTACT_EXPORT_NOT_VISIBLE", 404);
    internal static ContactHistoryExportExceptionV11 Conflict(string code) => new(code, 409);
    internal static ContactHistoryExportExceptionV11 Unavailable() => new("CONTACT_EXPORT_OUTCOME_UNPROVEN", 503);
}
