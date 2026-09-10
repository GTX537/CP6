using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using CP6.WebApi.BackgroundServices;
using Dapr.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

// This narrow diagnostic uses the actual producer adapter and SDK transport. It does not
// replace the SQL/Core/CRM acceptance and never labels a candidate metadata variant delivered.
static class IdentityTransportProbe
{
    public static async Task<int> RunAsync(string outputDirectory, string diagnosticDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(diagnosticDirectory);
        var observations = new List<object>();
        var complete = false;
        var source = Environment.GetEnvironmentVariable("CP6_C02_PROBE_SOURCE") ?? throw new InvalidOperationException("C02_SOURCE_REQUIRED");
        var secret = Environment.GetEnvironmentVariable("APP_API_TOKEN") ?? throw new InvalidOperationException("C02_APP_TOKEN_REQUIRED");
        var port = int.Parse(Environment.GetEnvironmentVariable("CP6_C02_PROBE_APP_PORT")!);
        var endpoint = Environment.GetEnvironmentVariable("CP6_C02_PROBE_HTTP")!;
        var grpcEndpoint = Environment.GetEnvironmentVariable("CP6_C02_PROBE_GRPC")!;
        var received = new ConcurrentDictionary<string, TaskCompletionSource<Delivery>>();
        var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Any, port));
        await using var app = builder.Build();
        bool Authorized(HttpContext context) => context.Request.Headers.TryGetValue("dapr-api-token", out var values) && values.Count == 1 &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(values[0]!), Encoding.UTF8.GetBytes(secret));
        app.MapGet("/dapr/subscribe", (HttpContext context) =>
        {
            if (!Authorized(context)) return Results.Unauthorized();
            subscribed.TrySetResult();
            return Results.Json(new[] { new { pubsubname = "cp6-kafka-pubsub", topic = IdentityEventContracts.Topic,
                route = "/events", metadata = new { rawPayload = "false" } } });
        });
        app.MapPost("/events", async (HttpContext context) =>
        {
            if (!Authorized(context)) return Results.Unauthorized();
            using var stream = new MemoryStream();
            await context.Request.Body.CopyToAsync(stream);
            var bytes = stream.ToArray();
            using var json = JsonDocument.Parse(bytes);
            if (received.TryGetValue(json.RootElement.GetProperty("id").GetString()!, out var waiting))
                waiting.TrySetResult(new(bytes, context.Request.ContentType ?? "", Header("__topic"), Header("__key"), Header("pubsubname")));
            return Results.Json(new { status = "SUCCESS" });
            string Header(string name) => context.Request.Headers.TryGetValue(name, out var values) && values.Count == 1 ? values[0]! : "";
        });
        try
        {
            await app.StartAsync();
            await subscribed.Task.WaitAsync(TimeSpan.FromSeconds(60));
            using var dapr = new DaprClientBuilder().UseHttpEndpoint(endpoint).UseGrpcEndpoint(grpcEndpoint).Build();
            using var http = new HttpClient { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromSeconds(10) };
            var transport = new Cp6DaprTransport(dapr, http);
            var validator = new IdentityEventValidator(Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/platform")), "https://identity.cp6.test");
            var publisher = new IdentityDaprPublisher(transport, validator);
            var allPassed = true;
            foreach (var mode in new[] { "current-producer", "reserved-header-spoof", "wrong-broker-key" })
            {
                var tenant = Guid.NewGuid();
                var envelope = IdentityEventContracts.Create(tenant, "permission:role:2", 1, IdentityEventContracts.PermissionChanged,
                    new PermissionIdentityData(tenant, "role:2", true, 1,
                        [new("crm-lead", "query", 2, [], [new("company", 1)])]), "us", Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
                var completion = new TaskCompletionSource<Delivery>(TaskCreationOptions.RunContinuationsAsynchronously);
                received[envelope.MessageId] = completion;
                if (mode == "current-producer")
                    await publisher.PublishAsync(new(Guid.NewGuid(), envelope.MessageId, tenant, envelope.TopicName, envelope.PartitionKey,
                        envelope.Payload, envelope.CorrelationId, envelope.CausationId, envelope.AggregateId, envelope.AggregateVersion, 1));
                else
                    await transport.PublishAsync("cp6-kafka-pubsub", IdentityEventContracts.Topic, envelope.Payload,
                        Cp6CloudEventCodec.StructuredContentType, new Dictionary<string, string>
                        {
                            // __key is a producer alias that sets the actual broker key, even if its
                            // header is excluded. Never combine the two aliases in a positive case.
                            ["partitionKey"] = mode == "wrong-broker-key" ? "forged-key" : envelope.PartitionKey,
                            ["rawPayload"] = "true", ["__ToPiC"] = "forged-topic", ["__KeY"] = "forged-header-key",
                            ["PubSubName"] = "forged-pubsub"
                        });
                var delivery = await completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
                var sourceBytes = envelope.Payload.ToArray();
                var bytesEqual = sourceBytes.AsSpan().SequenceEqual(delivery.Bytes);
                var sourceDataHash = DataHash(sourceBytes);
                var receivedDataHash = DataHash(delivery.Bytes);
                var metadataValid = delivery.Topic == IdentityEventContracts.Topic && delivery.Key == envelope.PartitionKey && delivery.Pubsub == "cp6-kafka-pubsub";
                File.WriteAllText(Path.Combine(diagnosticDirectory, mode + "-metadata.json"), JsonSerializer.Serialize(new
                { expectedTopic = IdentityEventContracts.Topic, expectedKey = envelope.PartitionKey, received = delivery with { Bytes = [] } }));
                var valid = validator.IsValid(delivery.Bytes);
                var accepted = validator.Validate(new Cp6InboxDelivery("cp6-c02-probe", envelope.MessageId, tenant,
                    delivery.Topic, delivery.Key, delivery.Bytes, envelope.AggregateId, envelope.AggregateVersion)).IsValid;
                var correctRouting = mode == "wrong-broker-key"
                    ? !metadataValid && delivery.Key == "forged-key" && !accepted && delivery.Topic == IdentityEventContracts.Topic && delivery.Pubsub == "cp6-kafka-pubsub"
                    : metadataValid && accepted;
                var pass = bytesEqual && sourceDataHash == receivedDataHash && correctRouting && valid && delivery.ContentType == Cp6CloudEventCodec.StructuredContentType;
                allPassed &= pass;
                observations.Add(new { mode, passed = pass, bytesEqual, sourceDataHash, receivedDataHash, metadataValid,
                    topicValid = delivery.Topic == IdentityEventContracts.Topic, keyValid = delivery.Key == envelope.PartitionKey,
                    pubsubValid = delivery.Pubsub == "cp6-kafka-pubsub",
                    deliveryAccepted = accepted, expectedDeliveryAccepted = mode != "wrong-broker-key",
                    contractValid = valid, delivery.ContentType, sourcePayloadSha256 = IdentityEventContracts.Hash(sourceBytes),
                    receivedPayloadSha256 = IdentityEventContracts.Hash(delivery.Bytes) });
                Console.WriteLine($"PROBE {mode}: passed={pass} exactBytes={bytesEqual} dataHash={sourceDataHash == receivedDataHash} routing={correctRouting} accepted={accepted}");
            }
            complete = allPassed && observations.Count == 3;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(diagnosticDirectory, "transport-probe.log"), ex.ToString());
            Console.WriteLine("FAIL transport-probe; see private diagnostic");
        }
        finally
        {
            await app.StopAsync();
            File.WriteAllText(Path.Combine(outputDirectory, "transport-probe.json"), JsonSerializer.Serialize(new
            {
                schemaId = "cp6.c02.transport-byte-probe.v1", conclusion = complete ? "success" : "failure", source,
                scope = "Actual SDK publisher, Dapr 1.18.2 and Kafka 4.3.1; generated contract event; no SQL/CRM acceptance",
                assemblySha256 = IdentityEventContracts.Hash(File.ReadAllBytes(typeof(IdentityDaprPublisher).Assembly.Location)),
                observations, observedAtUtc = DateTimeOffset.UtcNow
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
        return complete ? 0 : 1;
    }

    private static string DataHash(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        return IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(json.RootElement.GetProperty("data").GetRawText()));
    }
    private sealed record Delivery(byte[] Bytes, string ContentType, string Topic, string Key, string Pubsub);
}
