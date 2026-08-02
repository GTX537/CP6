using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpacePutawayRecommendationServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 18, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('b', 64);

    [Fact]
    public async Task Generation_persists_explained_immutable_candidates()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();

        var response = await fixture.Service.GenerateAsync(
            fixture.SiteId,
            id,
            Request());

        Assert.Equal("Generated", response.Outcome);
        var value = response.Recommendation;
        Assert.Equal(id, value.RecommendationId);
        Assert.Equal(
            SpacePutawayRecommendationService.DefinitionVersion,
            value.DefinitionVersion);
        Assert.Equal("CandidatesGenerated", value.Outcome);
        Assert.Equal(3, value.ExaminedLocationCount);
        Assert.Equal(2, value.EligibleCandidateCount);
        Assert.Equal(2, value.ReturnedCandidateCount);
        Assert.Equal(
            "ConsolidateExactStockIdentity",
            value.Candidates[0].Category);
        Assert.Equal("F1-L01", value.Candidates[0].SpaceLocationCode);
        Assert.Equal("F1-L02", value.Candidates[1].SpaceLocationCode);
        Assert.Equal(1, value.Exclusions.ActiveTask);
        var exclusion = Assert.Single(value.ExclusionSamples);
        Assert.Equal("F1-L03", exclusion.SpaceLocationCode);
        Assert.Equal("ACTIVE_TASK_AT_OBSERVATION", exclusion.Reason);
        Assert.Contains(
            "RECOMMENDATION_DOES_NOT_RESERVE_MOVE_OR_WRITE_INVENTORY",
            value.Limitations);
        Assert.Equal(
            1,
            await fixture.Context.PutawayRecommendations.CountAsync());
        Assert.Equal(1, fixture.Runtime.InventoryCalls);
        Assert.Equal(1, fixture.Runtime.TaskCalls);
        Assert.Single(fixture.Access.Calls);
        Assert.False(fixture.Access.Calls[0].Write);

        var loaded = await fixture.Service.GetAsync(fixture.SiteId, id);
        Assert.Equal(value.RecommendationId, loaded.RecommendationId);
        Assert.Equal(value.Request, loaded.Request);
        Assert.Equal(
            value.Candidates.Select(item => item.SpaceLocationCode),
            loaded.Candidates.Select(item => item.SpaceLocationCode));
        Assert.Equal(value.ExclusionSamples, loaded.ExclusionSamples);
        Assert.Equal(value.Limitations, loaded.Limitations);
    }

    [Fact]
    public async Task Canonically_same_request_replays_but_changed_request_conflicts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        await fixture.Service.GenerateAsync(
            fixture.SiteId,
            id,
            Request() with
            {
                MaterialNumber = " sku-1 ",
                OwnerId = " owner-1 ",
                LotNumber = " lot-1 ",
                InboundQuantity = 5.0m,
            });

        var duplicate = await fixture.Service.GenerateAsync(
            fixture.SiteId,
            id,
            Request() with { InboundQuantity = 5.00m });

        Assert.Equal("Duplicate", duplicate.Outcome);
        Assert.Equal(1, fixture.Runtime.InventoryCalls);
        var error = await Assert.ThrowsAsync<SpaceProblemException>(
            () => fixture.Service.GenerateAsync(
                fixture.SiteId,
                id,
                Request() with { InboundQuantity = 99 }));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.PutawayRecommendationConflict, error.Code);
    }

    [Fact]
    public async Task External_principal_is_rejected_before_access_or_runtime()
    {
        await using var fixture = await Fixture.CreateAsync(external: true);

        var error = await Assert.ThrowsAsync<SpaceProblemException>(
            () => fixture.Service.GenerateAsync(
                fixture.SiteId,
                Guid.NewGuid(),
                Request()));

        Assert.Equal(403, error.StatusCode);
        Assert.Equal(
            SpaceErrorCodes.PutawayRecommendationsInternalOnly,
            error.Code);
        Assert.Empty(fixture.Access.Calls);
        Assert.Equal(0, fixture.Runtime.InventoryCalls);
    }

    [Fact]
    public async Task Unavailable_task_source_fails_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Runtime.Tasks = fixture.Runtime.Tasks with
        {
            Source = fixture.Runtime.Tasks.Source with
            {
                Kind = "Unavailable",
                IsAvailable = false,
            },
            Items = [],
        };

        var error = await Assert.ThrowsAsync<SpaceProblemException>(
            () => fixture.Service.GenerateAsync(
                fixture.SiteId,
                Guid.NewGuid(),
                Request()));

        Assert.Equal(503, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.WmsUnavailable, error.Code);
        Assert.Equal(
            0,
            await fixture.Context.PutawayRecommendations.CountAsync());
    }

    [Fact]
    public async Task Runtime_source_change_fails_closed_without_persistence()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Runtime.Tasks = fixture.Runtime.Tasks with
        {
            Source = fixture.Runtime.Tasks.Source with
            {
                AdapterId = "OTHER-ADAPTER",
            },
        };

        var error = await Assert.ThrowsAsync<SpaceProblemException>(
            () => fixture.Service.GenerateAsync(
                fixture.SiteId,
                Guid.NewGuid(),
                Request()));

        Assert.Equal(502, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.WmsRuntimeContractViolation, error.Code);
        Assert.Equal(
            0,
            await fixture.Context.PutawayRecommendations.CountAsync());
    }

    [Fact]
    public async Task Persisted_recommendations_cannot_be_modified_or_deleted()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.GenerateAsync(
            fixture.SiteId,
            Guid.NewGuid(),
            Request());
        var entity = await fixture.Context.PutawayRecommendations.SingleAsync();
        fixture.Context.Entry(entity).Property(value => value.Outcome)
            .CurrentValue = "NoCandidate";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Context.SaveChangesAsync());

        Assert.Equal("Putaway recommendations are immutable.", error.Message);
    }

    private static GenerateSpacePutawayRecommendationRequest Request() =>
        new(
            "SKU-1",
            "OWNER-1",
            "LOT-1",
            5,
            RequiredWidthMillimeters: 900,
            RequiredHeightMillimeters: 900,
            RequiredDepthMillimeters: 900,
            RequiredMaxLoad: 100,
            MaximumCandidates: 10);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SpaceContext context,
            SpacePutawayRecommendationService service,
            RecordingRuntime runtime,
            RecordingAccess access,
            Seeded seeded)
        {
            Context = context;
            Service = service;
            Runtime = runtime;
            Access = access;
            SiteId = seeded.SiteId;
        }

        public SpaceContext Context { get; }
        public SpacePutawayRecommendationService Service { get; }
        public RecordingRuntime Runtime { get; }
        public RecordingAccess Access { get; }
        public Guid SiteId { get; }

        public static async Task<Fixture> CreateAsync(bool external = false)
        {
            var execution = new TestExecution(
                Guid.NewGuid(),
                Guid.NewGuid(),
                external);
            var clock = new TestClock();
            var context = new SpaceContext(
                new DbContextOptionsBuilder<SpaceContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                    .Options,
                execution,
                clock);
            var seeded = await SeedAsync(context);
            var runtime = new RecordingRuntime
            {
                Inventory = Inventory(seeded),
                Tasks = Tasks(seeded),
            };
            var access = new RecordingAccess();
            var service = new SpacePutawayRecommendationService(
                context,
                runtime,
                execution,
                clock,
                access,
                new SpacePutawayRecommendationEngine());
            return new Fixture(context, service, runtime, access, seeded);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private static async Task<Seeded> SeedAsync(SpaceContext context)
    {
        var tenantId = context.CurrentTenantId;
        var siteId = Guid.NewGuid();
        var model = SpaceModel.Create(tenantId, siteId);
        var version = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "Published putaway model");
        var floorId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        var floor = SpaceFloorRevision.Create(
            tenantId,
            version.Id,
            floorId,
            siteId,
            1,
            "F1",
            "Floor 1");
        var zone = SpaceZoneRevision.Create(
            tenantId,
            version.Id,
            zoneId,
            floorId,
            "Z1",
            1);
        var rack = SpaceRackRevision.Create(
            tenantId,
            version.Id,
            rackId,
            floorId,
            zoneId,
            "R1");
        rack.ConfigureGeometry(0, 0, 0, 0, 6_000, 1_000, 3_000);
        var locations = Enumerable.Range(1, 3)
            .Select(index =>
                SpaceLocationRevision.Create(
                    tenantId,
                    version.Id,
                    Guid.NewGuid(),
                    floorId,
                    rackId,
                    $"F1-L0{index}",
                    index,
                    1,
                    1,
                    1_000,
                    1_000,
                    1_000,
                    200))
            .ToArray();
        context.AddRange(model, version, floor, zone, rack);
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
        return new Seeded(
            siteId,
            version.Id,
            floorId,
            zoneId,
            rackId,
            locations.Select(value => value.LogicalId).ToArray());
    }

    private static SpaceWmsRuntimeInventoryResponse Inventory(Seeded value) =>
        new(
            value.SiteId,
            value.VersionId,
            "WH-01",
            Source(),
            [InventoryItem(value, 0, "F1-L01", 10)]);

    private static SpaceWmsRuntimeInventoryItemDto InventoryItem(
        Seeded value,
        int index,
        string code,
        decimal quantity) =>
        new(
            value.LocationIds[index],
            Guid.NewGuid(),
            code,
            code,
            true,
            value.FloorId,
            "F1",
            "Floor 1",
            1,
            quantity,
            2,
            "SKU-1",
            "LOT-1",
            null,
            "OWNER-1");

    private static SpaceWmsRuntimeTaskResponse Tasks(Seeded value) =>
        new(
            value.SiteId,
            value.VersionId,
            "WH-01",
            Source(),
            [
                new SpaceWmsRuntimeTaskItemDto(
                    "TASK-1",
                    "Pick",
                    "Active",
                    1,
                    value.LocationIds[2],
                    Guid.NewGuid(),
                    "F1-L03",
                    "F1-L03",
                    true,
                    value.FloorId,
                    "F1",
                    "Floor 1",
                    1,
                    value.ZoneId,
                    "Z1",
                    value.RackId,
                    "R1",
                    2_500,
                    500,
                    500,
                    1,
                    "SKU-2"),
            ]);

    private static SpaceWmsRuntimeSourceDto Source() =>
        new(
            "Real",
            "cp6-wms-v1",
            "CP6_WMS",
            new DateTimeOffset(Now.AddSeconds(-10)),
            new DateTimeOffset(Now.AddSeconds(-8)),
            2_000,
            0,
            false,
            true);

    private sealed record Seeded(
        Guid SiteId,
        Guid VersionId,
        Guid FloorId,
        Guid ZoneId,
        Guid RackId,
        Guid[] LocationIds);

    private sealed record TestExecution(
        Guid TenantId,
        Guid ActorId,
        bool IsExternal) : ISpaceExecutionContext;

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class RecordingAccess : ISpaceDesignAccessEvaluator
    {
        public List<(Guid SiteId, bool Write)> Calls { get; } = [];

        public void EnsureSiteAccess(Guid siteId, bool write) =>
            Calls.Add((siteId, write));
    }

    private sealed class RecordingRuntime : ISpaceWmsRuntimeService
    {
        public required SpaceWmsRuntimeInventoryResponse Inventory { get; set; }
        public required SpaceWmsRuntimeTaskResponse Tasks { get; set; }
        public int InventoryCalls { get; private set; }
        public int TaskCalls { get; private set; }

        public Task<SpaceWmsRuntimeInventoryResponse> QueryInventoryAsync(
            Guid siteId,
            IReadOnlyCollection<Guid>? locationLogicalIds = null,
            CancellationToken cancellationToken = default)
        {
            InventoryCalls++;
            return Task.FromResult(Inventory);
        }

        public Task<SpaceWmsRuntimeTaskResponse> QueryTasksAsync(
            Guid siteId,
            IReadOnlyCollection<Guid>? locationLogicalIds = null,
            CancellationToken cancellationToken = default)
        {
            TaskCalls++;
            return Task.FromResult(Tasks);
        }

        public Task<SpaceWmsRuntimeInventoryLocateResponse> LocateInventoryAsync(
            Guid siteId,
            SpaceWmsInventoryLocateCriteria criteria,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SpaceWmsRuntimeTaskPathResponse> GetTaskPathAsync(
            Guid siteId,
            string taskId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SpaceWmsRuntimeWarehouseOverviewResponse>
            GetWarehouseOverviewAsync(
                Guid siteId,
                int abcWindowDays = 90,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
