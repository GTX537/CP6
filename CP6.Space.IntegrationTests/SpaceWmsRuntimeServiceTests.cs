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

        Assert.Equal("Simulated", inventory.Source.Kind);
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
    }

    [Fact]
    public async Task Inventory_and_task_queries_use_500_item_chunks()
    {
        var execution = Execution();
        var clock = new TestClock();
        await using var context = NewContext(execution, clock);
        var seeded = await SeedPublishedAsync(
            context,
            Enumerable.Range(1, 1_001).Select(value => $"L-{value:0000}").ToArray());
        var source = new RecordingRuntimeSource();
        var service = CreateService(context, execution, clock, seeded.SiteId, source);

        await service.QueryInventoryAsync(seeded.SiteId);
        await service.QueryTasksAsync(seeded.SiteId);

        Assert.Equal([500, 500, 1], source.InventoryBatchSizes);
        Assert.Equal([500, 500, 1], source.TaskBatchSizes);
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

    private static async Task<SeededPublished> SeedPublishedAsync(
        SpaceContext context,
        params string[] locationCodes)
    {
        var tenantId = context.CurrentTenantId;
        var siteId = Guid.NewGuid();
        var model = SpaceModel.Create(tenantId, siteId);
        var version = SpaceModelVersion.CreateDraft(
            tenantId, model.Id, 1, "Published runtime");
        var floorLogicalId = Guid.NewGuid();
        var zoneLogicalId = Guid.NewGuid();
        var rackLogicalId = Guid.NewGuid();
        var floor = SpaceFloorRevision.Create(
            tenantId, version.Id, floorLogicalId, siteId, 1,
            "F1", "Floor 1", height: 5_000);
        var zone = SpaceZoneRevision.Create(
            tenantId, version.Id, zoneLogicalId, floorLogicalId,
            "STORAGE", zoneType: 1);
        var rack = SpaceRackRevision.Create(
            tenantId, version.Id, rackLogicalId, floorLogicalId,
            zoneLogicalId, "RACK-01");
        rack.ConfigureGeometry(
            0, 0, 0, 0,
            width: Math.Max(1, locationCodes.Length) * 1_000,
            depth: 1_100,
            height: 4_000);
        var level = SpaceRackLevelRevision.Create(
            tenantId, version.Id, Guid.NewGuid(), rackLogicalId,
            levelNo: 1,
            bottomZ: 0,
            clearHeight: 1_200,
            binCount: Math.Max(1, locationCodes.Length),
            depthCount: 1,
            cellWidth: 1_000,
            cellDepth: 1_100);
        var locationIds = locationCodes.Select(_ => Guid.NewGuid()).ToArray();
        var locations = locationIds.Select((logicalId, index) =>
            SpaceLocationRevision.Create(
                tenantId, version.Id, logicalId, floorLogicalId,
                rackLogicalId, locationCodes[index],
                columnNo: index + 1,
                levelNo: 1,
                depthNo: 1,
                width: 1_000,
                height: 1_200,
                depth: 1_100)).ToArray();

        context.AddRange(model, version, floor, zone, rack, level);
        context.AddRange(locations);
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
        public DateTime UtcNow => Now;
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
        public string RuntimeAdapterId => "recording-wms-v1";
        public string RuntimeDataSourceId => "RECORDING_WMS";
        public SpaceWmsDataSourceKind RuntimeDataSourceKind =>
            SpaceWmsDataSourceKind.Real;
        public List<int> InventoryBatchSizes { get; } = [];
        public List<int> TaskBatchSizes { get; } = [];
        public IReadOnlyList<SpaceWmsInventoryItem> InventoryItems { get; init; } = [];
        public IReadOnlyList<SpaceWmsTaskItem> TaskItems { get; init; } = [];

        public Task<SpaceWmsInventoryResult> QueryInventoryAsync(
            SpaceWmsInventoryQuery request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            InventoryBatchSizes.Add(request.LogicalIds.Count);
            var requested = request.LogicalIds.ToHashSet();
            return Task.FromResult(new SpaceWmsInventoryResult(
                Source(),
                InventoryItems.Where(value => requested.Contains(value.LogicalId))
                    .ToArray()));
        }

        public Task<SpaceWmsTaskResult> QueryTasksAsync(
            SpaceWmsTaskQuery request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            TaskBatchSizes.Add(request.LogicalIds.Count);
            var requested = request.LogicalIds.ToHashSet();
            return Task.FromResult(new SpaceWmsTaskResult(
                Source(),
                TaskItems.Where(value => requested.Contains(value.LogicalId))
                    .ToArray()));
        }

        private static SpaceWmsSourceMetadata Source() =>
            new(
                SpaceWmsDataSourceKind.Real,
                "RECORDING_WMS",
                new DateTimeOffset(Now));
    }
}
