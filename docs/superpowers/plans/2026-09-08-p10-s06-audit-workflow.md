# P10 S06 Read-Only Audit Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans sequentially; the user selected no delegation.

**Goal:** Close the executable post-publication verification path without new image builds, R2 writes or automatic state promotion.

**Architecture:** A third manual protected job compiles the fixed-package reader from an exact main SHA, calls the existing normal verify-platform command after publication has completed, and retains its raw result under its SHA-256 filename in an immutable attempt-bound artifact. The eight-field CLI observation is evidence, not a Frozen transition. A later cross-repository audit must read the successful completed audit run, verify its artifact identity/hash, and append a content-addressed decision plus the four public ledgers.

**Tech Stack:** GitHub Actions, ubuntu-24.04, .NET 8.0.424, cosign 3.1.3, existing read-only verifier.

---

## Scope and rationale

This is the already-approved design sections 12.4 and 17 completion check. Publication cannot claim its own final conclusion; the normal reader explicitly queries both completed source-bound runs. No generic scheduling, approval bypass, new credentials, tag creation, remote object mutation or candidate rewrite is added. Audit packages/actions/contents permissions are read-only; the only Environment secrets referenced are the existing R2 consumer pair. The hosted audit retains the exact CLI stdout bytes, including its final newline, under their own SHA-256, not under the candidate SHA. The artifact/run metadata separately identifies this observer. Artifact retention is 90 days and is not permanent storage or Object Lock.

## Task 1: Add failing source-wiring tests

- [ ] Create tools/p10/ReleaseVerifier.Tests/S06AuditWorkflowWiringTests.cs with:

```csharp
using System.Text.RegularExpressions;

namespace CP6.P10.ReleaseVerifier.Tests;

// These source checks complement actionlint and real post-publication verification, not replace them.
public sealed class S06AuditWorkflowWiringTests
{
    [Fact]
    public void Audit_is_manual_exact_main_and_has_only_read_permissions_and_consumer_secrets()
    {
        var text = Read();
        Assert.Contains("  workflow_dispatch:", text);
        Assert.DoesNotMatch("(?m)^  (push|pull_request|pull_request_target|workflow_run):", text);
        Assert.Contains("  contents: read\n  actions: read\n  packages: read", text);
        Assert.DoesNotContain(": write", text);
        Assert.Contains("jobs:\n  verify:\n    runs-on: ubuntu-24.04", text);
        Assert.Contains("    environment: p10-platform-candidate", text);
        Assert.DoesNotContain("secrets.", text[..text.IndexOf("    steps:", StringComparison.Ordinal)]);
        Assert.DoesNotContain("runner.", text[..text.IndexOf("    steps:", StringComparison.Ordinal)]);
        var secrets = Regex.Matches(text, @"secrets\.(P10_[A-Z0-9_]+)")
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "P10_R2_CONSUMER_ACCESS_KEY_ID", "P10_R2_CONSUMER_SECRET_ACCESS_KEY" }, secrets);
        Assert.Contains("gh api repos/GTX537/CP6/branches/main", text);
        Assert.Contains("\"$EXPECTED_SHA\" == \"$GITHUB_SHA\"", text);
        Assert.Contains("\"$GITHUB_WORKFLOW_SHA\" == \"$GITHUB_SHA\"", text);
        Assert.Contains("persist-credentials: false", text);
        foreach (Match match in Regex.Matches(text, @"uses: ([^\s]+)"))
            Assert.Matches(@"^[A-Za-z0-9-]+/[A-Za-z0-9-]+@[a-f0-9]{40}$", match.Groups[1].Value);
    }

    [Fact]
    public void Audit_runs_normal_completed_verification_without_building_an_image_or_publishing()
    {
        var text = Read();
        Assert.Contains("dotnet-version: " + ImageBuildProfile.SdkVersion, text);
        Assert.Contains(ImageBuildProfile.CosignSha256, text);
        Assert.Contains("--locked-mode", text);
        Assert.Contains("dotnet publish ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj", text);
        Assert.Contains("verify-platform \"$RELEASE_TAG\"", text);
        Assert.DoesNotContain("confirm-platform-", text);
        Assert.DoesNotContain("docker ", text);
        Assert.DoesNotContain("cosign sign", text);
        Assert.DoesNotContain("prepare-publication", text);
        Assert.DoesNotContain("commit-publication", text);
        Assert.DoesNotContain("P10_CRM_", text);
        Assert.DoesNotContain("continue-on-error:", text);
        Assert.DoesNotContain("if: always()", text);
    }

    [Fact]
    public void Audit_retains_only_hash_named_readonly_result_and_never_claims_automatic_frozen()
    {
        var text = Read();
        Assert.Contains("sha256sum \"$result\"", text);
        Assert.Contains("mv \"$result\" \"$RUNNER_TEMP/p10-audit/$digest.json\"", text);
        Assert.Contains("name: p10-s06-audit-${{ github.sha }}-${{ github.run_id }}-${{ github.run_attempt }}", text);
        Assert.Contains("path: ${{ runner.temp }}/p10-audit/", text);
        Assert.Contains("if-no-files-found: error", text);
        Assert.Contains("overwrite: false", text);
        Assert.Contains("retention-days: 90", text);
        Assert.Contains("Frozen remains a separate cross-repository audit/ledger decision; deployable=false.", text);
        Assert.True(text.IndexOf("verify-platform", StringComparison.Ordinal) < text.IndexOf("actions/upload-artifact@", StringComparison.Ordinal));
    }

    private static string Read()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "eng", "p10", "verifier.Dockerfile"))) continue;
            var path = Path.Combine(directory.FullName, ".github", "workflows", "p10-platform-audit.yml");
            Assert.True(File.Exists(path), "The P10 read-only audit workflow is missing.");
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }
        throw new InvalidOperationException("P10 source checkout is required for audit-wiring tests.");
    }
}
```

