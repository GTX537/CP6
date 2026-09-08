# P10 S06 Validation Artifact and Candidate Assembly Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans inline and sequentially in the existing task. The owner already selected this mode; no agent dispatch.

**Goal:** Assemble canonical validation records and a later Platform candidate from one closed, content-addressed evidence set without predicting publication success.

**Architecture:** Validation emits an exact canonical artifact index plus 23 referenced control/payload objects in the normal 11-kind case. Inspection reuses the package-owned contracts and S06 subject/gate profile, fixes the four historical/trust payload hashes, and rejects unreferenced or changed files. Candidate assembly adds only the real publisher identity and reuses the same objects; actual workflow/proof authentication is a separate mandatory layer and all structural results remain explicitly unauthenticated.

**Tech Stack:** .NET 8.0.424, xUnit, CP6.Platform.Release 0.10.1 and existing canonical serialization/binding adapters; no dependency changes.

## Scope and reviewed decisions

- Parent sections 12.1–12.4 separate completed validation from publication. The validation index contains no publisher identity or guessed final workflow conclusion.
- The format `CP6.P10.ValidationArtifact/v1` is a CP6-specific artifact envelope, not a copied/reimplemented Platform schema. Each evidence record, gate, candidate and publication/provenance record is checked by the installed formal Release package.
- The immutable GitHub artifact reader is responsible for raw archive ID/name/SHA/run/attempt/digest/time checks. This module receives bounded file bytes and inspects their contents. Callers must supply the artifact creation cutoff from that independently verified metadata.
- Artifact names are `artifact-index.json` and `objects/<payload-sha256>`; the index has exact fields format, createdAtUtc, verifier, platformSource, images, evidence and releaseGateResult. The index time equals its gate time; records cannot be newer. Record references retain their full R2 content address, while the artifact filename uses the same raw SHA.
- SourceReference's raw payload hash becomes platformSource's source-provenance hash only after the payload exists. There is no self-hash cycle or substitution of a 40-character Git SHA for a SHA-256 content identity.
- 11 RequiredPublic Success records bind their exact payloads and the required package/source/image/policy subjects. All 19 gate names and hashes are inherited from the existing fixed profile. Schema-shaped Success fields alone never establish real validation or candidate acceptance.
- Native SPDX/SARIF and other third-party payload bytes are not canonicalized. The four immutable baseline payloads retain their compiled hashes and real package-owned semantic checks.
- S06 candidate packages are exactly the seven immutable 0.10.1 identities. Publisher and verifier have different run IDs, same public commit, fixed P10 workflow paths and environment. No publisher conclusion is serialized.
- Tests explicitly use unsigned structural vectors except historical S04/policy bytes; they assert that inspection remains unauthenticated and CandidateAccepted=false. They are not formal image, producer, scanner or release acceptance evidence.

## File structure

- Create `tools/p10/ReleaseVerifier/S06ArtifactAssembly.cs`: records/gate/index canonical generation and shared control-document serialization.
- Create `tools/p10/ReleaseVerifier/S06ArtifactInspection.cs`: closed artifact traversal and binding checks.
- Create `tools/p10/ReleaseVerifier/InspectedValidationArtifact.cs`: defensive structural snapshot.
- Create `tools/p10/ReleaseVerifier/S06CandidateAssembly.cs`: candidate assembly, package identities and defensive output.
- Create `tools/p10/ReleaseVerifier.Tests/S06AssemblyTests.cs`: exact raw-byte roundtrip and tamper coverage.

## Task 1 — Failure-first tests

- [x] Add the complete test file below and only throwing API scaffolds matching Task 2.
- [x] Run the focused Release test with TRX. Every new case must fail from missing implementation, not compilation or unrelated test setup.

### tools/p10/ReleaseVerifier.Tests/S06AssemblyTests.cs

```csharp
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
```

## Task 2 — Minimal implementation

- [x] Replace the scaffolds with the exact four source files below.

### tools/p10/ReleaseVerifier/S06ArtifactAssembly.cs

