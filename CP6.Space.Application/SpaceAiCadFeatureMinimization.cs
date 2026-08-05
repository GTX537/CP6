using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class SpaceAiCadFeatureVersions
{
    public const int SourceMapSchemaVersion = 1;
    public const int NormalizedGeometryPrecision = 4;
    public const int MinimumHmacKeyBytes = 32;
    public const int MaximumHmacKeyBytes = 128;
    public const int MaximumFeatures = 1_000_000;
}

public sealed record SpaceAiCadMappingHintV1(
    string LocalToken,
    WarehouseSpaceType TargetType,
    decimal Strength);

public sealed record SpaceAiCadLockedFactV1(
    string SourceRef,
    string FieldPath,
    string ValueToken);

public sealed record SpaceAiCadFeatureSourceMapEntryV1(
    string SourceKey,
    IReadOnlyList<string> SourceRefs);

public sealed record SpaceAiCadFeatureSourceMapV1(
    int SchemaVersion,
    bool IsLocalOnly,
    string SourceSha256,
    string CoordinateTransformSha256,
    Guid FloorLogicalId,
    SpaceAiDataPolicy Policy,
    string ProviderInputSha256,
    long FeatureCount,
    long MappedSourceCount,
    IReadOnlyList<SpaceAiCadFeatureSourceMapEntryV1> Entries,
    string SourceMapSha256);

public sealed record SpaceAiCadFeatureMinimizationV1(
    WarehouseGenerationInput ProviderInput,
    SpaceAiCadFeatureSourceMapV1 LocalSourceMap);

