using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpacePlanningDatasetServiceTests
{
    private static readonly DateTime Now =
        new(2026, 7, 29, 20, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset HistoricalFrom =
        new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset HistoricalTo =
        HistoricalFrom.AddHours(4);

    [Fact]
    public async Task Import_is_idempotent_sorted_and_maps_replay_clock()
    {
        await using var fixture = await Fixture.CreateAsync();
        var datasetId = Guid.NewGuid();
        var request = fixture.ValidRequest();

        var created = await fixture.Service.CreateAsync(
            fixture.SiteId,
            fixture.BranchId,
            datasetId,
            request);
        var duplicate = await fixture.Service.CreateAsync(
            fixture.SiteId,
            fixture.BranchId,
            datasetId,
            request);
        var loaded = await fixture.Service.GetAsync(
            fixture.SiteId,
            fixture.BranchId,
            datasetId);
        var list = await fixture.Service.GetListAsync(
            fixture.SiteId,
            fixture.BranchId,
            10);

        Assert.Equal("Created", created.Outcome);
        Assert.Equal("Duplicate", duplicate.Outcome);
        Assert.Equal(datasetId, loaded.DatasetId);
        Assert.True(loaded.Deidentified);
        Assert.False(loaded.ProductionWriteAllowed);
        Assert.Equal(2, loaded.TaskCount);
        Assert.Equal(fixture.FirstTaskToken, loaded.Tasks[0].TaskToken);
        Assert.Equal(fixture.SecondTaskToken, loaded.Tasks[1].TaskToken);
        Assert.Equal(
            new DateTimeOffset(
                2026,
                7,
                29,
                12,
                15,
                0,
                TimeSpan.Zero),
            loaded.Tasks[0].ReplayCreatedAtUtc);
        Assert.Equal(30m * 60m, loaded.ReplayClock.ReplayDurationSeconds);
        Assert.Single(list.Items);
        Assert.False(list.IsTruncated);
        Assert.Equal(
            1,
            await fixture.Context.PlanningHistoricalDatasets.CountAsync());
        Assert.Equal(
            2,
            await fixture.Context.PlanningHistoricalTasks.CountAsync());
        var model = await fixture.Context.Models.SingleAsync();
        Assert.Equal(
            fixture.PublishedVersionId,
            model.CurrentPublishedVersionId);
        Assert.Null(model.ActiveDraftVersionId);
    }

    [Fact]
    public async Task Raw_identifiers_missing_locations_and_conflicts_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var raw = fixture.ValidRequest() with
        {
            Tasks =
            [
                fixture.ValidRequest().Tasks[0] with
                {
                    TaskToken = "raw-order-123",
                },
            ],
        };
        var rawError = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.CreateAsync(
                fixture.SiteId,
                fixture.BranchId,
                Guid.NewGuid(),
                raw));
        Assert.Equal(
            SpaceErrorCodes.PlanningDatasetDeidentificationRequired,
            rawError.Code);

        var unattested = fixture.ValidRequest() with
        {
            ConfirmDeidentified = false,
        };
        var attestationError =
            await Assert.ThrowsAsync<SpaceProblemException>(() =>
                fixture.Service.CreateAsync(
                    fixture.SiteId,
                    fixture.BranchId,
                    Guid.NewGuid(),
                    unattested));
        Assert.Equal(
            SpaceErrorCodes.PlanningDatasetDeidentificationRequired,
            attestationError.Code);

        var missing = fixture.ValidRequest() with
        {
            Tasks =
            [
                fixture.ValidRequest().Tasks[0] with
                {
                    ToLocationLogicalId = Guid.NewGuid(),
                },
            ],
        };
        var locationError =
            await Assert.ThrowsAsync<SpaceProblemException>(() =>
                fixture.Service.CreateAsync(
                    fixture.SiteId,
                    fixture.BranchId,
                    Guid.NewGuid(),
                    missing));
        Assert.Equal(
            SpaceErrorCodes.PlanningDatasetLocationInvalid,
            locationError.Code);

        var datasetId = Guid.NewGuid();
        await fixture.Service.CreateAsync(
            fixture.SiteId,
            fixture.BranchId,
            datasetId,
            fixture.ValidRequest());
        var conflict = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.CreateAsync(
                fixture.SiteId,
                fixture.BranchId,
                datasetId,
                fixture.ValidRequest() with { Name = "Different" }));
        Assert.Equal(SpaceErrorCodes.PlanningDatasetConflict, conflict.Code);
    }

    [Fact]
    public async Task Dataset_is_immutable_and_external_users_are_denied()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.SiteId,
            fixture.BranchId,
            Guid.NewGuid(),
            fixture.ValidRequest());
        var dataset = await fixture.Context.PlanningHistoricalDatasets
            .SingleAsync(value => value.Id == created.Dataset.DatasetId);
        fixture.Context.Entry(dataset)
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

    [Fact]
    public async Task Import_requires_a_completed_production_isolated_clone()
    {
        await using var fixture = await Fixture.CreateAsync(
            completeClone: false);

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.CreateAsync(
                fixture.SiteId,
                fixture.BranchId,
                Guid.NewGuid(),
                fixture.ValidRequest()));

        Assert.Equal(SpaceErrorCodes.PlanningDatasetBranchNotReady, error.Code);
        Assert.Empty(await fixture.Context.PlanningHistoricalDatasets
            .ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SpaceContext context,
            TestExecution execution,
            Guid siteId,
            Guid branchId,
            Guid locationId,
            Guid publishedVersionId)
        {
            Context = context;
            Execution = execution;
            SiteId = siteId;
            BranchId = branchId;
            LocationId = locationId;
            PublishedVersionId = publishedVersionId;
            Service = CreateService();
        }

        public SpaceContext Context { get; }
        public TestExecution Execution { get; }
        public Guid SiteId { get; }
        public Guid BranchId { get; }
        public Guid LocationId { get; }
        public Guid PublishedVersionId { get; }
        public string FirstTaskToken => new('a', 64);
        public string SecondTaskToken => new('b', 64);
        public SpacePlanningDatasetService Service { get; }

        public SpacePlanningDatasetService CreateService(
            bool external = false) =>
            new(
                Context,
                Execution with { IsExternal = external },
                new RecordingAccess(SiteId));

        public CreateSpacePlanningHistoricalDatasetRequest ValidRequest() =>
            new(
                "July replay",
                HistoricalFrom,
                HistoricalTo,
                new DateTimeOffset(
                    2026,
                    7,
                    29,
                    12,
                    0,
                    0,
                    TimeSpan.Zero),
                8m,
                new string('c', 64),
                true,
                [
                    new CreateSpacePlanningHistoricalTaskRequest(
                        SecondTaskToken,
                        new string('e', 64),
                        "Move",
                        "Completed",
                        HistoricalFrom.AddHours(3),
                        HistoricalFrom.AddHours(3).AddMinutes(30),
                        LocationId,
                        LocationId,
                        2m),
                    new CreateSpacePlanningHistoricalTaskRequest(
                        FirstTaskToken,
                        null,
                        "Pick",
                        "Completed",
                        HistoricalFrom.AddHours(2),
                        HistoricalFrom.AddHours(2).AddMinutes(30),
                        null,
                        LocationId,
                        1m),
                ]);

        public static async Task<Fixture> CreateAsync(
            bool completeClone = true)
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
            if (completeClone)
            {
                var attempt = job.Claim(
                    "planning-test",
                    "v1",
                    Now,
                    TimeSpan.FromMinutes(5));
                job.Complete(
                    attempt.Id,
                    "planning-test",
                    Now.AddSeconds(1));
            }
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
            var locationId = Guid.NewGuid();
            var location = SpaceLocationRevision.Create(
                execution.TenantId,
                scenario.Id,
                locationId,
                Guid.NewGuid(),
                null,
                "PLAN-01",
                1,
                1,
                1,
                1000,
                1000,
                1000);

            context.AddRange(
                model,
                published,
                scenario,
                job,
                branch,
                location);
            await context.SaveChangesAsync();
            return new Fixture(
                context,
                execution,
                siteId,
                branchId,
                locationId,
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
