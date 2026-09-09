using System.Text.RegularExpressions;

namespace CP6.P10.ReleaseVerifier.Tests;

// Source-wiring regression checks, complemented by actionlint and actual protected-run acceptance.
// These are not a YAML parser and do not claim that any hosted tool or job executed successfully.
public sealed class S06WorkflowWiringTests
{
    [Theory]
    [InlineData("validation", "validate", "write")]
    [InlineData("candidate", "publish", "read")]
    public void Workflows_are_manual_single_hosted_jobs_with_pinned_actions_and_protected_scope(string kind, string job, string packages)
    {
        var text = Read(kind);
        Assert.Contains("  workflow_dispatch:", text);
        Assert.DoesNotMatch("(?m)^  (push|pull_request|pull_request_target|workflow_run):", text);
        Assert.Contains("jobs:\n  " + job + ":\n    runs-on: ubuntu-24.04", text);
        Assert.Contains("    environment: p10-platform-candidate", text);
        Assert.Contains("  contents: read\n  actions: read\n  packages: " + packages, text);
        Assert.Contains("  cancel-in-progress: false", text);
        Assert.Contains("          persist-credentials: false", text);
        Assert.DoesNotContain("continue-on-error:", text);
        Assert.DoesNotContain("if: always()", text);
        Assert.DoesNotContain("secrets.", text[..text.IndexOf("    steps:", StringComparison.Ordinal)]);
        Assert.DoesNotContain("runner.", text[..text.IndexOf("    steps:", StringComparison.Ordinal)]);
        foreach (Match match in Regex.Matches(text, @"uses: ([^\s]+)"))
            Assert.Matches(@"^[A-Za-z0-9-]+/[A-Za-z0-9-]+@[a-f0-9]{40}$", match.Groups[1].Value);
        Assert.Contains("dotnet-version: " + ImageBuildProfile.SdkVersion, text);
        Assert.Contains(ImageBuildProfile.CosignSha256, Step(text, "tools"));
        Assert.Contains("--locked-mode", text);
        var source = Step(text, "source");
        Assert.Contains("\"$EXPECTED_SHA\" == \"$GITHUB_SHA\"", source);
        Assert.Contains("gh api repos/GTX537/CP6/branches/main", source);
        Assert.Contains("\"$GITHUB_WORKFLOW_SHA\" == \"$GITHUB_SHA\"", source);
        Assert.DoesNotContain("r2-candidate", text);
        Assert.DoesNotContain("deploy/production", text);
    }

    [Fact]
    public void Validation_builds_once_and_runs_real_tests_scan_signature_and_container_verification_in_order()
    {
        var text = Read("validation");
        Ordered(text, "prepare", "tests", "context", "image", "scan", "sign", "finalize", "artifact");
        Assert.Single(Regex.Matches(text, "docker buildx build"));
        Assert.Contains("--output type=image,push=true,oci-mediatypes=true", Step(text, "image"));
        Assert.Contains("cp6-p10-verifier@$digest", Step(text, "image"));
        Assert.Contains("buildx-metadata.json", Step(text, "image"));
        Assert.Contains("P10_FORMAL_PACKAGE_ROOT:", Step(text, "tests"));
        Assert.Contains("dotnet test", Step(text, "tests"));
        Assert.DoesNotContain("--filter", Step(text, "tests"));
        Assert.Contains(ImageReportProfile.SyftLinuxArchiveSha256, Step(text, "tools"));
        Assert.Contains(ImageReportProfile.TrivyLinuxArchiveSha256, Step(text, "tools"));
        var scan = Step(text, "scan");
        Assert.Contains("--severity UNKNOWN,LOW,MEDIUM,HIGH,CRITICAL", scan);
        Assert.Contains(".properties.tags[2] != \"HIGH\" and .properties.tags[2] != \"CRITICAL\"", scan);
        Assert.Contains("spdx-json=", scan);
        var finalize = Step(text, "finalize");
        Assert.Contains("docker run --rm --read-only --cap-drop ALL --security-opt no-new-privileges", finalize);
        Assert.Contains("target=/stage,readonly", finalize);
        Assert.Contains("target=/images,readonly", finalize);
        Assert.Contains("finalize-validation /stage /images /out/artifact", finalize);
        Assert.DoesNotContain("docker.sock", finalize);
        Assert.DoesNotContain("secrets.", finalize);
        foreach (var name in S06CurrentWorkflowChecks.EnvironmentNames) Assert.Contains("--env " + name, finalize);
    }

    [Fact]
    public void Native_scanners_use_their_distinct_registry_source_syntax()
    {
        var scan = Step(Read("validation"), "scan");
        Assert.Contains("trivy\" image --image-src remote --scanners vuln", scan);
        Assert.DoesNotContain("--image-src registry", scan);
        Assert.Contains("syft\" \"registry:$image\"", scan);
        Assert.Contains("--severity UNKNOWN,LOW,MEDIUM,HIGH,CRITICAL --exit-code 0 --format sarif", scan);
        Assert.Contains(".properties.tags[2] != \"HIGH\" and .properties.tags[2] != \"CRITICAL\"", scan);
    }

