using System.Globalization;
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Exact cosign v3.1.3 image-signature claims; no registry or workflow acceptance here.
internal static class OciSignatureProfile
{
    internal const string EnvironmentName = "p10-platform-candidate";

    internal static Dictionary<string, string> Annotations(GitHubWorkflowIdentity workflow,
        string keyId, int version, DateTimeOffset signedAtUtc)
    {
        workflow.RequireValid();
        Require(workflow.Repository == "GTX537/CP6" && workflow.WorkflowPath == S06ReleaseIdentity.ValidationPath);
        return new(StringComparer.Ordinal)
        {
            ["cp6.imageRepository"] = S06ReleaseIdentity.ImageRepository,
            ["cp6.sourceGitSha"] = workflow.CommitSha,
            ["cp6.workflowRepository"] = workflow.Repository,
            ["cp6.workflowPath"] = workflow.WorkflowPath,
            ["cp6.workflowFileSha"] = workflow.WorkflowFileSha,
            ["cp6.runId"] = workflow.RunId.ToString(CultureInfo.InvariantCulture),
            ["cp6.runAttempt"] = workflow.RunAttempt.ToString(CultureInfo.InvariantCulture),
            ["cp6.environment"] = EnvironmentName,
            ["cp6.signerKeyId"] = keyId,
            ["cp6.trustPolicyVersion"] = version.ToString(CultureInfo.InvariantCulture),
            ["cp6.signedAtUtc"] = signedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["cp6.deployable"] = "false"
        };
    }

    // These three untrusted selectors only choose an already-pinned key and policy.
    internal static (string KeyId, int Version, DateTimeOffset SignedAtUtc) Selectors(JsonElement root,
        DateTimeOffset evaluationUtc)
    {
        try
        {
            var annotations = root.GetProperty("subject")[0].GetProperty("annotations");
            var keyId = annotations.GetProperty("cp6.signerKeyId").GetString()!;
            var versionText = annotations.GetProperty("cp6.trustPolicyVersion").GetString()!;
            var time = annotations.GetProperty("cp6.signedAtUtc").GetString()!;
            if (keyId.Length != 71 || !keyId.StartsWith("sha256:", StringComparison.Ordinal) ||
                keyId[7..].Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')) ||
                !int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
                version < 1 || version.ToString(CultureInfo.InvariantCulture) != versionText ||
                !DateTimeOffset.TryParseExact(time, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var signedAtUtc))
                throw new FormatException();
            if (evaluationUtc.Offset != TimeSpan.Zero || signedAtUtc < DateTimeOffset.UnixEpoch || signedAtUtc > evaluationUtc)
                throw Error("oci-signature-time");
            return (keyId, version, signedAtUtc);
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("oci-signature-selector"); }
    }

    internal static void RequireClaims(JsonElement root, string digest, GitHubWorkflowIdentity workflow,
        string keyId, int version, DateTimeOffset signedAtUtc)
    {
        try
        {
            Exact(root, "_type", "subject", "predicateType", "predicate");
            Require(root.GetProperty("_type").GetString() == "https://in-toto.io/Statement/v1" &&
                root.GetProperty("predicateType").GetString() == OciBundlePayload.PredicateType && version == 1);
            Exact(root.GetProperty("predicate"));
            var subjects = root.GetProperty("subject");
            Require(subjects.ValueKind == JsonValueKind.Array && subjects.GetArrayLength() == 1);
            var subject = subjects[0];
            // cosign sign --new-bundle-format emits digest and annotations, without a subject name.
            Exact(subject, "digest", "annotations");
            Exact(subject.GetProperty("digest"), "sha256");
            Require(subject.GetProperty("digest").GetProperty("sha256").GetString() == digest[7..]);
            var expected = Annotations(workflow, keyId, version, signedAtUtc);
            var actual = subject.GetProperty("annotations");
            Exact(actual, expected.Keys.ToArray());
            foreach (var pair in expected) Require(actual.GetProperty(pair.Key).GetString() == pair.Value);
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("oci-signature-claims"); }
    }

    private static void Exact(JsonElement value, params string[] names) =>
        Require(value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Select(p => p.Name)
            .Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal));

    private static void Require(bool condition)
    {
        if (!condition) throw Error("oci-signature-claims");
    }

    internal static Cp6ReleaseContractException Error(string code) =>
        new(code, "OCI signature violates the pinned S06 identity and trust profile.");
}
