using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using Dapr.Client;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.BackgroundServices;

public sealed class IdentityEventDispatchWorker(CrmIdentityRuntime runtime, IConfiguration configuration,
    ILogger<IdentityEventDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = configuration.GetConnectionString("DefaultConnection")!;
        using var dapr = new DaprClientBuilder().UseHttpEndpoint(runtime.Options.DaprHttpEndpoint)
            .UseGrpcEndpoint(runtime.Options.DaprGrpcEndpoint).Build();
        using var invocation = new HttpClient { BaseAddress = new Uri(runtime.Options.DaprHttpEndpoint), Timeout = TimeSpan.FromSeconds(10) };
        var publisher = new IdentityDaprPublisher(new Cp6DaprTransport(dapr, invocation), runtime.Validator);
        var ordinary = new Cp6OutboxDispatcher<CP6Context>(new ContextFactory<CP6Context>(() => new CP6Context(
            new DbContextOptionsBuilder<CP6Context>().UseSqlServer(connection).Options)), runtime.Validator, publisher, Options(32));
        var priority = new Cp6OutboxDispatcher<IdentityMessagingContext>(new ContextFactory<IdentityMessagingContext>(() => new IdentityMessagingContext(
            new DbContextOptionsBuilder<IdentityMessagingContext>().UseSqlServer(connection).Options)), runtime.Validator, publisher, Options(16));
        // Independent asynchronous loops: a blocked ordinary publish never consumes the priority budget.
        await Task.WhenAll(LoopAsync("priority", priority.DispatchBatchAsync, stoppingToken),
            LoopAsync("ordinary", ordinary.DispatchBatchAsync, stoppingToken));
    }

    private async Task LoopAsync(string queue, Func<string, CancellationToken, Task<Cp6OutboxDispatchResult>> dispatch, CancellationToken cancellationToken)
    {
        var worker = "c02-" + queue + "-" + Guid.NewGuid().ToString("N");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await dispatch(worker, cancellationToken);
                if (result.DeadLettered > 0) logger.LogError("C02 {Queue} queue dead-lettered {Count} messages.", queue, result.DeadLettered);
                await Task.Delay(result.Claimed == 0 ? 500 : 50, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("C02 {Queue} queue is temporarily unavailable.", queue);
                try { await Task.Delay(2000, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            }
        }
    }

    private static Cp6TransactionalMessagingOptions Options(int size) => new()
    {
        DispatchBatchSize = size, OutboxLeaseDuration = TimeSpan.FromMinutes(5), MaxOutboxAttempts = 10,
        InitialOutboxRetryDelay = TimeSpan.FromSeconds(1), MaximumOutboxRetryDelay = TimeSpan.FromMinutes(5)
    };

    private sealed class ContextFactory<T>(Func<T> create) : IDbContextFactory<T> where T : DbContext
    {
        public T CreateDbContext() => create();
    }
}

public sealed class IdentityDaprPublisher(ICp6DaprTransport transport, IdentityEventValidator validator) : ICp6OutboxPublisher
{
    public async Task PublishAsync(Cp6OutboxDispatchMessage message, CancellationToken cancellationToken = default)
    {
        var envelope = new Cp6OutboxEnvelope(message.MessageId, message.TenantId, message.TopicName, message.PartitionKey,
            message.Payload, message.CorrelationId, message.CausationId, message.AggregateId, message.AggregateVersion);
        if (!validator.Validate(envelope).IsValid) throw new Cp6OutboxPublishException("C02_IDENTITY_CONTRACT_INVALID", false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        // The payload is already a validated structured CloudEvent. Dapr's default wrapping
        // reparses JSON and changes the snapshot data hash used by consumer reconciliation.
        await transport.PublishAsync("cp6-kafka-pubsub", IdentityEventContracts.Topic, message.Payload,
            Cp6CloudEventCodec.StructuredContentType, new Dictionary<string, string>
            { ["partitionKey"] = message.PartitionKey, ["rawPayload"] = "true" }, deadline.Token);
    }
}
