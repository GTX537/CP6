using System.Text.Json;

namespace CP6.Tests.Space;

public sealed class SpaceGaEvidenceIndexTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ManifestPath = Path.Combine(
        RepositoryRoot,
        "docs",
        "space",
        "acceptance",
        "v1.3-ga",
        "ga-evidence-index.json");

    [Fact]
    public void Core_ga_index_is_complete_and_honestly_ga_ready()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var root = document.RootElement;

        Assert.Equal(3, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            "CP6_SPACE_STUDIO_V1_CORE_GA",
            root.GetProperty("programId").GetString());
        Assert.Equal("CoreGA", root.GetProperty("scope").GetString());
        Assert.Equal(72, root.GetProperty("baselinePercent").GetInt32());
        Assert.Equal(100, root.GetProperty("gaPercent").GetInt32());

        var signers = root.GetProperty("signers").EnumerateArray().ToArray();
        Assert.Equal(
            new[] { "DeliveryOwner" },
            signers.Select(item => item.GetProperty("role").GetString())
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.All(signers, signer =>
        {
            Assert.Equal("Signed", signer.GetProperty("status").GetString());
            Assert.Equal("BUBAO.GAO", signer.GetProperty("name").GetString());
            Assert.NotEmpty(signer.GetProperty("evidence").EnumerateArray());
        });

        var inputs = root.GetProperty("externalInputs")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(
            new[]
            {
                "AUTHORIZED_GOLDEN_CAD_CANDIDATES",
                "PRIMARY_PROVIDER_AND_ISOLATED_WORKER",
            },
            inputs.Select(item => item.GetProperty("id").GetString()).ToArray());
        Assert.All(inputs, input =>
        {
            Assert.False(string.IsNullOrWhiteSpace(
                input.GetProperty("ownerRole").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(
                input.GetProperty("deadlineMilestone").GetString()));
            Assert.NotEmpty(input.GetProperty("evidenceFormat").EnumerateArray());
        });
        Assert.Equal("Complete", inputs[0].GetProperty("status").GetString());
        Assert.NotEmpty(inputs[0].GetProperty("evidence").EnumerateArray());
        Assert.Equal("Complete", inputs[1].GetProperty("status").GetString());
        Assert.NotEmpty(inputs[1].GetProperty("evidence").EnumerateArray());

        var gates = root.GetProperty("gates").EnumerateArray().ToArray();
        Assert.Equal(
            new[]
            {
                "WP0_BASELINE_AND_GOVERNANCE",
                "WP1_DESIGN_V1_MANUAL_MODELING",
                "WP2_CAD_START_WIZARD",
                "WP3_PRIMARY_PROVIDER_AND_ISOLATED_WORKER",
                "WP4_THREE_PATH_END_TO_END",
                "WP5_VIEWER_ACCESSIBILITY_AND_PERFORMANCE",
                "WP6_PUBLISH_WMS_SECURITY_AND_RECOVERY",
                "WP7_GOLDEN_CAD_FORMAL_EVIDENCE",
                "WP8_RELEASE_REHEARSAL_AND_SIGNOFF",
            },
            gates.Select(item => item.GetProperty("id").GetString()).ToArray());
        Assert.All(gates, gate =>
        {
            Assert.True(gate.GetProperty("blocking").GetBoolean());
            Assert.Equal(
                "Complete",
                gate.GetProperty("implementationStatus").GetString());
            Assert.Equal(
                "Accepted",
                gate.GetProperty("acceptanceStatus").GetString());
            Assert.NotEmpty(gate.GetProperty("evidenceFormat").EnumerateArray());
            Assert.NotEmpty(
                gate.GetProperty("acceptanceCriteria").EnumerateArray());
            Assert.NotEmpty(
                gate.GetProperty("acceptedEvidence").EnumerateArray());
            foreach (var path in gate.GetProperty("evidencePaths")
                         .EnumerateArray()
                         .Select(value => value.GetString()!))
            {
                Assert.False(Path.IsPathRooted(path), path);
                var resolved = Path.GetFullPath(Path.Combine(
                    RepositoryRoot,
                    path.Replace('/', Path.DirectorySeparatorChar)));
                Assert.StartsWith(
                    RepositoryRoot + Path.DirectorySeparatorChar,
                    resolved,
                    StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(resolved), resolved);
            }
        });

        Assert.True(IsGaReady(root));
        Assert.Equal("GaReady", root.GetProperty("declaredStatus").GetString());
    }

    [Fact]
    public void Progress_reaches_100_only_after_acceptance_and_signoff()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var root = document.RootElement;
        var implementationComplete = root.GetProperty("gates")
            .EnumerateArray()
            .Where(gate => gate.GetProperty("implementationStatus").GetString() ==
                "Complete")
            .Select(gate => gate.GetProperty("id").GetString()!)
            .ToArray();

        Assert.Equal(
            [
                "WP0_BASELINE_AND_GOVERNANCE",
                "WP1_DESIGN_V1_MANUAL_MODELING",
                "WP2_CAD_START_WIZARD",
                "WP3_PRIMARY_PROVIDER_AND_ISOLATED_WORKER",
                "WP4_THREE_PATH_END_TO_END",
                "WP5_VIEWER_ACCESSIBILITY_AND_PERFORMANCE",
                "WP6_PUBLISH_WMS_SECURITY_AND_RECOVERY",
                "WP7_GOLDEN_CAD_FORMAL_EVIDENCE",
                "WP8_RELEASE_REHEARSAL_AND_SIGNOFF"
            ],
            implementationComplete);
        Assert.All(
            root.GetProperty("gates").EnumerateArray(),
            gate => Assert.Equal(
                "Accepted",
                gate.GetProperty("acceptanceStatus").GetString()));
        Assert.True(IsGaReady(root));
        Assert.Contains(
            "every blocking result gate is Accepted",
            root.GetProperty("progressPolicy").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "single delivery owner is Signed",
            root.GetProperty("progressPolicy").GetString(),
            StringComparison.Ordinal);
    }

    private static bool IsGaReady(JsonElement root) =>
        root.GetProperty("kickoffDate").ValueKind == JsonValueKind.String &&
        root.GetProperty("targetGaDate").ValueKind == JsonValueKind.String &&
        root.GetProperty("externalInputs").EnumerateArray().All(input =>
            input.GetProperty("status").GetString() == "Complete") &&
        root.GetProperty("gates").EnumerateArray().All(gate =>
            !gate.GetProperty("blocking").GetBoolean() ||
            gate.GetProperty("acceptanceStatus").GetString() == "Accepted") &&
        root.GetProperty("signers").EnumerateArray().All(signer =>
            signer.GetProperty("status").GetString() == "Signed");

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "CP6.slnx")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the CP6 repository root.");
    }
}
