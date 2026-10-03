using CP6.Core.Persistence;
using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Space.IntegrationTests;

public sealed partial class SpaceJobSqlServerTests
{
    private const string AttemptNumberConstraint = "UX_Space_JobAttempt_Tenant_Job_AttemptNo";

    [SqlServerFact]
    public async Task Claim_waiter_recovers_native_attempt_number_conflict()
    {
        var tenantId = Guid.NewGuid();
        await WithDatabaseAsync(async (_, connectionString) =>
        {
            await SeedJobAsync(connectionString, tenantId, NewJob(tenantId));
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var winnerGate = new ClaimSaveGate();
            var waiterGate = new ClaimSaveGate();
            var observer = new ClaimFailureObserver(output.WriteLine);
            await using var winner = CreateContext(connectionString, tenantId, new MutableClock(Now), winnerGate);
            await using var waiter = CreateContext(connectionString, tenantId, new MutableClock(Now), waiterGate, observer);
            Task<SpaceJobLease?>? first = null;
            Task<SpaceJobLease?>? second = null;
            SpaceJobLease? lease = null;
            try
            {
                first = new EfSpaceJobLeaseStore(winner, new MutableClock(Now))
                    .TryClaimNextAsync("worker-a", "parser-v1", TimeSpan.FromSeconds(60), budget.Token);
                await winnerGate.Entered.Task.WaitAsync(budget.Token);
                second = new EfSpaceJobLeaseStore(waiter, new MutableClock(Now))
                    .TryClaimNextAsync("worker-b", "parser-v1", TimeSpan.FromSeconds(60), budget.Token);
                await waiterGate.Entered.Task.WaitAsync(budget.Token);
                winnerGate.Release();
                lease = Assert.IsType<SpaceJobLease>(await first.WaitAsync(budget.Token));
                waiterGate.Release();
                Assert.Null(await second.WaitAsync(budget.Token));
                Assert.Equal(1, lease.AttemptNo);
                if (waiter.Database.IsNpgsql())
                    Assert.True(observer.AttemptNumberFailures > 0, "Observe a real PostgreSQL 23505 before recovery.");
                Assert.Null(waiter.Database.CurrentTransaction);
                Assert.False(waiter.ChangeTracker.HasChanges());
            }
            finally
            {
                winnerGate.Release();
                waiterGate.Release();
                await budget.CancelAsync();
                foreach (Task? request in new Task?[] { first, second })
                {
                    if (request is null) continue;
                    try { await request; }
                    catch { /* Drain the owned requests without replacing the primary failure. */ }
                }
            }

            await using var verify = CreateContext(connectionString, tenantId, new MutableClock(Now));
            var attempt = Assert.Single(await verify.JobAttempts.ToListAsync());
            var job = await verify.Jobs.SingleAsync();
            Assert.Equal(lease!.AttemptId, attempt.Id);
            Assert.Equal(lease.AttemptId, job.ActiveAttemptId);
            Assert.Equal(1, job.AttemptCount);
            Assert.Equal(SpaceJobStatus.Running, job.Status);
        });
    }

    [SqlServerFact]
    public Task Claim_unknown_unique_constraint_is_not_retried() => ClaimControlledRecoveryAsync("unknown", 1);

    [SqlServerFact]
    public Task Claim_attempt_number_recovery_is_bounded_and_rolls_back() => ClaimControlledRecoveryAsync("exhaust", 5);

    [SqlServerFact]
    public Task Claim_attempt_number_recovery_honors_cancellation() => ClaimControlledRecoveryAsync("cancel", 1);

    [SqlServerFact]
    public Task Claim_serialization_failure_retries_the_whole_transaction() => ClaimControlledRecoveryAsync("serialization", 2);

    [SqlServerFact]
    public Task Claim_deadlock_retries_the_whole_transaction() => ClaimControlledRecoveryAsync("deadlock", 2);

