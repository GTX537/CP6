# P10 S06 Native Image Report Binding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans sequentially in this task. The user selected no delegation.

**Goal:** Bind native SPDX/SARIF bytes to the exact approved image and tools, require the Release package in the SBOM, and block HIGH/CRITICAL vulnerabilities.

**Architecture:** Add selected-field report checks, not parallel SPDX/SARIF schema implementations. Preserve raw bytes and normal JSON numbers while rejecting duplicate members and excessive inputs. SPDX checks the scanner's native manifest checksum; SARIF checks native image name, repo digest and config ID, validates rule/result coverage and applies the existing R2 severity threshold.

**Tech Stack:** .NET 8.0.424, CP6.Platform.Release [0.10.1], Syft 1.51.1 SPDX 2.3, Trivy 0.74.0 SARIF 2.1.0, xUnit.

---

## Scope and source decisions

No Docker build or scan runs on the user's SQL/development PC in this module. It neither publishes an image/report nor changes existing R2 workflow tools or gates. Pure selected-field vectors demonstrate parser behavior only; they are not valid formal release evidence, scanner execution evidence or a candidate acceptance capability. The future protected GitHub-hosted validation must actually scan the one approved ghcr.io/gtx537/cp6-p10-verifier digest and bind these exact raw report hashes to the completed gate and provenance.

Official releases resolved on 2026-09-08 UTC:

- Syft v1.51.1, published 2026-08-27, source 91a0032987d91b7411b52f6f5c185c5e7f775495. Linux amd64 archive SHA-256 8fcb33017a0dc1058298c923c436d19dfa68ae93968e0b423248542e3afb9fc3.
- Trivy v0.74.0, published 2026-08-14, source e1fd17a0ea4a8cf24bc4b4dd7e2cfbf4bb31b994. Linux 64-bit archive SHA-256 2ae6fe3ee734b7fdf11335663e18c75ea12dccc76062f09f164a3b0f8be4371a.

Checked primary implementations:

