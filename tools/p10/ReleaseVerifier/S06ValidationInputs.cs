using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Local stage codecs only. Neither a matching producer document nor these raw bytes authenticate evidence.
internal static class S06ValidationInputs
{
    internal static IReadOnlyList<string> InitialKinds { get; } = Array.AsReadOnly(new[]
    {
        "CrmConsumer", "FormalPackagePublication", "FormalPackageVerification", "NuGetTrustPolicy",
        "PackageProvenance", "SourceReference", "TrustPolicy"
    });

    internal static IReadOnlyList<string> PreparationNames { get; } = Array.AsReadOnly(InitialKinds.Select(PayloadPath)
        .Concat(S06ReleaseIdentity.PackageHashes.Keys.Select(PackagePath)).Append("producer.json")
        .Order(StringComparer.Ordinal).ToArray());

    internal static IReadOnlyList<string> ImageNames { get; } = Array.AsReadOnly(new[]
    {
        "build-input.json", "buildx-metadata.json", "oci.sigstore.json", "sarif.json", "spdx.json"
    });

    internal static string PayloadPath(string kind) => "payloads/" + kind + ".json";
    internal static string PackagePath(string packageId) => "packages/" + packageId + "." + S06ReleaseIdentity.Version + ".nupkg";

    internal static (Dictionary<string, ReadOnlyMemory<byte>> Payloads, byte[] ReleasePackage) ReadPreparation(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files, GitHubWorkflowIdentity currentProducer)
    {
        RequireProducer(currentProducer);
        RequireFiles(files, PreparationNames);
        Require(files["producer.json"].Span.SequenceEqual(S06ArtifactAssembly.Canonical(Workflow(currentProducer))),
            "validation-stage-producer");
        foreach (var package in S06ReleaseIdentity.PackageHashes)
            Require(Cp6DeterministicJson.Sha256Hex(files[PackagePath(package.Key)].Span) == package.Value,
                "validation-stage-package");
        return (InitialKinds.ToDictionary(k => k, k => (ReadOnlyMemory<byte>)files[PayloadPath(k)].ToArray(),
            StringComparer.Ordinal), files[PackagePath(S06ReleaseIdentity.ReleasePackage)].ToArray());
    }

    internal static S06ImageInputs ReadImage(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files)
    {
        RequireFiles(files, ImageNames);
        var bytes = files["build-input.json"].ToArray();
        Require(bytes.AsSpan().SequenceEqual(Cp6DeterministicJson.Canonicalize(bytes)), "non-canonical-json");
        try
        {
            var value = GitHubApiJson.Parse(bytes);
            Exact(value, "imageDigest", "buildStartedAtUtc", "buildCompletedAtUtc");
            var digest = Text(value, "imageDigest");
            OciWirePolicy.RequireDigest(digest);
            var started = Time(value, "buildStartedAtUtc");
            var completed = Time(value, "buildCompletedAtUtc");
            Require(started <= completed && completed <= DateTimeOffset.UtcNow, "validation-image-time");
            return new(digest, started, completed, files["buildx-metadata.json"].ToArray(),
                files["spdx.json"].ToArray(), files["sarif.json"].ToArray(), files["oci.sigstore.json"].ToArray());
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("validation-image-shape"); }
    }

    private static void RequireFiles(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files, IReadOnlyCollection<string> names)
    {
        Require(files.Keys.Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal),
            StringComparer.Ordinal), "validation-stage-files");
        Require(files.All(p => p.Value.Length > 0 && p.Value.Length <=
            (p.Key.EndsWith(".nupkg", StringComparison.Ordinal) ? FormalNuGetVerifier.MaximumPackageBytes : Cp6DeterministicJson.MaximumBytes)) &&
            files.Values.Sum(v => (long)v.Length) <= WorkflowArtifactSelection.MaximumArchiveBytes, "validation-stage-size");
    }
}

internal sealed record S06ImageInputs(string Digest, DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc,
    byte[] BuildMetadata, byte[] Spdx, byte[] Sarif, byte[] SignatureBundle);
