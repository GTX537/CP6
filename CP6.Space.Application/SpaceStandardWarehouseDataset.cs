using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CP6.Space.Application;

public static class SpaceStandardWarehouseDatasetContract
{
    public const int SchemaVersion = 1;
    public const string DatasetVersion = "1.0.0";
    public const string CompatibleSpecVersion = "1.0";
    public const string GeneratorVersion =
        "space-standard-warehouse-generator-v1";
    public const string RandomSeed =
        "cp6-space-standard-warehouse-seed-v1";
    public const string WarehouseCode = "SPACE-STANDARD-01";
    public static readonly DateTimeOffset GeneratedAtUtc =
        new(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);
}

public sealed record SpaceStandardWarehouseCounts(
    int Floors,
    int Zones,
    int Aisles,
    int Racks,
    int Locations,
    int Skus,
    int StockRecords,
    int PickTasks,
    int PickTaskLines,
    int FaultCases);

public sealed record SpaceStandardWarehouseFloor(
    string ExpectedId,
    string Code,
    int Level,
    string Name,
    decimal OriginXmm,
    decimal OriginYmm,
    decimal OriginZmm,
    decimal WidthMm,
    decimal DepthMm,
    decimal HeightMm);

public sealed record SpaceStandardWarehouseZone(
    string ExpectedId,
    string FloorExpectedId,
    string Code,
    string ZoneType,
    decimal MinXmm,
    decimal MinYmm,
    decimal MaxXmm,
    decimal MaxYmm);

public sealed record SpaceStandardWarehouseAisle(
    string ExpectedId,
    string FloorExpectedId,
    string ZoneExpectedId,
    string Code,
    int Number,
    decimal StartXmm,
    decimal StartYmm,
    decimal EndXmm,
    decimal EndYmm);

public sealed record SpaceStandardWarehouseRack(
    string ExpectedId,
    string FloorExpectedId,
    string ZoneExpectedId,
    string AisleExpectedId,
    string Code,
    int Number,
    decimal Xmm,
    decimal Ymm,
    decimal Zmm,
    decimal RotationDegrees,
    decimal WidthMm,
    decimal DepthMm,
    decimal HeightMm,
    int Columns,
    int Levels,
    int Depths);

public sealed record SpaceStandardWarehouseLocation(
    string ExpectedId,
    Guid LogicalId,
    string Code,
    string FloorCode,
    int FloorLevel,
    string ZoneCode,
    string ZoneType,
    string AisleCode,
    string RackCode,
    int Column,
    int Level,
    int Depth,
    decimal Xmm,
    decimal Ymm,
    decimal Zmm,
    decimal WidthMm,
    decimal DepthMm,
    decimal HeightMm,
    bool IsActive);

public sealed record SpaceStandardWarehouseSku(
    string MaterialNumber,
    string Description,
    string OwnerCode,
    decimal UnitWeightKg);

public sealed record SpaceStandardWarehousePickTask(
    string TaskId,
    string RouteKind,
    IReadOnlyList<SpaceWmsTaskItem> Lines);

public sealed record SpaceStandardWarehouseFaultCase(
    string Code,
    string Kind,
    string RelativePath,
    string ExpectedErrorCode,
    string Description);

public sealed record SpaceStandardWarehouseLoadResult(
    string DatasetVersion,
    string ContentSha256,
    int BatchCount,
    int LocationCount,
    int InventoryCount,
    int PickTaskCount,
    int PickTaskLineCount);

public interface ISpaceStandardWarehouseDatasetLoader
{
    Task<SpaceStandardWarehouseLoadResult> LoadAsync(
        SpaceWmsContext context,
        SpaceStandardWarehouseDataset dataset,
        CancellationToken ct = default);
}

