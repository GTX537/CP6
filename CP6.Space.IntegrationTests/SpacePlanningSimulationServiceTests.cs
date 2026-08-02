using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpacePlanningSimulationServiceTests
{
    private static readonly DateTime Now =
        new(2026, 7, 29, 20, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset HistoricalFrom =
        new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Run_is_idempotent_evidence_backed_and_production_isolated()
    {
        await using var fixture = await Fixture.CreateAsync();
        var runId = Guid.NewGuid();

        var created = await fixture.Service.CreateAsync(
            fixture.SiteId,
            fixture.BranchId,
            runId,
            fixture.ValidRequest());
        var duplicate = await fixture.Service.CreateAsync(
            fixture.SiteId,
            fixture.BranchId,
            runId,
            fixture.ValidRequest());
        var loaded = await fixture.Service.GetAsync(
            fixture.SiteId,
            fixture.BranchId,
            runId);
        var list = await fixture.Service.GetListAsync(
            fixture.SiteId,
            fixture.BranchId,
            10);

        Assert.Equal("Created", created.Outcome);
        Assert.Equal("Duplicate", duplicate.Outcome);
        Assert.Equal(runId, loaded.RunId);
        Assert.Equal("Completed", loaded.Status);
        Assert.False(loaded.ProductionWriteAllowed);
        Assert.False(loaded.HighPrecisionPhysicalSimulation);
        Assert.Equal(1, loaded.ScenarioContentRevision);
        Assert.Equal(2, loaded.Distance.EligibleTaskCount);
        Assert.Equal(10m, loaded.Distance.TotalDistanceMeters);
        Assert.Equal(1_800, loaded.Congestion.CongestionSeconds);
        Assert.Equal(1, loaded.Capacity.OverloadedLocationCount);
        Assert.Equal(0.5m, loaded.Throughput.AverageCompletedTasksPerHour);
        Assert.Equal(37m, loaded.Cost.TotalCost);
        Assert.Single(loaded.LocationResults);
        Assert.False(loaded.LocationResultsTruncated);
        Assert.Single(list.Items);
        Assert.False(list.IsTruncated);
        Assert.Equal(
            1,
            await fixture.Context.PlanningSimulationRuns.CountAsync());
        Assert.Equal(
            1,
            await fixture.Context.PlanningSimulationLocationResults
                .CountAsync());
        var model = await fixture.Context.Models.SingleAsync();
        Assert.Equal(fixture.PublishedVersionId,
            model.CurrentPublishedVersionId);
        Assert.Null(model.ActiveDraftVersionId);
    }

    [Fact]
    public async Task Conflicts_invalid_dataset_and_unused_overrides_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var runId = Guid.NewGuid();
        await fixture.Service.CreateAsync(
            fixture.SiteId,
            fixture.BranchId,
            runId,
            fixture.ValidRequest());

        var conflict = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.CreateAsync(
                fixture.SiteId,
                fixture.BranchId,
                runId,
                fixture.ValidRequest() with { Name = "Different" }));
        Assert.Equal(
            SpaceErrorCodes.PlanningSimulationConflict,
            conflict.Code);

        var datasetError =
            await Assert.ThrowsAsync<SpaceProblemException>(() =>
                fixture.Service.CreateAsync(
                    fixture.SiteId,
                    fixture.BranchId,
                    Guid.NewGuid(),
                    fixture.ValidRequest() with
                    {
                        DatasetId = Guid.NewGuid(),
                    }));
        Assert.Equal(
            SpaceErrorCodes.PlanningSimulationDatasetInvalid,
            datasetError.Code);

        var overrideError =
            await Assert.ThrowsAsync<SpaceProblemException>(() =>
                fixture.Service.CreateAsync(
                    fixture.SiteId,
                    fixture.BranchId,
                    Guid.NewGuid(),
                    fixture.ValidRequest() with
                    {
                        LocationCapacities =
                        [
                            new(
                                fixture.SourceLocationId,
                                10,
                                1),
                        ],
                    }));
        Assert.Equal(
            SpaceErrorCodes.PlanningSimulationRequestInvalid,
            overrideError.Code);
    }

    [Fact]
    public async Task Results_are_immutable_and_external_users_are_denied()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.SiteId,
            fixture.BranchId,
            Guid.NewGuid(),
            fixture.ValidRequest());
        var run = await fixture.Context.PlanningSimulationRuns
            .SingleAsync(value => value.Id == created.Run.RunId);
        fixture.Context.Entry(run)
            .Property(value => value.Name)
            .CurrentValue = "Changed";
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Context.SaveChangesAsync());

        fixture.Context.ChangeTracker.Clear();
        var external = fixture.CreateService(external: true);
        var denied = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            external.GetListAsync(
                fixture.SiteId,
                fixture.BranchId,
                10));
        Assert.Equal(
            SpaceErrorCodes.PlanningScenarioInternalOnly,
            denied.Code);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SpaceContext context,
            TestExecution execution,
            Guid siteId,
            Guid branchId,
            Guid datasetId,
            Guid sourceLocationId,
            Guid destinationLocationId,
            Guid publishedVersionId)
        {
            Context = context;
            Execution = execution;
            SiteId = siteId;
            BranchId = branchId;
            DatasetId = datasetId;
            SourceLocationId = sourceLocationId;
            DestinationLocationId = destinationLocationId;
            PublishedVersionId = publishedVersionId;
            Service = CreateService();
        }

        public SpaceContext Context { get; }
        public TestExecution Execution { get; }
        public Guid SiteId { get; }
        public Guid BranchId { get; }
        public Guid DatasetId { get; }
        public Guid SourceLocationId { get; }
        public Guid DestinationLocationId { get; }
        public Guid PublishedVersionId { get; }
        public SpacePlanningSimulationService Service { get; }

        public SpacePlanningSimulationService CreateService(
            bool external = false) =>
            new(
                Context,
                Execution with { IsExternal = external },
                new RecordingAccess(SiteId));

        public CreateSpacePlanningSimulationRunRequest ValidRequest() =>
            new(
                "July capacity simulation",
                DatasetId,
                10,
                1,
                60,
                2,
                10,
                4,
                "cny",
                [
                    new(
                        DestinationLocationId,
                        3,
                        1),
                ]);

        public static async Task<Fixture> CreateAsync()
        {
            var execution = new TestExecution(
                Guid.NewGuid(),
                Guid.NewGuid());
            var context = new SpaceContext(
                new DbContextOptionsBuilder<SpaceContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                    .Options,
                execution,
                new TestClock());
            var siteId = Guid.NewGuid();
            var model = SpaceModel.Create(execution.TenantId, siteId);
            var published = SpaceModelVersion.CreateDraft(
                execution.TenantId,
                model.Id,
                1,
                "Production");
            published.BeginValidation();
            published.MarkReady(
                new string('1', 64),
                "space-rules-v1",
                new string('2', 64));
            published.BeginPublishing();
            published.MarkPublished(execution.ActorId, Now);
            model.SetPublishedVersion(published, new string('3', 64));
            model.BeginCutover(Guid.NewGuid());
            model.MarkFrozen();
            model.MarkBootstrapping();
            model.MarkVerified(published);
            model.ActivateDesignV1();

            var scenario = SpaceModelVersion
                .CreateInitializingPlanningScenario(
                    execution.TenantId,
                    model.Id,
                    2,
                    "July option",
                    published.Id,
                    Guid.NewGuid());
            scenario.CompleteInitialization(1);
            var job = SpaceJob.CreateQueued(
                execution.TenantId,
                SpaceJobType.CloneVersion,
                SpaceJobSubjectType.ModelVersion,
                scenario.Id,
                new string('4', 64),
                new string('5', 64),
                50,
                3,
                execution.ActorId,
                Now,
                Guid.NewGuid());
            var attempt = job.Claim(
                "planning-test",
                "v1",
                Now,
                TimeSpan.FromMinutes(5));
            job.Complete(
                attempt.Id,
                "planning-test",
                Now.AddSeconds(1));
            var branchId = Guid.NewGuid();
            var branch = SpacePlanningScenarioBranch.Create(
                execution.TenantId,
                branchId,
                new SpacePlanningScenarioBranchData(
                    siteId,
                    model.Id,
                    published.Id,
                    scenario.Id,
                    job.Id,
                    "July option",
                    "space-planning-scenario-v1",
                    new string('6', 64)));

            var floorId = Guid.NewGuid();
            var zoneId = Guid.NewGuid();
            var sourceRackId = Guid.NewGuid();
            var destinationRackId = Guid.NewGuid();
            var sourceRack = SpaceRackRevision.Create(
                execution.TenantId,
                scenario.Id,
                sourceRackId,
                floorId,
                zoneId,
                "SOURCE");
            sourceRack.ConfigureGeometry(0, 0, 0, 0, 2_000, 2_000, 2_000);
            var destinationRack = SpaceRackRevision.Create(
                execution.TenantId,
                scenario.Id,
                destinationRackId,
                floorId,
                zoneId,
                "DESTINATION");
            destinationRack.ConfigureGeometry(
                3_000,
                4_000,
                0,
                0,
                2_000,
                2_000,
                2_000);
            var sourceLevel = SpaceRackLevelRevision.Create(
                execution.TenantId,
                scenario.Id,
                Guid.NewGuid(),
                sourceRackId,
                1,
                0,
                1_000,
                1,
                1,
                1_000,
                1_000);
            var destinationLevel = SpaceRackLevelRevision.Create(
                execution.TenantId,
                scenario.Id,
                Guid.NewGuid(),
                destinationRackId,
                1,
                0,
                1_000,
                1,
                1,
                1_000,
                1_000);
            var sourceLocationId = Guid.NewGuid();
            var destinationLocationId = Guid.NewGuid();
            var sourceLocation = SpaceLocationRevision.Create(
                execution.TenantId,
                scenario.Id,
                sourceLocationId,
                floorId,
                sourceRackId,
                "SOURCE-01",
                1,
                1,
                1,
                1_000,
                1_000,
                1_000);
            var destinationLocation = SpaceLocationRevision.Create(
                execution.TenantId,
                scenario.Id,
                destinationLocationId,
                floorId,
                destinationRackId,
                "DESTINATION-01",
                1,
                1,
                1,
                1_000,
                1_000,
                1_000);

            var datasetId = Guid.NewGuid();
            var dataset = SpacePlanningHistoricalDataset.Create(
                execution.TenantId,
                datasetId,
                new SpacePlanningHistoricalDatasetData(
                    siteId,
                    model.Id,
                    branchId,
                    scenario.Id,
                    "July replay",
                    HistoricalFrom,
                    HistoricalFrom.AddHours(4),
                    new DateTimeOffset(
                        2026,
                        7,
                        29,
                        12,
                        0,
                        0,
                        TimeSpan.Zero),
                    8,
                    2,
                    new string('7', 64),
                    new string('8', 64),
                    SpacePlanningDatasetService.DefinitionVersion,
                    SpacePlanningDatasetService.DeidentificationVersion));
            var clock = SpaceReplayClock.Create(dataset);
            var worker = new string('a', 64);
            var tasks = new[]
            {
                SpacePlanningHistoricalTask.Create(
                    dataset,
                    new SpacePlanningHistoricalTaskData(
                        1,
                        new string('b', 64),
                        worker,
                        SpacePlanningTaskType.Move,
                        SpacePlanningTaskOutcome.Completed,
                        HistoricalFrom,
                        HistoricalFrom.AddHours(1),
                        clock.Map(HistoricalFrom),
                        clock.Map(HistoricalFrom.AddHours(1)),
                        sourceLocationId,
                        destinationLocationId,
                        2)),
                SpacePlanningHistoricalTask.Create(
                    dataset,
                    new SpacePlanningHistoricalTaskData(
                        2,
                        new string('c', 64),
                        worker,
                        SpacePlanningTaskType.Move,
                        SpacePlanningTaskOutcome.Completed,
                        HistoricalFrom.AddMinutes(30),
                        HistoricalFrom.AddMinutes(90),
                        clock.Map(HistoricalFrom.AddMinutes(30)),
                        clock.Map(HistoricalFrom.AddMinutes(90)),
                        sourceLocationId,
                        destinationLocationId,
                        2)),
            };
            context.AddRange(
                model,
                published,
                scenario,
                job,
                branch,
                sourceRack,
                destinationRack,
                sourceLevel,
                destinationLevel,
                sourceLocation,
                destinationLocation,
                dataset);
            context.AddRange(tasks);
            await context.SaveChangesAsync();
            return new Fixture(
                context,
                execution,
                siteId,
                branchId,
                datasetId,
                sourceLocationId,
                destinationLocationId,
                published.Id);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record TestExecution(Guid TenantId, Guid ActorId)
        : ISpaceExecutionContext
    {
        public bool IsExternal { get; init; }
    }

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow => Now.AddSeconds(2);
    }

    private sealed class RecordingAccess(Guid expectedSiteId)
        : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
            Assert.Equal(expectedSiteId, siteId);
        }
    }
}
