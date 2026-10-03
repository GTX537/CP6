using System.Data;
using System.Data.Common;
using CP6.Core.Persistence;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Space.IntegrationTests;

public sealed partial class SpaceVersionCloneSqlServerTests
{
    [SqlServerFact]
    public Task Design_v1_floor_waiter_recovers_native_conflict_as_revision_conflict() =>
        FloorWaiterRecoversAsync(sameKey: false);

    [SqlServerFact]
    public Task Design_v1_floor_waiter_recovers_native_conflict_and_replays_same_key() =>
        FloorWaiterRecoversAsync(sameKey: true);

    private async Task FloorWaiterRecoversAsync(bool sameKey)
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var versionId = await SeedFloorRecoveryCandidateAsync(context, execution.ActorId);
            var connectionString = context.Database.GetConnectionString()!;
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var firstSave = new FloorSaveGate();
            var waiter = new FloorTransactionWaiter();
            var failures = new FloorSerializationObserver(output.WriteLine);
            await using var firstContext = CreateContext(connectionString, execution, clock, firstSave);
            await using var secondContext = CreateContext(connectionString, execution, clock, waiter, failures);
            var firstService = NewDesignService(firstContext, execution, clock);
            var secondService = NewDesignService(secondContext, execution, clock);
            Task<CreateSpaceFloorResponse>? firstRequest = null;
            Task<(CreateSpaceFloorResponse? Response, string? Problem)>? secondRequest = null;
            try
            {
                firstRequest = firstService.CreateFloorAsync(versionId, FloorRecoveryRequest("F1"), "floor-recovery-first", budget.Token);
                await firstSave.Entered.Task.WaitAsync(budget.Token);
                secondRequest = RunSecondAsync();
                if (await Task.WhenAny(waiter.Waiting.Task, secondRequest).WaitAsync(budget.Token) == secondRequest)
                    await secondRequest;
                await waiter.Waiting.Task.WaitAsync(budget.Token);
                firstSave.Release();
                var first = await firstRequest.WaitAsync(budget.Token);
                var second = await secondRequest.WaitAsync(budget.Token);
                Assert.False(first.IdempotentReplay);
                Assert.Equal(1, first.VersionContentRevision);
                if (sameKey)
                {
                    Assert.Null(second.Problem);
                    Assert.NotNull(second.Response);
                    Assert.True(second.Response!.IdempotentReplay);
                    Assert.Equal(first.Floor.Revision.LogicalId, second.Response.Floor.Revision.LogicalId);
                    Assert.Equal(first.VersionContentRevision, second.Response.VersionContentRevision);
                }
                else
                {
                    Assert.Null(second.Response);
                    Assert.Equal(SpaceErrorCodes.ConcurrencyConflict, second.Problem);
                }
                if (secondContext.Database.IsNpgsql())
                    Assert.True(failures.SerializationFailures > 0, "The PostgreSQL floor waiter must observe real native 40001 before recovery.");
            }
            finally
            {
                firstSave.Release();
                await budget.CancelAsync();
                foreach (Task? request in new Task?[] { firstRequest, secondRequest })
                {
                    if (request is null) continue;
                    try { await request; }
                    catch { /* Drain owned requests while preserving the primary failure. */ }
                }
            }

            await using var verify = CreateContext(connectionString, execution, clock);
            Assert.Single(await verify.FloorRevisions.Where(value => value.ModelVersionId == versionId).ToListAsync());
            Assert.Single(await verify.IdempotencyRecords.ToListAsync());
            Assert.Equal(1, await verify.Versions.Where(value => value.Id == versionId).Select(value => value.ContentRevision).SingleAsync());