public sealed class SpaceStandardWarehouseDataset
{
    internal SpaceStandardWarehouseDataset(
        IReadOnlyList<SpaceStandardWarehouseFloor> floors,
        IReadOnlyList<SpaceStandardWarehouseZone> zones,
        IReadOnlyList<SpaceStandardWarehouseAisle> aisles,
        IReadOnlyList<SpaceStandardWarehouseRack> racks,
        IReadOnlyList<SpaceStandardWarehouseLocation> locations,
        IReadOnlyList<SpaceStandardWarehouseSku> skus,
        IReadOnlyList<SpaceWmsInventoryItem> inventory,
        IReadOnlyList<SpaceStandardWarehousePickTask> pickTasks,
        IReadOnlyList<SpaceStandardWarehouseFaultCase> faultCases,
        string contentSha256)
    {
        Floors = floors;
        Zones = zones;
        Aisles = aisles;
        Racks = racks;
        Locations = locations;
        Skus = skus;
        Inventory = inventory;
        PickTasks = pickTasks;
        FaultCases = faultCases;
        ContentSha256 = contentSha256;
        Counts = new SpaceStandardWarehouseCounts(
            floors.Count,
            zones.Count,
            aisles.Count,
            racks.Count,
            locations.Count,
            skus.Count,
            inventory.Count,
            pickTasks.Count,
            pickTasks.Sum(task => task.Lines.Count),
            faultCases.Count);
    }

    public int SchemaVersion =>
        SpaceStandardWarehouseDatasetContract.SchemaVersion;
    public string DatasetVersion =>
        SpaceStandardWarehouseDatasetContract.DatasetVersion;
    public string CompatibleSpecVersion =>
        SpaceStandardWarehouseDatasetContract.CompatibleSpecVersion;
    public string GeneratorVersion =>
        SpaceStandardWarehouseDatasetContract.GeneratorVersion;
    public string RandomSeed =>
        SpaceStandardWarehouseDatasetContract.RandomSeed;
    public string WarehouseCode =>
        SpaceStandardWarehouseDatasetContract.WarehouseCode;
    public DateTimeOffset GeneratedAtUtc =>
        SpaceStandardWarehouseDatasetContract.GeneratedAtUtc;
    public SpaceStandardWarehouseCounts Counts { get; }
    public IReadOnlyList<SpaceStandardWarehouseFloor> Floors { get; }
    public IReadOnlyList<SpaceStandardWarehouseZone> Zones { get; }
    public IReadOnlyList<SpaceStandardWarehouseAisle> Aisles { get; }
    public IReadOnlyList<SpaceStandardWarehouseRack> Racks { get; }
    public IReadOnlyList<SpaceStandardWarehouseLocation> Locations { get; }
    public IReadOnlyList<SpaceStandardWarehouseSku> Skus { get; }
    public IReadOnlyList<SpaceWmsInventoryItem> Inventory { get; }
    public IReadOnlyList<SpaceStandardWarehousePickTask> PickTasks { get; }
    public IReadOnlyList<SpaceStandardWarehouseFaultCase> FaultCases { get; }
    public string ContentSha256 { get; }

    public IReadOnlyList<SpaceWmsTaskItem> TaskLines =>
        PickTasks.SelectMany(task => task.Lines).ToArray();
}

public static class SpaceStandardWarehouseDatasetGenerator
{
    private const int Floors = 2;
    private const int AislesPerFloor = 10;
    private const int RacksPerAisle = 25;
    private const int ColumnsPerRack = 4;
    private const int LevelsPerRack = 5;
    private const int DepthsPerRack = 1;

    public static SpaceStandardWarehouseDataset Generate()
    {
        var floors = CreateFloors();
        var zones = CreateZones(floors);
        var aisles = CreateAisles(floors, zones);
        var racks = CreateRacks(aisles);
        var locations = CreateLocations(racks, zones);
        var skus = CreateSkus();
        var inventory = CreateInventory(locations, skus);
        var tasks = CreateTasks(locations, zones, skus);
        var faultCases = CreateFaultCases();
        var contentSha256 = ComputeContentHash(
            floors,
            zones,
            aisles,
            racks,
            locations,
            skus,
            inventory,
            tasks,
            faultCases);
        return new SpaceStandardWarehouseDataset(
            floors,
            zones,
            aisles,
            racks,
            locations,
            skus,
            inventory,
            tasks,
            faultCases,
            contentSha256);
    }

    public static Guid CreateDeterministicId(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A deterministic key is required.", nameof(key));
        var material = string.Concat(
            SpaceStandardWarehouseDatasetContract.RandomSeed,
            "\n",
            key.Trim());
        var hex = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        return Guid.ParseExact(hex[..32], "N");
    }

