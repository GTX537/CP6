# P10 S06 Public Attestation and Source Evidence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans sequentially in this task. The user selected no delegation.

**Goal:** Define the fixed public S06 in-toto envelope and produce sanitized source evidence from actual public Platform main reachability.

**Architecture:** Reuse CP6.Platform.Release deterministic JSON without duplicating its primary contracts. The envelope binds one selected evidence kind, fixed subjects, exact validation producer, policy/conclusion/non-deployability and creation time; each kind's details require a separate codec. SourceReference collects live public GitHub facts and includes only six safe selected fields.

**Tech Stack:** .NET 8.0.424, CP6.Platform.Release [0.10.1], existing fixed-host GitHub reader, canonical JSON, in-toto Statement v1 shape, xUnit.

---

## Scope and evidence boundaries

The five custom predicate identifiers use the existing reviewed CP6 schema namespace: https://schemas.cp6.dev/release/p10-s06/{kind}.v1. They are S06-specific payload profiles, not replacements or copies of Platform primary schemas. Common envelope success is not candidate acceptance and does not validate a kind's details. Full verification must invoke the appropriate detail codec, verify compiled Locator trust and every graph/workflow/package/image/publication gate.

The common source subject uses gitCommit=3ff27e26962dcfd722887afb80a4306010dd9ee1. It must not include candidate.platformSource.sha256OrDigest: that value is this SourceReference payload's eventual hash, so embedding it inside the payload would create a self-reference. The enclosing evidence record/candidate will bind the computed payload SHA-256 after serialization.

The fixed subjects are:

- SourceReference: one immutable Platform Git source;
- FormalPackageVerification: that source plus the seven exact formal package hashes;
- CrmConsumer: that source, seven packages and the pinned CRM S05 index hash;
- ImageProvenance: approved image digest and the exact Release package hash; and
- OciSignature: approved image digest.

The common producer is the protected p10-platform-validation.yml identity; the publisher is not substituted. SourceReference uses the existing actual GitHub source collector to prove that the fixed source remains reachable from protected Platform main. Its public fields contain repository/source/observed-main identities, observation time and the relation conclusion only. It copies no raw API response, token, user profile, log or private CRM content. Normal source consumption can independently recheck the same public source with ordinary GitHub read access; no new Platform credential is required.

Tests deliberately use unit producer IDs. Only the source collector performs actual public GitHub reads; those local unsigned test envelopes are not formal validation-run evidence and are never signed or uploaded. No workflow, runtime, package, trust anchor, R2 object or OCI content is changed by this module.

## File map

- Create tools/p10/ReleaseVerifier/S06InToto.cs: canonical five-kind envelope, fixed subjects and common selected-field helpers.
- Create tools/p10/ReleaseVerifier/SourceReferenceEvidence.cs: actual public source collector and six-field safe decoder.
- Create tools/p10/ReleaseVerifier.Tests/PublicAttestationTests.cs: 47 component cases, including a real public source read and unchanged-byte negative cases.
- Create this plan.

## Task 1: Observe missing-implementation failures

