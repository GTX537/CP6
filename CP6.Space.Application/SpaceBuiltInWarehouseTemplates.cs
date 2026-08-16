using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class SpaceBuiltInWarehouseTemplates
{
    public const int SchemaVersion = SpaceWarehouseTemplateContract.SchemaVersion;
    public const int MaximumFloorCommandCount = 300;

    private static readonly Lazy<CatalogEntry> StandardWarehouse =
        new(CreateStandardWarehouse, LazyThreadSafetyMode.ExecutionAndPublication);

    public static Guid StandardWarehouseTemplateId =>
        StandardWarehouse.Value.Template.Id;

    public static Guid StandardWarehouseVersionId =>
        StandardWarehouse.Value.Template.LatestVersion.Id;

    public static IReadOnlyList<SpaceWarehouseTemplateDto> List() =>
        [StandardWarehouse.Value.Template];

    public static bool TryPreview(
        Guid templateId,
        Guid templateVersionId,
        out SpaceWarehouseTemplateInstantiationPreviewDto? preview)
    {
        var entry = StandardWarehouse.Value;
        if (entry.Template.Id != templateId ||
            entry.Template.LatestVersion.Id != templateVersionId)
        {
            preview = null;
            return false;
        }

        preview = entry.Preview;
        return true;
    }

    public static bool TryBuildFloorCommandBatch(
        Guid templateId,
        Guid templateVersionId,
        string templateFloorKey,
        Guid modelVersionId,
        Guid floorLogicalId,
        Guid commandBatchId,
        Guid clientInstanceId,
        Guid leaseId,
        long expectedFloorRevision,
        long expectedContentRevision,
        out SpaceWarehouseTemplateFloorPlanDto? templateFloor,
        out SpaceWarehouseTemplateCountsDto? counts,
        out ApplySpaceLayoutCommandBatchRequest? commandBatch)
    {
        if (!TryPreview(templateId, templateVersionId, out var preview) ||
            preview is null)
        {
            templateFloor = null;
            counts = null;
            commandBatch = null;
            return false;
        }

        return TryBuildFloorCommandBatch(
            preview,
            templateFloorKey,
            modelVersionId,
            floorLogicalId,
            commandBatchId,
            clientInstanceId,
            leaseId,
            expectedFloorRevision,
            expectedContentRevision,
            out templateFloor,
            out counts,
            out commandBatch);
    }

    public static bool TryBuildFloorCommandBatch(
        SpaceWarehouseTemplateInstantiationPreviewDto preview,
        string templateFloorKey,
        Guid modelVersionId,
        Guid floorLogicalId,
        Guid commandBatchId,
        Guid clientInstanceId,
        Guid leaseId,
        long expectedFloorRevision,
        long expectedContentRevision,
        out SpaceWarehouseTemplateFloorPlanDto? templateFloor,
        out SpaceWarehouseTemplateCountsDto? counts,
        out ApplySpaceLayoutCommandBatchRequest? commandBatch)
    {
        ArgumentNullException.ThrowIfNull(preview);

        templateFloor = preview.Floors.SingleOrDefault(candidate =>
            string.Equals(
                candidate.Key,
                templateFloorKey?.Trim(),
                StringComparison.Ordinal));
        if (templateFloor is null)
        {
            counts = null;
            commandBatch = null;
            return false;
        }
        var selectedFloor = templateFloor;

        var zones = preview.Zones
            .Where(candidate => candidate.FloorKey == selectedFloor.Key)
            .ToArray();
        var zoneKeys = zones.Select(candidate => candidate.Key).ToHashSet();
        var aisles = preview.Aisles
            .Where(candidate => candidate.FloorKey == selectedFloor.Key)
            .ToArray();
        if (aisles.Any(candidate => !zoneKeys.Contains(candidate.ZoneKey)))
        {
            throw new InvalidOperationException(
                "The built-in warehouse template contains an invalid aisle parent chain.");
        }
        var aisleKeys = aisles.Select(candidate => candidate.Key).ToHashSet();
        var racks = preview.Racks
            .Where(candidate => candidate.FloorKey == selectedFloor.Key)
            .ToArray();
        if (racks.Any(candidate =>
                !zoneKeys.Contains(candidate.ZoneKey) ||
                !aisleKeys.Contains(candidate.AisleKey)))
        {
            throw new InvalidOperationException(
                "The built-in warehouse template contains an invalid floor parent chain.");
        }

        var zoneIds = zones.ToDictionary(
            candidate => candidate.Key,
            candidate => TemplateObjectId(
                preview.TemplateVersionId,
                modelVersionId,
                floorLogicalId,
                candidate.Key));
        var aisleIds = aisles.ToDictionary(
            candidate => candidate.Key,
            candidate => TemplateObjectId(
                preview.TemplateVersionId,
                modelVersionId,
                floorLogicalId,
                candidate.Key));
        var commands = new List<SpaceLayoutCommandDto>(
            zones.Length + aisles.Length + racks.Length);
        commands.AddRange(zones.Select(zone =>
            new SpaceLayoutCommandDto(
                CommandId(commandBatchId, SpaceLayoutCommandContract.CreateZone, zone.Key),
                SpaceLayoutCommandContract.CreateZone,
                zoneIds[zone.Key],
                CreateZone: new SpaceCreateLayoutZoneDto(
                    zone.ZoneCode,
                    zone.ZoneCode,
                    ZoneType(zone.ZoneType),
                    RectanglePolygon(
                        zone.MinX,
                        zone.MinY,
                        zone.MaxX,
                        zone.MaxY),
                    ZoneColor(zone.ZoneType),
                    null))));
        commands.AddRange(aisles.Select(aisle =>
        {
            const int halfWidth = 1_500;
            var minX = checked(Math.Min(aisle.StartX, aisle.EndX) - halfWidth);
            var minY = checked(Math.Min(aisle.StartY, aisle.EndY) - halfWidth);
            var maxX = checked(Math.Max(aisle.StartX, aisle.EndX) + halfWidth);
            var maxY = checked(Math.Max(aisle.StartY, aisle.EndY) + halfWidth);
            return new SpaceLayoutCommandDto(
                CommandId(commandBatchId, SpaceLayoutCommandContract.CreateAisle, aisle.Key),
                SpaceLayoutCommandContract.CreateAisle,
                aisleIds[aisle.Key],
                CreateAisle: new SpaceCreateLayoutAisleDto(
                    zoneIds[aisle.ZoneKey],
                    aisle.AisleCode,
                    aisle.AisleCode,
                    Direction(aisle),
                    RectanglePolygon(minX, minY, maxX, maxY),
                    Centerline(aisle)));
        }));
        commands.AddRange(racks.Select(rack =>
        {
            var rackId = TemplateObjectId(
                preview.TemplateVersionId,
                modelVersionId,
                floorLogicalId,
                rack.Key);
            return new SpaceLayoutCommandDto(
                CommandId(commandBatchId, SpaceLayoutCommandContract.CreateRack, rack.Key),
                SpaceLayoutCommandContract.CreateRack,
                rackId,
                CreateRack: new SpaceCreateLayoutRackDto(
                    zoneIds[rack.ZoneKey],
                    aisleIds[rack.AisleKey],
                    rack.RackCode,
                    rack.RackCode,
                    "Selective",
                    preview.TemplateVersionId,
                    rack.X,
                    rack.Y,
                    rack.Z,
                    rack.RotationZ,
                    rack.Width,
                    rack.Depth,
                    rack.Height,
                    RackLevels(rack)));
        }));
        if (commands.Count > MaximumFloorCommandCount)
        {
            throw new InvalidOperationException(
                "The built-in warehouse template exceeds the floor command limit.");
        }

        var locationCount = racks.Sum(candidate => checked(
            candidate.Columns * candidate.Levels * candidate.Depths));
        counts = new SpaceWarehouseTemplateCountsDto(
            Floors: 1,
            zones.Length,
            aisles.Length,
            racks.Length,
            locationCount);
        commandBatch = new ApplySpaceLayoutCommandBatchRequest(
            SpaceLayoutCommandContract.SchemaVersion,
            commandBatchId,
            clientInstanceId,
            leaseId,
            expectedFloorRevision,
            expectedContentRevision,
            commands);
        return true;
    }

    private static CatalogEntry CreateStandardWarehouse()
    {
        var dataset = SpaceStandardWarehouseDatasetGenerator.Generate();
        var templateId = SpaceStandardWarehouseDatasetGenerator.CreateDeterministicId(
            "warehouse-template:system:standard-warehouse");
        var templateVersionId =
            SpaceStandardWarehouseDatasetGenerator.CreateDeterministicId(
                "warehouse-template-version:system:standard-warehouse:v1");

        var floors = dataset.Floors.Select(value =>
            new SpaceWarehouseTemplateFloorPlanDto(
                value.ExpectedId,
                value.Code,
                value.Name,
                value.Level,
                CheckedInt(value.OriginZmm, nameof(value.OriginZmm)),
                CheckedInt(value.WidthMm, nameof(value.WidthMm)),
                CheckedInt(value.DepthMm, nameof(value.DepthMm)),
                CheckedInt(value.HeightMm, nameof(value.HeightMm))))
            .ToArray();
        var zones = dataset.Zones.Select(value =>
            new SpaceWarehouseTemplateZonePlanDto(
                value.ExpectedId,
                value.FloorExpectedId,
                value.Code,
                value.ZoneType,
                CheckedInt(value.MinXmm, nameof(value.MinXmm)),
                CheckedInt(value.MinYmm, nameof(value.MinYmm)),
                CheckedInt(value.MaxXmm, nameof(value.MaxXmm)),
                CheckedInt(value.MaxYmm, nameof(value.MaxYmm))))
            .ToArray();
        var aisles = dataset.Aisles.Select(value =>
            new SpaceWarehouseTemplateAislePlanDto(
                value.ExpectedId,
                value.FloorExpectedId,
                value.ZoneExpectedId,
                value.Code,
                CheckedInt(value.StartXmm, nameof(value.StartXmm)),
                CheckedInt(value.StartYmm, nameof(value.StartYmm)),
                CheckedInt(value.EndXmm, nameof(value.EndXmm)),
                CheckedInt(value.EndYmm, nameof(value.EndYmm))))
            .ToArray();
        var racks = dataset.Racks.Select(value =>
            new SpaceWarehouseTemplateRackPlanDto(
                value.ExpectedId,
                value.FloorExpectedId,
                value.ZoneExpectedId,
                value.AisleExpectedId,
                value.Code,
                CheckedInt(value.Xmm, nameof(value.Xmm)),
                CheckedInt(value.Ymm, nameof(value.Ymm)),
                CheckedInt(value.Zmm, nameof(value.Zmm)),
                value.RotationDegrees,
                CheckedInt(value.WidthMm, nameof(value.WidthMm)),
                CheckedInt(value.DepthMm, nameof(value.DepthMm)),
                CheckedInt(value.HeightMm, nameof(value.HeightMm)),
                value.Columns,
                value.Levels,
                value.Depths))
            .ToArray();
        var counts = new SpaceWarehouseTemplateCountsDto(
            floors.Length,
            zones.Length,
            aisles.Length,
            racks.Length,
            dataset.Locations.Count);
        var canonical = JsonSerializer.Serialize(
            new
            {
                schemaVersion = SchemaVersion,
                floors,
                zones,
                aisles,
                racks,
                locationCount = dataset.Locations.Count,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var contentHash = Sha256(canonical);
        var proposalHash = Sha256(
            $"space-template-preview-v1\n{templateId:D}\n" +
            $"{templateVersionId:D}\n{contentHash}");
        var version = new SpaceWarehouseTemplateVersionDto(
            templateVersionId,
            VersionNo: 1,
            SchemaVersion,
            contentHash,
            counts,
            Status: "Ready");
        var template = new SpaceWarehouseTemplateDto(
            templateId,
            Scope: "System",
            SpaceStandardWarehouseDatasetContract.WarehouseCode,
            Name: "CP6 标准货架仓",
            Description:
                "平台内置的双层标准货架仓模板；500 个货架、10,000 个库位。",
            Status: "Active",
            version);
        var preview = new SpaceWarehouseTemplateInstantiationPreviewDto(
            SchemaVersion,
            templateId,
            templateVersionId,
            contentHash,
            proposalHash,
            counts,
            floors,
            zones,
            aisles,
            racks,
            WritesDraft: false);
        return new CatalogEntry(template, preview);
    }

    private static int CheckedInt(decimal value, string parameterName)
    {
        if (value != decimal.Truncate(value) ||
            value is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"{parameterName} is not an integer millimeter value.");
        }
        return decimal.ToInt32(value);
    }

    private static IReadOnlyList<SpaceCreateLayoutRackLevelDto> RackLevels(
        SpaceWarehouseTemplateRackPlanDto rack)
    {
        if (rack.Columns <= 0 || rack.Levels <= 0 || rack.Depths <= 0)
            throw new InvalidOperationException("Rack counts must be positive.");
        if (rack.Height % rack.Levels != 0 ||
            rack.Width % rack.Columns != 0 ||
            rack.Depth % rack.Depths != 0)
        {
            throw new InvalidOperationException(
                "Rack dimensions must divide exactly into the requested cells.");
        }
        var levelHeight = rack.Height / rack.Levels;
        var cellWidth = rack.Width / rack.Columns;
        var cellDepth = rack.Depth / rack.Depths;
        const int beamHeight = 100;
        if (levelHeight <= beamHeight || cellWidth <= 0 || cellDepth <= 0)
            throw new InvalidOperationException("Rack dimensions cannot form a valid level plan.");
        return Enumerable.Range(1, rack.Levels)
            .Select(levelNo => new SpaceCreateLayoutRackLevelDto(
                levelNo,
                checked((levelNo - 1) * levelHeight),
                levelHeight - beamHeight,
                rack.Columns,
                rack.Depths,
                cellWidth,
                cellDepth,
                beamHeight,
                MaxLoad: null,
                LocationCodePrefix: null))
            .ToArray();
    }

    private static Guid TemplateObjectId(
        Guid templateVersionId,
        Guid modelVersionId,
        Guid floorLogicalId,
        string key) =>
        DeterministicId(
            $"template-object\n{templateVersionId:D}\n{modelVersionId:D}\n" +
            $"{floorLogicalId:D}\n{key}");

    private static Guid CommandId(Guid commandBatchId, string type, string key) =>
        DeterministicId($"template-command\n{commandBatchId:D}\n{type}\n{key}");

    private static Guid DeterministicId(string material)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string RectanglePolygon(
        int minX,
        int minY,
        int maxX,
        int maxY) =>
        JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                points = new[]
                {
                    new[] { minX, minY },
                    new[] { maxX, minY },
                    new[] { maxX, maxY },
                    new[] { minX, maxY },
                },
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string Centerline(
        SpaceWarehouseTemplateAislePlanDto aisle) =>
        JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                points = new[]
                {
                    new[] { aisle.StartX, aisle.StartY },
                    new[] { aisle.EndX, aisle.EndY },
                },
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static short Direction(SpaceWarehouseTemplateAislePlanDto aisle) =>
        (short)(Math.Abs(aisle.EndX - aisle.StartX) >=
                Math.Abs(aisle.EndY - aisle.StartY)
            ? 1
            : 2);

    private static short ZoneType(string value) => value switch
    {
        "Receiving" => 1,
        "Storage" => 2,
        "Shipping" => 3,
        "Picking" => 4,
        "Packing" => 5,
        _ => 0,
    };

    private static string ZoneColor(string value) => value switch
    {
        "Receiving" => "#2f9e44",
        "Storage" => "#0ca6b2",
        "Shipping" => "#f08c00",
        "Picking" => "#7048e8",
        "Packing" => "#d6336c",
        _ => "#64748b",
    };

    private static string Sha256(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record CatalogEntry(
        SpaceWarehouseTemplateDto Template,
        SpaceWarehouseTemplateInstantiationPreviewDto Preview);
}
