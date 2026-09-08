# P10 S06 Evidence Graph Binding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. The user selected sequential execution in this task; do not delegate or re-ask the execution mode.

**Goal:** Add bounded, package-fed inspection of all cross-document references in the selected non-deployable 0.10.1 Platform candidate, without equating structural consistency with formal acceptance.

**Architecture:** The formal Release package remains the schema and canonical-byte authority. Public CP6 owns this release-selection and evidence-completeness profile, checks a closed set of content-addressed objects, and returns an immutable inspection result whose CandidateAccepted is always false. Locator authentication and remote metadata checks precede this layer in the full verifier; actual NuGet/RFC3161, GitHub, CRM forward-binding, OCI signature, provenance, SBOM and scan semantics must also succeed before future acceptance.

**Tech Stack:** .NET SDK 8.0.424, C#/.NET 8, CP6.Platform.Release [0.10.1], xUnit, no new dependency.

---

## Approved scope and implementation decisions

- Continue the existing single-task S06 worktree, from reviewed base main 6f9d09f4e3b1627a25ec7859b748eba8cd66f621; preceding local commit 049e9a35. Root workspace changes are untouched.
- Implement approved parent design sections 7.4, 7.5, 8, 12 and 16.3 at the **graph-binding** boundary only. No workflow, Registry, R2 or runtime write is part of this module.
- Exactly the seven corrected S04 packages, their published/author-signed hashes, source 3ff27e26962dcfd722887afb80a4306010dd9ee1, version 0.10.1, feed and signer are pinned. These are release identities, not copied Platform schemas.
- Preserve the original publication/provenance bytes and their independent hashes. Tests carry only these public metadata bytes from immutable CRM bb1fd8b4f250fabde4476b6a450435de2d07c03f plus the reviewed public policies. The outer graph, its image digest and workflow IDs are explicitly unpublished regression data, never S06 acceptance evidence.
- The only image is ghcr.io/gtx537/cp6-p10-verifier at a sha256 digest, bound to the public verifier source. It is not an application image and never authorizes deployment.
- Platform source identity uses subjectKind SourceProvenance with the SHA-256 of the source-reference attestation and the actual 40-character sourceGitSha. The hash is not misrepresented as a hash of the source tree.
- Public workflow paths are .github/workflows/p10-platform-validation.yml and .github/workflows/p10-platform-candidate.yml. Their commits must agree, their run IDs must differ, and both declare the existing protected environment p10-platform-candidate. Validation requires protected CRM read and OCI signing inputs; this corrects the initial environment none assumption. External proof must establish completed successful validation before publication starts; a candidate never predicts publisher completion.
- crmConsumer identifies the completed original S05 main run 34134695003/1 at a31ca0e323418f7e4108cc6220c0f5fa132e7fc2, workflow blob 924014cb1231824a9b57ab82a6f9638f76329919. The CrmConsumer attestation also binds index 1332bf21e4253112d457f86cdc0cba829fc58f20c3914fa738dd992d917e2914. Independent verification of the evidence-only PR/main and subsequent CI remains a full-verifier requirement.
- Exactly 11 ordinal evidence kinds: CrmConsumer, FormalPackagePublication, FormalPackageVerification, ImageProvenance, ImageSbom, ImageScan, NuGetTrustPolicy, OciSignature, PackageProvenance, SourceReference, TrustPolicy. Each has exactly the subjects specified by the implementation below, a RequiredPublic access class, policy 1, Success, and the exact validation producer. Own object, source, package, consumer and image subject roles cannot be substituted.
- Gate inputs are the exact union of those full subjects, including sourceGitSha. Exactly 19 gates are required: the 11 evidence-kind names, seven NuGetSignature/<packageId> gates and VerifierPackage. Each gate binds its prescribed hash and is Success; overall Success alone is insufficient.
- Original S04 publication/provenance and both trust object hashes are pinned independently of candidate-supplied fields. The candidate buildProvenance must be the same full reference as its PackageProvenance record.
- Limit the supplied set to 32 objects, each 1..4 MiB, total candidate plus object bytes <=64 MiB. Require every referenced key/media/hash/length and reject unused objects. Copy input bytes before inspection and return defensive payload copies.
- CP6 candidate/record/gate/provenance canonicalization comes from the formal package. Third-party raw report bytes are never CP6-canonicalized (SARIF may contain fractions); semantic report verification belongs to the actual proof layer.
- All custom diagnostics are fixed codes/messages and do not echo untrusted field values.

## File map

| File | Responsibility |
|---|---|
| tools/p10/ReleaseVerifier/S06ReleaseIdentity.cs | Immutable selected-release and producer/lane identity profile |
| tools/p10/ReleaseVerifier/S06EvidenceBindings.cs | Exact evidence subjects and required successful gate map |
| tools/p10/ReleaseVerifier/EvidenceGraphInspection.cs | Bounded closed-graph traversal and byte/reference verification |
| tools/p10/ReleaseVerifier/InspectedEvidenceGraph.cs | Immutable, explicitly non-accepting inspection output |
| tools/p10/ReleaseVerifier.Tests/EvidenceGraphFixture.cs | Clearly test-only graph assembled around real historical metadata |
| tools/p10/ReleaseVerifier.Tests/EvidenceGraphBaseline.cs | Byte-exact public historical metadata, base64 to preserve bytes |
| tools/p10/ReleaseVerifier.Tests/EvidenceGraphInspectionTests.cs | Positive, mutation, limits, raw-byte and defensive-copy coverage |

## Task 1: Red tests before implementation

- [x] Add these complete test files; do not add behavior yet.

### tools/p10/ReleaseVerifier.Tests/EvidenceGraphFixture.cs

```csharp
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
```

### tools/p10/ReleaseVerifier.Tests/EvidenceGraphBaseline.cs