- [x] Add the complete test file first.

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// All producer IDs in this class are unit vectors, not real completed validation identities.
// The SourceReference factory alone performs actual public GitHub reads; no output is signed or published.
public sealed class PublicAttestationTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-09-07T10:00:00Z", CultureInfo.InvariantCulture);
    private static GitHubWorkflowIdentity Producer => new("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
        new string('b', 40), 12345, 1, new string('c', 40));
    private static JsonElement Details => JsonSerializer.SerializeToElement(new { notFormalEvidence = true });
    private static readonly Lazy<Task<byte[]>> ActualSource = new(() => SourceReferenceEvidence.CreateAsync(Producer, Token()));

    [Theory]
    [InlineData("SourceReference", 1, false)]
    [InlineData("FormalPackageVerification", 8, false)]
    [InlineData("CrmConsumer", 9, false)]
    [InlineData("ImageProvenance", 2, true)]
    [InlineData("OciSignature", 1, true)]
    public void Fixed_envelopes_are_deterministic_and_bind_only_their_selected_subjects(string kind, int count, bool image)
    {
        var bytes = S06InToto.Create(kind, Producer, Created, Details, image ? Digest : null);
        Assert.Equal(bytes, S06InToto.Create(kind, Producer, Created, Details, image ? Digest : null));
        Assert.Equal(bytes, Cp6DeterministicJson.Canonicalize(bytes));
        var statement = S06InToto.Read(bytes, kind, Producer, Created, image ? Digest : null);
        Assert.Equal(kind, statement.Kind);
        Assert.Equal(Created, statement.CreatedAtUtc);
        Assert.True(statement.Details.GetProperty("notFormalEvidence").GetBoolean());
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(bytes), statement.Sha256);
        var root = JsonNode.Parse(bytes)!;
        Assert.Equal(count, root["subject"]!.AsArray().Count);
        Assert.Equal("p10-platform-candidate", root["predicate"]!["producer"]!["environment"]!.GetValue<string>());
        var names = root["subject"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()).ToArray();
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        if (kind == "SourceReference")
        {
            Assert.Equal("git+https://github.com/GTX537/CP6.Platform", names[0]);
            Assert.Equal(S06ReleaseIdentity.Source, root["subject"]![0]!["digest"]!["gitCommit"]!.GetValue<string>());
            Assert.Null(root["subject"]![0]!["digest"]!["sha256"]); // No self-referential source-payload hash.
        }
    }

    [Theory]
    [InlineData("type")]
    [InlineData("predicate-type")]
    [InlineData("subject-name")]
    [InlineData("subject-digest")]
    [InlineData("extra-subject")]
    [InlineData("extra-root")]
    [InlineData("extra-predicate")]
    [InlineData("missing-details")]
    [InlineData("details-not-object")]
    [InlineData("failure")]
    [InlineData("deployable")]
    [InlineData("policy")]
    [InlineData("producer-run")]
    [InlineData("producer-attempt")]
    [InlineData("producer-path")]
    [InlineData("producer-environment")]
    [InlineData("producer-sha")]
    [InlineData("time-fraction")]
    [InlineData("time-after-cutoff")]
    public void Canonical_bytes_do_not_excuse_wrong_envelope_bindings(string mutation)
    {
        var root = JsonNode.Parse(S06InToto.Create("SourceReference", Producer, Created, Details))!;
        var predicate = root["predicate"]!;
        if (mutation == "type") root["_type"] = "https://in-toto.io/Statement/v0.1";
        if (mutation == "predicate-type") root["predicateType"] = "https://example.invalid/untrusted";
        if (mutation == "subject-name") root["subject"]![0]!["name"] = "git+https://github.com/Other/Repo";
        if (mutation == "subject-digest") root["subject"]![0]!["digest"]!["gitCommit"] = new string('d', 40);
        if (mutation == "extra-subject") root["subject"]!.AsArray().Add(root["subject"]![0]!.DeepClone());
        if (mutation == "extra-root") root["unreviewed"] = true;
        if (mutation == "extra-predicate") predicate["unreviewed"] = true;
        if (mutation == "missing-details") predicate.AsObject().Remove("details");
        if (mutation == "details-not-object") predicate["details"] = "not-an-object";
        if (mutation == "failure") predicate["conclusion"] = "Failure";
        if (mutation == "deployable") predicate["deployable"] = true;
        if (mutation == "policy") predicate["policyVersion"] = 2;
        if (mutation == "producer-run") predicate["producer"]!["runId"] = 12346;
        if (mutation == "producer-attempt") predicate["producer"]!["runAttempt"] = 2;
        if (mutation == "producer-path") predicate["producer"]!["workflowPath"] = S06ReleaseIdentity.PublicationPath;
        if (mutation == "producer-environment") predicate["producer"]!["environment"] = "none";
        if (mutation == "producer-sha") predicate["producer"]!["commitSha"] = new string('d', 40);
        if (mutation == "time-fraction") predicate["createdAtUtc"] = "2026-09-07T10:00:00Z";
        if (mutation == "time-after-cutoff") predicate["createdAtUtc"] = "2026-09-07T10:00:01.000Z";
        Assert.Throws<Cp6ReleaseContractException>(() => S06InToto.Read(Canonical(root), "SourceReference", Producer, Created));
    }

    [Theory]
    [InlineData("unknown-kind")]
    [InlineData("missing-image")]
    [InlineData("tag-not-digest")]
    [InlineData("irrelevant-image")]
    [InlineData("publication-producer")]
    [InlineData("wrong-producer-source")]
    [InlineData("offset-time")]
    [InlineData("before-epoch")]
    public void Invalid_expected_profile_or_producer_cannot_be_serialized(string mutation)
    {
        var kind = mutation == "unknown-kind" ? "Unknown" : mutation is "missing-image" or "tag-not-digest" ? "OciSignature" : "SourceReference";
        var digest = mutation == "tag-not-digest" ? RepositoryTag : mutation == "irrelevant-image" ? Digest : null;
        var producer = mutation switch
        {
            "publication-producer" => Producer with { WorkflowPath = S06ReleaseIdentity.PublicationPath },
            "wrong-producer-source" => Producer with { CommitSha = "invalid" },
            _ => Producer
        };
        var created = mutation == "offset-time" ? Created.ToOffset(TimeSpan.FromHours(1)) :
            mutation == "before-epoch" ? DateTimeOffset.UnixEpoch.AddSeconds(-1) : Created;
        Assert.Throws<Cp6ReleaseContractException>(() => S06InToto.Create(kind, producer, created, Details, digest));
    }

    private const string RepositoryTag = "ghcr.io/gtx537/cp6-p10-verifier:latest";

    [Theory]
    [InlineData(0)]
    [InlineData(4194305)]
    public void Envelope_read_is_bounded_before_copy_or_parse(int length) =>
        Assert.Throws<Cp6ReleaseContractException>(() => S06InToto.Read(new byte[length], "SourceReference", Producer, Created));

    [Theory]
    [InlineData("whitespace")]
    [InlineData("duplicate")]
    public void Canonical_and_duplicate_member_rules_apply_to_own_public_envelopes(string mutation)
    {
        var json = Encoding.UTF8.GetString(S06InToto.Create("SourceReference", Producer, Created, Details));
        var bytes = Encoding.UTF8.GetBytes(mutation == "whitespace" ? json + "\n" : "{\"_type\":\"duplicate\"," + json[1..]);
        Assert.Throws<Cp6ReleaseContractException>(() => S06InToto.Read(bytes, "SourceReference", Producer, Created));
    }

    [Fact]
    public async Task Actual_public_Platform_source_is_read_without_any_private_CRM_input()
    {
        var bytes = await ActualSource.Value;
        var source = SourceReferenceEvidence.Read(bytes, Producer, DateTimeOffset.UtcNow);
        Assert.Equal("GTX537/CP6.Platform", source.Repository);
        Assert.Equal(S06ReleaseIdentity.Source, source.SourceGitSha);
        Assert.True(source.MainProtectedAtObservation);
        Assert.Matches("^[0-9a-f]{40}$", source.ObservedMainSha);
        Assert.InRange(source.ObservedAtUtc, Created, DateTimeOffset.UtcNow);
        var details = JsonNode.Parse(bytes)!["predicate"]!["details"]!.AsObject();
        Assert.Equal(6, details.Count);
        Assert.False(Encoding.UTF8.GetString(bytes).Contains(Token(), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("source")]
    [InlineData("main")]
    [InlineData("unprotected")]
    [InlineData("relationship")]
    [InlineData("observation-time")]
    [InlineData("extra-field")]
    [InlineData("missing-field")]
    public async Task Safe_source_summary_cannot_change_the_compiled_source_or_its_observation_binding(string mutation)
    {
        var root = JsonNode.Parse(await ActualSource.Value)!;
        var details = root["predicate"]!["details"]!.AsObject();
        if (mutation == "repository") details["repository"] = "GTX537/CP6.CRM";
        if (mutation == "source") details["sourceGitSha"] = new string('d', 40);
        if (mutation == "main") details["observedMainSha"] = "main";
        if (mutation == "unprotected") details["mainProtectedAtObservation"] = false;
        if (mutation == "relationship") details["relationship"] = "Unknown";
        if (mutation == "observation-time") details["observedAtUtc"] = "2026-09-07T00:00:00.000Z";
        if (mutation == "extra-field") details["privateData"] = "not-allowed";
        if (mutation == "missing-field") details.Remove("observedMainSha");
        Assert.Throws<Cp6ReleaseContractException>(() => SourceReferenceEvidence.Read(Canonical(root), Producer, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Invalid_source_producer_fails_before_any_network_request() =>
        Assert.Equal("s06-attestation-producer", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            SourceReferenceEvidence.CreateAsync(Producer with { WorkflowPath = S06ReleaseIdentity.PublicationPath },
                "not-a-real-token"))).Code);

    [Fact]
    public async Task Precancelled_source_collection_performs_no_network_request()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SourceReferenceEvidence.CreateAsync(Producer, "not-a-real-token", cancellation.Token));
    }

    private static byte[] Canonical(JsonNode root) =>
        Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(root));

    private static string Token() => Environment.GetEnvironmentVariable("P10_GITHUB_READ_TOKEN") ??
        throw new InvalidOperationException("Actual public GitHub source read is required; no skip or mocked replacement.");
}
```

- [x] Add only throwing Create/Read/source entry declarations and the S06Attestation record, then run all 47 tests. The failure cause must be NotImplementedException, not skipped live inputs or compile errors.

Run from tools/p10 using the established .NET SDK 8.0.424 and P10_GITHUB_READ_TOKEN held only in the process environment:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~PublicAttestationTests --logger "trx;LogFileName=public-attestation-red.trx" --results-directory ../../artifacts/p10/public-attestation-red
```

