using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Native signed bytes stay native. The public wrapper never substitutes for independently authenticated OCI proof.
internal static class OciSignatureEvidence
{
    private const int ChunkBytes = 32768;
    private const int MaximumBundleBytes = 2 * 1024 * 1024;

    internal static byte[] Create(AuthenticatedOciSignature signature)
    {
        RequireProducer(signature.Workflow);
        var bundle = signature.CopyBundle();
        Require(bundle.Length is > 0 and <= MaximumBundleBytes, "s06-oci-size");
        var chunks = new List<string>();
        for (var offset = 0; offset < bundle.Length; offset += ChunkBytes)
            chunks.Add(Convert.ToBase64String(bundle.AsSpan(offset, Math.Min(ChunkBytes, bundle.Length - offset))));
        var details = JsonSerializer.SerializeToElement(new
        {
            imageRepository = signature.Repository,
            imageDigest = signature.Digest,
            signerKeyId = signature.SignerKeyId,
            policyVersion = signature.PolicyVersion,
            signedAtUtc = FormatTime(signature.SignedAtUtc),
            bundleSha256 = signature.BundleSha256,
            payloadSha256 = signature.PayloadSha256,
            bundleByteLength = bundle.Length,
            bundleChunks = chunks,
            verificationMode = "PinnedKeyOffline"
        });
        var bytes = S06InToto.Create("OciSignature", signature.Workflow, DateTimeOffset.UtcNow, details, signature.Digest);
        _ = RequireAuthenticated(bytes, signature.Workflow, DateTimeOffset.UtcNow, signature.Digest, signature);
        return bytes;
    }

    // Untrusted data only. The caller must run AuthenticatedOciSignature.AuthenticateAsync with compiled trust.
    internal static byte[] ReadBundleForAuthentication(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer,
        DateTimeOffset cutoff, string digest) => Parse(bytes, producer, cutoff, digest).Bundle.ToArray();

    internal static S06Attestation RequireAuthenticated(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer,
        DateTimeOffset cutoff, string digest, AuthenticatedOciSignature independentlyAuthenticatedSignature)
    {
        var parsed = Parse(bytes, producer, cutoff, digest);
        var signature = independentlyAuthenticatedSignature;
        var details = parsed.Statement.Details;
        Require(signature.Workflow == producer && signature.Digest == digest &&
            signature.Repository == S06ReleaseIdentity.ImageRepository &&
            signature.SignerKeyId == Text(details, "signerKeyId") &&
            signature.PolicyVersion == details.GetProperty("policyVersion").GetInt32() &&
            signature.SignedAtUtc == Time(details, "signedAtUtc") &&
            signature.BundleSha256 == Text(details, "bundleSha256") &&
            signature.PayloadSha256 == Text(details, "payloadSha256") &&
            signature.CopyBundle().AsSpan().SequenceEqual(parsed.Bundle), "s06-oci-proof");
        return parsed.Statement;
    }

    private static Parsed Parse(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer, DateTimeOffset cutoff, string digest)
    {
        var statement = S06InToto.Read(bytes, "OciSignature", producer, cutoff, digest);
        try
        {
            var details = statement.Details;
            Exact(details, "imageRepository", "imageDigest", "signerKeyId", "policyVersion", "signedAtUtc",
                "bundleSha256", "payloadSha256", "bundleByteLength", "bundleChunks", "verificationMode");
            Require(Text(details, "imageRepository") == S06ReleaseIdentity.ImageRepository &&
                Text(details, "imageDigest") == digest && Text(details, "verificationMode") == "PinnedKeyOffline",
                "s06-oci-identity");
            var length = details.GetProperty("bundleByteLength").GetInt32();
            Require(length is > 0 and <= MaximumBundleBytes, "s06-oci-size");
            var chunks = details.GetProperty("bundleChunks");
            Require(chunks.ValueKind == JsonValueKind.Array && chunks.GetArrayLength() == (length + ChunkBytes - 1) / ChunkBytes,
                "s06-oci-chunks");
            var bundle = new byte[length];
            for (var index = 0; index < chunks.GetArrayLength(); index++)
            {
                var size = Math.Min(ChunkBytes, length - index * ChunkBytes);
                var encoded = chunks[index].GetString()!;
                Require(encoded.Length == 4 * ((size + 2) / 3), "s06-oci-chunks");
                var part = Convert.FromBase64String(encoded);
                Require(part.Length == size && Convert.ToBase64String(part) == encoded, "s06-oci-chunks");
                part.CopyTo(bundle, index * ChunkBytes);
            }
            Require(Cp6DeterministicJson.Sha256Hex(bundle) == Text(details, "bundleSha256"), "s06-oci-bundle");
            var payload = OciBundlePayload.Read(bundle);
            Require(Cp6DeterministicJson.Sha256Hex(payload) == Text(details, "payloadSha256"), "s06-oci-bundle");
            var native = GitHubApiJson.Parse(payload);
            var selectors = OciSignatureProfile.Selectors(native, statement.CreatedAtUtc);
            OciSignatureProfile.RequireClaims(native, digest, producer, selectors.KeyId, selectors.Version, selectors.SignedAtUtc);
            Require(Text(details, "signerKeyId") == selectors.KeyId &&
                details.GetProperty("policyVersion").GetInt32() == selectors.Version &&
                Time(details, "signedAtUtc") == selectors.SignedAtUtc, "s06-oci-selector");
            return new(statement, bundle);
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("s06-oci-shape"); }
    }

    private sealed record Parsed(S06Attestation Statement, byte[] Bundle);
}