```csharp
namespace CP6.P10.ReleaseVerifier.Tests;

// Byte-exact public metadata, not copied Platform schema or validator code.
// S04 records: CRM bb1fd8b4f250fabde4476b6a450435de2d07c03f, docs/delivery/p10/0.10.1.
// Policies: the already reviewed public CP6 eng/p10/trust files. No secret/private key.
internal static class EvidenceGraphBaseline
{
    internal static byte[] Publication() => Convert.FromBase64String("""
        eyIkc2NoZW1hSWQiOiJodHRwczovL3NjaGVtYXMuY3A2LmRldi9yZWxlYXNlL2Zvcm1hbC1wYWNrYWdlLXB1YmxpY2F0aW9uLnYxIiwiYnVpbGRJ
        bnZvY2F0aW9uSWQiOiJwMTAtczA0OjNmZjI3ZTI2OTYyZGNmZDcyMjg4N2FmYjgwYTQzMDYwMTBkZDllZTE6MzQxMjY1MjExOTM6MSIsImNyZWF0
        ZWRBdFV0YyI6IjIwMjYtMDktMDdUMTM6MjI6MjAuNjA3WiIsInBhY2thZ2VzIjpbeyJhdXRob3JTaWduZWRQYWNrYWdlU2hhMjU2IjoiMTMxOTkx
        MmRhYzVjM2UxMmE1YmI0ODg1ZTY0ZDkyZjQzYTYwNTdlOTZkMmY2YzExYzA1ZGI5N2I5ZmM3ZjFmNyIsImZlZWRJZGVudGl0eSI6Imh0dHBzOi8v
        bnVnZXQucGtnLmdpdGh1Yi5jb20vR1RYNTM3L2luZGV4Lmpzb24jQ1A2LlBsYXRmb3JtLkFic3RyYWN0aW9ucy8wLjEwLjEiLCJmZWVkVHJhbnNm
        b3JtYXRpb24iOiJCeXRlUHJlc2VydmluZyIsInBhY2thZ2VJZCI6IkNQNi5QbGF0Zm9ybS5BYnN0cmFjdGlvbnMiLCJwdWJsaXNoZWRQYWNrYWdl
        U2hhMjU2IjoiMTMxOTkxMmRhYzVjM2UxMmE1YmI0ODg1ZTY0ZDkyZjQzYTYwNTdlOTZkMmY2YzExYzA1ZGI5N2I5ZmM3ZjFmNyIsInNpZ25lckZp
        bmdlcnByaW50IjoiMWRlYmZiOGZmMjg2ZWE1MTE5MmI3ZjI1OWQxYWM4MjNjMTA1YzQxODhlYWM0MDE0ODU5OGQzN2YwZTIwZmYwZCIsInNvdXJj
        ZUdpdFNoYSI6IjNmZjI3ZTI2OTYyZGNmZDcyMjg4N2FmYjgwYTQzMDYwMTBkZDllZTEiLCJ0aW1lc3RhbXBDZXJ0aWZpY2F0ZUNoYWluU2hhMjU2
        IjpbIjJkYTA5ZGE3ZjQxMzFmOWZlNzJkYjZjNWU2ZTljOTY1Njc1NWFmMDQzZjFlYTc0MmNjMGQyMTIwZTE0MWViZmMiLCJjYTBiMTU1NGVjZDkw
        MWVhMTlkY2FkODc0OWU5ZjI2NDhjOGQ2ZGZjZWExYWRkOWQyYzIxMDk0MTViYjgyY2NkIiwiMzM4NDZiNTQ1YTQ5YzliZTQ5MDNjNjBlMDE3MTNj
        MWJkNGU0ZWYzMWVhNjVjZDk1ZDY5ZTYyNzk0ZjMwYjk0MSIsIjNlOTA5OWI1MDE1ZThmNDg2YzAwYmNlYTlkMTExZWU3MjFmYWJhMzU1YTg5YmNm
        MWRmNjk1NjFlM2RjNjMyNWMiXSwidGltZXN0YW1wUG9saWN5IjoiUmZjMzE2MVJlcXVpcmVkIiwidGltZXN0YW1wUG9saWN5T2lkIjoiMi4xNi44
        NDAuMS4xMTQ0MTIuNy4xIiwidmVyc2lvbiI6IjAuMTAuMSJ9LHsiYXV0aG9yU2lnbmVkUGFja2FnZVNoYTI1NiI6IjI5NDllMjE0OWNhMmM5OGZh
        MWM5MzViODlmOTZjZTJkY2YzODgxODZkYWEwMzhmOGEyMGJiZTFkNTdmMDJiNzEiLCJmZWVkSWRlbnRpdHkiOiJodHRwczovL251Z2V0LnBrZy5n
        aXRodWIuY29tL0dUWDUzNy9pbmRleC5qc29uI0NQNi5QbGF0Zm9ybS5Bc3BOZXRDb3JlLzAuMTAuMSIsImZlZWRUcmFuc2Zvcm1hdGlvbiI6IkJ5
        dGVQcmVzZXJ2aW5nIiwicGFja2FnZUlkIjoiQ1A2LlBsYXRmb3JtLkFzcE5ldENvcmUiLCJwdWJsaXNoZWRQYWNrYWdlU2hhMjU2IjoiMjk0OWUy
        MTQ5Y2EyYzk4ZmExYzkzNWI4OWY5NmNlMmRjZjM4ODE4NmRhYTAzOGY4YTIwYmJlMWQ1N2YwMmI3MSIsInNpZ25lckZpbmdlcnByaW50IjoiMWRl
        YmZiOGZmMjg2ZWE1MTE5MmI3ZjI1OWQxYWM4MjNjMTA1YzQxODhlYWM0MDE0ODU5OGQzN2YwZTIwZmYwZCIsInNvdXJjZUdpdFNoYSI6IjNmZjI3
        ZTI2OTYyZGNmZDcyMjg4N2FmYjgwYTQzMDYwMTBkZDllZTEiLCJ0aW1lc3RhbXBDZXJ0aWZpY2F0ZUNoYWluU2hhMjU2IjpbIjJkYTA5ZGE3ZjQx
        MzFmOWZlNzJkYjZjNWU2ZTljOTY1Njc1NWFmMDQzZjFlYTc0MmNjMGQyMTIwZTE0MWViZmMiLCJjYTBiMTU1NGVjZDkwMWVhMTlkY2FkODc0OWU5
        ZjI2NDhjOGQ2ZGZjZWExYWRkOWQyYzIxMDk0MTViYjgyY2NkIiwiMzM4NDZiNTQ1YTQ5YzliZTQ5MDNjNjBlMDE3MTNjMWJkNGU0ZWYzMWVhNjVj
        ZDk1ZDY5ZTYyNzk0ZjMwYjk0MSIsIjNlOTA5OWI1MDE1ZThmNDg2YzAwYmNlYTlkMTExZWU3MjFmYWJhMzU1YTg5YmNmMWRmNjk1NjFlM2RjNjMy
        NWMiXSwidGltZXN0YW1wUG9saWN5IjoiUmZjMzE2MVJlcXVpcmVkIiwidGltZXN0YW1wUG9saWN5T2lkIjoiMi4xNi44NDAuMS4xMTQ0MTIuNy4x
        IiwidmVyc2lvbiI6IjAuMTAuMSJ9LHsiYXV0aG9yU2lnbmVkUGFja2FnZVNoYTI1NiI6ImUyYzYzZGQxNjBmZTE4OWRiMWZiYzkwOTBkYTM4Y2Fi
        MWQ2N2JkNTk5ZWRkMTMzM2FmYzk5MmE5M2Y0MTEwZWIiLCJmZWVkSWRlbnRpdHkiOiJodHRwczovL251Z2V0LnBrZy5naXRodWIuY29tL0dUWDUz
        Ny9pbmRleC5qc29uI0NQNi5QbGF0Zm9ybS5Db250cmFjdHMvMC4xMC4xIiwiZmVlZFRyYW5zZm9ybWF0aW9uIjoiQnl0ZVByZXNlcnZpbmciLCJw
        YWNrYWdlSWQiOiJDUDYuUGxhdGZvcm0uQ29udHJhY3RzIiwicHVibGlzaGVkUGFja2FnZVNoYTI1NiI6ImUyYzYzZGQxNjBmZTE4OWRiMWZiYzkw
        OTBkYTM4Y2FiMWQ2N2JkNTk5ZWRkMTMzM2FmYzk5MmE5M2Y0MTEwZWIiLCJzaWduZXJGaW5nZXJwcmludCI6IjFkZWJmYjhmZjI4NmVhNTExOTJi
        N2YyNTlkMWFjODIzYzEwNWM0MTg4ZWFjNDAxNDg1OThkMzdmMGUyMGZmMGQiLCJzb3VyY2VHaXRTaGEiOiIzZmYyN2UyNjk2MmRjZmQ3MjI4ODdh
        ZmI4MGE0MzA2MDEwZGQ5ZWUxIiwidGltZXN0YW1wQ2VydGlmaWNhdGVDaGFpblNoYTI1NiI6WyIyZGEwOWRhN2Y0MTMxZjlmZTcyZGI2YzVlNmU5
        Yzk2NTY3NTVhZjA0M2YxZWE3NDJjYzBkMjEyMGUxNDFlYmZjIiwiY2EwYjE1NTRlY2Q5MDFlYTE5ZGNhZDg3NDllOWYyNjQ4YzhkNmRmY2VhMWFk
        ZDlkMmMyMTA5NDE1YmI4MmNjZCIsIjMzODQ2YjU0NWE0OWM5YmU0OTAzYzYwZTAxNzEzYzFiZDRlNGVmMzFlYTY1Y2Q5NWQ2OWU2Mjc5NGYzMGI5
        NDEiLCIzZTkwOTliNTAxNWU4ZjQ4NmMwMGJjZWE5ZDExMWVlNzIxZmFiYTM1NWE4OWJjZjFkZjY5NTYxZTNkYzYzMjVjIl0sInRpbWVzdGFtcFBv
        bGljeSI6IlJmYzMxNjFSZXF1aXJlZCIsInRpbWVzdGFtcFBvbGljeU9pZCI6IjIuMTYuODQwLjEuMTE0NDEyLjcuMSIsInZlcnNpb24iOiIwLjEw
        LjEifSx7ImF1dGhvclNpZ25lZFBhY2thZ2VTaGEyNTYiOiJhMjE4ZDIyMGJkNDJhZGEzOTI4ZTQ1YTYyNDU1NThiN2YwZDNiOTZmZDRlYTNkOTE3
        Mzg4NmJiZTdjOTFlNmMwIiwiZmVlZElkZW50aXR5IjoiaHR0cHM6Ly9udWdldC5wa2cuZ2l0aHViLmNvbS9HVFg1MzcvaW5kZXguanNvbiNDUDYu
        UGxhdGZvcm0uRGVwbG95bWVudC8wLjEwLjEiLCJmZWVkVHJhbnNmb3JtYXRpb24iOiJCeXRlUHJlc2VydmluZyIsInBhY2thZ2VJZCI6IkNQNi5Q
        bGF0Zm9ybS5EZXBsb3ltZW50IiwicHVibGlzaGVkUGFja2FnZVNoYTI1NiI6ImEyMThkMjIwYmQ0MmFkYTM5MjhlNDVhNjI0NTU1OGI3ZjBkM2I5
        NmZkNGVhM2Q5MTczODg2YmJlN2M5MWU2YzAiLCJzaWduZXJGaW5nZXJwcmludCI6IjFkZWJmYjhmZjI4NmVhNTExOTJiN2YyNTlkMWFjODIzYzEw
        NWM0MTg4ZWFjNDAxNDg1OThkMzdmMGUyMGZmMGQiLCJzb3VyY2VHaXRTaGEiOiIzZmYyN2UyNjk2MmRjZmQ3MjI4ODdhZmI4MGE0MzA2MDEwZGQ5
        ZWUxIiwidGltZXN0YW1wQ2VydGlmaWNhdGVDaGFpblNoYTI1NiI6WyIyZGEwOWRhN2Y0MTMxZjlmZTcyZGI2YzVlNmU5Yzk2NTY3NTVhZjA0M2Yx
        ZWE3NDJjYzBkMjEyMGUxNDFlYmZjIiwiY2EwYjE1NTRlY2Q5MDFlYTE5ZGNhZDg3NDllOWYyNjQ4YzhkNmRmY2VhMWFkZDlkMmMyMTA5NDE1YmI4
        MmNjZCIsIjMzODQ2YjU0NWE0OWM5YmU0OTAzYzYwZTAxNzEzYzFiZDRlNGVmMzFlYTY1Y2Q5NWQ2OWU2Mjc5NGYzMGI5NDEiLCIzZTkwOTliNTAx
        NWU4ZjQ4NmMwMGJjZWE5ZDExMWVlNzIxZmFiYTM1NWE4OWJjZjFkZjY5NTYxZTNkYzYzMjVjIl0sInRpbWVzdGFtcFBvbGljeSI6IlJmYzMxNjFS
        ZXF1aXJlZCIsInRpbWVzdGFtcFBvbGljeU9pZCI6IjIuMTYuODQwLjEuMTE0NDEyLjcuMSIsInZlcnNpb24iOiIwLjEwLjEifSx7ImF1dGhvclNp
        Z25lZFBhY2thZ2VTaGEyNTYiOiI5MGEzNDE0MDFmOTA1OGJlMjk5N2U0MGY2MWMyZGQ1YWQwZmM0NzE3YzhiNjM2NzJhYmM5MDA5MTE4ZmM1YTcx
        IiwiZmVlZElkZW50aXR5IjoiaHR0cHM6Ly9udWdldC5wa2cuZ2l0aHViLmNvbS9HVFg1MzcvaW5kZXguanNvbiNDUDYuUGxhdGZvcm0uRW50aXR5
        RnJhbWV3b3JrLzAuMTAuMSIsImZlZWRUcmFuc2Zvcm1hdGlvbiI6IkJ5dGVQcmVzZXJ2aW5nIiwicGFja2FnZUlkIjoiQ1A2LlBsYXRmb3JtLkVu
        dGl0eUZyYW1ld29yayIsInB1Ymxpc2hlZFBhY2thZ2VTaGEyNTYiOiI5MGEzNDE0MDFmOTA1OGJlMjk5N2U0MGY2MWMyZGQ1YWQwZmM0NzE3Yzhi
        NjM2NzJhYmM5MDA5MTE4ZmM1YTcxIiwic2lnbmVyRmluZ2VycHJpbnQiOiIxZGViZmI4ZmYyODZlYTUxMTkyYjdmMjU5ZDFhYzgyM2MxMDVjNDE4
        OGVhYzQwMTQ4NTk4ZDM3ZjBlMjBmZjBkIiwic291cmNlR2l0U2hhIjoiM2ZmMjdlMjY5NjJkY2ZkNzIyODg3YWZiODBhNDMwNjAxMGRkOWVlMSIs
        InRpbWVzdGFtcENlcnRpZmljYXRlQ2hhaW5TaGEyNTYiOlsiMmRhMDlkYTdmNDEzMWY5ZmU3MmRiNmM1ZTZlOWM5NjU2NzU1YWYwNDNmMWVhNzQy
        Y2MwZDIxMjBlMTQxZWJmYyIsImNhMGIxNTU0ZWNkOTAxZWExOWRjYWQ4NzQ5ZTlmMjY0OGM4ZDZkZmNlYTFhZGQ5ZDJjMjEwOTQxNWJiODJjY2Qi
        LCIzMzg0NmI1NDVhNDljOWJlNDkwM2M2MGUwMTcxM2MxYmQ0ZTRlZjMxZWE2NWNkOTVkNjllNjI3OTRmMzBiOTQxIiwiM2U5MDk5YjUwMTVlOGY0
        ODZjMDBiY2VhOWQxMTFlZTcyMWZhYmEzNTVhODliY2YxZGY2OTU2MWUzZGM2MzI1YyJdLCJ0aW1lc3RhbXBQb2xpY3kiOiJSZmMzMTYxUmVxdWly
        ZWQiLCJ0aW1lc3RhbXBQb2xpY3lPaWQiOiIyLjE2Ljg0MC4xLjExNDQxMi43LjEiLCJ2ZXJzaW9uIjoiMC4xMC4xIn0seyJhdXRob3JTaWduZWRQ
        YWNrYWdlU2hhMjU2IjoiN2ZmYTIzYWNmOWMxNjIzY2JmNTIwZDhkZDdmZGZmMTA0YTc3YWJmMDc4MzJiZDRlODkxMDIwOTJhYjlmNDU1OCIsImZl
        ZWRJZGVudGl0eSI6Imh0dHBzOi8vbnVnZXQucGtnLmdpdGh1Yi5jb20vR1RYNTM3L2luZGV4Lmpzb24jQ1A2LlBsYXRmb3JtLk1lc3NhZ2luZy8w
        LjEwLjEiLCJmZWVkVHJhbnNmb3JtYXRpb24iOiJCeXRlUHJlc2VydmluZyIsInBhY2thZ2VJZCI6IkNQNi5QbGF0Zm9ybS5NZXNzYWdpbmciLCJw
        dWJsaXNoZWRQYWNrYWdlU2hhMjU2IjoiN2ZmYTIzYWNmOWMxNjIzY2JmNTIwZDhkZDdmZGZmMTA0YTc3YWJmMDc4MzJiZDRlODkxMDIwOTJhYjlm
        NDU1OCIsInNpZ25lckZpbmdlcnByaW50IjoiMWRlYmZiOGZmMjg2ZWE1MTE5MmI3ZjI1OWQxYWM4MjNjMTA1YzQxODhlYWM0MDE0ODU5OGQzN2Yw
        ZTIwZmYwZCIsInNvdXJjZUdpdFNoYSI6IjNmZjI3ZTI2OTYyZGNmZDcyMjg4N2FmYjgwYTQzMDYwMTBkZDllZTEiLCJ0aW1lc3RhbXBDZXJ0aWZp
        Y2F0ZUNoYWluU2hhMjU2IjpbIjJkYTA5ZGE3ZjQxMzFmOWZlNzJkYjZjNWU2ZTljOTY1Njc1NWFmMDQzZjFlYTc0MmNjMGQyMTIwZTE0MWViZmMi
        LCJjYTBiMTU1NGVjZDkwMWVhMTlkY2FkODc0OWU5ZjI2NDhjOGQ2ZGZjZWExYWRkOWQyYzIxMDk0MTViYjgyY2NkIiwiMzM4NDZiNTQ1YTQ5Yzli
        ZTQ5MDNjNjBlMDE3MTNjMWJkNGU0ZWYzMWVhNjVjZDk1ZDY5ZTYyNzk0ZjMwYjk0MSIsIjNlOTA5OWI1MDE1ZThmNDg2YzAwYmNlYTlkMTExZWU3
        MjFmYWJhMzU1YTg5YmNmMWRmNjk1NjFlM2RjNjMyNWMiXSwidGltZXN0YW1wUG9saWN5IjoiUmZjMzE2MVJlcXVpcmVkIiwidGltZXN0YW1wUG9s
        aWN5T2lkIjoiMi4xNi44NDAuMS4xMTQ0MTIuNy4xIiwidmVyc2lvbiI6IjAuMTAuMSJ9LHsiYXV0aG9yU2lnbmVkUGFja2FnZVNoYTI1NiI6ImFm
        ZTg1ZWFiZTc4OTY1ZTBkNzk2N2YzZjJjZjU0NTc0YzhhZGNiMDIxNDE1MjNhZWNkOGE4YmFmYzZkNzAwZTAiLCJmZWVkSWRlbnRpdHkiOiJodHRw
        czovL251Z2V0LnBrZy5naXRodWIuY29tL0dUWDUzNy9pbmRleC5qc29uI0NQNi5QbGF0Zm9ybS5SZWxlYXNlLzAuMTAuMSIsImZlZWRUcmFuc2Zv
        cm1hdGlvbiI6IkJ5dGVQcmVzZXJ2aW5nIiwicGFja2FnZUlkIjoiQ1A2LlBsYXRmb3JtLlJlbGVhc2UiLCJwdWJsaXNoZWRQYWNrYWdlU2hhMjU2
        IjoiYWZlODVlYWJlNzg5NjVlMGQ3OTY3ZjNmMmNmNTQ1NzRjOGFkY2IwMjE0MTUyM2FlY2Q4YThiYWZjNmQ3MDBlMCIsInNpZ25lckZpbmdlcnBy
        aW50IjoiMWRlYmZiOGZmMjg2ZWE1MTE5MmI3ZjI1OWQxYWM4MjNjMTA1YzQxODhlYWM0MDE0ODU5OGQzN2YwZTIwZmYwZCIsInNvdXJjZUdpdFNo
        YSI6IjNmZjI3ZTI2OTYyZGNmZDcyMjg4N2FmYjgwYTQzMDYwMTBkZDllZTEiLCJ0aW1lc3RhbXBDZXJ0aWZpY2F0ZUNoYWluU2hhMjU2IjpbIjJk
        YTA5ZGE3ZjQxMzFmOWZlNzJkYjZjNWU2ZTljOTY1Njc1NWFmMDQzZjFlYTc0MmNjMGQyMTIwZTE0MWViZmMiLCJjYTBiMTU1NGVjZDkwMWVhMTlk
        Y2FkODc0OWU5ZjI2NDhjOGQ2ZGZjZWExYWRkOWQyYzIxMDk0MTViYjgyY2NkIiwiMzM4NDZiNTQ1YTQ5YzliZTQ5MDNjNjBlMDE3MTNjMWJkNGU0
        ZWYzMWVhNjVjZDk1ZDY5ZTYyNzk0ZjMwYjk0MSIsIjNlOTA5OWI1MDE1ZThmNDg2YzAwYmNlYTlkMTExZWU3MjFmYWJhMzU1YTg5YmNmMWRmNjk1
        NjFlM2RjNjMyNWMiXSwidGltZXN0YW1wUG9saWN5IjoiUmZjMzE2MVJlcXVpcmVkIiwidGltZXN0YW1wUG9saWN5T2lkIjoiMi4xNi44NDAuMS4x
        MTQ0MTIuNy4xIiwidmVyc2lvbiI6IjAuMTAuMSJ9XSwic291cmNlR2l0U2hhIjoiM2ZmMjdlMjY5NjJkY2ZkNzIyODg3YWZiODBhNDMwNjAxMGRk
        OWVlMSIsInRvb2xjaGFpbiI6eyJkb3RuZXRTZGsiOiI4LjAuNDI0IiwibnVnZXRDbGllbnQiOiI2LjExLjIuMSIsInJ1bm5lckltYWdlIjoid2lu
        ZG93cy0yMDI1In0sInRydXN0Ijp7ImludGVybmFsbHlUcnVzdGVkIjp0cnVlLCJwb2xpY3lTaGEyNTYiOiJkYTM1OWUzYThlOWJlMjIyMDU0MWM1
        MzYxM2QyZGEyNzdjYjJiYjlhMjJhODc3MGRmMzBjODA4YTAzM2I5NTNmIiwicG9saWN5VmVyc2lvbiI6MSwicHVibGljQ2FUcnVzdGVkIjpmYWxz
        ZSwic2lnbmVyRmluZ2VycHJpbnQiOiIxZGViZmI4ZmYyODZlYTUxMTkyYjdmMjU5ZDFhYzgyM2MxMDVjNDE4OGVhYzQwMTQ4NTk4ZDM3ZjBlMjBm
        ZjBkIiwic3BraUtleUlkIjoic2hhMjU2OjI3ZWNjMjIzOWExYjNjMjM2ODYxMGQzNjAyYWFkYzUyNjBiNDRlMjZiYWZmZTg5NmI5YTI0NDk2NjJj
        Njk2ZDYiLCJ0aW1lc3RhbXBQb2xpY3kiOiJSZmMzMTYxUmVxdWlyZWQiLCJ0aW1lc3RhbXBTZXJ2aWNlIjoiaHR0cDovL3RpbWVzdGFtcC5kaWdp
        Y2VydC5jb20iLCJ0cnVzdE1vZGVsIjoiUGlubmVkU2VsZlNpZ25lZCJ9LCJ2ZXJpZmljYXRpb24iOnsibGludXgiOiJTdWNjZXNzIiwid2luZG93
        cyI6IlN1Y2Nlc3MifSwidmVyc2lvbiI6IjAuMTAuMSIsIndvcmtmbG93Ijp7ImNvbW1pdFNoYSI6IjNmZjI3ZTI2OTYyZGNmZDcyMjg4N2FmYjgw
        YTQzMDYwMTBkZDllZTEiLCJlbnZpcm9ubWVudCI6InAxMC1mb3JtYWwtcmVsZWFzZSIsInJlcG9zaXRvcnkiOiJHVFg1MzcvQ1A2LlBsYXRmb3Jt
        IiwicnVuQXR0ZW1wdCI6MSwicnVuSWQiOjM0MTI2NTIxMTkzLCJ3b3JrZmxvd0ZpbGVTaGEiOiIwMWM5NGUxMTIxMzIzMWVmNmY3M2Q4ZjlhOWNj
        Y2Q5ZjE1NjNlMTg1Iiwid29ya2Zsb3dQYXRoIjoiLmdpdGh1Yi93b3JrZmxvd3MvcDEwLWZvcm1hbC1wYWNrYWdlcy55bWwifX0=
        """);

    internal static byte[] Provenance() => Convert.FromBase64String("""
        eyIkc2NoZW1hSWQiOiJodHRwczovL3NjaGVtYXMuY3A2LmRldi9yZWxlYXNlL2J1aWxkLWludm9jYXRpb24tcHJvdmVuYW5jZS52MSIsImJ1aWxk
        SW52b2NhdGlvbklkIjoicDEwLXMwNDozZmYyN2UyNjk2MmRjZmQ3MjI4ODdhZmI4MGE0MzA2MDEwZGQ5ZWUxOjM0MTI2NTIxMTkzOjEiLCJjcmVh
        dGVkQXRVdGMiOiIyMDI2LTA5LTA3VDEzOjIwOjQzLjM3OVoiLCJmaW5hbFBhY2thZ2VzIjpbeyJmaW5hbFNoYTI1NiI6IjEzMTk5MTJkYWM1YzNl
        MTJhNWJiNDg4NWU2NGQ5MmY0M2E2MDU3ZTk2ZDJmNmMxMWMwNWRiOTdiOWZjN2YxZjciLCJwYWNrYWdlSWQiOiJDUDYuUGxhdGZvcm0uQWJzdHJh
        Y3Rpb25zIiwicHJlU2lnblNoYTI1NiI6ImExMmI4YzE0NzgxZTRjZjIzMWI5NDY2MmE2YWEwMGUwMzUwZDM0ZDA3ODdkOTNhMzk1MzM3OWMwMTVj
        YmFjZWUiLCJzdWJqZWN0Ijp7InNoYTI1Nk9yRGlnZXN0IjoiMTMxOTkxMmRhYzVjM2UxMmE1YmI0ODg1ZTY0ZDkyZjQzYTYwNTdlOTZkMmY2YzEx
        YzA1ZGI5N2I5ZmM3ZjFmNyIsInNvdXJjZUdpdFNoYSI6IjNmZjI3ZTI2OTYyZGNmZDcyMjg4N2FmYjgwYTQzMDYwMTBkZDllZTEiLCJzdWJqZWN0
        S2luZCI6IlBhY2thZ2UiLCJzdWJqZWN0TmFtZSI6IkNQNi5QbGF0Zm9ybS5BYnN0cmFjdGlvbnMifX0seyJmaW5hbFNoYTI1NiI6IjI5NDllMjE0
        OWNhMmM5OGZhMWM5MzViODlmOTZjZTJkY2YzODgxODZkYWEwMzhmOGEyMGJiZTFkNTdmMDJiNzEiLCJwYWNrYWdlSWQiOiJDUDYuUGxhdGZvcm0u
        QXNwTmV0Q29yZSIsInByZVNpZ25TaGEyNTYiOiJkNjZhOGY5NjRlOWQzOWU1YjA4NjhkYWZjMDdkMjNiMGE4ZmMyZDQ5MTE3ZmZkYmNhMjBmNzg3
        NzQzMDQyNWMzIiwic3ViamVjdCI6eyJzaGEyNTZPckRpZ2VzdCI6IjI5NDllMjE0OWNhMmM5OGZhMWM5MzViODlmOTZjZTJkY2YzODgxODZkYWEw
        MzhmOGEyMGJiZTFkNTdmMDJiNzEiLCJzb3VyY2VHaXRTaGEiOiIzZmYyN2UyNjk2MmRjZmQ3MjI4ODdhZmI4MGE0MzA2MDEwZGQ5ZWUxIiwic3Vi
        amVjdEtpbmQiOiJQYWNrYWdlIiwic3ViamVjdE5hbWUiOiJDUDYuUGxhdGZvcm0uQXNwTmV0Q29yZSJ9fSx7ImZpbmFsU2hhMjU2IjoiZTJjNjNk
        ZDE2MGZlMTg5ZGIxZmJjOTA5MGRhMzhjYWIxZDY3YmQ1OTllZGQxMzMzYWZjOTkyYTkzZjQxMTBlYiIsInBhY2thZ2VJZCI6IkNQNi5QbGF0Zm9y
        bS5Db250cmFjdHMiLCJwcmVTaWduU2hhMjU2IjoiN2RlNzc4YTgwYzBjY2RkMzA3MTYxZGE4Y2RlMzk5Yjc0NDA1NzY2MDQ2MzBkZjc3NzA3NzEz
        YWYwZWRmNjU5ZCIsInN1YmplY3QiOnsic2hhMjU2T3JEaWdlc3QiOiJlMmM2M2RkMTYwZmUxODlkYjFmYmM5MDkwZGEzOGNhYjFkNjdiZDU5OWVk
        ZDEzMzNhZmM5OTJhOTNmNDExMGViIiwic291cmNlR2l0U2hhIjoiM2ZmMjdlMjY5NjJkY2ZkNzIyODg3YWZiODBhNDMwNjAxMGRkOWVlMSIsInN1
        YmplY3RLaW5kIjoiUGFja2FnZSIsInN1YmplY3ROYW1lIjoiQ1A2LlBsYXRmb3JtLkNvbnRyYWN0cyJ9fSx7ImZpbmFsU2hhMjU2IjoiYTIxOGQy
        MjBiZDQyYWRhMzkyOGU0NWE2MjQ1NTU4YjdmMGQzYjk2ZmQ0ZWEzZDkxNzM4ODZiYmU3YzkxZTZjMCIsInBhY2thZ2VJZCI6IkNQNi5QbGF0Zm9y
        bS5EZXBsb3ltZW50IiwicHJlU2lnblNoYTI1NiI6IjVlYjI1MjZlNGUxMjU3MTYwMDE0OGM3NjNiZmRmMzZiZTE3NjgwYTdjMmMyNWU4M2NhZWM0
        OTk4ZWY0NDMwY2QiLCJzdWJqZWN0Ijp7InNoYTI1Nk9yRGlnZXN0IjoiYTIxOGQyMjBiZDQyYWRhMzkyOGU0NWE2MjQ1NTU4YjdmMGQzYjk2ZmQ0
        ZWEzZDkxNzM4ODZiYmU3YzkxZTZjMCIsInNvdXJjZUdpdFNoYSI6IjNmZjI3ZTI2OTYyZGNmZDcyMjg4N2FmYjgwYTQzMDYwMTBkZDllZTEiLCJz
        dWJqZWN0S2luZCI6IlBhY2thZ2UiLCJzdWJqZWN0TmFtZSI6IkNQNi5QbGF0Zm9ybS5EZXBsb3ltZW50In19LHsiZmluYWxTaGEyNTYiOiI5MGEz
        NDE0MDFmOTA1OGJlMjk5N2U0MGY2MWMyZGQ1YWQwZmM0NzE3YzhiNjM2NzJhYmM5MDA5MTE4ZmM1YTcxIiwicGFja2FnZUlkIjoiQ1A2LlBsYXRm
        b3JtLkVudGl0eUZyYW1ld29yayIsInByZVNpZ25TaGEyNTYiOiI2OGVhODgwMWMwMDYyNTc2NDA1OTNhYTU2ZTgwNWIwMmM5NmMwZjk3NTExMDFh
        YjU5OTNmYmZmMGE4MjI0Yzk4Iiwic3ViamVjdCI6eyJzaGEyNTZPckRpZ2VzdCI6IjkwYTM0MTQwMWY5MDU4YmUyOTk3ZTQwZjYxYzJkZDVhZDBm
        YzQ3MTdjOGI2MzY3MmFiYzkwMDkxMThmYzVhNzEiLCJzb3VyY2VHaXRTaGEiOiIzZmYyN2UyNjk2MmRjZmQ3MjI4ODdhZmI4MGE0MzA2MDEwZGQ5
        ZWUxIiwic3ViamVjdEtpbmQiOiJQYWNrYWdlIiwic3ViamVjdE5hbWUiOiJDUDYuUGxhdGZvcm0uRW50aXR5RnJhbWV3b3JrIn19LHsiZmluYWxT
        aGEyNTYiOiI3ZmZhMjNhY2Y5YzE2MjNjYmY1MjBkOGRkN2ZkZmYxMDRhNzdhYmYwNzgzMmJkNGU4OTEwMjA5MmFiOWY0NTU4IiwicGFja2FnZUlk
        IjoiQ1A2LlBsYXRmb3JtLk1lc3NhZ2luZyIsInByZVNpZ25TaGEyNTYiOiI0NTg0MjU0ZDg4ZDc4OTIyY2Q3YWYzMmY4ZDQ3MDFkZjA2MjU2ZDEx
        MDNiM2JjN2MyOTJlNTI2MWY5ZWY1YzViIiwic3ViamVjdCI6eyJzaGEyNTZPckRpZ2VzdCI6IjdmZmEyM2FjZjljMTYyM2NiZjUyMGQ4ZGQ3ZmRm
        ZjEwNGE3N2FiZjA3ODMyYmQ0ZTg5MTAyMDkyYWI5ZjQ1NTgiLCJzb3VyY2VHaXRTaGEiOiIzZmYyN2UyNjk2MmRjZmQ3MjI4ODdhZmI4MGE0MzA2
        MDEwZGQ5ZWUxIiwic3ViamVjdEtpbmQiOiJQYWNrYWdlIiwic3ViamVjdE5hbWUiOiJDUDYuUGxhdGZvcm0uTWVzc2FnaW5nIn19LHsiZmluYWxT
        aGEyNTYiOiJhZmU4NWVhYmU3ODk2NWUwZDc5NjdmM2YyY2Y1NDU3NGM4YWRjYjAyMTQxNTIzYWVjZDhhOGJhZmM2ZDcwMGUwIiwicGFja2FnZUlk
        IjoiQ1A2LlBsYXRmb3JtLlJlbGVhc2UiLCJwcmVTaWduU2hhMjU2IjoiNDc5MDZiNjkyMjY0OTY4ZDY5YzEzMGViNWJhNjc0NTE0MzdiMjNmYWE0
        NjNhZTZlYTdlOGNkZDgwMWRjN2Y1ZCIsInN1YmplY3QiOnsic2hhMjU2T3JEaWdlc3QiOiJhZmU4NWVhYmU3ODk2NWUwZDc5NjdmM2YyY2Y1NDU3
        NGM4YWRjYjAyMTQxNTIzYWVjZDhhOGJhZmM2ZDcwMGUwIiwic291cmNlR2l0U2hhIjoiM2ZmMjdlMjY5NjJkY2ZkNzIyODg3YWZiODBhNDMwNjAx
        MGRkOWVlMSIsInN1YmplY3RLaW5kIjoiUGFja2FnZSIsInN1YmplY3ROYW1lIjoiQ1A2LlBsYXRmb3JtLlJlbGVhc2UifX1dLCJwcmVTaWduT3V0
        cHV0cyI6W3sicGFja2FnZUlkIjoiQ1A2LlBsYXRmb3JtLkFic3RyYWN0aW9ucyIsInNoYTI1NiI6ImExMmI4YzE0NzgxZTRjZjIzMWI5NDY2MmE2
        YWEwMGUwMzUwZDM0ZDA3ODdkOTNhMzk1MzM3OWMwMTVjYmFjZWUifSx7InBhY2thZ2VJZCI6IkNQNi5QbGF0Zm9ybS5Bc3BOZXRDb3JlIiwic2hh
        MjU2IjoiZDY2YThmOTY0ZTlkMzllNWIwODY4ZGFmYzA3ZDIzYjBhOGZjMmQ0OTExN2ZmZGJjYTIwZjc4Nzc0MzA0MjVjMyJ9LHsicGFja2FnZUlk
        IjoiQ1A2LlBsYXRmb3JtLkNvbnRyYWN0cyIsInNoYTI1NiI6IjdkZTc3OGE4MGMwY2NkZDMwNzE2MWRhOGNkZTM5OWI3NDQwNTc2NjA0NjMwZGY3
        NzcwNzcxM2FmMGVkZjY1OWQifSx7InBhY2thZ2VJZCI6IkNQNi5QbGF0Zm9ybS5EZXBsb3ltZW50Iiwic2hhMjU2IjoiNWViMjUyNmU0ZTEyNTcx
        NjAwMTQ4Yzc2M2JmZGYzNmJlMTc2ODBhN2MyYzI1ZTgzY2FlYzQ5OThlZjQ0MzBjZCJ9LHsicGFja2FnZUlkIjoiQ1A2LlBsYXRmb3JtLkVudGl0
        eUZyYW1ld29yayIsInNoYTI1NiI6IjY4ZWE4ODAxYzAwNjI1NzY0MDU5M2FhNTZlODA1YjAyYzk2YzBmOTc1MTEwMWFiNTk5M2ZiZmYwYTgyMjRj
        OTgifSx7InBhY2thZ2VJZCI6IkNQNi5QbGF0Zm9ybS5NZXNzYWdpbmciLCJzaGEyNTYiOiI0NTg0MjU0ZDg4ZDc4OTIyY2Q3YWYzMmY4ZDQ3MDFk
        ZjA2MjU2ZDExMDNiM2JjN2MyOTJlNTI2MWY5ZWY1YzViIn0seyJwYWNrYWdlSWQiOiJDUDYuUGxhdGZvcm0uUmVsZWFzZSIsInNoYTI1NiI6IjQ3
        OTA2YjY5MjI2NDk2OGQ2OWMxMzBlYjViYTY3NDUxNDM3YjIzZmFhNDYzYWU2ZWE3ZThjZGQ4MDFkYzdmNWQifV0sInNvdXJjZUdpdFNoYSI6IjNm
        ZjI3ZTI2OTYyZGNmZDcyMjg4N2FmYjgwYTQzMDYwMTBkZDllZTEiLCJ0b29sY2hhaW4iOnsiZG90bmV0U2RrIjoiOC4wLjQyNCIsInJ1bm5lciI6
        IndpbjI1LXZzMjAyNiJ9fQ==
        """);

    internal static byte[] Trust() => Convert.FromBase64String("""
        eyIkc2NoZW1hSWQiOiJodHRwczovL3NjaGVtYXMuY3A2LmRldi9yZWxlYXNlL3Bpbm5lZC10cnVzdC1zdG9yZS52MSIsImFjY2VwdGVkSGlzdG9y
        aWNhbFBvbGljeVZlcnNpb25zIjpbXSwia2V5cyI6W3sia2V5SWQiOiJzaGEyNTY6OWMwZmQwNWIzMTU5NjUxY2MyZTkxMzg1NTVmMzIzODc5ODhj
        Njk2MTg4OWVlMDAyMTExMzllNzEwZjFmZWJhYSIsInB1YmxpY0tleSI6Ii0tLS0tQkVHSU4gUFVCTElDIEtFWS0tLS0tXHUwMDBhTUZrd0V3WUhL
        b1pJemowQ0FRWUlLb1pJemowREFRY0RRZ0FFdStqS1dJZzNCY01WVWoxUy9ucVR6NUNJM3B3S1x1MDAwYXducWszaXIxV2ZyNjVKdzk1OWJiTkRp
        N3JLdEhwc1hHdWFFcTNYOTZOaERtTktiM2NjZzRQZ0ZobVE9PVx1MDAwYS0tLS0tRU5EIFBVQkxJQyBLRVktLS0tLSIsInB1cnBvc2UiOiJjYW5k
        aWRhdGUtbG9jYXRvciIsInZhbGlkRnJvbVV0YyI6IjIwMjYtMDktMDJUMTc6NDQ6MDQuNTQzWiIsInZhbGlkVW50aWxVdGMiOiIyMDI4LTA5LTAx
        VDE3OjQ0OjA0LjU0M1oifSx7ImtleUlkIjoic2hhMjU2OmViNjIzZDc4NGZjNTUyOTRlOTQyZmE0OTA2MjQ3Nzc2OWEzNDk0M2Q1OTk3ZmRiZGQ0
        ODNhZDBmYjAxMDNjMjEiLCJwdWJsaWNLZXkiOiItLS0tLUJFR0lOIFBVQkxJQyBLRVktLS0tLVx1MDAwYU1Ga3dFd1lIS29aSXpqMENBUVlJS29a
        SXpqMERBUWNEUWdBRXcvNi9vMlBDWDg2RzRhRVlyMXZwejc1SWtjWjdcdTAwMGFaYnU3Ym9LRks3OTg1ekNtVHVOcEM3U2Y4QU9VcTMxWGRUSDRS
        bm5iZlVKbmZDa0UrVDhESktzMUh3PT1cdTAwMGEtLS0tLUVORCBQVUJMSUMgS0VZLS0tLS0iLCJwdXJwb3NlIjoib2NpIiwidmFsaWRGcm9tVXRj
        IjoiMjAyNi0wOS0wMlQxNzo0NToxMy45NDJaIiwidmFsaWRVbnRpbFV0YyI6IjIwMjgtMDktMDFUMTc6NDU6MTMuOTQyWiJ9XSwibWluaW11bUFj
        Y2VwdGVkUG9saWN5VmVyc2lvbiI6MSwicG9saWN5VmVyc2lvbiI6MSwic3RvcmFnZUF1dGhvcml0aWVzIjpbeyJhY2Nlc3NNb2RlIjoiQXV0aGVu
        dGljYXRlZFJlYWRDb25kaXRpb25hbENyZWF0ZSIsImFjY291bnRJZCI6IjMwYzRhOGQxNjk3ZmZkM2RlNmExZTBhODgzNzY2MDdjIiwiYWxsb3dl
        ZFByZWZpeGVzIjpbImNhbmRpZGF0ZXMvcGxhdGZvcm0vIiwib2JqZWN0cy9zaGEyNTYvIl0sImJ1Y2tldCI6ImNwNi1yZWxlYXNlIiwiZW5kcG9p
        bnRUZW1wbGF0ZSI6Imh0dHBzOi8ve2FjY291bnRJZH0ucjIuY2xvdWRmbGFyZXN0b3JhZ2UuY29tIiwiaWQiOiJjcDYtcmVsZWFzZS1yMi12MSIs
        Imp1cmlzZGljdGlvbiI6ImRlZmF1bHQiLCJtYXhPYmplY3RCeXRlcyI6NDE5NDMwNCwicHJvdmlkZXIiOiJjbG91ZGZsYXJlLXIyIn1dfQ==
        """);

    internal static byte[] NuGetTrust() => Convert.FromBase64String("""
        eyIkc2NoZW1hSWQiOiJodHRwczovL3NjaGVtYXMuY3A2LmRldi9yZWxlYXNlL3Bpbm5lZC1udWdldC10cnVzdC1zdG9yZS52MSIsImFsbG93ZWRQ
        YWNrYWdlSWRzIjpbIkNQNi5QbGF0Zm9ybS5BYnN0cmFjdGlvbnMiLCJDUDYuUGxhdGZvcm0uQXNwTmV0Q29yZSIsIkNQNi5QbGF0Zm9ybS5Db250
        cmFjdHMiLCJDUDYuUGxhdGZvcm0uRGVwbG95bWVudCIsIkNQNi5QbGF0Zm9ybS5FbnRpdHlGcmFtZXdvcmsiLCJDUDYuUGxhdGZvcm0uTWVzc2Fn
        aW5nIiwiQ1A2LlBsYXRmb3JtLlJlbGVhc2UiXSwiaW50ZXJuYWxseVRydXN0ZWQiOnRydWUsInBvbGljeVZlcnNpb24iOjEsInB1YmxpY0NhVHJ1
        c3RlZCI6ZmFsc2UsInNpZ25lcnMiOlt7ImFjdGl2YXRlZEF0VXRjIjoiMjAyNi0wOS0wM1QwNTowMToxMy4zNzFaIiwiY2VydGlmaWNhdGVQYXRo
        IjoiY2VydGlmaWNhdGVzLzFkZWJmYjhmZjI4NmVhNTExOTJiN2YyNTlkMWFjODIzYzEwNWM0MTg4ZWFjNDAxNDg1OThkMzdmMGUyMGZmMGQuY2Vy
        IiwiY2VydGlmaWNhdGVTaGEyNTYiOiIxZGViZmI4ZmYyODZlYTUxMTkyYjdmMjU5ZDFhYzgyM2MxMDVjNDE4OGVhYzQwMTQ4NTk4ZDM3ZjBlMjBm
        ZjBkIiwiaXNzdWVyIjoiQ049Q1A2IFBsYXRmb3JtIFJlbGVhc2UgU2lnbmluZyIsInJldm9jYXRpb25SZWFzb24iOm51bGwsInJldm9rZWRBdFV0
        YyI6bnVsbCwic3BraUtleUlkIjoic2hhMjU2OjI3ZWNjMjIzOWExYjNjMjM2ODYxMGQzNjAyYWFkYzUyNjBiNDRlMjZiYWZmZTg5NmI5YTI0NDk2
        NjJjNjk2ZDYiLCJzdGF0dXMiOiJDdXJyZW50Iiwic3ViamVjdCI6IkNOPUNQNiBQbGF0Zm9ybSBSZWxlYXNlIFNpZ25pbmciLCJ2YWxpZEZyb21V
        dGMiOiIyMDI2LTA5LTAzVDA0OjU2OjEzLjAwMFoiLCJ2YWxpZFVudGlsVXRjIjoiMjAyOC0wOS0wMlQwNTowMToxMy4wMDBaIn1dLCJ0aW1lc3Rh
        bXBQb2xpY3kiOiJSZmMzMTYxUmVxdWlyZWQiLCJ0aW1lc3RhbXBTZXJ2aWNlIjoiaHR0cDovL3RpbWVzdGFtcC5kaWdpY2VydC5jb20iLCJ0cnVz
        dE1vZGVsIjoiUGlubmVkU2VsZlNpZ25lZCJ9
        """);

}
```

