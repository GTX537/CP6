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
