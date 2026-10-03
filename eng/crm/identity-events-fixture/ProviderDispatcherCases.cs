using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using CP6.WebApi.BackgroundServices;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

sealed partial class IdentitySqlFixture
{
    public string? DispatcherEvidenceDirectory { private get; set; }

    public async Task ProviderIndependentDispatchersAsync()
    {
        var tenant = await SeedAsync();
        var runtime = Runtime(tenant);
        var grant = await GrantAsync(tenant);
        await new SqlCrmOidcGrantStore(database, connection, runtime).RevokeAsync(grant.Id);

        var ordinaryFactory = InvokeDispatcherMethod<IDbContextFactory<CP6Context>>(
            "CreateOrdinaryContextFactory", [typeof(DatabaseOptions), typeof(string)], database, connection);
        var priorityFactory = InvokeDispatcherMethod<IDbContextFactory<IdentityMessagingContext>>(
            "CreatePriorityContextFactory", [typeof(DatabaseOptions), typeof(string)], database, connection);
        VerifyDispatcherFactory(ordinaryFactory, DatabaseContextKind.Core);
        VerifyDispatcherFactory(priorityFactory, DatabaseContextKind.IdentityPriority);
        var ordinaryOptions = ProductionDispatchOptions(32);
        var priorityOptions = ProductionDispatchOptions(16);
        var ordinaryTargets = await ReadQueueAsync(ordinaryFactory, tenant);
        var priorityTargets = await ReadQueueAsync(priorityFactory, tenant);
        Require(ordinaryTargets.Length == 5 && priorityTargets.Length == 1
            && ordinaryTargets.Concat(priorityTargets).All(row => row.Status == Cp6OutboxStatus.Pending && row.AttemptCount == 0),
            "C02_DISPATCH_TARGETS_MUST_BE_ACTUAL_NEW_PRODUCER_MESSAGES");
        var priorityTarget = priorityTargets.Single();
        var priorityAggregate = IdentityEventContracts.TokenAggregate(Issuer, grant.Id.ToString("D"));
        Require(priorityTarget.AggregateId == priorityAggregate && priorityTarget.TenantId == tenant,
            "C02_DISPATCH_PRIORITY_TARGET_MUST_BE_REAL_REVOKED_GRANT");
        var now = runtime.Clock.GetUtcNow();
        var ordinaryEligible = await EligibleQueueCountAsync(ordinaryFactory, now);
        var priorityEligible = await EligibleQueueCountAsync(priorityFactory, now);
        var ordinaryRounds = BoundedDispatchRounds(ordinaryEligible, 32);
        var priorityRounds = BoundedDispatchRounds(priorityEligible, 16);
        Console.WriteLine($"C02 dispatcher existing eligible ordinary={ordinaryEligible}, priority={priorityEligible}; budgets=32/16; recording publisher component only.");

        var ordinaryPublisher = new DispatcherRecordingPublisher(tenant, blockFirst: true);
        var priorityPublisher = new DispatcherRecordingPublisher(tenant, blockFirst: false);
        var ordinary = new Cp6OutboxDispatcher<CP6Context>(ordinaryFactory, runtime.Validator, ordinaryPublisher, ordinaryOptions);
        var priority = new Cp6OutboxDispatcher<IdentityMessagingContext>(priorityFactory, runtime.Validator, priorityPublisher, priorityOptions);
        var ordinaryWorker = "c02-fixture-ordinary-" + Guid.NewGuid().ToString("N");
        var priorityWorker = "c02-fixture-priority-" + Guid.NewGuid().ToString("N");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var tasks = new List<Task>();
        var priorityBatches = 0;
        var ordinaryBatches = 0;
        var priorityProgressedWhileBlocked = false;
        var succeeded = false;
        var joined = false;
        try
        {
            var blockedBatch = ordinary.DispatchBatchAsync(ordinaryWorker, budget.Token);
            tasks.Add(blockedBatch);
            var blockedMessage = await ordinaryPublisher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), budget.Token);
            Require(!blockedBatch.IsCompleted && !ordinaryPublisher.Released, "C02_ORDINARY_PUBLISH_NOT_REALLY_BLOCKED");
            await using (var read = ordinaryFactory.CreateDbContext())
            {
                var leased = await read.Set<Cp6OutboxMessage>().AsNoTracking()
                    .SingleAsync(row => row.MessageId == blockedMessage, budget.Token);
                Require(leased.Status == Cp6OutboxStatus.Dispatching && leased.LeaseOwner == ordinaryWorker
                    && leased.LeaseToken is not null && leased.LeaseExpiresAtUtc > runtime.Clock.GetUtcNow(),
                    "C02_BLOCKED_ORDINARY_MESSAGE_HAS_NO_ACTUAL_LEASE");
            }
            for (; priorityBatches < priorityRounds; priorityBatches++)
            {
                var batch = priority.DispatchBatchAsync(priorityWorker, budget.Token);
                tasks.Add(batch);
                var result = await batch;
                Require(result.Claimed <= 16 && result.DeadLettered == 0, "C02_PRIORITY_PRODUCTION_BATCH_BUDGET_OR_CONTRACT_FAILURE");
                if (await IsPublishedAsync(priorityFactory, priorityTarget.MessageId, budget.Token))
                {
                    priorityBatches++;
                    break;
                }
                Require(result.Claimed > 0, "C02_PRIORITY_TARGET_NOT_REACHED_BY_NORMAL_ELIGIBLE_BATCHES");
            }
            Require(!blockedBatch.IsCompleted && !ordinaryPublisher.Released
                && await IsPublishedAsync(priorityFactory, priorityTarget.MessageId, budget.Token),
                "C02_PRIORITY_DID_NOT_PROGRESS_WHILE_ORDINARY_WAS_BLOCKED");
            await AssertDispatcherPublicationAsync(priorityFactory, priorityTarget, priorityPublisher, budget.Token);
            var stillOrdinary = await ReadQueueAsync(ordinaryFactory, tenant, budget.Token);
            Require(stillOrdinary.All(row => row.Status != Cp6OutboxStatus.Published),
                "C02_ORDINARY_TARGET_ESCAPED_BLOCKED_PUBLISH");
            priorityProgressedWhileBlocked = true;

