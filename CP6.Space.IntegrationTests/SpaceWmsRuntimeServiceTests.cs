using System.Diagnostics;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceWmsRuntimeServiceTests
{
    private static readonly DateTime Now =
        new(2026, 7, 31, 16, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('a', 64);

    [Fact]
    public async Task Simulator_inventory_and_tasks_map_adopted_identity_and_spatial_context()
    {
        var execution = Execution();
        var clock = new TestClock();
        await using var context = NewContext(execution, clock);
        var seeded = await SeedPublishedAsync(context, "NATIVE-01", "ADOPTED-01");
        var simulator = new StandardSpaceWmsSimulator();
        var adoptedWmsId = Guid.NewGuid();
        var adoption = SpaceWmsAdoption.Discover(
            execution.TenantId,
            seeded.SiteId,
            simulator.RuntimeAdapterId,
            simulator.RuntimeDataSourceId,
            simulator.RuntimeDataSourceKind.ToString(),
            adoptedWmsId,
            "external-adopted-01",
            "ADOPTED-01",
            true,
            "1",
            Hash,
            Now);
        adoption.Bind(seeded.PublishedVersionId, seeded.LocationIds[1], Now);
        context.WmsAdoptions.Add(adoption);
        await context.SaveChangesAsync();
        var wms = WmsContext(execution, seeded.SiteId);
        simulator.SeedInventory(wms,
        [
            new(seeded.LocationIds[0], "NATIVE-01", 10, 2, "SKU-A", "LOT-A", null),
            new(adoptedWmsId, "ADOPTED-01", 7, 3, "SKU-B", "LOT-B", "PALLET-B"),
        ]);
        simulator.SeedTasks(wms,
        [
            new("PICK-001", "Pick", "Released", 1, adoptedWmsId,
                "ADOPTED-01", 3, "SKU-B"),
        ]);
        var service = CreateService(context, execution, clock, seeded.SiteId, simulator);

        var inventory = await service.QueryInventoryAsync(seeded.SiteId);
        var tasks = await service.QueryTasksAsync(seeded.SiteId);
        var dispatchTasks = await service.QueryDispatchTasksAsync(seeded.SiteId);

        Assert.Equal("Simulated", inventory.Source.Kind);
        Assert.Equal(simulator.RuntimeAdapterId, inventory.Source.AdapterId);
        Assert.Equal(simulator.RuntimeDataSourceId, inventory.Source.DataSourceId);
        Assert.Equal(new DateTimeOffset(Now), inventory.Source.ReceivedAtUtc);
        Assert.True(inventory.Source.IsSimulated);
        Assert.Equal(2, inventory.Items.Count);
        var adopted = Assert.Single(inventory.Items,
            value => value.WmsLogicalId == adoptedWmsId);
        Assert.Equal(seeded.LocationIds[1], adopted.LocationLogicalId);
        Assert.True(adopted.CodeMatches);
        var task = Assert.Single(tasks.Items);
        Assert.Equal(seeded.LocationIds[1], task.LocationLogicalId);
        Assert.Equal(adoptedWmsId, task.WmsLogicalId);
        Assert.NotNull(task.AnchorXMillimeters);
        Assert.NotNull(task.AnchorYMillimeters);
        Assert.NotNull(task.AnchorZMillimeters);
        var dispatchTask = Assert.Single(dispatchTasks.Items);
        Assert.Equal("PICK-001", dispatchTask.TaskId);
        Assert.Equal("Pending", dispatchTask.Status);
        Assert.Equal("Source", dispatchTask.TargetLocationRole);
        Assert.Equal(seeded.LocationIds[1], dispatchTask.LocationLogicalId);
        Assert.Equal(adoptedWmsId, dispatchTask.WmsLogicalId);
        Assert.True(dispatchTask.TargetLocationResolved);
        Assert.True(dispatchTask.CodeMatches);
        Assert.False(string.IsNullOrWhiteSpace(dispatchTask.RowVersion));
    }

    [Fact]
    public async Task Warehouse_overview_exposes_exact_occupancy_area_workload_and_abc_rules()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001", "L-002");
        fixture.Source.InventoryItems =
        [
            new(fixture.LocationIds[0], "L-001", 10, 12,
                "SKU-A", "LOT-A", "CONT-A", "OWNER-A"),
            new(fixture.LocationIds[1], "L-002", 5, 1,
                "SKU-B", "LOT-B", null, "OWNER-B"),
            new(fixture.LocationIds[1], "L-002", 2, 0,
                "SKU-U", null, null, "OWNER-B"),
        ];
        fixture.Source.TaskItems =
        [
            new("TASK-1", "Pick", "Released", 1,
                fixture.LocationIds[0], "L-001", 2, "SKU-A"),
            new("TASK-1", "Pick", "Released", 2,
                fixture.LocationIds[1], "L-002", 1, "SKU-B"),
        ];
        fixture.Source.AbcItems =
        [
            new("SKU-A", 8, 80),
            new("SKU-B", 3, 15),
            new("SKU-C", 1, 5),
        ];

        var response = await fixture.Service.GetWarehouseOverviewAsync(
            fixture.SiteId,
            abcWindowDays: 90);

        Assert.True(response.IsRuntimeComplete);
        Assert.Equal(new DateTimeOffset(Now), response.CapturedAtUtc);
        Assert.Equal(1, response.Model.FloorCount);
        Assert.Equal(100m, response.Model.TotalFloorAreaSquareMeters);
        Assert.Equal(2.2m, response.Model.RackFootprintSquareMeters);
        Assert.Equal(2.2m, response.Model.RackFootprintRatePercent);
        Assert.Equal(2, response.Model.ActiveLocationCount);
        Assert.Equal(3, response.Inventory.InventoryLineCount);
        Assert.Equal(2, response.Inventory.OccupiedLocationCount);
        Assert.Equal(0, response.Inventory.UnoccupiedLocationCount);
        Assert.Equal(100m, response.Inventory.OccupiedLocationRatePercent);
        Assert.Null(response.Inventory.CapacityUtilizationPercent);
        Assert.Equal(
            "WMS_LOCATION_CAPACITY_NOT_AVAILABLE",
            response.Inventory.CapacityUtilizationReason);
        Assert.Equal(2, response.Inventory.DistinctOwnerCount);
        Assert.Equal(3, response.Inventory.DistinctMaterialCount);
        Assert.Equal(1, response.Tasks.ActiveTaskCount);
        Assert.Equal(2, response.Tasks.ActiveTaskStopCount);
        Assert.Equal(1, response.Anomalies.OverAllocatedInventoryLineCount);
        Assert.Equal(1, response.Anomalies.UnclassifiedAbcMaterialCount);
        Assert.Equal("2026-07-31", response.Abc.WindowEndDateExclusive);
        Assert.Equal("OutboundQuantityPreviousCumulativeShare", response.Abc.RankingMethod);
        Assert.Equal(1, response.Abc.ACount);
        Assert.Equal(1, response.Abc.BCount);
        Assert.Equal(0, response.Abc.CCount);
        Assert.Equal(1, response.Abc.UnclassifiedCount);
        Assert.Equal(
            ["A", "B", "Unclassified"],
            response.Abc.Materials.Select(value => value.Rank));
        Assert.Equal("A", response.Abc.Materials[0].Rank);
        Assert.Equal(0m, response.Abc.Materials[0].PreviousCumulativeSharePercent);
        Assert.Equal(80m, response.Abc.Materials[0].CumulativeSharePercent);
        Assert.Equal("B", response.Abc.Materials[1].Rank);
        Assert.Equal(80m, response.Abc.Materials[1].PreviousCumulativeSharePercent);
        Assert.Equal(2, response.Abc.Locations.Count);
        Assert.Equal("A", response.Abc.Locations[0].Rank);
        Assert.Equal("B", response.Abc.Locations[1].Rank);
        Assert.Single(response.Floors);
        Assert.Equal(2, response.Floors[0].OccupiedLocationCount);
    }

    [Fact]
    public async Task Warehouse_overview_keeps_model_metrics_and_marks_runtime_components_unavailable()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        fixture.Source.DeclaredKind = SpaceWmsDataSourceKind.Unavailable;

        var response = await fixture.Service.GetWarehouseOverviewAsync(
            fixture.SiteId);

        Assert.False(response.IsRuntimeComplete);
        Assert.Equal(100m, response.Model.TotalFloorAreaSquareMeters);
        Assert.Equal("Unavailable", response.Inventory.Source.Kind);
        Assert.Null(response.Inventory.InventoryLineCount);
        Assert.Null(response.Inventory.OccupiedLocationCount);
        Assert.Null(response.Inventory.OccupiedLocationRatePercent);
        Assert.Equal("Unavailable", response.Tasks.Source.Kind);
        Assert.Null(response.Tasks.ActiveTaskCount);
        Assert.Equal("Unavailable", response.Abc.Source.Kind);
        Assert.False(response.Abc.SpatialMappingAvailable);
        Assert.Null(response.Abc.MaterialCount);
        Assert.Empty(response.Abc.Materials);
        Assert.Empty(response.Abc.Locations);
    }

    [Fact]
    public async Task Warehouse_overview_marks_missing_floor_area_as_an_explicit_partial_snapshot()
    {
        await using var fixture = await RuntimeFixture.CreateWithBoundaryAsync(
            "{}",
            "L-001");

        var response = await fixture.Service.GetWarehouseOverviewAsync(
            fixture.SiteId);

        Assert.False(response.IsRuntimeComplete);
        Assert.Equal(1, response.Model.AreaMissingFloorCount);
        Assert.Null(response.Model.TotalFloorAreaSquareMeters);
        Assert.Null(response.Model.RackFootprintRatePercent);
        Assert.Equal(1, response.Anomalies.AreaMissingFloorCount);
        Assert.Null(Assert.Single(response.Floors).AreaSquareMeters);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public async Task Warehouse_overview_rejects_abc_window_outside_safe_bounds(
        int days)
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.GetWarehouseOverviewAsync(fixture.SiteId, days));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.RequestInvalid, error.Code);
    }

    [Fact]
    public async Task Warehouse_overview_fails_closed_for_duplicate_abc_materials()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        fixture.Source.InventoryItems =
        [
            new(fixture.LocationIds[0], "L-001", 1, 0,
                "SKU-A", null, null),
        ];
        fixture.Source.AbcItems =
        [
            new("SKU-A", 1, 10),
            new("SKU-A", 1, 5),
        ];

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.GetWarehouseOverviewAsync(fixture.SiteId));

        AssertContractViolation(error);
    }

    [Fact]
    public async Task Task_path_filters_at_source_and_explains_actual_cross_floor_workload()
    {
        var execution = Execution();
        var clock = new TestClock();
        await using var context = NewContext(execution, clock);
        var seeded = await SeedPublishedAsync(
            context,
            [
                new SeedLocation(LogicalId(1), "F1-A", FloorLevel: 1),
                new SeedLocation(LogicalId(2), "F2-B", FloorLevel: 2),
                new SeedLocation(LogicalId(3), "F2-C", FloorLevel: 2),
            ]);
        var source = new RecordingRuntimeSource
        {
            TaskItems =
            [
                new("TASK-1", "Pick", "Released", 2,
                    seeded.LocationIds[1], "F2-B", 3, "SKU-B"),
                new("OTHER", "Pick", "Released", 1,
                    seeded.LocationIds[2], "F2-C", 99, "SKU-X"),
                new("TASK-1", "Pick", "Released", 1,
                    seeded.LocationIds[0], "F1-A", 2, "SKU-A"),
                new("TASK-1", "Pick", "Released", 3,
                    seeded.LocationIds[2], "F2-C", null, "SKU-C"),
            ],
        };
        var service = CreateService(context, execution, clock, seeded.SiteId, source);

        var response = await service.GetTaskPathAsync(seeded.SiteId, " task-1 ");

        Assert.Equal("TASK-1", response.TaskId);
        Assert.Equal(3, response.StopCount);
        Assert.Equal(3, response.LocatedStopCount);
        Assert.Equal(2, response.FloorCount);
        Assert.Equal(2, response.ZoneCount);
        Assert.Equal(1, response.FloorTransitionCount);
        Assert.Equal(1, response.ZoneTransitionCount);
        Assert.Equal(5, response.TotalQuantity);
        Assert.True(response.CrossFloor);
        Assert.True(response.CrossZone);
        Assert.Equal([1, 2, 3], response.ActualStops.Select(value => value.SequenceNo));
        Assert.Equal(["F1", "F2"], response.Floors.Select(value => value.FloorCode));
        Assert.Equal([1, 2], response.Floors.Select(value => value.StopCount));
        Assert.Equal([2m, 3m], response.Floors.Select(value => value.TotalQuantity));
        Assert.Equal(2, response.Workloads.Count);
        Assert.Equal(2, response.Aisles.Count);
        Assert.All(source.TaskFilters, filter => Assert.Equal(["TASK-1"], filter));
    }

    [Fact]
    public async Task Task_path_distinguishes_empty_available_from_unavailable_source()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");

        var empty = await fixture.Service.GetTaskPathAsync(
            fixture.SiteId,
            "MISSING");

        Assert.True(empty.Source.IsAvailable);
        Assert.Empty(empty.ActualStops);
        Assert.Equal(0, empty.StopCount);
        Assert.Equal(["MISSING"], Assert.Single(fixture.Source.TaskFilters));

        fixture.Source.ResetCalls();
        fixture.Source.DeclaredKind = SpaceWmsDataSourceKind.Unavailable;
        var unavailable = await fixture.Service.GetTaskPathAsync(
            fixture.SiteId,
            "MISSING");

        Assert.False(unavailable.Source.IsAvailable);
        Assert.Empty(unavailable.ActualStops);
    }

    [Fact]
    public async Task Task_path_rejects_duplicate_actual_sequence_numbers()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        fixture.Source.TaskItems =
        [
            new("TASK-1", "Pick", "Released", 1,
                fixture.LocationIds[0], "L-001", 1, "SKU-A"),
            new("TASK-1", "Pick", "Released", 1,
                fixture.LocationIds[0], "L-001", 2, "SKU-B"),
        ];

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.GetTaskPathAsync(fixture.SiteId, "TASK-1"));

        AssertContractViolation(error);
    }

    [Fact]
    public async Task Inventory_locate_normalizes_ands_groups_and_sorts_cross_floor_hits()
    {
        var execution = Execution();
        var clock = new TestClock();
        await using var context = NewContext(execution, clock);
        var seededLocations = new[]
        {
            new SeedLocation(LogicalId(1), "F2-A", FloorLevel: 2),
            new SeedLocation(LogicalId(2), "F1-B", FloorLevel: 1),
            new SeedLocation(LogicalId(3), "F1-C", FloorLevel: 1),
        };
        var seeded = await SeedPublishedAsync(context, seededLocations);
        var source = new RecordingRuntimeSource
        {
            InventoryItems =
            [
                new(LogicalId(1), "F2-A", 5, 1, "SKU-01", "LOT-01", "BOX-01", "OWNER-A"),
                new(LogicalId(2), "F1-B", 3, 1, "SKU-01", "LOT-01", "BOX-01", "OWNER-A"),
                new(LogicalId(2), "F1-B", 2, 0, "SKU-01", "LOT-01", "BOX-01", "OWNER-A"),
                new(LogicalId(3), "F1-C", 99, 0, "SKU-01", "LOT-OTHER", "BOX-01", "OWNER-B"),
            ],
        };
        var service = CreateService(context, execution, clock, seeded.SiteId, source);

        var response = await service.LocateInventoryAsync(
            seeded.SiteId,
            new SpaceWmsInventoryLocateCriteria(
                "  SKU-01 ",
                " LOT-01 ",
                " BOX-01 ",
                " owner-a "));

        Assert.Equal("SKU-01", response.Criteria.MaterialNumber);
        Assert.Equal("LOT-01", response.Criteria.LotNumber);
        Assert.Equal("BOX-01", response.Criteria.ContainerNumber);
        Assert.Equal("OWNER-A", response.Criteria.OwnerId);
        Assert.Equal(2, response.LocationCount);
        Assert.Equal(2, response.FloorCount);
        Assert.Equal(["F1-B", "F2-A"],
            response.Items.Select(value => value.SpaceLocationCode).ToArray());
        var aggregated = response.Items[0];
        Assert.Equal(5, aggregated.PhysicalQuantity);
        Assert.Equal(1, aggregated.AllocatedQuantity);
        Assert.Equal(["SKU-01"], aggregated.MaterialNumbers);
        Assert.Equal(["LOT-01"], aggregated.LotNumbers);
        Assert.Equal(["BOX-01"], aggregated.ContainerNumbers);
        Assert.Equal(["OWNER-A"], aggregated.OwnerIds);
        Assert.All(source.InventoryCriteria, criteria =>
        {
            Assert.Equal("SKU-01", criteria!.MaterialNumber);
            Assert.Equal("LOT-01", criteria.LotNumber);
            Assert.Equal("BOX-01", criteria.ContainerNumber);
            Assert.Equal("OWNER-A", criteria.OwnerId);
        });
        Assert.All(source.InventoryOwnerScopes, ownerIds =>
            Assert.Equal(["OWNER-A"], ownerIds));
    }

    [Fact]
    public async Task Inventory_locate_distinguishes_empty_available_and_unavailable_sources()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");

        var empty = await fixture.Service.LocateInventoryAsync(
            fixture.SiteId,
            new SpaceWmsInventoryLocateCriteria("MISSING", null, null));

        Assert.True(empty.Source.IsAvailable);
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.LocationCount);
        Assert.Equal(0, empty.FloorCount);

        fixture.Source.ResetCalls();
        fixture.Source.DeclaredKind = SpaceWmsDataSourceKind.Unavailable;
        var unavailable = await fixture.Service.LocateInventoryAsync(
            fixture.SiteId,
            new SpaceWmsInventoryLocateCriteria("MISSING", null, null));

        Assert.False(unavailable.Source.IsAvailable);
        Assert.Empty(unavailable.Items);
    }

    [Fact]
    public async Task Inventory_locate_rejects_empty_criteria_before_scope_or_wms_query()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.LocateInventoryAsync(
                fixture.SiteId,
                new SpaceWmsInventoryLocateCriteria(" ", "", null)));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.RequestInvalid, error.Code);
        Assert.Empty(fixture.Source.InventoryBatchSizes);
    }

    [Fact]
    public async Task Inventory_locate_rejects_source_items_outside_exact_criteria()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        fixture.Source.IgnoreLocateCriteria = true;
        fixture.Source.InventoryItems =
        [
            new(fixture.LocationIds[0], "L-001", 1, 0, "OTHER", null, null),
        ];

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.LocateInventoryAsync(
                fixture.SiteId,
                new SpaceWmsInventoryLocateCriteria("SKU-01", null, null)));

        Assert.Equal(502, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.WmsRuntimeContractViolation, error.Code);
    }

    [Fact]
    public async Task Inventory_locate_rejects_source_items_outside_owner_criterion()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        fixture.Source.IgnoreLocateCriteria = true;
        fixture.Source.InventoryItems =
        [
            new(
                fixture.LocationIds[0],
                "L-001",
                1,
                0,
                "SKU-01",
                null,
                null,
                "OWNER-B"),
        ];

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.LocateInventoryAsync(
                fixture.SiteId,
                new SpaceWmsInventoryLocateCriteria(
                    null,
                    null,
                    null,
                    "owner-a")));

        Assert.Equal(502, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.WmsRuntimeContractViolation, error.Code);
    }

    [Fact]
    public async Task Exactly_10000_locations_use_twenty_500_item_chunks()
    {
        var execution = Execution();
        var clock = new TestClock();
        await using var context = NewContext(execution, clock);
        var seeded = await SeedPublishedAsync(
            context,
            Enumerable.Range(1, 10_000).Select(value => $"L-{value:00000}").ToArray());
        var source = new RecordingRuntimeSource();
        var service = CreateService(context, execution, clock, seeded.SiteId, source);

        var stopwatch = Stopwatch.StartNew();
        await service.QueryInventoryAsync(seeded.SiteId);
        var inventoryElapsed = stopwatch.Elapsed;
        stopwatch.Restart();
        await service.QueryTasksAsync(seeded.SiteId);
        var taskElapsed = stopwatch.Elapsed;

        Assert.Equal(Enumerable.Repeat(500, 20), source.InventoryBatchSizes);
        Assert.Equal(Enumerable.Repeat(500, 20), source.TaskBatchSizes);
        Assert.InRange(inventoryElapsed.TotalMilliseconds, 0, 3_000);
        Assert.InRange(taskElapsed.TotalMilliseconds, 0, 3_000);
    }

    [Fact]
    public async Task Requested_location_must_be_active_in_current_published_version()
    {
        var execution = Execution();
        var clock = new TestClock();
        await using var context = NewContext(execution, clock);
        var seeded = await SeedPublishedAsync(context, "L-001");
        var source = new RecordingRuntimeSource();
        var service = CreateService(context, execution, clock, seeded.SiteId, source);

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.QueryTasksAsync(seeded.SiteId, [Guid.NewGuid()]));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.LogicalIdNotFound, error.Code);
        Assert.Empty(source.TaskBatchSizes);
    }

    [Fact]
    public async Task Repeated_requested_locations_count_once_toward_query_limit()
    {
        var execution = Execution();
        var clock = new TestClock();
        await using var context = NewContext(execution, clock);
        var seeded = await SeedPublishedAsync(context, "L-001");
        var source = new RecordingRuntimeSource();
        var service = CreateService(context, execution, clock, seeded.SiteId, source);
        var repeated = Enumerable.Repeat(seeded.LocationIds[0], 10_001).ToArray();

        await service.QueryInventoryAsync(seeded.SiteId, repeated);

        Assert.Equal([1], source.InventoryBatchSizes);
    }

    [Fact]
    public async Task Full_site_query_over_limit_fails_before_wms()
    {
        var execution = Execution();
        var clock = new TestClock();
        await using var context = NewContext(execution, clock);
        var seeded = await SeedPublishedAsync(
            context,
            Enumerable.Range(1, 10_001)
                .Select(value => $"L-{value:00000}")
                .ToArray());
        var source = new RecordingRuntimeSource();
        var service = CreateService(context, execution, clock, seeded.SiteId, source);

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.QueryInventoryAsync(seeded.SiteId));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.RequestInvalid, error.Code);
        Assert.Empty(source.InventoryBatchSizes);
    }

    [Fact]
    public async Task Current_published_native_and_adopted_identity_collision_fails_before_wms()
    {
        var execution = Execution();
        var clock = new TestClock();
        await using var context = NewContext(execution, clock);
        var seeded = await SeedPublishedAsync(context, "NATIVE-01", "ADOPTED-01");
        var source = new RecordingRuntimeSource();
        var adoption = SpaceWmsAdoption.Discover(
            execution.TenantId,
            seeded.SiteId,
            source.RuntimeAdapterId,
            source.RuntimeDataSourceId,
            source.RuntimeDataSourceKind.ToString(),
            seeded.LocationIds[0],
            "external-adopted-01",
            "ADOPTED-01",
            true,
            "1",
            Hash,
            Now);
        adoption.Bind(seeded.PublishedVersionId, seeded.LocationIds[1], Now);
        context.WmsAdoptions.Add(adoption);
        await context.SaveChangesAsync();
        var service = CreateService(context, execution, clock, seeded.SiteId, source);

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.QueryTasksAsync(seeded.SiteId, [seeded.LocationIds[1]]));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.WmsAdoptionDuplicate, error.Code);
        Assert.Empty(source.TaskBatchSizes);
    }

    [Fact]
    public async Task Inventory_and_tasks_are_globally_sorted_after_all_chunks()
    {
        var execution = Execution();
        var clock = new TestClock();
        await using var context = NewContext(execution, clock);
        var seededLocations = Enumerable.Range(1, 501)
            .Select(value => new SeedLocation(
                LogicalId(value),
                value switch
                {
                    1 => "Z-0001",
                    501 => "A-0001",
                    _ => $"M-{value:0000}",
                }))
            .ToArray();
        var seeded = await SeedPublishedAsync(context, seededLocations);
        var firstChunkId = seeded.LocationIds[0];
        var secondChunkId = seeded.LocationIds[500];
        var source = new RecordingRuntimeSource
        {
            InventoryItems =
            [
                new(firstChunkId, "Z-0001", 1, 0, "M1", null, null),
                new(firstChunkId, "Z-0001", 2, 0, null, "L1", null),
                new(secondChunkId, "A-0001", 3, 0, "Z1", null, null),
                new(firstChunkId, "Z-0001", 4, 0, null, null, "C2"),
                new(firstChunkId, "Z-0001", 5, 0, null, null, "C1"),
            ],
            TaskItems =
            [
                new("B", "Pick", "Released", 1, firstChunkId, "Z-0001", 1, "M1"),
                new("A", "Pick", "Released", 2, firstChunkId, "Z-0001", 1, "M1"),
                new("A", "Pick", "Released", 1, secondChunkId, "A-0001", 1, "M1"),
                new("A", "Pick", "Released", 1, firstChunkId, "Z-0001", 1, "M1"),
            ],
        };
        var service = CreateService(context, execution, clock, seeded.SiteId, source);

        var inventory = await service.QueryInventoryAsync(seeded.SiteId);
        var tasks = await service.QueryTasksAsync(seeded.SiteId);

        Assert.Equal(
            [
                "A-0001|Z1|<null>|<null>",
                "Z-0001|<null>|<null>|C1",
                "Z-0001|<null>|<null>|C2",
                "Z-0001|<null>|L1|<null>",
                "Z-0001|M1|<null>|<null>",
            ],
            inventory.Items.Select(value =>
                $"{value.SpaceLocationCode}|{value.MaterialNumber ?? "<null>"}|" +
                $"{value.LotNumber ?? "<null>"}|{value.ContainerNumber ?? "<null>"}")
                .ToArray());
        Assert.Equal(
            [
                $"A|1|{firstChunkId:D}",
                $"A|1|{secondChunkId:D}",
                $"A|2|{firstChunkId:D}",
                $"B|1|{firstChunkId:D}",
            ],
            tasks.Items.Select(value =>
                $"{value.TaskId}|{value.SequenceNo}|{value.LocationLogicalId:D}")
                .ToArray());
    }

    [Fact]
    public async Task Unavailable_source_returns_empty_with_explicit_flags()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        fixture.Source.DeclaredKind = SpaceWmsDataSourceKind.Unavailable;

        var response = await fixture.Service.QueryInventoryAsync(fixture.SiteId);

        Assert.Empty(response.Items);
        Assert.Equal("Unavailable", response.Source.Kind);
        Assert.False(response.Source.IsAvailable);
        Assert.False(response.Source.IsSimulated);
    }

    [Fact]
    public async Task Returned_identity_outside_requested_scope_is_a_502_contract_violation()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        fixture.Source.UnexpectedInventoryIdentity = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.QueryInventoryAsync(fixture.SiteId));

        AssertContractViolation(error);
    }

    [Fact]
    public async Task Transport_failure_is_retryable_without_exposing_adapter_details()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        const string secret = "password=TOP_SECRET_TOKEN";
        fixture.Source.QueryException = new TimeoutException(secret);

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.QueryTasksAsync(fixture.SiteId));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.WmsUnavailable, error.Code);
        Assert.True(error.Retryable);
        Assert.DoesNotContain(secret, error.Detail ?? string.Empty);
    }

    [Fact]
    public async Task Cancellation_from_inside_source_call_is_preserved()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        using var cancellation = new CancellationTokenSource();
        fixture.Source.CancelTasksOnEntry = cancellation;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.QueryTasksAsync(
                fixture.SiteId,
                cancellationToken: cancellation.Token));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, fixture.Source.TaskQueryEntryCount);
    }

    [Fact]
    public async Task Multi_chunk_snapshot_uses_earliest_observation_and_rejects_source_change()
    {
        await using var fixture = await RuntimeFixture.CreateAsync(
            Enumerable.Range(1, 501).Select(value => $"L-{value:0000}").ToArray());
        fixture.Source.Observations =
        [
            new DateTimeOffset(2026, 7, 31, 16, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 31, 15, 59, 55, TimeSpan.Zero),
        ];

        var response = await fixture.Service.QueryInventoryAsync(fixture.SiteId);

        Assert.Equal(
            new DateTimeOffset(2026, 7, 31, 15, 59, 55, TimeSpan.Zero),
            response.Source.ObservedAtUtc);
        Assert.Equal(new DateTimeOffset(Now), response.Source.ReceivedAtUtc);
        Assert.Equal(5_000, response.Source.DelayMilliseconds);
        Assert.Equal(0, response.Source.ClockSkewMilliseconds);

        fixture.Source.ResetCalls();
        fixture.Source.ReturnedDataSourceIds = ["RECORDING_WMS", "OTHER_WMS"];
        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.QueryInventoryAsync(fixture.SiteId));

        AssertContractViolation(error);
    }

    [Fact]
    public async Task Empty_published_selection_keeps_declared_source_without_calling_wms()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();

        var response = await fixture.Service.QueryInventoryAsync(fixture.SiteId);

        Assert.Empty(response.Items);
        Assert.Equal("Real", response.Source.Kind);
        Assert.Equal(fixture.Source.RuntimeAdapterId, response.Source.AdapterId);
        Assert.Equal(new DateTimeOffset(Now), response.Source.ReceivedAtUtc);
        Assert.True(response.Source.IsAvailable);
        Assert.Empty(fixture.Source.InventoryBatchSizes);
    }

    [Fact]
    public async Task Runtime_source_reports_receive_delay_and_forward_clock_skew()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        fixture.Clock.UtcNow = Now.AddSeconds(12);

        var delayed = await fixture.Service.QueryInventoryAsync(fixture.SiteId);

        Assert.Equal("recording-wms-v1", delayed.Source.AdapterId);
        Assert.Equal(new DateTimeOffset(Now.AddSeconds(12)), delayed.Source.ReceivedAtUtc);
        Assert.Equal(12_000, delayed.Source.DelayMilliseconds);
        Assert.Equal(0, delayed.Source.ClockSkewMilliseconds);

        fixture.Source.ResetCalls();
        fixture.Source.Observations = [new DateTimeOffset(Now.AddSeconds(3))];
        fixture.Clock.UtcNow = Now;

        var skewed = await fixture.Service.QueryTasksAsync(fixture.SiteId);

        Assert.Equal(0, skewed.Source.DelayMilliseconds);
        Assert.Equal(3_000, skewed.Source.ClockSkewMilliseconds);
    }

    [Fact]
    public async Task Runtime_source_rejects_a_non_utc_receive_clock()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        fixture.Clock.UtcNow = DateTime.SpecifyKind(Now, DateTimeKind.Local);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.QueryInventoryAsync(fixture.SiteId));

        Assert.Equal("The Space clock must return UTC.", error.Message);
    }

    [Fact]
    public async Task More_than_10000_requested_locations_fail_before_wms()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        var requested = Enumerable.Range(0, 10_001)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.QueryInventoryAsync(fixture.SiteId, requested));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.RequestInvalid, error.Code);
        Assert.Empty(fixture.Source.InventoryBatchSizes);
    }

    [Fact]
    public async Task Null_item_collections_are_502_contract_violations()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        fixture.Source.ReturnNullInventoryItems = true;

        var inventoryError = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.QueryInventoryAsync(fixture.SiteId));

        AssertContractViolation(inventoryError);
        fixture.Source.ReturnNullInventoryItems = false;
        fixture.Source.ReturnNullTaskItems = true;
        var taskError = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.QueryTasksAsync(fixture.SiteId));
        AssertContractViolation(taskError);
    }

    [Theory]
    [InlineData("inventory-result")]
    [InlineData("inventory-source")]
    [InlineData("inventory-element")]
    [InlineData("task-result")]
    [InlineData("task-source")]
    [InlineData("task-element")]
    public async Task Null_adapter_output_is_a_502_contract_violation(
        string invalidCase)
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        switch (invalidCase)
        {
            case "inventory-result":
                fixture.Source.ReturnNullInventoryResult = true;
                break;
            case "inventory-source":
                fixture.Source.ReturnNullInventorySource = true;
                break;
            case "inventory-element":
                fixture.Source.ReturnNullInventoryElement = true;
                break;
            case "task-result":
                fixture.Source.ReturnNullTaskResult = true;
                break;
            case "task-source":
                fixture.Source.ReturnNullTaskSource = true;
                break;
            case "task-element":
                fixture.Source.ReturnNullTaskElement = true;
                break;
            default:
                throw new InvalidOperationException("Unknown test case.");
        }

        var error = invalidCase.StartsWith("inventory", StringComparison.Ordinal)
            ? await Assert.ThrowsAsync<SpaceProblemException>(() =>
                fixture.Service.QueryInventoryAsync(fixture.SiteId))
            : await Assert.ThrowsAsync<SpaceProblemException>(() =>
                fixture.Service.QueryTasksAsync(fixture.SiteId));

        AssertContractViolation(error);
    }

    [Fact]
    public async Task Returned_identity_must_belong_to_current_500_item_chunk()
    {
        await using var fixture = await RuntimeFixture.CreateAsync(
            Enumerable.Range(1, 501).Select(value => $"L-{value:0000}").ToArray());
        fixture.Source.UnexpectedInventoryIdentity = fixture.LocationIds[500];

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.QueryInventoryAsync(fixture.SiteId));

        AssertContractViolation(error);
    }

    [Fact]
    public async Task Later_unavailable_chunk_discards_prior_items()
    {
        await using var fixture = await RuntimeFixture.CreateAsync(
            Enumerable.Range(1, 501).Select(value => $"L-{value:0000}").ToArray());
        fixture.Source.InventoryOverrideItem = new(
            fixture.LocationIds[0],
            "L-0001",
            1,
            0,
            "M1",
            null,
            null);
        fixture.Source.ReturnedKinds =
        [
            SpaceWmsDataSourceKind.Real,
            SpaceWmsDataSourceKind.Unavailable,
        ];
        fixture.Source.Observations =
        [
            new DateTimeOffset(2026, 7, 31, 15, 59, 55, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 31, 16, 0, 0, TimeSpan.Zero),
        ];

        var response = await fixture.Service.QueryInventoryAsync(fixture.SiteId);

        Assert.Empty(response.Items);
        Assert.Equal("Unavailable", response.Source.Kind);
        Assert.False(response.Source.IsAvailable);
        Assert.False(response.Source.IsSimulated);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 31, 15, 59, 55, TimeSpan.Zero),
            response.Source.ObservedAtUtc);
        Assert.Equal([500, 1], fixture.Source.InventoryBatchSizes);
    }

    [Fact]
    public async Task Later_unavailable_chunk_cannot_change_data_source_identity()
    {
        await using var fixture = await RuntimeFixture.CreateAsync(
            Enumerable.Range(1, 501).Select(value => $"L-{value:0000}").ToArray());
        fixture.Source.ReturnedKinds =
        [
            SpaceWmsDataSourceKind.Real,
            SpaceWmsDataSourceKind.Unavailable,
        ];
        fixture.Source.ReturnedDataSourceIds = ["RECORDING_WMS", "OTHER_WMS"];

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.QueryInventoryAsync(fixture.SiteId));

        AssertContractViolation(error);
    }

    [Theory]
    [InlineData("empty-identity")]
    [InlineData("outside-identity")]
    [InlineData("blank-location-code")]
    public async Task Invalid_inventory_items_are_502_contract_violations(string invalidCase)
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        fixture.Source.InventoryOverrideItem = invalidCase switch
        {
            "empty-identity" => new(Guid.Empty, "L-001", 1, 0, null, null, null),
            "outside-identity" => new(Guid.NewGuid(), "L-001", 1, 0, null, null, null),
            "blank-location-code" => new(
                fixture.LocationIds[0], " ", 1, 0, null, null, null),
            _ => throw new InvalidOperationException("Unknown test case."),
        };

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.QueryInventoryAsync(fixture.SiteId));

        AssertContractViolation(error);
    }

    [Theory]
    [InlineData("blank-task-id")]
    [InlineData("blank-task-type")]
    [InlineData("blank-status")]
    [InlineData("invalid-sequence")]
    [InlineData("blank-location-code")]
    public async Task Invalid_task_items_are_502_contract_violations(string invalidCase)
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        var taskId = invalidCase == "blank-task-id" ? " " : "TASK-1";
        var taskType = invalidCase == "blank-task-type" ? " " : "Pick";
        var status = invalidCase == "blank-status" ? " " : "Released";
        var sequence = invalidCase == "invalid-sequence" ? 0 : 1;
        var locationCode = invalidCase == "blank-location-code" ? " " : "L-001";
        fixture.Source.TaskOverrideItem = new(
            taskId,
            taskType,
            status,
            sequence,
            fixture.LocationIds[0],
            locationCode,
            1,
            "M1");

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.QueryTasksAsync(fixture.SiteId));

        AssertContractViolation(error);
    }

    [Theory]
    [InlineData("undefined-kind")]
    [InlineData("blank-source-id")]
    [InlineData("long-source-id")]
    [InlineData("default-observation")]
    [InlineData("kind-mismatch")]
    [InlineData("source-id-mismatch")]
    public async Task Invalid_source_metadata_is_a_502_contract_violation(string invalidCase)
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        switch (invalidCase)
        {
            case "undefined-kind":
                fixture.Source.ReturnedKinds = [(SpaceWmsDataSourceKind)999];
                break;
            case "blank-source-id":
                fixture.Source.ReturnedDataSourceIds = [" "];
                break;
            case "long-source-id":
                fixture.Source.ReturnedDataSourceIds = [new string('x', 101)];
                break;
            case "default-observation":
                fixture.Source.Observations = [default];
                break;
            case "kind-mismatch":
                fixture.Source.ReturnedKinds = [SpaceWmsDataSourceKind.Simulated];
                break;
            case "source-id-mismatch":
                fixture.Source.ReturnedDataSourceIds = ["OTHER_WMS"];
                break;
            default:
                throw new InvalidOperationException("Unknown test case.");
        }

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.QueryInventoryAsync(fixture.SiteId));

        AssertContractViolation(error);
    }

    private static void AssertContractViolation(SpaceProblemException error)
    {
        Assert.Equal(502, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.WmsRuntimeContractViolation, error.Code);
        Assert.False(error.Retryable);
    }

    private sealed class RuntimeFixture : IAsyncDisposable
    {
        private RuntimeFixture(
            SpaceContext context,
            TestClock clock,
            RecordingRuntimeSource source,
            SpaceWmsRuntimeService service,
            Guid siteId,
            IReadOnlyList<Guid> locationIds)
        {
            Context = context;
            Clock = clock;
            Source = source;
            Service = service;
            SiteId = siteId;
            LocationIds = locationIds;
        }

        public SpaceContext Context { get; }
        public TestClock Clock { get; }
        public RecordingRuntimeSource Source { get; }
        public SpaceWmsRuntimeService Service { get; }
        public Guid SiteId { get; }
        public IReadOnlyList<Guid> LocationIds { get; }

        public static async Task<RuntimeFixture> CreateAsync(
            params string[] locationCodes) =>
            await CreateCoreAsync(null, locationCodes);

        public static async Task<RuntimeFixture> CreateWithBoundaryAsync(
            string boundaryJson,
            params string[] locationCodes) =>
            await CreateCoreAsync(boundaryJson, locationCodes);

        private static async Task<RuntimeFixture> CreateCoreAsync(
            string? boundaryJson,
            IReadOnlyList<string> locationCodes)
        {
            var execution = Execution();
            var clock = new TestClock();
            var context = NewContext(execution, clock);
            try
            {
                var seeded = await SeedPublishedAsync(
                    context,
                    locationCodes
                        .Select(value => new SeedLocation(Guid.NewGuid(), value))
                        .ToArray(),
                    boundaryJson);
                var source = new RecordingRuntimeSource();
                var service = CreateService(
                    context,
                    execution,
                    clock,
                    seeded.SiteId,
                    source);
                return new RuntimeFixture(
                    context,
                    clock,
                    source,
                    service,
                    seeded.SiteId,
                    seeded.LocationIds);
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private static TestExecutionContext Execution() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private static SpaceWmsRuntimeService CreateService(
        SpaceContext context,
        TestExecutionContext execution,
        TestClock clock,
        Guid siteId,
        ISpaceWmsRuntimeSource source,
        TestAccessEvaluator? access = null) =>
        new(
            context,
            execution,
            clock,
            access ?? new TestAccessEvaluator(siteId),
            new TestWarehouseResolver(siteId),
            source);

    private sealed record SeededPublished(
        Guid SiteId,
        Guid PublishedVersionId,
        IReadOnlyList<Guid> LocationIds);

    private sealed record SeedLocation(
        Guid LogicalId,
        string Code,
        int FloorLevel = 1);

    private const string ValidFloorBoundary =
        """
        {"schemaVersion":1,"kind":"polygon","points":[[0,0],[10000,0],[10000,10000],[0,10000]]}
        """;

    private static async Task<SeededPublished> SeedPublishedAsync(
        SpaceContext context,
        params string[] locationCodes) =>
        await SeedPublishedAsync(
            context,
            locationCodes
                .Select(value => new SeedLocation(Guid.NewGuid(), value))
                .ToArray());

    private static async Task<SeededPublished> SeedPublishedAsync(
        SpaceContext context,
        IReadOnlyList<SeedLocation> seededLocations,
        string? boundaryJson = null)
    {
        var tenantId = context.CurrentTenantId;
        var siteId = Guid.NewGuid();
        var model = SpaceModel.Create(tenantId, siteId);
        var version = SpaceModelVersion.CreateDraft(
            tenantId, model.Id, 1, "Published runtime");
        var locationIds = seededLocations.Select(value => value.LogicalId).ToArray();
        context.AddRange(model, version);
        foreach (var floorGroup in seededLocations.GroupBy(value => value.FloorLevel))
        {
            var floorLogicalId = Guid.NewGuid();
            var zoneLogicalId = Guid.NewGuid();
            var aisleLogicalId = Guid.NewGuid();
            var rackLogicalId = Guid.NewGuid();
            var floor = SpaceFloorRevision.Create(
                tenantId,
                version.Id,
                floorLogicalId,
                siteId,
                floorGroup.Key,
                $"F{floorGroup.Key}",
                $"Floor {floorGroup.Key}",
                elevation: (floorGroup.Key - 1) * 5_000,
                height: 5_000);
            floor.ConfigureBoundary(
                boundaryJson ?? ValidFloorBoundary,
                "RH_Z_UP_MM");
            var zone = SpaceZoneRevision.Create(
                tenantId,
                version.Id,
                zoneLogicalId,
                floorLogicalId,
                $"STORAGE-{floorGroup.Key}",
                zoneType: 1);
            var aisle = SpaceAisleRevision.Create(
                tenantId,
                version.Id,
                aisleLogicalId,
                zoneLogicalId,
                $"AISLE-{floorGroup.Key:00}",
                direction: 0);
            aisle.ConfigureShape("[]", "[[0,500],[10000,500]]");
            var rack = SpaceRackRevision.Create(
                tenantId,
                version.Id,
                rackLogicalId,
                floorLogicalId,
                zoneLogicalId,
                $"RACK-{floorGroup.Key:00}",
                aisleLogicalId);
            var floorLocations = floorGroup.ToArray();
            rack.ConfigureGeometry(
                0, 0, 0, 0,
                width: Math.Max(1, floorLocations.Length) * 1_000,
                depth: 1_100,
                height: 4_000);
            var level = SpaceRackLevelRevision.Create(
                tenantId, version.Id, Guid.NewGuid(), rackLogicalId,
                levelNo: 1,
                bottomZ: 0,
                clearHeight: 1_200,
                binCount: Math.Max(1, floorLocations.Length),
                depthCount: 1,
                cellWidth: 1_000,
                cellDepth: 1_100);
            var locations = floorLocations.Select((seed, index) =>
                SpaceLocationRevision.Create(
                    tenantId, version.Id, seed.LogicalId, floorLogicalId,
                    rackLogicalId, seed.Code,
                    columnNo: index + 1,
                    levelNo: 1,
                    depthNo: 1,
                    width: 1_000,
                    height: 1_200,
                    depth: 1_100)).ToArray();
            context.AddRange(floor, zone, aisle, rack, level);
            context.AddRange(locations);
        }
        await context.SaveChangesAsync();
        version.BeginValidation();
        version.MarkReady(Hash, "space-v1", Hash);
        version.BeginPublishing();
        version.MarkPublished(Guid.NewGuid(), Now);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(version);
        model.ActivateDesignV1();
        await context.SaveChangesAsync();
        return new SeededPublished(siteId, version.Id, locationIds);
    }

    private static Guid LogicalId(int value) =>
        Guid.Parse($"10000000-0000-0000-0000-{value:000000000000}");

    private static SpaceContext NewContext(
        ISpaceExecutionContext execution,
        ISpaceClock clock) =>
        new(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            execution,
            clock);

    private static SpaceWmsContext WmsContext(
        TestExecutionContext execution,
        Guid siteId) =>
        new(execution.TenantId, siteId, "WH1", execution.CorrelationId);

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId,
        Guid CorrelationId) :
        ISpaceExecutionContext,
        ISpaceCorrelationContext;

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow { get; set; } = Now;
    }

    private sealed class TestAccessEvaluator(Guid allowedSiteId) :
        ISpaceDesignAccessEvaluator
    {
        public List<bool> Writes { get; } = [];

        public void EnsureSiteAccess(Guid siteId, bool write)
        {
            if (siteId != allowedSiteId)
                throw new InvalidOperationException("Site access denied.");
            Writes.Add(write);
        }
    }

    private sealed class TestWarehouseResolver(Guid siteId) :
        ISpaceWarehouseResolver
    {
        public Task<SpaceWarehouseIdentity?> ResolveAsync(
            Guid requestedSiteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SpaceWarehouseIdentity?>(
                requestedSiteId == siteId
                    ? new SpaceWarehouseIdentity(siteId, "SITE", "WH1")
                    : null);
    }

    private sealed class RecordingRuntimeSource : ISpaceWmsRuntimeSource
    {
        private int _callIndex;

        public string RuntimeAdapterId => "recording-wms-v1";
        public string RuntimeDataSourceId => DeclaredDataSourceId;
        public SpaceWmsDataSourceKind RuntimeDataSourceKind => DeclaredKind;
        public string DeclaredDataSourceId { get; set; } = "RECORDING_WMS";
        public SpaceWmsDataSourceKind DeclaredKind { get; set; } =
            SpaceWmsDataSourceKind.Real;
        public Exception? QueryException { get; set; }
        public Guid? UnexpectedInventoryIdentity { get; set; }
        public IReadOnlyList<DateTimeOffset> Observations { get; set; } =
            [new DateTimeOffset(Now)];
        public IReadOnlyList<string> ReturnedDataSourceIds { get; set; } =
            ["RECORDING_WMS"];
        public IReadOnlyList<SpaceWmsDataSourceKind>? ReturnedKinds { get; set; }
        public bool ReturnNullInventoryResult { get; set; }
        public bool ReturnNullTaskResult { get; set; }
        public bool ReturnNullInventorySource { get; set; }
        public bool ReturnNullTaskSource { get; set; }
        public bool ReturnNullInventoryItems { get; set; }
        public bool ReturnNullTaskItems { get; set; }
        public bool ReturnNullInventoryElement { get; set; }
        public bool IgnoreLocateCriteria { get; set; }
        public bool ReturnNullTaskElement { get; set; }
        public CancellationTokenSource? CancelTasksOnEntry { get; set; }
        public int TaskQueryEntryCount { get; private set; }
        public SpaceWmsInventoryItem? InventoryOverrideItem { get; set; }
        public SpaceWmsTaskItem? TaskOverrideItem { get; set; }
        public List<int> InventoryBatchSizes { get; } = [];
        public List<SpaceWmsInventoryLocateCriteria?> InventoryCriteria { get; } = [];
        public List<IReadOnlyList<string>?> InventoryOwnerScopes { get; } = [];
        public List<int> TaskBatchSizes { get; } = [];
        public List<IReadOnlyList<string>?> TaskFilters { get; } = [];
        public IReadOnlyList<SpaceWmsInventoryItem> InventoryItems { get; set; } = [];
        public IReadOnlyList<SpaceWmsTaskItem> TaskItems { get; set; } = [];
        public IReadOnlyList<SpaceWmsAbcAggregate> AbcItems { get; set; } = [];

        public void ResetCalls()
        {
            _callIndex = 0;
            TaskQueryEntryCount = 0;
            InventoryBatchSizes.Clear();
            InventoryCriteria.Clear();
            InventoryOwnerScopes.Clear();
            TaskBatchSizes.Clear();
            TaskFilters.Clear();
        }

        public Task<SpaceWmsInventoryResult> QueryInventoryAsync(
            SpaceWmsInventoryQuery request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (QueryException is not null)
                throw QueryException;
            InventoryBatchSizes.Add(request.LogicalIds.Count);
            InventoryCriteria.Add(request.LocateCriteria);
            InventoryOwnerScopes.Add(request.OwnerIds);
            if (ReturnNullInventoryResult)
                return Task.FromResult<SpaceWmsInventoryResult>(null!);
            var requested = request.LogicalIds.ToHashSet();
            IReadOnlyList<SpaceWmsInventoryItem>? items = ReturnNullInventoryItems
                ? null
                : ReturnNullInventoryElement
                    ? new SpaceWmsInventoryItem[] { null! }
                    : UnexpectedInventoryIdentity.HasValue
                        ?
                        [
                            new(
                                UnexpectedInventoryIdentity.Value,
                                "UNEXPECTED",
                                1,
                                0,
                                null,
                                null,
                                null),
                        ]
                        : InventoryOverrideItem is not null
                            ? [InventoryOverrideItem]
                            : InventoryItems
                                .Where(value =>
                                    requested.Contains(value.LogicalId) &&
                                    (IgnoreLocateCriteria ||
                                     MatchesLocate(value, request.LocateCriteria)))
                                .ToArray();
            var source = ReturnNullInventorySource ? null! : NextSource();
            return Task.FromResult(new SpaceWmsInventoryResult(
                source,
                items!));
        }

        public Task<SpaceWmsTaskResult> QueryTasksAsync(
            SpaceWmsTaskQuery request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            TaskQueryEntryCount++;
            if (CancelTasksOnEntry is not null)
            {
                CancelTasksOnEntry.Cancel();
                ct.ThrowIfCancellationRequested();
            }
            if (QueryException is not null)
                throw QueryException;
            TaskBatchSizes.Add(request.LogicalIds.Count);
            TaskFilters.Add(request.TaskIds);
            if (ReturnNullTaskResult)
                return Task.FromResult<SpaceWmsTaskResult>(null!);
            var requested = request.LogicalIds.ToHashSet();
            IReadOnlyList<SpaceWmsTaskItem>? items = ReturnNullTaskItems
                ? null
                : ReturnNullTaskElement
                    ? new SpaceWmsTaskItem[] { null! }
                    : TaskOverrideItem is not null
                        ? [TaskOverrideItem]
                        : TaskItems.Where(value =>
                                requested.Contains(value.LogicalId) &&
                                (request.TaskIds is null ||
                                 request.TaskIds.Contains(
                                     value.TaskId,
                                     StringComparer.Ordinal)))
                              .ToArray();
            var source = ReturnNullTaskSource ? null! : NextSource();
            return Task.FromResult(new SpaceWmsTaskResult(
                source,
                items!));
        }

        public Task<SpaceWmsAbcResult> QueryAbcAsync(
            SpaceWmsAbcQuery request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (QueryException is not null)
                throw QueryException;
            return Task.FromResult(new SpaceWmsAbcResult(
                NextSource(),
                AbcItems));
        }

        private SpaceWmsSourceMetadata NextSource()
        {
            var sourceIndex = Math.Min(_callIndex, ReturnedDataSourceIds.Count - 1);
            var observationIndex = Math.Min(_callIndex, Observations.Count - 1);
            var kind = ReturnedKinds is null
                ? DeclaredKind
                : ReturnedKinds[Math.Min(_callIndex, ReturnedKinds.Count - 1)];
            _callIndex++;
            return new SpaceWmsSourceMetadata(
                kind,
                ReturnedDataSourceIds[sourceIndex],
                Observations[observationIndex]);
        }

        private static bool MatchesLocate(
            SpaceWmsInventoryItem item,
            SpaceWmsInventoryLocateCriteria? criteria) =>
            criteria is null ||
            (item.PhysicalQuantity > 0 &&
             Matches(item.MaterialNumber, criteria.MaterialNumber) &&
             Matches(item.LotNumber, criteria.LotNumber) &&
             Matches(item.ContainerNumber, criteria.ContainerNumber) &&
             (criteria.OwnerId is null || string.Equals(
                 item.OwnerId,
                 criteria.OwnerId,
                 StringComparison.OrdinalIgnoreCase)));

        private static bool Matches(string? actual, string? expected) =>
            expected is null || string.Equals(actual, expected, StringComparison.Ordinal);
    }
}
