using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.Persistence;
using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.IntegrationTests;

public sealed partial class SpaceVersionCloneSqlServerTests
{
    [SqlServerFact]
    public async Task Native_last_table_failure_rolls_back_snapshot_and_scheduled_retry_succeeds()
    {
        await WithDatabaseAsync(async (seed, execution, originalClock) =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var cancellationToken = deadline.Token;
            var clock = new CloneEdgeClock(originalClock.UtcNow);
            var (model, source) = await SeedPublishedAsync(seed, execution.ActorId, includeSnapshot: true);
            var sourceBefore = await ReadCloneEdgeSnapshotAsync(seed, source.Id, cancellationToken);
            Assert.Equal(12, sourceBefore.Counts.Length);
            Assert.All(sourceBefore.Counts, count => Assert.True(count > 0));
            var reservation = await new EfSpaceVersionCloneStore(seed, execution, clock).StartAsync(
                new SpaceVersionCloneRequest(model.Id, "Rollback then scheduled retry", Guid.NewGuid()), cancellationToken);
            var connectionString = seed.Database.GetConnectionString()!;
            var constraint = "CK_wp5_clone_last_" + Guid.NewGuid().ToString("N");
            var observer = new CloneEdgeCommandObserver();
            await using var worker = CreateCloneEdgeContext(connectionString, execution, clock, observer);
            await worker.Database.OpenConnectionAsync(cancellationToken);
            var backend = await CloneEdgeBackendAsync(worker, cancellationToken);
            var leases = new EfSpaceJobLeaseStore(worker, clock);
            var processor = new EfSpaceVersionCloneProcessor(worker, clock, leases);
            var installed = false;
            var nextAttemptAt = clock.UtcNow;
            try
            {
                await seed.Database.ExecuteSqlRawAsync(seed.Database.IsNpgsql()
                    ? $"ALTER TABLE public.\"Space_ElementAttribute\" ADD CONSTRAINT \"{constraint}\" CHECK (\"TenantId\" <> '{execution.TenantId:D}'::uuid OR \"ModelVersionId\" <> '{reservation.ModelVersionId:D}'::uuid) NOT VALID"
                    : $"ALTER TABLE [Space_ElementAttribute] WITH NOCHECK ADD CONSTRAINT [{constraint}] CHECK ([TenantId] <> '{execution.TenantId:D}' OR [ModelVersionId] <> '{reservation.ModelVersionId:D}')",
                    cancellationToken);
                installed = true;
                var lease = await leases.TryClaimNextAsync("clone-edge-failure", SpaceVersionCloneContract.ProcessorVersion,
                    TimeSpan.FromMinutes(2), cancellationToken);
                Assert.NotNull(lease);
                Assert.Equal(reservation.JobId, lease!.JobId);
                await Assert.ThrowsAsync<SpaceVersionStateException>(() => processor.ProcessAsync(lease, cancellationToken));

                var failure = Assert.Single(observer.Failures, value => value.ConstraintName == constraint);
                Assert.Equal(DatabaseFailureKind.CheckConstraint, failure.Kind);
                if (worker.Database.IsNpgsql()) Assert.Equal("23514", failure.SqlState);
                else Assert.Equal(547, failure.DatabaseErrorCode);
                Assert.False(failure.CanRetryTransaction);
                Assert.Single(observer.CloneBatches);
                Assert.Equal(backend, await CloneEdgeBackendAsync(worker, cancellationToken));
                await AssertCloneEdgeMapsGoneAsync(worker, observer.CloneBatches[0], cancellationToken);
                output.WriteLine($"Native clone last-table failure: provider={worker.Database.ProviderName}; SQLSTATE={failure.SqlState}; code={failure.DatabaseErrorCode}; constraint={constraint}.");

                // One command contains the full batch. The real final-table constraint proves that
                // path was reached; this observer does not pretend to record individual INSERTs.
                await using var verify = CreateCloneEdgeContext(connectionString, execution, clock);
                var empty = await ReadCloneEdgeSnapshotAsync(verify, reservation.ModelVersionId, cancellationToken);
                Assert.All(empty.Counts, count => Assert.Equal(0, count));
                Assert.Empty(empty.Fingerprints);
                Assert.Equal(sourceBefore.Fingerprints,
                    (await ReadCloneEdgeSnapshotAsync(verify, source.Id, cancellationToken)).Fingerprints);
                var target = await verify.Versions.AsNoTracking().SingleAsync(x => x.Id == reservation.ModelVersionId, cancellationToken);
                Assert.Equal(SpaceVersionStatus.Initializing, target.Status);
                var unchangedModel = await verify.Models.AsNoTracking().SingleAsync(x => x.Id == model.Id, cancellationToken);
                Assert.Equal(source.Id, unchangedModel.CurrentPublishedVersionId);
                Assert.Equal(target.Id, unchangedModel.ActiveDraftVersionId);
                var job = await verify.Jobs.AsNoTracking().SingleAsync(x => x.Id == reservation.JobId, cancellationToken);
                Assert.Equal(SpaceJobStatus.Queued, job.Status);
                Assert.Equal(1, job.AttemptCount);
                Assert.Equal(SpaceJobFailureKind.Bug, job.LastFailureKind);
                Assert.True(job.NextAttemptAtUtc > clock.UtcNow);
                nextAttemptAt = job.NextAttemptAtUtc;
                var attempt = Assert.Single(await verify.JobAttempts.AsNoTracking().Where(x => x.JobId == job.Id).ToArrayAsync(cancellationToken));
                Assert.Equal(SpaceJobAttemptOutcome.Failed, attempt.Outcome);
                Assert.Equal(SpaceJobFailureKind.Bug, attempt.FailureKind);
                Assert.Empty(await verify.JobSteps.AsNoTracking().Where(x => x.AttemptId == attempt.Id).ToArrayAsync(cancellationToken));
            }
            finally
            {
                if (installed)
                {
                    await using var cleanup = CreateCloneEdgeContext(connectionString, execution, clock);
                    await cleanup.Database.ExecuteSqlRawAsync(cleanup.Database.IsNpgsql()
                        ? $"ALTER TABLE public.\"Space_ElementAttribute\" DROP CONSTRAINT \"{constraint}\""
                        : $"ALTER TABLE [Space_ElementAttribute] DROP CONSTRAINT [{constraint}]");
                    Assert.Equal(0, await cleanup.Database.GetDbConnection().QuerySingleAsync<int>(cleanup.Database.IsNpgsql()
                        ? "SELECT COUNT(*)::int FROM pg_catalog.pg_constraint WHERE conname=@constraint AND conrelid='public.\"Space_ElementAttribute\"'::regclass"
                        : "SELECT COUNT(*) FROM sys.check_constraints WHERE name=@constraint AND parent_object_id=OBJECT_ID('Space_ElementAttribute')",
                        new { constraint }));
                }
            }

            // The real scheduler makes the failed Job eligible; no exception/retry loop is added.
            clock.AdvanceTo(DateTime.SpecifyKind(nextAttemptAt.AddSeconds(1), DateTimeKind.Utc));
            var retry = await leases.TryClaimNextAsync("clone-edge-retry", SpaceVersionCloneContract.ProcessorVersion,
                TimeSpan.FromMinutes(2), cancellationToken);
            Assert.NotNull(retry);
            Assert.Equal(reservation.JobId, retry!.JobId);
            Assert.Equal(2, retry.AttemptNo);
            var counts = await processor.ProcessAsync(retry, cancellationToken);
            Assert.Equal(12, counts.Total);
            Assert.Equal(backend, await CloneEdgeBackendAsync(worker, cancellationToken));
            Assert.Equal(2, observer.CloneBatches.Count);
            await AssertCloneEdgeMapsGoneAsync(worker, observer.CloneBatches.SelectMany(value => value).ToArray(), cancellationToken);
            Assert.Single(observer.Failures, value => value.ConstraintName == constraint);

            await using var completed = CreateCloneEdgeContext(connectionString, execution, clock);
            var copied = await ReadCloneEdgeSnapshotAsync(completed, reservation.ModelVersionId, cancellationToken);
            Assert.Equal(sourceBefore.Counts, copied.Counts);
            Assert.Equal(sourceBefore.Fingerprints,
                (await ReadCloneEdgeSnapshotAsync(completed, source.Id, cancellationToken)).Fingerprints);
            Assert.Equal(SpaceVersionStatus.Draft,
                (await completed.Versions.SingleAsync(x => x.Id == reservation.ModelVersionId, cancellationToken)).Status);
            var finalJob = await completed.Jobs.SingleAsync(x => x.Id == reservation.JobId, cancellationToken);
            Assert.Equal(SpaceJobStatus.Succeeded, finalJob.Status);
            Assert.Equal(2, finalJob.AttemptCount);
            var attempts = await completed.JobAttempts.AsNoTracking().Where(x => x.JobId == reservation.JobId).ToArrayAsync(cancellationToken);
            Assert.Single(attempts, value => value.Outcome == SpaceJobAttemptOutcome.Failed);
            var succeeded = Assert.Single(attempts, value => value.Outcome == SpaceJobAttemptOutcome.Succeeded);
            Assert.Single(await completed.JobSteps.AsNoTracking()
                .Where(x => x.AttemptId == succeeded.Id && x.StepCode == "CloneSnapshot").ToArrayAsync(cancellationToken));
            var finalModel = await completed.Models.SingleAsync(x => x.Id == model.Id, cancellationToken);
            Assert.Equal(source.Id, finalModel.CurrentPublishedVersionId);
            Assert.Equal(reservation.ModelVersionId, finalModel.ActiveDraftVersionId);
        });
    }

    [SqlServerFact]
    public async Task Concurrent_same_operation_returns_one_reservation_and_job()
    {
        await WithDatabaseAsync(async (seed, execution, clock) =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var cancellationToken = deadline.Token;
            var (model, source) = await SeedPublishedAsync(seed, execution.ActorId, includeSnapshot: false);
            var operation = Guid.NewGuid();
            var request = new SpaceVersionCloneRequest(model.Id, "Concurrent same operation", operation);
            var barrier = new CloneStartBarrier();
            var firstObserver = new CloneEdgeCommandObserver();
            var secondObserver = new CloneEdgeCommandObserver();
            var connectionString = seed.Database.GetConnectionString()!;
            await using var first = CreateCloneEdgeContext(connectionString, execution, clock,
                new CloneStartSavingBarrier(operation, barrier), firstObserver);
            await using var second = CreateCloneEdgeContext(connectionString, execution, clock,
                new CloneStartSavingBarrier(operation, barrier), secondObserver);
            await first.Database.OpenConnectionAsync(cancellationToken);
            await second.Database.OpenConnectionAsync(cancellationToken);
            Assert.NotEqual(first.ContextId.InstanceId, second.ContextId.InstanceId);
            Assert.NotEqual(await CloneEdgeBackendAsync(first, cancellationToken), await CloneEdgeBackendAsync(second, cancellationToken));
            var tasks = new List<Task<SpaceVersionCloneStartResult>>();
            Task<SpaceVersionCloneStartResult[]>? combined = null;
            try
            {
                tasks.Add(new EfSpaceVersionCloneStore(first, execution, clock).StartAsync(request, cancellationToken));
                tasks.Add(new EfSpaceVersionCloneStore(second, execution, clock).StartAsync(request, cancellationToken));
                var all = combined = Task.WhenAll(tasks);
                var reached = barrier.AllArrived.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
                var ready = await Task.WhenAny(reached, tasks[0], tasks[1]);
                if (ready != reached) await ready;
                await reached;
                Assert.Equal(2, barrier.Arrivals);
                barrier.Release.TrySetResult();
                var results = await all.WaitAsync(TimeSpan.FromSeconds(40), cancellationToken);
                Assert.Single(results, value => !value.Reused);
                Assert.Single(results, value => value.Reused);
                Assert.Equal(results[0].ModelVersionId, results[1].ModelVersionId);
                Assert.Equal(results[0].JobId, results[1].JobId);
                await using var verify = CreateCloneEdgeContext(connectionString, execution, clock);
                var version = Assert.Single(await verify.Versions.AsNoTracking()
                    .Where(x => x.ModelId == model.Id && x.CloneOperationId == operation).ToArrayAsync(cancellationToken));
                Assert.Equal(results[0].ModelVersionId, version.Id);
                Assert.Equal(SpaceVersionStatus.Initializing, version.Status);
                var job = Assert.Single(await verify.Jobs.AsNoTracking()
                    .Where(x => x.JobType == SpaceJobType.CloneVersion && x.SubjectId == version.Id).ToArrayAsync(cancellationToken));
                Assert.Equal(results[0].JobId, job.Id);
                Assert.Equal(SpaceJobStatus.Queued, job.Status);
                Assert.Empty(await verify.JobAttempts.AsNoTracking().Where(x => x.JobId == job.Id).ToArrayAsync(cancellationToken));
                var persistedModel = await verify.Models.AsNoTracking().SingleAsync(x => x.Id == model.Id, cancellationToken);
                Assert.Equal(version.Id, persistedModel.ActiveDraftVersionId);
                Assert.Equal(source.Id, persistedModel.CurrentPublishedVersionId);
                Assert.All((await ReadCloneEdgeSnapshotAsync(verify, version.Id, cancellationToken)).Counts, count => Assert.Equal(0, count));
            }
            finally
            {
                deadline.Cancel();
                barrier.Release.TrySetResult();
                // Join both operations before their pinned connections or the legacy owned database are disposed.
                try { await (combined ?? Task.WhenAll(tasks)); }
                catch { /* The original task/barrier failure is preserved by the try block. */ }
                foreach (var failure in firstObserver.Failures.Concat(secondObserver.Failures))
                    output.WriteLine($"Native clone-start contention: SQLSTATE={failure.SqlState}; code={failure.DatabaseErrorCode}; kind={failure.Kind}.");
            }
        });
    }

    private SpaceContext CreateCloneEdgeContext(string connectionString, TestExecutionContext execution, ISpaceClock clock,
        params IInterceptor[] interceptors)
    {
        if (SpaceRelationalFixture.IsSelected)
        {
            Assert.True(string.Equals(database.ConnectionString, connectionString, StringComparison.Ordinal),
                "Clone edge contexts must use the selected fixture connection.");
            return database.CreateSpaceContext(execution, clock, interceptors);
        }
        var options = new DbContextOptionsBuilder<SpaceContext>().UseSqlServer(connectionString,
            sql => sql.MigrationsHistoryTable(SpaceContext.MigrationsHistoryTable));
        options.AddInterceptors(interceptors);
        var context = new SpaceContext(options.Options, execution, clock);
        context.Database.SetCommandTimeout(180);
        return context;
    }

    private static Task<int> CloneEdgeBackendAsync(SpaceContext context, CancellationToken cancellationToken) =>
        context.Database.GetDbConnection().QuerySingleAsync<int>(new CommandDefinition(
            context.Database.IsNpgsql() ? "SELECT pg_backend_pid()" : "SELECT @@SPID",
            cancellationToken: cancellationToken));

    private static async Task AssertCloneEdgeMapsGoneAsync(SpaceContext context, string[] maps, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql())
        {
            Assert.Empty(maps);
            return;
        }
        Assert.True(maps.Length is 3 or 6);
        Assert.Equal(maps.Length, maps.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(0, await context.Database.GetDbConnection().QuerySingleAsync<int>(new CommandDefinition(
            "SELECT COUNT(*)::int FROM pg_catalog.pg_class WHERE relnamespace=pg_my_temp_schema() AND relname::text=ANY(@maps)",
            new { maps }, cancellationToken: cancellationToken)));
    }

    private static async Task<CloneEdgeSnapshot> ReadCloneEdgeSnapshotAsync(SpaceContext context, Guid versionId,
        CancellationToken cancellationToken)
    {
        var counts = new List<int>();
        var fingerprints = new List<string>();
        async Task ReadAsync<T>() where T : class
        {
            var rows = await context.Set<T>().IgnoreQueryFilters().AsNoTracking()
                .Where(row => EF.Property<Guid>(row, "TenantId") == context.CurrentTenantId
                    && EF.Property<Guid>(row, "ModelVersionId") == versionId).ToArrayAsync(cancellationToken);
            counts.Add(rows.Length);
            foreach (var row in rows)
            {
                var fields = context.Entry(row).Properties.OrderBy(value => value.Metadata.Name, StringComparer.Ordinal)
                    .ToDictionary(value => value.Metadata.Name, value => value.CurrentValue, StringComparer.Ordinal);
                fingerprints.Add(typeof(T).Name + ":" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(fields))));
            }
        }
        await ReadAsync<SpaceModelSource>();
        await ReadAsync<SpaceUnderlayCalibration>();
        await ReadAsync<SpaceFloorRevision>();
        await ReadAsync<SpaceZoneRevision>();
        await ReadAsync<SpaceAisleRevision>();
        await ReadAsync<SpaceRackRevision>();
        await ReadAsync<SpaceRackLevelRevision>();
        await ReadAsync<SpaceLocationRevision>();
        await ReadAsync<SpaceLocationExternalBinding>();
        await ReadAsync<SpaceDesignAttribute>();
        await ReadAsync<SpaceElementRevision>();
        await ReadAsync<SpaceElementAttribute>();
        return new CloneEdgeSnapshot(counts.ToArray(), fingerprints.Order(StringComparer.Ordinal).ToArray());
    }

    private sealed record CloneEdgeSnapshot(int[] Counts, string[] Fingerprints);

    private sealed class CloneEdgeClock(DateTime initial) : ISpaceClock
    {
        public DateTime UtcNow { get; private set; } = initial;
        public void AdvanceTo(DateTime next)
        {
            Assert.Equal(DateTimeKind.Utc, next.Kind);
            Assert.True(next > UtcNow);
            UtcNow = next;
        }
    }

    private sealed class CloneStartBarrier
    {
        public readonly TaskCompletionSource AllArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public int Arrivals => Volatile.Read(ref arrivals);
        public void Arrive()
        {
            if (Interlocked.Increment(ref arrivals) == 2) AllArrived.TrySetResult();
        }
    }

    private sealed class CloneStartSavingBarrier(Guid operation, CloneStartBarrier barrier) : SaveChangesInterceptor
    {
        private int entered;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = Assert.IsType<SpaceContext>(eventData.Context);
            if (context.ChangeTracker.Entries<SpaceModelVersion>()
                    .Any(entry => entry.State == EntityState.Added && entry.Entity.CloneOperationId == operation)
                && Interlocked.CompareExchange(ref entered, 1, 0) == 0)
            {
                Assert.Contains(context.ChangeTracker.Entries<SpaceJob>(),
                    entry => entry.State == EntityState.Added && entry.Entity.JobType == SpaceJobType.CloneVersion);
                Assert.NotNull(context.Database.CurrentTransaction);
                Assert.Equal(IsolationLevel.Serializable, context.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel);
                barrier.Arrive();
                await barrier.Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class CloneEdgeCommandObserver : DbCommandInterceptor
    {
        public List<string[]> CloneBatches { get; } = [];
        public List<DatabaseFailure> Failures { get; } = [];

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result)
        {
            CaptureBatch(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            CaptureBatch(command);
            return ValueTask.FromResult(result);
        }

        public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
            Failures.Add(DatabaseFailureClassifier.Classify(eventData.Exception));

        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Failures.Add(DatabaseFailureClassifier.Classify(eventData.Exception));
            return Task.CompletedTask;
        }

        private void CaptureBatch(DbCommand command)
        {
            if (!command.CommandText.Contains("DECLARE @SourceMap", StringComparison.Ordinal)
                && !command.CommandText.Contains("CREATE TEMP TABLE pg_temp.", StringComparison.Ordinal)) return;
            CloneBatches.Add(Regex.Matches(command.CommandText,
                    "CREATE TEMP TABLE pg_temp\\.\"(?<name>cp6_clone_(?:source|calibration|element)_[0-9a-f]{32})\"")
                .Select(match => match.Groups["name"].Value).ToArray());
        }
    }
}
