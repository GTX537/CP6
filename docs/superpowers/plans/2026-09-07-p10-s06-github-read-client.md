# P10 S06 Fixed GitHub Read Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. The user selected sequential execution in the existing task; no agents.

**Goal:** Read attempt-specific GitHub evidence through a bounded, authenticated, fixed-authority client.

**Architecture:** Internal targets construct only GET endpoints for three existing repositories and four known workflows. The transport never follows response URLs and returns bounded duplicate-free GitHub JSON, which is not yet a proof or candidate acceptance. The next S06 proof layer must check complete workflow identities, exact job sets, file blob identities, temporal order and protected-source reachability.

**Tech Stack:** .NET 8 BCL HTTP/JSON, pinned CP6.Platform.Release 0.10.1, existing xUnit project; no new dependency.

## Scope and facts

The existing S06 task worktree remains based on origin/main 6f9d09f4e3b1627a25ec7859b748eba8cd66f621. Root-workspace edits are untouched. No branch protection, Environment, secret, workflow, package, image, R2 or deployment mutation belongs to this module.

Live S05 evidence is CRM run 34134695003 / attempt 1 / source a31ca0e323418f7e4108cc6220c0f5fa132e7fc2. Its five jobs succeeded. The workflow blob is 924014cb1231824a9b57ab82a6f9638f76329919, 14,913 bytes. CRM main is currently unprotected: this module must not claim protected status or historical protection. Current Platform/public-CP6 protection is a separate observation, not inferred from a SHA.

Official sources: [attempt-specific workflow runs](https://docs.github.com/en/rest/actions/workflow-runs?apiVersion=2022-11-28#get-a-workflow-run-attempt), [attempt-specific jobs](https://docs.github.com/en/rest/actions/workflow-jobs?apiVersion=2022-11-28#list-jobs-for-a-workflow-run-attempt), [contents API](https://docs.github.com/en/rest/repos/contents?apiVersion=2022-11-28#get-repository-content), [comparison API](https://docs.github.com/en/rest/commits/commits?apiVersion=2022-11-28#compare-two-commits). API version 2022-11-28 has been exercised against the real endpoints. Actions read and Contents read are sufficient; the client has no write API. Local tests use an existing authorized token only in process environment, never command arguments/logs/files. A later protected workflow needs its own separately authorized least-privilege read identity; this plan does not copy a local administrative token into GitHub.

The response limit is 4 MiB, nesting 32, 256 object members, 4096 array entries, and 65536 UTF-8 bytes per string. Jobs are fetched once with per_page=100: the future proof validator must reject total_count greater than the returned exact set, not silently accept truncation. Unknown JSON fields/numeric forms may remain because GitHub owns this schema; duplicate names and malformed structures fail. Token strings are managed caller-owned input; disposal releases the reference but cannot promise zeroization.

Comparison requests deliberately select page 2 with per_page=1. GitHub documents that changed-file patches occur only on the first page. Live CRM page-1 output included an irrelevant 69,686-character patch, exceeding the bound. Page 2 retains status, counts and base/merge-base identities without those patches; both ahead and identical comparisons were checked live. We do not enumerate commits or infer reachability from a truncated commit array. Bounds are unchanged.

## Task 1: Tests and observed RED

**Files:** Create tools/p10/ReleaseVerifier.Tests/GitHubReadClientTests.cs.

- [x] Write the complete tests below, then introduce throwing API scaffolds only if compilation requires them.
- [x] Run the exact focused command and inspect every failure before implementation. All failures must be missing behavior (NotImplementedException), not malformed tests, missing credentials, or type binding mistakes.

PowerShell test environment: use the repository-selected .NET 8 SDK. Obtain the existing authenticated CLI token with `$env:P10_GITHUB_READ_TOKEN = gh auth token` in the same process; never echo it; remove it in finally. Run from tools/p10:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~GitHubReadClientTests --logger "trx;LogFileName=github-client-red.trx" --results-directory ../../artifacts/p10/github-client-red
```

Expected: failures from throwing scaffolds, zero skipped tests.

```csharp
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
```

## Task 2: Minimal implementation and GREEN

- [x] Apply the complete implementation below only after inspecting RED.
- [x] Re-run the focused test command with github-client-green.trx/results directory; expect every test to pass, including the real API test. No fake successful network handler is introduced.

### tools/p10/ReleaseVerifier/GitHubReadTarget.cs

```csharp
using System.Globalization;

namespace CP6.P10.ReleaseVerifier;

// No caller-selected URL, HTTP method, response link, pagination URL or repository owner.
internal sealed class GitHubReadTarget
{
    private GitHubReadTarget(string path) => Path = path;
    internal string Path { get; }

    internal static GitHubReadTarget Run(string repository, long runId, long attempt) =>
        new(RunPath(repository, runId, attempt));
    internal static GitHubReadTarget Jobs(string repository, long runId, long attempt) =>
        new(RunPath(repository, runId, attempt) + "/jobs?per_page=100&page=1");
    internal static GitHubReadTarget MainBranch(string repository) => new(RepositoryPath(repository) + "/branches/main");
    internal static GitHubReadTarget Compare(string repository, string source, string observedMain)
    {
        RequireSha(source);
        RequireSha(observedMain);
        return new(RepositoryPath(repository) + "/compare/" + source + "..." + observedMain + "?per_page=1&page=2");
    }

    internal static GitHubReadTarget Workflow(string repository, string path, string source)
    {
        RequireSha(source);
        var allowed = repository switch
        {
            "GTX537/CP6" => path is S06ReleaseIdentity.ValidationPath or S06ReleaseIdentity.PublicationPath,
            "GTX537/CP6.Platform" => path == ".github/workflows/p10-formal-packages.yml",
            "GTX537/CP6.CRM" => path == ".github/workflows/crm-validation.yml",
            _ => false
        };
        if (!allowed) throw GitHubWirePolicy.Error("github-workflow");
        return new(RepositoryPath(repository) + "/contents/" + path + "?ref=" + source);
    }

    internal static void RequireSha(string value)
    {
        if (value is null || value.Length != 40 || value.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw GitHubWirePolicy.Error("github-sha");
    }

    private static string RunPath(string repository, long runId, long attempt)
    {
        if (runId <= 0 || attempt <= 0 || attempt > int.MaxValue) throw GitHubWirePolicy.Error("github-run");
        return RepositoryPath(repository) + "/actions/runs/" + runId.ToString(CultureInfo.InvariantCulture) +
            "/attempts/" + attempt.ToString(CultureInfo.InvariantCulture);
    }

    private static string RepositoryPath(string repository)
    {
        if (repository is not ("GTX537/CP6" or "GTX537/CP6.Platform" or "GTX537/CP6.CRM"))
            throw GitHubWirePolicy.Error("github-repository");
        return "/repos/" + repository;
    }
}
```

### tools/p10/ReleaseVerifier/GitHubApiJson.cs

```csharp
using System.Text;
using System.Text.Json;

namespace CP6.P10.ReleaseVerifier;

// GitHub JSON is not CP6 canonical JSON. Preserve normal numbers and whitespace, reject ambiguity and excess.
internal static class GitHubApiJson
{
    internal static JsonElement Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 1 or > GitHubWirePolicy.MaximumBytes) throw GitHubWirePolicy.Error("github-json-size");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw GitHubWirePolicy.Error("github-json");
            Check(document.RootElement);
            return document.RootElement.Clone();
        }
        catch (JsonException) { throw GitHubWirePolicy.Error("github-json"); }
    }

    private static void Check(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || names.Count > 256 || Encoding.UTF8.GetByteCount(property.Name) > 65536)
                    throw GitHubWirePolicy.Error("github-json");
                Check(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            if (element.GetArrayLength() > 4096) throw GitHubWirePolicy.Error("github-json");
            foreach (var item in element.EnumerateArray()) Check(item);
        }
        else if (element.ValueKind == JsonValueKind.String && Encoding.UTF8.GetByteCount(element.GetString()!) > 65536)
            throw GitHubWirePolicy.Error("github-json");
    }
}
```

### tools/p10/ReleaseVerifier/GitHubWirePolicy.cs

```csharp
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

    internal static async Task<byte[]> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (response.StatusCode != HttpStatusCode.OK) throw Error("github-http-status");
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
```

### tools/p10/ReleaseVerifier/GitHubReadClient.cs

```csharp
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Internal transport only. Returned API JSON is input to proof validators, never candidate acceptance.
// No injected endpoint/handler or ambient credential; managed caller-owned token strings cannot be zeroed.
internal sealed class GitHubReadClient : IDisposable
{
    private readonly HttpClient _client;
    private string? _token;