```csharp
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Constructs control documents from supplied evidence bytes. This is structural assembly, never proof acceptance.
internal static class S06ArtifactAssembly
{
    internal const string IndexName = "artifact-index.json";
    internal const string Format = "CP6.P10.ValidationArtifact/v1";

    internal static InspectedValidationArtifact Create(GitHubWorkflowIdentity producer, string imageDigest,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> payloads)
    {
        RequireProducer(producer);
        OciWirePolicy.RequireDigest(imageDigest);
        Require(payloads.Count == S06EvidenceBindings.MediaTypes.Count &&
            payloads.Keys.Order(StringComparer.Ordinal).SequenceEqual(S06EvidenceBindings.MediaTypes.Keys.Order(StringComparer.Ordinal),
                StringComparer.Ordinal), "assembly-evidence-set");
        var owned = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var pair in payloads)
        {
            Require(pair.Value.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes, "assembly-size");
            owned.Add(pair.Key, pair.Value.ToArray());
        }
        var created = FormatTime(DateTimeOffset.UtcNow);
        var references = owned.ToDictionary(p => p.Key,
            p => ContentAddress.Create(p.Value, S06EvidenceBindings.MediaTypes[p.Key], p.Key.ToLowerInvariant() + ".json"),
            StringComparer.Ordinal);
        var source = S06EvidenceBindings.Subject("SourceProvenance", "GTX537/CP6.Platform",
            references["SourceReference"].Sha256, S06ReleaseIdentity.Source);
        var images = new[] { S06EvidenceBindings.Subject("OciImage", S06ReleaseIdentity.ImageRepository, imageDigest, producer.CommitSha) };
        var partialRoot = JsonSerializer.SerializeToElement(new { verifier = Workflow(producer), platformSource = source, images });
        var files = owned.ToDictionary(p => "objects/" + references[p.Key].Sha256,
            p => (ReadOnlyMemory<byte>)p.Value, StringComparer.Ordinal);
        var records = new List<JsonElement>();
        var recordSubjects = new List<JsonElement>();
        foreach (var kind in S06EvidenceBindings.MediaTypes.Keys.Order(StringComparer.Ordinal))
        {
            var subjects = S06EvidenceBindings.RequiredSubjects(partialRoot, kind, references[kind]);
            recordSubjects.AddRange(subjects);
            var record = Control(Cp6ReleaseContractIds.EvidenceRecord, new
            {
                createdAtUtc = created, evidenceKind = kind, producer = Workflow(producer), policyVersion = 1,
                accessClass = "RequiredPublic", @object = references[kind].ToJson(), subjects, conclusion = "Success"
            });
            var reference = ContentAddress.Create(record, Cp6ReleaseMediaTypes.EvidenceRecord, kind.ToLowerInvariant() + ".record.json");
            files.Add("objects/" + reference.Sha256, record);
            records.Add(reference.ToJson());
        }
        var gates = references.ToDictionary(p => p.Key, p => p.Value.Sha256, StringComparer.Ordinal);
        foreach (var package in S06ReleaseIdentity.PackageHashes) gates.Add("NuGetSignature/" + package.Key, package.Value);
        gates.Add("VerifierPackage", S06ReleaseIdentity.PackageHashes[S06ReleaseIdentity.ReleasePackage]);
        var gate = Control(Cp6ReleaseContractIds.ReleaseGateResult, new
        {
            createdAtUtc = created, workflow = Workflow(producer),
            inputSubjects = recordSubjects.DistinctBy(S06EvidenceBindings.ExactSubject, StringComparer.Ordinal)
                .OrderBy(S06EvidenceBindings.SubjectKey, StringComparer.Ordinal).ToArray(),
            gates = gates.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { name = p.Key, subjectHash = p.Value, conclusion = "Success" }).ToArray(),
            conclusion = "Success"
        });
        var gateReference = ContentAddress.Create(gate, Cp6ReleaseMediaTypes.ReleaseGateResult, "gate.json");
        files.Add("objects/" + gateReference.Sha256, gate);
        files.Add(IndexName, Canonical(new
        {
            format = Format, createdAtUtc = created, verifier = Workflow(producer), platformSource = source, images,
            evidence = records.ToArray(), releaseGateResult = gateReference.ToJson()
        }));
        return S06ArtifactInspection.Read(files, producer, DateTimeOffset.UtcNow);
    }

    internal static byte[] Canonical(object value) => Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(value));

    internal static byte[] Control(string schemaId, object properties)
    {
        var values = JsonSerializer.SerializeToElement(properties).EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        values.Add("$schemaId", JsonSerializer.SerializeToElement(schemaId));
        return Canonical(values);
    }
}
```

### tools/p10/ReleaseVerifier/S06ArtifactInspection.cs

