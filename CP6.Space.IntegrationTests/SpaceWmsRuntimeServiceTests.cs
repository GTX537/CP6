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
    public async Task Transport_failure_is_retryable_but_cancellation_is_preserved()
    {
        await using var fixture = await RuntimeFixture.CreateAsync("L-001");
        fixture.Source.QueryException = new TimeoutException("simulated timeout");

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.QueryTasksAsync(fixture.SiteId));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.WmsUnavailable, error.Code);
        Assert.True(error.Retryable);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.QueryTasksAsync(
                fixture.SiteId,
                cancellationToken: cancellation.Token));
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
        Assert.True(response.Source.IsAvailable);
        Assert.Empty(fixture.Source.InventoryBatchSizes);
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
            RecordingRuntimeSource source,
            SpaceWmsRuntimeService service,
            Guid siteId,
            IReadOnlyList<Guid> locationIds)
        {
            Context = context;
            Source = source;
            Service = service;
            SiteId = siteId;
            LocationIds = locationIds;
        }

        public SpaceContext Context { get; }
        public RecordingRuntimeSource Source { get; }
        public SpaceWmsRuntimeService Service { get; }
        public Guid SiteId { get; }
        public IReadOnlyList<Guid> LocationIds { get; }

        public static async Task<RuntimeFixture> CreateAsync(
            params string[] locationCodes)
        {
            var execution = Execution();
            var clock = new TestClock();
            var context = NewContext(execution, clock);
            try
            {
                var seeded = await SeedPublishedAsync(context, locationCodes);
                var source = new RecordingRuntimeSource();
                var service = CreateService(
                    context,
                    execution,
                    clock,
                    seeded.SiteId,
                    source);
                return new RuntimeFixture(
                    context,
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

    private sealed record SeedLocation(Guid LogicalId, string Code);

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
        IReadOnlyList<SeedLocation> seededLocations)
    {
        var locationCodes = seededLocations.Select(value => value.Code).ToArray();
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
        var locationIds = seededLocations.Select(value => value.LogicalId).ToArray();
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
        public bool ReturnNullInventoryItems { get; set; }
        public bool ReturnNullTaskItems { get; set; }
        public SpaceWmsInventoryItem? InventoryOverrideItem { get; set; }
        public SpaceWmsTaskItem? TaskOverrideItem { get; set; }
        public List<int> InventoryBatchSizes { get; } = [];
        public List<int> TaskBatchSizes { get; } = [];
        public IReadOnlyList<SpaceWmsInventoryItem> InventoryItems { get; init; } = [];
        public IReadOnlyList<SpaceWmsTaskItem> TaskItems { get; init; } = [];

        public void ResetCalls()
        {
            _callIndex = 0;
            InventoryBatchSizes.Clear();
            TaskBatchSizes.Clear();
        }

        public Task<SpaceWmsInventoryResult> QueryInventoryAsync(
            SpaceWmsInventoryQuery request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (QueryException is not null)
                throw QueryException;
            InventoryBatchSizes.Add(request.LogicalIds.Count);
            var requested = request.LogicalIds.ToHashSet();
            IReadOnlyList<SpaceWmsInventoryItem>? items = ReturnNullInventoryItems
                ? null
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
                            .Where(value => requested.Contains(value.LogicalId))
                            .ToArray();
            return Task.FromResult(new SpaceWmsInventoryResult(
                NextSource(),
                items!));
        }

        public Task<SpaceWmsTaskResult> QueryTasksAsync(
            SpaceWmsTaskQuery request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (QueryException is not null)
                throw QueryException;
            TaskBatchSizes.Add(request.LogicalIds.Count);
            var requested = request.LogicalIds.ToHashSet();
            IReadOnlyList<SpaceWmsTaskItem>? items = ReturnNullTaskItems
                ? null
                : TaskOverrideItem is not null
                    ? [TaskOverrideItem]
                    : TaskItems.Where(value => requested.Contains(value.LogicalId))
                        .ToArray();
            return Task.FromResult(new SpaceWmsTaskResult(
                NextSource(),
                items!));
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
    }
}
