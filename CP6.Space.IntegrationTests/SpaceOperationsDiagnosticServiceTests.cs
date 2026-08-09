using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceOperationsDiagnosticServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 16, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('a', 64);

    [Fact]
    public async Task Diagnosis_uses_real_current_model_evidence_and_separates_capacity()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.AddPosition("REAL", SpacePersonnelSourceKind.Real, "P1", -20, 0, fixture.LocationIds[0]);
        fixture.AddPosition("REAL", SpacePersonnelSourceKind.Real, "P1", -19, 10_000, fixture.LocationIds[1]);
        fixture.AddPosition("REAL", SpacePersonnelSourceKind.Real, "P1", -18, 0, fixture.LocationIds[0]);
        fixture.AddPosition("REAL", SpacePersonnelSourceKind.Real, "P1", -10, 0, fixture.LocationIds[0]);
        fixture.AddPosition("REAL", SpacePersonnelSourceKind.Real, "P1", -5, 0, fixture.LocationIds[0]);
        fixture.AddPosition("REAL", SpacePersonnelSourceKind.Real, "P2", -9.5, 0, fixture.LocationIds[0]);
        fixture.AddPosition("REAL", SpacePersonnelSourceKind.Real, "P2", -4.5, 0, fixture.LocationIds[0]);
        fixture.AddPosition("SIM", SpacePersonnelSourceKind.Simulated, "P9", -3, 0, fixture.LocationIds[0]);
        fixture.AddPosition("REAL", SpacePersonnelSourceKind.Real, "P3", -2, 0, Guid.NewGuid());
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.GetAsync(
            fixture.SiteId,
            new DateTimeOffset(Now.AddHours(-1)),
            new DateTimeOffset(Now));

        Assert.Equal(SpaceOperationsDiagnosticService.DefinitionVersion, result.DefinitionVersion);
        Assert.Equal(9, result.PersonnelSource.EvidenceEventCount);
        Assert.Equal(7, result.PersonnelSource.EligibleRealEventCount);
        Assert.Equal(1, result.PersonnelSource.ExcludedSimulatedEventCount);
        Assert.Equal(1, result.PersonnelSource.ExcludedOutsidePublishedModelEventCount);
        Assert.Equal(2, result.PersonnelSource.PersonCount);
        Assert.Equal(20m, result.Path.ObservedDistanceMeters);
        Assert.Equal(1, result.Path.BacktrackCount);
        Assert.Equal(2, result.Dwell.EpisodeCount);
        Assert.Equal(600, result.Dwell.TotalDwellSeconds);
        Assert.Equal(270, result.Congestion.ConcurrentSeconds);
        Assert.Equal(2, result.Congestion.PeakConcurrentPeople);

        Assert.True(result.Capacity.IsAvailable);
        Assert.Equal(2, result.Capacity.LocationCount);
        Assert.Equal(1, result.Capacity.OccupiedLocationCount);
        Assert.Equal(50m, result.Capacity.LocationOccupancyPercent);
        Assert.Equal("Normal", result.Capacity.LocationOccupancyPressure);
        Assert.Null(result.Capacity.CapacityUtilizationPercent);
        Assert.Equal("Unavailable", result.Capacity.CapacityUtilizationStatus);
        Assert.Equal(
            SpaceOperationsDiagnosticService.CapacityUnavailableReason,
            result.Capacity.CapacityUtilizationReason);
        Assert.Contains("SIMULATED_PERSONNEL_EVENTS_EXCLUDED", result.Limitations);
        Assert.Contains(
            "OUTSIDE_CURRENT_PUBLISHED_MODEL_EVENTS_EXCLUDED",
            result.Limitations);
        Assert.Equal(1, fixture.Runtime.InventoryCalls);
        Assert.Single(fixture.Access.Calls);
    }

    [Fact]
    public async Task Wms_unavailable_keeps_personnel_diagnosis_and_marks_occupancy_unknown()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.AddPosition("REAL", SpacePersonnelSourceKind.Real, "P1", -10, 0, fixture.LocationIds[0]);
        fixture.AddPosition("REAL", SpacePersonnelSourceKind.Real, "P1", -5, 0, fixture.LocationIds[0]);
        await fixture.Context.SaveChangesAsync();
        fixture.Runtime.ThrowUnavailable = true;

        var result = await fixture.Service.GetAsync(
            fixture.SiteId,
            new DateTimeOffset(Now.AddHours(-1)),
            new DateTimeOffset(Now));

        Assert.Equal(1, result.Path.KnownDistanceSegmentCount);
        Assert.Equal(1, result.Dwell.EpisodeCount);
        Assert.False(result.Capacity.IsAvailable);
        Assert.Null(result.Capacity.Source);
        Assert.Equal(2, result.Capacity.LocationCount);
        Assert.Null(result.Capacity.OccupiedLocationCount);
        Assert.Null(result.Capacity.LocationOccupancyPercent);
        Assert.Equal("Unavailable", result.Capacity.LocationOccupancyPressure);
        Assert.Contains("WMS_OCCUPANCY_SOURCE_UNAVAILABLE", result.Limitations);
    }

    [Fact]
    public async Task Mismatched_runtime_scope_fails_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Runtime.Inventory = fixture.Runtime.Inventory with
        {
            PublishedVersionId = Guid.NewGuid(),
        };

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.GetAsync(
                fixture.SiteId,
                new DateTimeOffset(Now.AddHours(-1)),
                new DateTimeOffset(Now)));

        Assert.Equal(502, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.WmsRuntimeContractViolation, error.Code);
    }

    [Fact]
    public async Task External_principal_is_rejected_before_access_and_runtime()
    {
        await using var fixture = await Fixture.CreateAsync(isExternal: true);

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.GetAsync(
                fixture.SiteId,
                new DateTimeOffset(Now.AddHours(-1)),
                new DateTimeOffset(Now)));

        Assert.Equal(403, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.OperationsDiagnosticsInternalOnly, error.Code);
        Assert.Empty(fixture.Access.Calls);
        Assert.Equal(0, fixture.Runtime.InventoryCalls);
    }

    [Theory]
    [InlineData(-25, 0)]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    public async Task Invalid_window_is_rejected_before_access_and_runtime(
        int fromHours,
        int toHours)
    {
        await using var fixture = await Fixture.CreateAsync();

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.GetAsync(
                fixture.SiteId,
                new DateTimeOffset(Now.AddHours(fromHours)),
                new DateTimeOffset(Now.AddHours(toHours))));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(SpaceErrorCodes.RequestInvalid, error.Code);
        Assert.Empty(fixture.Access.Calls);
        Assert.Equal(0, fixture.Runtime.InventoryCalls);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private int _sequence;

        private Fixture(
            SpaceContext context,
            SpaceOperationsDiagnosticService service,
            RecordingRuntime runtime,
            RecordingAccess access,
            TestExecution execution,
            Guid siteId,
            Guid publishedVersionId,
            Guid floorId,
            Guid[] locationIds)
        {
            Context = context;
            Service = service;
            Runtime = runtime;
            Access = access;
            Execution = execution;
            SiteId = siteId;
            PublishedVersionId = publishedVersionId;
            FloorId = floorId;
            LocationIds = locationIds;
        }

        public SpaceContext Context { get; }
        public SpaceOperationsDiagnosticService Service { get; }
        public RecordingRuntime Runtime { get; }
        public RecordingAccess Access { get; }
        public TestExecution Execution { get; }
        public Guid SiteId { get; }
        public Guid PublishedVersionId { get; }
        public Guid FloorId { get; }
        public Guid[] LocationIds { get; }

        public static async Task<Fixture> CreateAsync(bool isExternal = false)
        {
            var execution = new TestExecution(
                Guid.NewGuid(),
                Guid.NewGuid(),
                isExternal);
            var clock = new TestClock();
            var context = new SpaceContext(
                new DbContextOptionsBuilder<SpaceContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                    .Options,
                execution,
                clock);
            var seeded = await SeedAsync(context, execution.TenantId);
            var runtime = new RecordingRuntime
            {
                Inventory = Inventory(seeded),
            };
            var access = new RecordingAccess();
            var service = new SpaceOperationsDiagnosticService(
                context,
                runtime,
                execution,
                clock,
                access,
                new SpacePersonnelRuntimeOptions(),
                new SpaceOperationsDiagnosticEngine());
            return new Fixture(
                context,
                service,
                runtime,
                access,
                execution,
                seeded.SiteId,
                seeded.PublishedVersionId,
                seeded.FloorId,
                seeded.LocationIds);
        }

        public void AddPosition(
            string sourceId,
            SpacePersonnelSourceKind sourceKind,
            string personId,
            double minutesFromNow,
            decimal x,
            Guid locationId)
        {
            _sequence++;
            var occurred = Now.AddMinutes(minutesFromNow);
            Context.PersonnelEvents.Add(SpacePersonnelEvent.Create(
                Execution.TenantId,
                SiteId,
                sourceId,
                sourceKind,
                $"EVENT-{_sequence:000}",
                personId,
                null,
                SpacePersonnelEventKind.PositionObserved,
                null,
                FloorId,
                locationId,
                x,
                0,
                0,
                100,
                _sequence,
                occurred,
                occurred.AddSeconds(1),
                Hash));
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private static async Task<Seeded> SeedAsync(
        SpaceContext context,
        Guid tenantId)
    {
        var siteId = Guid.NewGuid();
        var model = SpaceModel.Create(tenantId, siteId);
        var version = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "Published diagnostics model");
        var floorId = Guid.NewGuid();
        var floor = SpaceFloorRevision.Create(
            tenantId,
            version.Id,
            floorId,
            siteId,
            1,
            "F1",
            "Floor 1");
        var locations = new[]
        {
            SpaceLocationRevision.Create(
                tenantId, version.Id, Guid.NewGuid(), floorId, null,
                "F1-L01", 1, 1, 1, 1_000, 1_000, 1_000),
            SpaceLocationRevision.Create(
                tenantId, version.Id, Guid.NewGuid(), floorId, null,
                "F1-L02", 2, 1, 1, 1_000, 1_000, 1_000),
        };
        context.AddRange(model, version, floor);
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
            locations.Select(value => value.LogicalId).ToArray());
    }

    private static SpaceWmsRuntimeInventoryResponse Inventory(Seeded seeded) =>
        new(
            seeded.SiteId,
            seeded.PublishedVersionId,
            "WH-01",
            new SpaceWmsRuntimeSourceDto(
                "Real",
                "cp6-wms-v1",
                "CP6_WMS",
                new DateTimeOffset(Now.AddSeconds(-10)),
                new DateTimeOffset(Now.AddSeconds(-8)),
                2_000,
                0,
                false,
                true),
            [
                InventoryItem(seeded, 0, 10),
                InventoryItem(seeded, 1, 0),
            ]);

    private static SpaceWmsRuntimeInventoryItemDto InventoryItem(
        Seeded seeded,
        int index,
        decimal quantity) =>
        new(
            seeded.LocationIds[index],
            seeded.LocationIds[index],
            $"F1-L0{index + 1}",
            $"F1-L0{index + 1}",
            true,
            seeded.FloorId,
            "F1",
            "Floor 1",
            1,
            quantity,
            0,
            quantity > 0 ? "SKU-1" : null,
            null,
            null,
            quantity > 0 ? "OWNER-1" : null);

    private sealed record Seeded(
        Guid SiteId,
        Guid PublishedVersionId,
        Guid FloorId,
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
        public bool ThrowUnavailable { get; set; }
        public int InventoryCalls { get; private set; }

        public Task<SpaceWmsRuntimeInventoryResponse> QueryInventoryAsync(
            Guid siteId,
            IReadOnlyCollection<Guid>? locationLogicalIds = null,
            CancellationToken cancellationToken = default)
        {
            InventoryCalls++;
            if (ThrowUnavailable)
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.WmsUnavailable,
                    503,
                    "The WMS runtime source is unavailable.");
            }
            return Task.FromResult(Inventory);
        }

        public Task<SpaceWmsRuntimeInventoryLocateResponse> LocateInventoryAsync(
            Guid siteId,
            SpaceWmsInventoryLocateCriteria criteria,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SpaceWmsRuntimeTaskResponse> QueryTasksAsync(
            Guid siteId,
            IReadOnlyCollection<Guid>? locationLogicalIds = null,
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
