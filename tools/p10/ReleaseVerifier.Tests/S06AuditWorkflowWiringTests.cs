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
