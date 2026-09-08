using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Two fixed GET targets only; no challenge-selected realm, redirects, tags or registry writes.
internal static class OciWirePolicy
{
    internal const string OciManifest = "application/vnd.oci.image.manifest.v1+json";
    internal const string DockerManifest = "application/vnd.docker.distribution.manifest.v2+json";

    internal static Cp6ReleaseContractException Error(string code) => new(code, "Pinned OCI manifest read did not complete successfully.");

    internal static void RequireDigest(string digest)
    {
        if (digest is null || digest.Length != 71 || !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
            digest[7..].Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f'))) throw Error("oci-digest");
    }

    internal static void RequireToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 4096 || token.Any(c => c is < '!' or > '~'))
            throw Error("oci-credential");
    }

    internal static HttpRequestMessage TokenRequest(string readToken)
    {
        RequireToken(readToken);
        var request = Request("https://ghcr.io/token?service=ghcr.io&scope=repository%3Agtx537%2Fcp6-p10-verifier%3Apull");
        request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("GTX537:" + readToken)));
        request.Headers.Accept.Add(new("application/json"));
        return request;
    }

    internal static HttpRequestMessage ManifestRequest(string digest, string pullToken)
    {
        RequireDigest(digest);
        RequireToken(pullToken);
        var request = Request("https://ghcr.io/v2/gtx537/cp6-p10-verifier/manifests/" + digest);
        request.Headers.Authorization = new("Bearer", pullToken);
        request.Headers.Accept.Add(new(OciManifest));
        request.Headers.Accept.Add(new(DockerManifest));
        return request;
    }

    private static HttpRequestMessage Request(string uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.UserAgent.ParseAdd("CP6-P10-ReleaseVerifier/1");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return request;
    }

    internal static async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, bool manifest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (response.StatusCode != HttpStatusCode.OK) throw Error("oci-http-status");
        var type = response.Content.Headers.ContentType;
        if (type is null || (manifest ? type.MediaType is not (OciManifest or DockerManifest) : type.MediaType != "application/json") ||
            response.Content.Headers.ContentEncoding.Count != 0 || response.Content.Headers.ContentRange is not null ||
            type.Parameters.Count > 1 || type.Parameters.Any(p => !string.Equals(p.Name, "charset", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(p.Value, "utf-8", StringComparison.OrdinalIgnoreCase)))
            throw Error("oci-media-type");
        try
        {
            return await StrictHttpBody.ReadAsync(response, type.MediaType!, manifest ? 4 * 1024 * 1024 : 65536, cancellationToken);
        }
        catch (Cp6ReleaseContractException) { throw Error("oci-body"); }
    }

    internal static string PullToken(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            var root = GitHubApiJson.Parse(bytes);
            var token = root.GetProperty("token").GetString()!;
            RequireToken(token);
            if (root.TryGetProperty("access_token", out var alternate) && alternate.GetString() != token)
                throw Error("oci-credential");
            return token;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw Error("oci-credential");
        }
    }
}
