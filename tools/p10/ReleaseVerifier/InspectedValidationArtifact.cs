using System.Collections.Frozen;
using System.Text.Json;

namespace CP6.P10.ReleaseVerifier;

internal sealed class InspectedValidationArtifact
{
    private readonly byte[] _indexBytes;
    private readonly Dictionary<string, byte[]> _files;
    private readonly Dictionary<string, byte[]> _objects;

    internal InspectedValidationArtifact(GitHubWorkflowIdentity producer, byte[] indexBytes, JsonElement index,
        JsonElement gate, Dictionary<string, InspectedEvidence> evidence, Dictionary<string, byte[]> files,
        Dictionary<string, ReadOnlyMemory<byte>> objects)
    {
        Producer = producer;
        _indexBytes = indexBytes.ToArray();
        Index = index.Clone();
        Gate = gate.Clone();
        Evidence = evidence.ToFrozenDictionary(StringComparer.Ordinal);
        _files = files.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
        _objects = objects.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
    }

    internal GitHubWorkflowIdentity Producer { get; }
    internal JsonElement Index { get; }
    internal JsonElement Gate { get; }
    internal IReadOnlyDictionary<string, InspectedEvidence> Evidence { get; }
    internal bool EvidenceAuthenticated => false;
    internal byte[] CopyIndexBytes() => _indexBytes.ToArray();
    internal IReadOnlyDictionary<string, ReadOnlyMemory<byte>> CopyFiles() =>
        _files.ToDictionary(p => p.Key, p => (ReadOnlyMemory<byte>)p.Value.ToArray(), StringComparer.Ordinal);
    internal IReadOnlyDictionary<string, ReadOnlyMemory<byte>> CopyObjects() =>
        _objects.ToDictionary(p => p.Key, p => (ReadOnlyMemory<byte>)p.Value.ToArray(), StringComparer.Ordinal);
}
