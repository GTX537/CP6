using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

internal enum R2CreateStatus { Created = 1, AlreadyExists = 2 }

// Protocol parsing is separately testable; an HTTP status is never candidate acceptance.
internal static class R2WirePolicy
{
    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        UseProxy = false,
        Credentials = null,
        PreAuthenticate = false,
        ActivityHeadersPropagator = null,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        MaxResponseHeadersLength = 16,
        MaxResponseDrainSize = 0,
        ResponseDrainTimeout = TimeSpan.Zero,
        SslOptions = new() { CertificateRevocationCheckMode = X509RevocationMode.Online }
    };

    internal static TimeSpan RequestLifetime(R2Credentials credentials, DateTimeOffset nowUtc)
    {
        if (credentials.IssuedAtUtc is not null && nowUtc < credentials.IssuedAtUtc) throw Error("r2-session-time");
        var remaining = credentials.ExpiresAtUtc - nowUtc;
        if (remaining is not null && remaining <= TimeSpan.Zero) throw Error("r2-session-time");
        return remaining is not null && remaining < TimeSpan.FromSeconds(60) ? remaining.Value : TimeSpan.FromSeconds(60);
    }

    internal static HttpRequestMessage Request(HttpMethod method, R2ObjectTarget target, R2Credentials credentials,
        ReadOnlyMemory<byte> payload, DateTimeOffset nowUtc)
    {
        if (method != HttpMethod.Get && method != HttpMethod.Head && method != HttpMethod.Put) throw Error("r2-method");
        if (method == HttpMethod.Put ? payload.Length is < 1 or > Cp6DeterministicJson.MaximumBytes : payload.Length != 0)
            throw Error("r2-request-body");
        var bytes = payload.ToArray();
        var hash = Cp6DeterministicJson.Sha256Hex(bytes);
        if (method == HttpMethod.Put && target.Reference is not null &&
            (target.Reference.Sha256 != hash || target.Reference.ByteLength != bytes.Length))
            throw Error("r2-write-binding");
        var authority = VerifierTrust.Load().RequireStorageAuthority(ContentAddress.StorageAuthority);
        var uri = new Uri(authority.Endpoint + "/" + authority.Bucket + "/" + target.Key, UriKind.Absolute);
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = uri.Host,
            ["x-amz-date"] = nowUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture),
            ["x-amz-content-sha256"] = hash
        };
        if (credentials.SessionToken is not null) headers.Add("x-amz-security-token", credentials.SessionToken);
        if (method == HttpMethod.Put)
        {
            headers.Add("if-none-match", "*");
            headers.Add("content-type", MediaTypeHeaderValue.Parse(target.MediaType).ToString());
        }
        var authorization = credentials.Sign(method.Method, uri.AbsolutePath, headers, nowUtc);
        var request = new HttpRequestMessage(method, uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        if (method == HttpMethod.Put)
        {
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(target.MediaType);
            request.Content.Headers.ContentLength = bytes.Length;
        }
        foreach (var pair in headers)
            if (pair.Key != "content-type") request.Headers.Add(pair.Key, pair.Value);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.ExpectContinue = false;
        request.Headers.TransferEncodingChunked = false;
        return request;
    }

    internal static async Task<byte[]?> ReadResponseAsync(HttpResponseMessage response, R2ObjectTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode != HttpStatusCode.OK) throw Error("r2-read-status");
        var content = response.Content;
        var length = content.Headers.ContentLength;
        if (content.Headers.ContentType?.ToString() != MediaTypeHeaderValue.Parse(target.MediaType).ToString() ||
            content.Headers.ContentEncoding.Count != 0 || content.Headers.ContentRange is not null ||
            length is null or < 1 or > Cp6DeterministicJson.MaximumBytes ||
            (target.Reference is not null && length != target.Reference.ByteLength))
            throw Error("r2-read-metadata");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        if (target.Reference is not null)
            return await BoundedObjectReader.ReadCheckedAsync(stream, target.Reference, target.MediaType, length, cancellationToken);
        var bytes = await BoundedObjectReader.ReadAsync(stream, checked((int)length.Value), cancellationToken);
        if (bytes.Length != length) throw Error("r2-read-length");
        return bytes;
    }

    internal static R2CreateStatus CreateResponse(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.OK => R2CreateStatus.Created,
        HttpStatusCode.PreconditionFailed => R2CreateStatus.AlreadyExists,
        _ => throw Error("r2-create-status")
    };

    internal static Cp6ReleaseContractException Error(string code) => new(code, "R2 transfer violates the fixed object policy.");
}