```csharp
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Structural artifact contents only. The completed producer and every real evidence proof remain mandatory.
internal static class S06ArtifactInspection
{
    internal static InspectedValidationArtifact Read(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files,
        GitHubWorkflowIdentity producer, DateTimeOffset createdBeforeUtc)
    {
        RequireProducer(producer);
        GitHubEvidenceChecks.RequireCutoff(createdBeforeUtc);
        Require(createdBeforeUtc <= DateTimeOffset.UtcNow, "assembly-time");
        Require(files.Count is > 0 and <= 32 && files.Values.All(v => v.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes) &&
            files.Values.Sum(v => (long)v.Length) <= WorkflowArtifactSelection.MaximumArchiveBytes, "assembly-size");
        var owned = files.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
        Require(owned.ContainsKey(S06ArtifactAssembly.IndexName), "assembly-index");
        var indexBytes = owned[S06ArtifactAssembly.IndexName];
        Require(indexBytes.AsSpan().SequenceEqual(Cp6DeterministicJson.Canonicalize(indexBytes)), "non-canonical-json");
        try
        {
            using var document = JsonDocument.Parse(indexBytes);
            var root = document.RootElement;
            Exact(root, "format", "createdAtUtc", "verifier", "platformSource", "images", "evidence", "releaseGateResult");
            Require(Text(root, "format") == S06ArtifactAssembly.Format &&
                Equal(root.GetProperty("verifier"), Workflow(producer)), "assembly-index");
            var created = Time(root, "createdAtUtc");
            Require(created <= createdBeforeUtc, "assembly-time");
            var objects = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal) { S06ArtifactAssembly.IndexName };
            var evidence = new Dictionary<string, InspectedEvidence>(StringComparer.Ordinal);
            foreach (var reference in root.GetProperty("evidence").EnumerateArray())
            {
                var recordBytes = ReadObject(reference, Cp6ReleaseMediaTypes.EvidenceRecord, owned, objects, used);
                _ = Cp6SupportingContractValidator.ValidateEvidenceRecord(recordBytes);
                var record = GitHubApiJson.Parse(recordBytes);
                var kind = Text(record, "evidenceKind");
                Require(S06EvidenceBindings.MediaTypes.TryGetValue(kind, out var media) && !evidence.ContainsKey(kind),
                    "assembly-evidence-set");
                Require(Text(record, "accessClass") == "RequiredPublic" && Text(record, "conclusion") == "Success" &&
                    record.GetProperty("policyVersion").GetInt32() == 1 && Equal(record.GetProperty("producer"), Workflow(producer)),
                    "assembly-evidence-policy");
                Require(Time(record, "createdAtUtc") <= created, "assembly-time");
                var payload = ContentAddress.Parse(record.GetProperty("object"));
                var bytes = ReadObject(record.GetProperty("object"), media!, owned, objects, used);
                evidence.Add(kind, new(record, payload, bytes));
            }
            Require(evidence.Keys.SequenceEqual(S06EvidenceBindings.MediaTypes.Keys.Order(StringComparer.Ordinal),
                StringComparer.Ordinal), "assembly-evidence-set");
            Require(Equal(root.GetProperty("platformSource"), S06EvidenceBindings.Subject("SourceProvenance",
                "GTX537/CP6.Platform", evidence["SourceReference"].Payload.Sha256, S06ReleaseIdentity.Source)), "assembly-source");
            var images = root.GetProperty("images");
            Require(images.GetArrayLength() == 1, "assembly-image");
            var digest = Text(images[0], "sha256OrDigest");
            OciWirePolicy.RequireDigest(digest);
            Require(Equal(images[0], S06EvidenceBindings.Subject("OciImage", S06ReleaseIdentity.ImageRepository, digest, producer.CommitSha)),
                "assembly-image");
            foreach (var pair in evidence)
                S06EvidenceBindings.RequireSubjects(pair.Value.Record.GetProperty("subjects"),
                    S06EvidenceBindings.RequiredSubjects(root, pair.Key, pair.Value.Payload), "assembly-subjects");
            Require(evidence["FormalPackagePublication"].Payload.Sha256 == S06ReleaseIdentity.PublicationHash &&
                evidence["PackageProvenance"].Payload.Sha256 == S06ReleaseIdentity.ProvenanceHash &&
                evidence["NuGetTrustPolicy"].Payload.Sha256 == S06ReleaseIdentity.NuGetTrustHash &&
                evidence["TrustPolicy"].Payload.Sha256 == VerifierTrust.Load().ValidatedDocument.Sha256, "assembly-baseline");
            _ = Cp6FormalPackagePublicationValidator.ValidateFormalPackagePublication(
                evidence["FormalPackagePublication"].CopyPayloadBytes(), PinnedNuGetTrust.Load(), DateTimeOffset.UtcNow);
            _ = Cp6SupportingContractValidator.ValidateBuildInvocationProvenance(evidence["PackageProvenance"].CopyPayloadBytes());
            var gateBytes = ReadObject(root.GetProperty("releaseGateResult"), Cp6ReleaseMediaTypes.ReleaseGateResult, owned, objects, used);
            _ = Cp6SupportingContractValidator.ValidateReleaseGateResult(gateBytes);
            var gate = GitHubApiJson.Parse(gateBytes);
            Require(Time(gate, "createdAtUtc") == created, "assembly-time");
            S06EvidenceBindings.RequireGate(root, gate, evidence);
            Require(used.Count == owned.Count, "assembly-unreferenced-file");
            return new(producer, indexBytes, root.Clone(), gate, evidence, owned, objects);
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw Error("assembly-shape");
        }
    }

    private static byte[] ReadObject(JsonElement reference, string media,
        IReadOnlyDictionary<string, byte[]> files, Dictionary<string, ReadOnlyMemory<byte>> objects, HashSet<string> used)
    {
        var address = ContentAddress.Parse(reference);
        var name = "objects/" + address.Sha256;
        Require(address.MediaType == media && files.TryGetValue(name, out _), "assembly-object");
        var bytes = files[name];
        Require(bytes.Length == address.ByteLength && Cp6DeterministicJson.Sha256Hex(bytes) == address.Sha256, "assembly-object");
        used.Add(name);
        objects[address.Key] = bytes;
        return bytes;
    }

    private static bool Equal(JsonElement left, JsonElement right) =>
        S06ArtifactAssembly.Canonical(new { value = left }).AsSpan().SequenceEqual(S06ArtifactAssembly.Canonical(new { value = right }));
}
```

