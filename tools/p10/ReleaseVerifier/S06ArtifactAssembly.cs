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
