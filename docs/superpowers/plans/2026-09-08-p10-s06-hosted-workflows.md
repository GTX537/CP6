# P10 S06 Hosted Workflows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans sequentially in this task. The user already selected no delegation.

**Goal:** Add the two actual main-only protected workflows for non-deployable validation and conditional candidate publication.

**Architecture:** A single hosted validate job restores pinned formal packages, collects actual inputs, runs the entire verifier suite, builds one runtime-only OCI manifest, produces native Syft/Trivy reports, signs its digest, then runs finalization inside that same image before uploading its immutable artifact. A separate hosted publish job on the same source consumes the completed validation, stores exact objects, signs/stores the Locator bundle, uploads immutable intent, invokes the isolated precommit inside commit-publication, and performs a fresh read-only postcheck. Existing WMS R2 workflows, Azure, trust keys and environment protections are untouched.

**Tech Stack:** GitHub Actions, ubuntu-24.04, pinned .NET 8.0.424, cosign 3.1.3, Syft 1.51.1, Trivy 0.74.0, GHCR/R2.

---

## Design and source checks

The approved prerequisites supersede the old design's shared-key/r2-candidate environment examples: the live environment is p10-platform-candidate with main-only branch policy, required reviewer GTX537, and separate OCI/Locator keys. The nine expected secret names were reconfirmed read-only on 2026-09-08; no values were read and no platform setting was changed.

The workflow grants packages:write only to validation, where the approved verifier image is built once. Publication has packages:read and compiles the same pinned-source CLI without rebuilding an image. Restore credentials are environment-only. Signing secrets occur only on signing steps; R2 parent/consumer pairs occur only on their required publication steps. The actual precommit child clears its inherited environment. Postconfirmation starts a separate CLI process with only the five P10 read inputs; no signing/publisher credential is job-global or present on that step.

Buildx provenance/SBOM index emission is disabled only in the new verifier lane to produce one linux/amd64 OCI manifest supported by the fixed reader. Native Buildx metadata, independent Syft SPDX and the complete native Trivy SARIF are instead bound by the implemented ImageProvenanceEvidence. Trivy emits all severities without truncation; jq rejects HIGH/CRITICAL before signing, and the strict C# SARIF reader independently enforces the same rule during actual container finalization and later consumption. This does not change the legacy R2 gates.

All native report bytes remain unchanged. The only canonicalized build input is CP6's three-field timestamp/digest control record. Cosign writes a new bundle only after a nonexistence check, uses env:// keys, and never receives private material via command arguments or disk. Its flags were checked against official v3.1.3 Sign/SignBlob and LoadTrustedMaterialAndSigningConfig implementations: key-only signing with --use-signing-config=false and --tlog-upload=false does not request Fulcio or load the public signing configuration. The already-approved pinned-key verification policy remains unchanged.

Actions commit pins were resolved from official repository v4/v3 refs on 2026-09-08. actionlint v1.7.12 is used locally to parse both workflows (Windows archive SHA-256 6e7241b51e6817ea6a047693d8e6fed13b31819c9a0dd6c5a726e1592d22f6e9). The xUnit source checks below are explicit regression guards, not a substitute YAML parser or actual runtime acceptance.

