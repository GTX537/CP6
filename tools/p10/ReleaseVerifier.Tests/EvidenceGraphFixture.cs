using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Only the S04/policy bytes are historical evidence. The surrounding graph is an unpublished test fixture.
internal sealed class EvidenceGraphFixture
{
    internal const string Source = "3ff27e26962dcfd722887afb80a4306010dd9ee1";
    internal const string PublicSource = "2222222222222222222222222222222222222222";
    internal const string CrmSource = "a31ca0e323418f7e4108cc6220c0f5fa132e7fc2";
    internal const string IndexHash = "1332bf21e4253112d457f86cdc0cba829fc58f20c3914fa738dd992d917e2914";
    internal const string Created = "2026-09-08T00:00:00.000Z";
    internal static readonly SortedDictionary<string, string> Kinds = new(StringComparer.Ordinal)
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
    };

    internal static GraphInput Build(Action<JsonObject>? candidateChange = null,
        Action<SortedDictionary<string, JsonObject>>? recordChange = null, Action<JsonObject>? gateChange = null,
        Action<Dictionary<string, byte[]>>? payloadChange = null)
    {
        var payloads = Kinds.Keys.ToDictionary(k => k, k => Encoding.UTF8.GetBytes(
            "{\"fixtureKind\":\"" + k + "\",\"notAcceptanceEvidence\":true}"), StringComparer.Ordinal);
        payloads["FormalPackagePublication"] = EvidenceGraphBaseline.Publication();
        payloads["PackageProvenance"] = EvidenceGraphBaseline.Provenance();
        payloads["TrustPolicy"] = EvidenceGraphBaseline.Trust();
        payloads["NuGetTrustPolicy"] = EvidenceGraphBaseline.NuGetTrust();
        payloads["ImageScan"] = """{"version":"2.1.0","runs":[{"tool":{"driver":{"name":"FixtureOnly"}},"properties":{"score":9.8},"results":[]}]}"""u8.ToArray();
        payloadChange?.Invoke(payloads);
        var references = payloads.ToDictionary(p => p.Key,
            p => Reference(p.Value, Kinds[p.Key], p.Key.ToLowerInvariant() + ".json"), StringComparer.Ordinal);
        var publication = JsonNode.Parse(EvidenceGraphBaseline.Publication())!.AsObject();
        var packageFields = new[] { "packageId", "version", "sourceGitSha", "authorSignedPackageSha256",
            "publishedPackageSha256", "feedIdentity", "feedTransformation", "signerFingerprint", "timestampPolicy" };
        var packages = publication["packages"]!.AsArray().Select(p => new JsonObject(p!.AsObject()
            .Where(field => packageFields.Contains(field.Key, StringComparer.Ordinal))
            .Select(field => new KeyValuePair<string, JsonNode?>(field.Key, field.Value!.DeepClone())))).ToArray();
        var root = new JsonObject
        {
            ["$schemaId"] = Cp6ReleaseContractIds.PlatformCandidate,
            ["candidateKind"] = "PlatformReference",
            ["deployable"] = false,
            ["createdAtUtc"] = Created,
            ["platformSource"] = Subject("SourceProvenance", "GTX537/CP6.Platform", Value(references["SourceReference"], "sha256"), Source),
            ["packages"] = new JsonArray(packages.Select(p => (JsonNode)p).ToArray()),
            ["buildProvenance"] = references["PackageProvenance"].DeepClone(),
            ["images"] = new JsonArray(Subject("OciImage", "ghcr.io/gtx537/cp6-p10-verifier",
                "sha256:" + Cp6DeterministicJson.Sha256Hex("unpublished-test-image"u8), PublicSource)),
            ["crmConsumer"] = Workflow("GTX537/CP6.CRM", ".github/workflows/crm-validation.yml", CrmSource,
                "924014cb1231824a9b57ab82a6f9638f76329919", 34134695003, "none"),
            ["publisher"] = Workflow("GTX537/CP6", ".github/workflows/p10-platform-candidate.yml", PublicSource,
                new string('4', 40), 999992, "p10-platform-candidate"),
            ["verifier"] = Workflow("GTX537/CP6", ".github/workflows/p10-platform-validation.yml", PublicSource,
                new string('5', 40), 999991, "p10-platform-candidate"),
            ["policyVersions"] = new JsonObject { ["trust"] = 1, ["evidence"] = 1 }
        };
        var records = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var kind in Kinds.Keys)
        {
            var subjects = new List<JsonNode>
            {
                Subject("Evidence", kind, Value(references[kind], "sha256"),
                    kind == "PackageProvenance" || kind == "FormalPackagePublication" ? Source : PublicSource)
            };
            if (new[] { "CrmConsumer", "FormalPackagePublication", "FormalPackageVerification", "PackageProvenance" }.Contains(kind))
            {
                subjects.AddRange(packages.Select(p => Subject("Package", Value(p, "packageId"), Value(p, "publishedPackageSha256"), Source)));
                subjects.Add(root["platformSource"]!.DeepClone());
            }
            if (kind == "SourceReference") subjects.Add(root["platformSource"]!.DeepClone());
            if (kind == "CrmConsumer") subjects.Add(Subject("Consumer", "GTX537/CP6.CRM", IndexHash, CrmSource));
            if (new[] { "ImageProvenance", "ImageSbom", "ImageScan", "OciSignature" }.Contains(kind))
                subjects.Add(root["images"]![0]!.DeepClone());
            if (kind == "ImageProvenance")
            {
                var release = packages.Single(p => Value(p, "packageId") == "CP6.Platform.Release");
                subjects.Add(Subject("Package", "CP6.Platform.Release", Value(release, "publishedPackageSha256"), Source));
            }
            if (kind == "TrustPolicy" || kind == "NuGetTrustPolicy")
                subjects.Add(Subject("Policy", kind, Value(references[kind], "sha256"), PublicSource));
            records[kind] = new JsonObject
            {
                ["$schemaId"] = Cp6ReleaseContractIds.EvidenceRecord,
                ["createdAtUtc"] = Created,
                ["evidenceKind"] = kind,
                ["producer"] = root["verifier"]!.DeepClone(),
                ["policyVersion"] = 1,
                ["accessClass"] = "RequiredPublic",
                ["object"] = references[kind].DeepClone(),
                ["subjects"] = SortedSubjects(subjects),
                ["conclusion"] = "Success"
            };
        }
        recordChange?.Invoke(records);
        var objects = payloads.ToDictionary(p => Value(references[p.Key], "key"),
            p => (ReadOnlyMemory<byte>)p.Value, StringComparer.Ordinal);
        var recordReferences = new JsonArray();
        foreach (var pair in records)
        {
            var bytes = Canonical(pair.Value);
            var reference = Reference(bytes, Cp6ReleaseMediaTypes.EvidenceRecord, pair.Key.ToLowerInvariant() + ".record.json");
            objects[Value(reference, "key")] = bytes;
            recordReferences.Add(reference);
        }
        root["evidence"] = recordReferences;
        var gateSubjects = records.Values.SelectMany(r => r["subjects"]!.AsArray()).Select(s => s!)
            .DistinctBy(ExactSubject, StringComparer.Ordinal);
        var gates = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in references) gates[pair.Key] = Value(pair.Value, "sha256");
        foreach (var package in packages) gates["NuGetSignature/" + Value(package, "packageId")] = Value(package, "publishedPackageSha256");
        gates["VerifierPackage"] = Value(packages.Single(p => Value(p, "packageId") == "CP6.Platform.Release"), "publishedPackageSha256");
        var gate = new JsonObject
        {
            ["$schemaId"] = Cp6ReleaseContractIds.ReleaseGateResult,
            ["createdAtUtc"] = Created,
            ["workflow"] = root["verifier"]!.DeepClone(),
            ["inputSubjects"] = SortedSubjects(gateSubjects),
            ["gates"] = new JsonArray(gates.Select(g => (JsonNode)new JsonObject
            {
                ["name"] = g.Key,
                ["subjectHash"] = g.Value,
                ["conclusion"] = "Success"
            }).ToArray()),
            ["conclusion"] = "Success"
        };
        gateChange?.Invoke(gate);
        var gateBytes = Canonical(gate);
        var gateReference = Reference(gateBytes, Cp6ReleaseMediaTypes.ReleaseGateResult, "gate.json");
        objects[Value(gateReference, "key")] = gateBytes;
        root["releaseGateResult"] = gateReference;
        candidateChange?.Invoke(root);
        return new(Canonical(root), objects);
    }

    internal static JsonObject Subject(string kind, string name, string hash, string source) => new()
    {
        ["subjectKind"] = kind,
        ["subjectName"] = name,
        ["sha256OrDigest"] = hash,
        ["sourceGitSha"] = source
    };

    internal static JsonArray SortedSubjects(IEnumerable<JsonNode> subjects) =>
        new(subjects.OrderBy(SubjectKey, StringComparer.Ordinal).Select(s => s.DeepClone()).ToArray());

    internal static string Value(JsonNode node, string name) => node[name]!.GetValue<string>();
    private static string SubjectKey(JsonNode node) =>
        Value(node, "subjectKind") + "\0" + Value(node, "subjectName") + "\0" + Value(node, "sha256OrDigest");
    private static string ExactSubject(JsonNode node) => SubjectKey(node) + "\0" + Value(node, "sourceGitSha");
    internal static byte[] Canonical(JsonNode value) => Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(value));
    internal static JsonObject Reference(byte[] bytes, string mediaType, string name) =>
        JsonNode.Parse(ContentAddress.Create(bytes, mediaType, name).ToJson().GetRawText())!.AsObject();

    private static JsonObject Workflow(string repository, string path, string commit, string file, long run, string environment) => new()
    {
        ["repository"] = repository,
        ["workflowPath"] = path,
        ["commitSha"] = commit,
        ["workflowFileSha"] = file,
        ["runId"] = run,
        ["runAttempt"] = 1,
        ["environment"] = environment
    };
}

internal sealed record GraphInput(byte[] Candidate, Dictionary<string, ReadOnlyMemory<byte>> Objects);
