using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Manifest hashes and descriptors only. Config/layer bytes are not downloaded or declared verified here.
internal static class OciManifestChecks
{
    internal static async Task<(byte[] Bytes, OciManifestDescription Description)> ResponseAsync(
        HttpResponseMessage response, string digest, CancellationToken cancellationToken)
    {
        OciWirePolicy.RequireDigest(digest);
        var bytes = await OciWirePolicy.ReadBodyAsync(response, true, cancellationToken);
        if (response.Headers.TryGetValues("Docker-Content-Digest", out var declared) &&
            !declared.SequenceEqual(new[] { digest }, StringComparer.Ordinal)) throw OciWirePolicy.Error("oci-digest");
        return (bytes, Read(bytes, digest, response.Content.Headers.ContentType!.MediaType!));
    }

    internal static OciManifestDescription Read(ReadOnlyMemory<byte> bytes, string digest, string mediaType)
    {
        OciWirePolicy.RequireDigest(digest);
        if (bytes.Length is < 1 or > 4 * 1024 * 1024) throw OciWirePolicy.Error("oci-body");
        if ("sha256:" + Cp6DeterministicJson.Sha256Hex(bytes.Span) != digest) throw OciWirePolicy.Error("oci-digest");
        try
        {
            if (mediaType is not (OciWirePolicy.OciManifest or OciWirePolicy.DockerManifest))
                throw OciWirePolicy.Error("oci-manifest");
            var root = GitHubApiJson.Parse(bytes);
            if (root.GetProperty("schemaVersion").GetInt32() != 2 || root.GetProperty("mediaType").GetString() != mediaType ||
                root.TryGetProperty("artifactType", out _) || root.TryGetProperty("subject", out _) ||
                root.TryGetProperty("manifests", out _)) throw OciWirePolicy.Error("oci-manifest");
            var config = Descriptor(root.GetProperty("config"));
            var oci = mediaType == OciWirePolicy.OciManifest;
            if (config.MediaType != (oci ? "application/vnd.oci.image.config.v1+json" : "application/vnd.docker.container.image.v1+json"))
                throw OciWirePolicy.Error("oci-manifest");
            var layers = root.GetProperty("layers");
            if (layers.ValueKind != JsonValueKind.Array || layers.GetArrayLength() is < 1 or > 128)
                throw OciWirePolicy.Error("oci-manifest");
            var descriptions = layers.EnumerateArray().Select(Descriptor).ToArray();
            foreach (var layer in descriptions)
                if (oci ? layer.MediaType is not ("application/vnd.oci.image.layer.v1.tar" or
                    "application/vnd.oci.image.layer.v1.tar+gzip" or "application/vnd.oci.image.layer.v1.tar+zstd") :
                    layer.MediaType != "application/vnd.docker.image.rootfs.diff.tar.gzip")
                    throw OciWirePolicy.Error("oci-manifest");
            return new(mediaType, config, Array.AsReadOnly(descriptions));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw OciWirePolicy.Error("oci-manifest");
        }
    }

    private static OciDescriptor Descriptor(JsonElement descriptor)
    {
        var result = new OciDescriptor(descriptor.GetProperty("mediaType").GetString()!,
            descriptor.GetProperty("digest").GetString()!, descriptor.GetProperty("size").GetInt64());
        OciWirePolicy.RequireDigest(result.Digest);
        if (string.IsNullOrEmpty(result.MediaType) || result.ByteLength < 1 ||
            descriptor.TryGetProperty("urls", out _) || descriptor.TryGetProperty("data", out _))
            throw OciWirePolicy.Error("oci-manifest");
        return result;
    }
}

internal sealed record OciDescriptor(string MediaType, string Digest, long ByteLength);
internal sealed record OciManifestDescription(string MediaType, OciDescriptor Config, IReadOnlyList<OciDescriptor> Layers);
