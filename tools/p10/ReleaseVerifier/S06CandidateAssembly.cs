using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

internal static class S06CandidateAssembly
{
    // No publication success field or invented workflow conclusion is included.
    internal static AssembledPlatformCandidate Create(InspectedValidationArtifact artifact, GitHubWorkflowIdentity publisher)
    {
        publisher.RequireValid();
        Require(publisher.Repository == "GTX537/CP6" && publisher.WorkflowPath == S06ReleaseIdentity.PublicationPath &&
            publisher.CommitSha == artifact.Producer.CommitSha && publisher.RunId != artifact.Producer.RunId, "assembly-publisher");
        var root = artifact.Index;
        var bytes = S06ArtifactAssembly.Control(Cp6ReleaseContractIds.PlatformCandidate, new
        {
            candidateKind = "PlatformReference", deployable = false, createdAtUtc = FormatTime(DateTimeOffset.UtcNow),
            platformSource = root.GetProperty("platformSource"),
            packages = S06ReleaseIdentity.PackageHashes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new
            {
                packageId = p.Key, version = S06ReleaseIdentity.Version, sourceGitSha = S06ReleaseIdentity.Source,
                authorSignedPackageSha256 = p.Value, publishedPackageSha256 = p.Value,
                feedIdentity = FormalFeedPolicy.Index + "#" + p.Key + "/" + S06ReleaseIdentity.Version,
                feedTransformation = "BytePreserving", signerFingerprint = S06ReleaseIdentity.Signer, timestampPolicy = "Rfc3161Required"
            }).ToArray(),
            buildProvenance = artifact.Evidence["PackageProvenance"].Record.GetProperty("object"),
            images = root.GetProperty("images"),
            crmConsumer = new
            {
                repository = "GTX537/CP6.CRM", workflowPath = ".github/workflows/crm-validation.yml",
                workflowFileSha = "924014cb1231824a9b57ab82a6f9638f76329919", runId = 34134695003L,
                runAttempt = 1, commitSha = S06ReleaseIdentity.CrmSource, environment = "none"
            },
            publisher = Workflow(publisher), verifier = Workflow(artifact.Producer), policyVersions = new { trust = 1, evidence = 1 },
            evidence = root.GetProperty("evidence"), releaseGateResult = root.GetProperty("releaseGateResult")
        });
        var objects = artifact.CopyObjects();
        return new(bytes, objects, EvidenceGraphInspection.Inspect(bytes, objects));
    }
}

internal sealed class AssembledPlatformCandidate
{
    private readonly byte[] _bytes;
    private readonly Dictionary<string, byte[]> _objects;

    internal AssembledPlatformCandidate(byte[] bytes, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> objects, InspectedEvidenceGraph inspection)
    {
        _bytes = bytes.ToArray();
        _objects = objects.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
        Inspection = inspection;
    }

    internal InspectedEvidenceGraph Inspection { get; }
    internal byte[] CopyBytes() => _bytes.ToArray();
    internal IReadOnlyDictionary<string, ReadOnlyMemory<byte>> CopyObjects() =>
        _objects.ToDictionary(p => p.Key, p => (ReadOnlyMemory<byte>)p.Value.ToArray(), StringComparer.Ordinal);
}