public static class SpaceAiCadFeatureMinimizer
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly IReadOnlyDictionary<string, string> SafeLexemes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AGV"] = "agv",
            ["AISLE"] = "aisle",
            ["CHARGER"] = "charger",
            ["COLUMN"] = "column",
            ["CONVEYOR"] = "conveyor",
            ["DOCK"] = "dock",
            ["DOOR"] = "door",
            ["EQUIPMENT"] = "equipment",
            ["FLOOR"] = "floor",
            ["FORKLIFT"] = "forklift",
            ["PACKING"] = "packing",
            ["PASSAGE"] = "passage",
            ["PICKING"] = "picking",
            ["RACK"] = "rack",
            ["RECEIVING"] = "receiving",
            ["ROOM"] = "room",
            ["SCALE"] = "scale",
            ["SHELF"] = "shelf",
            ["SHIPPING"] = "shipping",
            ["STAGING"] = "staging",
            ["STORAGE"] = "storage",
            ["WALL"] = "wall",
            ["WORKSTATION"] = "workstation",
            ["ZONE"] = "zone",
        };

    private static readonly IReadOnlyDictionary<string, Type> LockedFieldEnums =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["type"] = typeof(WarehouseSpaceType),
            ["attributes.zonePurpose"] = typeof(WarehouseZonePurpose),
            ["attributes.rackType"] = typeof(WarehouseRackType),
            ["attributes.doorType"] = typeof(WarehouseDoorType),
            ["attributes.dockType"] = typeof(WarehouseDockType),
            ["attributes.equipmentType"] = typeof(WarehouseEquipmentType),
        };

    public static SpaceAiCadFeatureMinimizationV1 Minimize(
        SpaceCadConversionRequest request,
        SpaceCadCoordinatePreparationV1 preparation,
        SpaceAiDataPolicy policy,
        ReadOnlySpan<byte> hmacKey,
        Guid siteId,
        Guid modelVersionId,
        Guid runId,
        WarehouseGenerationLimits limits,
        IReadOnlyList<SpaceAiCadMappingHintV1>? mappingHints = null,
        IReadOnlyList<SpaceAiCadLockedFactV1>? lockedFacts = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(limits);
        if (!Enum.IsDefined(policy) || policy == SpaceAiDataPolicy.Disabled)
            throw new ArgumentOutOfRangeException(nameof(policy));
        if (request.TenantId == Guid.Empty
            || siteId == Guid.Empty
            || modelVersionId == Guid.Empty
            || runId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant, site, model version and run identities are required.");
        }
        var key = CopyHmacKey(hmacKey);
        try
        {
            ValidatePreparation(request, preparation);
            if (preparation.Package.Entities.Count >
                SpaceAiCadFeatureVersions.MaximumFeatures)
            {
                throw new InvalidDataException(
                    "CAD feature input exceeds the provider feature limit.");
            }

            var runCorrelationKey = Token(
                key,
                "run-correlation",
                string.Join(
                    '\n',
                    request.TenantId.ToString("D"),
                    siteId.ToString("D"),
                    modelVersionId.ToString("D"),
                    runId.ToString("D")),
                "run-",
                64);
            var entityFeatures = BuildEntityFeatures(
                preparation,
                key,
                runId,
                policy);
            var (features, sourceMapEntries) = policy switch
            {
                SpaceAiDataPolicy.MetadataOnly => BuildMetadataFeatures(
                    entityFeatures,
                    key,
                    runId),
                SpaceAiDataPolicy.StructuredFeatures => BuildStructuredFeatures(
                    entityFeatures),
                _ => throw new ArgumentOutOfRangeException(nameof(policy)),
            };
            var providerHints = BuildMappingHints(
                mappingHints ?? [],
                key,
                runId);
            var providerLockedFacts = BuildLockedFacts(
                lockedFacts ?? [],
                policy,
                entityFeatures);
            var providerInput = new WarehouseGenerationInput(
                runCorrelationKey,
                policy,
                limits,
                features,
                providerHints,
                providerLockedFacts);
            var providerInputSha256 = ComputeSha256(CanonicalJson(providerInput));
            var sourceMapWithoutHash = new SpaceAiCadFeatureSourceMapV1(
                SpaceAiCadFeatureVersions.SourceMapSchemaVersion,
                IsLocalOnly: true,
                preparation.Metadata.SourceSha256,
                preparation.Metadata.TransformSha256,
                preparation.Metadata.TargetFloor.FloorLogicalId,
                policy,
                providerInputSha256,
                features.LongLength,
                sourceMapEntries.Sum(item => (long)item.SourceRefs.Count),
                sourceMapEntries,
                SourceMapSha256: string.Empty);
            var sourceMap = sourceMapWithoutHash with
            {
                SourceMapSha256 = ComputeSha256(CanonicalJson(sourceMapWithoutHash)),
            };
            var result = new SpaceAiCadFeatureMinimizationV1(
                providerInput,
                sourceMap);
            Validate(result);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static string CreateRunCorrelationKey(
        ReadOnlySpan<byte> hmacKey,
        Guid tenantId,
        Guid siteId,
        Guid modelVersionId,
        Guid runId)
    {
        if (tenantId == Guid.Empty
            || siteId == Guid.Empty
            || modelVersionId == Guid.Empty
            || runId == Guid.Empty)
        {
            throw new ArgumentException("Correlation identities are required.");
        }
        var key = CopyHmacKey(hmacKey);
        try
        {
            return Token(
                key,
                "run-correlation",
                string.Join(
                    '\n',
                    tenantId.ToString("D"),
                    siteId.ToString("D"),
                    modelVersionId.ToString("D"),
                    runId.ToString("D")),
                "run-",
                64);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static string SerializeProviderInput(
        SpaceAiCadFeatureMinimizationV1 result)
    {
        Validate(result);
        return CanonicalJson(result.ProviderInput);
    }

    public static string SerializeLocalSourceMap(
        SpaceAiCadFeatureMinimizationV1 result)
    {
        Validate(result);
        return CanonicalJson(result.LocalSourceMap);
    }

    public static void Validate(SpaceAiCadFeatureMinimizationV1 result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(result.ProviderInput);
        ArgumentNullException.ThrowIfNull(result.LocalSourceMap);
        _ = new WarehouseGenerationInput(
            result.ProviderInput.RunCorrelationKey,
            result.ProviderInput.Policy,
            result.ProviderInput.Limits,
            result.ProviderInput.Features,
            result.ProviderInput.MappingHints,
            result.ProviderInput.LockedFacts);
        var map = result.LocalSourceMap;
        ArgumentNullException.ThrowIfNull(map.Entries);
        var expectedProviderHash = ComputeSha256(CanonicalJson(result.ProviderInput));
        var canonicalEntries = map.Entries
            .OrderBy(item => item.SourceKey, StringComparer.Ordinal)
            .ToArray();
        if (map.SchemaVersion != SpaceAiCadFeatureVersions.SourceMapSchemaVersion
            || !map.IsLocalOnly
            || !IsSha256(map.SourceSha256)
            || !IsSha256(map.CoordinateTransformSha256)
            || map.FloorLogicalId == Guid.Empty
            || map.Policy != result.ProviderInput.Policy
            || !map.ProviderInputSha256.Equals(
                expectedProviderHash,
                StringComparison.Ordinal)
            || map.FeatureCount != result.ProviderInput.Features.Count
            || map.MappedSourceCount != map.Entries.Sum(
                item => (long)item.SourceRefs.Count)
            || !map.Entries.SequenceEqual(canonicalEntries)
            || !IsSha256(map.SourceMapSha256))
        {
            throw new InvalidDataException(
                "Local CAD feature source map identity is invalid.");
        }

        var featureKeys = result.ProviderInput.Features
            .Select(item => item.SourceKey)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var mapKeys = map.Entries
            .Select(item => item.SourceKey)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var sourceRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in map.Entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(entry.SourceRefs);
            if (entry.SourceRefs.Count == 0
                || !entry.SourceRefs.SequenceEqual(entry.SourceRefs
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal))
                || entry.SourceRefs.Any(sourceRef =>
                    string.IsNullOrWhiteSpace(sourceRef)
                    || !sourceRef.Equals(sourceRef.Trim(), StringComparison.Ordinal)
                    || !sourceRefs.Add(sourceRef)))
            {
                throw new InvalidDataException(
                    "Local CAD feature source map entry is invalid.");
            }
        }
        if (!featureKeys.SequenceEqual(mapKeys, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Provider features and local source map do not match.");
        }
        var expectedMapHash = ComputeSha256(CanonicalJson(
            map with { SourceMapSha256 = string.Empty }));
        if (!map.SourceMapSha256.Equals(expectedMapHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Local CAD feature source map hash is invalid.");
        }
    }

    private static void ValidatePreparation(
        SpaceCadConversionRequest request,
        SpaceCadCoordinatePreparationV1 preparation)
    {
        if (!preparation.ReadyForParsing
            || preparation.Issues.Any(issue =>
                issue.Severity == SpaceCadIssueSeverity.Blocking))
        {
            throw new InvalidDataException(
                "AI CAD minimization requires a parsing-ready coordinate package.");
        }
        SpaceCadConversionContract.ValidatePackage(request, preparation.Package);
        _ = SpaceCadCoordinatePreparation.SerializeMetadata(preparation.Metadata);
        if (!request.SourceSha256.Equals(
                preparation.Metadata.SourceSha256,
                StringComparison.Ordinal)
            || !preparation.Metadata.SourceSha256.Equals(
                preparation.Package.Document.SourceSha256,
                StringComparison.Ordinal)
            || preparation.Metadata.PreparedBounds !=
                preparation.Package.Document.Bounds
            || preparation.Metadata.TargetFloor.FloorLogicalId == Guid.Empty)
        {
            throw new InvalidDataException(
                "AI CAD minimization source, transform or floor chain is invalid.");
        }
    }

    private static EntityFeature[] BuildEntityFeatures(
        SpaceCadCoordinatePreparationV1 preparation,
        byte[] key,
        Guid runId,
        SpaceAiDataPolicy policy)
    {
        var layers = new Dictionary<string, SpaceCadIrLayerV1>(
            StringComparer.Ordinal);
        foreach (var layer in preparation.Package.Layers)
        {
            if (!layers.TryAdd(layer.LayerId, layer))
                throw new InvalidDataException("CAD layer identity is duplicated.");
        }
        var blockCounts = preparation.Package.Entities
            .Where(item => item.BlockName is not null)
            .GroupBy(item => item.BlockName!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
        var entities = new List<EntityFeature>(preparation.Package.Entities.Count);
        foreach (var entity in preparation.Package.Entities)
        {
            if (!layers.TryGetValue(entity.LayerId, out var layer))
                throw new InvalidDataException("CAD entity references an unknown layer.");
            var sourceKey = Token(
                key,
                "source-key",
                $"{runId:D}\n{preparation.Metadata.SourceSha256}\n{entity.SourceRef}",
                "source-",
                32);
            var layerToken = NamedToken(
                key,
                "layer-token",
                runId,
                $"{layer.LayerId}\n{layer.Name}",
                layer.Name,
                "layer-");
            var blockToken = entity.BlockName is null
                ? null
                : NamedToken(
                    key,
                    "block-token",
                    runId,
                    entity.BlockName,
                    entity.BlockName,
                    "block-");
            var repetitionGroup = entity.BlockName is not null
                && blockCounts.GetValueOrDefault(entity.BlockName) > 1
                    ? Token(
                        key,
                        "repetition-group",
                        $"{runId:D}\n{entity.BlockName}",
                        "repeat-",
                        24)
                    : null;
            var attributeTokens = entity.Attributes.Keys
                .Select(attribute => Token(
                    key,
                    "attribute-key",
                    $"{runId:D}\n{attribute}",
                    "attribute-",
                    24))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Take(64)
                .ToArray();
            entities.Add(new EntityFeature(
                entity.SourceRef,
                sourceKey,
                ToProviderType(entity),
                layerToken,
                blockToken,
                AngleBucket(entity),
                AspectRatioBucket(entity.Bounds),
                repetitionGroup,
                attributeTokens,
                policy == SpaceAiDataPolicy.StructuredFeatures
                    ? NormalizeBounds(
                        entity.Bounds,
                        preparation.Package.Document.Bounds)
                    : null));
        }
        return entities
            .OrderBy(item => item.SourceKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static (
        WarehouseGenerationFeature[] Features,
        SpaceAiCadFeatureSourceMapEntryV1[] SourceMapEntries)
        BuildMetadataFeatures(
            IReadOnlyList<EntityFeature> entityFeatures,
            byte[] key,
            Guid runId)
    {
        var pairs = entityFeatures
            .GroupBy(item => new MetadataGroupKey(
                item.CadEntityType,
                item.LayerToken,
                item.BlockToken,
                item.AngleBucket,
                item.AspectRatioBucket,
                item.RepetitionGroup))
            .Select(group =>
            {
                var identity = string.Join(
                    '\n',
                    runId.ToString("D"),
                    group.Key.CadEntityType,
                    group.Key.LayerToken,
                    group.Key.BlockToken ?? "-",
                    group.Key.AngleBucket,
                    group.Key.AspectRatioBucket?.ToString() ?? "-",
                    group.Key.RepetitionGroup ?? "-");
                var sourceKey = Token(
                    key,
                    "metadata-group",
                    identity,
                    "group-",
                    32);
                var feature = new WarehouseGenerationFeature(
                    sourceKey,
                    group.Key.CadEntityType,
                    group.Key.LayerToken,
                    group.Key.BlockToken,
                    group.Count(),
                    NormalizedBounds: null,
                    group.Key.AngleBucket,
                    group.Key.RepetitionGroup,
                    group.SelectMany(item => item.AttributeKeyTokens)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .Take(64)
                        .ToArray(),
                    RelationSourceKeys: [],
                    group.Key.AspectRatioBucket);
                var map = new SpaceAiCadFeatureSourceMapEntryV1(
                    sourceKey,
                    group.Select(item => item.SourceRef)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray());
                return (Feature: feature, Map: map);
            })
            .OrderBy(item => item.Feature.SourceKey, StringComparer.Ordinal)
            .ToArray();
        return (
            pairs.Select(item => item.Feature).ToArray(),
            pairs.Select(item => item.Map).ToArray());
    }

    private static (
        WarehouseGenerationFeature[] Features,
        SpaceAiCadFeatureSourceMapEntryV1[] SourceMapEntries)
        BuildStructuredFeatures(IReadOnlyList<EntityFeature> entityFeatures)
    {
        var relations = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var group in entityFeatures
            .Where(item => item.RepetitionGroup is not null)
            .GroupBy(item => item.RepetitionGroup!, StringComparer.Ordinal))
        {
            var ordered = group
                .OrderBy(item => item.SourceKey, StringComparer.Ordinal)
                .ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                var adjacent = new List<string>(2);
                if (index > 0) adjacent.Add(ordered[index - 1].SourceKey);
                if (index + 1 < ordered.Length)
                    adjacent.Add(ordered[index + 1].SourceKey);
                relations[ordered[index].SourceKey] = adjacent
                    .Order(StringComparer.Ordinal)
                    .ToArray();
            }
        }
        var features = entityFeatures
            .Select(item => new WarehouseGenerationFeature(
                item.SourceKey,
                item.CadEntityType,
                item.LayerToken,
                item.BlockToken,
                1,
                item.NormalizedBounds,
                item.AngleBucket,
                item.RepetitionGroup,
                item.AttributeKeyTokens,
                relations.GetValueOrDefault(item.SourceKey) ?? [],
                item.AspectRatioBucket))
            .OrderBy(item => item.SourceKey, StringComparer.Ordinal)
            .ToArray();
        var sourceMap = entityFeatures
            .Select(item => new SpaceAiCadFeatureSourceMapEntryV1(
                item.SourceKey,
                [item.SourceRef]))
            .OrderBy(item => item.SourceKey, StringComparer.Ordinal)
            .ToArray();
        return (features, sourceMap);
    }

    private static WarehouseGenerationMappingHint[] BuildMappingHints(
        IReadOnlyList<SpaceAiCadMappingHintV1> hints,
        byte[] key,
        Guid runId)
    {
        if (hints.Count > 10_000)
            throw new ArgumentOutOfRangeException(nameof(hints));
        return hints
            .Select(hint =>
            {
                ArgumentNullException.ThrowIfNull(hint);
                if (!IsLocalToken(hint.LocalToken, 512)
                    || !Enum.IsDefined(hint.TargetType)
                    || hint.Strength is < 0 or > 1)
                {
                    throw new InvalidDataException(
                        "CAD AI mapping hint is invalid.");
                }
                return new WarehouseGenerationMappingHint(
                    Token(
                        key,
                        "mapping-hint",
                        $"{runId:D}\n{hint.LocalToken}\n{hint.TargetType}",
                        "hint-",
                        24),
                    hint.TargetType,
                    hint.Strength);
            })
            .DistinctBy(
                item => $"{item.Token}\n{item.TargetType}",
                StringComparer.Ordinal)
            .OrderBy(item => item.Token, StringComparer.Ordinal)
            .ThenBy(item => item.TargetType)
            .ToArray();
    }

    private static WarehouseGenerationLockedFact[] BuildLockedFacts(
        IReadOnlyList<SpaceAiCadLockedFactV1> facts,
        SpaceAiDataPolicy policy,
        IReadOnlyList<EntityFeature> entityFeatures)
    {
        if (facts.Count == 0) return [];
        if (policy != SpaceAiDataPolicy.StructuredFeatures)
        {
            throw new InvalidDataException(
                "Metadata-only CAD features cannot carry object-level locked facts.");
        }
        var sourceKeys = entityFeatures.ToDictionary(
            item => item.SourceRef,
            item => item.SourceKey,
            StringComparer.Ordinal);
        var result = new List<WarehouseGenerationLockedFact>(facts.Count);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fact in facts)
        {
            ArgumentNullException.ThrowIfNull(fact);
            if (!sourceKeys.TryGetValue(fact.SourceRef, out var sourceKey)
                || !LockedFieldEnums.TryGetValue(
                    fact.FieldPath,
                    out var enumType)
                || !TryCanonicalEnum(enumType, fact.ValueToken, out var valueToken)
                || !identities.Add($"{sourceKey}\n{fact.FieldPath}"))
            {
                throw new InvalidDataException(
                    "CAD AI locked fact is invalid or not allowlisted.");
            }
            result.Add(new WarehouseGenerationLockedFact(
                sourceKey,
                fact.FieldPath,
                valueToken));
        }
        return result
            .OrderBy(item => item.SourceKey, StringComparer.Ordinal)
            .ThenBy(item => item.FieldPath, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryCanonicalEnum(
        Type enumType,
        string value,
        out string canonical)
    {
        canonical = string.Empty;
        if (!IsLocalToken(value, 64)
            || !Enum.TryParse(enumType, value, ignoreCase: false, out var parsed)
            || parsed is null
            || !Enum.IsDefined(enumType, parsed))
        {
            return false;
        }
        canonical = parsed.ToString()!;
        return true;
    }

    private static WarehouseCadEntityType ToProviderType(
        SpaceCadIrEntityV1 entity)
    {
        if (!entity.IsSupported) return WarehouseCadEntityType.Unknown;
        return entity.Type switch
        {
            SpaceCadIrEntityType.Line => WarehouseCadEntityType.Line,
            SpaceCadIrEntityType.Polyline => WarehouseCadEntityType.Polyline,
            SpaceCadIrEntityType.ClosedPolyline =>
                WarehouseCadEntityType.ClosedPolyline,
            SpaceCadIrEntityType.Circle => WarehouseCadEntityType.Circle,
            SpaceCadIrEntityType.Arc => WarehouseCadEntityType.Arc,
            SpaceCadIrEntityType.BlockReference =>
                WarehouseCadEntityType.BlockReference,
            SpaceCadIrEntityType.Text => WarehouseCadEntityType.TextToken,
            _ => WarehouseCadEntityType.Unknown,
        };
    }

    private static int AngleBucket(SpaceCadIrEntityV1 entity)
    {
        var first = entity.Points.FirstOrDefault();
        var second = entity.Points.Skip(1).FirstOrDefault(point => point != first);
        double radians;
        if (first is not null && second is not null)
        {
            radians = Math.Atan2(
                decimal.ToDouble(second.Y - first.Y),
                decimal.ToDouble(second.X - first.X));
        }
        else
        {
            radians = Math.Atan2(
                decimal.ToDouble(entity.Transform.M21),
                decimal.ToDouble(entity.Transform.M11));
        }
        var degrees = (radians * 180d / Math.PI + 360d) % 360d;
        return Math.Clamp((int)Math.Floor(degrees / 10d), 0, 35);
    }

    private static int? AspectRatioBucket(SpaceCadBoundsV1? bounds)
    {
        if (bounds is null) return null;
        var width = bounds.MaxX - bounds.MinX;
        var height = bounds.MaxY - bounds.MinY;
        if (width <= 0 || height <= 0) return null;
        var ratio = decimal.ToDouble(Math.Max(width, height))
                    / decimal.ToDouble(Math.Min(width, height));
        return Math.Clamp((int)Math.Floor(Math.Log2(ratio)), 0, 8);
    }

    private static WarehouseNormalizedBounds? NormalizeBounds(
        SpaceCadBoundsV1? item,
        SpaceCadBoundsV1? floor)
    {
        if (item is null || floor is null) return null;
        var floorWidth = floor.MaxX - floor.MinX;
        var floorHeight = floor.MaxY - floor.MinY;
        var itemWidth = item.MaxX - item.MinX;
        var itemHeight = item.MaxY - item.MinY;
        if (floorWidth <= 0
            || floorHeight <= 0
            || itemWidth <= 0
            || itemHeight <= 0)
        {
            return null;
        }
        var x = Quantize(Clamp01((item.MinX - floor.MinX) / floorWidth));
        var y = Quantize(Clamp01((item.MinY - floor.MinY) / floorHeight));
        var width = Quantize(Clamp01(itemWidth / floorWidth));
        var height = Quantize(Clamp01(itemHeight / floorHeight));
        width = Math.Min(width, Quantize(1 - x));
        height = Math.Min(height, Quantize(1 - y));
        return width > 0 && height > 0
            ? new WarehouseNormalizedBounds(x, y, width, height)
            : null;
    }

    private static decimal Quantize(decimal value) => decimal.Round(
        value,
        SpaceAiCadFeatureVersions.NormalizedGeometryPrecision,
        MidpointRounding.AwayFromZero);

    private static decimal Clamp01(decimal value) =>
        Math.Min(1m, Math.Max(0m, value));

    private static string NamedToken(
        byte[] key,
        string domain,
        Guid runId,
        string identity,
        string rawName,
        string prefix)
    {
        var category = SafeCategory(rawName);
        var hash = Hmac(key, domain, $"{runId:D}\n{identity}")[..24];
        return $"{prefix}{category}-{hash}";
    }

    private static string SafeCategory(string value)
    {
        var tokens = new List<string>();
        var buffer = new StringBuilder();
        void Flush()
        {
            if (buffer.Length == 0) return;
            if (SafeLexemes.TryGetValue(
                    buffer.ToString().ToUpperInvariant(),
                    out var safe))
            {
                tokens.Add(safe);
            }
            buffer.Clear();
        }
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsAsciiLetterOrDigit(character))
                buffer.Append(character);
            else
                Flush();
        }
        Flush();
        var safeTokens = tokens
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return safeTokens.Length == 0
            ? "generic"
            : string.Join('-', safeTokens);
    }

    private static string Token(
        byte[] key,
        string domain,
        string value,
        string prefix,
        int hexadecimalCharacters) =>
        prefix + Hmac(key, domain, value)[..hexadecimalCharacters];

    private static string Hmac(byte[] key, string domain, string value)
    {
        using var hmac = new HMACSHA256(key);
        var bytes = Encoding.UTF8.GetBytes($"cp6-space-ai-v1\n{domain}\n{value}");
        return Convert.ToHexString(hmac.ComputeHash(bytes)).ToLowerInvariant();
    }

    private static byte[] CopyHmacKey(ReadOnlySpan<byte> hmacKey)
    {
        if (hmacKey.Length is < SpaceAiCadFeatureVersions.MinimumHmacKeyBytes
            or > SpaceAiCadFeatureVersions.MaximumHmacKeyBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hmacKey),
                "The CAD AI HMAC key must contain 32-128 bytes.");
        }
        return hmacKey.ToArray();
    }

    private static bool IsLocalToken(string? value, int maximumLength) =>
        value is { Length: > 0 }
        && value.Length <= maximumLength
        && value.Equals(value.Trim(), StringComparison.Ordinal)
        && value.All(character => character >= ' ' && character != '\u007f');

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string CanonicalJson<T>(T value) =>
        JsonSerializer.Serialize(value, CanonicalJsonOptions);

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record EntityFeature(
        string SourceRef,
        string SourceKey,
        WarehouseCadEntityType CadEntityType,
        string LayerToken,
        string? BlockToken,
        int AngleBucket,
        int? AspectRatioBucket,
        string? RepetitionGroup,
        IReadOnlyList<string> AttributeKeyTokens,
        WarehouseNormalizedBounds? NormalizedBounds);

    private sealed record MetadataGroupKey(
        WarehouseCadEntityType CadEntityType,
        string LayerToken,
        string? BlockToken,
        int AngleBucket,
        int? AspectRatioBucket,
        string? RepetitionGroup);
}
