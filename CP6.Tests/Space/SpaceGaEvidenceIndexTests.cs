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
    public void Core_ga_index_is_complete_and_honestly_no_go()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var root = document.RootElement;

        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
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
            Assert.Equal("Pending", signer.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, signer.GetProperty("name").ValueKind);
            Assert.Empty(signer.GetProperty("evidence").EnumerateArray());
        });

        var inputs = root.GetProperty("externalInputs")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(
            new[]
            {
                "AUTHORIZED_GOLDEN_CAD_CANDIDATES",
                "PROVIDER_APPROVALS_AND_ISOLATED_WORKER",
                "TWO_PILOT_SITES_AND_WMS_WINDOWS",
            },
            inputs.Select(item => item.GetProperty("id").GetString()).ToArray());
        Assert.All(inputs, input =>
        {
            Assert.Equal("Pending", input.GetProperty("status").GetString());
            Assert.False(string.IsNullOrWhiteSpace(
                input.GetProperty("ownerRole").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(
                input.GetProperty("deadlineMilestone").GetString()));
            Assert.NotEmpty(input.GetProperty("evidenceFormat").EnumerateArray());
        });

        var gates = root.GetProperty("gates").EnumerateArray().ToArray();
        Assert.Equal(
            new[]
            {
                "WP0_BASELINE_AND_GOVERNANCE",
                "WP1_DESIGN_V1_MANUAL_MODELING",
                "WP2_CAD_START_WIZARD",
                "WP3_SITE_PRIMARY_BACKUP_PROVIDERS",
                "WP4_THREE_PATH_END_TO_END",
                "WP5_VIEWER_ACCESSIBILITY_AND_PERFORMANCE",
                "WP6_PUBLISH_WMS_SECURITY_AND_RECOVERY",
                "WP7_GOLDEN_CAD_FORMAL_EVIDENCE",
                "WP8_TWO_SITE_PILOT_AND_SIGNOFF",
            },
            gates.Select(item => item.GetProperty("id").GetString()).ToArray());
        Assert.All(gates, gate =>
        {
            Assert.True(gate.GetProperty("blocking").GetBoolean());
            Assert.Equal(
                "Pending",
                gate.GetProperty("acceptanceStatus").GetString());
            Assert.NotEmpty(gate.GetProperty("evidenceFormat").EnumerateArray());
            Assert.NotEmpty(
                gate.GetProperty("acceptanceCriteria").EnumerateArray());
            Assert.Empty(gate.GetProperty("acceptedEvidence").EnumerateArray());
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

        Assert.False(IsGaReady(root));
        Assert.Equal("NoGo", root.GetProperty("declaredStatus").GetString());
    }

    [Fact]
    public void Progress_cannot_reach_100_from_repository_completion_alone()
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
                "WP1_DESIGN_V1_MANUAL_MODELING",
                "WP2_CAD_START_WIZARD",
                "WP5_VIEWER_ACCESSIBILITY_AND_PERFORMANCE",
                "WP6_PUBLISH_WMS_SECURITY_AND_RECOVERY"
            ],
            implementationComplete);
        Assert.Contains(
            root.GetProperty("gates").EnumerateArray(),
            gate =>
                gate.GetProperty("id").GetString() ==
                    "WP1_DESIGN_V1_MANUAL_MODELING" &&
                gate.GetProperty("implementationStatus").GetString() ==
                    "Complete" &&
                gate.GetProperty("acceptanceStatus").GetString() ==
                    "Pending");
        Assert.False(IsGaReady(root));
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
