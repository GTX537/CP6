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