- [ ] From tools/p10 run the pinned .NET 8 host: dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06AuditWorkflowWiringTests.
Expected: three assertion failures because the audit YAML is absent; no compiler/analyzer errors.

## Task 2: Add the read-only hosted workflow

- [ ] Create .github/workflows/p10-platform-audit.yml with:

```yaml
name: P10 Platform candidate read-only audit

on:
  workflow_dispatch:
    inputs:
      expected_sha:
        description: Exact main source of this read-only verifier
        required: true
        type: string
      release_tag:
        description: Existing Platform candidate whose publication has completed
        required: true
        type: string

permissions:
  contents: read
  actions: read
  packages: read

concurrency:
  group: p10-platform-audit
  cancel-in-progress: false

jobs:
  verify:
    runs-on: ubuntu-24.04
    environment: p10-platform-candidate
    timeout-minutes: 45
    defaults:
      run:
        shell: bash
        working-directory: tools/p10
    env:
      DOTNET_NOLOGO: "1"
      DOTNET_CLI_TELEMETRY_OPTOUT: "1"
    steps:
      - name: Checkout exact dispatch source
        uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4
        with:
          ref: ${{ github.sha }}
          persist-credentials: false

      - name: Require exact main dispatch
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
        uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9 # v4
        with:
          dotnet-version: 8.0.424

      - name: Install digest-verified cosign
        run: |
          set -euo pipefail
          mkdir "$RUNNER_TEMP/p10-tools"
          curl --fail --location --proto '=https' --tlsv1.2 --retry 3 \
            https://github.com/sigstore/cosign/releases/download/v3.1.3/cosign-linux-amd64 \
            --output "$RUNNER_TEMP/p10-tools/cosign"
          printf '%s  %s\n' 4629c757b7618056f8ddd7e2625ae9fdd94c0372a65049520bc7d9df9efc7f71 \
            "$RUNNER_TEMP/p10-tools/cosign" | sha256sum --check --status
          chmod 0555 "$RUNNER_TEMP/p10-tools/cosign"

      - name: Compile the pinned-source reader
        env:
          NuGetPackageSourceCredentials_github: Username=GTX537;Password=${{ github.token }};ValidAuthenticationTypes=Basic
        run: |
          set -euo pipefail
          [[ "$(dotnet --version)" == 8.0.424 ]]
          dotnet restore ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj --locked-mode \
            --configfile "$GITHUB_WORKSPACE/eng/p10/NuGet.formal.config" --packages "$RUNNER_TEMP/p10-nuget"
          dotnet publish ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj -c Release --no-restore \
            -p:UseAppHost=false --output "$RUNNER_TEMP/p10-reader"

      - name: Verify completed publication through authoritative discovery
        id: verify
        env:
          RELEASE_TAG: ${{ inputs.release_tag }}
          P10_COSIGN_PATH: ${{ runner.temp }}/p10-tools/cosign
          P10_GITHUB_READ_TOKEN: ${{ github.token }}
          P10_FEED_READ_TOKEN: ${{ github.token }}
          P10_R2_CONSUMER_ACCESS_KEY_ID: ${{ secrets.P10_R2_CONSUMER_ACCESS_KEY_ID }}
          P10_R2_CONSUMER_SECRET_ACCESS_KEY: ${{ secrets.P10_R2_CONSUMER_SECRET_ACCESS_KEY }}
        run: |
          set -euo pipefail
          mkdir "$RUNNER_TEMP/p10-audit"
          result="$RUNNER_TEMP/p10-audit/result.json"
          dotnet "$RUNNER_TEMP/p10-reader/CP6.P10.ReleaseVerifier.dll" verify-platform "$RELEASE_TAG" > "$result"
          digest="$(sha256sum "$result" | cut -d ' ' -f 1)"
          [[ "$digest" =~ ^[0-9a-f]{64}$ ]]
          mv "$result" "$RUNNER_TEMP/p10-audit/$digest.json"
          printf 'verification_result_sha256=%s\n' "$digest" >> "$GITHUB_OUTPUT"

      - name: Retain immutable content-addressed verification result
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4
        with:
          name: p10-s06-audit-${{ github.sha }}-${{ github.run_id }}-${{ github.run_attempt }}
          path: ${{ runner.temp }}/p10-audit/
          if-no-files-found: error
          overwrite: false
          retention-days: 90
          compression-level: 0

      - name: Record audit observation boundary
        env:
          RESULT_SHA256: ${{ steps.verify.outputs.verification_result_sha256 }}
        run: |
          set -euo pipefail
          printf 'Completed candidate verification result SHA-256: %s\n' "$RESULT_SHA256" >> "$GITHUB_STEP_SUMMARY"
          printf 'Frozen remains a separate cross-repository audit/ledger decision; deployable=false.\n' >> "$GITHUB_STEP_SUMMARY"
```

