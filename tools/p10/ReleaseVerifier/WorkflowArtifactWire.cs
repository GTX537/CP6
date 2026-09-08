using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

internal static class WorkflowArtifactWire
{
    // Read-only preflight observed this GitHub-owned Actions storage account family.
    // Never forward a GitHub token to the signed storage URL and never follow a second redirect.
    internal static HttpRequestMessage BlobRequest(Uri? location)
    {
        Require(location is { IsAbsoluteUri: true } && location.Scheme == "https" && location.IsDefaultPort &&
            location.UserInfo.Length == 0 && location.Fragment.Length == 0 && location.Query.Length is > 0 and <= 8192 &&
            Regex.IsMatch(location.DnsSafeHost, "^productionresultssa[0-9]{1,3}\\.blob\\.core\\.windows\\.net$", RegexOptions.CultureInvariant) &&
            location.AbsolutePath.StartsWith("/actions-results/", StringComparison.Ordinal) &&
            location.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal) &&
            !location.OriginalString.Contains('\\'), "artifact-redirect");
        var request = new HttpRequestMessage(HttpMethod.Get, location)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/zip"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return request;
    }

    internal static async Task<byte[]> ReadZipAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Require(response.StatusCode == HttpStatusCode.OK, "artifact-http-status");
        var headers = response.Content.Headers;
        Require(headers.ContentType?.MediaType is "application/zip" or "application/octet-stream" &&
            headers.ContentType.Parameters.Count == 0 && headers.ContentEncoding.Count == 0 &&
            headers.ContentRange is null, "artifact-media-type");
        var declared = headers.ContentLength;
        Require(declared is null or > 0 and <= WorkflowArtifactSelection.MaximumArchiveBytes, "artifact-size");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            Require(output.Length + count <= WorkflowArtifactSelection.MaximumArchiveBytes, "artifact-size");
            output.Write(buffer, 0, count);
        }
        Require(output.Length > 0 && (declared is null || output.Length == declared), "artifact-size");
        return output.ToArray();
    }
}
