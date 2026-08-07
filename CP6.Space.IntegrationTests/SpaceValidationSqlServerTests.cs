using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceValidationSqlServerTests
{
    [SqlServerFact]
    public async Task Validation_job_passes_reuses_and_preserves_ai_category()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            Guid versionId;
            Guid siteId;
            await using (var seed = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                var graph = await SeedCandidateAsync(
                    seed,
                    completeRack: true);
                versionId = graph.Version.Id;
                siteId = graph.Model.SiteId;
                seed.Issues.Add(
                    SpaceModelIssue.Create(
                        execution.TenantId,
                        versionId,
                        null,
                        null,
                        SpaceIssueSeverity.Warning,
                        "AI_LOW_CONFIDENCE",
                        "layer:rack",
                        graph.Rack.LogicalId,
                        """{"score":0.42}""",
                        "review-ai-proposal",
                        category: SpaceValidationCategories.AiProvenance,
                        fieldPath: "/attributes/rackType",
                        evidenceJson: """{"provider":"mock"}"""));
                await seed.SaveChangesAsync();
            }

            Guid validationId;
            Guid jobId;
            await using (var requestContext = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                var service = NewValidationService(
                    requestContext,
                    execution,
                    clock,
                    siteId);
                var created = await service.RequestValidationAsync(versionId);

                Assert.False(created.Reused);
                Assert.Equal("Queued", created.Validation.Status);
                Assert.Empty(created.Validation.Issues);
                validationId = created.Validation.Id;
                jobId = created.Validation.JobId;
                Assert.Equal(
                    SpaceVersionStatus.Validating,
                    await requestContext.Versions
                        .Where(value => value.Id == versionId)
                        .Select(value => value.Status)
                        .SingleAsync());
            }

            await ProcessNextAsync(connectionString, execution, clock);

            await using (var verify = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                var service = NewValidationService(
                    verify,
                    execution,
                    clock,
                    siteId);
                var result = await service.GetValidationAsync(validationId);
                Assert.Equal("Passed", result.Status);
                Assert.Equal(0, result.BlockingCount);
                Assert.Equal(1, result.WarningCount);
                var ai = Assert.Single(
                    result.Issues,
                    issue => issue.Code == "AI_LOW_CONFIDENCE");
                Assert.Equal("AiProvenance", ai.Category);
                Assert.Null(ai.GenerationRunId);
                Assert.Null(ai.GenerationProposalId);
                Assert.Equal(validationId, ai.ValidationRunId);
                Assert.Equal(
                    SpaceVersionStatus.Ready,
                    await verify.Versions
                        .Where(value => value.Id == versionId)
                        .Select(value => value.Status)
                        .SingleAsync());
                Assert.Equal(
                    SpaceJobStatus.Succeeded,
                    await verify.Jobs
                        .Where(value => value.Id == jobId)
                        .Select(value => value.Status)
                        .SingleAsync());

                var replay = await service.RequestValidationAsync(versionId);
                Assert.True(replay.Reused);
                Assert.Equal(validationId, replay.Validation.Id);
                Assert.Single(await verify.ValidationRuns.ToListAsync());
                Assert.Single(
                    await verify.Jobs
                        .Where(value => value.JobType == SpaceJobType.Validate)
                        .ToListAsync());

                var publishedVersion = await verify.Versions
                    .SingleAsync(value => value.Id == versionId);
                publishedVersion.BeginPublishing();
                publishedVersion.MarkPublished(execution.ActorId, clock.UtcNow);
                await verify.SaveChangesAsync();

                var rejected = await Assert.ThrowsAsync<SpaceProblemException>(
                    () => service.RequestValidationAsync(versionId));
                Assert.Equal(SpaceErrorCodes.VersionStateInvalid, rejected.Code);
                Assert.Equal(409, rejected.StatusCode);
            }

            var otherExecution = execution with
            {
                TenantId = Guid.NewGuid(),
                ActorId = Guid.NewGuid(),
            };
            await using var other = CreateContext(
                connectionString,
                otherExecution,
                clock);
            var otherService = NewValidationService(
                other,
                otherExecution,
                clock,
                siteId);
            var hidden = await Assert.ThrowsAsync<SpaceProblemException>(
                () => otherService.GetValidationAsync(validationId));
            Assert.Equal(SpaceErrorCodes.ValidationNotFound, hidden.Code);
            Assert.Equal(404, hidden.StatusCode);
        });
    }

    [SqlServerFact]
    public async Task Concurrent_same_input_reuses_one_run_and_one_job()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            Guid versionId;
            Guid siteId;
            await using (var seed = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                var graph = await SeedCandidateAsync(
                    seed,
                    completeRack: true);
                versionId = graph.Version.Id;
                siteId = graph.Model.SiteId;
            }

            await using var firstContext = CreateContext(
                connectionString,
                execution,
                clock);
            await using var secondContext = CreateContext(
                connectionString,
                execution,
                clock);
            var first = NewValidationService(
                firstContext,
                execution,
                clock,
                siteId);
            var second = NewValidationService(
                secondContext,
                execution,
                clock,
                siteId);

            var results = await Task.WhenAll(
                first.RequestValidationAsync(versionId),
                second.RequestValidationAsync(versionId));

            Assert.Equal(
                results[0].Validation.Id,
                results[1].Validation.Id);
            Assert.Single(results, result => result.Reused);

            await using var verify = CreateContext(
                connectionString,
                execution,
                clock);
            Assert.Single(await verify.ValidationRuns.ToListAsync());
            Assert.Single(
                await verify.Jobs
                    .Where(job => job.JobType == SpaceJobType.Validate)
                    .ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task Incomplete_rack_blocks_version_and_emits_unified_issue()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            Guid versionId;
            Guid siteId;
            await using (var seed = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                var graph = await SeedCandidateAsync(
                    seed,
                    completeRack: false);
                versionId = graph.Version.Id;
                siteId = graph.Model.SiteId;
            }

            Guid validationId;
            await using (var request = CreateContext(
                             connectionString,
                             execution,
                             clock))
            {
                var service = NewValidationService(
                    request,
                    execution,
                    clock,
                    siteId);
                validationId =
                    (await service.RequestValidationAsync(versionId))
                    .Validation.Id;
            }

            await ProcessNextAsync(connectionString, execution, clock);

            await using var verify = CreateContext(
                connectionString,
                execution,
                clock);
            var result = await NewValidationService(
                    verify,
                    execution,
                    clock,
                    siteId)
                .GetValidationAsync(validationId);
            Assert.Equal("Blocked", result.Status);
            Assert.True(result.BlockingCount > 0);
            Assert.Contains(
                result.Issues,
                issue =>
                    issue.Code ==
                    SpaceValidationIssueCodes.RackLocationIncomplete);
            Assert.Equal(
                SpaceVersionStatus.Draft,
                await verify.Versions
                    .Where(value => value.Id == versionId)
                    .Select(value => value.Status)
                    .SingleAsync());
        });
    }

    private static async Task ProcessNextAsync(
        string connectionString,
        TestExecutionContext execution,
        TestClock clock)
    {
        await using var worker = CreateContext(
            connectionString,
            execution,
            clock);
        var leases = new EfSpaceJobLeaseStore(worker, clock);
        var lease = await leases.TryClaimNextAsync(
            "validation-test-worker",
            SpaceValidationRuleSet.ProcessorVersion,
            TimeSpan.FromMinutes(2));
        Assert.NotNull(lease);
        Assert.Equal(SpaceJobType.Validate, lease!.JobType);
        var runner = new SpaceJobProcessorRunner(
            leases,
            [
                new SpaceValidationJobProcessor(
                    worker,
                    clock,
                    new TestProfileProvider(),
                    new SpaceValidationEngine()),
            ],
            new SpaceJobProcessorOptions
            {
                LeaseDuration = TimeSpan.FromMinutes(2),
                HeartbeatInterval = TimeSpan.FromSeconds(10),
            });
        await runner.RunClaimedAsync(lease);
    }

    private static SpaceValidationService NewValidationService(
        SpaceContext context,
        TestExecutionContext execution,
        TestClock clock,
        Guid allowedSiteId) =>
        new(
            context,
            execution,
            clock,
            new TestAccessEvaluator(allowedSiteId),
            new TestProfileProvider(),
            new SpaceValidationEngine());

    private static async Task<SeededCandidate> SeedCandidateAsync(
        SpaceContext context,
        bool completeRack)
    {
        var model = SpaceModel.Create(
            context.CurrentTenantId,
            Guid.NewGuid());
        context.Add(model);
        await context.SaveChangesAsync();

        var version = SpaceModelVersion.CreateDraft(
            context.CurrentTenantId,
            model.Id,
            1,
            "Validation candidate");
        model.ReserveDraft(version);
        var floor = SpaceFloorRevision.Create(
            context.CurrentTenantId,
            version.Id,
            Guid.NewGuid(),
            model.SiteId,
            1,
            "F1",
            "Floor 1",
            height: 5000);
        floor.ConfigureBoundary(
            """{"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}""",
            "LOCAL_MM_Z_UP");
        var zone = SpaceZoneRevision.Create(
            context.CurrentTenantId,
            version.Id,
            Guid.NewGuid(),
            floor.LogicalId,
            "Z1",
            1);
        zone.ConfigureShape(
            """{"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}""");
        var rack = SpaceRackRevision.Create(
            context.CurrentTenantId,
            version.Id,
            Guid.NewGuid(),
            floor.LogicalId,
            zone.LogicalId,
            "R1");
        rack.ConfigureGeometry(
            1000,
            1000,
            0,
            0,
            2000,
            1000,
            2000);
        var level = SpaceRackLevelRevision.Create(
            context.CurrentTenantId,
            version.Id,
            Guid.NewGuid(),
            rack.LogicalId,
            1,
            0,
            1800,
            2,
            1,
            1000,
            1000,
            100);
        var first = SpaceLocationRevision.Create(
            context.CurrentTenantId,
            version.Id,
            Guid.NewGuid(),
            floor.LogicalId,
            rack.LogicalId,
            "R1-01",
            1,
            1,
            1,
            1000,
            1800,
            1000);
        context.AddRange(version, floor, zone, rack, level, first);
        if (completeRack)
        {
            context.Add(
                SpaceLocationRevision.Create(
                    context.CurrentTenantId,
                    version.Id,
                    Guid.NewGuid(),
                    floor.LogicalId,
                    rack.LogicalId,
                    "R1-02",
                    2,
                    1,
                    1,
                    1000,
                    1800,
                    1000));
        }
        await context.SaveChangesAsync();
        return new SeededCandidate(model, version, rack);
    }

    private static async Task WithDatabaseAsync(
        Func<string, TestExecutionContext, TestClock, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceValidation_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        var execution = new TestExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        var clock = new TestClock();
        await using var setup = CreateContext(
            connectionString,
            execution,
            clock);
        try
        {
            await setup.Database.MigrateAsync();
            await action(connectionString, execution, clock);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static SpaceContext CreateContext(
        string connectionString,
        TestExecutionContext execution,
        TestClock clock)
    {
        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(
                    SpaceContext.MigrationsHistoryTable))
            .Options;
        return new SpaceContext(options, execution, clock);
    }

    private sealed record SeededCandidate(
        SpaceModel Model,
        SpaceModelVersion Version,
        SpaceRackRevision Rack);

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId,
        Guid CorrelationId) :
        ISpaceExecutionContext,
        ISpaceCorrelationContext;

    private sealed class TestProfileProvider :
        ISpaceValidationProfileProvider
    {
        private static readonly SpaceValidationProfile Profile =
            SpaceValidationProfile.Create(
                "cp6-wms-v1",
                30,
                "^[A-Za-z0-9][A-Za-z0-9._/-]{0,29}$",
                100_000);

        public Task<SpaceValidationProfile> GetProfileAsync(
            Guid tenantId,
            Guid siteId,
            Guid correlationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Profile);
    }

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow { get; } = DateTime.UtcNow;
    }

    private sealed class TestAccessEvaluator(Guid allowedSiteId) :
        ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
            if (siteId != allowedSiteId)
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.TenantScopeDenied,
                    403,
                    "Site denied.");
            }
        }
    }
}