Sources: [cosign Sign](https://github.com/sigstore/cosign/blob/v3.1.3/cmd/cosign/cli/sign.go), [SignBlob](https://github.com/sigstore/cosign/blob/v3.1.3/cmd/cosign/cli/signblob.go), [signing configuration](https://github.com/sigstore/cosign/blob/v3.1.3/cmd/cosign/cli/signcommon/common.go), [actionlint release](https://github.com/rhysd/actionlint/releases/tag/v1.7.12).

## Files

The Docker configuration path is set through GITHUB_ENV only after the exact-source step runs on the Runner. GitHub does not allow runner context in a job-level env expression; actionlint and a source regression guard enforce this boundary. See [context availability](https://docs.github.com/en/actions/reference/workflows-and-actions/contexts#context-availability).

- Create .github/workflows/p10-platform-validation.yml.
- Create .github/workflows/p10-platform-candidate.yml.
- Create tools/p10/ReleaseVerifier.Tests/S06WorkflowWiringTests.cs.

## Task 1: Red wiring tests

- [ ] Add these exact tests before creating either workflow:

```csharp
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
```

- [ ] Run the focused suite; all seven cases must fail with the explicit missing-workflow assertion:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06WorkflowWiringTests
```

## Task 2: Implement the validation workflow

- [ ] Create the complete file:

```yaml
name: P10 Platform validation

on:
  workflow_dispatch:
    inputs:
      expected_sha:
        description: Exact current main commit to validate
        required: true
        type: string

permissions:
  contents: read
  actions: read
  packages: write

concurrency:
  group: p10-platform-validation
  cancel-in-progress: false

jobs:
  validate:
    runs-on: ubuntu-24.04
    environment: p10-platform-candidate
    timeout-minutes: 90
    defaults:
      run:
        shell: bash
        working-directory: tools/p10
    env:
      DOTNET_NOLOGO: "1"
      DOTNET_CLI_TELEMETRY_OPTOUT: "1"
    steps:
      - name: Checkout exact dispatch source
        id: checkout
        uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4
        with:
          ref: ${{ github.sha }}
          persist-credentials: false

      - name: Require exact main dispatch
        id: source
        env:
          EXPECTED_SHA: ${{ inputs.expected_sha }}
          GH_TOKEN: ${{ github.token }}
        run: |
          set -euo pipefail
          [[ "$GITHUB_EVENT_NAME" == workflow_dispatch && "$GITHUB_REF" == refs/heads/main ]]
          [[ "$EXPECTED_SHA" =~ ^[0-9a-f]{40}$ && "$EXPECTED_SHA" == "$GITHUB_SHA" ]]
          [[ "$GITHUB_WORKFLOW_SHA" == "$GITHUB_SHA" && "$(git rev-parse HEAD)" == "$GITHUB_SHA" ]]
          [[ "$(gh api repos/GTX537/CP6/branches/main --jq .commit.sha)" == "$GITHUB_SHA" ]]
          printf 'DOCKER_CONFIG=%s/p10-docker-config\n' "$RUNNER_TEMP" >> "$GITHUB_ENV"

      - name: Install pinned SDK
        id: sdk
        uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4
        with:
          dotnet-version: 8.0.424

      - name: Install digest-verified evidence tools
        id: tools
        run: |
          set -euo pipefail
          mkdir "$RUNNER_TEMP/p10-tools"
          curl --fail --location --proto '=https' --tlsv1.2 --retry 3 \
            https://github.com/sigstore/cosign/releases/download/v3.1.3/cosign-linux-amd64 \
            --output "$RUNNER_TEMP/p10-tools/cosign"
          printf '%s  %s\n' 4629c757b7618056f8ddd7e2625ae9fdd94c0372a65049520bc7d9df9efc7f71 \
            "$RUNNER_TEMP/p10-tools/cosign" | sha256sum --check --status
          curl --fail --location --proto '=https' --tlsv1.2 --retry 3 \
            https://github.com/anchore/syft/releases/download/v1.51.1/syft_1.51.1_linux_amd64.tar.gz \
            --output "$RUNNER_TEMP/p10-tools/syft.tar.gz"
          printf '%s  %s\n' 8fcb33017a0dc1058298c923c436d19dfa68ae93968e0b423248542e3afb9fc3 \
            "$RUNNER_TEMP/p10-tools/syft.tar.gz" | sha256sum --check --status
          tar -xzf "$RUNNER_TEMP/p10-tools/syft.tar.gz" -C "$RUNNER_TEMP/p10-tools" syft
          curl --fail --location --proto '=https' --tlsv1.2 --retry 3 \
            https://github.com/aquasecurity/trivy/releases/download/v0.74.0/trivy_0.74.0_Linux-64bit.tar.gz \
            --output "$RUNNER_TEMP/p10-tools/trivy.tar.gz"
          printf '%s  %s\n' 2ae6fe3ee734b7fdf11335663e18c75ea12dccc76062f09f164a3b0f8be4371a \
            "$RUNNER_TEMP/p10-tools/trivy.tar.gz" | sha256sum --check --status
          tar -xzf "$RUNNER_TEMP/p10-tools/trivy.tar.gz" -C "$RUNNER_TEMP/p10-tools" trivy
          chmod 0555 "$RUNNER_TEMP/p10-tools/cosign" "$RUNNER_TEMP/p10-tools/syft" "$RUNNER_TEMP/p10-tools/trivy"

      - name: Locked restore and build verifier
        id: restore
        env:
          NuGetPackageSourceCredentials_github: Username=GTX537;Password=${{ github.token }};ValidAuthenticationTypes=Basic
        run: |
          set -euo pipefail
          [[ "$(dotnet --version)" == 8.0.424 ]]
          dotnet restore ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --locked-mode \
            --configfile "$GITHUB_WORKSPACE/eng/p10/NuGet.formal.config" --packages "$RUNNER_TEMP/p10-nuget"
          dotnet build ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore -p:UseAppHost=false
          dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes

      - name: Collect actual public validation inputs
        id: prepare
        env:
          P10_GITHUB_READ_TOKEN: ${{ github.token }}
          P10_FEED_READ_TOKEN: ${{ github.token }}
          P10_CRM_READ_TOKEN: ${{ secrets.P10_CRM_ACTIONS_READ_TOKEN }}
        run: |
          set -euo pipefail
          dotnet ReleaseVerifier/bin/Release/net8.0/CP6.P10.ReleaseVerifier.dll \
            prepare-validation "$RUNNER_TEMP/p10-stage"

      - name: Run full verifier tests with real package and signature inputs
        id: tests
        env:
          P10_COSIGN_PATH: ${{ runner.temp }}/p10-tools/cosign
          P10_FORMAL_PACKAGE_ROOT: ${{ runner.temp }}/p10-stage/packages
          P10_GITHUB_READ_TOKEN: ${{ secrets.P10_CRM_ACTIONS_READ_TOKEN }}
          P10_FEED_READ_TOKEN: ${{ github.token }}
        run: |
          set -euo pipefail
          dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-build --no-restore \
            --logger "trx;LogFileName=p10-verifier.trx" --results-directory "$RUNNER_TEMP/p10-test-results"

      - name: Prepare runtime-only build context
        id: context
        run: |
          set -euo pipefail
          mkdir "$RUNNER_TEMP/p10-image-context" "$RUNNER_TEMP/p10-image-inputs"
          dotnet publish ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj -c Release --no-build --no-restore \
            -p:UseAppHost=false --output "$RUNNER_TEMP/p10-image-context/publish"
          cp "$GITHUB_WORKSPACE/eng/p10/verifier.Dockerfile" "$RUNNER_TEMP/p10-image-context/Dockerfile"
          printf '%s  %s\n' b2f21cd090f7ecc8d933f48858b6f4584fcc12fe502db4304798714ba1c22474 \
            "$RUNNER_TEMP/p10-image-context/Dockerfile" | sha256sum --check --status
          cp "$RUNNER_TEMP/p10-tools/cosign" "$RUNNER_TEMP/p10-image-context/cosign"

      - name: Set up hosted Buildx
        id: buildx
        uses: docker/setup-buildx-action@8d2750c68a42422c14e847fe6c8ac0403b4cbd6f # v3

      - name: Authenticate only the approved GHCR repository
        id: registry
        uses: docker/login-action@c94ce9fb468520275223c153574b00df6fe4bcc9 # v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ github.token }}

      - name: Build once and push the non-deployable verifier
        id: image
        run: |
          set -euo pipefail
          started="$(date -u +%Y-%m-%dT%H:%M:%S.%3NZ)"
          docker buildx build "$RUNNER_TEMP/p10-image-context" --platform linux/amd64 \
            --build-arg CP6_SOURCE_SHA="$GITHUB_SHA" --provenance=false --sbom=false \
            --output type=image,push=true,oci-mediatypes=true \
            --tag "ghcr.io/gtx537/cp6-p10-verifier:validation-$GITHUB_RUN_ID-$GITHUB_RUN_ATTEMPT" \
            --metadata-file "$RUNNER_TEMP/p10-image-inputs/buildx-metadata.json"
          completed="$(date -u +%Y-%m-%dT%H:%M:%S.%3NZ)"
          digest="$(jq -er '."containerimage.digest"' "$RUNNER_TEMP/p10-image-inputs/buildx-metadata.json")"
          [[ "$digest" =~ ^sha256:[0-9a-f]{64}$ ]]
          jq -n --arg imageDigest "$digest" --arg buildStartedAtUtc "$started" --arg buildCompletedAtUtc "$completed" \
            '{imageDigest:$imageDigest,buildStartedAtUtc:$buildStartedAtUtc,buildCompletedAtUtc:$buildCompletedAtUtc}' \
            > "$RUNNER_TEMP/p10-build-input.raw.json"
          dotnet ReleaseVerifier/bin/Release/net8.0/CP6.P10.ReleaseVerifier.dll canonicalize \
            "$RUNNER_TEMP/p10-build-input.raw.json" "$RUNNER_TEMP/p10-image-inputs/build-input.json"
          docker pull "ghcr.io/gtx537/cp6-p10-verifier@$digest"
          printf 'digest=%s\n' "$digest" >> "$GITHUB_OUTPUT"

      - name: Produce full native SBOM and vulnerability reports
        id: scan
        env:
          IMAGE_DIGEST: ${{ steps.image.outputs.digest }}
        run: |
          set -euo pipefail
          image="ghcr.io/gtx537/cp6-p10-verifier@$IMAGE_DIGEST"
          "$RUNNER_TEMP/p10-tools/syft" "registry:$image" -o "spdx-json=$RUNNER_TEMP/p10-image-inputs/spdx.json"
          "$RUNNER_TEMP/p10-tools/trivy" image --image-src registry --scanners vuln \
            --severity UNKNOWN,LOW,MEDIUM,HIGH,CRITICAL --exit-code 0 --format sarif \
            --output "$RUNNER_TEMP/p10-image-inputs/sarif.json" "$image"
          # Preserve all native findings; enforce HIGH/CRITICAL before signing and again in the verifier.
          jq -e 'all(.runs[0].tool.driver.rules[]; .properties.tags[2] != "HIGH" and .properties.tags[2] != "CRITICAL")' \
            "$RUNNER_TEMP/p10-image-inputs/sarif.json" > /dev/null

      - name: Sign the exact OCI digest with its separate protected key
        id: sign
        env:
          IMAGE_DIGEST: ${{ steps.image.outputs.digest }}
          P10_OCI_COSIGN_PRIVATE_KEY: ${{ secrets.P10_OCI_COSIGN_PRIVATE_KEY }}
          COSIGN_PASSWORD: ${{ secrets.P10_OCI_COSIGN_PASSWORD }}
        run: |
          set -euo pipefail
          [[ ! -e "$RUNNER_TEMP/p10-image-inputs/oci.sigstore.json" ]]
          producer="$RUNNER_TEMP/p10-stage/producer.json"
          signed="$(date -u +%Y-%m-%dT%H:%M:%S.%3NZ)"
          "$RUNNER_TEMP/p10-tools/cosign" sign --yes --key env://P10_OCI_COSIGN_PRIVATE_KEY \
            --use-signing-config=false --tlog-upload=false --new-bundle-format=true \
            --bundle "$RUNNER_TEMP/p10-image-inputs/oci.sigstore.json" \
            -a cp6.imageRepository=ghcr.io/gtx537/cp6-p10-verifier \
            -a "cp6.sourceGitSha=$GITHUB_SHA" -a cp6.workflowRepository=GTX537/CP6 \
            -a cp6.workflowPath=.github/workflows/p10-platform-validation.yml \
            -a "cp6.workflowFileSha=$(jq -er .workflowFileSha "$producer")" \
            -a "cp6.runId=$GITHUB_RUN_ID" -a "cp6.runAttempt=$GITHUB_RUN_ATTEMPT" \
            -a cp6.environment=p10-platform-candidate \
            -a cp6.signerKeyId=sha256:eb623d784fc55294e942fa49062477769a34943d5997fdbdd483ad0fb0103c21 \
            -a cp6.trustPolicyVersion=1 -a "cp6.signedAtUtc=$signed" -a cp6.deployable=false \
            "ghcr.io/gtx537/cp6-p10-verifier@$IMAGE_DIGEST"
          chmod 0644 "$RUNNER_TEMP/p10-image-inputs/oci.sigstore.json"

      - name: Verify inside the actual runtime image and assemble public evidence
        id: finalize
        env:
          IMAGE_DIGEST: ${{ steps.image.outputs.digest }}
          P10_GITHUB_READ_TOKEN: ${{ github.token }}
          P10_FEED_READ_TOKEN: ${{ github.token }}
        run: |
          set -euo pipefail
          mkdir "$RUNNER_TEMP/p10-validation-out"
          sudo chown 1654:1654 "$RUNNER_TEMP/p10-validation-out"
          docker run --rm --read-only --cap-drop ALL --security-opt no-new-privileges \
            --tmpfs /tmp:rw,nosuid,nodev,size=512m,mode=1777 \
            --mount "type=bind,source=$RUNNER_TEMP/p10-stage,target=/stage,readonly" \
            --mount "type=bind,source=$RUNNER_TEMP/p10-image-inputs,target=/images,readonly" \
            --mount "type=bind,source=$RUNNER_TEMP/p10-validation-out,target=/out" \
            --env HOME=/tmp --env DOTNET_CLI_HOME=/tmp --env DOTNET_NOLOGO=1 \
            --env P10_GITHUB_READ_TOKEN --env P10_FEED_READ_TOKEN \
            --env GITHUB_ACTIONS --env GITHUB_REPOSITORY --env GITHUB_REPOSITORY_ID --env GITHUB_REF \
            --env GITHUB_EVENT_NAME --env GITHUB_SHA --env GITHUB_WORKFLOW_REF --env GITHUB_WORKFLOW_SHA \
            --env GITHUB_RUN_ID --env GITHUB_RUN_ATTEMPT --env GITHUB_JOB --env GITHUB_SERVER_URL \
            --env GITHUB_API_URL --env RUNNER_ENVIRONMENT --env RUNNER_OS --env RUNNER_ARCH \
            "ghcr.io/gtx537/cp6-p10-verifier@$IMAGE_DIGEST" finalize-validation /stage /images /out/artifact

      - name: Upload immutable completed-validation handoff
        id: artifact
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4
        with:
          name: p10-s06-validation-${{ github.sha }}-${{ github.run_id }}-${{ github.run_attempt }}
          path: ${{ runner.temp }}/p10-validation-out/artifact/
          if-no-files-found: error
          overwrite: false
          retention-days: 90
          compression-level: 0

      - name: Record handoff identity without claiming publication
        id: summary
        env:
          ARTIFACT_ID: ${{ steps.artifact.outputs.artifact-id }}
          ARTIFACT_DIGEST: ${{ steps.artifact.outputs.artifact-digest }}
          IMAGE_DIGEST: ${{ steps.image.outputs.digest }}
        run: |
          set -euo pipefail
          printf 'P10 validation handoff: source=%s run=%s attempt=%s artifact=%s archive-sha256=%s image=%s\n' \
            "$GITHUB_SHA" "$GITHUB_RUN_ID" "$GITHUB_RUN_ATTEMPT" "$ARTIFACT_ID" "$ARTIFACT_DIGEST" "$IMAGE_DIGEST" \
            >> "$GITHUB_STEP_SUMMARY"
          printf 'Candidate publication and acceptance remain pending; deployable=false.\n' >> "$GITHUB_STEP_SUMMARY"
```

## Task 3: Implement the publication workflow

- [ ] Create the complete file:

```yaml
name: P10 Platform candidate publication

on:
  workflow_dispatch:
    inputs:
      expected_sha:
        description: Exact main commit used by the completed validation
        required: true
        type: string
      release_tag:
        description: New Platform candidate identity, not a Git tag or production release
        required: true
        type: string
      validation_run_id:
        description: Successful completed P10 validation run
        required: true
        type: string
      validation_attempt:
        description: Exact completed validation attempt
        required: true
        type: string
      validation_artifact_id:
        description: Immutable validation artifact ID
        required: true
        type: string

permissions:
  contents: read
  actions: read
  packages: read

concurrency:
  group: p10-platform-candidate
  cancel-in-progress: false

jobs:
  publish:
    runs-on: ubuntu-24.04
    environment: p10-platform-candidate
    timeout-minutes: 120
    defaults:
      run:
        shell: bash
        working-directory: tools/p10
    env:
      DOTNET_NOLOGO: "1"
      DOTNET_CLI_TELEMETRY_OPTOUT: "1"
      RELEASE_TAG: ${{ inputs.release_tag }}
    steps:
      - name: Checkout exact dispatch source
        id: checkout
        uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4
        with:
          ref: ${{ github.sha }}
          persist-credentials: false

      - name: Require exact main dispatch
        id: source
        env:
          EXPECTED_SHA: ${{ inputs.expected_sha }}
          GH_TOKEN: ${{ github.token }}
        run: |
          set -euo pipefail
          [[ "$GITHUB_EVENT_NAME" == workflow_dispatch && "$GITHUB_REF" == refs/heads/main ]]
          [[ "$EXPECTED_SHA" =~ ^[0-9a-f]{40}$ && "$EXPECTED_SHA" == "$GITHUB_SHA" ]]
          [[ "$GITHUB_WORKFLOW_SHA" == "$GITHUB_SHA" && "$(git rev-parse HEAD)" == "$GITHUB_SHA" ]]
          [[ "$(gh api repos/GTX537/CP6/branches/main --jq .commit.sha)" == "$GITHUB_SHA" ]]

      - name: Install pinned SDK
        id: sdk
        uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4
        with:
          dotnet-version: 8.0.424

      - name: Install digest-verified cosign
        id: tools
        run: |
          set -euo pipefail
          mkdir "$RUNNER_TEMP/p10-tools"
          curl --fail --location --proto '=https' --tlsv1.2 --retry 3 \
            https://github.com/sigstore/cosign/releases/download/v3.1.3/cosign-linux-amd64 \
            --output "$RUNNER_TEMP/p10-tools/cosign"
          printf '%s  %s\n' 4629c757b7618056f8ddd7e2625ae9fdd94c0372a65049520bc7d9df9efc7f71 \
            "$RUNNER_TEMP/p10-tools/cosign" | sha256sum --check --status
          chmod 0555 "$RUNNER_TEMP/p10-tools/cosign"

      - name: Compile pinned-source publisher without rebuilding the OCI image
        id: compile
        env:
          NuGetPackageSourceCredentials_github: Username=GTX537;Password=${{ github.token }};ValidAuthenticationTypes=Basic
        run: |
          set -euo pipefail
          [[ "$(dotnet --version)" == 8.0.424 ]]
          dotnet restore ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj --locked-mode \
            --configfile "$GITHUB_WORKSPACE/eng/p10/NuGet.formal.config" --packages "$RUNNER_TEMP/p10-nuget"
          dotnet publish ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj -c Release --no-restore \
            -p:UseAppHost=false --output "$RUNNER_TEMP/p10-publisher"

      - name: Verify completed validation and store exact public objects
        id: prepare
        env:
          VALIDATION_RUN: ${{ inputs.validation_run_id }}
          VALIDATION_ATTEMPT: ${{ inputs.validation_attempt }}
          VALIDATION_ARTIFACT: ${{ inputs.validation_artifact_id }}
          P10_COSIGN_PATH: ${{ runner.temp }}/p10-tools/cosign
          P10_GITHUB_READ_TOKEN: ${{ github.token }}
          P10_FEED_READ_TOKEN: ${{ github.token }}
          P10_R2_PUBLISH_ACCESS_KEY_ID: ${{ secrets.P10_R2_PUBLISH_ACCESS_KEY_ID }}
          P10_R2_PUBLISH_SECRET_ACCESS_KEY: ${{ secrets.P10_R2_PUBLISH_SECRET_ACCESS_KEY }}
        run: |
          set -euo pipefail
          dotnet "$RUNNER_TEMP/p10-publisher/CP6.P10.ReleaseVerifier.dll" prepare-publication "$RELEASE_TAG" \
            "$VALIDATION_RUN" "$VALIDATION_ATTEMPT" "$VALIDATION_ARTIFACT" "$RUNNER_TEMP/p10-publication-stage"

      - name: Sign exact Locator bytes with the separate protected key
        id: sign
        env:
          P10_LOCATOR_COSIGN_PRIVATE_KEY: ${{ secrets.P10_LOCATOR_COSIGN_PRIVATE_KEY }}
          COSIGN_PASSWORD: ${{ secrets.P10_LOCATOR_COSIGN_PASSWORD }}
        run: |
          set -euo pipefail
          [[ ! -e "$RUNNER_TEMP/p10-publication-stage/locator.sigstore.json" ]]
          "$RUNNER_TEMP/p10-tools/cosign" sign-blob --yes --key env://P10_LOCATOR_COSIGN_PRIVATE_KEY \
            --use-signing-config=false --tlog-upload=false --new-bundle-format=true \
            --bundle "$RUNNER_TEMP/p10-publication-stage/locator.sigstore.json" \
            "$RUNNER_TEMP/p10-publication-stage/locator.json"

      - name: Authenticate and store or reuse the fixed bundle
        id: bundle
        env:
          P10_COSIGN_PATH: ${{ runner.temp }}/p10-tools/cosign
          P10_GITHUB_READ_TOKEN: ${{ github.token }}
          P10_FEED_READ_TOKEN: ${{ github.token }}
          P10_R2_CONSUMER_ACCESS_KEY_ID: ${{ secrets.P10_R2_CONSUMER_ACCESS_KEY_ID }}
          P10_R2_CONSUMER_SECRET_ACCESS_KEY: ${{ secrets.P10_R2_CONSUMER_SECRET_ACCESS_KEY }}
          P10_R2_PUBLISH_ACCESS_KEY_ID: ${{ secrets.P10_R2_PUBLISH_ACCESS_KEY_ID }}
          P10_R2_PUBLISH_SECRET_ACCESS_KEY: ${{ secrets.P10_R2_PUBLISH_SECRET_ACCESS_KEY }}
        run: |
          set -euo pipefail
          dotnet "$RUNNER_TEMP/p10-publisher/CP6.P10.ReleaseVerifier.dll" store-publication-bundle \
            "$RELEASE_TAG" "$RUNNER_TEMP/p10-publication-stage" "$RUNNER_TEMP/p10-publication-intent"

      - name: Upload immutable publication intent
        id: intent
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4
        with:
          name: p10-s06-intent-${{ github.sha }}-${{ github.run_id }}-${{ github.run_attempt }}
          path: ${{ runner.temp }}/p10-publication-intent/
          if-no-files-found: error
          overwrite: false
          retention-days: 90
          compression-level: 0

      - name: Run isolated read-only precommit then conditionally create Locator
        id: commit
        env:
          INTENT_ARTIFACT_ID: ${{ steps.intent.outputs.artifact-id }}
          P10_COSIGN_PATH: ${{ runner.temp }}/p10-tools/cosign
          P10_GITHUB_READ_TOKEN: ${{ github.token }}
          P10_FEED_READ_TOKEN: ${{ github.token }}
          P10_R2_CONSUMER_ACCESS_KEY_ID: ${{ secrets.P10_R2_CONSUMER_ACCESS_KEY_ID }}
          P10_R2_CONSUMER_SECRET_ACCESS_KEY: ${{ secrets.P10_R2_CONSUMER_SECRET_ACCESS_KEY }}
          P10_R2_PUBLISH_ACCESS_KEY_ID: ${{ secrets.P10_R2_PUBLISH_ACCESS_KEY_ID }}
          P10_R2_PUBLISH_SECRET_ACCESS_KEY: ${{ secrets.P10_R2_PUBLISH_SECRET_ACCESS_KEY }}
        run: |
          set -euo pipefail
          dotnet "$RUNNER_TEMP/p10-publisher/CP6.P10.ReleaseVerifier.dll" commit-publication "$RELEASE_TAG" "$INTENT_ARTIFACT_ID"

      - name: Confirm published discovery in a new read-only process
        id: confirm
        env:
          P10_COSIGN_PATH: ${{ runner.temp }}/p10-tools/cosign
          P10_GITHUB_READ_TOKEN: ${{ github.token }}
          P10_FEED_READ_TOKEN: ${{ github.token }}
          P10_R2_CONSUMER_ACCESS_KEY_ID: ${{ secrets.P10_R2_CONSUMER_ACCESS_KEY_ID }}
          P10_R2_CONSUMER_SECRET_ACCESS_KEY: ${{ secrets.P10_R2_CONSUMER_SECRET_ACCESS_KEY }}
        run: |
          set -euo pipefail
          dotnet "$RUNNER_TEMP/p10-publisher/CP6.P10.ReleaseVerifier.dll" confirm-platform-published "$RELEASE_TAG"

      - name: Record pending external audit
        id: summary
        run: |
          set -euo pipefail
          printf 'P10 Platform candidate %s passed in-run postconfirmation.\n' "$RELEASE_TAG" >> "$GITHUB_STEP_SUMMARY"
          printf 'Normal verify-platform requires this workflow to complete. Frozen/Consumable requires final cross-repository audit. deployable=false.\n' \
            >> "$GITHUB_STEP_SUMMARY"
```

## Task 4: Verify and review before any external run

- [ ] From tools/p10 use the configured .NET 8 host and actual cosign/formal-package/GitHub/feed read inputs:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06WorkflowWiringTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
```

- [ ] Run the digest-verified actionlint 1.7.12 executable from the repo root with these arguments (shellcheck is unavailable on this Windows host):

```text
-shellcheck= .github/workflows/p10-platform-validation.yml .github/workflows/p10-platform-candidate.yml
```

Expected: seven wiring cases and the whole suite pass with zero skips; format/actionlint exit 0. Check all three plan blocks match the files, review native tool invocations and every secret-to-step mapping, ensure no private signing-key/credential file can be uploaded, and confirm existing R2/Azure files are unchanged.

- [ ] Record results and checkpoint only these four files:

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-08-p10-s06-hosted-workflows.md .github/workflows/p10-platform-validation.yml .github/workflows/p10-platform-candidate.yml tools/p10/ReleaseVerifier.Tests/S06WorkflowWiringTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): wire protected validation and candidate workflows"
```

Integration still requires the complete branch review, latest-main integration, required CI, a successful protected validation on the final immutable source, a separately approved publication on that same source, normal completed-workflow verification, and content-addressed audit/project-ledger updates. No automatic self-approval, Git tag push, production deployment, remote overwrite/delete or P10 completion claim is authorized by these source checks.

## Execution evidence — 2026-09-08

All four tasks above are implemented and locally verified. The seven wiring tests first failed with both workflow files absent. Test-only assertion/analyzer and secret-name regular-expression errors were corrected without weakening production assertions. Native actionlint then caught the invalid job-level `runner.temp` context; a regression assertion failed before moving the nonsecret Docker configuration path to the Runner source step. The final focused run passed 7/7; actionlint 1.7.12 and `dotnet format --verify-no-changes` exited 0. The fresh full Release suite after that correction passed 1,525/1,525 with zero failures and zero skips (2m12s).

All three embedded implementation blocks were compared with the actual source. Scoped review confirmed one image build, unchanged native reports, separate signing keys, step-local secrets, isolated precommit, a read-only postcheck, and no changes to existing R2/Azure workflows. These are local code/wiring results only: hosted Linux execution, GHCR/R2 publication, environment approval and final candidate audit remain pending.