### tools/p10/ReleaseVerifier/InspectedValidationArtifact.cs

```csharp
using System.Collections.Frozen;
using System.Text.Json;

namespace CP6.P10.ReleaseVerifier;

internal sealed class InspectedValidationArtifact
{
    private readonly byte[] _indexBytes;
    private readonly Dictionary<string, byte[]> _files;
    private readonly Dictionary<string, byte[]> _objects;

    internal InspectedValidationArtifact(GitHubWorkflowIdentity producer, byte[] indexBytes, JsonElement index,
        JsonElement gate, Dictionary<string, InspectedEvidence> evidence, Dictionary<string, byte[]> files,
        Dictionary<string, ReadOnlyMemory<byte>> objects)
    {
        Producer = producer;
        _indexBytes = indexBytes.ToArray();
        Index = index.Clone();
        Gate = gate.Clone();
        Evidence = evidence.ToFrozenDictionary(StringComparer.Ordinal);
        _files = files.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
        _objects = objects.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
    }

    internal GitHubWorkflowIdentity Producer { get; }
    internal JsonElement Index { get; }
    internal JsonElement Gate { get; }
    internal IReadOnlyDictionary<string, InspectedEvidence> Evidence { get; }
    internal bool EvidenceAuthenticated => false;
    internal byte[] CopyIndexBytes() => _indexBytes.ToArray();
    internal IReadOnlyDictionary<string, ReadOnlyMemory<byte>> CopyFiles() =>
        _files.ToDictionary(p => p.Key, p => (ReadOnlyMemory<byte>)p.Value.ToArray(), StringComparer.Ordinal);
    internal IReadOnlyDictionary<string, ReadOnlyMemory<byte>> CopyObjects() =>
        _objects.ToDictionary(p => p.Key, p => (ReadOnlyMemory<byte>)p.Value.ToArray(), StringComparer.Ordinal);
}
```

### tools/p10/ReleaseVerifier/S06CandidateAssembly.cs