    internal GitHubReadClient(string token)
    {
        GitHubWirePolicy.RequireToken(token);
        _token = token;
        _client = new HttpClient(GitHubWirePolicy.CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
    }

    internal async Task<JsonElement> ReadAsync(GitHubReadTarget target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_token is null) throw GitHubWirePolicy.Error("github-client-disposed");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var request = GitHubWirePolicy.Request(target, _token);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            return GitHubApiJson.Parse(await GitHubWirePolicy.ReadResponseAsync(response, deadline.Token));
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw GitHubWirePolicy.Error("github-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // No response body, URL, token, runner details or inner exceptions escape this boundary.
            throw GitHubWirePolicy.Error("github-transfer");
        }
    }

    public override string ToString() => "GitHubReadClient";
    public void Dispose()
    {
        _client.Dispose();
        _token = null;
    }
}
```

## Task 3: Regression, review and commit

- [x] Run locked restore, full Release tests and format verification, with the existing required cosign/formal-package/feed inputs and the new GitHub token in process environment. Required live tests never skip.
- [x] Check exact plan/source parity, all newly added lines, whitespace and secret hygiene. Confirm existing workflows/trust/locks/runtime are unchanged.
- [ ] Stage exactly this plan plus its four production files and test file, then commit with `feat(p10): read fixed GitHub evidence through bounded transport`.
- [ ] Continue S06 proof validation and publication integration. This module does not complete P10 or establish a Frozen candidate.

Commands, from tools/p10:

```powershell
dotnet restore ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --configfile ../../eng/p10/NuGet.formal.config --packages ../../artifacts/p10/foundation-restore/packages --locked-mode --verbosity minimal
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
git diff --check
```

## Verification outcome (2026-09-08)

Initial compilation identified the helper name Main as a possible entry point under the executable project's top-level Program; it was renamed MainBranch before the behavioral RED run. All 72 focused tests then failed with NotImplementedException, with zero unexpected failures or skips. Planned implementation passed 71 tests and exposed the real page-1 diff bound. The documented page-2 adjustment first produced three expected endpoint assertions plus the live size failure, then all 72 focused tests passed, including real ahead/identical comparisons and exact S05 attempt/jobs/workflow bytes.

Locked restore passed without lockfile changes. The complete Release suite passed 479/479 with zero skipped tests; dotnet format --verify-no-changes passed. Exact plan/source checks passed for all five code/test files. Full staged source and test review and hygiene checks found no secret values, private key, machine-specific path, runtime change or existing-gate changes. These are component results, not formal S06 candidate acceptance.
