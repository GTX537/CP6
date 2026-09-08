using System.Security.Cryptography;

namespace CP6.P10.ReleaseVerifier.Tests;

// Build-policy wiring tests; actual clean-room builds and native scans are separate evidence.
public sealed class CosignSecurityBuildTests
{
    [Theory]
    [InlineData("validation")]
    [InlineData("candidate")]
    [InlineData("audit")]
    public void Every_consumer_reproduces_and_checks_the_same_reviewed_security_build(string kind)
    {
        var text = File.ReadAllText(Path.Combine(Root(), ".github", "workflows", $"p10-platform-{kind}.yml"))
            .Replace("\r\n", "\n");
        Assert.Contains("bash \"$GITHUB_WORKSPACE/eng/p10/cosign/build.sh\" \"$RUNNER_TEMP/p10-tools\"", text);
        Assert.DoesNotContain("https://github.com/sigstore/cosign/releases/download/v3.1.3/cosign-linux-amd64", text);
        Assert.Contains(ImageBuildProfile.CosignSha256, text);
    }

    [Fact]
    public void Security_derivative_cannot_claim_the_original_upstream_binary_identity()
    {
        Assert.Equal("3.1.3-cp6.1", ImageBuildProfile.CosignVersion);
        Assert.NotEqual("4629c757b7618056f8ddd7e2625ae9fdd94c0372a65049520bc7d9df9efc7f71",
            ImageBuildProfile.CosignSha256);
    }

    [Fact]
    public void Recipe_pins_source_toolchain_and_locks_and_never_inherits_signing_credentials()
    {
        var path = Path.Combine(Root(), "eng", "p10", "cosign", "build.sh");
        Assert.True(File.Exists(path), "The reviewed reproducible security build recipe is required.");
        var compilerPath = Path.Combine(Root(), "eng", "p10", "cosign", "compile.sh");
        Assert.True(File.Exists(compilerPath), "The isolated compiler recipe is required.");
        var text = (File.ReadAllText(path) + File.ReadAllText(compilerPath)).Replace("\r\n", "\n");
        Assert.Contains("go1.26.8.linux-amd64.tar.gz", text);
        Assert.Contains("d0f743b33e8d8945e6b1f432edd15785c70507121d6e2a723b21285eddf8b57b", text);
        Assert.Contains("11926fa5bbbbde47e88fc006b625a17769b743b2", text);
        Assert.Contains("fbf05afe62db35ca00129ff65fab6f7ad8b7851ae1cf7dbb4b1789b6ccd65db2", text);
        Assert.Contains("h1:001JQRI/PJ/5T+g/kJ1KTvKFbb322+fomc+pHDZ/6sg=", text);
        Assert.Contains("env -i", text);
        Assert.Contains("GOTOOLCHAIN=local", text);
        Assert.Contains("GOSUMDB=sum.golang.org", text);
        Assert.Contains("GOENV=off", text);
        Assert.Contains("go mod verify", text);
        Assert.Contains("-mod=readonly", text);
        Assert.Contains("-trimpath", text);
        Assert.Contains("-buildvcs=false", text);
        Assert.Contains("-buildid=", text);
        Assert.Contains("lock-checksums.sha256", text);
        Assert.Contains("checksums.sha256", text);
        Assert.DoesNotContain("@latest", text);
        Assert.DoesNotContain("GOSUMDB=off", text);
        Assert.DoesNotContain("PRIVATE_KEY", text);
        Assert.DoesNotContain("COSIGN_PASSWORD", text);
    }

    [Fact]
    public void Reviewed_recipe_and_dependency_locks_are_bound_by_exact_checksums()
    {
        var profile = Path.Combine(Root(), "eng", "p10", "cosign");
        var lines = File.ReadAllLines(Path.Combine(profile, "lock-checksums.sha256"));
        var names = new[] { "go.mod", "go.sum", "compile.sh" };
        Assert.Equal(names.Length, lines.Length);
        foreach (var name in names)
        {
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(profile, name)))).ToLowerInvariant();
            Assert.Contains(hash + "  " + name, lines);
        }
    }

    [Fact]
    public void Frozen_binary_hashes_match_both_runtime_platform_pins()
    {
        var lines = File.ReadAllLines(Path.Combine(Root(), "eng", "p10", "cosign", "checksums.sha256"));
        Assert.Equal(2, lines.Length);
        Assert.Contains(ImageBuildProfile.CosignSha256 + "  cosign", lines);
        var windows = typeof(CosignBlobVerifier).GetField("WindowsSha256",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetRawConstantValue();
        Assert.Contains(windows + "  cosign-windows-amd64.exe", lines);
        Assert.NotEqual("9fe59be0eca1271873ce019061335eb1ac419b7059202e797828467ddabe33be", windows);
    }

    [Fact]
    public void Modified_tool_license_and_notice_are_included_in_the_runtime_build_context()
    {
        var context = File.ReadAllText(Path.Combine(Root(), ".github", "workflows", "p10-platform-validation.yml"));
        Assert.Contains("publish/THIRD-PARTY-NOTICES", context);
        Assert.Contains("p10-tools/cosign.LICENSE", context);
        Assert.Contains("p10-tools/cosign.NOTICE", context);
    }

    private static string Root()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "eng", "p10", "verifier.Dockerfile")))
                return directory.FullName;
        throw new InvalidOperationException("P10 source checkout is required for security-build tests.");
    }
}
