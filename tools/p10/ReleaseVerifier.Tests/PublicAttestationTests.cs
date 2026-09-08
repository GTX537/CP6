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