## Task 2: Implement the fixed envelope and live source evidence

- [x] Add these complete implementations.

### tools/p10/ReleaseVerifier/S06InToto.cs

```csharp
using System.Globalization;
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Release-specific payload envelope, not a replacement for CP6.Platform.Release contracts.
// An unsigned envelope is data, never a candidate-acceptance or workflow-success capability.
internal static class S06InToto
{
    private const string Prefix = "https://schemas.cp6.dev/release/p10-s06/";

    internal static byte[] Create(string kind, GitHubWorkflowIdentity producer, DateTimeOffset createdAtUtc,
        JsonElement details, string? imageDigest = null)
    {
        RequireProducer(producer);
        RequireUtc(createdAtUtc);
        Require(details.ValueKind == JsonValueKind.Object, "s06-attestation-details");
        var document = JsonSerializer.SerializeToUtf8Bytes(new
        {
            _type = "https://in-toto.io/Statement/v1",
            subject = Subjects(kind, imageDigest),
            predicateType = Prefix + kind + ".v1",
            predicate = new
            {
                createdAtUtc = FormatTime(createdAtUtc),
                producer = Workflow(producer),
                policyVersion = 1,
                conclusion = "Success",
                deployable = false,
                details
            }
        });
        return Cp6DeterministicJson.Canonicalize(document);
    }

    internal static S06Attestation Read(ReadOnlyMemory<byte> bytes, string kind, GitHubWorkflowIdentity producer,
        DateTimeOffset cutoff, string? imageDigest = null)
    {
        RequireProducer(producer);
        RequireUtc(cutoff);
        var subjects = Subjects(kind, imageDigest);
        Require(bytes.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes, "s06-attestation-size");
        var owned = bytes.ToArray();
        Require(owned.AsSpan().SequenceEqual(Cp6DeterministicJson.Canonicalize(owned)), "non-canonical-json");
        try
        {
            using var document = JsonDocument.Parse(owned);
            var root = document.RootElement;
            Exact(root, "_type", "subject", "predicateType", "predicate");
            Require(Text(root, "_type") == "https://in-toto.io/Statement/v1" &&
                Text(root, "predicateType") == Prefix + kind + ".v1" &&
                Equal(root.GetProperty("subject"), subjects), "s06-attestation-subject");
            var predicate = root.GetProperty("predicate");
            Exact(predicate, "createdAtUtc", "producer", "policyVersion", "conclusion", "deployable", "details");
            Require(Equal(predicate.GetProperty("producer"), Workflow(producer)) &&
                predicate.GetProperty("policyVersion").GetInt64() == 1 &&
                Text(predicate, "conclusion") == "Success" &&
                predicate.GetProperty("deployable").ValueKind == JsonValueKind.False, "s06-attestation-policy");
            var created = Time(predicate, "createdAtUtc");
            Require(created <= cutoff, "s06-attestation-time");
            var details = predicate.GetProperty("details");
            Require(details.ValueKind == JsonValueKind.Object, "s06-attestation-details");
            return new(kind, created, details.Clone(), Cp6DeterministicJson.Sha256Hex(owned));
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("s06-attestation-shape"); }
    }

    internal static void RequireProducer(GitHubWorkflowIdentity workflow)
    {
        workflow.RequireValid();
        Require(workflow.Repository == "GTX537/CP6" && workflow.WorkflowPath == S06ReleaseIdentity.ValidationPath,
            "s06-attestation-producer");
    }

    internal static JsonElement Workflow(GitHubWorkflowIdentity workflow) => JsonSerializer.SerializeToElement(new
    {
        repository = workflow.Repository,
        workflowPath = workflow.WorkflowPath,
        workflowFileSha = workflow.WorkflowFileSha,
        runId = workflow.RunId,
        runAttempt = workflow.RunAttempt,
        commitSha = workflow.CommitSha,
        environment = OciSignatureProfile.EnvironmentName
    });

    internal static string FormatTime(DateTimeOffset time) =>
        time.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    internal static DateTimeOffset Time(JsonElement value, string name)
    {
        Require(DateTimeOffset.TryParseExact(Text(value, name), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var time) && time >= DateTimeOffset.UnixEpoch, "s06-attestation-time");
        return time;
    }

    internal static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;

    internal static void Exact(JsonElement value, params string[] names) =>
        Require(value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Select(p => p.Name)
            .Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            "s06-attestation-shape");

    internal static void Require(bool condition, string code)
    {
        if (!condition) throw Error(code);
    }

    internal static Cp6ReleaseContractException Error(string code) =>
        new(code, "S06 public evidence violates the fixed envelope or selected-field profile.");

    private static void RequireUtc(DateTimeOffset time) =>
        Require(time.Offset == TimeSpan.Zero && time >= DateTimeOffset.UnixEpoch, "s06-attestation-time");

    private static bool Equal(JsonElement left, JsonElement right) =>
        Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(new { value = left }))
            .AsSpan().SequenceEqual(Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(new { value = right })));

    private static JsonElement Subjects(string kind, string? imageDigest)
    {
        Require(kind is "SourceReference" or "FormalPackageVerification" or "CrmConsumer" or "ImageProvenance" or "OciSignature",
            "s06-attestation-kind");
        var subjects = new List<JsonElement>();
        if (kind is "SourceReference" or "FormalPackageVerification" or "CrmConsumer")
            subjects.Add(Subject("git+https://github.com/GTX537/CP6.Platform", "gitCommit", S06ReleaseIdentity.Source));
        if (kind is "FormalPackageVerification" or "CrmConsumer")
            subjects.AddRange(S06ReleaseIdentity.PackageHashes.Select(p => Package(p.Key, p.Value)));
        if (kind == "CrmConsumer")
            subjects.Add(Subject("GTX537/CP6.CRM/p10-s05/0.10.1", "sha256", S06ReleaseIdentity.CrmIndexHash));
        if (kind is "ImageProvenance" or "OciSignature")
        {
            OciWirePolicy.RequireDigest(imageDigest!);
            subjects.Add(Subject(S06ReleaseIdentity.ImageRepository, "sha256", imageDigest![7..]));
            if (kind == "ImageProvenance")
                subjects.Add(Package(S06ReleaseIdentity.ReleasePackage,
                    S06ReleaseIdentity.PackageHashes[S06ReleaseIdentity.ReleasePackage]));
        }
        else Require(imageDigest is null, "s06-attestation-subject");
        return JsonSerializer.SerializeToElement(subjects.OrderBy(s => Text(s, "name"), StringComparer.Ordinal));
    }

    private static JsonElement Package(string id, string hash) =>
        Subject("https://nuget.pkg.github.com/GTX537/index.json#" + id + "/" + S06ReleaseIdentity.Version, "sha256", hash);

    private static JsonElement Subject(string name, string algorithm, string hash) =>
        JsonSerializer.SerializeToElement(new { name, digest = new Dictionary<string, string> { [algorithm] = hash } });
}

internal sealed record S06Attestation(string Kind, DateTimeOffset CreatedAtUtc, JsonElement Details, string Sha256);
```

