using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// The service index is authenticated data, never authority to move credentials to another host.
internal static class FormalFeedPolicy
{
    internal const string Index = "https://nuget.pkg.github.com/GTX537/index.json";
    internal const string BaseAddress = "https://nuget.pkg.github.com/GTX537/download";
    private const string BlobPrefix = "https://nugetregistryv2prod.blob.core.windows.net/";

    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        UseProxy = false,
        Credentials = null,
        PreAuthenticate = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        MaxConnectionsPerServer = 2,
        MaxResponseHeadersLength = 16,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ActivityHeadersPropagator = null,
        SslOptions = new() { CertificateRevocationCheckMode = X509RevocationMode.Online }
    };

    internal static void RequirePackageId(string id)
    {
        if (string.IsNullOrEmpty(id) || !S06ReleaseIdentity.PackageHashes.ContainsKey(id)) throw Error("feed-package-id");
    }

    internal static void RequireIndex(ReadOnlyMemory<byte> raw)
    {
        if (raw.Length is < 1 or > 65536) throw Error("feed-index-size");
        try
        {
            using var document = JsonDocument.Parse(raw, new() { MaxDepth = 16 });
            var root = document.RootElement;
            if (Single(root, "version").GetString() is not ("3.0.0" or "3.0.0-beta.1")) throw Error("feed-index");
            var resources = Single(root, "resources");
            if (resources.ValueKind != JsonValueKind.Array || resources.GetArrayLength() is < 1 or > 128)
                throw Error("feed-index");
            var matches = 0;
            foreach (var resource in resources.EnumerateArray())
            {
                var type = Single(resource, "@type");
                if (type.ValueKind != JsonValueKind.String) throw Error("feed-index");
                if (type.GetString() != "PackageBaseAddress/3.0.0") continue;
                if (Single(resource, "@id").GetString() != BaseAddress) throw Error("feed-index-authority");
                matches++;
            }
            if (matches != 1) throw Error("feed-index");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw Error("feed-index");
        }
    }

    internal static HttpRequestMessage IndexRequest(string token) => AuthenticatedRequest(new Uri(Index), token, "application/json");

    internal static HttpRequestMessage PackageRequest(string id, string token)
    {
        RequirePackageId(id);
        var lower = id.ToLowerInvariant();
        return AuthenticatedRequest(new Uri($"{BaseAddress}/{lower}/0.10.1/{lower}.0.10.1.nupkg"),
            token, "application/octet-stream");
    }

    internal static HttpRequestMessage BlobRequest(Uri? location)
    {
        if (location is null || !location.IsAbsoluteUri || location.OriginalString.Length > 8192 ||
            !location.OriginalString.StartsWith(BlobPrefix, StringComparison.Ordinal) ||
            !location.IsWellFormedOriginalString() || location.OriginalString.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\') ||
            location.Scheme != Uri.UriSchemeHttps || location.Host != "nugetregistryv2prod.blob.core.windows.net" ||
            !location.IsDefaultPort || location.UserInfo.Length != 0 || location.Fragment.Length != 0 || location.Query.Length < 2)
            throw Error("feed-redirect");
        return Request(location, "application/octet-stream");
    }

    private static HttpRequestMessage AuthenticatedRequest(Uri uri, string token, string mediaType)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 4096 || token.Any(c => c is < '!' or > '~'))
            throw Error("feed-credential");
        var request = Request(uri, mediaType);
        request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("GTX537:" + token)));
        return request;
    }

    private static HttpRequestMessage Request(Uri uri, string mediaType)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new(mediaType));
        request.Headers.UserAgent.ParseAdd("CP6-P10-Verifier/1");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return request;
    }

    private static JsonElement Single(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error("feed-index");
        var matches = value.EnumerateObject().Where(p => p.Name == name).ToArray();
        if (matches.Length != 1) throw Error("feed-index");
        return matches[0].Value;
    }

    internal static Cp6ReleaseContractException Error(string code) =>
        new(code, "Formal feed transfer violates the fixed authenticated-read policy.");
}
