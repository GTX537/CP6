using System.Data;
using System.Data.Common;
using CP6.Core.Persistence;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceValidationSqlServerTests(SpaceRelationalFixture database, ITestOutputHelper output)
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

            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var workers = new[] {
                first.RequestValidationAsync(versionId, budget.Token),
                second.RequestValidationAsync(versionId, budget.Token) };
            CreateSpaceValidationResponse[] results;
            try
            {
                results = await Task.WhenAll(workers).WaitAsync(budget.Token);
            }
            finally
            {
                await budget.CancelAsync();
                try { await Task.WhenAll(workers); }
                catch { /* The primary await retains the failure; dispose happens only after both requests settle. */ }
            }

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

    [SqlServerFact]
    public async Task Concurrent_waiter_recovers_native_conflict_and_reuses_validation()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            SeededCandidate graph;
            await using (var seed = CreateContext(connectionString, execution, clock))
                graph = await SeedCandidateAsync(seed, completeRack: true);

            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var profileGate = new WaitingProfileProvider();
            var waiter = new ValidationLockWaiter();
            var failures = new ValidationSerializationObserver(output.WriteLine);
            await using var firstContext = CreateContext(connectionString, execution, clock);
            await using var secondContext = CreateContext(connectionString, execution, clock, waiter, failures);
            var first = NewValidationService(firstContext, execution, clock, graph.Model.SiteId, profileGate);
            var second = NewValidationService(secondContext, execution, clock, graph.Model.SiteId);
            Task<CreateSpaceValidationResponse>? firstRequest = null;
            Task<CreateSpaceValidationResponse>? secondRequest = null;
            try
            {
                firstRequest = first.RequestValidationAsync(graph.Version.Id, budget.Token);
                await profileGate.Entered.Task.WaitAsync(budget.Token);
                secondRequest = second.RequestValidationAsync(graph.Version.Id, budget.Token);
                if (await Task.WhenAny(waiter.Waiting.Task, secondRequest).WaitAsync(budget.Token) == secondRequest)
                    await secondRequest;
                await waiter.Waiting.Task.WaitAsync(budget.Token);
                profileGate.Release();
                var results = await Task.WhenAll(firstRequest, secondRequest).WaitAsync(budget.Token);
                Assert.Equal(results[0].Validation.Id, results[1].Validation.Id);
                Assert.Single(results, value => value.Reused);
                if (secondContext.Database.IsNpgsql())
                    Assert.True(failures.SerializationFailures > 0, "The coordinated PostgreSQL waiter must observe actual native 40001 before recovering.");
            }
            finally
            {
                profileGate.Release();
                await budget.CancelAsync();
                foreach (var request in new[] { firstRequest, secondRequest })
                {
                    if (request is null) continue;
                    try { await request; }
                    catch { /* Preserve the primary await failure and drain both owned requests. */ }
                }
            }

            await using var verify = CreateContext(connectionString, execution, clock);
            Assert.Single(await verify.ValidationRuns.ToListAsync());
            Assert.Single(await verify.Jobs.Where(value => value.JobType == SpaceJobType.Validate).ToListAsync());
            Assert.Equal(SpaceVersionStatus.Validating,
                await verify.Versions.Where(value => value.Id == graph.Version.Id).Select(value => value.Status).SingleAsync());
        });
    }

    [SqlServerFact]
    public async Task Validation_request_preserves_unsaved_caller_changes()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            SeededCandidate graph;
            await using (var seed = CreateContext(connectionString, execution, clock))
                graph = await SeedCandidateAsync(seed, completeRack: true);
            await using var context = CreateContext(connectionString, execution, clock);
            var pending = SpaceModel.Create(execution.TenantId, Guid.NewGuid());
            context.Add(pending);
            var service = NewValidationService(context, execution, clock, graph.Model.SiteId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestValidationAsync(graph.Version.Id));
            Assert.Equal(EntityState.Added, context.Entry(pending).State);
            Assert.Null(context.Database.CurrentTransaction);
            await using var verify = CreateContext(connectionString, execution, clock);
            Assert.Single(await verify.Models.ToListAsync());
            Assert.Empty(await verify.ValidationRuns.ToListAsync());
            Assert.Empty(await verify.Jobs.ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task Validation_request_preserves_caller_owned_transaction()
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            SeededCandidate graph;
            await using (var seed = CreateContext(connectionString, execution, clock))
                graph = await SeedCandidateAsync(seed, completeRack: true);
            await using var context = CreateContext(connectionString, execution, clock);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var service = NewValidationService(context, execution, clock, graph.Model.SiteId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestValidationAsync(graph.Version.Id));
            Assert.Same(transaction, context.Database.CurrentTransaction);
            context.Add(SpaceModel.Create(execution.TenantId, Guid.NewGuid()));
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
            await using var verify = CreateContext(connectionString, execution, clock);
            Assert.Single(await verify.Models.ToListAsync());
            Assert.Empty(await verify.ValidationRuns.ToListAsync());
            Assert.Empty(await verify.Jobs.ToListAsync());
        });
    }

    [SqlServerFact]
    public Task Validation_unknown_failure_is_not_retried() => ValidationRecoveryStopsAsync("unknown", 1);

    [SqlServerFact]
    public Task Validation_recovery_honors_cancellation() => ValidationRecoveryStopsAsync("cancel", 1);

    [SqlServerFact]
    public Task Validation_recovery_is_bounded_and_rolls_back() => ValidationRecoveryStopsAsync("exhaust", 3);

    private async Task ValidationRecoveryStopsAsync(string mode, int expectedAttempts)
    {
        await WithDatabaseAsync(async (connectionString, execution, clock) =>
        {
            SeededCandidate graph;
            await using (var seed = CreateContext(connectionString, execution, clock))
                graph = await SeedCandidateAsync(seed, completeRack: true);
            using var cancellation = new CancellationTokenSource();
            var failure = new ValidationSaveFailure(mode, cancellation);
            await using var context = CreateContext(connectionString, execution, clock, failure);
            var service = NewValidationService(context, execution, clock, graph.Model.SiteId);
            if (mode == "unknown")
            {
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.RequestValidationAsync(graph.Version.Id, cancellation.Token));
                Assert.Same(failure.UnknownFailure, exception);
            }
            else if (mode == "cancel")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => service.RequestValidationAsync(graph.Version.Id, cancellation.Token));
            else
                await Assert.ThrowsAsync<DatabaseResourceLockDeadlockException>(
                    () => service.RequestValidationAsync(graph.Version.Id, cancellation.Token));
            Assert.Equal(expectedAttempts, failure.Attempts);
            Assert.Null(context.Database.CurrentTransaction);
            await using var verify = CreateContext(connectionString, execution, clock);
            Assert.Empty(await verify.ValidationRuns.ToListAsync());
            Assert.Empty(await verify.Jobs.ToListAsync());
            Assert.Equal(SpaceVersionStatus.Draft,
                await verify.Versions.Where(value => value.Id == graph.Version.Id).Select(value => value.Status).SingleAsync());
        });
    }

    private async Task ProcessNextAsync(
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
        Guid allowedSiteId,
        ISpaceValidationProfileProvider? profiles = null) =>
        new(
            context,
            execution,
            clock,
            new TestAccessEvaluator(allowedSiteId),
            profiles ?? new TestProfileProvider(),
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

    private async Task WithDatabaseAsync(
        Func<string, TestExecutionContext, TestClock, Task> action)
    {
        if (SpaceRelationalFixture.IsSelected)
        {
            database.WriteSetupEvidence(output);
            await action(database.ConnectionString,
                new TestExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), new TestClock());
            return;
        }

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

    private SpaceContext CreateContext(
        string connectionString,
        TestExecutionContext execution,
        TestClock clock,
        params IInterceptor[] interceptors)
    {
        if (SpaceRelationalFixture.IsSelected)
        {
            Assert.True(string.Equals(database.ConnectionString, connectionString, StringComparison.Ordinal),
                "Selected Space contexts must use the fixture-owned connection.");
            return database.CreateSpaceContext(execution, clock,
                [new SpaceNativeFailureObserver(database.Database.Provider, output.WriteLine, "validation"), .. interceptors]);
        }

        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(
                    SpaceContext.MigrationsHistoryTable))
            .AddInterceptors(interceptors)
            .Options;
        return new SpaceContext(options, execution, clock);
    }

    private sealed class WaitingProfileProvider : ISpaceValidationProfileProvider
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => released.TrySetResult();
        public async Task<SpaceValidationProfile> GetProfileAsync(Guid tenantId, Guid siteId, Guid correlationId,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
            return await new TestProfileProvider().GetProfileAsync(tenantId, siteId, correlationId, cancellationToken);
        }
    }

    private sealed class ValidationLockWaiter : DbTransactionInterceptor
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool started;
        public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (!started)
            {
                started = true;
                if (eventData.Context!.Database.IsNpgsql())
                {
                    // Establish a real PostgreSQL Serializable snapshot before the
                    // first request is released; the production lock is not replaced.
                    await using var command = connection.CreateCommand();
                    command.Transaction = result;
                    command.CommandText = "SELECT pg_catalog.txid_current_snapshot()::text;";
                    await command.ExecuteScalarAsync(cancellationToken);
                }
                Waiting.TrySetResult();
            }
            return result;
        }
    }

    private sealed class ValidationSerializationObserver(Action<string> report) : SaveChangesInterceptor
    {
        public int SerializationFailures { get; private set; }
        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            var failure = DatabaseFailureClassifier.Classify(eventData.Exception);
            if (failure.SqlState == "40001") SerializationFailures++;
            report($"BUG155 coordinated native failure: SQLSTATE={failure.SqlState ?? "none"}; Kind={failure.Kind}.");
            return Task.CompletedTask;
        }
    }

    // These bounded-recovery controls inject failures before any write; they are
    // distinct from the coordinated test's actual PostgreSQL serialization error.
    private sealed class ValidationSaveFailure(string mode, CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public int Attempts { get; private set; }
        public InvalidOperationException UnknownFailure { get; } = new("BUG155 controlled unknown failure");
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (mode == "unknown") throw UnknownFailure;
            if (mode == "cancel") cancellation.Cancel();
            throw new DatabaseResourceLockDeadlockException(DatabaseProvider.SqlServer);
        }
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
