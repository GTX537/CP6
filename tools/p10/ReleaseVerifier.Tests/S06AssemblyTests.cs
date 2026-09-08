using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Only the baseline S04/public policy bytes are real historical records.
// Other payloads and workflow IDs are deliberately unsigned structural vectors, never acceptance evidence.
public sealed class S06AssemblyTests
{
    private static GitHubWorkflowIdentity Producer => new("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
        new string('5', 40), 999991, 1, EvidenceGraphFixture.PublicSource);
    private static GitHubWorkflowIdentity Publisher => new("GTX537/CP6", S06ReleaseIdentity.PublicationPath,
        new string('4', 40), 999992, 1, EvidenceGraphFixture.PublicSource);
    private static string ImageDigest => "sha256:" + Cp6DeterministicJson.Sha256Hex("unpublished-test-image"u8);

    [Fact]
    public void Validation_artifact_roundtrip_preserves_all_raw_payloads_and_has_no_publisher()
    {
        var payloads = Payloads();
        var artifact = S06ArtifactAssembly.Create(Producer, ImageDigest, payloads);
        var reread = S06ArtifactInspection.Read(artifact.CopyFiles(), Producer, DateTimeOffset.UtcNow);
        Assert.Equal(24, reread.CopyFiles().Count);
        Assert.Equal(23, reread.CopyObjects().Count);
        Assert.Equal(11, reread.Evidence.Count);
        Assert.False(reread.Index.TryGetProperty("publisher", out _));
        Assert.False(reread.EvidenceAuthenticated);
        Assert.Equal(19, reread.Gate.GetProperty("gates").GetArrayLength());
        foreach (var pair in payloads) Assert.Equal(pair.Value.ToArray(), reread.Evidence[pair.Key].CopyPayloadBytes());
        Assert.Throws<Cp6ReleaseContractException>(() => Cp6DeterministicJson.Canonicalize(payloads["ImageScan"].Span));
    }

    [Fact]
    public void Candidate_assembly_uses_the_same_evidence_objects_and_exact_formal_package_identities()
    {
        var artifact = S06ArtifactAssembly.Create(Producer, ImageDigest, Payloads());
        var candidate = S06CandidateAssembly.Create(artifact, Publisher);
        var graph = candidate.Inspection;
        Assert.False(graph.CandidateAccepted);
        Assert.False(graph.Candidate.GetProperty("deployable").GetBoolean());
        Assert.Equal(7, graph.Candidate.GetProperty("packages").GetArrayLength());
        Assert.Equal(23, graph.ObjectCount);
        Assert.Equal(11, graph.Evidence.Count);
        Assert.False(graph.Candidate.GetProperty("publisher").TryGetProperty("conclusion", out _));
        Assert.Equal(Producer.RunId, graph.Gate.GetProperty("workflow").GetProperty("runId").GetInt64());
        Assert.Equal(Publisher.RunId, graph.Candidate.GetProperty("publisher").GetProperty("runId").GetInt64());
        Assert.Equal(graph.Sha256, EvidenceGraphInspection.Inspect(candidate.CopyBytes(), candidate.CopyObjects()).Sha256);
    }

    [Fact]
    public void Snapshot_copies_cannot_rewrite_an_assembled_graph()
    {
        var artifact = S06ArtifactAssembly.Create(Producer, ImageDigest, Payloads());
        var index = artifact.CopyIndexBytes();
        index[0] = 0;
        Assert.Equal((byte)'{', artifact.CopyIndexBytes()[0]);
        var candidate = S06CandidateAssembly.Create(artifact, Publisher);
        var bytes = candidate.CopyBytes();
        bytes[0] = 0;
        Assert.Equal((byte)'{', candidate.CopyBytes()[0]);
        Assert.False(candidate.Inspection.CandidateAccepted);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("empty")]
    [InlineData("oversize")]
    [InlineData("publication")]
    [InlineData("provenance")]
    [InlineData("nuget-trust")]
    [InlineData("trust")]
    public void Assembly_rejects_incomplete_or_unpinned_baseline_inputs(string mutation)
    {
        var payloads = Payloads();
        _ = S06ArtifactAssembly.Create(Producer, ImageDigest, payloads);
        if (mutation == "missing") payloads.Remove("ImageScan");
        if (mutation == "extra") payloads.Add("Optional", "{}"u8.ToArray());
        if (mutation == "empty") payloads["ImageScan"] = ReadOnlyMemory<byte>.Empty;
        if (mutation == "oversize") payloads["ImageScan"] = new byte[4194305];
        if (mutation == "publication") payloads["FormalPackagePublication"] = "{}"u8.ToArray();
        if (mutation == "provenance") payloads["PackageProvenance"] = "{}"u8.ToArray();
        if (mutation == "nuget-trust") payloads["NuGetTrustPolicy"] = "{}"u8.ToArray();
        if (mutation == "trust") payloads["TrustPolicy"] = "{}"u8.ToArray();
        Assert.Throws<Cp6ReleaseContractException>(() => S06ArtifactAssembly.Create(Producer, ImageDigest, payloads));
    }

