using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06ReleaseIdentity;

namespace CP6.P10.ReleaseVerifier;

public static class EvidenceGraphInspection
{
    public static InspectedEvidenceGraph Inspect(ReadOnlyMemory<byte> candidate,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> objects)
    {
        Require(candidate.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes, "graph-size");
        Require(objects.Count is > 0 and <= 32, "graph-size");
        long total = candidate.Length;
        foreach (var item in objects)
        {
            Require(item.Key.Length <= 300 && item.Value.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes, "graph-size");
            total += item.Value.Length;
            Require(total <= 64L * 1024 * 1024, "graph-size");
        }
        var bytes = candidate.ToArray();
        var snapshot = objects.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
        var checkedCandidate = Cp6ReleaseValidator.ValidatePlatformCandidate(bytes);
        using var candidateDocument = JsonDocument.Parse(bytes);
        var root = candidateDocument.RootElement;
        RequireCandidate(root);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var evidence = new Dictionary<string, InspectedEvidence>(StringComparer.Ordinal);
        foreach (var reference in root.GetProperty("evidence").EnumerateArray())
        {
            var recordBytes = Read(reference, Cp6ReleaseMediaTypes.EvidenceRecord, snapshot, used);
            _ = Cp6SupportingContractValidator.ValidateEvidenceRecord(recordBytes);
            using var recordDocument = JsonDocument.Parse(recordBytes);
            var record = recordDocument.RootElement;
            var kind = Text(record, "evidenceKind");
            Require(S06EvidenceBindings.MediaTypes.ContainsKey(kind) && !evidence.ContainsKey(kind), "graph-evidence-set");
            Require(Text(record, "accessClass") == "RequiredPublic" &&
                Text(record, "conclusion") == "Success" && record.GetProperty("policyVersion").GetInt64() == 1, "graph-evidence-policy");
            Require(record.GetProperty("producer").GetRawText() == root.GetProperty("verifier").GetRawText(), "graph-evidence-producer");
            var payload = ContentAddress.Parse(record.GetProperty("object"));
            var payloadBytes = Read(record.GetProperty("object"), S06EvidenceBindings.MediaTypes[kind], snapshot, used);
            evidence.Add(kind, new(record, payload, payloadBytes));
        }
        Require(evidence.Keys.SequenceEqual(S06EvidenceBindings.MediaTypes.Keys.Order(StringComparer.Ordinal),
            StringComparer.Ordinal), "graph-evidence-set");
        Require(Text(root.GetProperty("platformSource"), "sha256OrDigest") ==
            evidence["SourceReference"].Payload.Sha256, "graph-source-binding");
        foreach (var item in evidence)
        {
            S06EvidenceBindings.RequireSubjects(item.Value.Record.GetProperty("subjects"),
                S06EvidenceBindings.RequiredSubjects(root, item.Key, item.Value.Payload), "graph-evidence-subjects");
        }
        RequireBaseline(evidence["FormalPackagePublication"], PublicationHash);
        RequireBaseline(evidence["PackageProvenance"], ProvenanceHash);
        RequireBaseline(evidence["NuGetTrustPolicy"], NuGetTrustHash);
        RequireBaseline(evidence["TrustPolicy"], VerifierTrust.Load().ValidatedDocument.Sha256);
        Require(root.GetProperty("buildProvenance").GetRawText() ==
            evidence["PackageProvenance"].Record.GetProperty("object").GetRawText(), "graph-provenance-binding");
        _ = Cp6SupportingContractValidator.ValidateBuildInvocationProvenance(evidence["PackageProvenance"].CopyPayloadBytes());
        var gateBytes = Read(root.GetProperty("releaseGateResult"), Cp6ReleaseMediaTypes.ReleaseGateResult, snapshot, used);
        _ = Cp6SupportingContractValidator.ValidateReleaseGateResult(gateBytes);
        using var gateDocument = JsonDocument.Parse(gateBytes);
        var gate = gateDocument.RootElement;
        S06EvidenceBindings.RequireGate(root, gate, evidence);
        foreach (var item in evidence.Values)
            Require(string.CompareOrdinal(Text(item.Record, "createdAtUtc"), Text(gate, "createdAtUtc")) <= 0, "graph-time");
        Require(used.Count == snapshot.Count, "graph-unreferenced-object");
        return new(checkedCandidate.Sha256, root, gate, evidence, used.Count);
    }

    private static byte[] Read(JsonElement reference, string mediaType,
        IReadOnlyDictionary<string, byte[]> objects, HashSet<string> used)
    {
        var address = ContentAddress.Parse(reference);
        Require(address.MediaType == mediaType, "graph-media-type");
        Require(objects.TryGetValue(address.Key, out var bytes), "graph-missing-object");
        Require(bytes!.Length == address.ByteLength, "graph-object-size");
        Require(Cp6DeterministicJson.Sha256Hex(bytes) == address.Sha256, "graph-object-hash");
        used.Add(address.Key);
        return bytes;
    }

    private static void RequireBaseline(InspectedEvidence evidence, string expectedHash) =>
        Require(evidence.Payload.Sha256 == expectedHash, "graph-baseline-hash");
}
