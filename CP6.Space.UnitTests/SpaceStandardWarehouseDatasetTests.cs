using CP6.Space.Application;

namespace CP6.Space.UnitTests;

public sealed class SpaceStandardWarehouseDatasetTests
{
    [Fact]
    public void Generates_frozen_standard_warehouse_counts()
    {
        var dataset = SpaceStandardWarehouseDatasetGenerator.Generate();

        Assert.Equal(1, dataset.SchemaVersion);
        Assert.Equal("1.0.0", dataset.DatasetVersion);
        Assert.Equal("1.0", dataset.CompatibleSpecVersion);
        Assert.Equal(
            "space-standard-warehouse-generator-v1",
            dataset.GeneratorVersion);
        Assert.Equal(
            "cp6-space-standard-warehouse-seed-v1",
            dataset.RandomSeed);
        Assert.Equal(2, dataset.Counts.Floors);
        Assert.Equal(7, dataset.Counts.Zones);
        Assert.Equal(20, dataset.Counts.Aisles);
        Assert.Equal(500, dataset.Counts.Racks);
        Assert.Equal(10_000, dataset.Counts.Locations);
        Assert.Equal(100, dataset.Counts.Skus);
        Assert.Equal(5_000, dataset.Counts.StockRecords);
        Assert.Equal(100, dataset.Counts.PickTasks);
        Assert.Equal(200, dataset.Counts.PickTaskLines);
        Assert.Equal(6, dataset.Counts.FaultCases);
        Assert.Matches("^[0-9a-f]{64}$", dataset.ContentSha256);
    }

    [Fact]
    public void Generates_unique_codes_and_complete_hierarchy()
    {
        var dataset = SpaceStandardWarehouseDatasetGenerator.Generate();

        Assert.Equal(
            dataset.Locations.Count,
            dataset.Locations.Select(location => location.Code).Distinct().Count());
        Assert.Equal(
            dataset.Locations.Count,
            dataset.Locations.Select(location => location.LogicalId).Distinct().Count());
        Assert.Equal(
            dataset.Racks.Count,
            dataset.Racks.Select(rack => rack.Code).Distinct().Count());
        Assert.All(
            dataset.Aisles,
            aisle => Assert.Contains(
                dataset.Zones,
                zone => zone.ExpectedId == aisle.ZoneExpectedId));
        Assert.All(
            dataset.Racks,
            rack =>
            {
                Assert.Contains(
                    dataset.Aisles,
                    aisle => aisle.ExpectedId == rack.AisleExpectedId);
                Assert.Equal(
                    20,
                    dataset.Locations.Count(
                        location => location.RackCode == rack.Code));
            });
        Assert.All(
            dataset.Locations,
            location => Assert.Contains(
                dataset.Racks,
                rack => rack.Code == location.RackCode));
    }

    [Fact]
    public void Geometry_is_in_floor_bounds_and_covers_required_zone_types()
    {
        var dataset = SpaceStandardWarehouseDatasetGenerator.Generate();
        var requiredTypes = new[]
        {
            "Receiving",
            "Storage",
            "Picking",
            "Packing",
            "Shipping",
        };

        Assert.All(
            requiredTypes,
            type => Assert.Contains(
                dataset.Zones,
                zone => zone.ZoneType == type));
        Assert.All(
            dataset.Locations,
            location =>
            {
                var floor = Assert.Single(
                    dataset.Floors,
                    value => value.Code == location.FloorCode);
                Assert.InRange(
                    location.Xmm,
                    floor.OriginXmm,
                    floor.OriginXmm + floor.WidthMm);
                Assert.InRange(
                    location.Ymm,
                    floor.OriginYmm,
                    floor.OriginYmm + floor.DepthMm);
                Assert.InRange(
                    location.Zmm,
                    0m,
                    floor.HeightMm);
            });
    }

    [Fact]
    public void Inventory_and_tasks_reference_generated_locations_and_skus()
    {
        var dataset = SpaceStandardWarehouseDatasetGenerator.Generate();
        var locationIds = dataset.Locations
            .Select(location => location.LogicalId)
            .ToHashSet();
        var locationCodes = dataset.Locations
            .ToDictionary(location => location.LogicalId, location => location.Code);
        var skuCodes = dataset.Skus
            .Select(sku => sku.MaterialNumber)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(
            dataset.Inventory,
            item =>
            {
                Assert.Contains(item.LogicalId, locationIds);
                Assert.Equal(locationCodes[item.LogicalId], item.LocationCode);
                Assert.Contains(item.MaterialNumber!, skuCodes);
                Assert.True(item.PhysicalQuantity >= item.AllocatedQuantity);
                Assert.False(string.IsNullOrWhiteSpace(item.LotNumber));
                Assert.False(string.IsNullOrWhiteSpace(item.ContainerNumber));
            });
        Assert.All(
            dataset.TaskLines,
            line =>
            {
                Assert.Contains(line.LogicalId, locationIds);
                Assert.Equal(locationCodes[line.LogicalId], line.LocationCode);
                Assert.Contains(line.MaterialNumber!, skuCodes);
            });
        Assert.Equal(
            dataset.Counts.PickTasks,
            dataset.TaskLines.Select(line => line.TaskId).Distinct().Count());
        Assert.Equal(
            25,
            dataset.PickTasks.Count(task => task.RouteKind == "CrossFloor"));
        Assert.Equal(
            25,
            dataset.PickTasks.Count(task => task.RouteKind == "CrossZone"));
    }

    [Fact]
    public void Rebuild_is_byte_identity_equivalent_at_contract_boundaries()
    {
        var first = SpaceStandardWarehouseDatasetGenerator.Generate();
        var second = SpaceStandardWarehouseDatasetGenerator.Generate();

        Assert.Equal(first.ContentSha256, second.ContentSha256);
        Assert.Equal(first.Locations, second.Locations);
        Assert.Equal(first.Inventory, second.Inventory);
        Assert.Equal(first.TaskLines, second.TaskLines);
        Assert.Equal(first.FaultCases, second.FaultCases);
        Assert.Equal(
            SpaceStandardWarehouseDatasetGenerator.CreateDeterministicId(
                "location:F1-A01-R001-C01-L01-D01"),
            first.Locations[0].LogicalId);
    }

    [Fact]
    public void Fault_catalog_covers_frozen_anomaly_contract()
    {
        var dataset = SpaceStandardWarehouseDatasetGenerator.Generate();

        Assert.Equal(
            new[]
            {
                "COORDINATE_OUT_OF_BOUNDS",
                "CORRUPT_CAD",
                "DUPLICATE_LOCATION_CODE",
                "REQUIRED_COLUMN_MISSING",
                "UNKNOWN_LAYER",
                "WMS_TIMEOUT",
            },
            dataset.FaultCases
                .Select(value => value.Code)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
        Assert.All(
            dataset.FaultCases,
            fault =>
            {
                Assert.StartsWith("fault-cases/", fault.RelativePath);
                Assert.StartsWith("SPACE_", fault.ExpectedErrorCode);
            });
    }
}
