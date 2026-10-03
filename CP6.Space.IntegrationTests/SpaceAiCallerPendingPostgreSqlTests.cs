using System.Data;
using System.Data.Common;
using CP6.Core.Persistence;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.IntegrationTests;

public sealed partial class SpaceAiAtomicApplySqlServerTests
{
    [SpaceAiPostgreSqlFact]
    public async Task PostgreSql_cancel_contention_preserves_pending_caller_changes_without_retry()
    {
        Assert.True(SpaceRelationalFixture.IsSelected);
        Assert.Equal(DatabaseProvider.PostgreSql, database.Database.Provider);
        database.WriteSetupEvidence(output);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellationToken = deadline.Token;
        var execution = new TestExecutionContext(Guid.NewGuid(), Guid.NewGuid());
        var clock = new MutableClock(Start);
        await using var seed = database.CreateSpaceContext(execution, clock);
        var graph = await SeedReviewedZoneAsync(seed, execution);
        var queued = await QueueAsync(seed, execution, clock, graph);
        seed.ChangeTracker.Clear();
        var run = await seed.GenerationRuns.AsNoTracking().SingleAsync(
            item => item.Id == graph.RunId, cancellationToken);
        var request = new SpaceAiRunActionRequest(Convert.ToBase64String(run.RowVersion));
        const string idempotencyKey = "cancel-with-pending-caller-model";

        var firstSave = new CallerPendingCancelSaveHold();
        var secondCommands = new CallerPendingCancelCommandObserver();
        await using var first = database.CreateSpaceContext(execution, clock, firstSave);
        await using var second = database.CreateSpaceContext(execution, clock, secondCommands);
        await first.Database.OpenConnectionAsync(cancellationToken);
        await second.Database.OpenConnectionAsync(cancellationToken);
        await seed.Database.OpenConnectionAsync(cancellationToken);
        var firstPid = await CallerPendingBackendAsync(first, cancellationToken);
        var secondPid = await CallerPendingBackendAsync(second, cancellationToken);
        var observerPid = await CallerPendingBackendAsync(seed, cancellationToken);
        Assert.NotEqual(first.ContextId.InstanceId, second.ContextId.InstanceId);
        Assert.Equal(3, new[] { firstPid, secondPid, observerPid }.Distinct().Count());

        // This entity belongs to the caller's pending work, not to Cancel's replayable command.
        var pendingModel = SpaceModel.Create(execution.TenantId, Guid.NewGuid());
        second.Models.Add(pendingModel);
        Assert.Equal(EntityState.Added, second.Entry(pendingModel).State);
        Assert.True(second.ChangeTracker.HasChanges());
        Assert.Null(second.Database.CurrentTransaction);

        Task<SpaceAiGenerationRunActionDto>? firstTask = null;
        Task<Exception?>? secondTask = null;
        var started = new List<Task>();
        try
        {
            firstTask = RecoveryService(first, execution, clock).CancelAsync(
                graph.RunId, request, idempotencyKey, cancellationToken);
            started.Add(firstTask);
            var arrived = firstSave.Arrived.Task;
            if (await Task.WhenAny(arrived, firstTask).WaitAsync(cancellationToken) == firstTask)
            {
                await firstTask;
                Assert.Fail("The first Cancel completed before holding its transaction at SaveChanges.");
            }
            await arrived.WaitAsync(cancellationToken);

            secondTask = Record.ExceptionAsync(() => RecoveryService(second, execution, clock).CancelAsync(
                graph.RunId, request, idempotencyKey, cancellationToken));
            started.Add(secondTask);
            Assert.True(await ObserveCallerPendingBlockAsync(
                seed.Database.GetDbConnection(), firstPid, secondPid, secondTask, cancellationToken),
                "The second physical session must wait for the first session's run lock before it commits.");
            Assert.False(firstTask.IsCompleted);
            output.WriteLine($"Native Cancel contention: blocker={firstPid}; waiter={secondPid}; observer={observerPid}; PostgreSQL blocking relationship confirmed.");

            firstSave.Release.TrySetResult();
            var winner = await firstTask.WaitAsync(cancellationToken);
            var contenderFailure = await secondTask.WaitAsync(cancellationToken);
            var native = Assert.Single(secondCommands.Failures);
            Assert.Equal("40001", native.SqlState);
            Assert.Equal(DatabaseFailureKind.SerializationFailure, native.Kind);
            Assert.True(native.CanRetryTransaction);
            Assert.Equal("Cancelled", winner.Status);
            Assert.False(winner.IdempotentReplay);
            Assert.Equal(1, firstSave.Arrivals);

            var pendingEntry = second.ChangeTracker.Entries<SpaceModel>()
                .SingleOrDefault(entry => entry.Entity.Id == pendingModel.Id);
            output.WriteLine($"Caller-pending outcome: native SQLSTATE={native.SqlState}; propagated={contenderFailure is not null}; pendingState={pendingEntry?.State.ToString() ?? "Detached"}; replayReads={secondCommands.ReplayReads}; runLockReads={secondCommands.RunLockReads}.");

            // A retry would read the winner's idempotency record and return success after Clear
            // detached pendingModel. The native failure must escape this single owned attempt.
            Assert.NotNull(contenderFailure);
            Assert.IsType<Npgsql.PostgresException>(contenderFailure!.GetBaseException());
            Assert.Equal("40001", DatabaseFailureClassifier.Classify(contenderFailure!).SqlState);
            Assert.Equal(1, secondCommands.ReplayReads);
            Assert.Equal(1, secondCommands.RunLockReads);
            Assert.Null(second.Database.CurrentTransaction);
            Assert.NotNull(pendingEntry);
            Assert.Same(pendingModel, pendingEntry!.Entity);
            Assert.Equal(EntityState.Added, pendingEntry.State);
            Assert.True(second.ChangeTracker.HasChanges());
            Assert.Empty(pendingModel.RowVersion);

            await using var verify = database.CreateSpaceContext(execution, clock);
            Assert.False(await verify.Models.AsNoTracking().AnyAsync(
                item => item.Id == pendingModel.Id, cancellationToken));
            Assert.Equal(SpaceGenerationRunStatus.Cancelled,
                (await verify.GenerationRuns.AsNoTracking().SingleAsync(
                    item => item.Id == graph.RunId, cancellationToken)).Status);
            Assert.Equal(SpaceJobStatus.Cancelled,
                (await verify.Jobs.AsNoTracking().SingleAsync(
                    item => item.Id == queued.JobId, cancellationToken)).Status);
            Assert.Single(await verify.IdempotencyRecords.AsNoTracking()
                .Where(item => item.Operation == "space.ai-generation-run.cancel")
                .ToArrayAsync(cancellationToken));
            Assert.Equal(SpaceGenerationProposalStatus.Obsolete,
                (await verify.GenerationProposals.AsNoTracking().SingleAsync(
                    item => item.Id == graph.ProposalId, cancellationToken)).Status);
            Assert.Equal(0, (await verify.Versions.AsNoTracking().SingleAsync(
                item => item.Id == graph.VersionId, cancellationToken)).ContentRevision);
        }
        finally
        {
            firstSave.Release.TrySetResult();
            await deadline.CancelAsync();
            // Every started operation settles before its pinned connection is disposed.
            try { await Task.WhenAll(started); }
            catch { /* Preserve the primary assertion/operation failure after joining both workers. */ }
        }
    }

