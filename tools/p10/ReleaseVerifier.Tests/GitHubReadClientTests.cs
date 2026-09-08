using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class GitHubReadClientTests
{
    private const string Repo = "GTX537/CP6.CRM";
    private const long Run = 34134695003;
    private const string Sha = S06ReleaseIdentity.CrmSource;
    private const string Path = ".github/workflows/crm-validation.yml";
    private const string Token = "not-a-real-token";

    [Fact]
    public async Task Live_attempt_jobs_workflow_and_source_are_read_from_the_fixed_API()
    {
        var token = Environment.GetEnvironmentVariable("P10_GITHUB_READ_TOKEN")
            ?? throw new InvalidOperationException("Live GitHub read is required; this test does not skip.");
        using var client = new GitHubReadClient(token);
        var run = await client.ReadAsync(GitHubReadTarget.Run(Repo, Run, 1));
        Assert.Equal(Run, run.GetProperty("id").GetInt64());
        Assert.Equal(1, run.GetProperty("run_attempt").GetInt32());
        Assert.Equal(Sha, run.GetProperty("head_sha").GetString());
        Assert.Equal("success", run.GetProperty("conclusion").GetString());
        var jobs = await client.ReadAsync(GitHubReadTarget.Jobs(Repo, Run, 1));
        Assert.Equal(5, jobs.GetProperty("total_count").GetInt32());
        Assert.All(jobs.GetProperty("jobs").EnumerateArray(), job =>
        {
            Assert.Equal(Run, job.GetProperty("run_id").GetInt64());
            Assert.Equal(1, job.GetProperty("run_attempt").GetInt32());
            Assert.Equal(Sha, job.GetProperty("head_sha").GetString());
            Assert.Equal("success", job.GetProperty("conclusion").GetString());
        });
        var workflow = await client.ReadAsync(GitHubReadTarget.Workflow(Repo, Path, Sha));
        Assert.Equal(Path, workflow.GetProperty("path").GetString());
        Assert.Equal("924014cb1231824a9b57ab82a6f9638f76329919", workflow.GetProperty("sha").GetString());
        var bytes = Convert.FromBase64String(workflow.GetProperty("content").GetString()!);
        Assert.Equal(14913, bytes.Length);
        var gitBlob = Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0").Concat(bytes).ToArray();
        Assert.Equal(workflow.GetProperty("sha").GetString(), Convert.ToHexString(SHA1.HashData(gitBlob)).ToLowerInvariant());
        var main = await client.ReadAsync(GitHubReadTarget.MainBranch(Repo));
        var observedMain = main.GetProperty("commit").GetProperty("sha").GetString()!;
        var compare = await client.ReadAsync(GitHubReadTarget.Compare(Repo, Sha, observedMain));
        Assert.False(compare.TryGetProperty("files", out _));
        Assert.Contains(compare.GetProperty("status").GetString(), new[] { "ahead", "identical" });
        Assert.Equal(0, compare.GetProperty("behind_by").GetInt32());
        Assert.Equal(Sha, compare.GetProperty("base_commit").GetProperty("sha").GetString());
        Assert.Equal(Sha, compare.GetProperty("merge_base_commit").GetProperty("sha").GetString());
        var identical = await client.ReadAsync(GitHubReadTarget.Compare(Repo, observedMain, observedMain));
        Assert.Equal("identical", identical.GetProperty("status").GetString());
        Assert.False(identical.TryGetProperty("files", out _));
        // This test proves authenticated API read, not historical branch protection or a P10 candidate.
    }

    [Theory]
    [InlineData("GTX537/CP6")]
    [InlineData("GTX537/CP6.Platform")]
    [InlineData(Repo)]
    public void Targets_select_only_allowlisted_repository_paths(string repository)
    {
        Assert.Equal("/repos/" + repository + "/branches/main", GitHubReadTarget.MainBranch(repository).Path);
        Assert.Equal("/repos/" + repository + "/actions/runs/123/attempts/2", GitHubReadTarget.Run(repository, 123, 2).Path);
        Assert.EndsWith("/attempts/2/jobs?per_page=100&page=1", GitHubReadTarget.Jobs(repository, 123, 2).Path, StringComparison.Ordinal);
        Assert.EndsWith("/compare/" + Sha + "..." + Sha + "?per_page=1&page=2",
            GitHubReadTarget.Compare(repository, Sha, Sha).Path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("gtx537/CP6")]
    [InlineData("GTX537/CP6.Portal")]
    [InlineData("OTHER/CP6")]
    [InlineData("GTX537/CP6?token=secret")]
    [InlineData("GTX537/CP6/../CP6.CRM")]
    public void Repository_input_cannot_expand_authority(string repository) =>
        Reject(() => GitHubReadTarget.MainBranch(repository), "github-repository");

    [Theory]
    [InlineData(0L, 1L)]
    [InlineData(-1L, 1L)]
    [InlineData(1L, 0L)]
    [InlineData(1L, -1L)]
    [InlineData(1L, 2147483648L)]
    public void Run_identity_is_positive_and_attempt_is_bounded(long runId, long attempt) =>
        Reject(() => GitHubReadTarget.Run(Repo, runId, attempt), "github-run");

    [Theory]
    [InlineData("")]
    [InlineData("main")]
    [InlineData("A31ca0e323418f7e4108cc6220c0f5fa132e7fc2")]
    [InlineData("a31ca0e323418f7e4108cc6220c0f5fa132e7fc2\n")]
    [InlineData("a31ca0e323418f7e4108cc6220c0f5fa132e7fcg")]
    public void Refs_cannot_be_tags_short_SHAs_or_path_input(string sha)
    {
        Reject(() => GitHubReadTarget.Compare(Repo, sha, Sha), "github-sha");
        Reject(() => GitHubReadTarget.Compare(Repo, Sha, sha), "github-sha");
        Reject(() => GitHubReadTarget.Workflow(Repo, Path, sha), "github-sha");
    }

    [Theory]
    [InlineData("GTX537/CP6", S06ReleaseIdentity.ValidationPath)]
    [InlineData("GTX537/CP6", S06ReleaseIdentity.PublicationPath)]
    [InlineData("GTX537/CP6.Platform", ".github/workflows/p10-formal-packages.yml")]
    [InlineData(Repo, Path)]
    public void Known_workflows_are_bound_to_an_exact_commit(string repo, string path) =>
        Assert.Equal("/repos/" + repo + "/contents/" + path + "?ref=" + Sha, GitHubReadTarget.Workflow(repo, path, Sha).Path);

    [Theory]
    [InlineData("GTX537/CP6", Path)]
    [InlineData("OTHER/CP6", Path)]
    [InlineData(Repo, "../secret")]
    [InlineData(Repo, ".github/workflows/crm-validation.yml?ref=main")]
    public void Workflow_path_cannot_select_another_file(string repo, string path) =>
        Reject(() => GitHubReadTarget.Workflow(repo, path, Sha), "github-workflow");

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("line\nbreak")]
    [InlineData("é")]
    public void Credentials_are_bounded_before_transport(string token) =>
        Reject(() => new GitHubReadClient(token), "github-credential");

    [Fact]
    public void Credential_limit_and_serialization_do_not_expose_a_token()
    {
        Reject(() => new GitHubReadClient(new string('x', 4097)), "github-credential");
        using var client = new GitHubReadClient(Token);
        Assert.Equal("{}", JsonSerializer.Serialize(client));
        Assert.DoesNotContain(Token, client.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void HTTP_request_is_fixed_GET_with_only_explicit_authentication()
    {
        using var request = GitHubWirePolicy.Request(GitHubReadTarget.Run(Repo, Run, 1), Token);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.github.com/repos/" + Repo + "/actions/runs/" + Run + "/attempts/1", request.RequestUri!.AbsoluteUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", Token), request.Headers.Authorization);
        Assert.Equal("2022-11-28", Assert.Single(request.Headers.GetValues("X-GitHub-Api-Version")));
        Assert.Null(request.Content);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, request.VersionPolicy);
        Assert.True(request.Headers.CacheControl!.NoCache);
        Assert.True(request.Headers.CacheControl.NoStore);
        Assert.DoesNotContain(request.Headers, h => h.Key is "Cookie" or "Referer" or "Proxy-Authorization");
    }

    [Fact]
    public void HTTP_handler_does_not_forward_tokens_follow_redirects_or_bypass_TLS()
    {
        using var handler = GitHubWirePolicy.CreateHandler();
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
        Assert.Equal(0, handler.MaxResponseDrainSize);
        Assert.Equal(TimeSpan.Zero, handler.ResponseDrainTimeout);
    }

    [Theory]
    [InlineData(206)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task HTTP_errors_redirects_and_partial_bodies_fail_closed(int status)
    {
        using var response = Response("{}"u8.ToArray());
        response.StatusCode = (HttpStatusCode)status;
        await RejectAsync(() => GitHubWirePolicy.ReadResponseAsync(response, default), "github-http-status");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("html")]
    [InlineData("gzip")]
    [InlineData("range")]
    [InlineData("charset")]
    [InlineData("parameter")]
    [InlineData("duplicate-charset")]
    public async Task Response_requires_unencoded_JSON_with_unambiguous_UTF8(string mutation)
    {
        using var response = Response("{}"u8.ToArray());
        if (mutation == "missing") response.Content.Headers.ContentType = null;
        if (mutation == "html") response.Content.Headers.ContentType = new("text/html");
        if (mutation == "gzip") response.Content.Headers.ContentEncoding.Add("gzip");
        if (mutation == "range") response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 1, 2);
        if (mutation == "charset") response.Content.Headers.ContentType!.CharSet = "utf-16";
        if (mutation == "parameter") response.Content.Headers.ContentType!.Parameters.Add(new("extra", "1"));
        if (mutation == "duplicate-charset") response.Content.Headers.ContentType!.Parameters.Add(new("charset", "utf-8"));
        await RejectAsync(() => GitHubWirePolicy.ReadResponseAsync(response, default), "github-media-type");
    }

    [Theory]
    [InlineData(0, 0L)]
    [InlineData(2, 1L)]
    [InlineData(2, 3L)]
    [InlineData(2, 4194305L)]
    [InlineData(4194305, 4194305L)]
    public async Task Response_is_length_checked_and_bounded(int length, long declared)
    {
        using var response = Response(new byte[length]);
        response.Content.Headers.ContentLength = declared;
        await RejectAsync(() => GitHubWirePolicy.ReadResponseAsync(response, default), "github-size");
    }

    [Fact]
    public async Task Response_preserves_bytes_and_supports_bounded_unknown_length()
    {
        var bytes = "{ \"rate\": 0.5 }\n"u8.ToArray();
        using var response = Response(bytes);
        response.Content.Headers.ContentLength = null;
        Assert.Equal(bytes, await GitHubWirePolicy.ReadResponseAsync(response, default));
        Assert.Equal(0.5, GitHubApiJson.Parse(bytes).GetProperty("rate").GetDouble());
    }

    [Theory]
    [InlineData("{\"id\":1,\"id\":2}")]
    [InlineData("{\"nested\":{\"x\":1,\"x\":2}}")]
    [InlineData("{\"a\":[{\"x\":1,\"x\":2}]}")]
    [InlineData("{\"id\":1,}")]
    [InlineData("{/*comment*/\"id\":1}")]
    [InlineData("[]")]
    [InlineData("{} {}")]
    [InlineData("{")]
    public void Ambiguous_or_invalid_JSON_is_rejected(string json) =>
        Reject(() => GitHubApiJson.Parse(Encoding.UTF8.GetBytes(json)), "github-json");

    [Theory]
    [InlineData("members")]
    [InlineData("array")]
    [InlineData("string")]
    [InlineData("depth")]
    public void JSON_has_structural_resource_bounds(string mutation)
    {
        var json = mutation switch
        {
            "members" => "{" + string.Join(",", Enumerable.Range(0, 257).Select(i => "\"" + i + "\":0")) + "}",
            "array" => "{\"a\":[" + string.Join(",", Enumerable.Repeat("0", 4097)) + "]}",
            "string" => "{\"s\":\"" + new string('a', 65537) + "\"}",
            _ => string.Concat(Enumerable.Repeat("{\"a\":", 33)) + "0" + new string('}', 33)
        };
        Reject(() => GitHubApiJson.Parse(Encoding.UTF8.GetBytes(json)), "github-json");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4194305)]
    public void JSON_byte_limit_is_checked_before_parsing(int length) =>
        Reject(() => GitHubApiJson.Parse(new byte[length]), "github-json-size");

    [Fact]
    public async Task Cancellation_and_disposal_fail_before_network()
    {
        using var client = new GitHubReadClient(Token);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadAsync(GitHubReadTarget.MainBranch(Repo), cancel.Token));
        using var response = Response("{}"u8.ToArray());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GitHubWirePolicy.ReadResponseAsync(response, cancel.Token));
        client.Dispose();
        await RejectAsync(() => client.ReadAsync(GitHubReadTarget.MainBranch(Repo)), "github-client-disposed");
    }

    private static HttpResponseMessage Response(byte[] bytes)
    {
        var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        result.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");
        return result;
    }

    private static void Reject(Action action, string code)
    {
        var error = Assert.Throws<Cp6ReleaseContractException>(action);
        Assert.Equal(code, error.Code);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(Token, error.ToString(), StringComparison.Ordinal);
    }

    private static async Task RejectAsync(Func<Task> action, string code)
    {
        var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(action);
        Assert.Equal(code, error.Code);
        Assert.Null(error.InnerException);
    }
}