    private static IReadOnlyList<SpaceStandardWarehouseFloor> CreateFloors() =>
    [
        new(
            "floor:F1",
            "F1",
            1,
            "Ground Floor",
            0m,
            0m,
            0m,
            140_000m,
            120_000m,
            6_000m),
        new(
            "floor:F2",
            "F2",
            2,
            "Upper Floor",
            0m,
            0m,
            6_000m,
            140_000m,
            120_000m,
            6_000m),
    ];

    private static IReadOnlyList<SpaceStandardWarehouseZone> CreateZones(
        IReadOnlyList<SpaceStandardWarehouseFloor> floors)
    {
        var floor1 = floors[0];
        var floor2 = floors[1];
        return
        [
            Zone(floor1, "F1-RECV", "Receiving", 1, 2),
            Zone(floor1, "F1-STOR", "Storage", 3, 6),
            Zone(floor1, "F1-SHIP", "Shipping", 7, 10),
            Zone(floor2, "F2-STOR", "Storage", 1, 4),
            Zone(floor2, "F2-PICK", "Picking", 5, 8),
            Zone(floor2, "F2-PACK", "Packing", 9, 9),
            Zone(floor2, "F2-SHIP", "Shipping", 10, 10),
        ];
    }

    private static SpaceStandardWarehouseZone Zone(
        SpaceStandardWarehouseFloor floor,
        string code,
        string type,
        int firstLocalAisle,
        int lastLocalAisle) =>
        new(
            $"zone:{code}",
            floor.ExpectedId,
            code,
            type,
            AisleX(firstLocalAisle) - 5_000m,
            5_000m,
            AisleX(lastLocalAisle) + 5_000m,
            115_000m);

    private static IReadOnlyList<SpaceStandardWarehouseAisle> CreateAisles(
        IReadOnlyList<SpaceStandardWarehouseFloor> floors,
        IReadOnlyList<SpaceStandardWarehouseZone> zones)
    {
        var aisles = new List<SpaceStandardWarehouseAisle>(
            Floors * AislesPerFloor);
        foreach (var floor in floors)
        {
            for (var local = 1; local <= AislesPerFloor; local++)
            {
                var number = ((floor.Level - 1) * AislesPerFloor) + local;
                var code = $"{floor.Code}-A{local:00}";
                var zone = zones.Single(value =>
                    value.FloorExpectedId == floor.ExpectedId &&
                    AisleX(local) >= value.MinXmm &&
                    AisleX(local) <= value.MaxXmm);
                aisles.Add(new SpaceStandardWarehouseAisle(
                    $"aisle:{code}",
                    floor.ExpectedId,
                    zone.ExpectedId,
                    code,
                    number,
                    AisleX(local),
                    10_000m,
                    AisleX(local),
                    110_000m));
            }
        }
        return aisles;
    }

    private static IReadOnlyList<SpaceStandardWarehouseRack> CreateRacks(
        IReadOnlyList<SpaceStandardWarehouseAisle> aisles)
    {
        var racks = new List<SpaceStandardWarehouseRack>(
            Floors * AislesPerFloor * RacksPerAisle);
        foreach (var aisle in aisles)
        {
            for (var rackNo = 1; rackNo <= RacksPerAisle; rackNo++)
            {
                var code = $"{aisle.Code}-R{rackNo:000}";
                var leftSide = rackNo % 2 != 0;
                racks.Add(new SpaceStandardWarehouseRack(
                    $"rack:{code}",
                    aisle.FloorExpectedId,
                    aisle.ZoneExpectedId,
                    aisle.ExpectedId,
                    code,
                    rackNo,
                    aisle.StartXmm + (leftSide ? -2_500m : 2_500m),
                    12_000m + ((rackNo - 1) * 3_800m),
                    0m,
                    leftSide ? 0m : 180m,
                    3_600m,
                    1_100m,
                    5_000m,
                    ColumnsPerRack,
                    LevelsPerRack,
                    DepthsPerRack));
            }
        }
        return racks;
    }

