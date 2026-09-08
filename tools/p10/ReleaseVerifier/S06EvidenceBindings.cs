using System.Collections.Frozen;
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06ReleaseIdentity;

namespace CP6.P10.ReleaseVerifier;

internal static class S06EvidenceBindings
{
    internal static readonly FrozenDictionary<string, string> MediaTypes = new Dictionary<string, string>
    {
        ["CrmConsumer"] = Cp6ReleaseMediaTypes.InToto,
        ["FormalPackagePublication"] = Cp6ReleaseMediaTypes.FormalPackagePublication,
        ["FormalPackageVerification"] = Cp6ReleaseMediaTypes.InToto,
        ["ImageProvenance"] = Cp6ReleaseMediaTypes.InToto,
        ["ImageSbom"] = Cp6ReleaseMediaTypes.Spdx,
        ["ImageScan"] = Cp6ReleaseMediaTypes.Sarif,
        ["NuGetTrustPolicy"] = Cp6ReleaseMediaTypes.PinnedNuGetTrustStore,
        ["OciSignature"] = Cp6ReleaseMediaTypes.InToto,
        ["PackageProvenance"] = Cp6ReleaseMediaTypes.BuildInvocationProvenance,
        ["SourceReference"] = Cp6ReleaseMediaTypes.InToto,
        ["TrustPolicy"] = Cp6ReleaseMediaTypes.PinnedTrustStore
    }.ToFrozenDictionary(StringComparer.Ordinal);

    internal static JsonElement[] RequiredSubjects(JsonElement root, string kind, ContentAddress payload)
    {
        var publicSource = Text(root.GetProperty("verifier"), "commitSha");
        var originalSource = kind is "FormalPackagePublication" or "PackageProvenance" ? Source : publicSource;
        var subjects = new List<JsonElement> { Subject("Evidence", kind, payload.Sha256, originalSource) };
        if (kind is "CrmConsumer" or "FormalPackagePublication" or "FormalPackageVerification" or "PackageProvenance")
        {
            subjects.AddRange(PackageHashes.Select(p => Subject("Package", p.Key, p.Value, Source)));
            subjects.Add(root.GetProperty("platformSource").Clone());
        }
        if (kind == "SourceReference") subjects.Add(root.GetProperty("platformSource").Clone());
        if (kind == "CrmConsumer") subjects.Add(Subject("Consumer", "GTX537/CP6.CRM", CrmIndexHash, CrmSource));
        if (kind is "ImageProvenance" or "ImageSbom" or "ImageScan" or "OciSignature")
            subjects.Add(root.GetProperty("images")[0].Clone());
        if (kind == "ImageProvenance")
            subjects.Add(Subject("Package", ReleasePackage, PackageHashes[ReleasePackage], Source));
        if (kind is "TrustPolicy" or "NuGetTrustPolicy")
            subjects.Add(Subject("Policy", kind, payload.Sha256, publicSource));
        return subjects.OrderBy(SubjectKey, StringComparer.Ordinal).ToArray();
    }

    internal static JsonElement Subject(string kind, string name, string hash, string source) =>
        JsonSerializer.SerializeToElement(new { subjectKind = kind, subjectName = name, sha256OrDigest = hash, sourceGitSha = source });

    internal static string SubjectKey(JsonElement value) =>
        $"{Text(value, "subjectKind")}\0{Text(value, "subjectName")}\0{Text(value, "sha256OrDigest")}";

    internal static string ExactSubject(JsonElement value) =>
        SubjectKey(value) + "\0" + Text(value, "sourceGitSha");

    internal static void RequireSubjects(JsonElement actual, IEnumerable<JsonElement> expected, string code) =>
        Require(actual.EnumerateArray().Select(ExactSubject).SequenceEqual(
            expected.OrderBy(SubjectKey, StringComparer.Ordinal).Select(ExactSubject), StringComparer.Ordinal), code);

    internal static void RequireGate(JsonElement root, JsonElement gate,
        IReadOnlyDictionary<string, InspectedEvidence> evidence)
    {
        Require(Text(gate, "conclusion") == "Success", "graph-gate-conclusion");
        Require(gate.GetProperty("workflow").GetRawText() == root.GetProperty("verifier").GetRawText(), "graph-gate-workflow");
        Require(string.CompareOrdinal(Text(gate, "createdAtUtc"), Text(root, "createdAtUtc")) <= 0, "graph-time");
        var subjects = evidence.Values.SelectMany(e => e.Record.GetProperty("subjects").EnumerateArray())
            .DistinctBy(ExactSubject, StringComparer.Ordinal).ToArray();
        RequireSubjects(gate.GetProperty("inputSubjects"), subjects, "graph-gate-inputs");
        var expected = evidence.ToDictionary(p => p.Key, p => p.Value.Payload.Sha256, StringComparer.Ordinal);
        foreach (var package in PackageHashes) expected.Add("NuGetSignature/" + package.Key, package.Value);
        expected.Add("VerifierPackage", PackageHashes[ReleasePackage]);
        var gates = gate.GetProperty("gates").EnumerateArray().ToArray();
        Require(gates.Select(g => Text(g, "name")).SequenceEqual(expected.Keys.Order(StringComparer.Ordinal),
            StringComparer.Ordinal), "graph-gate-set");
        foreach (var item in gates)
        {
            Require(Text(item, "conclusion") == "Success", "graph-gate-conclusion");
            Require(Text(item, "subjectHash") == expected[Text(item, "name")], "graph-gate-binding");
        }
    }
}
