using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class ReleaseArchiveSourceTests
{
    [Theory]
    [InlineData("crm-index")]
    [InlineData("crm-main-linux")]
    [InlineData("crm-main-windows")]
    [InlineData("crm-pr-linux")]
    [InlineData("crm-pr-windows")]
    [InlineData("publication")]
    [InlineData("package-provenance")]
    public async Task Actual_immutable_archive_matches_both_raw_SHA256_and_Git_blob(string name)
    {
        var token = Environment.GetEnvironmentVariable("P10_GITHUB_READ_TOKEN")
            ?? throw new InvalidOperationException("Live archive read is required; this test does not skip.");
        var before = DateTimeOffset.UtcNow;
        var result = await ReleaseArchiveSource.ReadAsync(name, token);
        var pinned = PinnedReleaseDocument.Get(name);
        Assert.Same(pinned, result.Document);
        Assert.InRange(result.RetrievedAtUtc, before, DateTimeOffset.UtcNow);
        var bytes = result.CopyBytes();
        Assert.Equal(pinned.ByteLength, bytes.Length);
        Assert.Equal(pinned.Sha256, Cp6DeterministicJson.Sha256Hex(bytes));
        bytes[0] ^= 1;
        Assert.NotEqual(bytes[0], result.CopyBytes()[0]);
        Assert.Equal(pinned.Sha256, Cp6DeterministicJson.Sha256Hex(result.CopyBytes()));
    }

    [Theory]
    [InlineData("crm-index", "consumer-0.10.1/verification-index.v1.json", 4598)]
    [InlineData("crm-main-linux", "consumer-0.10.1/main-linux.consumer-evidence.v1.json", 2174)]
    [InlineData("crm-main-windows", "consumer-0.10.1/main-windows.consumer-evidence.v1.json", 2184)]
    [InlineData("crm-pr-linux", "consumer-0.10.1/pr-linux.consumer-evidence.v1.json", 2174)]
    [InlineData("crm-pr-windows", "consumer-0.10.1/pr-windows.consumer-evidence.v1.json", 2184)]
    [InlineData("publication", "0.10.1/formal-package-publication.v1.json", 7550)]
    [InlineData("package-provenance", "0.10.1/build-invocation-provenance.v1.json", 4132)]
    public void Archive_paths_and_commit_cannot_be_selected_by_untrusted_JSON(string name, string suffix, int length)
    {
        var pinned = PinnedReleaseDocument.Get(name);
        Assert.Equal(name, pinned.Name);
        Assert.Equal("docs/delivery/p10/" + suffix, pinned.Path);
        Assert.Equal(length, pinned.ByteLength);
        var target = GitHubReadTarget.Archive(pinned);
        Assert.Equal("/repos/GTX537/CP6.CRM/contents/docs/delivery/p10/" + suffix +
            "?ref=bb1fd8b4f250fabde4476b6a450435de2d07c03f", target.Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("CRM-INDEX")]
    [InlineData("../crm-index")]
    [InlineData("crm-index?ref=main")]
    [InlineData("https://attacker.invalid")]
    public async Task Unknown_archive_names_are_rejected_before_credentials_or_network(string name)
    {
        Assert.Equal("github-archive-name", Assert.Throws<Cp6ReleaseContractException>(() => PinnedReleaseDocument.Get(name)).Code);
        Assert.Equal("github-archive-name", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            ReleaseArchiveSource.ReadAsync(name, ""))).Code);
    }

    [Fact]
    public void Exact_historical_publication_bytes_are_preserved()
    {
        var bytes = EvidenceGraphBaseline.Publication();
        var contents = Contents(bytes);
        contents["download_url"] = "https://attacker.invalid/not-followed";
        contents["content"] = "\n" + Convert.ToBase64String(bytes) + "\n";
        var result = ReleaseArchiveSource.Decode(PinnedReleaseDocument.Get("publication"), Element(contents));
        Assert.Equal(bytes, result);
        Assert.NotEqual((byte)'\n', result[^1]);
        Assert.Equal(S06ReleaseIdentity.PublicationHash, Cp6DeterministicJson.Sha256Hex(result));
    }

    [Theory]
    [InlineData("type", "github-archive-identity")]
    [InlineData("path", "github-archive-identity")]
    [InlineData("sha", "github-archive-identity")]
    [InlineData("size", "github-archive-identity")]
    [InlineData("encoding", "github-archive-identity")]
    [InlineData("invalid-base64", "github-archive-content")]
    [InlineData("short", "github-archive-size")]
    [InlineData("long", "github-archive-size")]
    [InlineData("tamper", "github-archive-hash")]
    public void Substituted_or_changed_archive_bytes_are_never_accepted(string mutation, string code)
    {
        var bytes = EvidenceGraphBaseline.Publication();
        var contents = Contents(bytes);
        if (mutation == "type") contents["type"] = "symlink";
        if (mutation == "path") contents["path"] = "docs/another-file.json";
        if (mutation == "sha") contents["sha"] = new string('a', 40);
        if (mutation == "size") contents["size"] = bytes.Length + 1;
        if (mutation == "encoding") contents["encoding"] = "none";
        if (mutation == "invalid-base64") contents["content"] = "%bad";
        if (mutation == "short") contents["content"] = Convert.ToBase64String(bytes[..^1]);
        if (mutation == "long") contents["content"] = Convert.ToBase64String(bytes.Concat(new byte[] { 10 }).ToArray());
        if (mutation == "tamper") { bytes[10] ^= 1; contents["content"] = Convert.ToBase64String(bytes); }
        var error = Assert.Throws<Cp6ReleaseContractException>(() =>
            ReleaseArchiveSource.Decode(PinnedReleaseDocument.Get("publication"), Element(contents)));
        Assert.Equal(code, error.Code);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Decoder_bounds_base64_even_when_called_without_the_HTTP_JSON_reader()
    {
        var contents = Contents(EvidenceGraphBaseline.Publication());
        contents["content"] = new string('A', 65537);
        var element = JsonSerializer.SerializeToElement(contents);
        Assert.Equal("github-archive-size", Assert.Throws<Cp6ReleaseContractException>(() =>
            ReleaseArchiveSource.Decode(PinnedReleaseDocument.Get("publication"), element)).Code);
    }

    [Fact]
    public async Task Precancelled_archive_read_does_not_use_a_name_or_token()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReleaseArchiveSource.ReadAsync("", "", cancel.Token));
    }

    private static JsonObject Contents(byte[] bytes) => new()
    {
        ["type"] = "file",
        ["path"] = "docs/delivery/p10/0.10.1/formal-package-publication.v1.json",
        ["sha"] = "2b4fbab6359df41e7e8daf19614fa3cdcabc1543",
        ["size"] = 7550,
        ["encoding"] = "base64",
        ["content"] = Convert.ToBase64String(bytes)
    };

    private static JsonElement Element(JsonNode value) => GitHubApiJson.Parse(Encoding.UTF8.GetBytes(value.ToJsonString()));
}