### tools/p10/ReleaseVerifier.Tests/EvidenceGraphInspectionTests.cs

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.Tests.EvidenceGraphFixture;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class EvidenceGraphInspectionTests
{
    [Fact]
    public void Coherent_unpublished_graph_is_inspectable_but_never_accepted()
    {
        var input = Build();
        var result = EvidenceGraphInspection.Inspect(input.Candidate, input.Objects);
        Assert.False(result.CandidateAccepted);
        Assert.Equal(23, result.ObjectCount);
        Assert.Equal(11, result.Evidence.Count);
        Assert.Equal(19, result.Gate.GetProperty("gates").GetArrayLength());
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(input.Candidate), result.Sha256);
        Assert.Equal("8b5fc47fd77902a3433d61b4cf181b67ef61ee994b60ea8286a690ab5084c961",
            result.Evidence["FormalPackagePublication"].Payload.Sha256);
        Assert.Equal("6885eee1db5075faeb1a321684f579ef853166a32db8af1cf861c34c79ec64a9",
            result.Evidence["PackageProvenance"].Payload.Sha256);
    }

    [Fact]
    public void Third_party_report_bytes_are_not_CP6_canonicalized_and_results_do_not_alias_input()
    {
        var input = Build();
        var result = EvidenceGraphInspection.Inspect(input.Candidate, input.Objects);
        var report = result.Evidence["ImageScan"];
        var original = report.CopyPayloadBytes();
        using var parsed = JsonDocument.Parse(original);
        Assert.Equal(9.8, parsed.RootElement.GetProperty("runs")[0].GetProperty("properties").GetProperty("score").GetDouble());
        Assert.Equal(input.Objects[report.Payload.Key].ToArray(), original);
        original[0] = 0;
        input.Candidate[0] = 0;
        input.Objects.Clear();
        Assert.Equal((byte)'{', report.CopyPayloadBytes()[0]);
        Assert.Equal("PlatformReference", result.Candidate.GetProperty("candidateKind").GetString());
        Assert.False(result.CandidateAccepted);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("source")]
    [InlineData("hash")]
    [InlineData("feed")]
    [InlineData("signer")]
    [InlineData("transformation")]
    [InlineData("test-timestamp")]
    public void Candidate_package_declarations_must_match_the_exact_formal_release(string mutation)
    {
        Reject(Build(candidateChange: root =>
        {
            var packages = root["packages"]!.AsArray();
            if (mutation == "version") foreach (var p in packages) p!["version"] = "0.10.0";
            if (mutation == "source") foreach (var p in packages) p!["sourceGitSha"] = PublicSource;
            var first = packages[0]!;
            if (mutation == "hash")
            {
                first["authorSignedPackageSha256"] = new string('a', 64);
                first["publishedPackageSha256"] = new string('a', 64);
            }
            if (mutation == "feed") first["feedIdentity"] = "https://untrusted.invalid/feed";
            if (mutation == "signer") first["signerFingerprint"] = new string('a', 64);
            if (mutation is "transformation" or "test-timestamp") first["feedTransformation"] = "Documented";
            if (mutation == "test-timestamp") first["timestampPolicy"] = "TestOnlyNone";
        }), "graph-package-identity");
    }

    [Theory]
    [InlineData("trust", "graph-policy")]
    [InlineData("evidence-policy", "graph-policy")]
    [InlineData("source-kind", "graph-source-identity")]
    [InlineData("source-name", "graph-source-identity")]
    [InlineData("source-sha", "graph-source-identity")]
    [InlineData("source-hash", "graph-source-binding")]
    [InlineData("no-image", "graph-image-set")]
    [InlineData("extra-image", "graph-image-set")]
    [InlineData("image-kind", "graph-image-identity")]
    [InlineData("image-repository", "graph-image-identity")]
    [InlineData("image-no-digest-prefix", "graph-image-identity")]
    [InlineData("image-source", "graph-image-identity")]
    [InlineData("publisher-repository", "graph-workflow-identity")]
    [InlineData("publisher-path", "graph-workflow-identity")]
    [InlineData("publisher-environment", "graph-workflow-identity")]
    [InlineData("verifier-path", "graph-workflow-identity")]
    [InlineData("verifier-environment", "graph-workflow-identity")]
    [InlineData("same-run", "graph-workflow-separation")]
    [InlineData("different-commit", "graph-workflow-separation")]
    [InlineData("crm-run", "graph-crm-identity")]
    [InlineData("crm-attempt", "graph-crm-identity")]
    [InlineData("crm-commit", "graph-crm-identity")]
    [InlineData("crm-workflow-blob", "graph-crm-identity")]
    [InlineData("provenance", "graph-provenance-binding")]
    public void Candidate_identity_and_lane_bindings_cannot_be_substituted(string mutation, string expected)
    {
        Reject(Build(candidateChange: root =>
        {
            var source = root["platformSource"]!;
            var image = root["images"]![0]!;
            var publisher = root["publisher"]!;
            var verifier = root["verifier"]!;
            var crm = root["crmConsumer"]!;
            switch (mutation)
            {
                case "trust": root["policyVersions"]!["trust"] = 2; break;
                case "evidence-policy": root["policyVersions"]!["evidence"] = 2; break;
                case "source-kind": source["subjectKind"] = "Package"; break;
                case "source-name": source["subjectName"] = "GTX537/CP6.Portal"; break;
                case "source-sha": source["sourceGitSha"] = PublicSource; break;
                case "source-hash": source["sha256OrDigest"] = new string('a', 64); break;
                case "no-image": root["images"] = new JsonArray(); break;
                case "extra-image": root["images"]!.AsArray().Add(image.DeepClone()); break;
                case "image-kind": image["subjectKind"] = "Package"; break;
                case "image-repository": image["subjectName"] = "ghcr.io/gtx537/cp6-api"; break;
                case "image-no-digest-prefix": image["sha256OrDigest"] = Value(image, "sha256OrDigest")[7..]; break;
                case "image-source": image["sourceGitSha"] = Source; break;
                case "publisher-repository": publisher["repository"] = "GTX537/CP6.Platform"; break;
                case "publisher-path": publisher["workflowPath"] = ".github/workflows/r2-candidate.yml"; break;
                case "publisher-environment": publisher["environment"] = "none"; break;
                case "verifier-path": verifier["workflowPath"] = ".github/workflows/r2-candidate.yml"; break;
                case "verifier-environment": verifier["environment"] = "r2-candidate"; break;
                case "same-run": publisher["runId"] = verifier["runId"]!.DeepClone(); break;
                case "different-commit": publisher["commitSha"] = Source; break;
                case "crm-run": crm["runId"] = 999991; break;
                case "crm-attempt": crm["runAttempt"] = 2; break;
                case "crm-commit": crm["commitSha"] = Source; break;
                case "crm-workflow-blob": crm["workflowFileSha"] = PublicSource; break;
                case "provenance": root["buildProvenance"] = root["releaseGateResult"]!.DeepClone(); break;
                default: throw new InvalidOperationException(mutation);
            }
        }), expected);
    }

    [Theory]
    [InlineData("missing", "graph-evidence-set")]
    [InlineData("extra", "graph-evidence-set")]
    [InlineData("duplicate", "graph-evidence-set")]
    [InlineData("restricted", "graph-evidence-policy")]
    [InlineData("test-only", "graph-evidence-policy")]
    [InlineData("failure", "graph-evidence-policy")]
    [InlineData("policy", "graph-evidence-policy")]
    [InlineData("producer", "graph-evidence-producer")]
    [InlineData("media", "graph-media-type")]
    [InlineData("subject-missing", "graph-evidence-subjects")]
    [InlineData("subject-extra", "graph-evidence-subjects")]
    [InlineData("subject-source", "graph-evidence-subjects")]
    [InlineData("subject-name", "graph-evidence-subjects")]
    [InlineData("late-record", "graph-time")]
    public void Required_evidence_is_complete_public_successful_and_exactly_subject_bound(string mutation, string expected)
    {
        Reject(Build(recordChange: records =>
        {
            var record = records["ImageScan"];
            switch (mutation)
            {
                case "missing": records.Remove("ImageScan"); break;
                case "extra":
                    records["ZUnexpected"] = record.DeepClone().AsObject();
                    records["ZUnexpected"]["evidenceKind"] = "ZUnexpected";
                    break;
                case "duplicate": record["evidenceKind"] = "CrmConsumer"; break;
                case "restricted": record["accessClass"] = "RestrictedAudit"; break;
                case "test-only": record["accessClass"] = "TestOnly"; break;
                case "failure": record["conclusion"] = "Failure"; break;
                case "policy": record["policyVersion"] = 2; break;
                case "producer": record["producer"]!["runAttempt"] = 2; break;
                case "media": record["object"]!["mediaType"] = Cp6ReleaseMediaTypes.Spdx; break;
                case "subject-missing": record["subjects"]!.AsArray().RemoveAt(1); break;
                case "subject-extra":
                    record["subjects"]!.AsArray().Add(Subject("ZZExtra", "extra", new string('a', 64), Source));
                    break;
                case "subject-source": record["subjects"]![1]!["sourceGitSha"] = Source; break;
                case "subject-name": record["subjects"]![1]!["subjectName"] = "ghcr.io/gtx537/cp6-api"; break;
                case "late-record": record["createdAtUtc"] = "2026-09-09T00:00:00.000Z"; break;
                default: throw new InvalidOperationException(mutation);
            }
        }), expected);
    }

    [Theory]
    [InlineData("FormalPackagePublication")]
    [InlineData("PackageProvenance")]
    [InlineData("TrustPolicy")]
    [InlineData("NuGetTrustPolicy")]
    public void Rehashed_replacement_baselines_are_not_accepted_as_the_selected_release(string kind) =>
        Reject(Build(payloadChange: payloads =>
            payloads[kind] = payloads[kind].Concat(new byte[] { 10 }).ToArray()), "graph-baseline-hash");

    [Theory]
    [InlineData("overall-failure", "graph-gate-conclusion")]
    [InlineData("one-failure", "graph-gate-conclusion")]
    [InlineData("missing-gate", "graph-gate-set")]
    [InlineData("renamed-gate", "graph-gate-set")]
    [InlineData("wrong-subject", "graph-gate-binding")]
    [InlineData("missing-input", "graph-gate-inputs")]
    [InlineData("wrong-input-source", "graph-gate-inputs")]
    [InlineData("workflow", "graph-gate-workflow")]
    [InlineData("late-gate", "graph-time")]
    public void Overall_success_cannot_hide_incomplete_or_misbound_individual_gates(string mutation, string expected)
    {
        Reject(Build(gateChange: gate =>
        {
            var gates = gate["gates"]!.AsArray();
            switch (mutation)
            {
                case "overall-failure": gate["conclusion"] = "Failure"; break;
                case "one-failure": gates[0]!["conclusion"] = "Failure"; break;
                case "missing-gate": gates.RemoveAt(0); break;
                case "renamed-gate": gates[0]!["name"] = "AUnapproved"; break;
                case "wrong-subject": gates[0]!["subjectHash"] = gates[1]!["subjectHash"]!.DeepClone(); break;
                case "missing-input": gate["inputSubjects"]!.AsArray().RemoveAt(0); break;
                case "wrong-input-source": gate["inputSubjects"]![0]!["sourceGitSha"] = PublicSource; break;
                case "workflow": gate["workflow"]!["runAttempt"] = 2; break;
                case "late-gate": gate["createdAtUtc"] = "2026-09-09T00:00:00.000Z"; break;
                default: throw new InvalidOperationException(mutation);
            }
        }), expected);
    }

    [Theory]
    [InlineData("missing", "graph-missing-object")]
    [InlineData("hash", "graph-object-hash")]
    [InlineData("length", "graph-object-size")]
    [InlineData("extra", "graph-unreferenced-object")]
    public void The_supplied_object_set_must_match_every_exact_reference(string mutation, string expected)
    {
        var input = Build();
        var key = input.Objects.Keys.First();
        switch (mutation)
        {
            case "missing": input.Objects.Remove(key); break;
            case "hash":
                var bytes = input.Objects[key].ToArray();
                bytes[0] ^= 1;
                input.Objects[key] = bytes;
                break;
            case "length": input.Objects[key] = input.Objects[key].ToArray().Concat(new byte[] { 10 }).ToArray(); break;
            case "extra": input.Objects.Add("unreferenced-object", "{}"u8.ToArray()); break;
            default: throw new InvalidOperationException(mutation);
        }
        Reject(input, expected);
    }

    [Theory]
    [InlineData("candidate-empty")]
    [InlineData("candidate-large")]
    [InlineData("object-empty")]
    [InlineData("object-large")]
    [InlineData("object-count")]
    [InlineData("total-bytes")]
    public void Input_limits_are_enforced_before_contract_or_graph_traversal(string mutation)
    {
        var input = Build();
        var candidate = input.Candidate;
        if (mutation == "candidate-empty") candidate = [];
        if (mutation == "candidate-large") candidate = new byte[4194305];
        if (mutation == "object-empty") input.Objects[input.Objects.Keys.First()] = ReadOnlyMemory<byte>.Empty;
        if (mutation == "object-large") input.Objects[input.Objects.Keys.First()] = new byte[4194305];
        if (mutation == "object-count") for (var i = 0; i < 33; i++) input.Objects["extra-" + i] = "{}"u8.ToArray();
        if (mutation == "total-bytes")
        {
            input.Objects.Clear();
            var shared = new byte[4194304];
            for (var i = 0; i < 16; i++) input.Objects["large-" + i] = shared;
        }
        Reject(new(candidate, input.Objects), "graph-size");
    }

    [Fact]
    public void Candidate_noncanonical_bytes_fail_before_graph_inspection()
    {
        var input = Build();
        Reject(new(input.Candidate.Concat(new byte[] { 10 }).ToArray(), input.Objects), "non-canonical-json");
    }

    [Fact]
    public void Candidate_evidence_order_is_the_ordinal_evidence_kind_order() =>
        Reject(Build(candidateChange: root =>
        {
            var refs = root["evidence"]!.AsArray().Select(r => r!.DeepClone()).Reverse().ToArray();
            root["evidence"] = new JsonArray(refs);
        }), "graph-evidence-set");

    [Fact]
    public void Correctly_shaped_but_hash_unbound_object_keys_fail_closed() =>
        Reject(Build(candidateChange: root =>
            root["releaseGateResult"]!["key"] = "objects/sha256/aa/" + new string('a', 64) + "/gate.json"), "object-key-binding");

    [Fact]
    public void Errors_do_not_echo_untrusted_field_values()
    {
        const string marker = "UNTRUSTED_DIAGNOSTIC_MARKER";
        var input = Build(candidateChange: root => root["packages"]![0]!["feedIdentity"] = marker);
        var error = Assert.Throws<Cp6ReleaseContractException>(() => EvidenceGraphInspection.Inspect(input.Candidate, input.Objects));
        Assert.Equal("graph-package-identity", error.Code);
        Assert.DoesNotContain(marker, error.ToString(), StringComparison.Ordinal);
    }

    private static void Reject(GraphInput input, string expected) =>
        Assert.Equal(expected, Assert.Throws<Cp6ReleaseContractException>(() =>
            EvidenceGraphInspection.Inspect(input.Candidate, input.Objects)).Code);
}
```

- [x] Run the focused tests; initially the new API is absent. Add only the following throwing scaffolds, then run again to demonstrate actual behavior failures rather than compilation errors.

### Throwing scaffold: tools/p10/ReleaseVerifier/EvidenceGraphInspection.cs

```csharp
namespace CP6.P10.ReleaseVerifier;

