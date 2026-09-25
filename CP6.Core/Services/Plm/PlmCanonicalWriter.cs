using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CP6.Entity.DTOs.Plm;

namespace CP6.Core.Services.Plm;

public sealed record PlmEncodedItem(PlmProjectedItem Projection, string IdentityKey, byte[] ItemCanonicalBytes, string ItemDigest);
public sealed record PlmEncodedManifest(byte[] CanonicalBytes, string ManifestDigest, IReadOnlyList<PlmEncodedItem> Items);

public static class PlmCanonicalWriter
{
    public const string ManifestSchema = "plm-technical-manifest-v1";
    public const string ItemSchema = "plm-technical-manifest-item-v1";

    public static PlmEncodedManifest Encode(Guid tenantId, IEnumerable<PlmProjectedItem> source)
    {
        if (tenantId == Guid.Empty) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
        var projected = source.ToList();
        if (projected.Count(x => x.ItemType == "PRODUCT") != 1 || projected.Count == 0)
            throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
        projected.Sort(CompareItems);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var bomOrders = new HashSet<int>();
        var routingOrders = new HashSet<int>();
        var encoded = new List<PlmEncodedItem>(projected.Count);
        string? productCode = null;
        foreach (var sourceItem in projected)
        {
            var item = sourceItem;
            var (owner, identityNames) = item.ItemType switch
            {
                "PRODUCT" => ("ProductMaster", new[] { "productCd" }),
                "BOM" => ("ProductMaterial", new[] { "productCd", "processCd", "materialCd" }),
                "ROUTING" => ("ProductProcess", new[] { "productCd", "taskCd" }),
                _ => throw PlmException.Invalid("PLM_CANONICAL_SCHEMA")
            };
            if (item.SourceOwnerType != owner || item.Identity.Count != identityNames.Length ||
                item.Identity.Where((x, i) => x.Name != identityNames[i]).Any())
                throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
            var schema = PlmSnapshotProjector.Schema(item.ItemType);
            if (item.Content.Count != schema.Count || item.Content.Where((x, i) => x.Name != schema[i].Name || x.Kind != schema[i].Kind).Any())
                throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
            item = item with { Content = item.Content.Select((x, i) => schema[i] with { Value = x.Value }).ToArray() };
            var identity = new StringBuilder();
            AppendIdentity(identity, item.Identity);
            var identityKey = identity.ToString();
            var itemProductCode = Normalize(item.Identity[0].Value, true);
            if (item.ItemType == "PRODUCT") productCode = itemProductCode;
            else if (productCode != itemProductCode) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
            if (item.ProductCd != item.Identity[0].Value ||
                item.ItemType == "BOM" && (item.ProcessCd != item.Identity[1].Value || item.MaterialCd != item.Identity[2].Value) ||
                item.ItemType == "ROUTING" && item.TaskCd != item.Identity[1].Value)
                throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
            var normalizedKeys = item.Identity.Select(x => (x.Name, Value: Normalize(x.Value, true))).ToArray();
            item = item with
            {
                Identity = normalizedKeys, ProductCd = normalizedKeys[0].Value,
                ProcessCd = item.ItemType == "BOM" ? normalizedKeys[1].Value : null,
                MaterialCd = item.ItemType == "BOM" ? normalizedKeys[2].Value : null,
                TaskCd = item.ItemType == "ROUTING" ? normalizedKeys[1].Value : null
            };
            if (identityKey.Length > 256 || !seen.Add(item.ItemType + "|" + identityKey))
                throw PlmException.Invalid("PLM_DUPLICATE_ITEM");
            if (item.ItemType != "PRODUCT")
            {
                if (item.SortOrder is null or <= 0 ||
                    item.Content.Single(x => x.Name == "sortOrder").Value is not int contentOrder ||
                    contentOrder != item.SortOrder) throw PlmException.Invalid("PLM_AMBIGUOUS_ORDER");
                if (!(item.ItemType == "BOM" ? bomOrders : routingOrders).Add(item.SortOrder.Value))
                    throw PlmException.Invalid("PLM_AMBIGUOUS_ORDER");
            }
            var input = new StringBuilder();
            input.Append("{\"schemaVersion\":"); AppendQuoted(input, ItemSchema, false);
            input.Append(",\"tenantId\":"); AppendQuoted(input, tenantId.ToString("D").ToLowerInvariant(), false);
            AppendItemCore(input, item, identityKey);
            input.Append('}');
            var bytes = Encoding.UTF8.GetBytes(input.ToString());
            encoded.Add(new PlmEncodedItem(item, identityKey, bytes, PlmDigest.Sha256(bytes)));
        }
        var root = new StringBuilder();
        root.Append("{\"schemaVersion\":"); AppendQuoted(root, ManifestSchema, false);
        root.Append(",\"tenantId\":"); AppendQuoted(root, tenantId.ToString("D").ToLowerInvariant(), false);
        root.Append(",\"items\":[");
        for (var i = 0; i < encoded.Count; i++)
        {
            if (i > 0) root.Append(',');
            var item = encoded[i];
            root.Append('{');
            // The item digest input contains the same item core, without the prefix.
            AppendQuoted(root, "itemType", false); root.Append(':'); AppendQuoted(root, item.Projection.ItemType, false);
            root.Append(",\"sourceOwnerType\":"); AppendQuoted(root, item.Projection.SourceOwnerType, false);
            root.Append(",\"identity\":").Append(item.IdentityKey);
            root.Append(",\"content\":"); AppendContent(root, item.Projection.Content);
            root.Append(",\"itemDigest\":"); AppendQuoted(root, item.ItemDigest, false);
            root.Append('}');
        }
        root.Append("]}");
        var rootBytes = Encoding.UTF8.GetBytes(root.ToString());
        return new PlmEncodedManifest(rootBytes, PlmDigest.Sha256(rootBytes), encoded);
    }

