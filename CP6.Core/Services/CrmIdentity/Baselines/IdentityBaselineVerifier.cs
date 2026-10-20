using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CP6.Core.Services.CrmIdentity.Baselines;

public sealed class IdentityBaselineVerifier
{
    public const string Contract = "cp6-identity-baseline-v1";
    public const int MaximumItemPayloadBytes = 1_048_576;
    private readonly object _gate = new();
    private readonly IdentityEventValidator _validator;
    private readonly IdentityBaselineSourceV1 _source;
    private readonly string _boundary;
    private readonly IdentityBaselineLimits _limits;
    private readonly Dictionary<string, IdentityVersion> _initial;
    private readonly Dictionary<string, IdentityRawSnapshotV1> _received = new(StringComparer.Ordinal);
    private long _bytes;
    private bool _closed;

    public IdentityBaselineVerifier(CrmIdentityRuntime runtime, IdentityBaselineSourceV1 source, string boundary, long totalCount,
        IEnumerable<IdentityVersion> initial, IdentityBaselineLimits? limits = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _limits = limits ?? new();
        if (_limits.MaximumItems is < 1 or > 1_000_000 || _limits.MaximumPayloadBytes is < 1 or > 268_435_456) throw Invalid();
        if (runtime is null || source is null || source.TenantId == Guid.Empty ||
            source.Issuer != runtime.Options.Issuer || !Text(source.Issuer, 2048) ||
            !runtime.Options.Tenants.TryGetValue(source.TenantId, out var region) || source.Region != region ||
            source.ContractBundleSha256 != runtime.ContractBundleSha256 || !Hash(source.ContractBundleSha256) || !Hash(source.StorageIdentitySha256))
            throw new IdentityBaselineException("C02_BASELINE_SOURCE_CHANGED");
        if (!Text(boundary, 2048)) throw Invalid();
        _source = source; _boundary = boundary; _validator = runtime.Validator;
        _initial = Directory(initial, totalCount, ct);
    }

    // Any failed operation poisons this candidate. Callers cannot catch a malformed row and
    // continue to publish the remaining set. No mutable partial result is exposed.
    public void Add(IdentityRawSnapshotV1 snapshot, CancellationToken ct = default)
    {
        lock (_gate)
        {
            try
            {
                Open(ct);
                if (snapshot is null || snapshot.Contract != Contract) throw Invalid();
                if (snapshot.Source != _source) throw new IdentityBaselineException("C02_BASELINE_SOURCE_CHANGED");
                if (snapshot.Boundary != _boundary) throw new IdentityBaselineException("C02_BASELINE_BOUNDARY_CHANGED");
                if (snapshot.Item is null || !Text(snapshot.Item.AggregateId, 128) || !_initial.TryGetValue(snapshot.Item.AggregateId, out var expected) || expected != snapshot.Item ||
                    _received.ContainsKey(snapshot.Item.AggregateId)) throw Invalid();
                if (snapshot.PayloadBase64 is null || snapshot.PayloadBase64.Length is 0 or > ((MaximumItemPayloadBytes + 2) / 3 * 4)) throw Invalid();
                var bytes = Convert.FromBase64String(snapshot.PayloadBase64);
                if (bytes.Length > MaximumItemPayloadBytes || Convert.ToBase64String(bytes) != snapshot.PayloadBase64 ||
                    IdentityEventContracts.Hash(bytes) != expected.PayloadSha256) throw Invalid();
                if (_bytes + bytes.Length > _limits.MaximumPayloadBytes) throw new IdentityBaselineException("C02_BASELINE_LIMIT_EXCEEDED");
                if (!_validator.IsValidSnapshotData(_source.TenantId, expected, _source.Region, bytes)) throw Invalid();
                ct.ThrowIfCancellationRequested(); _received.Add(expected.AggregateId, snapshot); _bytes += bytes.Length;
            }
            catch (FormatException) { Close(); throw Invalid(); }
            catch { Close(); throw; }
        }
    }

    public VerifiedIdentityBaseline Complete(string finalBoundary, long finalTotalCount,
        IEnumerable<IdentityVersion> final, CancellationToken ct = default)
    {
        lock (_gate)
        {
            try
            {
                Open(ct);
                if (finalBoundary != _boundary) throw new IdentityBaselineException("C02_BASELINE_BOUNDARY_CHANGED");
                var last = Directory(final, finalTotalCount, ct);
                if (_received.Count != _initial.Count || last.Count != _initial.Count ||
                    _initial.Any(x => !last.TryGetValue(x.Key, out var item) || x.Value != item))
                    throw new IdentityBaselineException("C02_BASELINE_INCOMPLETE");
                var ordered = _received.Values.OrderBy(x => x.Item.AggregateId, StringComparer.Ordinal).ToArray();
                var result = new VerifiedIdentityBaseline(_source, _boundary, ordered, Digest(ordered, ct));
                ct.ThrowIfCancellationRequested(); Close(); return result;
            }
            catch { Close(); throw; }
        }
    }

    private Dictionary<string, IdentityVersion> Directory(IEnumerable<IdentityVersion> rows, long count, CancellationToken ct)
    {
        if (count < 0 || rows is null) throw Invalid();
        if (count > _limits.MaximumItems) throw new IdentityBaselineException("C02_BASELINE_LIMIT_EXCEEDED");
        var result = new Dictionary<string, IdentityVersion>(StringComparer.Ordinal);
        foreach (var item in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (result.Count >= _limits.MaximumItems) throw new IdentityBaselineException("C02_BASELINE_LIMIT_EXCEEDED");
            if (item is null || !Text(item.AggregateId, 128) || item.AggregateId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or ':' or '-')) ||
                item.Version < 1 || !Hash(item.PayloadSha256) || !IdentityEventContracts.IsSupported(item.EventType) ||
                !result.TryAdd(item.AggregateId, item)) throw Invalid();
            if (result.Count > count) throw new IdentityBaselineException("C02_BASELINE_INCOMPLETE");
        }
        if (result.Count != count) throw new IdentityBaselineException("C02_BASELINE_INCOMPLETE");
        return result;
    }

    // v1 digest: SHA-256 of UTF-8 fields, each prefixed by its unsigned 4-byte big-endian
    // byte length; source fields, opaque boundary, count, then ordinally sorted item tuples.
    // Each tuple's verified original-data SHA-256 binds the retained base64 bytes.
    private string Digest(IdentityRawSnapshotV1[] rows, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Field(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length); hash.AppendData(length); hash.AppendData(bytes);
        }
        Field(Contract); Field(_source.TenantId.ToString("D")); Field(_source.Issuer); Field(_source.Region);
        Field(_source.ContractBundleSha256); Field(_source.StorageIdentitySha256); Field(_boundary); Field(rows.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested(); Field(row.Item.AggregateId); Field(row.Item.EventType);
            Field(row.Item.Version.ToString(CultureInfo.InvariantCulture)); Field(row.Item.PayloadSha256); Field(row.Item.IsDeleted ? "1" : "0");
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private void Open(CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (_closed) throw Invalid(); }
    private void Close() { _closed = true; _received.Clear(); _initial.Clear(); }
    private static bool Text(string? value, int length)
    {
        if (string.IsNullOrEmpty(value) || value.Length > length || value.Any(char.IsControl)) return false;
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); return true; }
        catch (EncoderFallbackException) { return false; }
    }
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
    private static IdentityBaselineException Invalid() => new("C02_BASELINE_INVALID");
}