public static class EvidenceGraphInspection
{
    public static InspectedEvidenceGraph Inspect(ReadOnlyMemory<byte> candidate,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> objects) => throw new NotImplementedException();
}
```

### Throwing scaffold: tools/p10/ReleaseVerifier/InspectedEvidenceGraph.cs

```csharp
using System.Text.Json;

namespace CP6.P10.ReleaseVerifier;

public sealed class InspectedEvidenceGraph
{
    public string Sha256 => throw new NotImplementedException();
    public JsonElement Candidate => throw new NotImplementedException();
    public JsonElement Gate => throw new NotImplementedException();
    public IReadOnlyDictionary<string, InspectedEvidence> Evidence => throw new NotImplementedException();
    public int ObjectCount => throw new NotImplementedException();
    public bool CandidateAccepted => throw new NotImplementedException();
}

public sealed class InspectedEvidence
{
    public ContentAddress Payload => throw new NotImplementedException();
    public byte[] CopyPayloadBytes() => throw new NotImplementedException();
}
```

- [x] Execute the focused Red command from tools/p10. Expected: 74 tests fail with NotImplementedException, zero skipped. Any fixture/setup error must be corrected before implementation.

```powershell
if (-not $env:DOTNET_HOST_PATH -or -not (Test-Path -LiteralPath $env:DOTNET_HOST_PATH)) { throw 'Set DOTNET_HOST_PATH to the approved .NET 8 host.' }
$env:DOTNET_ROOT = Split-Path -Parent $env:DOTNET_HOST_PATH
$env:PATH = $env:DOTNET_ROOT + [IO.Path]::PathSeparator + $env:PATH
$env:P10_COSIGN_PATH = (Get-Command $env:P10_COSIGN_PATH -ErrorAction Stop).Source
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --configuration Release --filter FullyQualifiedName~EvidenceGraphInspectionTests
```

The inherited P10_COSIGN_PATH must identify the previously hash-verified cosign 3.1.3 executable. Do not download a second copy or introduce a skip when unavailable.

## Task 2: Minimal complete binding implementation

- [x] Replace the throwing scaffolds and create the two fixed-profile files with exactly this implementation.

### tools/p10/ReleaseVerifier/S06ReleaseIdentity.cs

```csharp
using System.Collections.Frozen;
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Release selection, not a copy of the Platform schema. A new release needs reviewed pins.
internal static class S06ReleaseIdentity
{
    internal const string Source = "3ff27e26962dcfd722887afb80a4306010dd9ee1";
    internal const string Version = "0.10.1";
    internal const string Signer = "1debfb8ff286ea51192b7f259d1ac823c105c4188eac40148598d37f0e20ff0d";
    internal const string PublicationHash = "8b5fc47fd77902a3433d61b4cf181b67ef61ee994b60ea8286a690ab5084c961";
    internal const string ProvenanceHash = "6885eee1db5075faeb1a321684f579ef853166a32db8af1cf861c34c79ec64a9";
    internal const string NuGetTrustHash = "da359e3a8e9be2220541c53613d2da277cb2bb9a22a8770df30c808a033b953f";
    internal const string CrmIndexHash = "1332bf21e4253112d457f86cdc0cba829fc58f20c3914fa738dd992d917e2914";
    internal const string CrmSource = "a31ca0e323418f7e4108cc6220c0f5fa132e7fc2";
    internal const string ImageRepository = "ghcr.io/gtx537/cp6-p10-verifier";
    internal const string ValidationPath = ".github/workflows/p10-platform-validation.yml";
    internal const string PublicationPath = ".github/workflows/p10-platform-candidate.yml";
    internal const string ReleasePackage = "CP6.Platform.Release";

