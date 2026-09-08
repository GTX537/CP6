using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Selected upstream-format unit vectors only. These are not scanner, image or release evidence.
public sealed class ImageReportTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ConfigDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Repository = "ghcr.io/gtx537/cp6-p10-verifier";
    private const string RootId = "SPDXRef-DocumentRoot-Image-verifier";

    [Fact]
    public void Native_SPDX_manifest_checksum_and_release_package_are_bound_without_reserialization()
    {
        var bytes = Bytes(Spdx());
        var result = SpdxImageReport.Read(bytes, Digest);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(bytes), result.Sha256);
        Assert.Equal(bytes.Length, result.ByteLength);
        Assert.Equal(1, result.PackageCount);
        Assert.Equal(DateTimeOffset.Parse("2026-09-08T00:00:00Z", CultureInfo.InvariantCulture), result.CreatedAtUtc);
        Assert.Contains("\n", Encoding.UTF8.GetString(bytes));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("license")]
    [InlineData("document-id")]
    [InlineData("name")]
    [InlineData("namespace-host")]
    [InlineData("namespace-path")]
    [InlineData("namespace-query")]
    [InlineData("tool")]
    [InlineData("creator")]
    [InlineData("time")]
    [InlineData("fraction")]
    [InlineData("no-packages")]
    [InlineData("root-only")]
    [InlineData("duplicate-package-id")]
    [InlineData("package-id")]
    [InlineData("package-name")]
    [InlineData("no-describes")]
    [InlineData("multiple-describes")]
    [InlineData("wrong-describes-source")]
    [InlineData("wrong-describes-target")]
    [InlineData("root-name")]
    [InlineData("root-version")]
    [InlineData("root-purpose")]
    [InlineData("root-hash")]
    [InlineData("config-not-manifest")]
    [InlineData("root-algorithm")]
    [InlineData("multiple-hashes")]
    [InlineData("release-name")]
    [InlineData("release-version")]
    public void Wrong_SPDX_document_tool_image_or_Release_identity_is_rejected(string mutation)
    {
        var root = Spdx();
        var image = root["packages"]![1]!;
        var release = root["packages"]![0]!;
        if (mutation == "version") root["spdxVersion"] = "SPDX-2.2";
        if (mutation == "license") root["dataLicense"] = "unreviewed";
        if (mutation == "document-id") root["SPDXID"] = "SPDXRef-Other";
        if (mutation == "name") root["name"] = Repository + ":latest";
        if (mutation == "namespace-host") root["documentNamespace"] = "https://example.invalid/syft/image/x";
        if (mutation == "namespace-path") root["documentNamespace"] = "https://anchore.com/syft/dir/x";
        if (mutation == "namespace-query") root["documentNamespace"] = "https://anchore.com/syft/image/x?query=1";
        if (mutation == "tool") root["creationInfo"]!["creators"]![1] = "Tool: syft-1.18.1";
        if (mutation == "creator") root["creationInfo"]!["creators"]![0] = "Organization: Unknown";
        if (mutation == "time") root["creationInfo"]!["created"] = "not-time";
        if (mutation == "fraction") root["creationInfo"]!["created"] = "2026-09-08T00:00:00.000Z";
        if (mutation == "no-packages") root["packages"] = new JsonArray();
        if (mutation == "root-only") root["packages"]!.AsArray().RemoveAt(0);
        if (mutation == "duplicate-package-id") release["SPDXID"] = RootId;
        if (mutation == "package-id") release["SPDXID"] = "not-spdx";
        if (mutation == "package-name") release["name"] = "";
        if (mutation == "no-describes") root["relationships"] = new JsonArray();
        if (mutation == "multiple-describes") root["relationships"]!.AsArray().Add(root["relationships"]![1]!.DeepClone());
        if (mutation == "wrong-describes-source") root["relationships"]![1]!["spdxElementId"] = "SPDXRef-Other";
        if (mutation == "wrong-describes-target") root["relationships"]![1]!["relatedSpdxElement"] = "SPDXRef-Other";
        if (mutation == "root-name") image["name"] = "ghcr.io/gtx537/cp6-api";
        if (mutation == "root-version") image["versionInfo"] = "latest";
        if (mutation == "root-purpose") image["primaryPackagePurpose"] = "FILE";
        if (mutation == "root-hash") image["checksums"]![0]!["checksumValue"] = new string('c', 64);
        if (mutation == "config-not-manifest") image["checksums"]![0]!["checksumValue"] = ConfigDigest[7..];
        if (mutation == "root-algorithm") image["checksums"]![0]!["algorithm"] = "SHA1";
        if (mutation == "multiple-hashes") image["checksums"]!.AsArray().Add(image["checksums"]![0]!.DeepClone());
        if (mutation == "release-name") release["name"] = "CP6.Platform.Testing";
        if (mutation == "release-version") release["versionInfo"] = "0.10.0";
        Assert.Throws<Cp6ReleaseContractException>(() => SpdxImageReport.Read(Bytes(root), Digest));
    }

    [Fact]
    public void Native_zero_finding_SARIF_is_not_confused_with_missing_scan_results()
    {
        var bytes = Bytes(Sarif());
        var result = SarifImageReport.Read(bytes, Digest, ConfigDigest);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(bytes), result.Sha256);
        Assert.Equal(bytes.Length, result.ByteLength);
        Assert.Equal(0, result.FindingCount);
        Assert.Equal(0, result.UnknownCount + result.LowCount + result.MediumCount);
    }

    [Fact]
    public void Full_lower_severity_results_and_native_CVSS_floats_are_preserved_and_counted()
    {
        var root = Sarif("UNKNOWN", "LOW", "MEDIUM");
        root["runs"]![0]!["tool"]!["driver"]!["rules"]![2]!["properties"]!["cvssv3_baseScore"] = 5.7;
        var bytes = Bytes(root);
        var result = SarifImageReport.Read(bytes, Digest, ConfigDigest);
        Assert.Equal(Cp6DeterministicJson.Sha256Hex(bytes), result.Sha256);
        Assert.Equal(3, result.FindingCount);
        Assert.Equal(1, result.UnknownCount);
        Assert.Equal(1, result.LowCount);
        Assert.Equal(1, result.MediumCount);
        Assert.Throws<Cp6ReleaseContractException>(() => Cp6DeterministicJson.Canonicalize(bytes));
    }

    [Theory]
    [InlineData("HIGH")]
    [InlineData("CRITICAL")]
    public void Existing_R2_high_and_critical_blocking_threshold_is_not_weakened(string severity)
    {
        Assert.Equal("image-vulnerability", Assert.Throws<Cp6ReleaseContractException>(() =>
            SarifImageReport.Read(Bytes(Sarif(severity)), Digest, ConfigDigest)).Code);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("schema")]
    [InlineData("no-runs")]
    [InlineData("multiple-runs")]
    [InlineData("tool-name")]
    [InlineData("tool-version")]
    [InlineData("tool-uri")]
    [InlineData("image-name")]
    [InlineData("image-config")]
    [InlineData("repo-digest")]
    [InlineData("multiple-digests")]
    [InlineData("host-path")]
    [InlineData("missing-results")]
    [InlineData("missing-rules")]
    [InlineData("duplicate-rule")]
    [InlineData("non-vulnerability")]
    [InlineData("unknown-severity")]
    [InlineData("wrong-tags")]
    [InlineData("rule-level")]
    [InlineData("unused-rule")]
    [InlineData("result-rule-id")]
    [InlineData("result-rule-index")]
    [InlineData("fractional-index")]
    [InlineData("result-level")]
    [InlineData("suppressed")]
    [InlineData("empty-message")]
    public void SARIF_scan_identity_rules_results_and_coverage_fail_closed(string mutation)
    {
        var root = Sarif("MEDIUM");
        var run = root["runs"]![0]!;
        var driver = run["tool"]!["driver"]!;
        var rule = driver["rules"]![0]!;
        var result = run["results"]![0]!;
        if (mutation == "version") root["version"] = "2.0.0";
        if (mutation == "schema") root["$schema"] = "https://example.invalid/schema";
        if (mutation == "no-runs") root["runs"] = new JsonArray();
        if (mutation == "multiple-runs") root["runs"]!.AsArray().Add(run.DeepClone());
        if (mutation == "tool-name") driver["name"] = "Unknown";
        if (mutation == "tool-version") driver["version"] = "0.58.2";
        if (mutation == "tool-uri") driver["informationUri"] = "https://example.invalid/scanner";
        if (mutation == "image-name") run["properties"]!["imageName"] = Repository + ":latest";
        if (mutation == "image-config") run["properties"]!["imageID"] = Digest;
        if (mutation == "repo-digest") run["properties"]!["repoDigests"]![0] = Repository + "@" + ConfigDigest;
        if (mutation == "multiple-digests") run["properties"]!["repoDigests"]!.AsArray().Add(Repository + "@" + Digest);
        if (mutation == "host-path") run["originalUriBaseIds"] = new JsonObject { ["ROOTPATH"] = "file:///tmp/scan/" };
        if (mutation == "missing-results") run.AsObject().Remove("results");
        if (mutation == "missing-rules") driver.AsObject().Remove("rules");
        if (mutation == "duplicate-rule") driver["rules"]!.AsArray().Add(rule.DeepClone());
        if (mutation == "non-vulnerability") rule["name"] = "Secret";
        if (mutation == "unknown-severity") rule["properties"]!["tags"]![2] = "NONE";
        if (mutation == "wrong-tags") rule["properties"]!["tags"]![0] = "misconfiguration";
        if (mutation == "rule-level") rule["defaultConfiguration"]!["level"] = "note";
        if (mutation == "unused-rule") run["results"] = new JsonArray();
        if (mutation == "result-rule-id") result["ruleId"] = "CVE-UNIT-OTHER";
        if (mutation == "result-rule-index") result["ruleIndex"] = 2;
        if (mutation == "fractional-index") result["ruleIndex"] = 0.5;
        if (mutation == "result-level") result["level"] = "note";
        if (mutation == "suppressed") result["suppressions"] = new JsonArray(new JsonObject { ["kind"] = "inSource" });
        if (mutation == "empty-message") result["message"]!["text"] = "";
        Assert.Throws<Cp6ReleaseContractException>(() => SarifImageReport.Read(Bytes(root), Digest, ConfigDigest));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 4194305)]
    [InlineData(false, 4194305)]
    public void Report_inputs_are_bounded_before_copy_or_parse(bool spdx, int length)
    {
        Assert.Equal("image-report-size", Assert.Throws<Cp6ReleaseContractException>(() =>
        {
            if (spdx) SpdxImageReport.Read(new byte[length], Digest);
            else SarifImageReport.Read(new byte[length], Digest, ConfigDigest);
        }).Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Duplicate_JSON_members_are_never_hidden_by_reserialization(bool spdx)
    {
        var json = Encoding.UTF8.GetString(Bytes(spdx ? Spdx() : Sarif()));
        var bytes = Encoding.UTF8.GetBytes("{\"duplicate\":1,\"duplicate\":2," + json[1..]);
        Assert.Equal("image-report-json", Assert.Throws<Cp6ReleaseContractException>(() =>
        {
            if (spdx) SpdxImageReport.Read(bytes, Digest);
            else SarifImageReport.Read(bytes, Digest, ConfigDigest);
        }).Code);
    }

    private static byte[] Bytes(JsonNode node) =>
        JsonSerializer.SerializeToUtf8Bytes(node, new JsonSerializerOptions { WriteIndented = true });

    private static JsonObject Spdx() => new()
    {
        ["spdxVersion"] = "SPDX-2.3",
        ["dataLicense"] = "CC0-1.0",
        ["SPDXID"] = "SPDXRef-DOCUMENT",
        ["name"] = Repository,
        ["documentNamespace"] = "https://anchore.com/syft/image/unit-vector",
        ["creationInfo"] = new JsonObject
        {
            ["creators"] = new JsonArray("Organization: Anchore, Inc", "Tool: syft-1.51.1"),
            ["created"] = "2026-09-08T00:00:00Z"
        },
        ["packages"] = new JsonArray(
            new JsonObject
            {
                ["name"] = "CP6.Platform.Release",
                ["SPDXID"] = "SPDXRef-Package-dotnet-Release",
                ["versionInfo"] = "0.10.1",
                ["downloadLocation"] = "NOASSERTION",
                ["filesAnalyzed"] = false
            },
            new JsonObject
            {
                ["name"] = Repository,
                ["SPDXID"] = RootId,
                ["versionInfo"] = Digest,
                ["primaryPackagePurpose"] = "CONTAINER",
                ["downloadLocation"] = "NOASSERTION",
                ["filesAnalyzed"] = false,
                ["checksums"] = new JsonArray(new JsonObject { ["algorithm"] = "SHA256", ["checksumValue"] = Digest[7..] })
            }),
        ["relationships"] = new JsonArray(
            new JsonObject
            {
                ["spdxElementId"] = RootId,
                ["relatedSpdxElement"] = "SPDXRef-Package-dotnet-Release",
                ["relationshipType"] = "CONTAINS"
            },
            new JsonObject
            {
                ["spdxElementId"] = "SPDXRef-DOCUMENT",
                ["relatedSpdxElement"] = RootId,
                ["relationshipType"] = "DESCRIBES"
            })
    };

    private static JsonObject Sarif(params string[] severities)
    {
        var rules = new JsonArray();
        var results = new JsonArray();
        for (var index = 0; index < severities.Length; index++)
        {
            var severity = severities[index];
            var level = severity is "HIGH" or "CRITICAL" ? "error" : severity == "MEDIUM" ? "warning" : "note";
            var id = "CVE-UNIT-" + index.ToString(CultureInfo.InvariantCulture);
            rules.Add(new JsonObject
            {
                ["id"] = id,
                ["name"] = "LanguageSpecificPackageVulnerability",
                ["properties"] = new JsonObject { ["tags"] = new JsonArray("vulnerability", "security", severity) },
                ["defaultConfiguration"] = new JsonObject { ["level"] = level }
            });
            results.Add(new JsonObject
            {
                ["ruleId"] = id,
                ["ruleIndex"] = index,
                ["level"] = level,
                ["message"] = new JsonObject { ["text"] = "Unit vector, not vulnerability evidence." }
            });
        }
        return new JsonObject
        {
            ["version"] = "2.1.0",
            ["$schema"] = "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/main/sarif-2.1/schema/sarif-schema-2.1.0.json",
            ["runs"] = new JsonArray(new JsonObject
            {
                ["tool"] = new JsonObject
                {
                    ["driver"] = new JsonObject
                    {
                        ["name"] = "Trivy",
                        ["fullName"] = "Trivy Vulnerability Scanner",
                        ["informationUri"] = "https://github.com/aquasecurity/trivy",
                        ["version"] = "0.74.0",
                        ["rules"] = rules
                    }
                },
                ["properties"] = new JsonObject
                {
                    ["imageName"] = Repository + "@" + Digest,
                    ["imageID"] = ConfigDigest,
                    ["repoDigests"] = new JsonArray(Repository + "@" + Digest),
                    ["repoTags"] = null
                },
                ["results"] = results,
                ["columnKind"] = "utf16CodeUnits"
            })
        };
    }
}
