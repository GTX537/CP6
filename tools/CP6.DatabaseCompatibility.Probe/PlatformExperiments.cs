using CP6.Platform.EntityFramework;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace CP6.DatabaseCompatibility.Probe;

internal static class PlatformExperiments
{
    private static readonly ProbeMessageValidator Validator = new();
    private static readonly Cp6TransactionalMessagingOptions Options = new() { DispatchBatchSize = 100, MaxOutboxAttempts = 10, MaxInboxAttempts = 10 };

    public static async Task RunAsync(ProbeDatabase db, ProbeReport report)
    {
        var clock = new ProbeClock();
        await report.CheckAsync("Platform.0102.OutboxInsertClaimStaleLeaseAndPublish", () => OutboxClaimAsync(db, clock));
        await report.CheckAsync("Platform.0102.ConcurrentClaimExactlyOneOwner", () => OutboxRaceAsync(db, clock));
        await report.CheckAsync("Platform.0102.OutboxRetryDeadLetterAndRequeue", () => OutboxRetryAsync(db, clock));
        await report.CheckAsync("Platform.0102.InboxCheckpointDuplicatesAndOrder", () => InboxAsync(db, clock));
        await report.CheckAsync("Platform.0102.InboxFailureDeadLetterRequeue", () => InboxFailureAsync(db, clock));
        await report.CheckAsync("Platform.0102.InboxConcurrentDuplicate", () => InboxDuplicateRaceAsync(db, clock));
        await report.CheckAsync("Platform.0102.InboxConcurrentCheckpoint", () => CheckpointRaceAsync(db, clock));
        await report.CheckAsync("Platform.0102.DeadLetterReplayStaleContextRace", () => ReplayRaceAsync(db, clock));
        await report.CheckAsync("Platform.0102.RetentionCutoffsAndActiveLease", () => RetentionAsync(db, clock));
        await report.CheckAsync("Platform.0102.NonzeroOffsetParameterAndUtcContract", () => OffsetContractAsync(db));
        if (db.Provider == ProbeProvider.PostgreSql)
            await report.CheckAsync("Platform.0102.RealSerializationFailureBehavior", () => SerializationFailureAsync(db, clock));
    }

    private static PlatformContext Context(ProbeDatabase db) => new(db.NewConnection(), db.Provider, db.Schema, ownsConnection: true);
    private static Cp6OutboxEnvelope Envelope(string label, Guid tenant) => new($"probe-{label}-{Guid.NewGuid():N}", tenant, "cp6.compat.probe", tenant.ToString("N"), new byte[] { 1, 2, 3 }, "probe-correlation", "probe-causation", Guid.NewGuid().ToString("N"), 1);
    private static Cp6InboxDelivery Delivery(string label, Guid tenant, string aggregate, int version) => new("probe-consumer", $"probe-{label}-{Guid.NewGuid():N}", tenant, "cp6.compat.probe", tenant.ToString("N"), new byte[] { 1, 2, 3 }, aggregate, version);
    private static Cp6InboxProcessor<PlatformContext> Processor(ProbeDatabase db, ProbeClock clock) => new(new PlatformContextFactory(db), Validator, Options, clock);
    private static Cp6OutboxStore<PlatformContext> Store(PlatformContext context, ProbeClock clock) => new(context, Validator, clock);