```csharp
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

internal static class S06CandidateAssembly
{
    // No publication success field or invented workflow conclusion is included.
    internal static AssembledPlatformCandidate Create(InspectedValidationArtifact artifact, GitHubWorkflowIdentity publisher)
    {
        publisher.RequireValid();
        Require(publisher.Repository == "GTX537/CP6" && publisher.WorkflowPath == S06ReleaseIdentity.PublicationPath &&
            publisher.CommitSha == artifact.Producer.CommitSha && publisher.RunId != artifact.Producer.RunId, "assembly-publisher");
        var root = artifact.Index;
        var bytes = S06ArtifactAssembly.Control(Cp6ReleaseContractIds.PlatformCandidate, new
        {
            candidateKind = "PlatformReference", deployable = false, createdAtUtc = FormatTime(DateTimeOffset.UtcNow),
            platformSource = root.GetProperty("platformSource"),
            packages = S06ReleaseIdentity.PackageHashes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new
            {
                packageId = p.Key, version = S06ReleaseIdentity.Version, sourceGitSha = S06ReleaseIdentity.Source,
                authorSignedPackageSha256 = p.Value, publishedPackageSha256 = p.Value,
                feedIdentity = FormalFeedPolicy.Index + "#" + p.Key + "/" + S06ReleaseIdentity.Version,
                feedTransformation = "BytePreserving", signerFingerprint = S06ReleaseIdentity.Signer, timestampPolicy = "Rfc3161Required"
            }).ToArray(),
            buildProvenance = artifact.Evidence["PackageProvenance"].Record.GetProperty("object"),
            images = root.GetProperty("images"),
            crmConsumer = new
            {
                repository = "GTX537/CP6.CRM", workflowPath = ".github/workflows/crm-validation.yml",
                workflowFileSha = "924014cb1231824a9b57ab82a6f9638f76329919", runId = 34134695003L,
                runAttempt = 1, commitSha = S06ReleaseIdentity.CrmSource, environment = "none"
            },
            publisher = Workflow(publisher), verifier = Workflow(artifact.Producer), policyVersions = new { trust = 1, evidence = 1 },
            evidence = root.GetProperty("evidence"), releaseGateResult = root.GetProperty("releaseGateResult")
        });
        var objects = artifact.CopyObjects();
        return new(bytes, objects, EvidenceGraphInspection.Inspect(bytes, objects));
    }
}

internal sealed class AssembledPlatformCandidate
{
    private readonly byte[] _bytes;
    private readonly Dictionary<string, byte[]> _objects;

    internal AssembledPlatformCandidate(byte[] bytes, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> objects, InspectedEvidenceGraph inspection)
    {
        _bytes = bytes.ToArray();
        _objects = objects.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
        Inspection = inspection;
    }

    internal InspectedEvidenceGraph Inspection { get; }
    internal byte[] CopyBytes() => _bytes.ToArray();
    internal IReadOnlyDictionary<string, ReadOnlyMemory<byte>> CopyObjects() =>
        _objects.ToDictionary(p => p.Key, p => (ReadOnlyMemory<byte>)p.Value.ToArray(), StringComparer.Ordinal);
}
```

## Task 3 — Verify and checkpoint

- [x] Run focused tests and all Release verifier tests with real mandatory cosign/formal-package/GitHub/feed inputs.
- [x] Run formatting verification, exact code/plan parity and complete six-file scope/hygiene review.
- [ ] Save the reviewed six-file scope as a local auditable commit.

From `tools/p10` with .NET 8.0.424 and existing process-only test environment:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~S06AssemblyTests --logger "trx;LogFileName=s06-assembly-red.trx" --results-directory ../../artifacts/p10/assembly-red
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~S06AssemblyTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore -c Release
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
```

From the task repository root:

```powershell
git diff --check
git add -- tools/p10/ReleaseVerifier/S06ArtifactAssembly.cs tools/p10/ReleaseVerifier/S06ArtifactInspection.cs tools/p10/ReleaseVerifier/InspectedValidationArtifact.cs tools/p10/ReleaseVerifier/S06CandidateAssembly.cs tools/p10/ReleaseVerifier.Tests/S06AssemblyTests.cs docs/superpowers/plans/2026-09-08-p10-s06-artifact-candidate-assembly.md
git diff --cached --check
git commit -m "feat(p10): assemble validation artifacts and platform candidates"
```

## Self-review and remaining acceptance

Every requirement here maps to Tasks 1–3; the existing graph inspector remains unchanged and rechecks the assembled candidate. This module supplies no acceptance capability or network-write path. Actual completed-workflow evidence, native proof verification, protected collector, CLI/workflows, clean pre/post readback, conditional Locator commit and cross-repository audit remain required before P10 completion. No production, old R2, trust, package publication or remote object mutation occurs in this checkpoint.

## Execution record — 2026-09-08

Proper RED: 30/30 new cases failed from missing implementation with zero skips or unrelated failures, independently counted from TRX. GREEN: 30/30 focused passed; complete Release suite passed 1,211/1,211 with zero skipped. Formatting, exact five-file code/plan parity, full source review and six-path hygiene checks passed. The assembled unsigned unit graphs remain CandidateAccepted=false; no real protected workflow, image build, remote publication or S06 acceptance is claimed.
