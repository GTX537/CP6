using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CP6.Core.Services.Wms;

// Persistence primitives only. References below must be resolved by the adopting Owner.
// No HTTP/DI registration: this store cannot establish delegation, coverage or permission.
internal sealed partial class CapacityOccupancyStoreV1
{
    internal sealed record Space(Guid EnvironmentId, string Mode, Guid? SandboxScopeId);
    internal sealed record Evidence(string Owner, Guid Id, string Version, string Digest);
    internal sealed record Code(string Id, string Version);
    internal sealed record Domain(Guid WarehouseId, Guid LocationId, Code Metric, Code Uom, Code Precision);
    internal sealed record ShareKey(string Owner, string Lineage, string Share);
    internal sealed record Range(string From, string To);
    internal sealed record Share(ShareKey Key, string Kind, Evidence Original, Code Uom, Range Range,
        string CapacityQuantity, Evidence Conversion, Evidence Delegation);
    internal sealed record Alias(string Owner, string Key, Evidence Authority);
    internal sealed record Operation(string Owner, string Key);
    internal sealed record Condition(string ExpectedRevision, string? Limit, Evidence Authority);
    internal sealed record Snapshot(string Revision, string Physical, string Unreceived, string Total,
        Domain Domain, string Coverage = "RECORDED_FACTS_ONLY", bool AdmissionGrant = false);
    internal sealed record Receipt(Space Space, Guid TenantId, Operation Operation, string Action, string InputDigest, Snapshot Result);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex QuantityPattern = new(@"\A(?:0|[1-9][0-9]{0,12})\.[0-9]{8}\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex VersionPattern = new(@"\A(?:0|[1-9][0-9]{0,18})\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly BigInteger Scale = 100000000;
    private static readonly BigInteger Maximum = BigInteger.Pow(10, 21) - 1;
    private static InvalidOperationException Error(string code) => new(code);
    private static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    private static T Freeze<T>(T value) => JsonSerializer.Deserialize<T>(Bytes(value), Json)!;
    private static byte[] Canonical(object value)
    {
        using var document = JsonDocument.Parse(Bytes(value)); using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(document.RootElement, writer);
        return stream.ToArray();
        static void Write(JsonElement e, Utf8JsonWriter w)
        {
            if (e.ValueKind == JsonValueKind.Object)
            { w.WriteStartObject(); foreach (var p in e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)) { w.WritePropertyName(p.Name); Write(p.Value, w); } w.WriteEndObject(); }
            else if (e.ValueKind == JsonValueKind.Array) { w.WriteStartArray(); foreach (var x in e.EnumerateArray()) Write(x, w); w.WriteEndArray(); }
            else e.WriteTo(w);
        }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Text(string? value)
    { if (string.IsNullOrWhiteSpace(value) || value.Length > 500 || value != value.Trim() || value.Any(char.IsControl)) throw Error("CAPACITY_IDENTITY_INVALID"); }
    private static long Revision(string value, bool positive = false)
    {
        if (value == null || !VersionPattern.IsMatch(value) || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var v) || positive && v == 0)
            throw Error("CAPACITY_REVISION_INVALID");
        return v;
    }
    private static void Ref(Evidence e)
    {
        if (e == null || e.Id == Guid.Empty) throw Error("CAPACITY_EVIDENCE_INVALID");
        Text(e.Owner); Revision(e.Version, true);
        if (e.Digest == null || e.Digest.Length != 64 || e.Digest.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw Error("CAPACITY_EVIDENCE_INVALID");
    }
    private static void Unit(Code unit)
    { if (unit == null) throw Error("CAPACITY_UNIT_INVALID"); Text(unit.Id); Revision(unit.Version, true); }
    private static BigInteger Quantity(string value, bool positive = false)
    {
        if (value == null || !QuantityPattern.IsMatch(value)) throw Error("CAPACITY_QUANTITY_INVALID");
        var n = BigInteger.Parse(value.Replace(".", ""), CultureInfo.InvariantCulture);
        if (positive && n == 0) throw Error("CAPACITY_QUANTITY_INVALID");
        return n;
    }
    private static string QuantityText(BigInteger n)
    {
        if (n < 0 || n > Maximum) throw Error("CAPACITY_QUANTITY_OVERFLOW");
        return (n / Scale).ToString(CultureInfo.InvariantCulture) + "." + (n % Scale).ToString("D8", CultureInfo.InvariantCulture);
    }
    private static decimal Number(BigInteger n) => decimal.Parse(QuantityText(n), CultureInfo.InvariantCulture);
    private static (BigInteger From, BigInteger To) Bounds(Range range)
    {
        if (range == null) throw Error("CAPACITY_RANGE_INVALID");
        var from = Quantity(range.From); var to = Quantity(range.To);
        if (from >= to) throw Error("CAPACITY_RANGE_INVALID"); return (from, to);
    }
    private static string Key(ShareKey key)
    {
        if (key == null) throw Error("CAPACITY_IDENTITY_INVALID");
        Text(key.Owner); Text(key.Lineage); Text(key.Share); return Hash(Canonical(key));
    }
    private static string DomainKey(Domain d)
    {
        if (d == null || d.WarehouseId == Guid.Empty || d.LocationId == Guid.Empty) throw Error("CAPACITY_DOMAIN_INVALID");
        Unit(d.Metric); Unit(d.Uom); Unit(d.Precision);
        // A changed unit or metric version cannot create a second independent capacity pool.
        return Hash(Canonical(new { d.WarehouseId, d.LocationId, Metric = d.Metric.Id }));
    }
    private static void Validate(Share share)
    {
        if (share == null || share.Kind is not ("PHYSICAL" or "UNRECEIVED")) throw Error("CAPACITY_SHARE_INVALID");
        Key(share.Key); Ref(share.Original); Unit(share.Uom); Bounds(share.Range);
        Quantity(share.CapacityQuantity); Ref(share.Conversion); Ref(share.Delegation);
    }
    private static BigInteger Measure(Share share, Range range)
    {
        var whole = Bounds(share.Range); var part = Bounds(range);
        if (part.From < whole.From || part.To > whole.To) throw Error("CAPACITY_RANGE_OUTSIDE_SHARE");
        var numerator = (part.To - part.From) * Quantity(share.CapacityQuantity);
        var amount = BigInteger.DivRem(numerator, whole.To - whole.From, out var remainder);
        if (remainder != 0) throw Error("CAPACITY_CONVERSION_NOT_EXACT");
        QuantityText(amount); return amount;
    }
}
