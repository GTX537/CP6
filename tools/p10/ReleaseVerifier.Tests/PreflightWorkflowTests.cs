using System.Text.RegularExpressions;

namespace CP6.P10.ReleaseVerifier.Tests;

// Source-wiring tests complement, but do not replace, the independently approved hosted run.
public sealed class PreflightWorkflowTests
{
    [Fact]
    public void Preflight_is_scoped_to_the_task_branch_and_separate_read_only_resource()
    {
        var text = Read();
        Assert.Contains("branches: [codex/p10-native-scan-preflight]", text);
        Assert.Contains("environment: P10_CRM_ACTIONS_READ_TOKEN", text);
        Assert.Contains("contents: read\n  actions: read\n  packages: read", text);
        Assert.Contains("runs-on: ubuntu-24.04", text);
        Assert.Contains("refs/heads/codex/p10-native-scan-preflight", text);
        Assert.Contains("\"$GITHUB_WORKFLOW_SHA\" == \"$GITHUB_SHA\"", text);
        Assert.Contains("persist-credentials: false", text);
        Assert.DoesNotContain("pull_request_target:", text);
        Assert.DoesNotContain("p10-platform-candidate", text);
        Assert.DoesNotContain("continue-on-error:", text);
        Assert.DoesNotContain("secrets.", text[..text.IndexOf("    steps:", StringComparison.Ordinal)]);
        foreach (Match match in Regex.Matches(text, @"uses: ([^\s]+)"))
            Assert.Matches(@"^[A-Za-z0-9-]+/[A-Za-z0-9-]+@[a-f0-9]{40}$", match.Groups[1].Value);
    }

    [Fact]
    public void Preflight_uses_the_pinned_tool_and_full_actual_input_suite_without_publication()
    {
        var text = Read();
        Assert.Contains("dotnet-version: " + ImageBuildProfile.SdkVersion, text);
        Assert.Contains("bash \"$GITHUB_WORKSPACE/eng/p10/cosign/build.sh\" \"$RUNNER_TEMP/p10-tools\"", text);
        Assert.Contains(ImageBuildProfile.CosignSha256, text);
        Assert.Contains("--locked-mode", text);
        Assert.Contains("CP6.P10.PreflightInputs.dll", text);
        Assert.Contains("P10_FORMAL_PACKAGE_ROOT:", text);
        Assert.Contains("dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-build --no-restore", text);
        Assert.DoesNotContain("--filter", text);
        Assert.DoesNotContain("docker ", text);
        Assert.DoesNotContain("PRIVATE_KEY", text);
        Assert.DoesNotContain("COSIGN_PASSWORD", text);
        Assert.DoesNotContain("P10_R2_", text);
        Assert.DoesNotContain("prepare-validation", text);
        Assert.DoesNotContain("finalize-validation", text);
        Assert.DoesNotContain("candidateAccepted=true", text);
        Assert.Equal(new[] { "P10_CRM_ACTIONS_READ_TOKEN" }, Regex.Matches(text, @"secrets\.(P10_[A-Z0-9_]+)")
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Preflight_retains_only_attempt_bound_test_results_not_private_input_packages()
    {
        var text = Read();
        Assert.Contains("name: p10-preflight-${{ github.sha }}-${{ github.run_id }}-${{ github.run_attempt }}", text);
        Assert.Contains("path: ${{ runner.temp }}/p10-test-results/p10-preflight.trx", text);
        Assert.Contains("if-no-files-found: error", text);
        Assert.Contains("overwrite: false", text);
        Assert.Contains("candidateAccepted=false; deployable=false", text);
    }

    [Fact]
    public void Input_collector_selects_exactly_the_existing_seven_package_identities()
    {
        var path = Path.Combine(Root(), "tools", "p10", "PreflightInputs", "Program.cs");
        Assert.True(File.Exists(path), "The read-only preflight collector is missing.");
        var text = File.ReadAllText(path);
        var ids = Regex.Matches(text, "\"(CP6\\.Platform\\.[A-Za-z]+)\"")
            .Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(S06ReleaseIdentity.PackageHashes.Keys.Order(StringComparer.Ordinal), ids);
        Assert.Contains("FormalPackageSource.DownloadAndVerifyAsync", text);
        Assert.Contains("FileMode.CreateNew", text);
        Assert.Contains("FileAttributes.ReparsePoint", text);
        Assert.Contains("PreflightInputsOnly", text);
        Assert.DoesNotContain("HttpClient", text);
        Assert.DoesNotContain("ValidationCommand", text);
    }

    private static string Read()
    {
        var path = Path.Combine(Root(), ".github", "workflows", "p10-platform-preflight.yml");
        Assert.True(File.Exists(path), "The independent hosted preflight workflow is missing.");
        return File.ReadAllText(path).Replace("\r\n", "\n");
    }

    private static string Root()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "eng", "p10", "verifier.Dockerfile")))
                return directory.FullName;
        throw new InvalidOperationException("The P10 source checkout is required.");
    }
}
