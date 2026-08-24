using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpacePlanningScenarioServiceTests
{
    private static readonly DateTime Now =
        new(2026, 7, 29, 19, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Multiple_scenarios_are_idempotent_and_do_not_reserve_draft()
    {
        await using var fixture = await Fixture.CreateAsync();
        var firstId = Guid.NewGuid();
        var request = new CreateSpacePlanningScenarioBranchRequest(
            fixture.Published.Id,
            "Peak season option");

        var created = await fixture.Service.CreateBranchAsync(
            fixture.SiteId,
            firstId,
            request);
        var duplicate = await fixture.Service.CreateBranchAsync(
            fixture.SiteId,
            firstId,
            request);
        var second = await fixture.Service.CreateBranchAsync(
            fixture.SiteId,
            Guid.NewGuid(),
            request with { Name = "Automation option" });
        var list = await fixture.Service.GetBranchesAsync(
            fixture.SiteId,
            10);
        var productionVersions = await fixture.CreateDesignService()
            .GetVersionsAsync(
                fixture.SiteId,
                status: null,
                limit: 10,
                cursor: null);
        var scenarioVersion = await fixture.CreateDesignService()
            .GetVersionAsync(created.Branch.ScenarioVersionId);

        Assert.Equal("Created", created.Outcome);
        Assert.Equal("Duplicate", duplicate.Outcome);
        Assert.Equal(created.Branch, duplicate.Branch);
        Assert.True(created.Branch.ProductionIsolated);
        Assert.Equal("Initializing", created.Branch.BranchStatus);
        Assert.Equal("Created", second.Outcome);
        Assert.Equal(2, list.Items.Count);
        Assert.False(list.IsTruncated);
        Assert.Single(productionVersions.Items);
        Assert.Equal(
            fixture.Published.Id,
            productionVersions.Items[0].Id);
        Assert.Equal("PlanningScenario", scenarioVersion.Purpose);
        Assert.Equal(
            2,
            await fixture.Context.Versions.CountAsync(
                value =>
                    value.Purpose ==
                    SpaceModelVersionPurpose.PlanningScenario));
        Assert.Equal(
            2,
            await fixture.Context.PlanningScenarioBranches.CountAsync());
        Assert.Null(
            (await fixture.Context.Models.SingleAsync()).ActiveDraftVersionId);
        Assert.Equal(
            fixture.Published.Id,
            (await fixture.Context.Models.SingleAsync())
                .CurrentPublishedVersionId);
    }

    [Fact]
    public async Task Conflicting_identity_stale_base_and_external_user_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var branchId = Guid.NewGuid();
        var request = new CreateSpacePlanningScenarioBranchRequest(
            fixture.Published.Id,
            "Option A");
        await fixture.Service.CreateBranchAsync(
            fixture.SiteId,
            branchId,
            request);

        var identityConflict =
            await Assert.ThrowsAsync<SpaceProblemException>(() =>
                fixture.Service.CreateBranchAsync(
                    fixture.SiteId,
                    branchId,
                    request with { Name = "Different" }));
        var staleBase =
            await Assert.ThrowsAsync<SpaceProblemException>(() =>
                fixture.Service.CreateBranchAsync(
                    fixture.SiteId,
                    Guid.NewGuid(),
                    request with
                    {
                        BasePublishedVersionId = Guid.NewGuid(),
                    }));

        Assert.Equal(
            SpaceErrorCodes.PlanningScenarioConflict,
            identityConflict.Code);
        Assert.Equal(
            SpaceErrorCodes.PlanningScenarioBaseInvalid,
            staleBase.Code);

        var external = fixture.CreateService(external: true);
        var externalError =
            await Assert.ThrowsAsync<SpaceProblemException>(() =>
                external.GetBranchesAsync(fixture.SiteId, 10));
        Assert.Equal(
            SpaceErrorCodes.PlanningScenarioInternalOnly,
            externalError.Code);
    }

    [Fact]
    public async Task Stored_branch_is_immutable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateBranchAsync(
            fixture.SiteId,
            Guid.NewGuid(),
            new CreateSpacePlanningScenarioBranchRequest(
                fixture.Published.Id,
                "Immutable"));
        var branch = await fixture.Context.PlanningScenarioBranches
            .SingleAsync(value =>
                value.Id == created.Branch.BranchId);

        fixture.Context.Entry(branch)
            .Property(value => value.Name)
            .CurrentValue = "Changed";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Context.SaveChangesAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SpaceContext context,
            TestExecution execution,
            TestClock clock,
            Guid siteId,
            SpaceModelVersion published)
        {
            Context = context;
            Execution = execution;
            Clock = clock;
            SiteId = siteId;
            Published = published;
            Service = CreateService();
        }

        public SpaceContext Context { get; }
        public TestExecution Execution { get; }
        public TestClock Clock { get; }
        public Guid SiteId { get; }
        public SpaceModelVersion Published { get; }
        public SpacePlanningScenarioService Service { get; }

        public SpacePlanningScenarioService CreateService(
            bool external = false) =>
            new(
                Context,
                Execution with { IsExternal = external },
                Clock,
                new RecordingAccess(SiteId));

        public SpaceDesignV1Service CreateDesignService() =>
            new(
                Context,
                Execution,
                Clock,
                new TestCursor(),
                new RecordingAccess(SiteId),
                new SpaceVersionCloneCoordinator(
                    Execution,
                    new UnusedCloneStore()),
                new SpaceSourceCoordinator(Execution));

        public static async Task<Fixture> CreateAsync()
        {
            var execution = new TestExecution(
                Guid.NewGuid(),
                Guid.NewGuid());
            var clock = new TestClock();
            var context = new SpaceContext(
                new DbContextOptionsBuilder<SpaceContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                    .Options,
                execution,
                clock);
            var siteId = Guid.NewGuid();
            var model = SpaceModel.Create(execution.TenantId, siteId);
            var published = SpaceModelVersion.CreateDraft(
                execution.TenantId,
                model.Id,
                1,
                "Production v1");
            published.BeginValidation();
            published.MarkReady(
                new string('a', 64),
                "space-rules-v1",
                new string('b', 64));
            published.BeginPublishing();
            published.MarkPublished(execution.ActorId, Now);
            model.SetPublishedVersion(published, new string('c', 64));
            model.BeginCutover(Guid.NewGuid());
            model.MarkFrozen();
            model.MarkBootstrapping();
            model.MarkVerified(published);
            model.ActivateDesignV1();
            context.Models.Add(model);
            context.Versions.Add(published);
            await context.SaveChangesAsync();
            return new Fixture(
                context,
                execution,
                clock,
                siteId,
                published);
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
        public DateTime UtcNow => Now;
    }

    private sealed class RecordingAccess(Guid expectedSiteId)
        : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
            Assert.Equal(expectedSiteId, siteId);
        }
    }

    private sealed class TestCursor : ISpaceCursorCodec
    {
        public string Encode(SpaceCursorState state) =>
            throw new NotSupportedException();

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedCloneStore : ISpaceVersionCloneStore
    {
        public Task<SpaceVersionCloneStartResult> StartAsync(
            SpaceVersionCloneRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SpaceBlankVersionStartResult> StartBlankAsync(
            SpaceBlankVersionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
