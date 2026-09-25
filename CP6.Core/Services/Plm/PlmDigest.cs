using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Entity.DomainModels.Plm;
using CP6.Entity.DTOs.Plm;

namespace CP6.Core.Services.Plm;

public static class PlmDigest
{
    public static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string HashCommand(string schema, params (string Name, string Value, bool Number)[] fields)
    {
        var sb = new StringBuilder();
        sb.Append("{\"schemaVersion\":"); PlmCanonicalWriter.AppendQuoted(sb, schema, false);
        foreach (var field in fields)
        {
            sb.Append(','); PlmCanonicalWriter.AppendQuoted(sb, field.Name, false); sb.Append(':');
            if (field.Number) sb.Append(field.Value);
            else PlmCanonicalWriter.AppendQuoted(sb, field.Value, false);
        }
        sb.Append('}');
        return Sha256(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    public static PlmEncodedManifest Verify(PlmTechnicalManifest manifest, IReadOnlyList<PlmTechnicalManifestItem> stored)
    {
        try
        {
            if (manifest.SchemaVersion != PlmCanonicalWriter.ManifestSchema || manifest.ItemCount != stored.Count || stored.Count == 0)
                throw new InvalidOperationException("Manifest schema or item count mismatch.");
            var projected = stored.Select(item => Decode(manifest, item)).ToArray();
            var result = PlmCanonicalWriter.Encode(manifest.TenantId, projected);
            if (result.ManifestDigest != manifest.ManifestDigest || !result.CanonicalBytes.AsSpan().SequenceEqual(manifest.CanonicalBytes))
                throw new InvalidOperationException("Manifest bytes or digest mismatch.");
            foreach (var encoded in result.Items)
            {
                var item = stored.Single(x => x.ItemType == encoded.Projection.ItemType && x.IdentityKey == encoded.IdentityKey);
                if (item.ItemDigest != encoded.ItemDigest || !item.ItemCanonicalBytes.AsSpan().SequenceEqual(encoded.ItemCanonicalBytes))
                    throw new InvalidOperationException("Item bytes or digest mismatch.");
            }
            return result;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or PlmException or ArgumentException)
        {
            throw PlmException.Conflict("PLM_MANIFEST_INTEGRITY");
        }
    }

    private static PlmProjectedItem Decode(PlmTechnicalManifest manifest, PlmTechnicalManifestItem item)
    {
        if (item.TenantId != manifest.TenantId || item.ManifestId != manifest.Id || item.ProductCd != manifest.ProductCd)
            throw new InvalidOperationException("Item scope mismatch.");
        using var json = JsonDocument.Parse(item.ItemCanonicalBytes);
        var root = json.RootElement;
        RequireProperties(root, "schemaVersion", "tenantId", "itemType", "sourceOwnerType", "identity", "content");
        if (root.GetProperty("schemaVersion").GetString() != PlmCanonicalWriter.ItemSchema ||
            root.GetProperty("tenantId").GetString() != manifest.TenantId.ToString("D").ToLowerInvariant() ||
            root.GetProperty("itemType").GetString() != item.ItemType ||
            root.GetProperty("sourceOwnerType").GetString() != item.SourceOwnerType)
            throw new InvalidOperationException("Item header mismatch.");
        var names = item.ItemType switch
        {
            "PRODUCT" => new[] { "productCd" },
            "BOM" => new[] { "productCd", "processCd", "materialCd" },
            "ROUTING" => new[] { "productCd", "taskCd" },
            _ => throw new InvalidOperationException("Unknown item type.")
        };
        var identityObject = root.GetProperty("identity");
        RequireProperties(identityObject, names);
        var identity = names.Select(name => (Name: name, Value: identityObject.GetProperty(name).GetString() ?? throw new InvalidOperationException("Null key."))).ToArray();
        if (identity[0].Value != item.ProductCd ||
            item.ProcessCd != (item.ItemType == "BOM" ? identity[1].Value : null) ||
            item.MaterialCd != (item.ItemType == "BOM" ? identity[2].Value : null) ||
            item.TaskCd != (item.ItemType == "ROUTING" ? identity[1].Value : null))
            throw new InvalidOperationException("Typed key mismatch.");
        var schema = PlmSnapshotProjector.Schema(item.ItemType);
        var content = root.GetProperty("content");
        RequireProperties(content, schema.Select(x => x.Name).ToArray());
        var fields = schema.Select(def =>
        {
            var value = content.GetProperty(def.Name);
            object? typed = value.ValueKind == JsonValueKind.Null ? null : def.Kind switch
            {
                "string" when value.ValueKind == JsonValueKind.String => value.GetString(),
                "integer" when value.ValueKind == JsonValueKind.Number => value.GetInt32(),
                "decimal" when value.ValueKind == JsonValueKind.Number => value.GetDecimal(),
                "boolean" when value.ValueKind is JsonValueKind.True or JsonValueKind.False => value.GetBoolean(),
                _ => throw new InvalidOperationException("Wrong content type.")
            };
            return def with { Value = typed };
        }).ToArray();
        var order = item.ItemType == "PRODUCT" ? (int?)null : (int)fields.Single(x => x.Name == "sortOrder").Value!;
        if (order != item.SemanticSortOrder) throw new InvalidOperationException("Sort order mismatch.");
        return new PlmProjectedItem(item.ItemType, item.SourceOwnerType, identity, fields,
            item.SourceOwnerId, item.SourceRowVersion, item.ProductCd, item.ProcessCd, item.MaterialCd, item.TaskCd, order);
    }

    private static void RequireProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Expected object.");
        var actual = element.EnumerateObject().Select(x => x.Name).ToArray();
        if (!actual.SequenceEqual(names, StringComparer.Ordinal)) throw new InvalidOperationException("Unexpected JSON property.");
    }
}
