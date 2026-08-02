using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpacePlanningComparisonServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 23, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset HistoricalFrom =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Comparison_and_human_decision_chain_are_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var comparisonId = Guid.NewGuid();
        var request = fixture.ValidComparisonRequest();

        var created = await fixture.Service.CreateComparisonAsync(
            fixture.SiteId,
            comparisonId,
            request);
        var duplicate = await fixture.Service.CreateComparisonAsync(
            fixture.SiteId,
            comparisonId,
            request);
        var loaded = await fixture.Service.GetComparisonAsync(
            fixture.SiteId,
            comparisonId);
        var list = await fixture.Service.GetComparisonsAsync(
            fixture.SiteId,
            10);

        Assert.Equal("Created", created.Outcome);
        Assert.Equal("Duplicate", duplicate.Outcome);
        Assert.False(loaded.AutomatedRanking);
        Assert.False(loaded.ProductionWriteAllowed);
        Assert.Equal(fixture.BaselineRunId, loaded.Entries[0].RunId);
        Assert.True(loaded.Entries[0].IsBaseline);
        Assert.Equal(-10m,
            loaded.Entries[1].DeltaFromBaseline.DistanceMeters);
        Assert.Contains(
            loaded.Entries[1].Risks,
            value => value.Code == "OVERLOADED_LOCATIONS_PRESENT");
        Assert.Single(list.Items);

        var firstDecisionId = Guid.NewGuid();
        var first = await fixture.Service.CreateDecisionAsync(
            fixture.SiteId,
            comparisonId,
            firstDecisionId,
            new CreateSpacePlanningDecisionRequest(
                "Selected",
                fixture.CandidateRunId,
                "Select the candidate after reviewing its capacity risk.",
                null));
        var firstDuplicate = await fixture.Service.CreateDecisionAsync(
            fixture.SiteId,
            comparisonId,
            firstDecisionId,
            new CreateSpacePlanningDecisionRequest(
                "Selected",
                fixture.CandidateRunId,
                "Select the candidate after reviewing its capacity risk.",
                null));
        var secondDecisionId = Guid.NewGuid();
        var second = await fixture.Service.CreateDecisionAsync(
            fixture.SiteId,
            comparisonId,
            secondDecisionId,
            new CreateSpacePlanningDecisionRequest(
                "Deferred",
                null,
                "Defer while the capacity assumption is verified.",
                firstDecisionId));
        var decisions = await fixture.Service.GetDecisionsAsync(
            fixture.SiteId,
            comparisonId,
            10);

        Assert.Equal("Created", first.Outcome);
        Assert.Equal("Duplicate", firstDuplicate.Outcome);
        Assert.True(first.Decision.HumanDecision);
        Assert.False(first.Decision.AutomatedRecommendation);
        Assert.False(first.Decision.ProductionWriteAllowed);
        Assert.Equal(firstDecisionId, second.Decision.SupersedesDecisionId);
        Assert.Equal(2, decisions.Items.Count);
        Assert.Equal(
            2,
            await fixture.Context.PlanningDecisionRecords.CountAsync());
    }

    [Fact]
    public async Task Comparison_conflicts_and_incomparable_evidence_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var comparisonId = Guid.NewGuid();
        await fixture.Service.CreateComparisonAsync(
            fixture.SiteId,
            comparisonId,
            fixture.ValidComparisonRequest());

        var conflict = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.CreateComparisonAsync(
                fixture.SiteId,
                comparisonId,
                fixture.ValidComparisonRequest() with { Name = "Different" }));
        Assert.Equal(SpaceErrorCodes.PlanningComparisonConflict, conflict.Code);

        await using var mismatched = await Fixture.CreateAsync(
            mismatchedSourceDataset: true);
        var invalid = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            mismatched.Service.CreateComparisonAsync(
                mismatched.SiteId,
                Guid.NewGuid(),
                mismatched.ValidComparisonRequest()));
        Assert.Equal(
            SpaceErrorCodes.PlanningComparisonEvidenceInvalid,
            invalid.Code);
    }

    [Fact]
    public async Task Decisions_require_current_head_and_evidence_is_immutable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var comparisonId = Guid.NewGuid();
        await fixture.Service.CreateComparisonAsync(
            fixture.SiteId,
            comparisonId,
            fixture.ValidComparisonRequest());

        var wrongRun = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.CreateDecisionAsync(
                fixture.SiteId,
                comparisonId,
                Guid.NewGuid(),
                new CreateSpacePlanningDecisionRequest(
                    "Selected",
                    Guid.NewGuid(),
                    "This run is not comparison evidence.",
                    null)));
        Assert.Equal(SpaceErrorCodes.PlanningDecisionInvalid, wrongRun.Code);

        var firstId = Guid.NewGuid();
        await fixture.Service.CreateDecisionAsync(
            fixture.SiteId,
            comparisonId,
            firstId,
            new CreateSpacePlanningDecisionRequest(
                "Deferred",
                null,
                "Wait for review.",
                null));
        var missingHead = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.CreateDecisionAsync(
                fixture.SiteId,
                comparisonId,
                Guid.NewGuid(),
                new CreateSpacePlanningDecisionRequest(
                    "RejectedAll",
                    null,
                    "Reject all current options.",
                    null)));
        Assert.Equal(SpaceErrorCodes.PlanningDecisionInvalid, missingHead.Code);

        var comparison = await fixture.Context.PlanningComparisons.SingleAsync();
        fixture.Context.Entry(comparison)
            .Property(value => value.Name)
            .CurrentValue = "Changed";
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Context.SaveChangesAsync());

        fixture.Context.ChangeTracker.Clear();
        var external = fixture.CreateService(external: true);
        var denied = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            external.GetComparisonsAsync(fixture.SiteId, 10));
        Assert.Equal(SpaceErrorCodes.PlanningScenarioInternalOnly, denied.Code);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SpaceContext context,
            TestExecution execution,
            Guid siteId,
            Guid baselineRunId,
            Guid candidateRunId)
        {
            Context = context;
            Execution = execution;
            SiteId = siteId;
            BaselineRunId = baselineRunId;
            CandidateRunId = candidateRunId;
            Service = CreateService();
        }

        public SpaceContext Context { get; }
        public TestExecution Execution { get; }
        public Guid SiteId { get; }
        public Guid BaselineRunId { get; }
        public Guid CandidateRunId { get; }
        public SpacePlanningComparisonService Service { get; }

        public SpacePlanningComparisonService CreateService(
            bool external = false) =>
            new(
                Context,
                Execution with { IsExternal = external },
                new RecordingAccess(SiteId));

        public CreateSpacePlanningComparisonRequest ValidComparisonRequest() =>
            new(
                "July scenario comparison",
                BaselineRunId,
                [CandidateRunId, BaselineRunId],
                95m,
                100m,
                0.25m,
                100m);

        public static async Task<Fixture> CreateAsync(
            bool mismatchedSourceDataset = false)
        {
            var execution = new TestExecution(Guid.NewGuid(), Guid.NewGuid());
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

            var first = Scenario(
                execution,
                siteId,
                model,
                published,
                "Baseline option",
                2,
                new string('4', 64));
            var second = Scenario(
                execution,
                siteId,
                model,
                published,
                "Candidate option",
                3,
                new string('5', 64));
            var sourceHash = new string('7', 64);
            var firstDataset = Dataset(
                execution,
                siteId,
                model.Id,
                first.Branch,
                first.Version,
                sourceHash,
                new string('8', 64));
            var secondDataset = Dataset(
                execution,
                siteId,
                model.Id,
                second.Branch,
                second.Version,
                mismatchedSourceDataset ? new string('9', 64) : sourceHash,
                new string('a', 64));
            var baselineRunId = Guid.NewGuid();
            var candidateRunId = Guid.NewGuid();
            var baseline = Run(
                execution,
                baselineRunId,
                siteId,
                model.Id,
                first.Branch,
                first.Version,
                firstDataset,
                "Baseline run",
                new string('b', 64),
                100m,
                1_000m,
                0,
                0,
                90m,
                80m);
            var candidate = Run(
                execution,
                candidateRunId,
                siteId,
                model.Id,
                second.Branch,
                second.Version,
                secondDataset,
                "Candidate run",
                new string('c', 64),
                90m,
                990m,
                3_600,
                1,
                120m,
                70m);

            context.AddRange(
                model,
                published,
                first.Version,
                first.Job,
                first.Branch,
                second.Version,
                second.Job,
                second.Branch,
                firstDataset,
                secondDataset,
                baseline,
                candidate);
            await context.SaveChangesAsync();
            return new Fixture(
                context,
                execution,
                siteId,
                baselineRunId,
                candidateRunId);
        }

        private static ScenarioEvidence Scenario(
            TestExecution execution,
            Guid siteId,
            SpaceModel model,
            SpaceModelVersion published,
            string name,
            int versionNo,
            string requestHash)
        {
            var version = SpaceModelVersion.CreateInitializingPlanningScenario(
                execution.TenantId,
                model.Id,
                versionNo,
                name,
                published.Id,
                Guid.NewGuid());
            version.CompleteInitialization(1);
            var job = SpaceJob.CreateQueued(
                execution.TenantId,
                SpaceJobType.CloneVersion,
                SpaceJobSubjectType.ModelVersion,
                version.Id,
                requestHash,
                new string('d', 64),
                50,
                3,
                execution.ActorId,
                Now,
                Guid.NewGuid());
            var attempt = job.Claim(
                "comparison-test",
                "v1",
                Now,
                TimeSpan.FromMinutes(5));
            job.Complete(attempt.Id, "comparison-test", Now.AddSeconds(1));
            var branch = SpacePlanningScenarioBranch.Create(
                execution.TenantId,
                Guid.NewGuid(),
                new SpacePlanningScenarioBranchData(
                    siteId,
                    model.Id,
                    published.Id,
                    version.Id,
                    job.Id,
                    name,
                    "space-planning-scenario-v1",
                    requestHash));
            return new ScenarioEvidence(version, job, branch);
        }

        private static SpacePlanningHistoricalDataset Dataset(
            TestExecution execution,
            Guid siteId,
            Guid modelId,
            SpacePlanningScenarioBranch branch,
            SpaceModelVersion version,
            string sourceHash,
            string requestHash) =>
            SpacePlanningHistoricalDataset.Create(
                execution.TenantId,
                Guid.NewGuid(),
                new SpacePlanningHistoricalDatasetData(
                    siteId,
                    modelId,
                    branch.Id,
                    version.Id,
                    "July replay",
                    HistoricalFrom,
                    HistoricalFrom.AddHours(4),
                    HistoricalFrom.AddDays(31),
                    8m,
                    2,
                    sourceHash,
                    requestHash,
                    SpacePlanningDatasetService.DefinitionVersion,
                    SpacePlanningDatasetService.DeidentificationVersion));

        private static SpacePlanningSimulationRun Run(
            TestExecution execution,
            Guid runId,
            Guid siteId,
            Guid modelId,
            SpacePlanningScenarioBranch branch,
            SpaceModelVersion version,
            SpacePlanningHistoricalDataset dataset,
            string name,
            string resultHash,
            decimal coverage,
            decimal distance,
            long congestionTaskSeconds,
            int overloaded,
            decimal utilization,
            decimal totalCost) =>
            SpacePlanningSimulationRun.Create(
                execution.TenantId,
                runId,
                new SpacePlanningSimulationRunData(
                    siteId,
                    modelId,
                    branch.Id,
                    version.Id,
                    version.ContentRevision,
                    dataset.Id,
                    name,
                    SpacePlanningSimulationEngine.DefinitionVersion,
                    new string('e', 64),
                    dataset.RequestHash,
                    resultHash,
                    SpacePlanningSimulationEngine.GeometryBasis,
                    "CNY",
                    100m,
                    2,
                    0,
                    60,
                    2m,
                    10m,
                    4m,
                    2,
                    2,
                    4m,
                    2,
                    distance,
                    coverage,
                    2,
                    congestionTaskSeconds,
                    congestionTaskSeconds,
                    overloaded,
                    utilization,
                    20m,
                    40m,
                    40m,
                    80m,
                    2m,
                    distance * 2m,
                    20m,
                    congestionTaskSeconds / 3_600m * 4m,
                    totalCost));

        public ValueTask DisposeAsync() => Context.DisposeAsync();

        private sealed record ScenarioEvidence(
            SpaceModelVersion Version,
            SpaceJob Job,
            SpacePlanningScenarioBranch Branch);
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
        public void EnsureSiteAccess(Guid siteId, bool write) =>
            Assert.Equal(expectedSiteId, siteId);
    }
}