    private static IReadOnlyList<SpaceStandardWarehouseLocation> CreateLocations(
        IReadOnlyList<SpaceStandardWarehouseRack> racks,
        IReadOnlyList<SpaceStandardWarehouseZone> zones)
    {
        var locations = new List<SpaceStandardWarehouseLocation>(
            racks.Count * ColumnsPerRack * LevelsPerRack * DepthsPerRack);
        foreach (var rack in racks)
        {
            var floorCode = rack.Code[..2];
            var floorLevel = int.Parse(
                floorCode.AsSpan(1),
                CultureInfo.InvariantCulture);
            var aisleCode = rack.Code[..6];
            var zone = zones.Single(value =>
                value.ExpectedId == rack.ZoneExpectedId);
            for (var level = 1; level <= LevelsPerRack; level++)
            {
                for (var column = 1; column <= ColumnsPerRack; column++)
                {
                    var code =
                        $"{rack.Code}-C{column:00}-L{level:00}-D01";
                    locations.Add(new SpaceStandardWarehouseLocation(
                        $"location:{code}",
                        CreateDeterministicId($"location:{code}"),
                        code,
                        floorCode,
                        floorLevel,
                        zone.Code,
                        zone.ZoneType,
                        aisleCode,
                        rack.Code,
                        column,
                        level,
                        1,
                        rack.Xmm + ((column - 2.5m) * 800m),
                        rack.Ymm,
                        rack.Zmm + ((level - 0.5m) * 1_000m),
                        800m,
                        1_100m,
                        1_000m,
                        true));
                }
            }
        }
        return locations;
    }

    private static IReadOnlyList<SpaceStandardWarehouseSku> CreateSkus() =>
        Enumerable.Range(1, 100)
            .Select(number => new SpaceStandardWarehouseSku(
                $"SKU-{number:0000}",
                $"Synthetic standard warehouse item {number:0000}",
                $"OWNER-{((number - 1) % 5) + 1:00}",
                0.25m + ((number % 40) * 0.125m)))
            .ToArray();

    private static IReadOnlyList<SpaceWmsInventoryItem> CreateInventory(
        IReadOnlyList<SpaceStandardWarehouseLocation> locations,
        IReadOnlyList<SpaceStandardWarehouseSku> skus) =>
        Enumerable.Range(0, 5_000)
            .Select(index =>
            {
                var location = locations[index * 2];
                var sku = skus[index % skus.Count];
                var physical = 10m + (index % 91);
                var allocated = index % 5;
                return new SpaceWmsInventoryItem(
                    location.LogicalId,
                    location.Code,
                    physical,
                    allocated,
                    sku.MaterialNumber,
                    $"LOT-{202601 + (index % 12):000000}-{index % 37:00}",
                    $"CONT-{index + 1:000000}",
                    sku.OwnerCode);
            })
            .ToArray();

    private static IReadOnlyList<SpaceStandardWarehousePickTask> CreateTasks(
        IReadOnlyList<SpaceStandardWarehouseLocation> locations,
        IReadOnlyList<SpaceStandardWarehouseZone> zones,
        IReadOnlyList<SpaceStandardWarehouseSku> skus)
    {
        var byFloor = locations
            .GroupBy(location => location.FloorCode)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var byZone = locations
            .GroupBy(location => location.ZoneCode)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var floor1ZoneCodes = zones
            .Where(zone => zone.FloorExpectedId == "floor:F1")
            .Select(zone => zone.Code)
            .ToArray();
        var tasks = new List<SpaceStandardWarehousePickTask>(100);
        for (var index = 0; index < 100; index++)
        {
            SpaceStandardWarehouseLocation source;
            SpaceStandardWarehouseLocation destination;
            string routeKind;
            if (index < 25)
            {
                source = byFloor["F1"][(index * 127) % byFloor["F1"].Length];
                destination =
                    byFloor["F2"][(index * 149 + 17) % byFloor["F2"].Length];
                routeKind = "CrossFloor";
            }
            else if (index < 50)
            {
                var firstZone = byZone[floor1ZoneCodes[index % floor1ZoneCodes.Length]];
                var secondZone = byZone[
                    floor1ZoneCodes[
                        (index + 1) % floor1ZoneCodes.Length]];
                source = firstZone[(index * 29) % firstZone.Length];
                destination = secondZone[(index * 31) % secondZone.Length];
                routeKind = "CrossZone";
            }
            else
            {
                var zone = byZone["F2-PICK"];
                source = zone[(index * 37) % zone.Length];
                destination = zone[(index * 41 + 3) % zone.Length];
                routeKind = "WithinZone";
            }
            var taskId = $"PICK-{index + 1:0000}";
            var sku = skus[index % skus.Count];
            var quantity = 1m + (index % 12);
            tasks.Add(new SpaceStandardWarehousePickTask(
                taskId,
                routeKind,
                [
                    new SpaceWmsTaskItem(
                        taskId,
                        $"Pick{routeKind}",
                        "Released",
                        1,
                        source.LogicalId,
                        source.Code,
                        quantity,
                        sku.MaterialNumber),
                    new SpaceWmsTaskItem(
                        taskId,
                        $"Pick{routeKind}",
                        "Released",
                        2,
                        destination.LogicalId,
                        destination.Code,
                        quantity,
                        sku.MaterialNumber),
                ]));
        }
        return tasks;
    }

