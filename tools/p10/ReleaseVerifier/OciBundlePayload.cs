using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// The envelope and payload are third-party bytes, not CP6 canonical JSON.
// This shape check provides no authentication; only the real cosign process does.
internal static class OciBundlePayload
{
    internal const string PredicateType = "https://sigstore.dev/cosign/sign/v1";

    internal static byte[] Read(ReadOnlyMemory<byte> bundle)
    {
        try
        {
            // Reuse the bounded duplicate-rejecting JSON reader without reserializing any signed byte.
            var root = GitHubApiJson.Parse(bundle);
            if (root.GetProperty("mediaType").GetString() != "application/vnd.dev.sigstore.bundle.v0.3+json" ||
                root.TryGetProperty("messageSignature", out _)) throw new FormatException();
            var envelope = root.GetProperty("dsseEnvelope");
            var signatures = envelope.GetProperty("signatures");
            if (envelope.GetProperty("payloadType").GetString() != "application/vnd.in-toto+json" ||
                signatures.ValueKind != JsonValueKind.Array || signatures.GetArrayLength() != 1)
                throw new FormatException();
            var encoded = envelope.GetProperty("payload").GetString()!;
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length is < 1 or > 49152 || Convert.ToBase64String(bytes) != encoded)
                throw new FormatException();
            _ = GitHubApiJson.Parse(bytes);
            return bytes;
        }
        catch (Exception)
        {
            throw new Cp6ReleaseContractException("cosign-bundle", "OCI bundle bytes violate the bounded DSSE profile.");
        }
    }
}
