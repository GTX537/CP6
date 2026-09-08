using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class FormalPackageSourceTests
{
    private const string TestToken = "not-a-real-token";
    private const string ValidIndex = """{"version":"3.0.0","resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"https://nuget.pkg.github.com/GTX537/download"}]}""";
    private const string Blob = "https://nugetregistryv2prod.blob.core.windows.net/unit-test/object?sig=not-real";

    [Theory]
    [InlineData("CP6.Platform.Abstractions")]
    [InlineData("CP6.Platform.AspNetCore")]
    [InlineData("CP6.Platform.Contracts")]
    [InlineData("CP6.Platform.Deployment")]
    [InlineData("CP6.Platform.EntityFramework")]
    [InlineData("CP6.Platform.Messaging")]
    [InlineData("CP6.Platform.Release")]
    public async Task Actual_GitHub_Packages_download_passes_independent_hash_author_and_timestamp_checks(string id)
    {
        var token = Environment.GetEnvironmentVariable("P10_FEED_READ_TOKEN")
            ?? throw new InvalidOperationException("Live authenticated formal feed read is required; this test does not skip.");
        var before = DateTimeOffset.UtcNow;
        var result = await FormalPackageSource.DownloadAndVerifyAsync(id, token);
        Assert.Equal(S06ReleaseIdentity.PackageHashes[id], result.Proof.Sha256);
        Assert.Equal(id, result.Proof.PackageId);
        Assert.Equal("0.10.1", result.Proof.Version);
        Assert.Equal(S06ReleaseIdentity.Source, result.Proof.SourceGitSha);
        Assert.Equal(FormalFeedPolicy.Index, result.FeedServiceIndex);
        Assert.InRange(result.RetrievedAtUtc, before, DateTimeOffset.UtcNow);
        var bytes = result.CopyPackageBytes();
        Assert.Equal(result.Proof.Sha256, Cp6DeterministicJson.Sha256Hex(bytes));
        bytes[0] = 0;
        Assert.Equal((byte)'P', result.CopyPackageBytes()[0]);
        Assert.False(result.Proof.PublicCaTrusted);
        Assert.True(result.Proof.InternallyTrusted);
    }

    [Theory]
    [InlineData("3.0.0")]
    [InlineData("3.0.0-beta.1")]
    public void Observed_service_index_base_is_required_before_constructing_a_package_request(string version)
    {
        FormalFeedPolicy.RequireIndex(Encoding.UTF8.GetBytes(ValidIndex.Replace(
            "\"version\":\"3.0.0\"", "\"version\":\"" + version + "\"", StringComparison.Ordinal)));
        using var request = FormalFeedPolicy.PackageRequest("CP6.Platform.Release", TestToken);
        Assert.Equal("https://nuget.pkg.github.com/GTX537/download/cp6.platform.release/0.10.1/cp6.platform.release.0.10.1.nupkg",
            request.RequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("foreign-host")]
    [InlineData("other-owner")]
    [InlineData("query")]
    [InlineData("duplicate-resource")]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-root")]
    [InlineData("version")]
    [InlineData("missing")]
    [InlineData("invalid")]
    public void Service_index_cannot_select_another_download_authority(string mutation)
    {
        var raw = mutation switch
        {
            "http" => ValidIndex.Replace("https://", "http://", StringComparison.Ordinal),
            "foreign-host" => ValidIndex.Replace("nuget.pkg.github.com", "attacker.invalid", StringComparison.Ordinal),
            "other-owner" => ValidIndex.Replace("GTX537", "AnotherOwner", StringComparison.Ordinal),
            "query" => ValidIndex.Replace("/download", "/download?token=untrusted", StringComparison.Ordinal),
            "duplicate-resource" => ValidIndex.Replace("}]}", "}," + ValidIndex[(ValidIndex.IndexOf("[", StringComparison.Ordinal) + 1)..], StringComparison.Ordinal),
            "duplicate-id" => ValidIndex.Replace("\"@id\":", "\"@id\":\"https://attacker.invalid\",\"@id\":", StringComparison.Ordinal),
            "duplicate-root" => ValidIndex.Replace("\"version\":", "\"version\":\"3.0.0\",\"version\":", StringComparison.Ordinal),
            "version" => ValidIndex.Replace("\"version\":\"3.0.0\"", "\"version\":\"2.0.0\"", StringComparison.Ordinal),
            "missing" => """{"version":"3.0.0","resources":[]}""",
            _ => "{"
        };
        Assert.Throws<Cp6ReleaseContractException>(() => FormalFeedPolicy.RequireIndex(Encoding.UTF8.GetBytes(raw)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65537)]
    public void Service_index_bytes_have_a_small_preparse_limit(int count) =>
        Assert.Equal("feed-index-size", Assert.Throws<Cp6ReleaseContractException>(() =>
            FormalFeedPolicy.RequireIndex(new byte[count])).Code);

    [Theory]
    [InlineData("")]
    [InlineData("cp6.platform.release")]
    [InlineData("../CP6.Platform.Release")]
    [InlineData("CP6.Platform.Testing")]
    public async Task Public_download_rejects_unselected_package_before_any_network_request(string id) =>
        await Reject(() => FormalPackageSource.DownloadAndVerifyAsync(id, TestToken), "feed-package-id");

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("line\nbreak")]
    [InlineData("é")]
    public async Task Credential_shape_is_checked_before_transport(string token) =>
        await Reject(() => FormalPackageSource.DownloadAndVerifyAsync("CP6.Platform.Release", token), "feed-credential");

    [Fact]
    public async Task Credential_length_is_bounded_before_transport() =>
        await Reject(() => FormalPackageSource.DownloadAndVerifyAsync("CP6.Platform.Release", new string('a', 4097)), "feed-credential");

    [Fact]
    public void Only_the_fixed_feed_requests_carry_authorization()
    {
        using var index = FormalFeedPolicy.IndexRequest(TestToken);
        using var package = FormalFeedPolicy.PackageRequest("CP6.Platform.Release", TestToken);
        using var blob = FormalFeedPolicy.BlobRequest(new Uri(Blob));
        Assert.Equal("Basic", index.Headers.Authorization!.Scheme);
        Assert.Equal("GTX537:" + TestToken, Encoding.UTF8.GetString(Convert.FromBase64String(index.Headers.Authorization.Parameter!)));
        Assert.Equal(index.Headers.Authorization, package.Headers.Authorization);
        Assert.Null(blob.Headers.Authorization);
        Assert.DoesNotContain(blob.Headers, h => h.Key is "Cookie" or "Referer" or "Proxy-Authorization");
        Assert.Equal(HttpMethod.Get, blob.Method);
        Assert.True(blob.Headers.CacheControl!.NoCache);
        Assert.True(blob.Headers.CacheControl.NoStore);
    }

    [Theory]
    [InlineData("https://attacker.invalid/file?sig=not-real")]
    [InlineData("http://nugetregistryv2prod.blob.core.windows.net/file?sig=not-real")]
    [InlineData("https://nugetregistryv2prod.blob.core.windows.net.attacker.invalid/file?sig=not-real")]
    [InlineData("https://nugetregistryv2prod.blob.core.windows.net:444/file?sig=not-real")]
    [InlineData("https://user@nugetregistryv2prod.blob.core.windows.net/file?sig=not-real")]
    [InlineData("https://nugetregistryv2prod.blob.core.windows.net/file?sig=not-real#fragment")]
    [InlineData("https://nugetregistryv2prod.blob.core.windows.net/file")]
    [InlineData("/relative?sig=not-real")]
    public void Redirect_targets_cannot_expand_the_storage_host_boundary(string location)
    {
        var failure = Assert.Throws<Cp6ReleaseContractException>(() =>
            FormalFeedPolicy.BlobRequest(new Uri(location, UriKind.RelativeOrAbsolute)));
        Assert.Equal("feed-redirect", failure.Code);
        Assert.DoesNotContain(location, failure.ToString(), StringComparison.Ordinal);
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public void Missing_or_oversized_redirects_are_rejected()
    {
        Assert.Equal("feed-redirect", Assert.Throws<Cp6ReleaseContractException>(() => FormalFeedPolicy.BlobRequest(null)).Code);
        Assert.Equal("feed-redirect", Assert.Throws<Cp6ReleaseContractException>(() =>
            FormalFeedPolicy.BlobRequest(new Uri(Blob + new string('a', 8192)))).Code);
    }

    [Fact]
    public void Production_handler_has_no_redirect_proxy_cookie_decompression_or_certificate_bypass()
    {
        using var handler = FormalFeedPolicy.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.False(handler.UseCookies);
        Assert.False(handler.PreAuthenticate);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.Null(handler.Credentials);
        Assert.Null(handler.ActivityHeadersPropagator);
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
        Assert.Equal(X509RevocationMode.Online, handler.SslOptions.CertificateRevocationCheckMode);
        Assert.Equal(TimeSpan.FromSeconds(10), handler.ConnectTimeout);
        Assert.Equal(16, handler.MaxResponseHeadersLength);
    }

    [Theory]
    [InlineData(206)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task Body_reader_rejects_partial_redirect_and_error_responses(int status)
    {
        using var response = Response("abc"u8.ToArray());
        response.StatusCode = (HttpStatusCode)status;
        await Reject(() => StrictHttpBody.ReadAsync(response, "application/octet-stream", 8, CancellationToken.None), "feed-http-status");
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("missing")]
    [InlineData("gzip")]
    public async Task Body_reader_requires_exact_media_type_and_unchanged_content_encoding(string mutation)
    {
        using var response = Response("abc"u8.ToArray());
        if (mutation == "wrong") response.Content.Headers.ContentType = new("text/html");
        if (mutation == "missing") response.Content.Headers.ContentType = null;
        if (mutation == "gzip") response.Content.Headers.ContentEncoding.Add("gzip");
        await Reject(() => StrictHttpBody.ReadAsync(response, "application/octet-stream", 8, CancellationToken.None), "feed-media-type");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 9)]
    [InlineData(9, 3)]
    [InlineData(3, 2)]
    [InlineData(3, 4)]
    [InlineData(3, 9)]
    public async Task Body_reader_rejects_empty_oversized_truncated_or_misdeclared_bytes(int count, int declared)
    {
        using var response = Response(new byte[count]);
        response.Content.Headers.ContentLength = declared;
        await Reject(() => StrictHttpBody.ReadAsync(response, "application/octet-stream", 8, CancellationToken.None), "feed-size");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8388609)]
    public async Task Body_limit_cannot_expand_the_global_binary_limit(int maximum)
    {
        using var response = Response("abc"u8.ToArray());
        await Reject(() => StrictHttpBody.ReadAsync(response, "application/octet-stream", maximum, CancellationToken.None), "feed-size");
    }

    [Fact]
    public async Task Exact_body_bytes_are_preserved()
    {
        var bytes = "raw\nbytes\0"u8.ToArray();
        using var response = Response(bytes);
        Assert.Equal(bytes, await StrictHttpBody.ReadAsync(response, "application/octet-stream", 64, CancellationToken.None));
    }

    [Fact]
    public async Task Pre_cancelled_calls_do_not_send_or_read()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FormalPackageSource.DownloadAndVerifyAsync("", "", cancel.Token));
        using var response = Response("abc"u8.ToArray());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            StrictHttpBody.ReadAsync(response, "application/octet-stream", 8, cancel.Token));
    }

    // HTTP message/byte unit tests are not remote Registry or candidate acceptance evidence.
    private static HttpResponseMessage Response(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return response;
    }

    private static async Task Reject(Func<Task> action, string expected) =>
        Assert.Equal(expected, (await Assert.ThrowsAsync<Cp6ReleaseContractException>(action)).Code);
}
