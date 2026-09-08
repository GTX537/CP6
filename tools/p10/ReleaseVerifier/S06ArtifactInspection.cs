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
