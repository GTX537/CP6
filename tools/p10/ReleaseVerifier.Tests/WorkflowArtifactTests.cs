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