    private static async Task<string> OutboxClaimAsync(ProbeDatabase db, ProbeClock clock)
    {
        await using var staleContext = Context(db);
        var stale = Store(staleContext, clock).Enqueue(Envelope("claim", Guid.NewGuid()));
        await staleContext.SaveChangesAsync();
        var original = stale.RowVersion.ToArray();
        Expect.Token(original);
        await using var claimant = Context(db);
        var first = (await Store(claimant, clock).ClaimBatchAsync("probe-worker-a", Options)).Single(claim => claim.Message.Id == stale.Id);
        var claimedToken = await db.ReadTokenAsync("Cp6_OutboxMessage", stale.Id);
        Expect.Changed(original, claimedToken);
        staleContext.Entry(stale).Property(nameof(Cp6OutboxMessage.LastErrorCode)).CurrentValue = "PROBE_STALE";
        var rejected = false;
        try { await staleContext.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { rejected = true; }
        Expect.True(rejected, "Actual Platform batch claim must invalidate a previously loaded EF row.");
        clock.Advance(TimeSpan.FromSeconds(31));
        await using var newOwner = Context(db);
        var second = (await Store(newOwner, clock).ClaimBatchAsync("probe-worker-b", Options)).Single(claim => claim.Message.Id == stale.Id);
        Expect.True(second.LeaseToken != first.LeaseToken, "Expired lease takeover must allocate a different fencing token.");
        var takeoverToken = await db.ReadTokenAsync("Cp6_OutboxMessage", stale.Id);
        Expect.Changed(claimedToken, takeoverToken);
        var stalePublish = false;
        var staleRetry = false;
        await using (var old = Context(db))
        {
            try { await Store(old, clock).MarkPublishedAsync(first); }
            catch (InvalidOperationException) { stalePublish = true; }
            try { await Store(old, clock).MarkFailedAsync(first, "PROBE_STALE", true, Options); }
            catch (InvalidOperationException) { staleRetry = true; }
        }
        Expect.True(stalePublish && staleRetry, "Old Platform lease must reject both publish and retry after takeover.");
        await Store(newOwner, clock).MarkPublishedAsync(second);
        var published = await newOwner.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(row => row.Id == stale.Id);
        Expect.True(published.Status == Cp6OutboxStatus.Published, "Current Platform lease owner must publish successfully.");
        Expect.Changed(takeoverToken, published.RowVersion);
        return "Actual package Enqueue hydrated 8 bytes; ExecuteUpdate claim and expired-lease takeover replaced them; stale EF row and old lease publish/retry rejected; current owner published.";
    }

    private static async Task<string> OutboxRaceAsync(ProbeDatabase db, ProbeClock clock)
    {
        Guid id;
        await using (var seed = Context(db))
        {
            var row = Store(seed, clock).Enqueue(Envelope("race", Guid.NewGuid()));
            await seed.SaveChangesAsync();
            id = row.Id;
        }
        var gate = new OutboxClaimGateInterceptor(id);
        await using var a = new PlatformContext(db.NewConnection(), db.Provider, db.Schema, ownsConnection: true, interceptor: gate);
        await using var b = new PlatformContext(db.NewConnection(), db.Provider, db.Schema, ownsConnection: true, interceptor: gate);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var results = await Task.WhenAll(Store(a, clock).ClaimBatchAsync("probe-race-a", Options, timeout.Token), Store(b, clock).ClaimBatchAsync("probe-race-b", Options, timeout.Token));
        Expect.True(gate.Arrivals == 2, "Both actual stores must select the same candidate and reach the pre-UPDATE barrier before racing.");
        var claims = results.SelectMany(result => result).Where(claim => claim.Message.Id == id).ToArray();
        Expect.True(claims.Length == 1, "Concurrent actual Platform claim must return exactly one owner for the same message.");
        await using var finish = Context(db);
        await Store(finish, clock).MarkPublishedAsync(claims[0]);
        return "Two actual package stores selected the same candidate and both reached the bounded pre-UPDATE command barrier (arrivals=2); exactly one claim returned and its owner published.";
    }

    private static async Task<string> OutboxRetryAsync(ProbeDatabase db, ProbeClock clock)
    {
        Guid id;
        await using (var seed = Context(db))
        {
            var row = Store(seed, clock).Enqueue(Envelope("retry", Guid.NewGuid()));
            await seed.SaveChangesAsync();
            id = row.Id;
        }
        await using (var retry = Context(db))
        {
            var claim = (await Store(retry, clock).ClaimBatchAsync("probe-retry", Options)).Single(item => item.Message.Id == id);
            var before = await db.ReadTokenAsync("Cp6_OutboxMessage", id);
            Expect.True(!await Store(retry, clock).MarkFailedAsync(claim, "PROBE_RETRY", true, Options), "Retryable first attempt must schedule retry rather than dead-letter.");
            Expect.Changed(before, await db.ReadTokenAsync("Cp6_OutboxMessage", id));
        }
        clock.Advance(TimeSpan.FromSeconds(6));
        Guid deadLetterId;
        byte[] deadLetterToken;
        await using (var dead = Context(db))
        {
            var claim = (await Store(dead, clock).ClaimBatchAsync("probe-dead", Options)).Single(item => item.Message.Id == id);
            Expect.True(await Store(dead, clock).MarkFailedAsync(claim, "PROBE_DEAD", false, Options), "Non-retryable failure must create a dead letter.");
            var letter = await dead.Set<Cp6DeadLetterRecord>().AsNoTracking().SingleAsync(item => item.MessageId == claim.Message.MessageId);
            Expect.Token(letter.RowVersion);
            deadLetterId = letter.Id;
            deadLetterToken = letter.RowVersion.ToArray();
        }
        var deadOutboxToken = await db.ReadTokenAsync("Cp6_OutboxMessage", id);
        await using (var replay = Context(db)) await Store(replay, clock).RequeueDeadLetteredAsync(id, "PROBE_REPLAY");
        Expect.Changed(deadOutboxToken, await db.ReadTokenAsync("Cp6_OutboxMessage", id));
        Expect.Changed(deadLetterToken, await db.ReadTokenAsync("Cp6_DeadLetterRecord", deadLetterId));
        await using (var publish = Context(db))
        {
            var claim = (await Store(publish, clock).ClaimBatchAsync("probe-replayed", Options)).Single(item => item.Message.Id == id);
            await Store(publish, clock).MarkPublishedAsync(claim);
        }
        return "Package retry, dead-letter insert, Outbox requeue and dead-letter replay update all persisted 8-byte tokens; requeued message published.";
    }

    private static async Task WriteBusinessAsync(ProbeDatabase db, PlatformContext context, Guid tenant, string tag, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        Expect.True(transaction is not null, "Actual Platform Inbox handler must run inside its transaction.");
        await connection.ExecuteAsync(new CommandDefinition($"INSERT INTO {db.Table("BusinessRecords")} ({db.Column("Id")},{db.Column("TenantId")},{db.Column("Value")}) VALUES (@Id,@Tenant,@Value)",
            new { Id = Guid.NewGuid(), Tenant = tenant, Value = tag }, transaction, cancellationToken: cancellationToken));
    }

    private static Task<int> CountBusinessAsync(ProbeDatabase db, string tag) =>
        db.Connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {db.Table("BusinessRecords")} WHERE {db.Column("Value")}=@Value", new { Value = tag });

    private static async Task<string> InboxAsync(ProbeDatabase db, ProbeClock clock)
    {
        var tenant = Guid.NewGuid();
        var aggregate = Guid.NewGuid().ToString("N");
        var tag = $"inbox-{Guid.NewGuid():N}";
        var first = Delivery("inbox", tenant, aggregate, 2);
        var processor = Processor(db, clock);
        var applied = await processor.ProcessAsync(first, (context, token) => WriteBusinessAsync(db, context, tenant, tag, token));
        Expect.True(applied.Disposition == Cp6InboxDisposition.Applied, "First actual Inbox event must apply.");
        Guid checkpointId;
        byte[] initialCheckpointToken;
        await using (var read = Context(db))
        {
            var inbox = await read.Set<Cp6InboxMessage>().AsNoTracking().SingleAsync(row => row.MessageId == first.MessageId);
            var checkpoint = await read.Set<Cp6InboxAggregateCheckpoint>().AsNoTracking().SingleAsync(row => row.AggregateId == aggregate && row.TenantId == tenant);
            Expect.Token(inbox.RowVersion);
            Expect.Token(checkpoint.RowVersion);
            checkpointId = checkpoint.Id;
            initialCheckpointToken = checkpoint.RowVersion.ToArray();
        }
        var duplicate = await processor.ProcessAsync(first, (context, token) => WriteBusinessAsync(db, context, tenant, tag, token));
        Expect.True(duplicate.Disposition == Cp6InboxDisposition.Duplicate && await CountBusinessAsync(db, tag) == 1, "Duplicate must not repeat business change.");
        var newer = Delivery("newer", tenant, aggregate, 3);
        var advanced = await processor.ProcessAsync(newer, (context, token) => WriteBusinessAsync(db, context, tenant, tag, token));
        Expect.True(advanced.Disposition == Cp6InboxDisposition.Applied, "Newer event must advance checkpoint.");
        Expect.Changed(initialCheckpointToken, await db.ReadTokenAsync("Cp6_InboxAggregateCheckpoint", checkpointId));
        var older = Delivery("older", tenant, aggregate, 1);
        var ignored = await processor.ProcessAsync(older, (context, token) => WriteBusinessAsync(db, context, tenant, tag, token));
        Expect.True(ignored.Disposition == Cp6InboxDisposition.IgnoredOutOfOrder && await CountBusinessAsync(db, tag) == 2, "Out-of-order event cannot repeat or reverse business update.");
        return "Actual Inbox/checkpoint inserted with 8-byte tokens; checkpoint version advance changed token; duplicate and older aggregate event did not repeat business writes.";
    }

    private static async Task<string> InboxFailureAsync(ProbeDatabase db, ProbeClock clock)
    {
        var tenant = Guid.NewGuid();
        var delivery = Delivery("inbox-failure", tenant, Guid.NewGuid().ToString("N"), 1);
        var tag = $"inbox-fail-{Guid.NewGuid():N}";
        var processor = Processor(db, clock);
        var failed = await processor.ProcessAsync(delivery, async (context, token) =>
        {
            await WriteBusinessAsync(db, context, tenant, tag, token);
            throw new Cp6InboxProcessingException("PROBE_FAIL", retryable: false);
        });
        Expect.True(failed.Disposition == Cp6InboxDisposition.DeadLettered && await CountBusinessAsync(db, tag) == 0, "Handler failure must roll back business and persist actual dead letter independently.");
        Guid inboxId;
        byte[] inboxToken;
        Guid letterId;
        byte[] letterToken;
        await using (var read = Context(db))
        {
            var inbox = await read.Set<Cp6InboxMessage>().AsNoTracking().SingleAsync(row => row.MessageId == delivery.MessageId);
            var letter = await read.Set<Cp6DeadLetterRecord>().AsNoTracking().SingleAsync(row => row.MessageId == delivery.MessageId);
            Expect.Token(inbox.RowVersion);
            Expect.Token(letter.RowVersion);
            inboxId = inbox.Id;
            inboxToken = inbox.RowVersion.ToArray();
            letterId = letter.Id;
            letterToken = letter.RowVersion.ToArray();
        }
        await processor.RequeueDeadLetteredAsync(delivery.ConsumerName, delivery.MessageId, "PROBE_REPLAY");
        Expect.Changed(inboxToken, await db.ReadTokenAsync("Cp6_InboxMessage", inboxId));
        Expect.Changed(letterToken, await db.ReadTokenAsync("Cp6_DeadLetterRecord", letterId));
        var replayed = await processor.ProcessAsync(delivery, (context, token) => WriteBusinessAsync(db, context, tenant, tag, token));
        Expect.True(replayed.Applied && await CountBusinessAsync(db, tag) == 1, "Requeued Inbox must apply business change exactly once.");
        return "Actual failed handler business write rolled back; Inbox/dead letter inserted with 8-byte tokens; requeue updated both tokens and replay applied once.";
    }

    private static async Task<string> InboxDuplicateRaceAsync(ProbeDatabase db, ProbeClock clock)
    {
        var tenant = Guid.NewGuid();
        var tag = $"inbox-race-{Guid.NewGuid():N}";
        var delivery = Delivery("duplicate-race", tenant, Guid.NewGuid().ToString("N"), 1);
        var gate = new TwoPartyGate();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        async Task Handler(PlatformContext context, CancellationToken token)
        {
            await gate.ArriveAsync(token);
            await WriteBusinessAsync(db, context, tenant, tag, token);
        }
        var results = await Task.WhenAll(Processor(db, clock).ProcessAsync(delivery, Handler, timeout.Token), Processor(db, clock).ProcessAsync(delivery, Handler, timeout.Token));
        Expect.True(results.Count(result => result.Applied) == 1 && await CountBusinessAsync(db, tag) == 1, "Concurrent duplicate processing must commit one business effect exactly.");
        Expect.True(results.All(result => result.Disposition is Cp6InboxDisposition.Applied or Cp6InboxDisposition.Duplicate or Cp6InboxDisposition.RetryScheduled), "Concurrent duplicate loser must use safe duplicate/retry disposition.");
        return $"Two actual processors passed a coordinated pre-write barrier; dispositions={string.Join(',', results.Select(result => result.Disposition))}; exactly one committed business effect.";
    }

    private static async Task<string> CheckpointRaceAsync(ProbeDatabase db, ProbeClock clock)
    {
        var tenant = Guid.NewGuid();
        var aggregate = Guid.NewGuid().ToString("N");
        var tag = $"checkpoint-race-{Guid.NewGuid():N}";
        var processor = Processor(db, clock);
        var seed = await processor.ProcessAsync(Delivery("checkpoint-seed", tenant, aggregate, 1), (_, _) => Task.CompletedTask);
        Expect.True(seed.Applied, "Checkpoint race needs an actual committed version-1 checkpoint.");
        var deliveries = new[] { Delivery("checkpoint-a", tenant, aggregate, 2), Delivery("checkpoint-b", tenant, aggregate, 3) };
        var gate = new TwoPartyGate();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        async Task Handler(PlatformContext context, CancellationToken token)
        {
            await gate.ArriveAsync(token);
            await WriteBusinessAsync(db, context, tenant, tag, token);
        }
        var results = await Task.WhenAll(deliveries.Select(delivery => Processor(db, clock).ProcessAsync(delivery, Handler, timeout.Token)));
        Expect.True(results.Count(result => result.Applied) == 1 && await CountBusinessAsync(db, tag) == 1, "Concurrent serializable checkpoint updates must commit only one initial business effect.");
        var winner = Array.FindIndex(results, result => result.Applied);
        var loser = 1 - winner;
        Expect.True(results[loser].Disposition == Cp6InboxDisposition.RetryScheduled, "Serializable checkpoint loser must schedule retry, never dead-letter or report a payload conflict.");
        var secondPass = new List<Cp6InboxProcessingResult>();
        for (var index = 0; index < deliveries.Length; index++)
            if (!results[index].Applied)
                secondPass.Add(await Processor(db, clock).ProcessAsync(deliveries[index], (context, token) => WriteBusinessAsync(db, context, tenant, tag, token)));
        var expectedSecond = deliveries[loser].AggregateVersion > deliveries[winner].AggregateVersion ? Cp6InboxDisposition.Applied : Cp6InboxDisposition.IgnoredOutOfOrder;
        Expect.True(secondPass.Count == 1 && secondPass[0].Disposition == expectedSecond, "Checkpoint second pass must legally apply the newer event or ignore the older one according to the actual winning version.");
        await using var read = Context(db);
        var checkpoint = await read.Set<Cp6InboxAggregateCheckpoint>().AsNoTracking().SingleAsync(row => row.AggregateId == aggregate && row.TenantId == tenant);
        Expect.True(checkpoint.AggregateVersion == 3, "After safe retry/out-of-order handling checkpoint must end at newest version.");
        Expect.Token(checkpoint.RowVersion);
        var effects = await CountBusinessAsync(db, tag);
        var appliedCount = results.Count(result => result.Applied) + secondPass.Count(result => result.Applied);
        Expect.True(effects == appliedCount, "Committed business effects must equal exactly the number of Applied results.");
        var appliedMax = Math.Max(deliveries[winner].AggregateVersion, secondPass[0].Applied ? deliveries[loser].AggregateVersion : 0);
        Expect.True(checkpoint.AggregateVersion == appliedMax, "Final checkpoint maximum must correspond to a legitimately Applied event, never an unsafe discarded event.");
        return $"Coordinated real serializable checkpoint race dispositions={string.Join(',', results.Select(result => result.Disposition))}; second pass={string.Join(',', secondPass.Select(result => result.Disposition))}; final aggregate version=3, committed effects={effects}.";
    }

    private static async Task<string> RetentionAsync(ProbeDatabase db, ProbeClock clock)
    {
        var now = clock.GetUtcNow().AddDays(120);
        var cases = new List<(string Table, Guid Id, bool Remains)>();
        var checkpoints = new List<Guid>();
        foreach (var offsetSeconds in new[] { -1, 0, 1 })
        {
            var at = now.Subtract(Options.PublishedOutboxRetention).AddSeconds(offsetSeconds);
            clock.Set(at);
            await using var publish = Context(db);
            var row = Store(publish, clock).Enqueue(Envelope("retention-published", Guid.NewGuid()));
            await publish.SaveChangesAsync();
            // Discard seed tracking so the package's untracked bulk claim does not return a stale tracked instance.
            publish.ChangeTracker.Clear();
            var claim = (await Store(publish, clock).ClaimBatchAsync("probe-retention-published", Options)).Single(item => item.Message.Id == row.Id);
            await Store(publish, clock).MarkPublishedAsync(claim);
            var stored = await publish.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(item => item.Id == row.Id);
            Expect.True(stored.PublishedAtUtc == at, "Published retention fixture must persist the exact cutoff offset.");
            cases.Add(("Cp6_OutboxMessage", row.Id, offsetSeconds >= 0));
        }
        foreach (var offsetSeconds in new[] { -1, 0, 1 })
        {
            var at = now.Subtract(Options.ProcessedInboxRetention).AddSeconds(offsetSeconds);
            clock.Set(at);
            var tenant = Guid.NewGuid();
            var delivery = Delivery("retention-inbox", tenant, Guid.NewGuid().ToString("N"), 1);
            var inboxResult = await Processor(db, clock).ProcessAsync(delivery, (_, _) => Task.CompletedTask);
            Expect.True(inboxResult.Applied, "Retention Inbox fixture must use the actual successful processor API.");
            await using var read = Context(db);
            var row = await read.Set<Cp6InboxMessage>().AsNoTracking().SingleAsync(item => item.MessageId == delivery.MessageId);
            var checkpoint = await read.Set<Cp6InboxAggregateCheckpoint>().AsNoTracking().SingleAsync(item => item.AggregateId == delivery.AggregateId && item.TenantId == tenant);
            Expect.True(row.ProcessedAtUtc == at, "Inbox retention fixture must persist the exact cutoff offset.");
            cases.Add(("Cp6_InboxMessage", row.Id, offsetSeconds >= 0));
            checkpoints.Add(checkpoint.Id);
        }
        foreach (var offsetSeconds in new[] { -1, 0, 1 })
        {
            var at = now.Subtract(Options.DeadLetterRetention).AddSeconds(offsetSeconds);
            clock.Set(at);
            await using var dead = Context(db);
            var row = Store(dead, clock).Enqueue(Envelope("retention-deadletter", Guid.NewGuid()));
            await dead.SaveChangesAsync();
            dead.ChangeTracker.Clear();
            var claim = (await Store(dead, clock).ClaimBatchAsync("probe-retention-dead", Options)).Single(item => item.Message.Id == row.Id);
            await Store(dead, clock).MarkFailedAsync(claim, "PROBE_RETENTION", false, Options);
            var letter = await dead.Set<Cp6DeadLetterRecord>().AsNoTracking().SingleAsync(item => item.MessageId == row.MessageId);
            Expect.True(letter.CreatedAtUtc == at, "Dead-letter fixture must persist the exact cutoff offset.");
            cases.Add(("Cp6_DeadLetterRecord", letter.Id, offsetSeconds >= 0));
            cases.Add(("Cp6_OutboxMessage", row.Id, offsetSeconds >= 0));
        }

        // Old created-at values alone must not delete pending or currently dispatching messages.
        clock.Set(now.AddDays(-120));
        Guid pendingId;
        Guid activeId;
        await using (var seed = Context(db))
        {
            var active = Store(seed, clock).Enqueue(Envelope("retention-active", Guid.NewGuid()));
            await seed.SaveChangesAsync();
            activeId = active.Id;
        }
        clock.Set(now);
        await using (var active = Context(db))
        {
            var claim = (await Store(active, clock).ClaimBatchAsync("probe-active-lease", Options)).Single(item => item.Message.Id == activeId);
            Expect.True(claim.LeaseExpiresAtUtc > now, "Active lease fixture must be unexpired at retention time.");
        }
        clock.Set(now.AddDays(-120));
        await using (var pending = Context(db))
        {
            var row = Store(pending, clock).Enqueue(Envelope("retention-pending", Guid.NewGuid()));
            await pending.SaveChangesAsync();
            pendingId = row.Id;
        }
        var processingDelivery = Delivery("retention-processing", Guid.NewGuid(), Guid.NewGuid().ToString("N"), 1);
        var processing = await Processor(db, clock).ProcessAsync(processingDelivery, (_, _) => throw new Cp6InboxProcessingException("PROBE_RETRY", retryable: true));
        Expect.True(processing.Disposition == Cp6InboxDisposition.RetryScheduled, "Retention processing fixture must remain unfinished through actual package retry path.");
        clock.Set(now);
        await using var context = Context(db);
        var result = await new Cp6MessageRetentionService<PlatformContext>(context, clock).DeleteExpiredAsync(Options);
        foreach (var item in cases)
        {
            var count = await db.Connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {db.Table(item.Table)} WHERE {db.Column("Id")}=@Id", new { item.Id });
            Expect.True(count == (item.Remains ? 1 : 0), "Actual retention must delete only before cutoff and retain exactly-at/after cutoff rows.");
        }
        Expect.True(await context.Set<Cp6OutboxMessage>().AnyAsync(item => item.Id == pendingId && item.Status == Cp6OutboxStatus.Pending), "Old pending message must survive retention.");
        Expect.True(await context.Set<Cp6OutboxMessage>().AnyAsync(item => item.Id == activeId && item.Status == Cp6OutboxStatus.Dispatching && item.LeaseExpiresAtUtc > now), "Old message with active lease must survive retention.");
        Expect.True(await context.Set<Cp6InboxMessage>().AnyAsync(item => item.MessageId == processingDelivery.MessageId && item.Status == Cp6InboxStatus.Processing), "Old unfinished Inbox must survive retention.");
        Expect.True(await context.Set<Cp6InboxAggregateCheckpoint>().CountAsync(item => checkpoints.Contains(item.Id)) == checkpoints.Count, "Retention must preserve durable aggregate checkpoints.");
        return $"Actual ExecuteDelete verified 12 per-row cutoff assertions (before removed; equal/after retained), pending/active lease/processing Inbox and all checkpoints retained; deletions outbox={result.OutboxDeleted}, inbox={result.InboxDeleted}, deadletters={result.DeadLettersDeleted}.";
    }

    private static async Task<string> ReplayRaceAsync(ProbeDatabase db, ProbeClock clock)
    {
        Guid letterId;
        await using (var dead = Context(db))
        {
            var message = Store(dead, clock).Enqueue(Envelope("replay-race", Guid.NewGuid()));
            await dead.SaveChangesAsync();
            dead.ChangeTracker.Clear();
            var claim = (await Store(dead, clock).ClaimBatchAsync("probe-replay-race", Options)).Single(item => item.Message.Id == message.Id);
            await Store(dead, clock).MarkFailedAsync(claim, "PROBE_REPLAY_RACE", false, Options);
            letterId = (await dead.Set<Cp6DeadLetterRecord>().AsNoTracking().SingleAsync(item => item.MessageId == message.MessageId)).Id;
        }
        await using var a = Context(db);
        await using var b = Context(db);
        var one = await a.Set<Cp6DeadLetterRecord>().SingleAsync(item => item.Id == letterId);
        var two = await b.Set<Cp6DeadLetterRecord>().SingleAsync(item => item.Id == letterId);
        Expect.True(one.RowVersion.SequenceEqual(two.RowVersion), "Replay race contexts must start from the same actual dead-letter token.");
        one.RecordReplay("PROBE_REPLAY_A", clock.GetUtcNow());
        two.RecordReplay("PROBE_REPLAY_B", clock.GetUtcNow());
        var original = one.RowVersion.ToArray();
        await a.SaveChangesAsync();
        Expect.Changed(original, one.RowVersion);
        var rejected = false;
        try { await b.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { rejected = true; }
        Expect.True(rejected, "Second actual dead-letter replay from stale token must be rejected.");
        await using var observer = Context(db);
        var stored = await observer.Set<Cp6DeadLetterRecord>().AsNoTracking().SingleAsync(item => item.Id == letterId);
        Expect.True(stored.ReplayReasonCode == "PROBE_REPLAY_A" && stored.ReplayedAtUtc is not null, "Stale replay cannot overwrite winner's audit reason/time.");
        return "Two actual package dead-letter entities read the same token; public RecordReplay winner committed replacement bytes, stale context threw DbUpdateConcurrencyException and could not overwrite replay audit.";
    }

    private static async Task<string> OffsetContractAsync(ProbeDatabase db)
    {
        var clock = new ProbeClock();
        var nonzero = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(2));
        clock.Set(nonzero);
        Guid id;
        var rejected = false;
        await using (var write = Context(db))
        {
            var message = Store(write, clock).Enqueue(Envelope("offset-contract", Guid.NewGuid()));
            id = message.Id;
            try { await write.SaveChangesAsync(); }
            catch (DbUpdateException exception) when (db.Provider == ProbeProvider.PostgreSql && exception.InnerException is ArgumentException) { rejected = true; }
        }
        await using var read = Context(db);
        if (db.Provider == ProbeProvider.SqlServer)
        {
            var stored = await read.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(item => item.Id == id);
            Expect.True(stored.CreatedAtUtc.Offset == nonzero.Offset && stored.CreatedAtUtc.UtcDateTime == nonzero.UtcDateTime, "SQL Server datetimeoffset must really round-trip the supplied offset and UTC instant.");
            Expect.Token(stored.RowVersion);
            return "Actual SQL Server datetimeoffset parameter with +02:00 round-tripped the same offset and UTC instant. Shared Platform/API time contract still requires explicit UTC inputs for PostgreSQL compatibility.";
        }
        Expect.True(rejected && !await read.Set<Cp6OutboxMessage>().AnyAsync(item => item.Id == id), "PostgreSQL must explicitly reject nonzero DateTimeOffset parameter and commit no row.");
        clock.Set(nonzero.ToUniversalTime());
        await using var utc = Context(db);
        var normalized = Store(utc, clock).Enqueue(Envelope("offset-normalized", Guid.NewGuid()));
        await utc.SaveChangesAsync();
        var utcStored = await utc.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(item => item.Id == normalized.Id);
        Expect.True(utcStored.CreatedAtUtc.Offset == TimeSpan.Zero && utcStored.CreatedAtUtc.UtcDateTime == nonzero.UtcDateTime, "Explicit UTC normalization must retain the instant when PostgreSQL saves and reads back.");
        Expect.Token(utcStored.RowVersion);
        return "Actual Npgsql/EF nonzero +02:00 parameter rejected before commit; explicit ToUniversalTime succeeded and read zero offset with the same instant. This freezes strict UTC caller input, not provider-equivalent arbitrary offsets.";
    }

    private static async Task<string> SerializationFailureAsync(ProbeDatabase db, ProbeClock clock)
    {
        // Force a genuine PostgreSQL 40001 inside the package's serializable handler transaction.
        var tenant = Guid.NewGuid();
        var aggregate = Guid.NewGuid().ToString("N");
        var id = Guid.NewGuid();
        await db.Connection.ExecuteAsync($"INSERT INTO {db.Table("BusinessRecords")} (\"Id\",\"TenantId\",\"Value\") VALUES (@Id,@Tenant,@Value)", new { Id = id, Tenant = tenant, Value = "serialization fixture" });
        var delivery = Delivery("40001", tenant, aggregate, 1);
        var observedState = "none";
        var result = await Processor(db, clock).ProcessAsync(delivery, async (context, token) =>
        {
            var connection = context.Database.GetDbConnection();
            var transaction = context.Database.CurrentTransaction!.GetDbTransaction();
            await connection.QuerySingleAsync<string>("SELECT \"Value\" FROM " + db.Table("BusinessRecords") + " WHERE \"Id\"=@Id", new { Id = id }, transaction);
            await using var competitor = db.NewConnection();
            await competitor.OpenAsync(token);
            await competitor.ExecuteAsync("UPDATE " + db.Table("BusinessRecords") + " SET \"Value\"=@Value WHERE \"Id\"=@Id", new { Id = id, Value = "competitor committed" });
            try
            {
                await connection.ExecuteAsync("UPDATE " + db.Table("BusinessRecords") + " SET \"Value\"=@Value WHERE \"Id\"=@Id", new { Id = id, Value = "serialization loser" }, transaction);
            }
            catch (Npgsql.PostgresException exception) { observedState = exception.SqlState; throw; }
        });
        Expect.True(observedState == "40001", "Experiment must observe a real PostgreSQL serialization failure, not a synthetic exception.");
        Expect.True(result.Disposition == Cp6InboxDisposition.RetryScheduled, "Actual package generic exception path must schedule retry for actual serialization failure.");
        var committed = await db.Connection.QuerySingleAsync<string>("SELECT \"Value\" FROM " + db.Table("BusinessRecords") + " WHERE \"Id\"=@Id", new { Id = id });
        Expect.True(committed == "competitor committed", "Serialization loser must not lose competitor's committed write.");
        return "Actual PostgreSQL SQLSTATE 40001 observed; Platform 0.10.2 generic exception path returned RetryScheduled, rolled back loser and retained competitor. This is behavior evidence, not provider-specific error classification.";
    }

    private sealed class TwoPartyGate
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ArriveAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref _arrivals) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(token);
        }
    }

    private sealed class OutboxClaimGateInterceptor(Guid candidate) : DbCommandInterceptor
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Arrivals => Volatile.Read(ref _arrivals);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("Cp6_OutboxMessage", StringComparison.Ordinal) && SelectsCandidate(command))
            {
                if (Interlocked.Increment(ref _arrivals) == 2) _ready.TrySetResult();
                await _ready.Task.WaitAsync(cancellationToken);
            }
            return result;
        }

        private bool SelectsCandidate(DbCommand command) => command.CommandText.Contains(candidate.ToString(), StringComparison.OrdinalIgnoreCase) ||
            command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value switch
            {
                Guid value => value == candidate,
                Guid[] values => values.Contains(candidate),
                string value => value.Contains(candidate.ToString(), StringComparison.OrdinalIgnoreCase),
                _ => false
            });
    }
}