    internal static readonly FrozenDictionary<string, string> PackageHashes = new Dictionary<string, string>
    {
        ["CP6.Platform.Abstractions"] = "1319912dac5c3e12a5bb4885e64d92f43a6057e96d2f6c11c05db97b9fc7f1f7",
        ["CP6.Platform.AspNetCore"] = "2949e2149ca2c98fa1c935b89f96ce2dcf388186daa038f8a20bbe1d57f02b71",
        ["CP6.Platform.Contracts"] = "e2c63dd160fe189db1fbc9090da38cab1d67bd599edd1333afc992a93f4110eb",
        ["CP6.Platform.Deployment"] = "a218d220bd42ada3928e45a6245558b7f0d3b96fd4ea3d9173886bbe7c91e6c0",
        ["CP6.Platform.EntityFramework"] = "90a341401f9058be2997e40f61c2dd5ad0fc4717c8b63672abc9009118fc5a71",
        ["CP6.Platform.Messaging"] = "7ffa23acf9c1623cbf520d8dd7fdff104a77abf07832bd4e89102092ab9f4558",
        [ReleasePackage] = "afe85eabe78965e0d7967f3f2cf54574c8adcb02141523aecd8a8bafc6d700e0"
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // Call only after the package has validated the complete candidate document.
    internal static void RequireCandidate(JsonElement root)
    {
        foreach (var package in root.GetProperty("packages").EnumerateArray())
        {
            var id = Text(package, "packageId");
            var expected = PackageHashes[id];
            Require(Text(package, "version") == Version &&
                Text(package, "sourceGitSha") == Source &&
                Text(package, "authorSignedPackageSha256") == expected &&
                Text(package, "publishedPackageSha256") == expected &&
                Text(package, "feedIdentity") == $"https://nuget.pkg.github.com/GTX537/index.json#{id}/{Version}" &&
                Text(package, "feedTransformation") == "BytePreserving" &&
                Text(package, "signerFingerprint") == Signer &&
                Text(package, "timestampPolicy") == "Rfc3161Required", "graph-package-identity");
        }
        var source = root.GetProperty("platformSource");
        Require(Text(source, "sourceGitSha") == Source &&
            Text(source, "subjectKind") == "SourceProvenance" &&
            Text(source, "subjectName") == "GTX537/CP6.Platform" &&
            Text(source, "sha256OrDigest").Length == 64, "graph-source-identity");
        var policy = root.GetProperty("policyVersions");
        Require(policy.GetProperty("trust").GetInt64() == 1 &&
            policy.GetProperty("evidence").GetInt64() == 1, "graph-policy");
        var verifier = root.GetProperty("verifier");
        var publisher = root.GetProperty("publisher");
        RequireWorkflow(verifier, "GTX537/CP6", ValidationPath, "p10-platform-candidate");
        RequireWorkflow(publisher, "GTX537/CP6", PublicationPath, "p10-platform-candidate");
        Require(Text(verifier, "commitSha") == Text(publisher, "commitSha") &&
            verifier.GetProperty("runId").GetInt64() != publisher.GetProperty("runId").GetInt64(), "graph-workflow-separation");
        var crm = root.GetProperty("crmConsumer");
        RequireWorkflow(crm, "GTX537/CP6.CRM", ".github/workflows/crm-validation.yml", "none");
        Require(Text(crm, "commitSha") == CrmSource &&
            Text(crm, "workflowFileSha") == "924014cb1231824a9b57ab82a6f9638f76329919" &&
            crm.GetProperty("runId").GetInt64() == 34134695003 &&
            crm.GetProperty("runAttempt").GetInt64() == 1, "graph-crm-identity");
        var images = root.GetProperty("images");
        Require(images.GetArrayLength() == 1, "graph-image-set");
        var image = images[0];
        Require(Text(image, "subjectKind") == "OciImage" &&
            Text(image, "subjectName") == ImageRepository &&
            Text(image, "sha256OrDigest").StartsWith("sha256:", StringComparison.Ordinal) &&
            Text(image, "sourceGitSha") == Text(verifier, "commitSha"), "graph-image-identity");
    }

    private static void RequireWorkflow(JsonElement value, string repository, string path, string environment) =>
        Require(Text(value, "repository") == repository && Text(value, "workflowPath") == path &&
            Text(value, "environment") == environment, "graph-workflow-identity");

    internal static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    internal static void Require(bool condition, string code)
    {
        if (!condition) throw new Cp6ReleaseContractException(code, "Evidence graph violates the fixed S06 binding profile.");
    }
}
```

### tools/p10/ReleaseVerifier/S06EvidenceBindings.cs

```csharp
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
```

### tools/p10/ReleaseVerifier/InspectedEvidenceGraph.cs

```csharp
using System.Collections.Frozen;
using System.Text.Json;

namespace CP6.P10.ReleaseVerifier;

// Immutable inspection output. Structural consistency is never candidate acceptance.
public sealed class InspectedEvidenceGraph
{
    internal InspectedEvidenceGraph(string sha256, JsonElement candidate, JsonElement gate,
        Dictionary<string, InspectedEvidence> evidence, int objectCount)
    {
        Sha256 = sha256;
        Candidate = candidate.Clone();
        Gate = gate.Clone();
        Evidence = evidence.ToFrozenDictionary(StringComparer.Ordinal);
        ObjectCount = objectCount;
    }

