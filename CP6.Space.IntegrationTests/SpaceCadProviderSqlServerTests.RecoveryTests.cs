using System.Data.Common;
using System.Data;
using CP6.Core.Persistence;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Space.IntegrationTests;

public sealed partial class SpaceCadProviderSqlServerTests
{
    [SqlServerFact]
    public Task Replace_waiter_recovers_native_serialization_conflict() =>
        CoordinatedReplacementAsync(replay: false);

    [SqlServerFact]
    public Task Replace_waiter_replays_same_key_after_native_serialization_conflict() =>
        CoordinatedReplacementAsync(replay: true);

    private async Task CoordinatedReplacementAsync(bool replay)
    {
        await WithDatabaseAsync(async (connectionString, tenantId, siteId) =>
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var winnerGate = new ProviderSaveGate();
            var waiterStarted = new ProviderTransactionWaiter();
            var observer = new ProviderSerializationObserver(output.WriteLine);
            var winnerExecution = new TestExecution(tenantId, Guid.NewGuid());
            var waiterExecution = replay ? winnerExecution : new TestExecution(tenantId, Guid.NewGuid());
            await using var winner = CreateContext(connectionString, winnerExecution, winnerGate);
            await using var waiter = CreateContext(connectionString, waiterExecution, waiterStarted, observer);
            Task<ReplaceSpaceCadProviderConfigurationResponse>? first = null;
            Task<ReplaceSpaceCadProviderConfigurationResponse>? second = null;
            try
            {
                first = NewService(winner, winnerExecution).ReplaceAsync(
                    siteId, Configuration(expectedRevision: 0), "native-winner", budget.Token);
                await winnerGate.Entered.Task.WaitAsync(budget.Token);
                second = NewService(waiter, waiterExecution).ReplaceAsync(
                    siteId, Configuration(expectedRevision: 0), replay ? "native-winner" : "native-waiter", budget.Token);
                await waiterStarted.Waiting.Task.WaitAsync(budget.Token);
                winnerGate.Release();
                var response = await first.WaitAsync(budget.Token);
                Assert.Equal(1, response.Capability.ConfigurationRevision);
                Assert.False(response.IdempotentReplay);
                if (replay)
                {
                    var repeated = await second.WaitAsync(budget.Token);
                    Assert.True(repeated.IdempotentReplay);
                    Assert.Equal(1, repeated.Capability.ConfigurationRevision);
                }
                else
                {
                    var conflict = await Assert.ThrowsAsync<SpaceProblemException>(() => second.WaitAsync(budget.Token));
                    Assert.Equal(SpaceErrorCodes.CadProviderRevisionConflict, conflict.Code);
                }
                if (waiter.Database.IsNpgsql())
                    Assert.True(observer.SerializationFailures > 0, "Observe actual PostgreSQL 40001 before recovery.");
                Assert.Null(winner.Database.CurrentTransaction);
                Assert.Null(waiter.Database.CurrentTransaction);
                Assert.False(waiter.ChangeTracker.HasChanges());
            }
            finally
            {
                winnerGate.Release();
                await budget.CancelAsync();
                foreach (Task? request in new Task?[] { first, second })
                {
                    if (request is null) continue;
                    try { await request; }
                    catch { /* Drain owned requests without replacing the primary failure. */ }
                }
            }

            await using var verify = CreateContext(connectionString, winnerExecution);
            var configuration = Assert.Single(await verify.CadProviderConfigurations.AsNoTracking().ToListAsync());
            Assert.Equal(siteId, configuration.SiteId);
            Assert.Equal(1, configuration.ConfigurationRevision);
            Assert.True(configuration.IsCurrent);
            Assert.Equal(2, await verify.CadProviderCertifications.CountAsync());
            Assert.Equal(1, await verify.IdempotencyRecords.CountAsync());
        });
    }

    [SqlServerFact]
    public Task Replace_serialization_failure_retries_the_whole_transaction() =>
        ControlledReplacementAsync("serialization", expectedAttempts: 2);

    [SqlServerFact]
    public Task Replace_deadlock_retries_the_whole_transaction() =>
        ControlledReplacementAsync("deadlock", expectedAttempts: 2);

    [SqlServerFact]
    public Task Replace_unknown_unique_constraint_is_not_retried() =>
        ControlledReplacementAsync("unknown", expectedAttempts: 1);

    [SqlServerFact]
    public Task Replace_recovery_is_bounded_and_rolls_back() =>
        ControlledReplacementAsync("exhaust", expectedAttempts: 3);

    [SqlServerFact]
    public Task Replace_recovery_honors_cancellation() =>
        ControlledReplacementAsync("cancel", expectedAttempts: 1);

    private async Task ControlledReplacementAsync(string mode, int expectedAttempts)
    {
        await WithDatabaseAsync(async (connectionString, tenantId, siteId) =>
        {
            var execution = new TestExecution(tenantId, Guid.NewGuid());
            await using (var seed = CreateContext(connectionString, execution))
                await NewService(seed, execution).ReplaceAsync(siteId, Configuration(expectedRevision: 0), "seed-configuration");

            using var cancellation = new CancellationTokenSource();
            var failure = new ProviderSaveFailure(mode, cancellation);
            await using var context = CreateContext(connectionString, execution, failure);
            // Unchanged tracked data is allowed; the retry must re-read it after rollback.
            await context.CadProviderConfigurations.SingleAsync();
            var service = NewService(context, execution);
            Task<ReplaceSpaceCadProviderConfigurationResponse> ReplaceAsync() => service.ReplaceAsync(
                siteId, Configuration(expectedRevision: 1), "controlled-replacement", cancellation.Token);
            if (mode == "unknown")
                Assert.Same(failure.UnknownFailure, await Assert.ThrowsAsync<DbUpdateException>(() => ReplaceAsync()));
            else if (mode == "cancel")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReplaceAsync());
            else if (mode == "exhaust")
                await Assert.ThrowsAsync<DatabaseResourceLockDeadlockException>(() => ReplaceAsync());
            else
            {
                var response = await ReplaceAsync();
                Assert.Equal(2, response.Capability.ConfigurationRevision);
                Assert.True(response.Capability.CadGaReady);
                Assert.False(response.IdempotentReplay);
                Assert.True((await ReplaceAsync()).IdempotentReplay);
            }

            Assert.Equal(expectedAttempts * 3, failure.Saves);
            Assert.Equal(expectedAttempts, failure.TransactionIds.Distinct().Count());
            Assert.Null(context.Database.CurrentTransaction);
            Assert.False(context.ChangeTracker.HasChanges());
            await using var verify = CreateContext(connectionString, execution);
            var history = await verify.CadProviderConfigurations.AsNoTracking()
                .OrderBy(value => value.ConfigurationRevision).ToArrayAsync();
            var succeeded = mode is "serialization" or "deadlock";
            Assert.Equal(succeeded ? 2 : 1, history.Length);
            Assert.Equal(succeeded ? 2 : 1, Assert.Single(history, value => value.IsCurrent).ConfigurationRevision);
            Assert.Equal(!succeeded, history[0].IsCurrent);
            Assert.Equal(succeeded ? 4 : 2, await verify.CadProviderCertifications.CountAsync());
            Assert.Equal(succeeded ? 2 : 1, await verify.IdempotencyRecords.CountAsync());
            var originalCertifications = await verify.CadProviderCertifications
                .Where(value => value.ConfigurationId == history[0].Id).ToArrayAsync();
            Assert.Equal(2, originalCertifications.Length);
            Assert.All(originalCertifications, value => Assert.True(value.HasCompleteQualification));
        });
    }

    [SqlServerFact]
    public async Task Replace_preserves_caller_pending_changes()
    {
        await WithDatabaseAsync(async (connectionString, tenantId, siteId) =>
        {
            var execution = new TestExecution(tenantId, Guid.NewGuid());
            await using var context = CreateContext(connectionString, execution);
            var pending = SpaceModel.Create(tenantId, Guid.NewGuid());
            context.Models.Add(pending);
            await Assert.ThrowsAsync<InvalidOperationException>(() => NewService(context, execution)
                .ReplaceAsync(siteId, Configuration(expectedRevision: 0), "pending-caller"));
            Assert.Equal(EntityState.Added, context.Entry(pending).State);
            await using (var verify = CreateContext(connectionString, execution))
            {
                Assert.False(await verify.Models.AnyAsync(value => value.Id == pending.Id));
                Assert.Empty(await verify.CadProviderConfigurations.ToListAsync());
                Assert.Empty(await verify.IdempotencyRecords.ToListAsync());
            }
            await context.SaveChangesAsync();
            await using var persisted = CreateContext(connectionString, execution);
            Assert.True(await persisted.Models.AnyAsync(value => value.Id == pending.Id));
        });
    }

    [SqlServerFact]
    public async Task Replace_preserves_caller_transaction()
    {
        await WithDatabaseAsync(async (connectionString, tenantId, siteId) =>
        {
            var execution = new TestExecution(tenantId, Guid.NewGuid());
            await using var context = CreateContext(connectionString, execution);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.ThrowsAsync<InvalidOperationException>(() => NewService(context, execution)
                .ReplaceAsync(siteId, Configuration(expectedRevision: 0), "caller-transaction"));
            Assert.Same(transaction, context.Database.CurrentTransaction);
            var pending = SpaceModel.Create(tenantId, Guid.NewGuid());
            context.Models.Add(pending);
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
            await using var verify = CreateContext(connectionString, execution);
            Assert.False(await verify.Models.AnyAsync(value => value.Id == pending.Id));
            Assert.Empty(await verify.CadProviderConfigurations.ToListAsync());
            Assert.Empty(await verify.IdempotencyRecords.ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task Replace_preserves_caller_ambient_transaction()
    {
        await WithDatabaseAsync(async (connectionString, tenantId, siteId) =>
        {
            var execution = new TestExecution(tenantId, Guid.NewGuid());
            await using var context = CreateContext(connectionString, execution);
            using (var scope = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            {
                var transaction = System.Transactions.Transaction.Current;
                Assert.NotNull(transaction);
                await Assert.ThrowsAsync<InvalidOperationException>(() => NewService(context, execution)
                    .ReplaceAsync(siteId, Configuration(expectedRevision: 0), "ambient-transaction"));
                Assert.Same(transaction, System.Transactions.Transaction.Current);
                Assert.Equal(System.Transactions.TransactionStatus.Active, transaction.TransactionInformation.Status);
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                scope.Complete();
            }
            await using var verify = CreateContext(connectionString, execution);
            Assert.Empty(await verify.CadProviderConfigurations.ToListAsync());
            Assert.Empty(await verify.IdempotencyRecords.ToListAsync());
        });
    }

    // Fail only at the final idempotency save, after supersede/configuration/
    // certification writes. These controls exercise real rollback, while the
    // error itself is injected separately from the two native regressions.
    private sealed class ProviderSaveFailure(string mode, CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public int Saves { get; private set; }
        public List<Guid> TransactionIds { get; } = [];
        public DbUpdateException UnknownFailure { get; } = new("BUG161 controlled unknown constraint",
            new PostgresException("Controlled unique failure", "ERROR", "ERROR", "23505", constraintName: "UX_Unrelated_Constraint"));

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Saves++;
            TransactionIds.Add(eventData.Context!.Database.CurrentTransaction!.TransactionId);
            if (Saves % 3 != 0) return ValueTask.FromResult(result);
            if (mode == "unknown") throw UnknownFailure;
            if (mode == "cancel") cancellation.Cancel();
            if (mode == "serialization" && Saves == 3)
                throw new PostgresException("Controlled serialization failure", "ERROR", "ERROR", "40001");
            if (mode is "exhaust" or "cancel" || mode == "deadlock" && Saves == 3)
                throw new DatabaseResourceLockDeadlockException(DatabaseProvider.SqlServer);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ProviderSaveGate : SaveChangesInterceptor
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

    private sealed class ProviderTransactionWaiter : DbTransactionInterceptor
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int used;
        public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref used, 1) == 0)
            {
                if (eventData.Context!.Database.IsNpgsql())
                {
                    // Establish an actual Serializable snapshot before releasing the
                    // winner's first save. The production site lock remains unchanged.
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

    private sealed class ProviderSerializationObserver(Action<string> report) : SaveChangesInterceptor
    {
        public int SerializationFailures { get; private set; }
        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            var failure = DatabaseFailureClassifier.Classify(eventData.Exception);
            if (failure.SqlState == "40001") SerializationFailures++;
            report($"BUG161 CAD provider native failure: SQLSTATE={failure.SqlState ?? "none"}; Kind={failure.Kind}.");
            return Task.CompletedTask;
        }
    }
}
