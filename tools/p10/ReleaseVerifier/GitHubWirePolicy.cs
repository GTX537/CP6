using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

internal static class GitHubWirePolicy
{
    internal const int MaximumBytes = 4 * 1024 * 1024;
    internal static Cp6ReleaseContractException Error(string code) => new(code, "GitHub read evidence failed its fixed policy.");

    internal static void RequireToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 4096 || token.Any(c => c is < '!' or > '~'))
            throw Error("github-credential");
    }

    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        Credentials = null,
        PreAuthenticate = false,
        ActivityHeadersPropagator = null,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        MaxResponseHeadersLength = 16,
        MaxResponseDrainSize = 0,
        ResponseDrainTimeout = TimeSpan.Zero,
        SslOptions = new() { CertificateRevocationCheckMode = X509RevocationMode.Online }
    };

    internal static HttpRequestMessage Request(GitHubReadTarget target, string token)
    {
        RequireToken(token);
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.github.com" + target.Path))
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd("CP6-P10-ReleaseVerifier/1");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return request;
    }

    internal static async Task<byte[]> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken,
        GitHubReadTarget? target = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            // Safe diagnostic only: no response body, headers, reason, URL or credential.
            Console.Error.WriteLine("p10-github-read target=" + (target?.DiagnosticCategory ?? "unspecified") +
                " status=" + ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
            throw Error("github-http-status");
        }
        var content = response.Content;
        var type = content.Headers.ContentType;
        if (type?.MediaType != "application/json" || content.Headers.ContentEncoding.Count != 0 ||
            content.Headers.ContentRange is not null || type.Parameters.Any(p =>
                !string.Equals(p.Name, "charset", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(p.Value, "utf-8", StringComparison.OrdinalIgnoreCase)) || type.Parameters.Count > 1)
            throw Error("github-media-type");
        var declared = content.Headers.ContentLength;
        if (declared is not null && (declared < 1 || declared > MaximumBytes)) throw Error("github-size");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaximumBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        if (count == 0 || count > MaximumBytes || (declared is not null && declared != count)) throw Error("github-size");
        return buffer.AsSpan(0, count).ToArray();
    }
}
