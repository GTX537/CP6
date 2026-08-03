using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class SpaceCadInventory
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static SpaceCadInventoryV1 Build(
        SpaceCadConversionRequest request,
        SpaceCadCoordinatePreparationV1 preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (!preparation.ReadyForParsing
            || preparation.Issues.Any(issue => issue.Severity == SpaceCadIssueSeverity.Blocking))
        {
            throw new InvalidDataException(
                "CAD inventory requires a coordinate preparation that is ready for parsing.");
        }

        SpaceCadConversionContract.ValidatePackage(request, preparation.Package);
        _ = SpaceCadCoordinatePreparation.SerializeMetadata(preparation.Metadata);
        if (!preparation.Metadata.SourceSha256.Equals(
                preparation.Package.Document.SourceSha256,
                StringComparison.Ordinal)
            || preparation.Metadata.PreparedBounds != preparation.Package.Document.Bounds
            || preparation.Metadata.TargetFloor.FloorLogicalId == Guid.Empty)
        {
            throw new InvalidDataException(
                "CAD inventory source, floor or prepared bounds do not match coordinate metadata.");
        }

        var entitiesByLayer = preparation.Package.Entities
            .GroupBy(entity => entity.LayerId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var layers = preparation.Package.Layers
            .OrderBy(layer => layer.Name, StringComparer.Ordinal)
            .ThenBy(layer => layer.LayerId, StringComparer.Ordinal)
            .Select(layer => Layer(
                layer,
                entitiesByLayer.GetValueOrDefault(layer.LayerId) ?? []))
            .ToArray();

        var blockReferences = preparation.Package.Entities
            .Where(entity => entity.Type == SpaceCadIrEntityType.BlockReference)
            .OrderBy(entity => entity.SourceRef, StringComparer.Ordinal)
            .Select(entity => new SpaceCadBlockReferenceInventoryV1(
                entity.SourceRef,
                entity.BlockName ?? "UNKNOWN",
                entity.LayerId,
                entity.IsSupported,
                SortedAttributes(entity.Attributes),
                entity.Bounds))
            .ToArray();

        var definitions = new Dictionary<string, SpaceCadIrBlockV1>(StringComparer.Ordinal);
        foreach (var definition in preparation.Package.Blocks)
        {
            if (!definitions.TryAdd(definition.Name, definition))
            {
                throw new InvalidDataException(
                    $"CAD block name '{definition.Name}' is not unique.");
            }
        }

        var referencesByBlock = blockReferences
            .GroupBy(reference => reference.BlockName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var blockNames = definitions.Keys
            .Concat(referencesByBlock.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var blocks = blockNames
            .Select(name => Block(
                name,
                definitions.GetValueOrDefault(name),
                referencesByBlock.GetValueOrDefault(name) ?? []))
            .ToArray();

        var summary = new SpaceCadInventorySummaryV1(
            layers.LongLength,
            layers.LongCount(layer => layer.EntityCount == 0),
            blocks.LongLength,
            blocks.LongCount(block => !block.IsDefined),
            blockReferences.LongLength,
            blockReferences.LongCount(reference => reference.Attributes.Count > 0),
            preparation.Package.Entities.Count,
            preparation.Package.Entities.LongCount(entity => entity.IsSupported),
            preparation.Package.Entities.LongCount(entity => !entity.IsSupported),
            preparation.Package.Document.Bounds);
        var withoutHash = new SpaceCadInventoryV1(
            SpaceCadInventoryVersions.SchemaVersion,
            preparation.Metadata.SourceSha256,
            preparation.Metadata.TransformSha256,
            preparation.Metadata.TargetFloor.FloorLogicalId,
            preparation.Metadata.TargetFloor.FloorCode,
            layers,
            blocks,
            blockReferences,
            summary,
            InventorySha256: string.Empty);
        var inventory = withoutHash with
        {
            InventorySha256 = ComputeSha256(CanonicalJson(withoutHash)),
        };
        Validate(inventory);
        return inventory;
    }

    public static string Serialize(SpaceCadInventoryV1 inventory)
    {
        Validate(inventory);
        return JsonSerializer.Serialize(inventory, CanonicalJsonOptions);
    }

    public static void Validate(SpaceCadInventoryV1 inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(inventory.Layers);
        ArgumentNullException.ThrowIfNull(inventory.Blocks);
        ArgumentNullException.ThrowIfNull(inventory.BlockReferences);
        ArgumentNullException.ThrowIfNull(inventory.Summary);
        if (inventory.SchemaVersion != SpaceCadInventoryVersions.SchemaVersion
            || !IsSha256(inventory.SourceSha256)
            || !IsSha256(inventory.CoordinateTransformSha256)
            || !IsSha256(inventory.InventorySha256)
            || inventory.FloorLogicalId == Guid.Empty
            || string.IsNullOrWhiteSpace(inventory.FloorCode)
            || inventory.FloorCode.Length > SpaceCadConversionContract.MaximumIdentifierLength
            || !inventory.FloorCode.Equals(inventory.FloorCode.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("CAD inventory identity is incomplete.");
        }
        if (!inventory.Layers.SequenceEqual(
                inventory.Layers
                    .OrderBy(layer => layer.Name, StringComparer.Ordinal)
                    .ThenBy(layer => layer.LayerId, StringComparer.Ordinal))
            || !inventory.Blocks.SequenceEqual(
                inventory.Blocks.OrderBy(block => block.Name, StringComparer.Ordinal))
            || !inventory.BlockReferences.SequenceEqual(
                inventory.BlockReferences.OrderBy(
                    reference => reference.SourceRef,
                    StringComparer.Ordinal)))
        {
            throw new InvalidDataException("CAD inventory records are not canonically ordered.");
        }

        var layerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in inventory.Layers)
        {
            RequireToken(layer.LayerId, nameof(layer.LayerId));
            RequireToken(layer.Name, nameof(layer.Name));
            RequireOptionalToken(layer.Color, nameof(layer.Color));
            RequireOptionalToken(layer.LineType, nameof(layer.LineType));
            ArgumentNullException.ThrowIfNull(layer.EntityTypeCounts);
            if (!layerIds.Add(layer.LayerId)
                || layer.EntityCount < 0
                || layer.SupportedEntityCount < 0
                || layer.UnsupportedEntityCount < 0
                || layer.BlockReferenceCount < 0
                || layer.AttributedEntityCount < 0
                || layer.SupportedEntityCount + layer.UnsupportedEntityCount != layer.EntityCount
                || layer.BlockReferenceCount > layer.EntityCount
                || layer.AttributedEntityCount > layer.EntityCount
                || layer.EntityTypeCounts.Values.Any(count => count < 0)
                || layer.EntityTypeCounts.Values.Sum() != layer.EntityCount)
            {
                throw new InvalidDataException("CAD layer inventory counts are inconsistent.");
            }
            foreach (var type in layer.EntityTypeCounts.Keys)
                RequireToken(type, "entity type");
            ValidateBounds(layer.Bounds);
        }

        var blockIds = new HashSet<string>(StringComparer.Ordinal);
        var blockNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in inventory.Blocks)
        {
            RequireToken(block.BlockId, nameof(block.BlockId));
            RequireToken(block.Name, nameof(block.Name));
            RequireOptionalToken(block.ExternalReferenceToken, nameof(block.ExternalReferenceToken));
            ArgumentNullException.ThrowIfNull(block.Attributes);
            if (!blockIds.Add(block.BlockId)
                || !blockNames.Add(block.Name)
                || block.DefinitionEntityCount < 0
                || block.ReferenceCount < 0
                || block.AttributedReferenceCount < 0
                || block.AttributedReferenceCount > block.ReferenceCount
                || (!block.IsDefined
                    && (block.DefinitionEntityCount != 0
                        || block.IsExternalReference
                        || block.ExternalReferenceToken is not null)))
            {
                throw new InvalidDataException("CAD block inventory is inconsistent.");
            }
            var attributeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var attribute in block.Attributes)
            {
                RequireToken(attribute.Name, nameof(attribute.Name));
                if (!attributeNames.Add(attribute.Name)
                    || attribute.ReferenceCount <= 0
                    || attribute.ReferenceCount > block.ReferenceCount
                    || attribute.DistinctValueCount <= 0
                    || attribute.DistinctValueCount > attribute.ReferenceCount)
                {
                    throw new InvalidDataException(
                        "CAD block attribute inventory is inconsistent.");
                }
            }
            if (!block.Attributes.SequenceEqual(
                    block.Attributes.OrderBy(
                        attribute => attribute.Name,
                        StringComparer.Ordinal)))
            {
                throw new InvalidDataException(
                    "CAD block attributes are not canonically ordered.");
            }
            ValidateBounds(block.ReferenceBounds);
        }

        var sourceRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in inventory.BlockReferences)
        {
            RequireSourceReference(reference.SourceRef);
            RequireToken(reference.BlockName, nameof(reference.BlockName));
            RequireToken(reference.LayerId, nameof(reference.LayerId));
            ArgumentNullException.ThrowIfNull(reference.Attributes);
            if (!sourceRefs.Add(reference.SourceRef)
                || !layerIds.Contains(reference.LayerId)
                || !blockNames.Contains(reference.BlockName)
                || reference.Attributes.Count > SpaceCadConversionContract.MaximumAttributeCount)
            {
                throw new InvalidDataException("CAD block reference inventory is inconsistent.");
            }
            foreach (var (key, value) in reference.Attributes)
            {
                if (string.IsNullOrWhiteSpace(key)
                    || key.Length > SpaceCadConversionContract.MaximumAttributeKeyLength
                    || value is null
                    || value.Length > SpaceCadConversionContract.MaximumAttributeValueLength)
                {
                    throw new InvalidDataException("CAD block reference attribute is invalid.");
                }
            }
            ValidateBounds(reference.Bounds);
        }

        foreach (var block in inventory.Blocks)
        {
            var references = inventory.BlockReferences
                .Where(reference => reference.BlockName.Equals(block.Name, StringComparison.Ordinal))
                .ToArray();
            if (block.ReferenceCount != references.LongLength
                || block.AttributedReferenceCount
                    != references.LongCount(reference => reference.Attributes.Count > 0)
                || block.ReferenceBounds != UnionBounds(references.Select(reference => reference.Bounds)))
            {
                throw new InvalidDataException("CAD block reference summary does not match records.");
            }
            var attributes = references
                .SelectMany(reference => reference.Attributes.Select(attribute => new
                {
                    attribute.Key,
                    attribute.Value,
                    reference.SourceRef,
                }))
                .GroupBy(attribute => attribute.Key, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new SpaceCadBlockAttributeInventoryV1(
                    group.Key,
                    group.Select(value => value.SourceRef).Distinct(StringComparer.Ordinal).LongCount(),
                    group.Select(value => value.Value).Distinct(StringComparer.Ordinal).LongCount()))
                .ToArray();
            if (!attributes.SequenceEqual(block.Attributes))
            {
                throw new InvalidDataException("CAD block attribute summary does not match records.");
            }
        }

        var summary = inventory.Summary;
        if (summary.LayerCount != inventory.Layers.Count
            || summary.EmptyLayerCount != inventory.Layers.LongCount(layer => layer.EntityCount == 0)
            || summary.BlockCount != inventory.Blocks.Count
            || summary.UndefinedBlockCount != inventory.Blocks.LongCount(block => !block.IsDefined)
            || summary.BlockReferenceCount != inventory.BlockReferences.Count
            || summary.AttributedBlockReferenceCount
                != inventory.BlockReferences.LongCount(reference => reference.Attributes.Count > 0)
            || summary.EntityCount != inventory.Layers.Sum(layer => layer.EntityCount)
            || summary.SupportedEntityCount != inventory.Layers.Sum(layer => layer.SupportedEntityCount)
            || summary.UnsupportedEntityCount != inventory.Layers.Sum(layer => layer.UnsupportedEntityCount)
            || summary.Bounds != UnionBounds(inventory.Layers.Select(layer => layer.Bounds)))
        {
            throw new InvalidDataException("CAD inventory summary does not match records.");
        }
        ValidateBounds(summary.Bounds);

        var expectedHash = ComputeSha256(CanonicalJson(inventory with { InventorySha256 = string.Empty }));
        if (!inventory.InventorySha256.Equals(expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("CAD inventory hash does not match its content.");
        }
    }

    public static SpaceCadInventoryPageV1<SpaceCadLayerInventoryV1> QueryLayers(
        SpaceCadInventoryV1 inventory,
        SpaceCadLayerInventoryQueryV1 query)
    {
        Validate(inventory);
        ArgumentNullException.ThrowIfNull(query);
        ValidateQuery(query.Search, query.Offset, query.Limit);
        var values = inventory.Layers.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            values = values.Where(layer =>
                Contains(layer.Name, query.Search) || Contains(layer.LayerId, query.Search));
        }
        if (query.IsVisible is { } visible)
            values = values.Where(layer => layer.IsVisible == visible);
        if (query.EntityType is { } type)
            values = values.Where(layer => layer.EntityTypeCounts.ContainsKey(type.ToString()));
        if (!query.IncludeEmpty)
            values = values.Where(layer => layer.EntityCount > 0);
        return Page(values, query.Offset, query.Limit);
    }

    public static SpaceCadInventoryPageV1<SpaceCadBlockInventoryV1> QueryBlocks(
        SpaceCadInventoryV1 inventory,
        SpaceCadBlockInventoryQueryV1 query)
    {
        Validate(inventory);
        ArgumentNullException.ThrowIfNull(query);
        ValidateQuery(query.Search, query.Offset, query.Limit);
        RequireOptionalQueryToken(query.AttributeName, nameof(query.AttributeName));
        var values = inventory.Blocks.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            values = values.Where(block =>
                Contains(block.Name, query.Search) || Contains(block.BlockId, query.Search));
        }
        if (query.IsExternalReference is { } external)
            values = values.Where(block => block.IsExternalReference == external);
        if (!string.IsNullOrWhiteSpace(query.AttributeName))
        {
            values = values.Where(block => block.Attributes.Any(attribute =>
                attribute.Name.Equals(query.AttributeName, StringComparison.OrdinalIgnoreCase)));
        }
        return Page(values, query.Offset, query.Limit);
    }

    public static SpaceCadInventoryPageV1<SpaceCadBlockReferenceInventoryV1> QueryBlockReferences(
        SpaceCadInventoryV1 inventory,
        SpaceCadBlockReferenceInventoryQueryV1 query)
    {
        Validate(inventory);
        ArgumentNullException.ThrowIfNull(query);
        ValidateQuery(search: null, query.Offset, query.Limit);
        RequireOptionalQueryToken(query.LayerId, nameof(query.LayerId));
        RequireOptionalQueryToken(query.BlockName, nameof(query.BlockName));
        RequireOptionalQueryToken(query.AttributeName, nameof(query.AttributeName));
        if (query.AttributeValue is not null
            && query.AttributeValue.Length > SpaceCadConversionContract.MaximumAttributeValueLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                "CAD attribute query value is too long.");
        }

        var values = inventory.BlockReferences.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.LayerId))
        {
            values = values.Where(reference =>
                reference.LayerId.Equals(query.LayerId, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(query.BlockName))
        {
            values = values.Where(reference =>
                reference.BlockName.Equals(query.BlockName, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(query.AttributeName))
        {
            values = values.Where(reference => reference.Attributes.Any(attribute =>
                attribute.Key.Equals(query.AttributeName, StringComparison.OrdinalIgnoreCase)
                && (query.AttributeValue is null
                    || attribute.Value.Equals(
                        query.AttributeValue,
                        StringComparison.OrdinalIgnoreCase))));
        }
        else if (query.AttributeValue is not null)
        {
            throw new ArgumentException(
                "An attribute name is required when filtering by attribute value.",
                nameof(query));
        }
        return Page(values, query.Offset, query.Limit);
    }

    private static SpaceCadLayerInventoryV1 Layer(
        SpaceCadIrLayerV1 layer,
        IReadOnlyList<SpaceCadIrEntityV1> entities)
    {
        var typeCounts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var group in entities.GroupBy(entity => entity.Type))
            typeCounts[group.Key.ToString()] = group.LongCount();
        return new SpaceCadLayerInventoryV1(
            layer.LayerId,
            layer.Name,
            layer.Color,
            layer.LineType,
            layer.IsVisible,
            entities.Count,
            entities.LongCount(entity => entity.IsSupported),
            entities.LongCount(entity => !entity.IsSupported),
            entities.LongCount(entity => entity.Type == SpaceCadIrEntityType.BlockReference),
            entities.LongCount(entity => entity.Attributes.Count > 0),
            typeCounts,
            UnionBounds(entities.Select(entity => entity.Bounds)));
    }

    private static SpaceCadBlockInventoryV1 Block(
        string name,
        SpaceCadIrBlockV1? definition,
        IReadOnlyList<SpaceCadBlockReferenceInventoryV1> references)
    {
        var attributes = references
            .SelectMany(reference => reference.Attributes.Select(attribute => new
            {
                attribute.Key,
                attribute.Value,
                reference.SourceRef,
            }))
            .GroupBy(attribute => attribute.Key, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new SpaceCadBlockAttributeInventoryV1(
                group.Key,
                group.Select(value => value.SourceRef).Distinct(StringComparer.Ordinal).LongCount(),
                group.Select(value => value.Value).Distinct(StringComparer.Ordinal).LongCount()))
            .ToArray();
        return new SpaceCadBlockInventoryV1(
            definition?.BlockId ?? $"U:{HashToken(name)}",
            name,
            IsDefined: definition is not null,
            definition?.IsExternalReference ?? false,
            definition?.ExternalReferenceToken,
            definition?.EntityCount ?? 0,
            references.Count,
            references.LongCount(reference => reference.Attributes.Count > 0),
            attributes,
            UnionBounds(references.Select(reference => reference.Bounds)));
    }

    private static SpaceCadInventoryPageV1<T> Page<T>(
        IEnumerable<T> source,
        int offset,
        int limit)
    {
        var values = source.ToArray();
        return new SpaceCadInventoryPageV1<T>(
            offset,
            limit,
            values.LongLength,
            values.Skip(offset).Take(limit).ToArray());
    }

    private static IReadOnlyDictionary<string, string> SortedAttributes(
        IReadOnlyDictionary<string, string> attributes)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in attributes)
            values.Add(key, value);
        return values;
    }

    private static void ValidateQuery(string? search, int offset, int limit)
    {
        if (search is not null
            && (string.IsNullOrWhiteSpace(search)
                || search.Length > SpaceCadConversionContract.MaximumIdentifierLength))
        {
            throw new ArgumentException("CAD inventory search text is invalid.", nameof(search));
        }
        if (offset < 0 || limit is <= 0 or > SpaceCadInventoryVersions.MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"CAD inventory pages must contain 1-{SpaceCadInventoryVersions.MaximumPageSize} items.");
        }
    }

    private static void RequireOptionalQueryToken(string? value, string name)
    {
        if (value is not null
            && (string.IsNullOrWhiteSpace(value)
                || value.Length > SpaceCadConversionContract.MaximumIdentifierLength))
        {
            throw new ArgumentException("CAD inventory query token is invalid.", name);
        }
    }

    private static bool Contains(string value, string search) =>
        value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static SpaceCadBoundsV1? UnionBounds(IEnumerable<SpaceCadBoundsV1?> values)
    {
        var bounds = values.Where(value => value is not null).Cast<SpaceCadBoundsV1>().ToArray();
        return bounds.Length == 0
            ? null
            : new SpaceCadBoundsV1(
                bounds.Min(value => value.MinX),
                bounds.Min(value => value.MinY),
                bounds.Max(value => value.MaxX),
                bounds.Max(value => value.MaxY));
    }

    private static void ValidateBounds(SpaceCadBoundsV1? bounds)
    {
        if (bounds is not null
            && (bounds.MinX > bounds.MaxX || bounds.MinY > bounds.MaxY))
        {
            throw new InvalidDataException("CAD inventory bounds are inverted.");
        }
    }

    private static void RequireToken(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > SpaceCadConversionContract.MaximumIdentifierLength)
        {
            throw new InvalidDataException($"CAD inventory {name} is invalid.");
        }
    }

    private static void RequireOptionalToken(string? value, string name)
    {
        if (value is not null)
            RequireToken(value, name);
    }

    private static void RequireSourceReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > SpaceCadConversionContract.MaximumSourceReferenceLength)
        {
            throw new InvalidDataException("CAD inventory source reference is invalid.");
        }
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string CanonicalJson(SpaceCadInventoryV1 inventory) =>
        JsonSerializer.Serialize(inventory, CanonicalJsonOptions);

    private static string HashToken(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 12))
            .ToLowerInvariant();

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
