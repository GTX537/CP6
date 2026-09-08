# P10 S06 Immutable Workflow Artifact Reader Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement task-by-task in the current task, sequentially as already selected by the owner. Do not dispatch agents.

**Goal:** Download only precisely identified S06 validation or publication-intent artifacts and reject any archive whose actual raw bytes differ from GitHub's immutable artifact metadata.

**Architecture:** A closed selector binds public repository ID, fixed workflow path, source SHA, run, attempt and purpose to the artifact name. A fixed HTTPS reader validates API metadata, allows one explicit GitHub Actions storage hop without credentials, hashes the ZIP before opening it, and keeps bounded allowed files in memory. Completion/job timing is independently established by callers; this layer alone never confers a completed-workflow or candidate-acceptance capability.

**Tech Stack:** .NET 8.0.424, xUnit, existing strict GitHub transport and CP6.Platform.Release 0.10.1; no new dependency.

---

## Inputs, scope and reviewed contract

- Approved P10 parent design sections 12.1–12.4 require completed validation before publication starts and the current publication run's immutable job artifact for pre-commit intended Locator handoff.
- [GitHub artifact API](https://docs.github.com/en/rest/actions/artifacts?apiVersion=2022-11-28) exposes artifact ID, name, SHA-256 digest, compressed size, run/SHA/repository identities and creation/expiry. Raw ZIP download redirects once through a short-lived signed Location; mismatch is a hard error before ZIP reading.
- Read-only inspection on 2026-09-08 independently confirmed public `GTX537/CP6` repository ID 1214929352 and the API metadata field shape. A header-only download probe returned HTTPS `productionresultssa19.blob.core.windows.net/actions-results/...`. No signed query, token or private response is recorded here.
- Restrict the storage account family to `productionresultssa[0-9]{1,3}.blob.core.windows.net`, HTTPS/default port, no userinfo/fragment, `/actions-results/` path and ZIP suffix. No second redirect, proxy, cookies, inherited credentials or API Authorization on the storage hop.
- Exact artifact names: `p10-s06-validation-<public-sha>-<run>-<attempt>` and `p10-s06-intent-<public-sha>-<run>-<attempt>`. An artifact from another attempt, source, run, repository, branch or purpose is rejected.
- Names are independently selected, not trusted merely because they occur in a workflow-authored JSON document. The caller must obtain the allowed creation window from actual run/job API observations. No public clock/URL/trust/success override is introduced.
- ZIP bytes and expanded total are each at most 64 MiB, at most 32 entries, each payload at most 4 MiB. Only `artifact-index.json`, `locator.json`, `locator.sigstore.json` and `objects/<64-lowercase-hex>` are allowed. Duplicate/case-colliding names, directories, symlinks, path traversal and oversized expansion fail before payload extraction. This module performs no filesystem extraction.
- The durable candidate remains in R2; workflow artifact expiry governs publication handoff, not a perpetual dependency of already-published normal consumers.
- Unit API/ZIP vectors are not workflow or release evidence. The live negative check targets an impossible artifact ID and cannot assert successful transport or S06 completion.

## File structure

- Create `WorkflowArtifactSelection.cs`: closed selectors and selected API metadata checks.
- Create `WorkflowArtifactWire.cs`: signed-storage request and bounded binary transfer.
- Create `WorkflowArtifactArchive.cs`: raw ZIP digest, allowed entries and bounded memory payloads.
- Create `WorkflowArtifactSource.cs`: real read-only orchestration and defensive downloaded snapshot.
- Modify `GitHubReadTarget.cs`: two fixed public repository artifact API GET paths.
- Create `WorkflowArtifactTests.cs`: selector, metadata, redirect, ZIP and unavailable-live-target coverage.
- All production files live under `tools/p10/ReleaseVerifier`; tests under `tools/p10/ReleaseVerifier.Tests`.

## Task 1 — Tests first

- [x] Write the full test file below; use only throwing scaffolds with these same production signatures to make missing behavior compile.
- [x] Run the focused Release tests into a TRX. Every new case must fail for missing implementation, with zero skips and no unexpected errors.

### tools/p10/ReleaseVerifier.Tests/WorkflowArtifactTests.cs

```csharp
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Pure API/ZIP vectors, not real completed-workflow or candidate acceptance evidence.
// The live test checks only that an unavailable artifact ID cannot become successful evidence.
public sealed class WorkflowArtifactTests
{
    private static GitHubWorkflowIdentity Workflow(bool intent = false) => new("GTX537/CP6",
        intent ? S06ReleaseIdentity.PublicationPath : S06ReleaseIdentity.ValidationPath,
        new string('b', 40), 12345, 2, new string('c', 40));
    private static WorkflowArtifactSelection Selection => WorkflowArtifactSelection.Validation(Workflow(), 123);
    private static readonly DateTimeOffset Latest = DateTimeOffset.UtcNow.AddMinutes(-1);
    private static readonly DateTimeOffset Earliest = Latest.AddHours(-1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Selectors_bind_ID_purpose_source_run_and_attempt_without_a_caller_URL(bool intent)
    {
        var selected = intent ? WorkflowArtifactSelection.PublicationIntent(Workflow(true), 123) : Selection;
        Assert.Equal("p10-s06-" + (intent ? "intent" : "validation") + "-" + new string('c', 40) + "-12345-2", selected.Name);
        Assert.Equal(123, selected.ArtifactId);
        Assert.Equal("/repos/GTX537/CP6/actions/artifacts/123", GitHubReadTarget.Artifact(123).Path);
        Assert.Equal("/repos/GTX537/CP6/actions/artifacts/123/zip", GitHubReadTarget.ArtifactZip(123).Path);
        using var request = GitHubWirePolicy.Request(GitHubReadTarget.ArtifactZip(123), "unit-read-token");
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("api.github.com", request.RequestUri!.Host);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
    }

    [Theory]
    [InlineData("validation-path")]
    [InlineData("intent-path")]
    [InlineData("repository")]
    [InlineData("id")]
    public void Invalid_selection_cannot_open_a_network_request(string mutation)
    {
        _ = Selection;
        Assert.Throws<Cp6ReleaseContractException>(() =>
        {
            if (mutation == "validation-path") WorkflowArtifactSelection.Validation(Workflow(true), 123);
            if (mutation == "intent-path") WorkflowArtifactSelection.PublicationIntent(Workflow(), 123);
            if (mutation == "repository") WorkflowArtifactSelection.Validation(Workflow() with { Repository = "GTX537/CP6.CRM" }, 123);
            if (mutation == "id") WorkflowArtifactSelection.Validation(Workflow(), 0);
        });
    }

    [Fact]
    public void API_metadata_is_bound_before_download_and_ignores_untrusted_extra_links()
    {
        var root = MetadataNode();
        root["untrusted_next"] = "https://attacker.invalid";
        var metadata = Selection.ReadMetadata(Element(root), Earliest, Latest);
        Assert.Equal(123, metadata.Id);
        Assert.Equal(Selection.Name, metadata.Name);
        Assert.Equal("sha256:" + new string('a', 64), metadata.Digest);
        Assert.Equal(1234, metadata.ByteLength);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("old-attempt")]
    [InlineData("expired")]
    [InlineData("digest")]
    [InlineData("zero-size")]
    [InlineData("oversize")]
    [InlineData("run")]
    [InlineData("sha")]
    [InlineData("branch")]
    [InlineData("repository")]
    [InlineData("head-repository")]
    [InlineData("url")]
    [InlineData("zip-url")]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("updated-before")]
    [InlineData("expiry")]
    public void Metadata_identity_time_and_archive_hash_fail_closed(string mutation)
    {
        var selected = Selection;
        var root = MetadataNode();
        if (mutation == "id") root["id"] = 124;
        if (mutation == "name") root["name"] = "cp6-dev-runtime";
        if (mutation == "old-attempt") root["name"] = selected.Name[..^1] + "1";
        if (mutation == "expired") root["expired"] = true;
        if (mutation == "digest") root["digest"] = new string('a', 64);
        if (mutation == "zero-size") root["size_in_bytes"] = 0;
        if (mutation == "oversize") root["size_in_bytes"] = 67108865;
        if (mutation == "run") root["workflow_run"]!["id"] = 12346;
        if (mutation == "sha") root["workflow_run"]!["head_sha"] = new string('d', 40);
        if (mutation == "branch") root["workflow_run"]!["head_branch"] = "topic";
        if (mutation == "repository") root["workflow_run"]!["repository_id"] = 1;
        if (mutation == "head-repository") root["workflow_run"]!["head_repository_id"] = 1;
        if (mutation == "url") root["url"] = "https://attacker.invalid/artifact";
        if (mutation == "zip-url") root["archive_download_url"] = "https://attacker.invalid/zip";
        if (mutation == "before") root["created_at"] = Time(Earliest.AddSeconds(-1));
        if (mutation == "after") root["updated_at"] = Time(Latest.AddSeconds(1));
        if (mutation == "updated-before") root["updated_at"] = Time(Earliest);
        if (mutation == "expiry") root["expires_at"] = Time(DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.Throws<Cp6ReleaseContractException>(() => selected.ReadMetadata(Element(root), Earliest, Latest));
    }

    [Fact]
    public void Signed_storage_request_has_no_API_credentials_cookies_or_referrer()
    {
        using var request = WorkflowArtifactWire.BlobRequest(new Uri(
            "https://productionresultssa19.blob.core.windows.net/actions-results/unit/artifact.zip?sig=unit-not-a-secret"));
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Null(request.Headers.Authorization);
        Assert.Null(request.Headers.Referrer);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Null(request.Content);
    }

    [Theory]
    [InlineData("http://productionresultssa19.blob.core.windows.net/actions-results/a.zip?sig=x")]
    [InlineData("https://attacker.invalid/actions-results/a.zip?sig=x")]
    [InlineData("https://productionresultssa19.blob.core.windows.net.attacker.invalid/actions-results/a.zip?sig=x")]
    [InlineData("https://other.blob.core.windows.net/actions-results/a.zip?sig=x")]
    [InlineData("https://user@productionresultssa19.blob.core.windows.net/actions-results/a.zip?sig=x")]
    [InlineData("https://productionresultssa19.blob.core.windows.net:444/actions-results/a.zip?sig=x")]
    [InlineData("https://productionresultssa19.blob.core.windows.net/other/a.zip?sig=x")]
    [InlineData("https://productionresultssa19.blob.core.windows.net/actions-results/a.json?sig=x")]
    [InlineData("https://productionresultssa19.blob.core.windows.net/actions-results/a.zip")]
    [InlineData("https://productionresultssa19.blob.core.windows.net/actions-results/a.zip?sig=x#fragment")]
    public void Off_profile_redirects_are_rejected(string location)
    {
        Assert.Throws<Cp6ReleaseContractException>(() => WorkflowArtifactWire.BlobRequest(new Uri(location)));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("redirect")]
    [InlineData("partial")]
    [InlineData("type")]
    [InlineData("encoding")]
    [InlineData("range")]
    [InlineData("charset")]
    [InlineData("length")]
    [InlineData("empty")]
    public async Task Binary_download_requires_a_complete_bounded_response(string mutation)
    {
        var bytes = mutation == "empty" ? Array.Empty<byte>() : Zip(("artifact-index.json", "{}"u8.ToArray()));
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new("application/zip");
        if (mutation == "redirect") response.StatusCode = HttpStatusCode.Redirect;
        if (mutation == "partial") response.StatusCode = HttpStatusCode.PartialContent;
        if (mutation == "type") response.Content.Headers.ContentType = new("text/html");
        if (mutation == "encoding") response.Content.Headers.ContentEncoding.Add("gzip");
        if (mutation == "range") response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, bytes.Length - 1, bytes.Length);
        if (mutation == "charset") response.Content.Headers.ContentType!.CharSet = "utf-8";
        if (mutation == "length") response.Content.Headers.ContentLength = bytes.Length + 1;
        if (mutation == "valid")
            Assert.Equal(bytes, await WorkflowArtifactWire.ReadZipAsync(response, CancellationToken.None));
        else
            await Assert.ThrowsAsync<Cp6ReleaseContractException>(() => WorkflowArtifactWire.ReadZipAsync(response, CancellationToken.None));
    }

    [Fact]
    public void ZIP_digest_is_verified_before_opening_malformed_archive()
    {
        var bytes = "not a zip"u8.ToArray();
        Assert.Equal("artifact-digest", Assert.Throws<Cp6ReleaseContractException>(() =>
            WorkflowArtifactArchive.Read(bytes, Metadata(bytes) with { Digest = "sha256:" + new string('a', 64) })).Code);
    }

    [Fact]
    public void Archive_bytes_and_returned_file_copies_cannot_change_the_downloaded_snapshot()
    {
        var bytes = Zip(("artifact-index.json", "{}"u8.ToArray()), ("objects/" + new string('a', 64), "raw-report"u8.ToArray()));
        var metadata = Metadata(bytes);
        var files = WorkflowArtifactArchive.Read(bytes, metadata);
        var downloaded = new DownloadedWorkflowArtifact(metadata, files, DateTimeOffset.UtcNow);
        Array.Fill(bytes, (byte)0);
        var copy = downloaded.CopyFile("artifact-index.json");
        copy[0] = 0;
        Assert.Equal("{}"u8.ToArray(), downloaded.CopyFile("artifact-index.json"));
        Assert.Equal(2, downloaded.Names.Count);
        Assert.Throws<Cp6ReleaseContractException>(() => downloaded.CopyFile("missing"));
    }

    [Theory]
    [InlineData("../locator.json")]
    [InlineData("/locator.json")]
    [InlineData("objects/../locator.json")]
    [InlineData("objects\\locator.json")]
    [InlineData("C:locator.json")]
    [InlineData("LOCATOR.JSON")]
    [InlineData("locator.json\n")]
    [InlineData("artifact-index.json/")]
    [InlineData("objects/abc")]
    public void ZIP_paths_are_a_closed_in_memory_file_profile(string name)
    {
        var bytes = Zip((name, "{}"u8.ToArray()));
        Assert.Throws<Cp6ReleaseContractException>(() => WorkflowArtifactArchive.Read(bytes, Metadata(bytes)));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("symlink")]
    [InlineData("directory")]
    [InlineData("empty")]
    [InlineData("entry-size")]
    [InlineData("file-count")]
    [InlineData("total-size")]
    public void ZIP_entry_set_type_and_expansion_are_bounded_before_reading(string mutation)
    {
        var files = new List<(string, byte[])> { ("artifact-index.json", "{}"u8.ToArray()) };
        if (mutation == "duplicate") files.Add(files[0]);
        if (mutation == "empty") files.Clear();
        if (mutation == "entry-size") files[0] = (files[0].Item1, new byte[4194305]);
        if (mutation == "file-count")
            for (var i = 0; i < 32; i++) files.Add(("objects/" + i.ToString("x64", CultureInfo.InvariantCulture), new byte[1]));
        if (mutation == "total-size")
            for (var i = 0; i < 17; i++) files.Add(("objects/" + i.ToString("x64", CultureInfo.InvariantCulture), new byte[4194304]));
        var attributes = mutation == "symlink" ? unchecked((int)0xa0000000) : mutation == "directory" ? 0x10 : 0;
        var bytes = ZipWithAttributes(attributes, files.ToArray());
        Assert.Throws<Cp6ReleaseContractException>(() => WorkflowArtifactArchive.Read(bytes, Metadata(bytes)));
    }

    [Fact]
    public async Task Precancelled_artifact_read_does_not_access_GitHub()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WorkflowArtifactSource.ReadAsync(Selection, Earliest, Latest, "unit-read-token", cancelled.Token));
    }

    [Fact]
    public async Task Actual_GitHub_missing_artifact_is_not_successful_evidence()
    {
        var selected = WorkflowArtifactSelection.Validation(Workflow(), long.MaxValue);
        var token = Environment.GetEnvironmentVariable("P10_GITHUB_READ_TOKEN") ??
            throw new InvalidOperationException("An actual GitHub read credential is required.");
        var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            WorkflowArtifactSource.ReadAsync(selected, Earliest, Latest, token));
        Assert.Equal("github-http-status", error.Code);
        Assert.Null(error.InnerException);
        Assert.False(error.ToString().Contains(token, StringComparison.Ordinal));
    }

    private static string Time(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static JsonElement Element(JsonNode value) => JsonSerializer.SerializeToElement(value);

    private static JsonObject MetadataNode() => new()
    {
        ["id"] = 123,
        ["name"] = Selection.Name,
        ["url"] = "https://api.github.com/repos/GTX537/CP6/actions/artifacts/123",
        ["archive_download_url"] = "https://api.github.com/repos/GTX537/CP6/actions/artifacts/123/zip",
        ["digest"] = "sha256:" + new string('a', 64),
        ["size_in_bytes"] = 1234,
        ["expired"] = false,
        ["created_at"] = Time(Earliest.AddMinutes(10)),
        ["updated_at"] = Time(Earliest.AddMinutes(11)),
        ["expires_at"] = Time(DateTimeOffset.UtcNow.AddDays(1)),
        ["workflow_run"] = new JsonObject
        {
            ["id"] = 12345,
            ["head_sha"] = new string('c', 40),
            ["head_branch"] = "main",
            ["repository_id"] = 1214929352,
            ["head_repository_id"] = 1214929352
        }
    };

    private static WorkflowArtifactMetadata Metadata(byte[] bytes) =>
        new(123, "unit-archive", "sha256:" + Cp6DeterministicJson.Sha256Hex(bytes), bytes.Length, Earliest, DateTimeOffset.UtcNow.AddDays(1));

    private static byte[] Zip(params (string Name, byte[] Bytes)[] files) => ZipWithAttributes(0, files);

    private static byte[] ZipWithAttributes(int attributes, params (string Name, byte[] Bytes)[] files)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Name, CompressionLevel.Fastest);
                entry.ExternalAttributes = attributes;
                using var stream = entry.Open();
                stream.Write(file.Bytes);
            }
        return output.ToArray();
    }
}
```

## Task 2 — Minimal reader

- [x] Add the two fixed targets immediately before `RequireSha` in `GitHubReadTarget.cs`.

```csharp
    internal static GitHubReadTarget Artifact(long artifactId)
    {
        if (artifactId <= 0) throw GitHubWirePolicy.Error("artifact-selection");
        return new("/repos/GTX537/CP6/actions/artifacts/" + artifactId.ToString(CultureInfo.InvariantCulture));
    }

    internal static GitHubReadTarget ArtifactZip(long artifactId) => new(Artifact(artifactId).Path + "/zip");
```

- [x] Replace the throwing scaffolds with the exact four files below.

### tools/p10/ReleaseVerifier/WorkflowArtifactSelection.cs

```csharp
using System.Globalization;
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

// A fixed artifact selector, not a successful/completed workflow capability.
internal sealed class WorkflowArtifactSelection
{
    internal const long RepositoryId = 1214929352;
    internal const int MaximumArchiveBytes = 64 * 1024 * 1024;

    private WorkflowArtifactSelection(GitHubWorkflowIdentity workflow, long artifactId, string purpose)
    {
        workflow.RequireValid();
        Require(workflow.Repository == "GTX537/CP6" && artifactId > 0, "artifact-selection");
        Workflow = workflow;
        ArtifactId = artifactId;
        Name = "p10-s06-" + purpose + "-" + workflow.CommitSha + "-" +
            workflow.RunId.ToString(CultureInfo.InvariantCulture) + "-" + workflow.RunAttempt.ToString(CultureInfo.InvariantCulture);
    }

    internal GitHubWorkflowIdentity Workflow { get; }
    internal long ArtifactId { get; }
    internal string Name { get; }

    internal static WorkflowArtifactSelection Validation(GitHubWorkflowIdentity workflow, long artifactId)
    {
        Require(workflow.WorkflowPath == S06ReleaseIdentity.ValidationPath, "artifact-selection");
        return new(workflow, artifactId, "validation");
    }

    internal static WorkflowArtifactSelection PublicationIntent(GitHubWorkflowIdentity workflow, long artifactId)
    {
        Require(workflow.WorkflowPath == S06ReleaseIdentity.PublicationPath, "artifact-selection");
        return new(workflow, artifactId, "intent");
    }

    internal WorkflowArtifactMetadata ReadMetadata(JsonElement value, DateTimeOffset earliest, DateTimeOffset latest)
    {
        RequireCutoff(earliest);
        RequireCutoff(latest);
        Require(earliest <= latest && latest <= DateTimeOffset.UtcNow, "artifact-time");
        Require(Number(value, "id") == ArtifactId && Text(value, "name") == Name &&
            Property(value, "expired").ValueKind == JsonValueKind.False, "artifact-identity");
        var api = "https://api.github.com" + GitHubReadTarget.Artifact(ArtifactId).Path;
        Require(Text(value, "url") == api && Text(value, "archive_download_url") == api + "/zip", "artifact-identity");
        var run = Property(value, "workflow_run");
        Require(Number(run, "id") == Workflow.RunId && Text(run, "head_sha") == Workflow.CommitSha &&
            Text(run, "head_branch") == "main" && Number(run, "repository_id") == RepositoryId &&
            Number(run, "head_repository_id") == RepositoryId, "artifact-workflow");
        var digest = Text(value, "digest");
        OciWirePolicy.RequireDigest(digest);
        var length = Number(value, "size_in_bytes");
        Require(length is > 0 and <= MaximumArchiveBytes, "artifact-size");
        var created = Time(value, "created_at");
        var updated = Time(value, "updated_at");
        var expires = Time(value, "expires_at");
        Require(earliest <= created && created <= updated && updated <= latest &&
            expires > DateTimeOffset.UtcNow, "artifact-time");
        return new(ArtifactId, Name, digest, (int)length, created, expires);
    }
}

internal sealed record WorkflowArtifactMetadata(long Id, string Name, string Digest, int ByteLength,
    DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);
```

### tools/p10/ReleaseVerifier/WorkflowArtifactWire.cs

```csharp
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
```

### tools/p10/ReleaseVerifier/WorkflowArtifactArchive.cs

```csharp
using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text.RegularExpressions;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

// Hash before opening ZIP. Files remain in memory; no path extraction or executable launch.
internal static class WorkflowArtifactArchive
{
    internal static IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Read(ReadOnlyMemory<byte> bytes,
        WorkflowArtifactMetadata metadata)
    {
        Require(bytes.Length is > 0 and <= WorkflowArtifactSelection.MaximumArchiveBytes &&
            bytes.Length == metadata.ByteLength, "artifact-size");
        var owned = bytes.ToArray();
        Require("sha256:" + Cp6DeterministicJson.Sha256Hex(owned) == metadata.Digest, "artifact-digest");
        try
        {
            using var input = new MemoryStream(owned, writable: false);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read);
            Require(archive.Entries.Count is > 0 and <= 32, "artifact-entry-set");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                var fileType = (entry.ExternalAttributes >> 16) & 0xf000;
                Require(Regex.IsMatch(entry.FullName, "^(artifact-index\\.json|locator\\.json|locator\\.sigstore\\.json|objects/[0-9a-f]{64})\\z",
                    RegexOptions.CultureInvariant) && names.Add(entry.FullName) &&
                    (fileType is 0 or 0x8000) && (entry.ExternalAttributes & 0x10) == 0, "artifact-entry-name");
                Require(entry.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes, "artifact-entry-size");
                total += entry.Length;
                Require(total <= WorkflowArtifactSelection.MaximumArchiveBytes, "artifact-entry-size");
            }
            var files = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                using var stream = entry.Open();
                var payload = new byte[(int)entry.Length + 1];
                var count = stream.ReadAtLeast(payload, payload.Length, throwOnEndOfStream: false);
                Require(count == entry.Length, "artifact-entry-size");
                files.Add(entry.FullName, payload.AsMemory(0, count));
            }
            return new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(files);
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw GitHubWirePolicy.Error("artifact-zip");
        }
    }
}
```

### tools/p10/ReleaseVerifier/WorkflowArtifactSource.cs

```csharp
using System.Net;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Binds raw artifact bytes to GitHub's exact ID/name/run/SHA/digest/time window.
// Caller obtains the producer/job window from independent workflow checks. This layer never asserts workflow completion.
internal static class WorkflowArtifactSource
{
    internal static async Task<DownloadedWorkflowArtifact> ReadAsync(WorkflowArtifactSelection selection,
        DateTimeOffset earliest, DateTimeOffset latest, string token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GitHubEvidenceChecks.RequireCutoff(earliest);
        GitHubEvidenceChecks.RequireCutoff(latest);
        GitHubEvidenceChecks.Require(earliest <= latest && latest <= DateTimeOffset.UtcNow, "artifact-time");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var metadataClient = new GitHubReadClient(token);
        try
        {
            var metadata = selection.ReadMetadata(await metadataClient.ReadAsync(
                GitHubReadTarget.Artifact(selection.ArtifactId), deadline.Token), earliest, latest);
            using var client = new HttpClient(GitHubWirePolicy.CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
            using var request = GitHubWirePolicy.Request(GitHubReadTarget.ArtifactZip(selection.ArtifactId), token);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            GitHubEvidenceChecks.Require(response.StatusCode == HttpStatusCode.Redirect, "artifact-http-status");
            using var blobRequest = WorkflowArtifactWire.BlobRequest(response.Headers.Location);
            using var blobResponse = await client.SendAsync(blobRequest, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var bytes = await WorkflowArtifactWire.ReadZipAsync(blobResponse, deadline.Token);
            var files = WorkflowArtifactArchive.Read(bytes, metadata);
            return new(metadata, files, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw GitHubWirePolicy.Error("artifact-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw GitHubWirePolicy.Error("artifact-transfer");
        }
    }
}

internal sealed class DownloadedWorkflowArtifact
{
    private readonly IReadOnlyDictionary<string, ReadOnlyMemory<byte>> _files;

    internal DownloadedWorkflowArtifact(WorkflowArtifactMetadata metadata,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files, DateTimeOffset observedAtUtc)
    {
        Metadata = metadata;
        _files = files.ToDictionary(p => p.Key, p => (ReadOnlyMemory<byte>)p.Value.ToArray(), StringComparer.Ordinal);
        ObservedAtUtc = observedAtUtc;
    }

    internal WorkflowArtifactMetadata Metadata { get; }
    internal DateTimeOffset ObservedAtUtc { get; }
    internal IReadOnlyList<string> Names => Array.AsReadOnly(_files.Keys.Order(StringComparer.Ordinal).ToArray());
    internal byte[] CopyFile(string name) => _files.TryGetValue(name, out var bytes) ?
        bytes.ToArray() : throw GitHubWirePolicy.Error("artifact-entry-set");
}
```

## Task 3 — Verify and checkpoint

- [x] Run focused tests and the complete Release suite with the existing real read credentials, formal package archive root and pinned official cosign path.
- [x] Run formatting verification, exact code-plan parity and seven-file scope/hygiene review. No remote mutation, test skipping, new token request or gate relaxation.
- [ ] Save the reviewed seven-file scope as an auditable local commit.

From `tools/p10` with .NET 8.0.424 and required process-only test environment inputs:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~WorkflowArtifactTests --logger "trx;LogFileName=s06-artifact-red.trx" --results-directory ../../artifacts/p10/workflow-artifact-red
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~WorkflowArtifactTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore -c Release
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
```

From the worktree:

```powershell
git diff --check
git add -- tools/p10/ReleaseVerifier/GitHubReadTarget.cs tools/p10/ReleaseVerifier/WorkflowArtifactSelection.cs tools/p10/ReleaseVerifier/WorkflowArtifactWire.cs tools/p10/ReleaseVerifier/WorkflowArtifactArchive.cs tools/p10/ReleaseVerifier/WorkflowArtifactSource.cs tools/p10/ReleaseVerifier.Tests/WorkflowArtifactTests.cs docs/superpowers/plans/2026-09-08-p10-s06-workflow-artifacts.md
git diff --cached --check
git commit -m "feat(p10): verify immutable workflow artifact handoffs"
```

## Self-review and acceptance boundary

Tasks 1–3 cover exact artifact identity, mandatory digest, time/expiry, credential isolation and ZIP size/path safety. Producer completion, prepared-job checks, artifact payload semantics, graph assembly, actual protected builds and conditional R2 publication are not claimed by this transport component. Their remaining integration must finish before S06 can be accepted. Existing R2/GHCR, trust and production configuration remain untouched.

## Execution record — 2026-09-08

- Proper RED: 64/64 new tests failed from missing implementation, as independently counted from TRX, with zero skips or unexpected failures.
- Initial GREEN: focused 64/64 and full 1,178/1,178 passed. Formatting passed.
- Scope review identified .NET regex end-anchor semantics allowing a trailing newline in a ZIP filename. Added the exact regression first: the new newline case failed while eight other path cases passed. Changed only the ZIP entry regex to strict end-of-input, updating the plan as well.
- Final GREEN after that correction: focused 65/65 and full 1,179/1,179 passed, zero skipped; formatting passed. Exact source/plan parity and scoped hygiene checks passed.
- The live negative test performed a real GitHub read and rejected an unavailable artifact. No successful S06 artifact download, completed producer, real candidate acceptance, remote write or deployment is claimed.
