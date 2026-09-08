# P10 S06 Pinned Release Archive Read Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Continue sequentially in the existing S06 worktree, without agents.

**Goal:** Retrieve the seven exact retained S04/S05 JSON documents from their immutable CRM evidence commit.

**Architecture:** A private-constructor allowlist fixes repository, commit, path, byte length, Git blob SHA-1 and raw SHA-256 before requests are constructed. A bounded GitHub Contents reader verifies the response identity and the decoded original bytes without following download_url or canonicalizing historical JSON. Returned owned bytes are evidence inputs, not candidate acceptance or proof of the CRM test conclusions.

**Tech Stack:** .NET 8 BCL, existing GitHub read client, frozen dictionaries, existing xUnit project.

## Fixed archive and scope

All files are read from GTX537/CP6.CRM at bb1fd8b4f250fabde4476b6a450435de2d07c03f. This module does not write files to CRM or change its completed CI/S05 state. The allowlist contains its retained verification index, four original consumer records, and the S04 publication/provenance records required by the candidate graph. All seven raw identities were rechecked through the authenticated Contents API before this plan.

The Contents API's Git blob identity is checked alongside independently pinned SHA-256; SHA-1 is not used as the evidence object-store hash. The original JSON bytes are retained exactly, including their original member order/timestamps. Files are at most 7,550 bytes. The decoder independently bounds base64 input to 65,536 characters; the existing HTTP/JSON layer retains its 4 MiB and structural bounds. No caller can supply an alternate repository, commit, path, object hash or download URL.

This archive read alone does not verify PR47 forward binding or its exact-main run. The subsequent CRM proof module must verify those live identities and the actual record semantics before candidate acceptance. Private test logs/TRX are not copied into public control evidence.

## Task 1: Tests and RED

**Create:** tools/p10/ReleaseVerifier.Tests/ReleaseArchiveSourceTests.cs.

- [x] Write the exact tests below; introduce only throwing scaffolds for missing APIs.
- [x] Run the focused suite with P10_GITHUB_READ_TOKEN only in process environment, then inspect every RED result. Expected: NotImplementedException, no skipped or unrelated failures.

From tools/p10 using the selected .NET 8 SDK:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ReleaseArchiveSourceTests --logger "trx;LogFileName=archive-red.trx" --results-directory ../../artifacts/p10/archive-red
```

```csharp
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
```

## Task 2: Minimal implementation and GREEN

- [x] Apply the two new production files and the complete updated target factory below only after RED inspection.
- [x] Re-run the focused suite with archive-green.trx and its own results directory. All tests, including seven actual authenticated reads, must pass; a missing credential is not a skip.

### tools/p10/ReleaseVerifier/PinnedReleaseDocument.cs

```csharp
using System.Collections.Frozen;

namespace CP6.P10.ReleaseVerifier;

// Reviewed immutable S04/S05 archives. This cannot select a caller-provided commit, file or hash.
internal sealed class PinnedReleaseDocument
{
    internal const string Repository = "GTX537/CP6.CRM";
    internal const string CommitSha = "bb1fd8b4f250fabde4476b6a450435de2d07c03f";
    private PinnedReleaseDocument(string name, string path, int length, string gitBlobSha, string sha256)
    {
        Name = name;
        Path = "docs/delivery/p10/" + path;
        ByteLength = length;
        GitBlobSha = gitBlobSha;
        Sha256 = sha256;
    }

    internal string Name { get; }
    internal string Path { get; }
    internal int ByteLength { get; }
    internal string GitBlobSha { get; }
    internal string Sha256 { get; }

    private static readonly FrozenDictionary<string, PinnedReleaseDocument> Documents = new[]
    {
        new PinnedReleaseDocument("crm-index", "consumer-0.10.1/verification-index.v1.json", 4598,
            "647cc8e47304ea7de93430b4f470ed6a089fbb2a", S06ReleaseIdentity.CrmIndexHash),
        new PinnedReleaseDocument("crm-main-linux", "consumer-0.10.1/main-linux.consumer-evidence.v1.json", 2174,
            "9d7f54ef73fe0a181813a9e3cb4dff3bbea465c7", "d4611662fb31055934fa964dc942c78bdf23e734a60d4c1bdf27484a096f3278"),
        new PinnedReleaseDocument("crm-main-windows", "consumer-0.10.1/main-windows.consumer-evidence.v1.json", 2184,
            "ef74f718b77a93d24453ff2c3cf2b2fe775438fb", "b507d9af5a0d6e3225ee3367c59cbde53b3b1d486b493002b77d57c9fe6281fa"),
        new PinnedReleaseDocument("crm-pr-linux", "consumer-0.10.1/pr-linux.consumer-evidence.v1.json", 2174,
            "f2b6c8238d9b0aed3215983e8880063ed3ac7de1", "2d7fee2faca05db1e6ed0ddf1a6f55b7d7b61ba112855f62ccbb99e06eb4e886"),
        new PinnedReleaseDocument("crm-pr-windows", "consumer-0.10.1/pr-windows.consumer-evidence.v1.json", 2184,
            "61cbfe747eb6c1cc9f04761f219daec31f61d413", "5e1e66d6c0e0c36a13741d9966dfd08d040fd7c6f01858fac45eb34e9c071278"),
        new PinnedReleaseDocument("publication", "0.10.1/formal-package-publication.v1.json", 7550,
            "2b4fbab6359df41e7e8daf19614fa3cdcabc1543", S06ReleaseIdentity.PublicationHash),
        new PinnedReleaseDocument("package-provenance", "0.10.1/build-invocation-provenance.v1.json", 4132,
            "2f0fc55997b70c6e0c64835fe42f2c3b97b97d6b", S06ReleaseIdentity.ProvenanceHash),
    }.ToFrozenDictionary(d => d.Name, StringComparer.Ordinal);