            async Task<(CreateSpaceFloorResponse? Response, string? Problem)> RunSecondAsync()
            {
                try
                {
                    var response = await secondService.CreateFloorAsync(versionId, FloorRecoveryRequest(sameKey ? "F1" : "F2"),
                        sameKey ? "floor-recovery-first" : "floor-recovery-second", budget.Token);
                    return (response, null);
                }
                catch (SpaceProblemException exception) { return (null, exception.Code); }
            }
        });
    }

    [SqlServerFact]
    public async Task Design_v1_floor_creation_preserves_unsaved_caller_changes()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var versionId = await SeedFloorRecoveryCandidateAsync(context, execution.ActorId);
            var pending = SpaceModel.Create(execution.TenantId, Guid.NewGuid());
            context.Add(pending);
            var service = NewDesignService(context, execution, clock);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateFloorAsync(versionId, FloorRecoveryRequest("F1"), "caller-pending"));
            Assert.Equal(EntityState.Added, context.Entry(pending).State);
            Assert.Null(context.Database.CurrentTransaction);
            await using var verify = CreateContext(context.Database.GetConnectionString()!, execution, clock);
            Assert.False(await verify.Models.AnyAsync(value => value.Id == pending.Id));
            Assert.Empty(await verify.FloorRevisions.Where(value => value.ModelVersionId == versionId).ToListAsync());
            Assert.Empty(await verify.IdempotencyRecords.ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task Design_v1_floor_creation_preserves_caller_owned_transaction()
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var versionId = await SeedFloorRecoveryCandidateAsync(context, execution.ActorId);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var service = NewDesignService(context, execution, clock);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateFloorAsync(versionId, FloorRecoveryRequest("F1"), "caller-transaction"));
            Assert.Same(transaction, context.Database.CurrentTransaction);
            var pending = SpaceModel.Create(execution.TenantId, Guid.NewGuid());
            context.Add(pending);
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
            await using var verify = CreateContext(context.Database.GetConnectionString()!, execution, clock);
            Assert.False(await verify.Models.AnyAsync(value => value.Id == pending.Id));
            Assert.Empty(await verify.FloorRevisions.Where(value => value.ModelVersionId == versionId).ToListAsync());
            Assert.Empty(await verify.IdempotencyRecords.ToListAsync());
        });
    }

    [SqlServerFact]
    public Task Design_v1_floor_creation_unknown_failure_is_not_retried() => FloorRecoveryStopsAsync("unknown", 1);

    [SqlServerFact]
    public Task Design_v1_floor_creation_recovery_honors_cancellation() => FloorRecoveryStopsAsync("cancel", 1);

    [SqlServerFact]
    public Task Design_v1_floor_creation_recovery_is_bounded_and_rolls_back() => FloorRecoveryStopsAsync("exhaust", 3);

    private async Task FloorRecoveryStopsAsync(string mode, int expectedAttempts)
    {
        await WithDatabaseAsync(async (context, execution, clock) =>
        {
            var versionId = await SeedFloorRecoveryCandidateAsync(context, execution.ActorId);
            using var cancellation = new CancellationTokenSource();
            var failure = new FloorSaveFailure(mode, cancellation);
            await using var requestContext = CreateContext(context.Database.GetConnectionString()!, execution, clock, failure);
            var service = NewDesignService(requestContext, execution, clock);
            if (mode == "unknown")
            {
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateFloorAsync(versionId, FloorRecoveryRequest("F1"), "controlled-stop", cancellation.Token));
                Assert.Same(failure.UnknownFailure, exception);
            }
            else if (mode == "cancel")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateFloorAsync(versionId, FloorRecoveryRequest("F1"), "controlled-stop", cancellation.Token));
            else
                await Assert.ThrowsAsync<DatabaseResourceLockDeadlockException>(() => service.CreateFloorAsync(versionId, FloorRecoveryRequest("F1"), "controlled-stop", cancellation.Token));
            Assert.Equal(expectedAttempts, failure.Attempts);
            Assert.Null(requestContext.Database.CurrentTransaction);
            await using var verify = CreateContext(context.Database.GetConnectionString()!, execution, clock);
            Assert.Empty(await verify.FloorRevisions.Where(value => value.ModelVersionId == versionId).ToListAsync());
            Assert.Empty(await verify.IdempotencyRecords.ToListAsync());
            Assert.Equal(0, await verify.Versions.Where(value => value.Id == versionId).Select(value => value.ContentRevision).SingleAsync());
        });
    }

    private static CreateSpaceFloorRequest FloorRecoveryRequest(string floorCode) => new(floorCode, floorCode, 1, 0, 6_000, ExpectedContentRevision: 0);

    private static async Task<Guid> SeedFloorRecoveryCandidateAsync(SpaceContext context, Guid actorId)
    {
        var (model, published) = await SeedPublishedAsync(context, actorId, includeSnapshot: false);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(published);
        model.ActivateDesignV1();
        var draft = SpaceModelVersion.CreateBlankDraft(context.CurrentTenantId, model.Id, 2, "Floor recovery", Guid.NewGuid());
        model.ReserveDraft(draft);
        context.Versions.Add(draft);
        await context.SaveChangesAsync();
        return draft.Id;
    }

    private sealed class FloorSaveGate : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool entered;
        public void Release() => released.TrySetResult();
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!entered)
            {
                entered = true;
                Entered.TrySetResult();
                await released.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class FloorTransactionWaiter : DbTransactionInterceptor
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
                    // Establish a real Serializable snapshot before the first save
                    // is released. Production transaction locks remain unchanged.
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

    private sealed class FloorSerializationObserver(Action<string> report) : SaveChangesInterceptor
    {
        public int SerializationFailures { get; private set; }
        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            var failure = DatabaseFailureClassifier.Classify(eventData.Exception);
            if (failure.SqlState == "40001") SerializationFailures++;
            report($"BUG157 floor native failure: SQLSTATE={failure.SqlState ?? "none"}; Kind={failure.Kind}.");
            return Task.CompletedTask;
        }
    }

    // These controls inject failures before writes within real transactions. They
    // are separate from the coordinated regression's actual PostgreSQL 40001.
    private sealed class FloorSaveFailure(string mode, CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public int Attempts { get; private set; }
        public InvalidOperationException UnknownFailure { get; } = new("BUG157 controlled unknown failure");
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (mode == "unknown") throw UnknownFailure;
            if (mode == "cancel") cancellation.Cancel();
            throw new DatabaseResourceLockDeadlockException(DatabaseProvider.SqlServer);
        }
    }
}