    private static int CompareItems(PlmProjectedItem a, PlmProjectedItem b)
    {
        var rankA = Rank(a.ItemType); var rankB = Rank(b.ItemType);
        if (rankA != rankB) return rankA.CompareTo(rankB);
        var count = Math.Min(a.Identity.Count, b.Identity.Count);
        for (var i = 0; i < count; i++)
        {
            var left = Encoding.UTF8.GetBytes(Normalize(a.Identity[i].Value, true));
            var right = Encoding.UTF8.GetBytes(Normalize(b.Identity[i].Value, true));
            var diff = left.AsSpan().SequenceCompareTo(right);
            if (diff != 0) return diff;
        }
        return a.Identity.Count.CompareTo(b.Identity.Count);
    }

    private static int Rank(string type) => type switch { "PRODUCT" => 0, "BOM" => 1, "ROUTING" => 2, _ => 3 };

    private static void AppendItemCore(StringBuilder sb, PlmProjectedItem item, string identityKey)
    {
        sb.Append(",\"itemType\":"); AppendQuoted(sb, item.ItemType, false);
        sb.Append(",\"sourceOwnerType\":"); AppendQuoted(sb, item.SourceOwnerType, false);
        sb.Append(",\"identity\":").Append(identityKey);
        sb.Append(",\"content\":"); AppendContent(sb, item.Content);
    }

    private static void AppendIdentity(StringBuilder sb, IReadOnlyList<(string Name, string Value)> keys)
    {
        sb.Append('{');
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > 0) sb.Append(',');
            AppendQuoted(sb, keys[i].Name, false);
            sb.Append(':');
            var limit = keys[i].Name switch { "productCd" => 20, "processCd" or "taskCd" => 10, "materialCd" => 20, _ => 0 };
            var value = Normalize(keys[i].Value, true);
            if (value.Length == 0 || value.Length > limit) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
            AppendQuoted(sb, value, true);
        }
        sb.Append('}');
    }

    private static void AppendContent(StringBuilder sb, IReadOnlyList<PlmField> fields)
    {
        sb.Append('{');
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (!keys.Add(field.Name)) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
            if (keys.Count > 1) sb.Append(',');
            AppendQuoted(sb, field.Name, false); sb.Append(':');
            if (field.Value is null) { if (field.Required) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA"); sb.Append("null"); continue; }
            switch (field.Kind)
            {
                case "string":
                    if (field.Value is not string str) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
                    str = Normalize(str, false);
                    if (str.Length > field.Limit || (field.Required && str.Length == 0))
                        throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
                    AppendQuoted(sb, str, false);
                    break;
                case "integer":
                    if (field.Value is not int number || (field.Name == "trackingMode" && number is < 0 or > 3) || (field.Name == "usageType" && number is not (1 or 2)) || (field.Name == "sortOrder" && number <= 0))
                        throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
                    sb.Append(number.ToString(CultureInfo.InvariantCulture));
                    break;
                case "decimal":
                    if (field.Value is not decimal amount) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
                    var scale = (decimal.GetBits(amount)[3] >> 16) & 0xff;
                    var integralDigits = decimal.Truncate(decimal.Abs(amount)).ToString("0", CultureInfo.InvariantCulture).Length;
                    if (scale > 8 || integralDigits > field.Precision - 8 || (field.Name == "setRatio" && amount <= 0))
                        throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
                    sb.Append(amount == 0 ? "0" : amount.ToString("0.############################", CultureInfo.InvariantCulture));
                    break;
                case "boolean":
                    if (field.Value is not bool flag) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
                    sb.Append(flag ? "true" : "false");
                    break;
                default: throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
            }
        }
        sb.Append('}');
    }

    private static string Normalize(string value, bool identity)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch))
            {
                if (++i >= value.Length || !char.IsLowSurrogate(value[i])) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
            }
            else if (char.IsLowSurrogate(ch) || (identity && ch < 32)) throw PlmException.Invalid("PLM_CANONICAL_SCHEMA");
        }
        return value.Normalize(NormalizationForm.FormC);
    }

    public static void AppendQuoted(StringBuilder sb, string value, bool identity)
    {
        value = Normalize(value, identity);
        sb.Append('"');
        foreach (var ch in value)
        {
            if (ch == '"') sb.Append("\\\"");
            else if (ch == '\\') sb.Append("\\\\");
            else if (ch < 32) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
            else sb.Append(ch);
        }
        sb.Append('"');
    }
}