    internal static PinnedReleaseDocument Get(string name)
    {
        if (string.IsNullOrEmpty(name) || !Documents.TryGetValue(name, out var document))
            throw GitHubWirePolicy.Error("github-archive-name");
        return document;
    }
}
```

### tools/p10/ReleaseVerifier/ReleaseArchiveSource.cs

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

internal static class ReleaseArchiveSource
{
    internal static async Task<DownloadedReleaseDocument> ReadAsync(string name, string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pinned = PinnedReleaseDocument.Get(name);
        using var client = new GitHubReadClient(token);
        var contents = await client.ReadAsync(GitHubReadTarget.Archive(pinned), cancellationToken);
        var bytes = Decode(pinned, contents);
        return new(pinned, bytes, DateTimeOffset.UtcNow);
    }

    internal static byte[] Decode(PinnedReleaseDocument pinned, JsonElement contents)
    {
        Require(Text(contents, "type") == "file" && Text(contents, "path") == pinned.Path &&
            Text(contents, "sha") == pinned.GitBlobSha && Number(contents, "size") == pinned.ByteLength &&
            Text(contents, "encoding") == "base64", "github-archive-identity");
        var encoded = Text(contents, "content");
        Require(encoded.Length <= 65536, "github-archive-size");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(encoded); }
        catch (FormatException) { throw GitHubWirePolicy.Error("github-archive-content"); }
        Require(bytes.Length == pinned.ByteLength, "github-archive-size");
        Require(Cp6DeterministicJson.Sha256Hex(bytes) == pinned.Sha256, "github-archive-hash");
        var header = Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\0");
        Require(Convert.ToHexString(SHA1.HashData(header.Concat(bytes).ToArray())).ToLowerInvariant() == pinned.GitBlobSha,
            "github-archive-hash");
        // Return original bytes, never canonicalized or rewritten. Semantic acceptance is a separate layer.
        return bytes;
    }
}

internal sealed class DownloadedReleaseDocument
{
    private readonly byte[] _bytes;
    internal DownloadedReleaseDocument(PinnedReleaseDocument document, byte[] bytes, DateTimeOffset retrievedAtUtc)
    {
        Document = document;
        _bytes = bytes.ToArray();
        RetrievedAtUtc = retrievedAtUtc;
    }

    internal PinnedReleaseDocument Document { get; }
    internal DateTimeOffset RetrievedAtUtc { get; }
    internal byte[] CopyBytes() => _bytes.ToArray();
}
```

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

    internal static GitHubReadTarget Archive(PinnedReleaseDocument document) =>
        new(RepositoryPath(PinnedReleaseDocument.Repository) + "/contents/" + document.Path + "?ref=" + PinnedReleaseDocument.CommitSha);

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

## Task 3: Regression, review and commit

- [x] Run locked restore, all Release tests and format verification with the existing required cosign/formal-package/feed/GitHub inputs kept out of command arguments and files.
- [x] Verify exact plan/source parity, full scoped diff and secret/whitespace hygiene; confirm existing workflows, runtime, trust and lockfiles are unchanged.
- [ ] Stage only this plan, its test, PinnedReleaseDocument.cs, ReleaseArchiveSource.cs and the GitHubReadTarget.cs change; commit `feat(p10): read immutable formal and CRM evidence archives`.
- [ ] Continue CRM semantic and forward-binding verification. This module does not publish a candidate or complete P10.

```powershell
dotnet restore ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --configfile ../../eng/p10/NuGet.formal.config --packages ../../artifacts/p10/foundation-restore/packages --locked-mode --verbosity minimal
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
git diff --check
```

## Verification outcome (2026-09-08)

All 31 focused tests first failed with NotImplementedException, with zero unrelated failures or skipped tests. The planned code passed all 31, including the actual seven archived documents. Full Release regression passed 588/588 with zero skips, locked restore was unchanged, and format verification passed. Full scoped diff/plan parity and secret hygiene checks passed; two extra EOF blank lines identified by staged whitespace review were removed. No old archive, runtime, workflow, trust instance or dependency was changed. This is verified archive retrieval, not a candidate acceptance or P10 completion claim.
