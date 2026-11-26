using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;

namespace CP6.Core.Services.Pub;

public sealed record ActFileAnchorV1(string Kind, Guid Id, string Version);

public sealed record ActFileIdentityV1(Guid TenantId, Guid SubjectId, string OperationKind, Guid OperationKey,
    string IntentDigest, ActFileAnchorV1 Primary, Guid? ActivityId, string? ActivityRevision,
    string? ReferenceSetVersion, Guid? ReferenceId, string AttachmentId, string Version, string ContentSha256,
    string Purpose, string Action, string? CurrentAccessStamp);

public sealed record ActFileProofRequestV1(string Mode, ActFileIdentityV1[] References);

public sealed record ActFileAttestationV1(string KeyId, byte[] Body, byte[] Signature);

public sealed record ActFileGuardStampV1(Guid StoreId, string Epoch, Guid Receipt)
{
    internal ActFileGuardStampV1(Guid storeId, long epoch, Guid receipt)
        : this(storeId, epoch.ToString(CultureInfo.InvariantCulture), receipt) { }
}

public sealed record ActFileDownloadEvidenceV1(string Bytes, string MediaType, string SafeFileName);

public sealed record ActFileDecisionV1(ActFileIdentityV1 Identity, string Decision, string ScanState,
    string ScanVersion, string ScanEvidenceDigest, Guid PolicyVersion, Guid? RetentionDecisionRef,
    ActFileDownloadEvidenceV1? Download);

public sealed record ActFileEvidenceV1(string SchemaVersion, string Authority, string Mode, string KeyId,
    Guid AttestationRef, string ConsumerClientId, ActFileGuardStampV1 Guard, ActFileDecisionV1[] Decisions);

public sealed record ActFileCallerV1(Guid TenantId, string ClientId, string Issuer, string TokenId, DateTimeOffset ExpiresAt);

public static class ActFileReferenceV1
{
    public const string Contract = "ACT-FILE-REFERENCE/1";
    public const string Authority = "Main.File.Content.ACT/1";
    public const string LocalGuard = "LOCAL_AUTHORITATIVE_GUARD";
    public const string GuardedStream = "OWNER_GUARDED_STREAM";
    public const string Purpose = "ACTIVITY_EVIDENCE";
    internal const string DownloadMediaType = "application/octet-stream";
    internal static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false
    };

    public static T Read<T>(JsonElement body) where T : class =>
        body.Deserialize<T>(Wire) ?? throw Invalid();

    internal static OrderPriceCorrectionException Invalid() => new("ACT_FILE_REFERENCE_INVALID", 400);
    internal static OrderPriceCorrectionException Denied() => new("ACT_FILE_REFERENCE_FORBIDDEN", 403);
    internal static OrderPriceCorrectionException Unproven(Exception? cause = null) => new("COMMIT_WINDOW_UNPROVEN", 503, cause);

    internal static ActFileIdentityV1 Validate(ActFileProofRequestV1 request, Guid tenant)
    {
        if (request.Mode is not (LocalGuard or GuardedStream))
            throw new OrderPriceCorrectionException("NO_COMMIT_GUARANTEE", 503);
        if (request.References is not { Length: > 0 } || request.References[0] is not { } first ||
            request.Mode == GuardedStream && request.References.Length != 1)
            throw Invalid();
        if (first.TenantId != tenant) throw Denied();
        var references = new HashSet<Guid>();
        var attachments = new HashSet<(string, string)>();
        foreach (var value in request.References)
        {
            if (value is null || value.TenantId != tenant || value.SubjectId != first.SubjectId ||
                value.OperationKey != first.OperationKey || value.OperationKind != first.OperationKind ||
                value.IntentDigest != first.IntentDigest || value.Primary != first.Primary ||
                value.ActivityId != first.ActivityId || value.ActivityRevision != first.ActivityRevision ||
                value.ReferenceSetVersion != first.ReferenceSetVersion)
                throw Invalid();
            if (value.SubjectId == Guid.Empty || value.OperationKey == Guid.Empty || !Opaque(value.AttachmentId) ||
                value.Primary is not { Id: var anchor } || anchor == Guid.Empty ||
                value.Primary.Kind is not ("Account" or "Contact" or "Lead" or "Opportunity") ||
                !Opaque(value.Primary.Version) || !Key(value.OperationKind, 64) ||
                !Digest(value.IntentDigest) || !Digest(value.ContentSha256) || !Opaque(value.Version))
                throw Invalid();
            if (value.Purpose != Purpose) throw Denied();
            if (!attachments.Add((value.AttachmentId, value.Version))) throw Invalid();
            if (value.ActivityId is null)
            {
                if (value.ActivityRevision is not null || value.ReferenceSetVersion is not null || value.Action != "ADD")
                    throw Invalid();
            }
            else if (value.ActivityId == Guid.Empty || !Revision(value.ActivityRevision) || !Revision(value.ReferenceSetVersion))
                throw Invalid();

            if (request.Mode == GuardedStream)
            {
                if (value.Action != "DOWNLOAD" || !Opaque(value.CurrentAccessStamp))
                    throw Invalid();
            }
            else if (value.CurrentAccessStamp is not null || value.Action is not ("ADD" or "RETAIN" or "REMOVE"))
                throw Invalid();

            if (value.Action == "ADD")
            {
                if (value.ReferenceId is not null) throw Invalid();
            }
            else if (value.ReferenceId is not { } reference || reference == Guid.Empty || !references.Add(reference))
                throw Invalid();
        }
        return first;
    }

    internal static bool Key(string? value, int max = 192) =>
        value is { Length: > 0 } && value.Length <= max &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':' or '/');

    internal static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);

    private static bool Opaque(string? value) => !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    private static bool Revision(string? value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
        out var parsed) && parsed > 0 && value == parsed.ToString(CultureInfo.InvariantCulture);
}