- [Syft SPDX model](https://github.com/anchore/syft/blob/91a0032987d91b7411b52f6f5c185c5e7f775495/syft/format/common/spdxhelpers/to_format_model.go): the root CONTAINER checksum uses ManifestDigest, not the config ID. DESCRIBES is a relationship. Creator time has UTC seconds.
- [Syft image golden](https://github.com/anchore/syft/blob/91a0032987d91b7411b52f6f5c185c5e7f775495/syft/format/spdxjson/testdata/snapshot/TestSPDXJSONImageEncoder.golden) and [version build flags](https://github.com/anchore/syft/blob/91a0032987d91b7411b52f6f5c185c5e7f775495/.goreleaser.yaml).
- [Trivy SARIF writer](https://github.com/aquasecurity/trivy/blob/e1fd17a0ea4a8cf24bc4b4dd7e2cfbf4bb31b994/pkg/report/sarif.go): container reports include imageName, imageID, repoDigests and repoTags. Severity is the third rule tag; HIGH/CRITICAL maps to error. A container scan has no host originalUriBaseIds.
- [Trivy SARIF tests](https://github.com/aquasecurity/trivy/blob/e1fd17a0ea4a8cf24bc4b4dd7e2cfbf4bb31b994/pkg/report/sarif_test.go): zero findings use explicit empty rules/results arrays.

The future Syft invocation must set source-name to the fixed repository and source-version to the exact digest, while its native manifest checksum must independently agree. The future Trivy invocation must read the registry by digest, emit all vulnerability severities with no ignores/suppressions, and preserve its native report. Reporting all lower severities does not weaken the HIGH/CRITICAL failure threshold already used in r2-candidate.yml. Report hashes are over untouched bytes, including floating-point CVSS values; these third-party reports are never CP6-canonicalized.

Full SBOM schema conformance and scanner execution remain the upstream tools' responsibility and are checked through the actual pinned tool invocation in the completed validation workflow. This module checks the release-specific bindings consumed by CP6. It does not claim that a syntactically plausible empty report proves a clean image.

## File map

- Create tools/p10/ReleaseVerifier/ImageReportProfile.cs: selected tool versions/archive identities, shared bounded native JSON/error helpers.
- Create tools/p10/ReleaseVerifier/SpdxImageReport.cs: SPDX image, tool, package and timestamp observations.
- Create tools/p10/ReleaseVerifier/SarifImageReport.cs: SARIF image/config, rule/result coverage and severity checks.
- Create tools/p10/ReleaseVerifier.Tests/ImageReportTests.cs: 66 selected-field component cases, including honest zero findings and native floats.
- Create this plan.

## Task 1: Observe missing-implementation failures

- [x] Add the complete tests first.

```csharp
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
```

- [x] Add only throwing Read declarations and observation records, then observe all 66 cases fail because implementation is absent.

Run from tools/p10 with the established SDK 8.0.424 and locked restore assets:

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ImageReportTests --logger "trx;LogFileName=image-reports-red.trx" --results-directory ../../artifacts/p10/image-reports-red
```

## Task 2: Implement native report bindings

- [x] Add the following three complete implementations.

### tools/p10/ReleaseVerifier/ImageReportProfile.cs

```csharp
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Selected upstream tool releases, separate from the unchanged existing R2 workflow versions.
internal static class ImageReportProfile
{
    internal const string SyftVersion = "1.51.1";
    internal const string SyftLinuxArchiveSha256 = "8fcb33017a0dc1058298c923c436d19dfa68ae93968e0b423248542e3afb9fc3";
    internal const string TrivyVersion = "0.74.0";
    internal const string TrivyLinuxArchiveSha256 = "2ae6fe3ee734b7fdf11335663e18c75ea12dccc76062f09f164a3b0f8be4371a";

    internal static JsonElement Read(ReadOnlyMemory<byte> bytes)
    {
        try { return GitHubApiJson.Parse(bytes); }
        catch (Exception) { throw Error("image-report-json"); }
    }

    internal static void Require(bool condition, string code)
    {
        if (!condition) throw Error(code);
    }

    internal static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;

    internal static Cp6ReleaseContractException Error(string code) =>
        new(code, "Image report violates the selected S06 tool, identity or finding profile.");
}
```

### tools/p10/ReleaseVerifier/SpdxImageReport.cs

```csharp
using System.Globalization;
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.ImageReportProfile;

namespace CP6.P10.ReleaseVerifier;

// Parses native third-party bytes, not a claim that syft ran or that the image is acceptable.
internal static class SpdxImageReport
{
    internal static SpdxImageObservation Read(ReadOnlyMemory<byte> bytes, string imageDigest)
    {
        OciWirePolicy.RequireDigest(imageDigest);
        if (bytes.Length is < 1 or > 4 * 1024 * 1024) throw Error("image-report-size");
        var owned = bytes.ToArray();
        var root = ImageReportProfile.Read(owned);
        try
        {
            Require(Text(root, "spdxVersion") == "SPDX-2.3" && Text(root, "dataLicense") == "CC0-1.0" &&
                Text(root, "SPDXID") == "SPDXRef-DOCUMENT" && Text(root, "name") == S06ReleaseIdentity.ImageRepository,
                "spdx-document-binding");
            var space = Text(root, "documentNamespace");
            Require(Uri.TryCreate(space, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
                uri.Host == "anchore.com" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
                uri.AbsolutePath.StartsWith("/syft/image/", StringComparison.Ordinal), "spdx-document-binding");
            var creation = root.GetProperty("creationInfo");
            Require(creation.GetProperty("creators").EnumerateArray().Select(v => v.GetString()).SequenceEqual(
                new[] { "Organization: Anchore, Inc", "Tool: syft-" + SyftVersion }, StringComparer.Ordinal), "spdx-tool");
            Require(DateTimeOffset.TryParseExact(Text(creation, "created"), "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var created) && created >= DateTimeOffset.UnixEpoch, "spdx-time");
            var packages = root.GetProperty("packages").EnumerateArray().ToArray();
            Require(packages.Length >= 2 && packages.Select(p => Text(p, "SPDXID"))
                .Distinct(StringComparer.Ordinal).Count() == packages.Length &&
                packages.All(p => Text(p, "SPDXID").StartsWith("SPDXRef-", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(Text(p, "name"))), "spdx-package-binding");
            var describes = root.GetProperty("relationships").EnumerateArray()
                .Where(r => Text(r, "relationshipType") == "DESCRIBES").ToArray();
            Require(describes.Length == 1 && Text(describes[0], "spdxElementId") == "SPDXRef-DOCUMENT", "spdx-image-binding");
            var roots = packages.Where(p => Text(p, "SPDXID") == Text(describes[0], "relatedSpdxElement")).ToArray();
            Require(roots.Length == 1, "spdx-image-binding");
            var image = roots[0];
            Require(Text(image, "name") == S06ReleaseIdentity.ImageRepository &&
                Text(image, "versionInfo") == imageDigest && Text(image, "primaryPackagePurpose") == "CONTAINER",
                "spdx-image-binding");
            var hashes = image.GetProperty("checksums").EnumerateArray().ToArray();
            Require(hashes.Length == 1 && Text(hashes[0], "algorithm") == "SHA256" &&
                Text(hashes[0], "checksumValue") == imageDigest[7..], "spdx-image-binding");
            var release = packages.Where(p => Text(p, "name") == S06ReleaseIdentity.ReleasePackage).ToArray();
            Require(release.Length > 0 && release.All(p => Text(p, "versionInfo") == S06ReleaseIdentity.Version),
                "spdx-release-package");
            return new(Cp6DeterministicJson.Sha256Hex(owned), owned.Length, created, packages.Length - 1);
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("spdx-shape"); }
    }
}

internal sealed record SpdxImageObservation(string Sha256, int ByteLength, DateTimeOffset CreatedAtUtc, int PackageCount);
```

### tools/p10/ReleaseVerifier/SarifImageReport.cs

```csharp
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.ImageReportProfile;

namespace CP6.P10.ReleaseVerifier;

// Checks complete Trivy vulnerability results, including zero findings, without CP6-canonicalizing floats.
internal static class SarifImageReport
{
    internal static SarifImageObservation Read(ReadOnlyMemory<byte> bytes, string imageDigest, string configDigest)
    {
        OciWirePolicy.RequireDigest(imageDigest);
        OciWirePolicy.RequireDigest(configDigest);
        if (bytes.Length is < 1 or > 4 * 1024 * 1024) throw Error("image-report-size");
        var owned = bytes.ToArray();
        var root = ImageReportProfile.Read(owned);
        try
        {
            Require(Text(root, "version") == "2.1.0" && Text(root, "$schema") ==
                "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/main/sarif-2.1/schema/sarif-schema-2.1.0.json",
                "sarif-format");
            var runs = root.GetProperty("runs");
            Require(runs.ValueKind == JsonValueKind.Array && runs.GetArrayLength() == 1, "sarif-run-set");
            var run = runs[0];
            var driver = run.GetProperty("tool").GetProperty("driver");
            Require(Text(driver, "name") == "Trivy" && Text(driver, "fullName") == "Trivy Vulnerability Scanner" &&
                Text(driver, "informationUri") == "https://github.com/aquasecurity/trivy" &&
                Text(driver, "version") == TrivyVersion, "sarif-tool");
            var identity = run.GetProperty("properties");
            var reference = S06ReleaseIdentity.ImageRepository + "@" + imageDigest;
            Require(Text(identity, "imageName") == reference && Text(identity, "imageID") == configDigest &&
                identity.GetProperty("repoDigests").EnumerateArray().Select(v => v.GetString())
                    .SequenceEqual(new[] { reference }, StringComparer.Ordinal) &&
                !run.TryGetProperty("originalUriBaseIds", out _), "sarif-image-binding");
            var rules = driver.GetProperty("rules").EnumerateArray().ToArray();
            Require(rules.Select(r => Text(r, "id")).Distinct(StringComparer.Ordinal).Count() == rules.Length, "sarif-rules");
            var severityByRule = rules.Select(RuleSeverity).ToArray();
            var usedRules = new HashSet<int>();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["UNKNOWN"] = 0,
                ["LOW"] = 0,
                ["MEDIUM"] = 0,
                ["HIGH"] = 0,
                ["CRITICAL"] = 0
            };
            var results = run.GetProperty("results").EnumerateArray().ToArray();
            foreach (var result in results)
            {
                var indexValue = result.GetProperty("ruleIndex");
                Require(indexValue.TryGetInt32(out var index) && index >= 0 && index < rules.Length, "sarif-result");
                Require(Text(result, "ruleId") == Text(rules[index], "id") &&
                    Text(result, "level") == Level(severityByRule[index]) &&
                    !string.IsNullOrWhiteSpace(Text(result.GetProperty("message"), "text")) &&
                    (!result.TryGetProperty("suppressions", out var suppressions) ||
                        (suppressions.ValueKind == JsonValueKind.Array && suppressions.GetArrayLength() == 0)),
                    "sarif-result");
                usedRules.Add(index);
                counts[severityByRule[index]]++;
            }
            Require(usedRules.Count == rules.Length, "sarif-rules");
            Require(counts["HIGH"] == 0 && counts["CRITICAL"] == 0, "image-vulnerability");
            return new(Cp6DeterministicJson.Sha256Hex(owned), owned.Length, results.Length,
                counts["UNKNOWN"], counts["LOW"], counts["MEDIUM"]);
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("sarif-shape"); }
    }

    private static string RuleSeverity(JsonElement rule)
    {
        Require(!string.IsNullOrWhiteSpace(Text(rule, "id")) &&
            Text(rule, "name") is "OsPackageVulnerability" or "LanguageSpecificPackageVulnerability", "sarif-rules");
        var tags = rule.GetProperty("properties").GetProperty("tags").EnumerateArray().Select(v => v.GetString()).ToArray();
        Require(tags.Length == 3 && tags[0] == "vulnerability" && tags[1] == "security" &&
            tags[2] is "UNKNOWN" or "LOW" or "MEDIUM" or "HIGH" or "CRITICAL", "sarif-rules");
        var severity = tags[2]!;
        Require(Text(rule.GetProperty("defaultConfiguration"), "level") == Level(severity), "sarif-rules");
        return severity;
    }

    private static string Level(string severity) => severity switch
    {
        "HIGH" or "CRITICAL" => "error",
        "MEDIUM" => "warning",
        _ => "note"
    };
}

internal sealed record SarifImageObservation(string Sha256, int ByteLength, int FindingCount,
    int UnknownCount, int LowCount, int MediumCount);
```

- [x] Run focused tests, the full Release suite with actual existing network/cryptographic inputs, and format verification.

```powershell
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ImageReportTests
dotnet test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
dotnet format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --verify-no-changes --no-restore
```

Expected: 66 focused and 933 total tests pass, zero skips. Actual formal image scanning remains pending; component fixtures never count as those results.

## Task 3: Review and checkpoint

- [x] Compare all four source/test code blocks with the actual files, inspect the complete five-file scope, and scan for secret/path residue or unrelated changes.
- [x] Stage only the five files below, enforce each native-command exit code, and commit the verified checkpoint.

```powershell
git diff --check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git add -- docs/superpowers/plans/2026-09-08-p10-s06-image-report-binding.md tools/p10/ReleaseVerifier/ImageReportProfile.cs tools/p10/ReleaseVerifier/SpdxImageReport.cs tools/p10/ReleaseVerifier/SarifImageReport.cs tools/p10/ReleaseVerifier.Tests/ImageReportTests.cs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git commit -m "feat(p10): bind native image reports and enforce vulnerability gates"
```

- [ ] Complete actual protected-workflow scanner execution, graph provenance, publication and cross-repository audit before P10 acceptance.

## Observed results (2026-09-08 UTC)

- Red: 66/66 failures, all NotImplementedException; zero unexpected failures and zero skipped/not-executed cases.
- Focused green: 66/66 passed. Complete Release suite: 933/933 passed, zero skips, no build warnings/errors. Format verification exited 0.
- Exact four-file code/plan parity, full five-file scope review and secret/path/placeholder hygiene passed. Existing workflow, runtime, package lock and trust files are unchanged.
- The report checks are ready for real protected-workflow outputs. No actual image scan, clean-image claim, R2 publication or P10 completion is asserted by this checkpoint.