            ordinaryPublisher.Release();
            var firstResult = await blockedBatch;
            ordinaryBatches = 1;
            Require(firstResult.Claimed <= 32 && firstResult.DeadLettered == 0, "C02_ORDINARY_PRODUCTION_BATCH_BUDGET_OR_CONTRACT_FAILURE");
            while (!await ArePublishedAsync(ordinaryFactory, ordinaryTargets, budget.Token) && ordinaryBatches < ordinaryRounds)
            {
                var batch = ordinary.DispatchBatchAsync(ordinaryWorker, budget.Token);
                tasks.Add(batch);
                var result = await batch;
                ordinaryBatches++;
                Require(result.Claimed is > 0 and <= 32 && result.DeadLettered == 0,
                    "C02_ORDINARY_TARGETS_NOT_REACHED_BY_NORMAL_ELIGIBLE_BATCHES");
            }
            Require(await ArePublishedAsync(ordinaryFactory, ordinaryTargets, budget.Token), "C02_ORDINARY_TARGETS_NOT_PUBLISHED_AFTER_RELEASE");
            foreach (var target in ordinaryTargets)
                await AssertDispatcherPublicationAsync(ordinaryFactory, target, ordinaryPublisher, budget.Token);
            succeeded = true;
        }
        finally
        {
            ordinaryPublisher.Release();
            if (!succeeded) await budget.CancelAsync();
            try
            {
                // Pass the real budget token to every dispatcher; always join before the case exits.
                await Task.WhenAll(tasks);
                joined = tasks.All(task => task.IsCompleted);
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested)
            {
                joined = tasks.All(task => task.IsCompleted);
                if (succeeded) throw;
            }
            finally
            {
                joined = tasks.All(task => task.IsCompleted);
                Require(joined, "C02_DISPATCHER_TASK_SURVIVED_CASE_CLEANUP");
                if (DispatcherEvidenceDirectory is not null)
                    await File.WriteAllTextAsync(Path.Combine(DispatcherEvidenceDirectory, "dispatcher-component-proof.json"),
                        JsonSerializer.Serialize(new
                        {
                            schemaId = "cp6.c02.provider-dispatcher-component-verification.v1",
                            provider = database.Provider.ToString(), success = succeeded && joined,
                            boundary = "actual production factories/options and Platform dispatcher with recording publisher; no Dapr/Kafka/CRM delivery",
                            platformAssemblySha256 = IdentityEventContracts.Hash(File.ReadAllBytes(typeof(Cp6OutboxDispatcher<>).Assembly.Location)),
                            webApiAssemblySha256 = IdentityEventContracts.Hash(File.ReadAllBytes(typeof(IdentityEventDispatchWorker).Assembly.Location)),
                            existingEligibleOrdinary = ordinaryEligible, existingEligiblePriority = priorityEligible,
                            ordinaryBatchSize = ordinaryOptions.DispatchBatchSize, priorityBatchSize = priorityOptions.DispatchBatchSize,
                            ordinaryBatches, priorityBatches, priorityProgressedWhileBlocked,
                            ordinaryReleased = ordinaryPublisher.Released, allDispatcherTasksJoined = joined,
                            ordinaryTargetCount = ordinaryTargets.Length, priorityTargetCount = priorityTargets.Length,
                            targetPayloadSha256 = ordinaryTargets.Concat(priorityTargets).Select(row => new
                            {
                                row.MessageId, row.AggregateId, row.AggregateVersion, row.PartitionKey,
                                sha256 = IdentityEventContracts.Hash(row.Payload)
                            }), observedAtUtc = DateTimeOffset.UtcNow
                        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            }
        }
    }

    private static T InvokeDispatcherMethod<T>(string name, Type[] arguments, params object[] values) where T : class
    {
        var method = typeof(IdentityEventDispatchWorker).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic,
            binder: null, types: arguments, modifiers: null)
            ?? throw new InvalidOperationException("C02_PRODUCTION_DISPATCHER_METHOD_MISSING");
        return method.Invoke(null, values) as T ?? throw new InvalidOperationException("C02_PRODUCTION_DISPATCHER_METHOD_TYPE_MISMATCH");
    }

    private void VerifyDispatcherFactory<T>(IDbContextFactory<T> factory, DatabaseContextKind kind) where T : DbContext
    {
        using var context = factory.CreateDbContext();
        var profile = DatabaseMigrationProfile.For(database, kind);
        var relational = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());
        Require(context.Database.ProviderName == (database.Provider == DatabaseProvider.PostgreSql
                ? "Npgsql.EntityFrameworkCore.PostgreSQL" : "Microsoft.EntityFrameworkCore.SqlServer")
            && relational.MigrationsAssembly == profile.MigrationsAssembly
            && relational.MigrationsHistoryTableName == profile.HistoryTable
            && relational.MigrationsHistoryTableSchema == profile.HistorySchema,
            "C02_ACTUAL_PRODUCTION_DISPATCHER_FACTORY_PROVIDER_OR_PROFILE_MISMATCH");
        Require(database.Provider == DatabaseProvider.PostgreSql
                ? context.Database.GetDbConnection() is Npgsql.NpgsqlConnection
                : context.Database.GetDbConnection() is Microsoft.Data.SqlClient.SqlConnection,
            "C02_ACTUAL_PRODUCTION_DISPATCHER_FACTORY_CONNECTION_TYPE_MISMATCH");
    }

    private static Cp6TransactionalMessagingOptions ProductionDispatchOptions(int batchSize)
    {
        var options = InvokeDispatcherMethod<Cp6TransactionalMessagingOptions>("Options", [typeof(int)], batchSize);
        Require(options.DispatchBatchSize == batchSize && options.OutboxLeaseDuration == TimeSpan.FromMinutes(5)
            && options.MaxOutboxAttempts == 10 && options.InitialOutboxRetryDelay == TimeSpan.FromSeconds(1)
            && options.MaximumOutboxRetryDelay == TimeSpan.FromMinutes(5), "C02_PRODUCTION_DISPATCHER_OPTIONS_CHANGED");
        return options;
    }

    private static int BoundedDispatchRounds(int eligible, int size)
    {
        var rounds = (eligible + size - 1) / size + 2;
        Require(rounds <= 128, "C02_DISPATCH_FIXTURE_BACKLOG_EXCEEDS_BOUNDED_BUDGET");
        return rounds;
    }

    private static async Task<int> EligibleQueueCountAsync<T>(IDbContextFactory<T> factory, DateTimeOffset now) where T : DbContext
    {
        await using var context = factory.CreateDbContext();
        return await context.Set<Cp6OutboxMessage>().AsNoTracking().CountAsync(row =>
            row.Status == Cp6OutboxStatus.Pending && row.AvailableAtUtc <= now
            || row.Status == Cp6OutboxStatus.Dispatching && row.LeaseExpiresAtUtc <= now);
    }

    private static async Task<Cp6OutboxMessage[]> ReadQueueAsync<T>(IDbContextFactory<T> factory, Guid tenant,
        CancellationToken token = default) where T : DbContext
    {
        await using var context = factory.CreateDbContext();
        return await context.Set<Cp6OutboxMessage>().AsNoTracking().Where(row => row.TenantId == tenant).ToArrayAsync(token);
    }

    private static async Task<bool> IsPublishedAsync<T>(IDbContextFactory<T> factory, string messageId, CancellationToken token) where T : DbContext
    {
        await using var context = factory.CreateDbContext();
        return await context.Set<Cp6OutboxMessage>().AsNoTracking().AnyAsync(row => row.MessageId == messageId
            && row.Status == Cp6OutboxStatus.Published, token);
    }

    private static async Task<bool> ArePublishedAsync<T>(IDbContextFactory<T> factory, Cp6OutboxMessage[] targets, CancellationToken token) where T : DbContext
    {
        var ids = targets.Select(row => row.MessageId).ToArray();
        await using var context = factory.CreateDbContext();
        return await context.Set<Cp6OutboxMessage>().AsNoTracking().CountAsync(row => ids.Contains(row.MessageId)
            && row.Status == Cp6OutboxStatus.Published, token) == targets.Length;
    }

    private static async Task AssertDispatcherPublicationAsync<T>(IDbContextFactory<T> factory, Cp6OutboxMessage original,
        DispatcherRecordingPublisher publisher, CancellationToken token) where T : DbContext
    {
        Require(publisher.Targets.TryGetValue(original.MessageId, out var observed)
            && observed.Tenant == original.TenantId && observed.Topic == original.TopicName
            && observed.Partition == original.PartitionKey && observed.Partition == $"{original.TenantId:D}:{original.AggregateId}"
            && observed.Correlation == original.CorrelationId && observed.Causation == original.CausationId
            && observed.Aggregate == original.AggregateId && observed.Version == original.AggregateVersion
            && observed.Payload.AsSpan().SequenceEqual(original.Payload), "C02_DISPATCH_ORIGINAL_BYTES_OR_METADATA_CHANGED");
        await using var context = factory.CreateDbContext();
        var persisted = await context.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(row => row.Id == original.Id, token);
        Require(persisted.Status == Cp6OutboxStatus.Published && persisted.PublishedAtUtc is not null && persisted.AttemptCount == 1
            && persisted.LeaseOwner is null && persisted.LeaseToken is null && persisted.LeaseExpiresAtUtc is null
            && persisted.Payload.AsSpan().SequenceEqual(original.Payload) && persisted.PartitionKey == original.PartitionKey
            && persisted.RowVersion.Length == 8 && !persisted.RowVersion.AsSpan().SequenceEqual(original.RowVersion),
            "C02_DISPATCH_PUBLISHED_STATE_OR_TOKEN_NOT_ACTUALLY_PERSISTED");
    }

    private sealed class DispatcherRecordingPublisher(Guid targetTenant, bool blockFirst) : ICp6OutboxPublisher
    {
        private int entered;
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentDictionary<string, RecordedPublication> Targets { get; } = new(StringComparer.Ordinal);
        public bool Released => release.Task.IsCompleted;
        public void Release() => release.TrySetResult();

        public async Task PublishAsync(Cp6OutboxDispatchMessage message, CancellationToken cancellationToken = default)
        {
            if (blockFirst && Interlocked.CompareExchange(ref entered, 1, 0) == 0)
            {
                Entered.TrySetResult(message.MessageId);
                await release.Task.WaitAsync(cancellationToken);
            }
            if (message.TenantId == targetTenant)
                Require(Targets.TryAdd(message.MessageId, new(message.TenantId, message.TopicName, message.PartitionKey,
                    message.Payload.ToArray(), message.CorrelationId, message.CausationId, message.AggregateId, message.AggregateVersion)),
                    "C02_DISPATCH_TARGET_PUBLISHED_MORE_THAN_ONCE");
        }
    }

    private sealed record RecordedPublication(Guid Tenant, string Topic, string Partition, byte[] Payload,
        string Correlation, string Causation, string Aggregate, int Version);
}