    private static IReadOnlyList<SpaceStandardWarehouseFaultCase>
        CreateFaultCases() =>
    [
        new(
            "UNKNOWN_LAYER",
            "Cad",
            "fault-cases/unknown-layer.dxf",
            "SPACE_CAD_LAYER_UNKNOWN",
            "Synthetic entity on an unmapped CAD layer."),
        new(
            "DUPLICATE_LOCATION_CODE",
            "LocationMaster",
            "fault-cases/duplicate-location-code.csv",
            "SPACE_LOCATION_CODE_DUPLICATE",
            "Two logical locations share one business code."),
        new(
            "COORDINATE_OUT_OF_BOUNDS",
            "Geometry",
            "fault-cases/coordinate-out-of-bounds.json",
            "SPACE_GEOMETRY_OUT_OF_BOUNDS",
            "A location lies outside its floor bounds."),
        new(
            "REQUIRED_COLUMN_MISSING",
            "LocationMaster",
            "fault-cases/missing-location-code.csv",
            "SPACE_REQUIRED_COLUMN_MISSING",
            "The location-master fixture intentionally omits LocationCode."),
        new(
            "CORRUPT_CAD",
            "Cad",
            "fault-cases/corrupt-input.dxf",
            "SPACE_CAD_INVALID",
            "Truncated DXF fixture for deterministic parser failure."),
        new(
            "WMS_TIMEOUT",
            "Wms",
            "fault-cases/wms-timeout.json",
            "SPACE_WMS_RETRYABLE",
            "Simulator timeout profile with fixed delay."),
    ];

    private static decimal AisleX(int localAisle) =>
        10_000m + ((localAisle - 1) * 12_000m);

    private static string ComputeContentHash(
        IReadOnlyList<SpaceStandardWarehouseFloor> floors,
        IReadOnlyList<SpaceStandardWarehouseZone> zones,
        IReadOnlyList<SpaceStandardWarehouseAisle> aisles,
        IReadOnlyList<SpaceStandardWarehouseRack> racks,
        IReadOnlyList<SpaceStandardWarehouseLocation> locations,
        IReadOnlyList<SpaceStandardWarehouseSku> skus,
        IReadOnlyList<SpaceWmsInventoryItem> inventory,
        IReadOnlyList<SpaceStandardWarehousePickTask> tasks,
        IReadOnlyList<SpaceStandardWarehouseFaultCase> faultCases)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            DatasetVersion =
                SpaceStandardWarehouseDatasetContract.DatasetVersion,
            GeneratorVersion =
                SpaceStandardWarehouseDatasetContract.GeneratorVersion,
            RandomSeed =
                SpaceStandardWarehouseDatasetContract.RandomSeed,
            Floors = floors,
            Zones = zones,
            Aisles = aisles,
            Racks = racks,
            Locations = locations,
            Skus = skus,
            Inventory = inventory,
            Tasks = tasks,
            FaultCases = faultCases,
        });
        return Convert.ToHexString(
                SHA256.HashData(canonical))
            .ToLowerInvariant();
    }
}