    public string Sha256 { get; }
    public JsonElement Candidate { get; }
    public JsonElement Gate { get; }
    public IReadOnlyDictionary<string, InspectedEvidence> Evidence { get; }
    public int ObjectCount { get; }
    public bool CandidateAccepted => false;
}

public sealed class InspectedEvidence
{
    private readonly byte[] _payloadBytes;

    internal InspectedEvidence(JsonElement record, ContentAddress payload, byte[] payloadBytes)
    {
        Record = record.Clone();
        Payload = payload;
        _payloadBytes = payloadBytes.ToArray();
    }

    public JsonElement Record { get; }
    public ContentAddress Payload { get; }
    public byte[] CopyPayloadBytes() => _payloadBytes.ToArray();
}
```

### tools/p10/ReleaseVerifier/EvidenceGraphInspection.cs

```csharp
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
```

- [x] Repeat the focused test command. Expected: 74 passed, zero failed/skipped.
- [x] Run the complete Release suite and whitespace verification from tools/p10 with the same SDK/cosign environment.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --configuration Release
dotnet format whitespace ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
```

Expected: existing 97 plus 74 new tests pass, zero skipped; formatting exits 0. If a genuine problem appears, investigate its cause, correct only task files and keep this plan synchronized.

## Task 3: Review and scoped commit

- [x] Review the entire diff against main, check no package/trust bytes, locks, workflows, runtime registrations, other repository files or root workspace edits changed.
- [x] Scan new files for private keys, credentials, machine paths, debug output and accidental acceptance flags. Public certificate/key metadata and immutable package hashes are intentional public inputs.
- [x] Confirm source/test files match the complete code blocks in this plan and record observed Red/Green results below.
- [ ] Stage only these eight task files and make a normal auditable commit.

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-07-p10-s06-evidence-graph.md tools/p10/ReleaseVerifier/S06ReleaseIdentity.cs tools/p10/ReleaseVerifier/S06EvidenceBindings.cs tools/p10/ReleaseVerifier/EvidenceGraphInspection.cs tools/p10/ReleaseVerifier/InspectedEvidenceGraph.cs tools/p10/ReleaseVerifier.Tests/EvidenceGraphFixture.cs tools/p10/ReleaseVerifier.Tests/EvidenceGraphBaseline.cs tools/p10/ReleaseVerifier.Tests/EvidenceGraphInspectionTests.cs
git diff --cached --check
git commit -m "feat(p10): bind candidate evidence graphs to the selected formal release"
```

- [ ] Continue full S06 implementation: actual NuGet/current trust/RFC3161, GitHub and CRM forward proof, OCI build/sign/SBOM/scan/provenance, pinned network I/O, real conditional R2 pre/post-commit verification, workflows and cross-repository audit. Merge/push completion and the four project ledgers belong to that full closure, not this non-accepting module alone.

## Self-review

The graph policy fills the parent contract's cross-document and required-gate requirements without copying schemas or treating unverified assertions as true. It does not purport to implement the entire approved parent design. Tests use the real formal validators and real historical public metadata; synthetic report/workflow/image data only prove the non-accepting inspection layer. No test-only acceptance entry point or caller-supplied trust-policy switch is added.

## Observed execution

Execution results are recorded after their commands finish; no remote S06 acceptance or publication is claimed here.
- Red: missing API produced the expected compile failures; after throwing scaffolds, the actual TRX contained 74 Failed results and every failure contained NotImplementedException, with no setup failures or skipped cases.
- Green: all 74 focused graph tests passed on the first complete implementation run.
- Full verification: 171 passed, 0 failed, 0 skipped in Release, followed by dotnet format whitespace --verify-no-changes exit 0. No dependency change or warning occurred.
- The seven source/test files were read back and compared to this plan's complete code blocks; all matched. Historical S04 baseline file blobs were independently compared against CRM bb1fd8b4f250fabde4476b6a450435de2d07c03f before embedding their exact bytes.
- Hygiene review found no private keys, tokens, machine-specific paths, console output or skipped tests in the new source/tests/plan. GitHub origin/main was fetched and remained the existing S06 baseline; all new changes are the eight listed task files.
- No Registry/R2 write, candidate authentication/acceptance claim, runtime registration, workflow change or overall P10 completion is associated with this inspection-only module.
- The initial staged Git whitespace check found one extra trailing blank line in five newly added files, caused by the file-creation patch ending with an empty added line. Only those trailing lines were removed. The staged check then passed, followed by a fresh full 171/171 Release pass and whitespace-format exit 0; no behavior changed.
