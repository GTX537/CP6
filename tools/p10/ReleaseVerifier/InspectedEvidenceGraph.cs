using System.Collections.Frozen;
using System.Text.Json;

namespace CP6.P10.ReleaseVerifier;

// Immutable inspection output. Structural consistency is never candidate acceptance.
public sealed class InspectedEvidenceGraph
{
    internal InspectedEvidenceGraph(string sha256, JsonElement candidate, JsonElement gate,
        Dictionary<string, InspectedEvidence> evidence, int objectCount)
    {
        Sha256 = sha256;
        Candidate = candidate.Clone();
        Gate = gate.Clone();
        Evidence = evidence.ToFrozenDictionary(StringComparer.Ordinal);
        ObjectCount = objectCount;
    }

    public string Sha256 { get; }
    public JsonElement Candidate { get; }
    public JsonElement Gate { get; }
    public IReadOnlyDictionary<string, InspectedEvidence> Evidence { get; }
    public int ObjectCount { get; }
    public bool CandidateAccepted => false;
}

public sealed class InspectedEvidence
{
    private readonly byte[] _payloadBytes;

    internal InspectedEvidence(JsonElement record, ContentAddress payload, byte[] payloadBytes)
    {
        Record = record.Clone();
        Payload = payload;
        _payloadBytes = payloadBytes.ToArray();
    }

    public JsonElement Record { get; }
    public ContentAddress Payload { get; }
    public byte[] CopyPayloadBytes() => _payloadBytes.ToArray();
}
