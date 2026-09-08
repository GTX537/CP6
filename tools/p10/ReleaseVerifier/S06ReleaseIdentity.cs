using System.Collections.Frozen;
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Release selection, not a copy of the Platform schema. A new release needs reviewed pins.
internal static class S06ReleaseIdentity
{
    internal const string Source = "3ff27e26962dcfd722887afb80a4306010dd9ee1";
    internal const string Version = "0.10.1";
    internal const string Signer = "1debfb8ff286ea51192b7f259d1ac823c105c4188eac40148598d37f0e20ff0d";
    internal const string PublicationHash = "8b5fc47fd77902a3433d61b4cf181b67ef61ee994b60ea8286a690ab5084c961";
    internal const string ProvenanceHash = "6885eee1db5075faeb1a321684f579ef853166a32db8af1cf861c34c79ec64a9";
    internal const string NuGetTrustHash = "da359e3a8e9be2220541c53613d2da277cb2bb9a22a8770df30c808a033b953f";
    internal const string CrmIndexHash = "1332bf21e4253112d457f86cdc0cba829fc58f20c3914fa738dd992d917e2914";
    internal const string CrmSource = "a31ca0e323418f7e4108cc6220c0f5fa132e7fc2";
    internal const string ImageRepository = "ghcr.io/gtx537/cp6-p10-verifier";
    internal const string ValidationPath = ".github/workflows/p10-platform-validation.yml";
    internal const string PublicationPath = ".github/workflows/p10-platform-candidate.yml";
    internal const string ReleasePackage = "CP6.Platform.Release";

    internal static readonly FrozenDictionary<string, string> PackageHashes = new Dictionary<string, string>
    {
        ["CP6.Platform.Abstractions"] = "1319912dac5c3e12a5bb4885e64d92f43a6057e96d2f6c11c05db97b9fc7f1f7",
        ["CP6.Platform.AspNetCore"] = "2949e2149ca2c98fa1c935b89f96ce2dcf388186daa038f8a20bbe1d57f02b71",
        ["CP6.Platform.Contracts"] = "e2c63dd160fe189db1fbc9090da38cab1d67bd599edd1333afc992a93f4110eb",
        ["CP6.Platform.Deployment"] = "a218d220bd42ada3928e45a6245558b7f0d3b96fd4ea3d9173886bbe7c91e6c0",
        ["CP6.Platform.EntityFramework"] = "90a341401f9058be2997e40f61c2dd5ad0fc4717c8b63672abc9009118fc5a71",
        ["CP6.Platform.Messaging"] = "7ffa23acf9c1623cbf520d8dd7fdff104a77abf07832bd4e89102092ab9f4558",
        [ReleasePackage] = "afe85eabe78965e0d7967f3f2cf54574c8adcb02141523aecd8a8bafc6d700e0"
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // Call only after the package has validated the complete candidate document.
    internal static void RequireCandidate(JsonElement root)
    {
        foreach (var package in root.GetProperty("packages").EnumerateArray())
        {
            var id = Text(package, "packageId");
            var expected = PackageHashes[id];
            Require(Text(package, "version") == Version &&
                Text(package, "sourceGitSha") == Source &&
                Text(package, "authorSignedPackageSha256") == expected &&
                Text(package, "publishedPackageSha256") == expected &&
                Text(package, "feedIdentity") == $"https://nuget.pkg.github.com/GTX537/index.json#{id}/{Version}" &&
                Text(package, "feedTransformation") == "BytePreserving" &&
                Text(package, "signerFingerprint") == Signer &&
                Text(package, "timestampPolicy") == "Rfc3161Required", "graph-package-identity");
        }
        var source = root.GetProperty("platformSource");
        Require(Text(source, "sourceGitSha") == Source &&
            Text(source, "subjectKind") == "SourceProvenance" &&
            Text(source, "subjectName") == "GTX537/CP6.Platform" &&
            Text(source, "sha256OrDigest").Length == 64, "graph-source-identity");
        var policy = root.GetProperty("policyVersions");
        Require(policy.GetProperty("trust").GetInt64() == 1 &&
            policy.GetProperty("evidence").GetInt64() == 1, "graph-policy");
        var verifier = root.GetProperty("verifier");
        var publisher = root.GetProperty("publisher");
        RequireWorkflow(verifier, "GTX537/CP6", ValidationPath, "p10-platform-candidate");
        RequireWorkflow(publisher, "GTX537/CP6", PublicationPath, "p10-platform-candidate");
        Require(Text(verifier, "commitSha") == Text(publisher, "commitSha") &&
            verifier.GetProperty("runId").GetInt64() != publisher.GetProperty("runId").GetInt64(), "graph-workflow-separation");
        var crm = root.GetProperty("crmConsumer");
        RequireWorkflow(crm, "GTX537/CP6.CRM", ".github/workflows/crm-validation.yml", "none");
        Require(Text(crm, "commitSha") == CrmSource &&
            Text(crm, "workflowFileSha") == "924014cb1231824a9b57ab82a6f9638f76329919" &&
            crm.GetProperty("runId").GetInt64() == 34134695003 &&
            crm.GetProperty("runAttempt").GetInt64() == 1, "graph-crm-identity");
        var images = root.GetProperty("images");
        Require(images.GetArrayLength() == 1, "graph-image-set");
        var image = images[0];
        Require(Text(image, "subjectKind") == "OciImage" &&
            Text(image, "subjectName") == ImageRepository &&
            Text(image, "sha256OrDigest").StartsWith("sha256:", StringComparison.Ordinal) &&
            Text(image, "sourceGitSha") == Text(verifier, "commitSha"), "graph-image-identity");
    }

    private static void RequireWorkflow(JsonElement value, string repository, string path, string environment) =>
        Require(Text(value, "repository") == repository && Text(value, "workflowPath") == path &&
            Text(value, "environment") == environment, "graph-workflow-identity");

    internal static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    internal static void Require(bool condition, string code)
    {
        if (!condition) throw new Cp6ReleaseContractException(code, "Evidence graph violates the fixed S06 binding profile.");
    }
}
