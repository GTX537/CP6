using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceJobProcessorPersistenceTests
{
    private static readonly DateTime Now =
        new(2026, 7, 30, 20, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Default_registration_exposes_eleven_explicit_processors()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISpaceExecutionContext>(
            _ => new TestExecutionContext(
                Guid.NewGuid(),
                Guid.NewGuid()));
        services.AddSpaceDesignV1Persistence(
            "Server=(localdb)\\MSSQLLocalDB;Database=unused;" +
            "Trusted_Connection=True;TrustServerCertificate=True");
        services.AddScoped<ISpacePublishJobExecutor, RecordingExecutor>();
        services.AddScoped<
            ISpaceHistoricalRepublishJobExecutor,
            RecordingExecutor>();
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType ==
                typeof(ISpaceExcelCadMatchService));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var processors = scope.ServiceProvider
            .GetServices<ISpaceJobProcessor>()
            .OrderBy(processor => processor.JobType)
            .ToArray();

        Assert.Equal(11, processors.Length);
        Assert.IsType<SpaceCadParseJobProcessor>(processors[0]);
        Assert.IsType<SpaceExcelPreflightJobProcessor>(processors[1]);
        Assert.IsType<SpaceImportJobProcessor>(processors[2]);
        Assert.IsType<SpaceValidationJobProcessor>(processors[3]);
        Assert.IsType<SpaceBuildSceneJobProcessor>(processors[4]);
        Assert.IsType<SpacePublishJobProcessor>(processors[5]);
        Assert.IsType<SpacePublishReconciliationJobProcessor>(processors[6]);
        Assert.IsType<SpaceGenerationApplyJobProcessor>(processors[7]);
        Assert.IsType<SpaceAiRetentionJobProcessor>(processors[8]);
        Assert.IsType<SpaceHistoricalRepublishJobProcessor>(processors[9]);
        Assert.IsType<SpaceExcelCadMatchJobProcessor>(processors[10]);
        Assert.IsType<UnavailableSpaceCadParseProvider>(
            scope.ServiceProvider.GetRequiredService<ISpaceCadParseProvider>());
        Assert.IsType<SpaceCadParseJobStepExecutor>(
            scope.ServiceProvider.GetRequiredService<
                ISpaceCadParseJobStepExecutor>());
        Assert.IsType<SpaceExcelCadMatchJobStepExecutor>(
            scope.ServiceProvider.GetRequiredService<
                ISpaceExcelCadMatchJobStepExecutor>());
        Assert.IsType<UnavailableSpaceImportJobStepExecutor>(
            scope.ServiceProvider.GetRequiredService<
                ISpaceImportJobStepExecutor>());
        Assert.IsType<UnavailableSpaceBuildSceneJobStepExecutor>(
            scope.ServiceProvider.GetRequiredService<
                ISpaceBuildSceneJobStepExecutor>());
        Assert.IsType<SpaceGenerationApplyStepExecutor>(
            scope.ServiceProvider.GetRequiredService<
                ISpaceGenerationApplyStepExecutor>());
        Assert.IsType<NoOpSpaceAiApplyFaultInjector>(
            scope.ServiceProvider.GetRequiredService<
                ISpaceAiApplyFaultInjector>());
        Assert.IsType<ClosedSpaceAiRetentionAuthorization>(
            scope.ServiceProvider.GetRequiredService<
                ISpaceAiRetentionAuthorization>());
        Assert.IsType<SpaceJobProcessorRunner>(
            scope.ServiceProvider.GetRequiredService<
                ISpaceJobProcessorRunner>());
    }

    [SqlServerFact]
    public async Task Filtered_runner_persists_only_import_and_build_scene_steps()
    {
        var tenantId = Guid.NewGuid();
        var clock = new MutableClock(Now);

        await WithDatabaseAsync(
            tenantId,
            clock,
            async context =>
            {
                var import = NewJob(
                    tenantId,
                    SpaceJobType.Import,
                    SpaceJobSubjectType.ModelSource,
                    'a');
                var buildScene = NewJob(
                    tenantId,
                    SpaceJobType.BuildScene,
                    SpaceJobSubjectType.ModelVersion,
                    'b');
                var fileScan = NewJob(
                    tenantId,
                    SpaceJobType.FileScan,
                    SpaceJobSubjectType.File,
                    'c');
                context.Jobs.AddRange(import, buildScene, fileScan);
                await context.SaveChangesAsync();

                var executor = new RecordingExecutor();
                var runner = Runner(context, clock, executor);
                Assert.True(await runner.RunNextAsync(
                    SpaceJobType.Import,
                    "worker-import"));
                Assert.True(await runner.RunNextAsync(
                    SpaceJobType.BuildScene,
                    "worker-build-scene"));

                context.ChangeTracker.Clear();
                var jobs = await context.Jobs
                    .OrderBy(job => job.JobType)
                    .ToListAsync();
                Assert.Equal(
                    SpaceJobStatus.Queued,
                    jobs.Single(job =>
                        job.JobType == SpaceJobType.FileScan).Status);
                Assert.Equal(
                    SpaceJobStatus.Succeeded,
                    jobs.Single(job =>
                        job.JobType == SpaceJobType.Import).Status);
                Assert.Equal(
                    SpaceJobStatus.Succeeded,
                    jobs.Single(job =>
                        job.JobType == SpaceJobType.BuildScene).Status);

                var attempts = await context.JobAttempts
                    .OrderBy(attempt => attempt.ProcessorVersion)
                    .ToListAsync();
                Assert.Equal(2, attempts.Count);
                Assert.Contains(
                    attempts,
                    attempt => attempt.ProcessorVersion ==
                        SpaceImportJobProcessor.Version);
                Assert.Contains(
                    attempts,
                    attempt => attempt.ProcessorVersion ==
                        SpaceBuildSceneJobProcessor.Version);

                var steps = await context.JobSteps.ToListAsync();
                Assert.Equal(18, steps.Count);
                Assert.All(
                    steps,
                    step => Assert.Equal(
                        SpaceJobStepStatus.Succeeded,
                        step.Status));
                Assert.Equal(
                    SpaceImportJobSteps.All,
                    steps
                        .Where(step =>
                            attempts.Single(attempt =>
                                attempt.Id == step.AttemptId)
                                .ProcessorVersion ==
                            SpaceImportJobProcessor.Version)
                        .OrderBy(step => step.StepNo)
                        .Select(step => step.StepCode));
                Assert.Equal(
                    SpaceBuildSceneJobSteps.All,
                    steps
                        .Where(step =>
                            attempts.Single(attempt =>
                                attempt.Id == step.AttemptId)
                                .ProcessorVersion ==
                            SpaceBuildSceneJobProcessor.Version)
                        .OrderBy(step => step.StepNo)
                        .Select(step => step.StepCode));
            });
    }

    [SqlServerFact]
    public async Task Retry_reuses_only_matching_successful_checkpoints()
    {
        var tenantId = Guid.NewGuid();
        var clock = new MutableClock(Now);

        await WithDatabaseAsync(
            tenantId,
            clock,
            async context =>
            {
                context.Jobs.Add(
                    NewJob(
                        tenantId,
                        SpaceJobType.Import,
                        SpaceJobSubjectType.ModelSource,
                        'd'));
                await context.SaveChangesAsync();

                var failing = new RecordingExecutor
                {
                    Handler = (execution, _) =>
                        execution.StepCode ==
                        SpaceImportJobSteps.ParseCadIr
                            ? throw new SpaceJobProcessingException(
                                SpaceJobFailureKind.Transient,
                                SpaceErrorCodes.ParseFailed,
                                "The CAD parser was temporarily unavailable.")
                            : Task.FromResult(
                                Output(execution.StepCode)),
                };
                await Runner(context, clock, failing)
                    .RunNextAsync(
                        SpaceJobType.Import,
                        "worker-first");

                context.ChangeTracker.Clear();
                var queued = await context.Jobs.SingleAsync();
                Assert.Equal(SpaceJobStatus.Queued, queued.Status);
                Assert.Equal(
                    Now.AddSeconds(5),
                    queued.NextAttemptAtUtc);

                clock.UtcNow = Now.AddSeconds(5);
                var succeeding = new RecordingExecutor();
                await Runner(context, clock, succeeding)
                    .RunNextAsync(
                        SpaceJobType.Import,
                        "worker-second");

                context.ChangeTracker.Clear();
                Assert.Equal(
                    SpaceJobStatus.Succeeded,
                    (await context.Jobs.SingleAsync()).Status);
                var attempts = await context.JobAttempts
                    .OrderBy(attempt => attempt.AttemptNo)
                    .ToListAsync();
                Assert.Equal(2, attempts.Count);
                Assert.Equal(
                    SpaceJobAttemptOutcome.Failed,
                    attempts[0].Outcome);
                Assert.Equal(
                    SpaceJobAttemptOutcome.Succeeded,
                    attempts[1].Outcome);

                var firstSteps = await context.JobSteps
                    .Where(step =>
                        step.AttemptId == attempts[0].Id)
                    .OrderBy(step => step.StepNo)
                    .ToListAsync();
                Assert.Equal(3, firstSteps.Count);
                Assert.Equal(
                    [
                        SpaceJobStepStatus.Succeeded,
                        SpaceJobStepStatus.Succeeded,
                        SpaceJobStepStatus.Failed,
                    ],
                    firstSteps.Select(step => step.Status));

                var secondSteps = await context.JobSteps
                    .Where(step =>
                        step.AttemptId == attempts[1].Id)
                    .OrderBy(step => step.StepNo)
                    .ToListAsync();
                Assert.Equal(6, secondSteps.Count);
                Assert.Equal(
                    SpaceJobStepStatus.Reused,
                    secondSteps[0].Status);
                Assert.Equal(
                    SpaceJobStepStatus.Reused,
                    secondSteps[1].Status);
                Assert.All(
                    secondSteps.Skip(2),
                    step => Assert.Equal(
                        SpaceJobStepStatus.Succeeded,
                        step.Status));
                Assert.DoesNotContain(
                    SpaceImportJobSteps.VerifySourceSafe,
                    succeeding.StepCodes);
                Assert.DoesNotContain(
                    SpaceImportJobSteps.ConvertCad,
                    succeeding.StepCodes);
                Assert.Contains(
                    SpaceImportJobSteps.ParseCadIr,
                    succeeding.StepCodes);
            });
    }

    [SqlServerFact]
    public async Task Explicit_retry_reuses_matching_checkpoints_from_retry_lineage()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var clock = new MutableClock(Now);

        await WithDatabaseAsync(
            tenantId,
            clock,
            async context =>
            {
                var original = NewJob(
                    tenantId,
                    SpaceJobType.Import,
                    SpaceJobSubjectType.ModelSource,
                    'e');
                context.Jobs.Add(original);
                await context.SaveChangesAsync();
                var failing = new RecordingExecutor
                {
                    Handler = (execution, _) =>
                        execution.StepCode == SpaceImportJobSteps.ParseCadIr
                            ? throw new SpaceJobProcessingException(
                                SpaceJobFailureKind.Resource,
                                SpaceErrorCodes.JobProcessorUnavailable,
                                "The converter is temporarily unavailable.")
                            : Task.FromResult(Output(execution.StepCode)),
                };
                await Runner(context, clock, failing).RunNextAsync(
                    SpaceJobType.Import,
                    "worker-original");

                context.ChangeTracker.Clear();
                original = await context.Jobs.SingleAsync();
                Assert.Equal(SpaceJobStatus.Failed, original.Status);
                var retry = original.CreateExplicitRetry(
                    original.BusinessKey,
                    original.InputHash,
                    actorId,
                    Now.AddMinutes(1),
                    Guid.NewGuid(),
                    original.PayloadJson);
                context.Jobs.Add(retry);
                await context.SaveChangesAsync();

                clock.UtcNow = Now.AddMinutes(1);
                var succeeding = new RecordingExecutor();
                await Runner(context, clock, succeeding).RunNextAsync(
                    SpaceJobType.Import,
                    "worker-retry");

                context.ChangeTracker.Clear();
                retry = await context.Jobs.SingleAsync(job => job.Id == retry.Id);
                Assert.Equal(SpaceJobStatus.Succeeded, retry.Status);
                var retryAttempt = await context.JobAttempts.SingleAsync(
                    attempt => attempt.JobId == retry.Id);
                var retrySteps = await context.JobSteps
                    .Where(step => step.AttemptId == retryAttempt.Id)
                    .OrderBy(step => step.StepNo)
                    .ToListAsync();
                Assert.Equal(SpaceJobStepStatus.Reused, retrySteps[0].Status);
                Assert.Equal(SpaceJobStepStatus.Reused, retrySteps[1].Status);
                Assert.DoesNotContain(
                    SpaceImportJobSteps.VerifySourceSafe,
                    succeeding.StepCodes);
                Assert.DoesNotContain(
                    SpaceImportJobSteps.ConvertCad,
                    succeeding.StepCodes);
            });
    }

    private static SpaceJobProcessorRunner Runner(
        SpaceContext context,
        ISpaceClock clock,
        RecordingExecutor executor) =>
        new(
            new EfSpaceJobLeaseStore(context, clock),
            [
                new SpaceImportJobProcessor(executor),
                new SpaceBuildSceneJobProcessor(executor),
            ]);

    private static SpaceJob NewJob(
        Guid tenantId,
        SpaceJobType jobType,
        SpaceJobSubjectType subjectType,
        char hashCharacter) =>
        SpaceJob.CreateQueued(
            tenantId,
            jobType,
            subjectType,
            Guid.NewGuid(),
            new string(hashCharacter, 64),
            new string('f', 64),
            50,
            3,
            Guid.NewGuid(),
            Now,
            Guid.NewGuid());

    private static SpaceJobStepOutput Output(string stepCode) =>
        new(
            JsonSerializer.Serialize(new { stepCode }),
            Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(stepCode)))
                .ToLowerInvariant());

    private static async Task WithDatabaseAsync(
        Guid tenantId,
        ISpaceClock clock,
        Func<SpaceContext, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceE13S03_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        await using var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        SpaceContext.MigrationsHistoryTable))
                .Options,
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            clock);

        try
        {
            await context.Database.MigrateAsync();
            await action(context);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private sealed class RecordingExecutor
        : ISpaceImportJobStepExecutor,
          ISpaceBuildSceneJobStepExecutor,
          ISpacePublishJobExecutor,
          ISpaceHistoricalRepublishJobExecutor
    {
        public Func<
            SpaceJobStepExecution,
            CancellationToken,
            Task<SpaceJobStepOutput>> Handler
        { get; init; } =
            (execution, _) =>
                Task.FromResult(Output(execution.StepCode));

        public List<string> StepCodes { get; } = [];

        public async Task<SpaceJobStepOutput> ExecuteAsync(
            SpaceJobStepExecution execution,
            CancellationToken cancellationToken = default)
        {
            StepCodes.Add(execution.StepCode);
            return await Handler(execution, cancellationToken);
        }
    }

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId)
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
