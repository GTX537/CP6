using System.Collections.Frozen;

namespace CP6.P10.ReleaseVerifier;

// Reviewed immutable S04/S05 archives. This cannot select a caller-provided commit, file or hash.
internal sealed class PinnedReleaseDocument
{
    internal const string Repository = "GTX537/CP6.CRM";
    internal const string CommitSha = "bb1fd8b4f250fabde4476b6a450435de2d07c03f";
    private PinnedReleaseDocument(string name, string path, int length, string gitBlobSha, string sha256)
    {
        Name = name;
        Path = "docs/delivery/p10/" + path;
        ByteLength = length;
        GitBlobSha = gitBlobSha;
        Sha256 = sha256;
    }

    internal string Name { get; }
    internal string Path { get; }
    internal int ByteLength { get; }
    internal string GitBlobSha { get; }
    internal string Sha256 { get; }

    private static readonly FrozenDictionary<string, PinnedReleaseDocument> Documents = new[]
    {
        new PinnedReleaseDocument("crm-index", "consumer-0.10.1/verification-index.v1.json", 4598,
            "647cc8e47304ea7de93430b4f470ed6a089fbb2a", S06ReleaseIdentity.CrmIndexHash),
        new PinnedReleaseDocument("crm-main-linux", "consumer-0.10.1/main-linux.consumer-evidence.v1.json", 2174,
            "9d7f54ef73fe0a181813a9e3cb4dff3bbea465c7", "d4611662fb31055934fa964dc942c78bdf23e734a60d4c1bdf27484a096f3278"),
        new PinnedReleaseDocument("crm-main-windows", "consumer-0.10.1/main-windows.consumer-evidence.v1.json", 2184,
            "ef74f718b77a93d24453ff2c3cf2b2fe775438fb", "b507d9af5a0d6e3225ee3367c59cbde53b3b1d486b493002b77d57c9fe6281fa"),
        new PinnedReleaseDocument("crm-pr-linux", "consumer-0.10.1/pr-linux.consumer-evidence.v1.json", 2174,
            "f2b6c8238d9b0aed3215983e8880063ed3ac7de1", "2d7fee2faca05db1e6ed0ddf1a6f55b7d7b61ba112855f62ccbb99e06eb4e886"),
        new PinnedReleaseDocument("crm-pr-windows", "consumer-0.10.1/pr-windows.consumer-evidence.v1.json", 2184,
            "61cbfe747eb6c1cc9f04761f219daec31f61d413", "5e1e66d6c0e0c36a13741d9966dfd08d040fd7c6f01858fac45eb34e9c071278"),
        new PinnedReleaseDocument("publication", "0.10.1/formal-package-publication.v1.json", 7550,
            "2b4fbab6359df41e7e8daf19614fa3cdcabc1543", S06ReleaseIdentity.PublicationHash),
        new PinnedReleaseDocument("package-provenance", "0.10.1/build-invocation-provenance.v1.json", 4132,
            "2f0fc55997b70c6e0c64835fe42f2c3b97b97d6b", S06ReleaseIdentity.ProvenanceHash),
    }.ToFrozenDictionary(d => d.Name, StringComparer.Ordinal);

    internal static PinnedReleaseDocument Get(string name)
    {
        if (string.IsNullOrEmpty(name) || !Documents.TryGetValue(name, out var document))
            throw GitHubWirePolicy.Error("github-archive-name");
        return document;
    }
}