## Task 3: Verify and checkpoint

- [ ] Run the focused command, then the entire Release suite with the same actual formal-package/cosign/GitHub/feed inputs as the protected validation suite. Expected: three audit-wiring cases and the full suite pass, no skips.
- [ ] Run dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes from tools/p10. Expected: exit 0.
- [ ] Run digest-verified actionlint 1.7.12 from the repository root with -shellcheck= and the three exact .github/workflows/p10-platform-{validation,candidate,audit}.yml paths. Expected: exit 0; Windows has no shellcheck.
- [ ] Compare both code blocks to their files, inspect the full scoped diff and scan for private material.
- [ ] Run git diff --check; stage only this plan, the audit YAML and its test; inspect git diff --cached --check and --stat; commit with feat(p10): add read-only completed candidate audit.

No actual workflow success, credential access, object presence or Frozen state is inferred from these source tests. Integration and owner approvals remain separate.

## Execution evidence — 2026-09-08

All tasks were implemented sequentially. RED: all three new tests failed because the workflow did not exist. GREEN: all three passed; formatting and actionlint on all three P10 workflows exited 0. After the ordinary merge of current origin/main (c93f8608), the full Release suite passed 1,528/1,528, zero failures/skips, in 47 seconds. Both embedded code blocks match the checked-in files. Review confirmed read-only scopes, the existing consumer pair only, normal completed-workflow verification and no image build, signing, R2 mutation or automatic Frozen decision. Hosted execution and final ledger acceptance are still pending.
