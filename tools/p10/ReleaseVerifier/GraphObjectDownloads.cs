using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Shared production byte boundary for the R2 adapter; the delegate supplies bytes, never verification success.
internal sealed class GraphObjectDownloads(Func<ContentAddress, CancellationToken, Task<byte[]?>> read)
{
    private readonly Dictionary<string, (ContentAddress Address, byte[] Bytes)> _objects = new(StringComparer.Ordinal);
    private long _reservedBytes;

    internal async Task<byte[]> ReadAsync(ContentAddress address, string mediaType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (address.MediaType != mediaType) throw Error("graph-download-media");
        if (_objects.TryGetValue(address.Key, out var existing))
        {
            if (existing.Address.MediaType != address.MediaType || existing.Address.Sha256 != address.Sha256 ||
                existing.Address.ByteLength != address.ByteLength) throw Error("graph-download-reference");
            return existing.Bytes.ToArray();
        }
        // One candidate plus at most 32 referenced objects; reserve the entire declared size before I/O.
        if (_objects.Count >= 33) throw Error("graph-download-count");
        if (_reservedBytes + address.ByteLength > 64L * 1024 * 1024) throw Error("graph-download-budget");
        _reservedBytes += address.ByteLength;
        var received = await read(address, cancellationToken) ?? throw Error("graph-download-missing");
        cancellationToken.ThrowIfCancellationRequested();
        if (received.Length != address.ByteLength) throw Error("graph-download-size");
        var bytes = received.ToArray();
        if (Cp6DeterministicJson.Sha256Hex(bytes) != address.Sha256) throw Error("graph-download-hash");
        _objects.Add(address.Key, (address, bytes));
        return bytes.ToArray();
    }

    internal IReadOnlyDictionary<string, ReadOnlyMemory<byte>> SnapshotExcept(string candidateKey) =>
        _objects.Where(pair => pair.Key != candidateKey).ToDictionary(pair => pair.Key,
            pair => (ReadOnlyMemory<byte>)pair.Value.Bytes.ToArray(), StringComparer.Ordinal);

    private static Cp6ReleaseContractException Error(string code) => new(code, "Graph download violates the signed reference and aggregate limits.");
}