public sealed class ActFileSignerV1
{
    private const string AlgorithmOid = "1.2.840.10045.3.1.7";
    private readonly string privateKeyPem;
    public string KeyId { get; }
    public string PublicKeySpkiBase64 { get; }

    public ActFileSignerV1(string keyId, string privateKeyPem)
    {
        if (!ActFileReferenceV1.Key(keyId, 64)) throw new InvalidOperationException("ACT 文件签名键标识无效。");
        this.privateKeyPem = privateKeyPem;
        KeyId = keyId;
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        var parameters = key.ExportParameters(true);
        try
        {
            if (parameters.Curve.Oid.Value != AlgorithmOid || parameters.D is not { Length: 32 })
                throw new InvalidOperationException("ACT 文件签名需要 P-256 私钥。");
            PublicKeySpkiBase64 = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        }
        finally
        {
            if (parameters.D is not null) CryptographicOperations.ZeroMemory(parameters.D);
        }
    }

    internal ActFileAttestationV1 Sign(ActFileEvidenceV1 evidence)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(evidence, ActFileReferenceV1.Wire);
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return new(KeyId, body, key.SignData(body, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    public static ActFileEvidenceV1 Verify(ActFileAttestationV1 proof, string keyId, string publicKeySpkiBase64)
    {
        if (proof is null || proof.KeyId != keyId || proof.Body is not { Length: > 0 } || proof.Signature is not { Length: 64 })
            throw ActFileReferenceV1.Denied();
        try
        {
            using var key = ECDsa.Create();
            var spki = Convert.FromBase64String(publicKeySpkiBase64);
            key.ImportSubjectPublicKeyInfo(spki, out var used);
            if (used != spki.Length || key.ExportParameters(false).Curve.Oid.Value != AlgorithmOid ||
                !key.VerifyData(proof.Body, proof.Signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw ActFileReferenceV1.Denied();
            var evidence = JsonSerializer.Deserialize<ActFileEvidenceV1>(proof.Body, ActFileReferenceV1.Wire);
            if (evidence is null || evidence.SchemaVersion != ActFileReferenceV1.Contract ||
                evidence.Authority != ActFileReferenceV1.Authority || evidence.KeyId != keyId)
                throw ActFileReferenceV1.Denied();
            return evidence;
        }
        catch (Exception error) when (error is CryptographicException or FormatException or JsonException)
        {
            throw ActFileReferenceV1.Denied();
        }
    }

    public static ActFileEvidenceV1 VerifyFor(ActFileAttestationV1 proof, string keyId, string publicKeySpkiBase64,
        ActFileProofRequestV1 expected, string clientId, ActFileGuardStampV1 held)
    {
        var evidence = Verify(proof, keyId, publicKeySpkiBase64);
        if (evidence.Guard != held) throw ActFileReferenceV1.Denied();
        RequireIdentity(evidence, expected, clientId);
        return evidence;
    }

    internal static void RequireIdentity(ActFileEvidenceV1 evidence, ActFileProofRequestV1 expected, string clientId)
    {
        if (evidence.Mode != expected.Mode || evidence.ConsumerClientId != clientId ||
            evidence.Decisions is null || evidence.Decisions.Length != expected.References.Length ||
            !evidence.Decisions.Zip(expected.References).All(pair =>
                pair.First is { Decision: "ALLOW" } decision && decision.Identity == pair.Second))
            throw ActFileReferenceV1.Denied();
    }
}