### tools/p10/ReleaseVerifier/SourceReferenceEvidence.cs

```csharp
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Public Platform source observations only; no private CRM token or raw GitHub response is embedded.
internal static class SourceReferenceEvidence
{
    internal static async Task<byte[]> CreateAsync(GitHubWorkflowIdentity producer, string githubReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireProducer(producer);
        var observed = await GitHubEvidenceSource.ReadSourceAsync("GTX537/CP6.Platform",
            S06ReleaseIdentity.Source, githubReadToken, cancellationToken);
        var details = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            repository = observed.Repository,
            sourceGitSha = observed.SourceGitSha,
            observedMainSha = observed.ObservedMainSha,
            mainProtectedAtObservation = observed.MainProtectedAtObservation,
            observedAtUtc = FormatTime(observed.ObservedAtUtc),
            relationship = "SourceReachableFromMain"
        });
        var bytes = Create("SourceReference", producer, observed.ObservedAtUtc, details);
        _ = Read(bytes, producer, DateTimeOffset.UtcNow);
        return bytes;
    }

    internal static GitHubSourceObservation Read(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer,
        DateTimeOffset cutoff)
    {
        var statement = S06InToto.Read(bytes, "SourceReference", producer, cutoff);
        try
        {
            var details = statement.Details;
            Exact(details, "repository", "sourceGitSha", "observedMainSha", "mainProtectedAtObservation", "observedAtUtc", "relationship");
            var main = Text(details, "observedMainSha");
            GitHubReadTarget.RequireSha(main);
            var observed = Time(details, "observedAtUtc");
            Require(Text(details, "repository") == "GTX537/CP6.Platform" &&
                Text(details, "sourceGitSha") == S06ReleaseIdentity.Source &&
                details.GetProperty("mainProtectedAtObservation").ValueKind == System.Text.Json.JsonValueKind.True &&
                Text(details, "relationship") == "SourceReachableFromMain" &&
                observed == statement.CreatedAtUtc, "s06-source-claims");
            return new("GTX537/CP6.Platform", S06ReleaseIdentity.Source, main, true, observed);
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("s06-source-shape"); }
    }
}
```