    [Fact]
    public void Validation_secrets_are_limited_to_CRM_reads_and_the_separate_OCI_signing_step()
    {
        var text = Read("validation");
        Assert.DoesNotContain("P10_R2_", text);
        Assert.DoesNotContain("P10_LOCATOR_", text);
        Assert.Equal(new[] { "P10_CRM_ACTIONS_READ_TOKEN" }, Secrets(Step(text, "prepare")));
        Assert.Equal(new[] { "P10_CRM_ACTIONS_READ_TOKEN" }, Secrets(Step(text, "tests")));
        Assert.Equal(new[] { "P10_OCI_COSIGN_PASSWORD", "P10_OCI_COSIGN_PRIVATE_KEY" }, Secrets(Step(text, "sign")));
        Assert.Contains("--key env://P10_OCI_COSIGN_PRIVATE_KEY", Step(text, "sign"));
        Assert.Contains("--use-signing-config=false --tlog-upload=false --new-bundle-format=true", Step(text, "sign"));
        Assert.DoesNotContain("PRIVATE_KEY", Step(text, "context"));
    }

    [Fact]
    public void Publication_reuses_validation_and_commits_only_after_an_immutable_intent()
    {
        var text = Read("candidate");
        Ordered(text, "compile", "prepare", "sign", "bundle", "intent", "commit", "confirm");
        Assert.DoesNotContain("docker build", text);
        Assert.DoesNotContain("P10_OCI_", text);
        Assert.DoesNotContain("P10_CRM_", text);
        Assert.Contains("prepare-publication \"$RELEASE_TAG\"", Step(text, "prepare"));
        Assert.Contains("--key env://P10_LOCATOR_COSIGN_PRIVATE_KEY", Step(text, "sign"));
        Assert.Contains("store-publication-bundle", Step(text, "bundle"));
        Assert.Contains("${{ steps.intent.outputs.artifact-id }}", Step(text, "commit"));
        Assert.Contains("commit-publication \"$RELEASE_TAG\" \"$INTENT_ARTIFACT_ID\"", Step(text, "commit"));
        Assert.Contains("confirm-platform-published \"$RELEASE_TAG\"", Step(text, "confirm"));
        Assert.Equal(new[] { "P10_LOCATOR_COSIGN_PASSWORD", "P10_LOCATOR_COSIGN_PRIVATE_KEY" }, Secrets(Step(text, "sign")));
        Assert.Equal(new[] { "P10_R2_CONSUMER_ACCESS_KEY_ID", "P10_R2_CONSUMER_SECRET_ACCESS_KEY" }, Secrets(Step(text, "confirm")));
        Assert.DoesNotContain("P10_R2_PUBLISH", Step(text, "confirm"));
        Assert.DoesNotContain("COSIGN_PASSWORD", Step(text, "confirm"));
        Assert.Contains("Frozen/Consumable requires final cross-repository audit", Step(text, "summary"));
    }

    [Theory]
    [InlineData("validation", "artifact", "validation")]
    [InlineData("candidate", "intent", "intent")]
    public void Handoffs_use_exact_immutable_attempt_bound_names_and_fail_for_missing_files(string kind, string step, string purpose)
    {
        var text = Step(Read(kind), step);
        Assert.Contains("name: p10-s06-" + purpose + "-${{ github.sha }}-${{ github.run_id }}-${{ github.run_attempt }}", text);
        Assert.Contains("if-no-files-found: error", text);
        Assert.Contains("overwrite: false", text);
        Assert.Contains("retention-days: 90", text);
    }

    private static string Read(string kind)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "eng", "p10", "verifier.Dockerfile"))) continue;
            var path = Path.Combine(directory.FullName, ".github", "workflows", "p10-platform-" + kind + ".yml");
            Assert.True(File.Exists(path), "The P10 workflow implementation is missing.");
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }
        throw new InvalidOperationException("P10 source checkout is required for workflow-wiring tests.");
    }

    private static string Step(string text, string id)
    {
        var steps = Regex.Split(text, "(?m)(?=^      - name: )").Where(s => s.StartsWith("      - name:", StringComparison.Ordinal));
        return Assert.Single(steps, s => s.Contains("\n        id: " + id + "\n", StringComparison.Ordinal));
    }

    private static string[] Secrets(string step) => Regex.Matches(step, @"secrets\.(P10_[A-Z0-9_]+)")
        .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static void Ordered(string text, params string[] ids)
    {
        var previous = -1;
        foreach (var id in ids)
        {
            var current = text.IndexOf("\n        id: " + id + "\n", StringComparison.Ordinal);
            Assert.True(current > previous, "Workflow phases must preserve the declared order.");
            previous = current;
        }
    }
}
