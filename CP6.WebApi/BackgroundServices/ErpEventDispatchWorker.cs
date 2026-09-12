using CP6.Core.Services.ErpIntegration;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using Dapr.Client;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.BackgroundServices;

public sealed class ErpEventDispatchWorker(IDbContextFactory<ErpIntegrationContext> factory, ErpIntegrationRuntime runtime,
    ILogger<ErpEventDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var dapr = new DaprClientBuilder().UseHttpEndpoint(runtime.Options.DaprHttpEndpoint)
            .UseGrpcEndpoint(runtime.Options.DaprGrpcEndpoint).UseDaprApiToken(runtime.Options.DaprApiToken).Build();
        using var invocation = new HttpClient { BaseAddress = new Uri(runtime.Options.DaprHttpEndpoint), Timeout = TimeSpan.FromSeconds(10) };
        invocation.DefaultRequestHeaders.Add("dapr-api-token", runtime.Options.DaprApiToken);
        var dispatcher = new Cp6OutboxDispatcher<ErpIntegrationContext>(factory, runtime.Validator,
            new ErpDaprPublisher(new Cp6DaprTransport(dapr, invocation), runtime.Validator), new()
            {
                DispatchBatchSize = 32, OutboxLeaseDuration = TimeSpan.FromMinutes(5), MaxOutboxAttempts = 10,
                InitialOutboxRetryDelay = TimeSpan.FromSeconds(1), MaximumOutboxRetryDelay = TimeSpan.FromMinutes(5)
            });
        var worker = "c03-results-" + Guid.NewGuid().ToString("N");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await dispatcher.DispatchBatchAsync(worker, stoppingToken);
                if (result.DeadLettered > 0) logger.LogError("C03 result Outbox dead-lettered {Count} messages.", result.DeadLettered);
                await Task.Delay(result.Claimed == 0 ? 500 : 50, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("C03 result dispatch is temporarily unavailable.");
                try { await Task.Delay(2000, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}

public sealed class ErpDaprPublisher(ICp6DaprTransport transport, ErpEventValidator validator) : ICp6OutboxPublisher
{
    public async Task PublishAsync(Cp6OutboxDispatchMessage message, CancellationToken cancellationToken = default)
    {
        var envelope = new Cp6OutboxEnvelope(message.MessageId, message.TenantId, message.TopicName, message.PartitionKey,
            message.Payload, message.CorrelationId, message.CausationId, message.AggregateId, message.AggregateVersion);
        if (message.TopicName != ErpEventContracts.ResultTopic || !validator.Validate(envelope).IsValid)
            throw new Cp6OutboxPublishException("C03_RESULT_CONTRACT_INVALID", false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        await transport.PublishAsync("cp6-kafka-pubsub", ErpEventContracts.ResultTopic, message.Payload,
            Cp6CloudEventCodec.StructuredContentType, new Dictionary<string, string>
            { ["partitionKey"] = message.PartitionKey, ["rawPayload"] = "true" }, deadline.Token);
    }
}
