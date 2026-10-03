using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
using CP6.Platform.EntityFramework;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

sealed partial class IdentitySqlFixture
{
    private const string DispatcherFailureCode = "C02_FIXTURE_PUBLISH_UNAVAILABLE";

    public async Task ProviderDispatcherRetryRecoveryAsync()
    {
        var proof = new List<object>();
        var completed = false;
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var targets = await PrepareDispatcherFailureTargetsAsync(budget.Token);
            var clock = new DispatcherFailureClock();
            proof.Add(await RetryRecoveryAsync(targets.OrdinaryFactory, targets.Ordinary, targets.Runtime, 32, clock, budget.Token));
            proof.Add(await RetryRecoveryAsync(targets.PriorityFactory, targets.Priority, targets.Runtime, 16, clock, budget.Token));
            completed = true;
        }
        finally { await WriteDispatcherFailureProofAsync("dispatcher-retry-proof.json", "retry-and-original-byte-recovery", proof, completed); }
    }

    public async Task ProviderDispatcherOwnersAndLeaseAsync()
    {
        var proof = new List<object>();
        var completed = false;
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var race = await PrepareDispatcherFailureTargetsAsync(budget.Token);
            var clock = new DispatcherFailureClock();
            proof.Add(await OwnerRaceAsync(race.OrdinaryFactory, race.Ordinary, race.Runtime, 32, clock,
                options => new CP6Context(options), budget.Token));
            proof.Add(await OwnerRaceAsync(race.PriorityFactory, race.Priority, race.Runtime, 16, clock,
                options => new IdentityMessagingContext(options), budget.Token));
            // New actual producer rows isolate expiry/takeover from the rows already published by the race.
            var lease = await PrepareDispatcherFailureTargetsAsync(budget.Token);
            clock = new DispatcherFailureClock();
            proof.Add(await LeaseTakeoverAsync(lease.OrdinaryFactory, lease.Ordinary, lease.Runtime, 32, clock, budget.Token));
            proof.Add(await LeaseTakeoverAsync(lease.PriorityFactory, lease.Priority, lease.Runtime, 16, clock, budget.Token));
            completed = true;
        }
        finally { await WriteDispatcherFailureProofAsync("dispatcher-owners-and-leases-proof.json", "actual-owner-race-and-expired-lease-takeover-fencing", proof, completed); }
    }

    public async Task ProviderDispatcherTenFailuresAsync()
    {
        var proof = new List<object>();
        var completed = false;
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var targets = await PrepareDispatcherFailureTargetsAsync(budget.Token);
            var clock = new DispatcherFailureClock();
            proof.Add(await ExhaustDispatcherAsync(targets.OrdinaryFactory, targets.Ordinary, targets.Runtime, 32, clock, budget.Token));
            proof.Add(await ExhaustDispatcherAsync(targets.PriorityFactory, targets.Priority, targets.Runtime, 16, clock, budget.Token));
            completed = true;
        }
        finally { await WriteDispatcherFailureProofAsync("dispatcher-ten-failures-proof.json", "ten-actual-publisher-failures-and-bound-deadletter", proof, completed); }
    }

    private async Task<DispatcherFailureTargets> PrepareDispatcherFailureTargetsAsync(CancellationToken token)
    {
        var ordinary = InvokeDispatcherMethod<IDbContextFactory<CP6Context>>("CreateOrdinaryContextFactory",
            [typeof(DatabaseOptions), typeof(string)], database, connection);
        var priority = InvokeDispatcherMethod<IDbContextFactory<IdentityMessagingContext>>("CreatePriorityContextFactory",
            [typeof(DatabaseOptions), typeof(string)], database, connection);
        VerifyDispatcherFactory(ordinary, DatabaseContextKind.Core);
        VerifyDispatcherFactory(priority, DatabaseContextKind.IdentityPriority);
        var drainRuntime = Runtime(Guid.NewGuid());
        await DrainExistingDispatcherBacklogAsync(ordinary, drainRuntime, 32, token);
        await DrainExistingDispatcherBacklogAsync(priority, drainRuntime, 16, token);
        await RequireNoOtherActiveOutboxAsync(ordinary, Guid.Empty, token);
        await RequireNoOtherActiveOutboxAsync(priority, Guid.Empty, token);
        var tenant = await SeedAsync();
        var runtime = Runtime(tenant);
        var grant = await GrantAsync(tenant);
        await new SqlCrmOidcGrantStore(database, connection, runtime).RevokeAsync(grant.Id);
        var ordinaryRows = await ReadQueueAsync(ordinary, tenant, token);
        var priorityRows = await ReadQueueAsync(priority, tenant, token);
        Require(ordinaryRows.Length == 5 && priorityRows.Length == 1
            && ordinaryRows.Concat(priorityRows).All(row => row.Status == Cp6OutboxStatus.Pending && row.AttemptCount == 0),
            "C02_FAILURE_GATES_REQUIRE_ACTUAL_FRESH_PRODUCER_MESSAGES");
        await RequireNoOtherActiveOutboxAsync(ordinary, tenant, token);
        await RequireNoOtherActiveOutboxAsync(priority, tenant, token);
        return new(ordinary, priority, ordinaryRows.OrderBy(row => row.MessageId, StringComparer.Ordinal).First(), priorityRows.Single(), runtime);
    }

    private static async Task DrainExistingDispatcherBacklogAsync<T>(IDbContextFactory<T> factory, CrmIdentityRuntime runtime,
        int batchSize, CancellationToken token) where T : DbContext
    {
        var eligible = await EligibleQueueCountAsync(factory, DateTimeOffset.UtcNow);
        var rounds = BoundedDispatchRounds(eligible, batchSize);
        var dispatcher = new Cp6OutboxDispatcher<T>(factory, runtime.Validator, new DispatcherSuccessPublisher(), ProductionDispatchOptions(batchSize));
        var processed = 0;
        for (var index = 0; index < rounds; index++)
        {
            var result = await dispatcher.DispatchBatchAsync("c02-normal-backlog-" + Guid.NewGuid().ToString("N"), token);
            Require(result.Claimed <= batchSize && result.RetryScheduled == 0 && result.DeadLettered == 0,
                "C02_EXISTING_FIXTURE_BACKLOG_REQUIRES_ROOT_ATTENTION");
            processed += result.Published;
            if (result.Claimed == 0)
            {
                Console.WriteLine($"C02 failure gates normal backlog drain: context={typeof(T).Name}, published={processed}, batch={batchSize}.");
                return;
            }
        }
        Require(await EligibleQueueCountAsync(factory, DateTimeOffset.UtcNow) == 0, "C02_EXISTING_FIXTURE_BACKLOG_EXCEEDS_NORMAL_BUDGET");
    }

    private static async Task RequireNoOtherActiveOutboxAsync<T>(IDbContextFactory<T> factory, Guid tenant, CancellationToken token) where T : DbContext
    {
        await using var db = factory.CreateDbContext();
        Require(!await db.Set<Cp6OutboxMessage>().AnyAsync(row => row.TenantId != tenant
            && (row.Status == Cp6OutboxStatus.Pending || row.Status == Cp6OutboxStatus.Dispatching), token),
            "C02_OTHER_FIXTURE_HAS_FUTURE_OR_ACTIVE_OUTBOX_ROOT_MUST_HANDLE");
    }

    private static async Task<object> RetryRecoveryAsync<T>(IDbContextFactory<T> factory, Cp6OutboxMessage original,
        CrmIdentityRuntime runtime, int batchSize, DispatcherFailureClock clock, CancellationToken token) where T : DbContext
    {
        var options = ProductionDispatchOptions(batchSize);
        var publisher = new DispatcherControlledFailurePublisher(original.MessageId, failAttempts: 1);
        var dispatcher = new Cp6OutboxDispatcher<T>(factory, runtime.Validator, publisher, options, clock);
        var started = clock.GetUtcNow();
        var first = await dispatcher.DispatchBatchAsync("c02-retry-first-" + Guid.NewGuid().ToString("N"), token);
        Require(first.Claimed <= batchSize && first.RetryScheduled == 1 && first.DeadLettered == 0, "C02_DISPATCHER_DID_NOT_SCHEDULE_TARGET_RETRY");
        var failed = await ReadFailureTargetAsync(factory, original, token);
        Require(failed.Status == Cp6OutboxStatus.Pending && failed.AttemptCount == 1 && failed.LastErrorCode == DispatcherFailureCode
            && failed.AvailableAtUtc == started.AddSeconds(1), "C02_DISPATCHER_RETRY_NOT_ACTUALLY_PERSISTED");
        RequireClearedLease(failed);
        RequireFailureMessageUnchanged(original, failed);
        RequireChangedFailureToken(original.RowVersion, failed.RowVersion);
        var early = await dispatcher.DispatchBatchAsync("c02-retry-early-" + Guid.NewGuid().ToString("N"), token);
        Require(early.Claimed == 0 && publisher.TargetCalls.Count == 1, "C02_DISPATCHER_RETRIED_BEFORE_ACTUAL_AVAILABLE_TIME");
        clock.AdvanceTo(failed.AvailableAtUtc);
        var recovery = await dispatcher.DispatchBatchAsync("c02-retry-recovered-" + Guid.NewGuid().ToString("N"), token);
        Require(recovery.Claimed == 1 && recovery.Published == 1 && recovery.RetryScheduled == 0 && recovery.DeadLettered == 0,
            "C02_DISPATCHER_TARGET_DID_NOT_RECOVER");
        var published = await ReadFailureTargetAsync(factory, original, token);
        Require(published.Status == Cp6OutboxStatus.Published && published.AttemptCount == 2
            && published.PublishedAtUtc == clock.GetUtcNow() && published.LastErrorCode is null,
            "C02_DISPATCHER_RECOVERY_NOT_ACTUALLY_PERSISTED");
        RequireClearedLease(published);
        RequireFailureMessageUnchanged(original, published);
        RequireChangedFailureToken(failed.RowVersion, published.RowVersion);
        Require(publisher.TargetCalls.Count == 2 && publisher.SuccessfulTargetCalls == 1, "C02_DISPATCHER_RECOVERY_PUBLISH_COUNT_MISMATCH");
        foreach (var call in publisher.TargetCalls) RequireDispatchBytesUnchanged(original, call);
        return new
        {
            queue = typeof(T).Name, batchSize, original.MessageId, payloadSha256 = original.PayloadSha256,
            actualFailedAtInjectedUtc = started, actualStoredAvailableAtUtc = failed.AvailableAtUtc,
            earlyActualBatchClaimed = early.Claimed, actualAttemptCount = published.AttemptCount,
            actualSuccessfulTargetPublishes = publisher.SuccessfulTargetCalls,
            publishedAtInjectedUtc = published.PublishedAtUtc, originalBytesAndMetadataPreserved = true,
            finalNativeTokenSha256 = IdentityEventContracts.Hash(published.RowVersion)
        };
    }

    private static async Task<object> OwnerRaceAsync<T>(IDbContextFactory<T> factory, Cp6OutboxMessage original,
        CrmIdentityRuntime runtime, int batchSize, DispatcherFailureClock clock, Func<DbContextOptions<T>, T> create,
        CancellationToken token) where T : DbContext
    {
        var barrier = new DispatcherOwnerClaimBarrier(original.Id);
        IDbContextFactory<T> intercepted;
        using (var template = factory.CreateDbContext())
        {
            // Retain the actual production factory's configured provider/profile/services, adding only the native command barrier.
            var actualOptions = (DbContextOptions<T>)template.GetService<IDbContextOptions>();
            var options = new DbContextOptionsBuilder<T>(actualOptions).AddInterceptors(new DispatcherOwnerClaimInterceptor(barrier)).Options;
            intercepted = new DispatcherFailureContextFactory<T>(() => create(options));
        }
        var publisher = new DispatcherControlledFailurePublisher(original.MessageId, failAttempts: 0);
        var optionsForDispatch = ProductionDispatchOptions(batchSize);
        var first = new Cp6OutboxDispatcher<T>(intercepted, runtime.Validator, publisher, optionsForDispatch, clock);
        var second = new Cp6OutboxDispatcher<T>(intercepted, runtime.Validator, publisher, optionsForDispatch, clock);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        var tasks = new List<Task<Cp6OutboxDispatchResult>>();
        var completed = false;
        Cp6OutboxDispatchResult[] results;
        try
        {
            tasks.Add(first.DispatchBatchAsync("c02-owner-a-" + Guid.NewGuid().ToString("N"), budget.Token));
            tasks.Add(second.DispatchBatchAsync("c02-owner-b-" + Guid.NewGuid().ToString("N"), budget.Token));
            await barrier.BothArrived.Task.WaitAsync(TimeSpan.FromSeconds(15), budget.Token);
            Require(barrier.Arrivals == 2 && barrier.PhysicalConnections.Distinct(StringComparer.Ordinal).Count() == 2,
                "C02_OWNER_RACE_REQUIRES_TWO_PHYSICAL_SESSIONS_TARGETING_SAME_ROW");
            Require(publisher.TargetCalls.IsEmpty && tasks.All(task => !task.IsCompleted), "C02_OWNER_RACE_TARGET_PUBLISHED_BEFORE_BARRIER");
            barrier.Release();
            results = await Task.WhenAll(tasks);
            Require(results.All(result => result.Claimed <= batchSize && result.RetryScheduled == 0 && result.DeadLettered == 0),
                "C02_OWNER_RACE_DISPATCH_FAILED_OR_CHANGED_BUDGET");
            Require(barrier.UpdateResults.Count == 2 && barrier.UpdateResults.Count(count => count > 0) == 1
                && barrier.UpdateResults.Count(count => count == 0) == 1 && results.Count(result => result.Claimed > 0) == 1,
                "C02_OWNER_RACE_NATIVE_UPDATE_DID_NOT_HAVE_EXACTLY_ONE_WINNER");
            completed = true;
        }
        finally
        {
            barrier.Release();
            if (!completed) await budget.CancelAsync();
            try { await Task.WhenAll(tasks); }
            catch when (!completed) { /* Preserve the primary failure only after every real dispatcher has joined. */ }
            Require(tasks.All(task => task.IsCompleted), "C02_OWNER_RACE_BACKGROUND_TASK_SURVIVED");
        }
        var persisted = await ReadFailureTargetAsync(factory, original, token);
        Require(publisher.TargetCalls.Count == 1 && publisher.SuccessfulTargetCalls == 1 && persisted.Status == Cp6OutboxStatus.Published
            && persisted.AttemptCount == 1, "C02_OWNER_RACE_MORE_THAN_ONE_TARGET_OWNER_OR_PUBLISH");
        RequireClearedLease(persisted);
        RequireFailureMessageUnchanged(original, persisted);
        RequireDispatchBytesUnchanged(original, publisher.TargetCalls.Single());
        RequireChangedFailureToken(original.RowVersion, persisted.RowVersion);
        return new
        {
            queue = typeof(T).Name, batchSize, original.MessageId, preUpdateActualTargetArrivals = barrier.Arrivals,
            distinctPhysicalSessions = barrier.PhysicalConnections.Distinct(StringComparer.Ordinal).Count(),
            nativeClaimUpdateCounts = barrier.UpdateResults.ToArray(), ownerActualClaimCounts = results.Select(result => result.Claimed).ToArray(),
            actualTargetPublisherCalls = publisher.TargetCalls.Count, persisted.AttemptCount,
            allActualDispatcherTasksJoined = tasks.All(task => task.IsCompleted), payloadSha256 = persisted.PayloadSha256
        };
    }

    private static async Task<object> LeaseTakeoverAsync<T>(IDbContextFactory<T> factory, Cp6OutboxMessage original,
        CrmIdentityRuntime runtime, int batchSize, DispatcherFailureClock clock, CancellationToken token) where T : DbContext
    {
        var options = ProductionDispatchOptions(batchSize);
        var old = await ClaimFailureTargetAndPublishPeersAsync(factory, original, runtime, options, clock, "c02-lease-old", token);
        var leased = await ReadFailureTargetAsync(factory, original, token);
        Require(leased.Status == Cp6OutboxStatus.Dispatching && leased.AttemptCount == 1
            && leased.LeaseToken == old.LeaseToken && leased.LeaseOwner == old.LeaseOwner
            && leased.LeaseExpiresAtUtc == clock.GetUtcNow().AddMinutes(5), "C02_OLD_LEASE_NOT_ACTUALLY_PERSISTED");
        var expires = old.LeaseExpiresAtUtc;
        clock.AdvanceTo(expires.AddMilliseconds(1));
        var current = await ClaimFailureTargetAndPublishPeersAsync(factory, original, runtime, options, clock, "c02-lease-current", token);
        var takeover = await ReadFailureTargetAsync(factory, original, token);
        Require(current.LeaseToken != old.LeaseToken && current.Message.AttemptCount == 2
            && takeover.LeaseToken == current.LeaseToken && takeover.LeaseOwner == current.LeaseOwner,
            "C02_EXPIRED_LEASE_TAKEOVER_DID_NOT_CHANGE_OWNER_AND_TOKEN");
        RequireChangedFailureToken(leased.RowVersion, takeover.RowVersion);
        await using (var rejectOld = factory.CreateDbContext())
            await RequireRejectedOldClaimAsync(() => new Cp6OutboxStore<T>(rejectOld, runtime.Validator, clock).MarkPublishedAsync(old, token));
        await using (var rejectOld = factory.CreateDbContext())
            await RequireRejectedOldClaimAsync(async () => { await new Cp6OutboxStore<T>(rejectOld, runtime.Validator, clock)
                .MarkFailedAsync(old, DispatcherFailureCode, true, options, cancellationToken: token); });
        var afterRejected = await ReadFailureTargetAsync(factory, original, token);
        Require(afterRejected.RowVersion.AsSpan().SequenceEqual(takeover.RowVersion)
            && afterRejected.Status == Cp6OutboxStatus.Dispatching && afterRejected.AttemptCount == 2
            && afterRejected.LeaseToken == current.LeaseToken, "C02_REJECTED_OLD_CLAIM_MUTATED_CURRENT_OWNER");
        RequireDispatchBytesUnchanged(original, current.Message);
        await new DispatcherSuccessPublisher().PublishAsync(current.Message, token);
        await using (var finish = factory.CreateDbContext())
            await new Cp6OutboxStore<T>(finish, runtime.Validator, clock).MarkPublishedAsync(current, token);
        var published = await ReadFailureTargetAsync(factory, original, token);
        Require(published.Status == Cp6OutboxStatus.Published && published.AttemptCount == 2 && published.PublishedAtUtc == clock.GetUtcNow(),
            "C02_CURRENT_LEASE_OWNER_DID_NOT_PUBLISH");
        RequireClearedLease(published);
        RequireFailureMessageUnchanged(original, published);
        RequireChangedFailureToken(takeover.RowVersion, published.RowVersion);
        return new
        {
            queue = typeof(T).Name, batchSize, original.MessageId, oldActualLeaseExpiresAtUtc = expires,
            injectedTakeoverUtc = clock.GetUtcNow(), oldPublishRejectedAfterActualTakeover = true, oldRetryRejectedAfterActualTakeover = true,
            rejectedOldOperationsLeftCurrentNativeTokenUnchanged = true, currentOwnerPublished = true,
            published.AttemptCount, payloadSha256 = published.PayloadSha256
        };
    }

    private static async Task<Cp6ClaimedOutboxMessage> ClaimFailureTargetAndPublishPeersAsync<T>(IDbContextFactory<T> factory,
        Cp6OutboxMessage original, CrmIdentityRuntime runtime, Cp6TransactionalMessagingOptions options, DispatcherFailureClock clock,
        string ownerPrefix, CancellationToken token) where T : DbContext
    {
        await using var db = factory.CreateDbContext();
        var store = new Cp6OutboxStore<T>(db, runtime.Validator, clock);
        var claims = await store.ClaimBatchAsync(ownerPrefix + "-" + Guid.NewGuid().ToString("N"), options, token);
        Require(claims.Count <= options.DispatchBatchSize && claims.All(claim => claim.Message.TenantId == original.TenantId),
            "C02_LEASE_FIXTURE_MUST_NOT_CLAIM_OTHER_TENANT_FUTURE_WORK");
        var target = claims.Single(claim => claim.Message.Id == original.Id);
        foreach (var peer in claims.Where(claim => claim.Message.Id != original.Id))
        {
            await new DispatcherSuccessPublisher().PublishAsync(peer.Message, token);
            await store.MarkPublishedAsync(peer, token);
        }
        return target;
    }

    private static async Task<object> ExhaustDispatcherAsync<T>(IDbContextFactory<T> factory, Cp6OutboxMessage original,
        CrmIdentityRuntime runtime, int batchSize, DispatcherFailureClock clock, CancellationToken token) where T : DbContext
    {
        var options = ProductionDispatchOptions(batchSize);
        var publisher = new DispatcherControlledFailurePublisher(original.MessageId, failAttempts: 10);
        var dispatcher = new Cp6OutboxDispatcher<T>(factory, runtime.Validator, publisher, options, clock);
        var observations = new List<object>();
        var previous = original.RowVersion;
        Cp6OutboxMessage? final = null;
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var now = clock.GetUtcNow();
            var result = await dispatcher.DispatchBatchAsync("c02-ten-failures-" + Guid.NewGuid().ToString("N"), token);
            Require(result.Claimed is > 0 && result.Claimed <= batchSize && result.RetryScheduled == (attempt < 10 ? 1 : 0)
                && result.DeadLettered == (attempt == 10 ? 1 : 0), "C02_ACTUAL_DISPATCHER_FAILURE_OUTCOME_MISMATCH");
            final = await ReadFailureTargetAsync(factory, original, token);
            Require(final.AttemptCount == attempt && publisher.TargetCalls.Count == attempt && final.LastErrorCode == DispatcherFailureCode,
                "C02_TEN_FAILURES_ACTUAL_TARGET_ATTEMPT_NOT_PERSISTED");
            RequireClearedLease(final);
            RequireFailureMessageUnchanged(original, final);
            RequireChangedFailureToken(previous, final.RowVersion);
            previous = final.RowVersion;
            if (attempt < 10)
            {
                var expectedDelay = TimeSpan.FromSeconds(1 << (attempt - 1));
                Require(final.Status == Cp6OutboxStatus.Pending && final.AvailableAtUtc == now.Add(expectedDelay),
                    "C02_TEN_FAILURES_PERSISTED_BACKOFF_DIFFERED_FROM_PRODUCTION_OPTIONS");
                clock.AdvanceTo(final.AvailableAtUtc);
            }
            observations.Add(new { actualTargetAttempt = attempt, atInjectedUtc = now, final.Status, final.AvailableAtUtc,
                retryScheduled = result.RetryScheduled, deadLettered = result.DeadLettered, nativeTokenSha256 = IdentityEventContracts.Hash(final.RowVersion) });
        }
        Require(final is not null && final.Status == Cp6OutboxStatus.DeadLettered && final.DeadLetteredAtUtc == clock.GetUtcNow()
            && final.PublishedAtUtc is null && publisher.SuccessfulTargetCalls == 0, "C02_TENTH_FAILURE_DID_NOT_PERSIST_DEADLETTER_STATE");
        foreach (var call in publisher.TargetCalls) RequireDispatchBytesUnchanged(original, call);
        await using var verify = factory.CreateDbContext();
        var dead = await verify.Set<Cp6DeadLetterRecord>().AsNoTracking().SingleAsync(row => row.MessageId == original.MessageId, token);
        Require(dead.Direction == Cp6DeadLetterDirection.Outbound && dead.TenantId == original.TenantId
            && dead.PayloadSha256 == original.PayloadSha256 && dead.ErrorCode == DispatcherFailureCode
            && dead.CreatedAtUtc == clock.GetUtcNow() && dead.ReplayedAtUtc is null && dead.RowVersion.Length == 8,
            "C02_ACTUAL_DEADLETTER_NOT_BOUND_TO_TARGET_BYTES_TENANT_AND_FAILURE");
        var extra = await dispatcher.DispatchBatchAsync("c02-deadletter-ineligible-" + Guid.NewGuid().ToString("N"), token);
        Require(extra.Claimed == 0 && publisher.TargetCalls.Count == 10, "C02_DEADLETTER_TARGET_WAS_DISPATCHED_AGAIN");
        var unchanged = await ReadFailureTargetAsync(factory, original, token);
        Require(unchanged.RowVersion.AsSpan().SequenceEqual(final!.RowVersion), "C02_INELIGIBLE_DEADLETTER_BATCH_MUTATED_TARGET");
        return new
        {
            queue = typeof(T).Name, batchSize, maxAttempts = options.MaxOutboxAttempts, original.MessageId,
            actualTargetPublisherFailures = publisher.TargetCalls.Count, nativeAttemptCount = unchanged.AttemptCount,
            deadLetterId = dead.Id, deadLetterPayloadSha256 = dead.PayloadSha256, boundNativeDeadletter = true,
            extraActualBatchClaimed = extra.Claimed, attempts = observations
        };
    }

    private static async Task<Cp6OutboxMessage> ReadFailureTargetAsync<T>(IDbContextFactory<T> factory, Cp6OutboxMessage original,
        CancellationToken token) where T : DbContext
    {
        await using var db = factory.CreateDbContext();
        return await db.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(row => row.Id == original.Id && row.TenantId == original.TenantId, token);
    }
    private static async Task RequireRejectedOldClaimAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (InvalidOperationException error) when (error.Message == "The Outbox lease is no longer owned by this claim.") { return; }
        throw new InvalidOperationException("C02_OLD_CLAIM_WAS_NOT_REJECTED_BY_ACTUAL_SIGNED_STORE");
    }
    private static void RequireClearedLease(Cp6OutboxMessage row) => Require(row.LeaseOwner is null && row.LeaseToken is null
        && row.LeaseExpiresAtUtc is null, "C02_ACTUAL_OUTBOX_TERMINAL_OR_RETRY_LEASE_NOT_CLEARED");
    private static void RequireChangedFailureToken(byte[] original, byte[] current) => Require(original.Length == 8 && current.Length == 8
        && !original.AsSpan().SequenceEqual(current), "C02_ACTUAL_OUTBOX_NATIVE_TOKEN_DID_NOT_CHANGE");
    private static void RequireFailureMessageUnchanged(Cp6OutboxMessage original, Cp6OutboxMessage current) => Require(
        original.Id == current.Id && original.MessageId == current.MessageId && original.TenantId == current.TenantId
        && original.TopicName == current.TopicName && original.PartitionKey == current.PartitionKey
        && original.CorrelationId == current.CorrelationId && original.CausationId == current.CausationId
        && original.AggregateId == current.AggregateId && original.AggregateVersion == current.AggregateVersion
        && original.PayloadSha256 == current.PayloadSha256 && original.Payload.AsSpan().SequenceEqual(current.Payload),
        "C02_ACTUAL_OUTBOX_FAILURE_OR_RECOVERY_CHANGED_ORIGINAL_BYTES_AND_METADATA");
    private static void RequireDispatchBytesUnchanged(Cp6OutboxMessage original, Cp6OutboxDispatchMessage call) => Require(
        original.Id == call.Id && original.MessageId == call.MessageId && original.TenantId == call.TenantId
        && original.TopicName == call.TopicName && original.PartitionKey == call.PartitionKey
        && original.CorrelationId == call.CorrelationId && original.CausationId == call.CausationId
        && original.AggregateId == call.AggregateId && original.AggregateVersion == call.AggregateVersion
        && call.Payload.Span.SequenceEqual(original.Payload), "C02_ACTUAL_PUBLISHER_DID_NOT_RECEIVE_ORIGINAL_BYTES_AND_METADATA");

    private async Task WriteDispatcherFailureProofAsync(string name, string scope, List<object> rows, bool completed)
    {
        if (DispatcherEvidenceDirectory is null) return;
        await File.WriteAllTextAsync(Path.Combine(DispatcherEvidenceDirectory, name), JsonSerializer.Serialize(new
        {
            schemaId = "cp6.c02.provider-dispatcher-failure-verification.v1", provider = database.Provider.ToString(), scope, completed,
            boundary = "actual signed Platform store/dispatcher, production factory options and native owned queues; recording/failing publisher only; no Dapr/Kafka/CRM delivery",
            clockScope = "TimeProvider injected through formal package constructors; logical time advance, no claim of OS waiting for retry or lease expiry",
            platformAssemblySha256 = IdentityEventContracts.Hash(File.ReadAllBytes(typeof(Cp6OutboxStore<>).Assembly.Location)),
            observations = rows, observedAtUtc = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    private sealed record DispatcherFailureTargets(IDbContextFactory<CP6Context> OrdinaryFactory,
        IDbContextFactory<IdentityMessagingContext> PriorityFactory, Cp6OutboxMessage Ordinary, Cp6OutboxMessage Priority, CrmIdentityRuntime Runtime);
    private sealed class DispatcherFailureClock : TimeProvider
    {
        // Both databases retain whole milliseconds exactly; start just above the actual producer timestamps.
        private DateTimeOffset now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1);
        public override DateTimeOffset GetUtcNow() => now;
        public void AdvanceTo(DateTimeOffset next) { Require(next >= now, "C02_INJECTED_PACKAGE_CLOCK_CANNOT_GO_BACKWARDS"); now = next; }
    }
    private sealed class DispatcherSuccessPublisher : ICp6OutboxPublisher
    {
        public Task PublishAsync(Cp6OutboxDispatchMessage message, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    }
    private sealed class DispatcherControlledFailurePublisher(string messageId, int failAttempts) : ICp6OutboxPublisher
    {
        private int calls, successes;
        public ConcurrentQueue<Cp6OutboxDispatchMessage> TargetCalls { get; } = new();
        public int SuccessfulTargetCalls => Volatile.Read(ref successes);
        public Task PublishAsync(Cp6OutboxDispatchMessage message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (message.MessageId == messageId)
            {
                TargetCalls.Enqueue(message with { Payload = message.Payload.ToArray() });
                if (Interlocked.Increment(ref calls) <= failAttempts) throw new Cp6OutboxPublishException(DispatcherFailureCode, retryable: true);
                Interlocked.Increment(ref successes);
            }
            return Task.CompletedTask;
        }
    }
    private sealed class DispatcherFailureContextFactory<T>(Func<T> create) : IDbContextFactory<T> where T : DbContext
    {
        public T CreateDbContext() => create();
        public Task<T> CreateDbContextAsync(CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(create()); }
    }
    private sealed class DispatcherOwnerClaimBarrier(Guid target)
    {
        private int arrivals;
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid Target => target;
        public int Arrivals => Volatile.Read(ref arrivals);
        public TaskCompletionSource BothArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> PhysicalConnections { get; } = new();
        public ConcurrentQueue<int> UpdateResults { get; } = new();
        public void Release() => release.TrySetResult();
        public async Task ArriveAsync(DbConnection connection, CancellationToken token)
        {
            PhysicalConnections.Enqueue(connection switch
            {
                Microsoft.Data.SqlClient.SqlConnection sql => "SqlServer:" + sql.ClientConnectionId,
                Npgsql.NpgsqlConnection pg => "PostgreSql:" + pg.ProcessID,
                _ => throw new InvalidOperationException("C02_OWNER_GATE_REQUIRES_NATIVE_CONNECTION")
            });
            var count = Interlocked.Increment(ref arrivals);
            Require(count <= 2, "C02_OWNER_GATE_EXPECTS_EXACTLY_TWO_TARGET_UPDATES");
            if (count == 2) BothArrived.TrySetResult();
            await release.Task.WaitAsync(token);
        }
    }
    private sealed class DispatcherOwnerClaimInterceptor(DispatcherOwnerClaimBarrier barrier) : DbCommandInterceptor
    {
        private readonly ConcurrentDictionary<Guid, byte> gatedCommands = new();
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (IsTargetClaim(command) && gatedCommands.TryAdd(data.CommandId, 0)) await barrier.ArriveAsync(command.Connection!, token);
            return result;
        }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken token = default)
        {
            if (gatedCommands.ContainsKey(data.CommandId)) barrier.UpdateResults.Enqueue(result);
            return ValueTask.FromResult(result);
        }
        private bool IsTargetClaim(DbCommand command) => command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            && command.CommandText.Contains("Cp6_OutboxMessage", StringComparison.Ordinal)
            && command.CommandText.Contains("AttemptCount", StringComparison.Ordinal) && command.CommandText.Contains("LeaseToken", StringComparison.Ordinal)
            && command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value switch
            {
                Guid id => id == barrier.Target,
                Guid[] ids => ids.Contains(barrier.Target),
                string text => text.Contains(barrier.Target.ToString("D"), StringComparison.OrdinalIgnoreCase),
                _ => false
            });
    }
}
