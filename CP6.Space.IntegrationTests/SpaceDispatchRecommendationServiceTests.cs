using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceDispatchRecommendationServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 18, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('c', 64);

    [Fact]
    public async Task Generation_persists_explained_immutable_assignments()
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
            SpaceDispatchRecommendationService.DefinitionVersion,
            value.DefinitionVersion);
        Assert.Equal("AssignmentsGenerated", value.Outcome);
        Assert.Equal(2, value.ExaminedTaskCount);
        Assert.Equal(2, value.EligibleTaskCount);
        Assert.Equal(2, value.ExaminedPersonCount);
        Assert.Equal(2, value.EligiblePersonCount);
        Assert.Equal(2, value.MatchableAssignmentCount);
        Assert.Equal(2, value.ReturnedAssignmentCount);
        Assert.Equal("TASK-1", value.Assignments[0].TaskId);
        Assert.Equal("AQIDBA==", value.Assignments[0].TaskRowVersion);
        Assert.Contains(
            "RECOMMENDATION_DOES_NOT_APPROVE_ASSIGN_CLAIM_START_OR_WRITE_TASKS",
            value.Limitations);
        Assert.Equal(
            1,
            await fixture.Context.DispatchRecommendations.CountAsync());
        Assert.Equal(1, fixture.Runtime.DispatchCalls);
        Assert.Single(fixture.Access.Calls);
        Assert.False(fixture.Access.Calls[0].Write);

        var loaded = await fixture.Service.GetAsync(fixture.SiteId, id);
        Assert.Equal(value.RecommendationId, loaded.RecommendationId);
        Assert.Equal(value.Request, loaded.Request);
        Assert.Equal(
            value.Assignments.Select(item => item.TaskId),
            loaded.Assignments.Select(item => item.TaskId));
        Assert.Equal(value.Limitations, loaded.Limitations);
    }

    [Fact]
    public async Task Same_canonical_request_replays_but_changed_request_conflicts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        await fixture.Service.GenerateAsync(
            fixture.SiteId,
            id,
            Request() with { TaskType = " pick " });

        var duplicate = await fixture.Service.GenerateAsync(
            fixture.SiteId,
            id,
            Request());

        Assert.Equal("Duplicate", duplicate.Outcome);
        Assert.Equal(1, fixture.Runtime.DispatchCalls);
        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.GenerateAsync(
                fixture.SiteId,
                id,
                Request() with { MaximumAssignments = 1 }));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal(
            SpaceErrorCodes.DispatchRecommendationConflict,
            error.Code);
    }

    [Fact]
    public async Task External_and_unavailable_sources_fail_closed_without_persistence()
    {
        await using var external = await Fixture.CreateAsync(external: true);
        var denied = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            external.Service.GenerateAsync(
                external.SiteId,
                Guid.NewGuid(),
                Request()));
        Assert.Equal(403, denied.StatusCode);
        Assert.Equal(
            SpaceErrorCodes.DispatchRecommendationsInternalOnly,
            denied.Code);
        Assert.Empty(external.Access.Calls);
        Assert.Equal(0, external.Runtime.DispatchCalls);

        await using var unavailable = await Fixture.CreateAsync();
        unavailable.Runtime.Tasks = unavailable.Runtime.Tasks with
        {
            Source = unavailable.Runtime.Tasks.Source with
            {
                Kind = "Unavailable",
                IsAvailable = false,
            },
            Items = [],
        };
        var failed = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            unavailable.Service.GenerateAsync(
                unavailable.SiteId,
                Guid.NewGuid(),
                Request()));
        Assert.Equal(503, failed.StatusCode);
        Assert.Equal(SpaceErrorCodes.WmsUnavailable, failed.Code);
        Assert.Empty(await unavailable.Context.DispatchRecommendations
            .ToListAsync());
    }

    [Fact]
    public async Task Persisted_recommendations_cannot_be_modified_or_deleted()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.GenerateAsync(
            fixture.SiteId,
            Guid.NewGuid(),
            Request());
        var entity = await fixture.Context.DispatchRecommendations.SingleAsync();
        fixture.Context.Entry(entity).Property(value => value.Outcome)
            .CurrentValue = "NoAssignment";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Context.SaveChangesAsync());

        Assert.Equal("Dispatch recommendations are immutable.", error.Message);
    }

    private static GenerateSpaceDispatchRecommendationRequest Request() =>
        new(
            TaskType: "PICK",
            MaximumTravelDistanceMeters: 10,
            MaximumAssignments: 10);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SpaceContext context,
            SpaceDispatchRecommendationService service,
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
        public SpaceDispatchRecommendationService Service { get; }
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
                Tasks = Tasks(seeded),
            };
            var access = new RecordingAccess();
            var service = new SpaceDispatchRecommendationService(
                context,
                runtime,
                execution,
                clock,
                access,
                new SpacePersonnelRuntimeOptions(),
                new SpaceDispatchRecommendationEngine());
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
            "Published dispatch model");
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
        rack.ConfigureGeometry(0, 0, 0, 0, 2_000, 1_000, 3_000);
        var level = SpaceRackLevelRevision.Create(
            tenantId,
            version.Id,
            Guid.NewGuid(),
            rackId,
            levelNo: 1,
            bottomZ: 0,
            clearHeight: 1_000,
            binCount: 2,
            depthCount: 1,
            cellWidth: 1_000,
            cellDepth: 1_000);
        var locations = Enumerable.Range(1, 2)
            .Select(index => SpaceLocationRevision.Create(
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
                1_000))
            .ToArray();
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

        var states = locations.Select((location, index) => PersonnelState(
            tenantId,
            siteId,
            floorId,
            location.LogicalId,
            $"PERSON-{index + 1}",
            index + 1)).ToArray();
        context.AddRange(states);
        await context.SaveChangesAsync();
        return new Seeded(
            siteId,
            version.Id,
            floorId,
            zoneId,
            rackId,
            locations.Select(value => value.LogicalId).ToArray());
    }

    private static SpacePersonnelCurrentState PersonnelState(
        Guid tenantId,
        Guid siteId,
        Guid floorId,
        Guid locationId,
        string personId,
        long sequence)
    {
        var position = SpacePersonnelEvent.Create(
            tenantId,
            siteId,
            "PDA-01",
            SpacePersonnelSourceKind.Real,
            $"POSITION-{sequence}",
            personId,
            null,
            SpacePersonnelEventKind.PositionObserved,
            null,
            floorId,
            locationId,
            null,
            null,
            null,
            null,
            sequence,
            Now.AddMinutes(-2),
            Now.AddMinutes(-2),
            Hash);
        var state = SpacePersonnelCurrentState.Create(position);
        state.Apply(SpacePersonnelEvent.Create(
            tenantId,
            siteId,
            "PDA-01",
            SpacePersonnelSourceKind.Real,
            $"WORK-{sequence}",
            personId,
            null,
            SpacePersonnelEventKind.WorkStateChanged,
            SpacePersonnelWorkState.Idle,
            null,
            null,
            null,
            null,
            null,
            null,
            sequence,
            Now.AddMinutes(-1),
            Now.AddMinutes(-1),
            Hash));
        return state;
    }

    private static SpaceWmsRuntimeDispatchTaskResponse Tasks(Seeded value) =>
        new(
            value.SiteId,
            value.VersionId,
            "WH-01",
            Source(),
            value.LocationIds.Select((locationId, index) =>
                new SpaceWmsRuntimeDispatchTaskItemDto(
                    TaskId: $"TASK-{index + 1}",
                    TaskType: "Pick",
                    Status: "Pending",
                    AssignedTo: null,
                    Priority: index + 1,
                    ContractVersion: 1,
                    ExecutionVersion: 0,
                    RowVersion: "AQIDBA==",
                    TargetLocationRole: "Source",
                    WmsLocationCode: $"F1-L0{index + 1}",
                    TargetLocationResolved: true,
                    LocationLogicalId: locationId,
                    WmsLogicalId: Guid.NewGuid(),
                    SpaceLocationCode: $"F1-L0{index + 1}",
                    CodeMatches: true,
                    FloorLogicalId: value.FloorId,
                    FloorCode: "F1",
                    FloorName: "Floor 1",
                    FloorLevel: 1,
                    ZoneLogicalId: value.ZoneId,
                    ZoneCode: "Z1",
                    RackLogicalId: value.RackId,
                    RackCode: "R1",
                    AnchorXMillimeters: index * 1_000 + 500,
                    AnchorYMillimeters: 500,
                    AnchorZMillimeters: 500,
                    Quantity: 1,
                    MaterialNumber: "SKU-1"))
                .ToArray());

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
        public required SpaceWmsRuntimeDispatchTaskResponse Tasks { get; set; }
        public int DispatchCalls { get; private set; }

        public Task<SpaceWmsRuntimeDispatchTaskResponse> QueryDispatchTasksAsync(
            Guid siteId,
            CancellationToken cancellationToken = default)
        {
            DispatchCalls++;
            return Task.FromResult(Tasks);
        }

        public Task<SpaceWmsRuntimeInventoryResponse> QueryInventoryAsync(
            Guid siteId,
            IReadOnlyCollection<Guid>? locationLogicalIds = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SpaceWmsRuntimeTaskResponse> QueryTasksAsync(
            Guid siteId,
            IReadOnlyCollection<Guid>? locationLogicalIds = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