    private static Task<int> CallerPendingBackendAsync(SpaceContext context, CancellationToken cancellationToken) =>
        context.Database.GetDbConnection().QuerySingleAsync<int>(new CommandDefinition(
            "SELECT pg_backend_pid()", commandTimeout: 5, cancellationToken: cancellationToken));

    private static async Task<bool> ObserveCallerPendingBlockAsync(
        DbConnection observer, int blocker, int waiter, Task competing, CancellationToken cancellationToken)
    {
        while (!competing.IsCompleted)
        {
            var blocked = await observer.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT @blocker = ANY(pg_blocking_pids(@waiter))", new { blocker, waiter },
                commandTimeout: 5, cancellationToken: cancellationToken));
            if (blocked) return true;
            // Time alone does not prove contention; only the actual backend blocking relationship does.
            await Task.Delay(20, cancellationToken);
        }
        return false;
    }

    private sealed class CallerPendingCancelSaveHold : SaveChangesInterceptor
    {
        private int arrivals;
        public int Arrivals => Volatile.Read(ref arrivals);
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var context = Assert.IsType<SpaceContext>(eventData.Context);
            Assert.Contains(context.ChangeTracker.Entries<SpaceIdempotencyRecord>(),
                entry => entry.State == EntityState.Added && entry.Entity.Operation == "space.ai-generation-run.cancel");
            Assert.NotNull(context.Database.CurrentTransaction);
            Assert.Equal(IsolationLevel.Serializable, context.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel);
            Interlocked.Increment(ref arrivals);
            Arrived.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class CallerPendingCancelCommandObserver : DbCommandInterceptor
    {
        public int ReplayReads { get; private set; }
        public int RunLockReads { get; private set; }
        public List<DatabaseFailure> Failures { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"Space_IdempotencyRecord\"", StringComparison.Ordinal)) ReplayReads++;
            if (command.CommandText.Contains("\"Space_GenerationRun\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) RunLockReads++;
            return ValueTask.FromResult(result);
        }

        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Failures.Add(DatabaseFailureClassifier.Classify(eventData.Exception));
            return Task.CompletedTask;
        }
    }

    private sealed class SpaceAiPostgreSqlFactAttribute : FactAttribute
    {
        public SpaceAiPostgreSqlFactAttribute()
        {
            if (!SpaceRelationalFixture.IsSelected)
                Skip = "Select the owned WP5 PostgreSQL Space provider and connection for this native retry regression.";
            else if (Environment.GetEnvironmentVariable(SpaceRelationalFixture.ProviderVariable) == "SqlServer")
                Skip = "This PostgreSQL Serializable retry regression is excluded from the SQL Server matrix.";
            // Invalid selected inputs fail the fixture; an explicitly selected PostgreSQL case never skips.
        }
    }
}