    private async Task ClaimControlledRecoveryAsync(string mode, int expectedSaves)
    {
        var tenantId = Guid.NewGuid();
        await WithDatabaseAsync(async (_, connectionString) =>
        {
            await SeedJobAsync(connectionString, tenantId, NewJob(tenantId));
            using var cancellation = new CancellationTokenSource();
            var failures = new ClaimSaveFailure(mode, cancellation);
            await using var context = CreateContext(connectionString, tenantId, new MutableClock(Now), failures);
            var store = new EfSpaceJobLeaseStore(context, new MutableClock(Now));
            if (mode == "unknown")
                Assert.Same(failures.UnknownFailure, await Assert.ThrowsAsync<DbUpdateException>(() => ClaimAsync()));
            else if (mode == "cancel")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClaimAsync());
            else if (mode == "exhaust")
                Assert.Null(await ClaimAsync());
            else
                Assert.Equal(1, Assert.IsType<SpaceJobLease>(await ClaimAsync()).AttemptNo);

            Assert.Equal(expectedSaves, failures.Saves);
            Assert.Equal(expectedSaves, failures.TransactionIds.Distinct().Count());
            Assert.Null(context.Database.CurrentTransaction);
            await using var verify = CreateContext(connectionString, tenantId, new MutableClock(Now));
            var job = await verify.Jobs.SingleAsync();
            if (mode is "serialization" or "deadlock")
            {
                Assert.Single(await verify.JobAttempts.ToListAsync());
                Assert.Equal(1, job.AttemptCount);
                Assert.Equal(SpaceJobStatus.Running, job.Status);
            }
            else
            {
                Assert.Empty(await verify.JobAttempts.ToListAsync());
                Assert.Equal(0, job.AttemptCount);
                Assert.Equal(SpaceJobStatus.Queued, job.Status);
            }

            Task<SpaceJobLease?> ClaimAsync() => store.TryClaimNextAsync("worker", "parser-v1", TimeSpan.FromSeconds(60), cancellation.Token);
        });
    }

    private sealed class ClaimSaveGate : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int used;
        public void Release() => released.TrySetResult();
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref used, 1) == 0)
            {
                Entered.TrySetResult();
                await released.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class ClaimFailureObserver(Action<string> report) : SaveChangesInterceptor
    {
        public int AttemptNumberFailures { get; private set; }
        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            var failure = DatabaseFailureClassifier.Classify(eventData.Exception);
            if (failure.SqlState == "23505" && failure.MatchesConstraint(AttemptNumberConstraint)) AttemptNumberFailures++;
            report($"BUG159 claim native failure: SQLSTATE={failure.SqlState ?? "none"}; Kind={failure.Kind}; Constraint={failure.ConstraintName ?? "none"}.");
            return Task.CompletedTask;
        }
    }

    // These controls run in real transactions, but inject errors before writes.
    // They are separate from the coordinated regression's actual native 23505.
    private sealed class ClaimSaveFailure(string mode, CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public int Saves { get; private set; }
        public List<Guid> TransactionIds { get; } = [];
        public DbUpdateException UnknownFailure { get; } = UniqueFailure("UX_Unrelated_Constraint");
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Saves++;
            TransactionIds.Add(eventData.Context!.Database.CurrentTransaction!.TransactionId);
            if (mode == "unknown") throw UnknownFailure;
            if (mode == "cancel") cancellation.Cancel();
            if (mode == "deadlock" && Saves == 1) throw new DatabaseResourceLockDeadlockException(DatabaseProvider.SqlServer);
            if (mode == "serialization" && Saves == 1)
                throw new PostgresException("Controlled serialization failure", "ERROR", "ERROR", "40001");
            if (mode is "exhaust" or "cancel") throw UniqueFailure(AttemptNumberConstraint);
            return ValueTask.FromResult(result);
        }

        private static DbUpdateException UniqueFailure(string constraint) => new("Controlled unique conflict",
            new PostgresException("Controlled unique conflict", "ERROR", "ERROR", "23505", constraintName: constraint));
    }
}
