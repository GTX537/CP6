using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceJobSqlServerTests
{
    private static readonly DateTime Now =
        new(2026, 7, 26, 14, 0, 0, DateTimeKind.Utc);

    private const string InputHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [SqlServerFact]
    public async Task Two_workers_competing_for_one_job_create_one_attempt()
    {
        var tenantId = Guid.NewGuid();

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                await SeedJobAsync(connectionString, tenantId, NewJob(tenantId));
                await using var first = CreateContext(
                    connectionString,
                    tenantId,
                    new MutableClock(Now));
                await using var second = CreateContext(
                    connectionString,
                    tenantId,
                    new MutableClock(Now));
                var firstStore = new EfSpaceJobLeaseStore(
                    first,
                    new MutableClock(Now));
                var secondStore = new EfSpaceJobLeaseStore(
                    second,
                    new MutableClock(Now));

                var claims = await Task.WhenAll(
                    firstStore.TryClaimNextAsync(
                        "worker-a",
                        "parser-v1",
                        TimeSpan.FromSeconds(60)),
                    secondStore.TryClaimNextAsync(
                        "worker-b",
                        "parser-v1",
                        TimeSpan.FromSeconds(60)));

                var lease = Assert.Single(claims, claim => claim is not null);
                Assert.Equal(1, lease!.AttemptNo);

                await using var verify = CreateContext(
                    connectionString,
                    tenantId,
                    new MutableClock(Now));
                Assert.Single(await verify.JobAttempts.ToListAsync());
                var job = await verify.Jobs.SingleAsync();
                Assert.Equal(SpaceJobStatus.Running, job.Status);
                Assert.Equal(lease.AttemptId, job.ActiveAttemptId);
            });
    }

    [SqlServerFact]
    public async Task Expired_lease_takeover_abandons_old_attempt_and_fences_old_worker()
    {
        var tenantId = Guid.NewGuid();
        var clockA = new MutableClock(Now);
        var clockB = new MutableClock(Now);

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                await SeedJobAsync(connectionString, tenantId, NewJob(tenantId));
                await using var contextA = CreateContext(
                    connectionString,
                    tenantId,
                    clockA);
                await using var contextB = CreateContext(
                    connectionString,
                    tenantId,
                    clockB);
                var storeA = new EfSpaceJobLeaseStore(contextA, clockA);
                var storeB = new EfSpaceJobLeaseStore(contextB, clockB);
                var first = Assert.IsType<SpaceJobLease>(
                    await storeA.TryClaimNextAsync(
                        "worker-a",
                        "parser-v1",
                        TimeSpan.FromSeconds(60)));

                clockA.UtcNow = Now.AddSeconds(61);
                clockB.UtcNow = Now.AddSeconds(61);
                var second = Assert.IsType<SpaceJobLease>(
                    await storeB.TryClaimNextAsync(
                        "worker-b",
                        "parser-v1",
                        TimeSpan.FromSeconds(60)));

                Assert.Equal(2, second.AttemptNo);
                await Assert.ThrowsAsync<SpaceJobLeaseLostException>(
                    () => storeA.ReportProgressAsync(
                        first,
                        1,
                        10,
                        "Stale"));
                await storeB.ReportProgressAsync(
                    second,
                    1,
                    10,
                    "Recovered");

                await using var verify = CreateContext(
                    connectionString,
                    tenantId,
                    clockB);
                var attempts = await verify.JobAttempts
                    .OrderBy(attempt => attempt.AttemptNo)
                    .ToListAsync();
                Assert.Equal(SpaceJobAttemptOutcome.Abandoned, attempts[0].Outcome);
                Assert.Equal(SpaceJobAttemptOutcome.Running, attempts[1].Outcome);
                Assert.Equal(
                    second.AttemptId,
                    (await verify.Jobs.SingleAsync()).ActiveAttemptId);
            });
    }

    [SqlServerFact]
    public async Task Successful_checkpoint_is_reused_only_for_matching_input_and_processor()
    {
        var tenantId = Guid.NewGuid();
        var clock = new MutableClock(Now);

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                await SeedJobAsync(connectionString, tenantId, NewJob(tenantId));
                await using var context = CreateContext(
                    connectionString,
                    tenantId,
                    clock);
                var store = new EfSpaceJobLeaseStore(context, clock);
                var first = Assert.IsType<SpaceJobLease>(
                    await store.TryClaimNextAsync(
                        "worker-a",
                        "parser-v1",
                        TimeSpan.FromSeconds(60)));
                var started = await store.StartStepAsync(
                    first,
                    1,
                    "AcquireInput");
                var fenced = await store.CompleteStepAsync(
                    started.Lease,
                    started.StepId,
                    """{"artifactId":"input"}""",
                    new string('b', 64));
                await store.FailJobAsync(
                    fenced,
                    SpaceJobFailureKind.Transient,
                    "OBJECT_STORE_TIMEOUT",
                    "Object storage timed out.");

                clock.UtcNow = Now.AddSeconds(5);
                var second = Assert.IsType<SpaceJobLease>(
                    await store.TryClaimNextAsync(
                        "worker-b",
                        "parser-v1",
                        TimeSpan.FromSeconds(60)));
                var reusable = await store.FindReusableCheckpointAsync(
                    second,
                    "AcquireInput");

                Assert.NotNull(reusable);
                Assert.Equal(started.StepId, reusable!.StepId);
                Assert.Equal(new string('b', 64), reusable.OutputHash);
            });
    }

    [SqlServerFact]
    public async Task Cancellation_is_observed_at_safe_checkpoint_without_losing_lease()
    {
        var tenantId = Guid.NewGuid();
        var workerClock = new MutableClock(Now);

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                await SeedJobAsync(connectionString, tenantId, NewJob(tenantId));
                await using var workerContext = CreateContext(
                    connectionString,
                    tenantId,
                    workerClock);
                var store = new EfSpaceJobLeaseStore(workerContext, workerClock);
                var lease = Assert.IsType<SpaceJobLease>(
                    await store.TryClaimNextAsync(
                        "worker-a",
                        "parser-v1",
                        TimeSpan.FromSeconds(60)));

                await using (var requestContext = CreateContext(
                                 connectionString,
                                 tenantId,
                                 new MutableClock(Now.AddSeconds(1))))
                {
                    var job = await requestContext.Jobs.SingleAsync();
                    job.RequestCancellation(
                        Guid.NewGuid(),
                        Now.AddSeconds(1));
                    await requestContext.SaveChangesAsync();
                }

                workerClock.UtcNow = Now.AddSeconds(2);
                await store.AcknowledgeCancellationAsync(lease);

                await using var verify = CreateContext(
                    connectionString,
                    tenantId,
                    workerClock);
                Assert.Equal(
                    SpaceJobStatus.Cancelled,
                    (await verify.Jobs.SingleAsync()).Status);
                Assert.Equal(
                    SpaceJobAttemptOutcome.Cancelled,
                    (await verify.JobAttempts.SingleAsync()).Outcome);
            });
    }

    [SqlServerFact]
    public async Task Final_expired_attempt_is_deadlettered_instead_of_claimed_again()
    {
        var tenantId = Guid.NewGuid();
        var clock = new MutableClock(Now);

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                await SeedJobAsync(
                    connectionString,
                    tenantId,
                    NewJob(tenantId, maxAttempts: 1));
                await using var context = CreateContext(
                    connectionString,
                    tenantId,
                    clock);
                var store = new EfSpaceJobLeaseStore(context, clock);
                await store.TryClaimNextAsync(
                    "worker-a",
                    "parser-v1",
                    TimeSpan.FromSeconds(60));

                clock.UtcNow = Now.AddSeconds(61);
                Assert.Null(await store.TryClaimNextAsync(
                    "worker-b",
                    "parser-v1",
                    TimeSpan.FromSeconds(60)));

                context.ChangeTracker.Clear();
                Assert.Equal(
                    SpaceJobStatus.DeadLetter,
                    (await context.Jobs.SingleAsync()).Status);
                Assert.Equal(
                    SpaceJobAttemptOutcome.Abandoned,
                    (await context.JobAttempts.SingleAsync()).Outcome);
            });
    }

    [SqlServerFact]
    public async Task Active_business_key_is_unique_and_tenant_scoped()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var businessKey = new string('c', 64);

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                await using (var contextA = CreateContext(
                                 connectionString,
                                 tenantA,
                                 new MutableClock(Now)))
                {
                    contextA.Jobs.Add(NewJob(tenantA, businessKey: businessKey));
                    await contextA.SaveChangesAsync();
                    contextA.Jobs.Add(NewJob(tenantA, businessKey: businessKey));
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => contextA.SaveChangesAsync());
                }

                await using var contextB = CreateContext(
                    connectionString,
                    tenantB,
                    new MutableClock(Now));
                contextB.Jobs.Add(NewJob(tenantB, businessKey: businessKey));
                await contextB.SaveChangesAsync();
                Assert.Single(await contextB.Jobs.ToListAsync());
                Assert.Equal(
                    2,
                    await contextB.Jobs.IgnoreQueryFilters().CountAsync());
        });
    }

    [SqlServerFact]
    public async Task Concurrent_duplicate_delivery_returns_one_active_job()
    {
        var tenantId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var request = new SpaceJobEnqueueRequest(
            SpaceJobType.CadParse,
            SpaceJobSubjectType.ModelSource,
            subjectId,
            InputHash,
            "parser-v1",
            "mapping-v1");

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                await using var first = CreateContext(
                    connectionString,
                    tenantId,
                    new MutableClock(Now));
                await using var second = CreateContext(
                    connectionString,
                    tenantId,
                    new MutableClock(Now));
                var firstCoordinator = new SpaceJobCoordinator(
                    new TestExecutionContext(tenantId, Guid.NewGuid()),
                    new MutableClock(Now),
                    new EfSpaceJobQueue(first));
                var secondCoordinator = new SpaceJobCoordinator(
                    new TestExecutionContext(tenantId, Guid.NewGuid()),
                    new MutableClock(Now),
                    new EfSpaceJobQueue(second));

                var results = await Task.WhenAll(
                    firstCoordinator.EnqueueAsync(request),
                    secondCoordinator.EnqueueAsync(request));

                Assert.Equal(results[0].Job.Id, results[1].Job.Id);
                Assert.Single(results, result => result.Reused);
                await using var verify = CreateContext(
                    connectionString,
                    tenantId,
                    new MutableClock(Now));
                Assert.Single(await verify.Jobs.ToListAsync());
            });
    }

    [SqlServerFact]
    public async Task Attempt_foreign_key_and_query_filter_reject_cross_tenant_job_access()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var job = NewJob(tenantA);

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                await SeedJobAsync(connectionString, tenantA, job);
                await using var contextB = CreateContext(
                    connectionString,
                    tenantB,
                    new MutableClock(Now));

                Assert.Empty(await contextB.Jobs.ToListAsync());
                var attemptId = Guid.NewGuid();
                var sql = $"""
                    INSERT INTO [Space_JobAttempt]
                        ([Id], [JobId], [AttemptNo], [WorkerId], [StartedAtUtc],
                         [Outcome], [InputHash], [ProcessorVersion], [TenantId],
                         [CreatedAtUtc], [IsDeleted])
                    VALUES
                        ('{attemptId}', '{job.Id}', 1, 'forbidden-worker', SYSUTCDATETIME(),
                         0, '{InputHash}', 'parser-v1', '{tenantB}',
                         SYSUTCDATETIME(), 0)
                    """;

                await Assert.ThrowsAsync<SqlException>(
                    () => contextB.Database.ExecuteSqlRawAsync(sql));
            });
    }

    [SqlServerFact]
    public async Task Progress_query_returns_attempts_and_open_issue_counts()
    {
        var tenantId = Guid.NewGuid();
        var clock = new MutableClock(Now);

        await WithDatabaseAsync(
            async (_, connectionString) =>
            {
                var seeded = NewJob(tenantId);
                await SeedJobAsync(connectionString, tenantId, seeded);
                await using var context = CreateContext(
                    connectionString,
                    tenantId,
                    clock);
                var store = new EfSpaceJobLeaseStore(context, clock);
                var lease = Assert.IsType<SpaceJobLease>(
                    await store.TryClaimNextAsync(
                        "worker-a",
                        "parser-v1",
                        TimeSpan.FromSeconds(60)));
                await store.ReportProgressAsync(
                    lease,
                    3,
                    10,
                    "Convert");
                context.Issues.AddRange(
                    SpaceModelIssue.Create(
                        tenantId,
                        null,
                        null,
                        seeded.Id,
                        SpaceIssueSeverity.Warning,
                        "CAD_LAYER_AMBIGUOUS"),
                    SpaceModelIssue.Create(
                        tenantId,
                        null,
                        null,
                        seeded.Id,
                        SpaceIssueSeverity.Blocking,
                        "CAD_UNIT_REQUIRED"));
                await context.SaveChangesAsync();

                var snapshot = await new EfSpaceJobProgressReader(context)
                    .GetAsync(tenantId, seeded.Id);

                Assert.NotNull(snapshot);
                Assert.Equal(3, snapshot!.ProgressDone);
                Assert.Equal(10, snapshot.ProgressTotal);
                Assert.Equal(1, snapshot.OpenWarningCount);
                Assert.Equal(1, snapshot.OpenBlockingCount);
                Assert.Single(snapshot.Attempts);
            });
    }

    private static SpaceJob NewJob(
        Guid tenantId,
        int maxAttempts = 5,
        string? businessKey = null) =>
        SpaceJob.CreateQueued(
            tenantId,
            SpaceJobType.CadParse,
            SpaceJobSubjectType.ModelSource,
            Guid.NewGuid(),
            businessKey ?? new string('d', 64),
            InputHash,
            10,
            maxAttempts,
            Guid.NewGuid(),
            Now,
            Guid.NewGuid());

    private static async Task SeedJobAsync(
        string connectionString,
        Guid tenantId,
        SpaceJob job)
    {
        await using var context = CreateContext(
            connectionString,
            tenantId,
            new MutableClock(Now));
        context.Jobs.Add(job);
        await context.SaveChangesAsync();
    }

    private static async Task WithDatabaseAsync(
        Func<SpaceContext, string, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceJob_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;

        await using var context = CreateContext(
            connectionString,
            Guid.NewGuid(),
            new MutableClock(Now));
        try
        {
            await context.Database.MigrateAsync();
            await action(context, connectionString);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static SpaceContext CreateContext(
        string connectionString,
        Guid tenantId,
        ISpaceClock clock)
    {
        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(SpaceContext.MigrationsHistoryTable))
            .Options;
        return new SpaceContext(
            options,
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            clock);
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId)
        : ISpaceExecutionContext;

    private sealed class MutableClock : ISpaceClock
    {
        public MutableClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; set; }
    }
}
