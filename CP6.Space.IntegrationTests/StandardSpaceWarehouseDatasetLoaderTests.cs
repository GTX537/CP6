using CP6.Space.Application;
using CP6.Space.Infrastructure;

namespace CP6.Space.IntegrationTests;

public sealed class StandardSpaceWarehouseDatasetLoaderTests
{
    private static readonly SpaceWmsContext Context = new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("20000000-0000-0000-0000-000000000002"),
        SpaceStandardWarehouseDatasetContract.WarehouseCode,
        Guid.Parse("30000000-0000-0000-0000-000000000003"));

    [Fact]
    public async Task Loads_standard_dataset_into_simulator_in_atomic_batches()
    {
        var simulator = new StandardSpaceWmsSimulator();
        var loader = new StandardSpaceWarehouseDatasetLoader(simulator);
        var dataset = SpaceStandardWarehouseDatasetGenerator.Generate();

        var result = await loader.LoadAsync(Context, dataset);
        var locations = await simulator.QueryLocationsAsync(
            new SpaceWmsLocationQuery(Context, []));
        var inventory = await simulator.QueryInventoryAsync(
            new SpaceWmsInventoryQuery(Context, []));
        var tasks = await simulator.QueryTasksAsync(
            new SpaceWmsTaskQuery(Context, []));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var abc = await simulator.QueryAbcAsync(
            new SpaceWmsAbcQuery(Context, today.AddDays(-90), today));

        Assert.Equal(10, result.BatchCount);
        Assert.Equal(10_000, result.LocationCount);
        Assert.Equal(5_000, result.InventoryCount);
        Assert.Equal(100, result.PickTaskCount);
        Assert.Equal(200, result.PickTaskLineCount);
        Assert.Equal(dataset.ContentSha256, result.ContentSha256);
        Assert.True(locations.Source.IsSimulated);
        Assert.Equal(10_000, locations.Items.Count);
        Assert.Equal(5_000, inventory.Items.Count);
        Assert.Equal(200, tasks.Items.Count);
        Assert.Equal(100, abc.Items.Count);
        Assert.True(abc.Source.IsSimulated);
        Assert.Equal(
            100,
            tasks.Items.Select(item => item.TaskId).Distinct().Count());
        Assert.Equal(
            dataset.Locations
                .Select(location => location.Code)
                .OrderBy(code => code, StringComparer.Ordinal),
            locations.Items.Select(location => location.LocationCode));
    }

    [Fact]
    public async Task Rejects_a_context_for_a_different_warehouse()
    {
        var simulator = new StandardSpaceWmsSimulator();
        var loader = new StandardSpaceWarehouseDatasetLoader(simulator);
        var dataset = SpaceStandardWarehouseDatasetGenerator.Generate();
        var context = Context with { WarehouseCode = "OTHER-WAREHOUSE" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => loader.LoadAsync(context, dataset));

        Assert.Equal(
            "SPACE_STANDARD_DATASET_WAREHOUSE_MISMATCH",
            exception.Message);
    }

    [Fact]
    public async Task Reload_rebuilds_same_catalog_and_state_hashes()
    {
        var simulator = new StandardSpaceWmsSimulator();
        var loader = new StandardSpaceWarehouseDatasetLoader(simulator);
        var dataset = SpaceStandardWarehouseDatasetGenerator.Generate();

        await loader.LoadAsync(Context, dataset);
        var first = await simulator.QueryLocationsAsync(
            new SpaceWmsLocationQuery(Context, []));
        await loader.LoadAsync(Context, dataset);
        var second = await simulator.QueryLocationsAsync(
            new SpaceWmsLocationQuery(Context, []));

        Assert.Equal(first.Items, second.Items);
    }

    [Fact]
    public async Task Loaded_inventory_and_tasks_produce_blocking_references()
    {
        var simulator = new StandardSpaceWmsSimulator();
        var loader = new StandardSpaceWarehouseDatasetLoader(simulator);
        var dataset = SpaceStandardWarehouseDatasetGenerator.Generate();
        await loader.LoadAsync(Context, dataset);
        var logicalIds = dataset.Inventory
            .Take(10)
            .Select(item => item.LogicalId)
            .ToArray();

        var references = await simulator.GetBlockingReferencesAsync(
            new SpaceWmsBlockingReferencesRequest(Context, logicalIds));

        Assert.True(references.Source.IsSimulated);
        Assert.Contains(
            references.Items,
            item => item.Kind == SpaceWmsBlockingReferenceKind.Inventory);
        Assert.Contains(
            references.Items,
            item => item.Kind == SpaceWmsBlockingReferenceKind.ActiveTask);
    }

    [Fact]
    public async Task Timeout_fault_case_is_reproducible_after_dataset_load()
    {
        var simulator = new StandardSpaceWmsSimulator();
        var loader = new StandardSpaceWarehouseDatasetLoader(simulator);
        var dataset = SpaceStandardWarehouseDatasetGenerator.Generate();
        await loader.LoadAsync(Context, dataset);
        simulator.ConfigureFault(
            Context,
            new SpaceWmsSimulatorFaultProfile(
                SpaceWmsSimulatorFaultMode.Timeout,
                Delay: TimeSpan.Zero,
                ErrorCode: "SPACE_WMS_RETRYABLE"));

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => simulator.QueryInventoryAsync(
                new SpaceWmsInventoryQuery(Context, [])));

        Assert.Equal("SPACE_WMS_RETRYABLE", exception.Message);
    }
}