    [Theory]
    [InlineData("format")]
    [InlineData("extra")]
    [InlineData("producer")]
    [InlineData("source")]
    [InlineData("image")]
    [InlineData("image-source")]
    [InlineData("missing-file")]
    [InlineData("changed-file")]
    [InlineData("unused-file")]
    [InlineData("record-policy")]
    [InlineData("record-subject")]
    [InlineData("record-time")]
    [InlineData("gate-failure")]
    [InlineData("gate-missing")]
    [InlineData("noncanonical")]
    [InlineData("early-cutoff")]
    public void Artifact_index_and_referenced_records_form_a_closed_bound_set(string mutation)
    {
        var artifact = S06ArtifactAssembly.Create(Producer, ImageDigest, Payloads());
        var files = artifact.CopyFiles().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var index = JsonNode.Parse(artifact.CopyIndexBytes())!;
        if (mutation == "format") index["format"] = "other";
        if (mutation == "extra") index["unreviewed"] = true;
        if (mutation == "producer") index["verifier"]!["runId"] = 999993;
        if (mutation == "source") index["platformSource"]!["sourceGitSha"] = new string('a', 40);
        if (mutation == "image") index["images"]![0]!["subjectName"] = "ghcr.io/gtx537/cp6-api";
        if (mutation == "image-source") index["images"]![0]!["sourceGitSha"] = new string('a', 40);
        var firstKey = files.Keys.First(k => k != "artifact-index.json");
        if (mutation == "missing-file") files.Remove(firstKey);
        if (mutation == "changed-file") files[firstKey] = "{}"u8.ToArray();
        if (mutation == "unused-file") files["objects/" + new string('a', 64)] = "{}"u8.ToArray();
        if (mutation is "record-policy" or "record-subject" or "record-time")
        {
            var reference = index["evidence"]![0]!;
            var record = JsonNode.Parse(files["objects/" + reference["sha256"]!.GetValue<string>()].Span)!;
            if (mutation == "record-policy") record["accessClass"] = "OptionalRestricted";
            if (mutation == "record-subject") record["subjects"]![0]!["sourceGitSha"] = new string('a', 40);
            if (mutation == "record-time") record["createdAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UtcNow.AddDays(1));
            index["evidence"]![0] = ReplaceObject(files, reference, record);
        }
        if (mutation is "gate-failure" or "gate-missing")
        {
            var reference = index["releaseGateResult"]!;
            var gate = JsonNode.Parse(files["objects/" + reference["sha256"]!.GetValue<string>()].Span)!;
            if (mutation == "gate-failure") gate["gates"]![0]!["conclusion"] = "Failure";
            if (mutation == "gate-missing") gate["gates"]!.AsArray().RemoveAt(0);
            index["releaseGateResult"] = ReplaceObject(files, reference, gate);
        }
        files["artifact-index.json"] = mutation == "noncanonical" ?
            Encoding.UTF8.GetBytes(index.ToJsonString(new JsonSerializerOptions { WriteIndented = true })) : Canonical(index);
        Assert.Throws<Cp6ReleaseContractException>(() => S06ArtifactInspection.Read(files, Producer,
            mutation == "early-cutoff" ? DateTimeOffset.UnixEpoch : DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("same-run")]
    [InlineData("source")]
    [InlineData("path")]
    public void Candidate_publisher_is_a_separate_invocation_of_the_same_public_source(string mutation)
    {
        var artifact = S06ArtifactAssembly.Create(Producer, ImageDigest, Payloads());
        var publisher = mutation switch
        {
            "same-run" => Publisher with { RunId = Producer.RunId },
            "source" => Publisher with { CommitSha = new string('a', 40) },
            _ => Producer
        };
        Assert.Throws<Cp6ReleaseContractException>(() => S06CandidateAssembly.Create(artifact, publisher));
    }

    private static Dictionary<string, ReadOnlyMemory<byte>> Payloads()
    {
        var fixture = EvidenceGraphFixture.Build();
        return EvidenceGraphInspection.Inspect(fixture.Candidate, fixture.Objects).Evidence
            .ToDictionary(p => p.Key, p => (ReadOnlyMemory<byte>)p.Value.CopyPayloadBytes(), StringComparer.Ordinal);
    }

    private static byte[] Canonical(JsonNode node) => Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(node));

    private static JsonNode ReplaceObject(Dictionary<string, ReadOnlyMemory<byte>> files, JsonNode original, JsonNode replacement)
    {
        var reference = ContentAddress.Parse(JsonSerializer.SerializeToElement(original));
        var bytes = Canonical(replacement);
        var next = ContentAddress.Create(bytes, reference.MediaType, reference.Key.Split('/')[^1]);
        files.Remove("objects/" + reference.Sha256);
        files.Add("objects/" + next.Sha256, bytes);
        return JsonNode.Parse(next.ToJson().GetRawText())!;
    }
}