- [x] Run focused and full Release tests with real GitHub/feed/package/cosign inputs, then format verification.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~PublicAttestationTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
```

Expected: 47 focused and 980 total tests pass, zero skips. SourceReference reads the actual public repository through the existing fixed-host client; all unsigned producer IDs remain component vectors.

## Task 3: Review and checkpoint

- [x] Compare all three implementation/test files against the full code blocks; inspect the four-file scope and verify no sensitive, machine-specific or unrelated changes.
- [ ] Record actual results, stage precisely this scope, enforce native-command exit status and commit.

```powershell
git diff --check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git add -- docs/superpowers/plans/2026-09-08-p10-s06-public-attestation-source.md tools/p10/ReleaseVerifier/S06InToto.cs tools/p10/ReleaseVerifier/SourceReferenceEvidence.cs tools/p10/ReleaseVerifier.Tests/PublicAttestationTests.cs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git commit -m "feat(p10): bind public attestations and collect source evidence"
```

- [ ] Implement the remaining typed details and complete actual protected validation, publication, clean pre/post verification and cross-repository audit before P10 acceptance.

## Observed component verification

On 2026-09-08 the 47 new cases first failed with the deliberate throwing declarations (47 NotImplementedException failures, no unexpected failures or skips), then all 47 passed against the implementation. The initial 980-case Release run had 80 failures rooted in a 30-second GitHub archive-read timeout shared by the CRM baseline. The unchanged fixed archive subsequently returned its expected path, 4598-byte size and Git blob SHA, and all 90 CRM consumer cases passed on a targeted rerun. No timeout, transport or acceptance rule was changed.

One subsequent full-test invocation omitted the separate feed-token environment binding; its missing-input failures were not acceptance evidence and that invocation was cancelled. With both existing read-token inputs restored in the process environment, the complete Release suite passed 980/980, with zero skips, in 1 minute 11 seconds. Format verification also passed. Secrets remained in process memory and were removed from the invocation environment.

These results verify only this component. The public source read was live, but the producer IDs used by the tests were unsigned unit vectors. No S06 workflow, OCI image, signature, R2 object or Locator was created or published by this module, and P10 remains open.
