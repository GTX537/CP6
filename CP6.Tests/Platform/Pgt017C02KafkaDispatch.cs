using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using CP6.WebApi.BackgroundServices;
using Dapr.Client;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private static async Task DispatchC02ScopeThroughBroker(Harness h, CrmIdentityRuntime runtime, string aggregate, int version)
    {
        var http = Environment.GetEnvironmentVariable("CP6_PGT017_C02_DAPR_HTTP")!;
        var grpc = Environment.GetEnvironmentVariable("CP6_PGT017_C02_DAPR_GRPC")!;
        using var dapr = new DaprClientBuilder().UseHttpEndpoint(http).UseGrpcEndpoint(grpc).Build();
        using var invocation = new HttpClient { BaseAddress = new Uri(http), Timeout = TimeSpan.FromSeconds(10) };
        var publisher = new IdentityDaprPublisher(new Cp6DaprTransport(dapr, invocation), runtime.Validator);
        var connection = h.Db.Database.GetConnectionString()!;
        var database = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var factory = IdentityEventDispatchWorker.CreateOrdinaryContextFactory(database, connection);
        var dispatcher = new Cp6OutboxDispatcher<CP6Context>(factory, runtime.Validator, publisher, new()
        {
            DispatchBatchSize = 64, OutboxLeaseDuration = TimeSpan.FromMinutes(1)
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        var record = h.Db.Set<Cp6OutboxMessage>().AsNoTracking().Where(row =>
            row.TenantId == h.Tenant && row.AggregateId == aggregate && row.AggregateVersion == version);
        while (await record.Select(row => row.Status).SingleAsync(timeout.Token) != Cp6OutboxStatus.Published)
        {
            var dispatched = await dispatcher.DispatchBatchAsync("pgt017-c02-kafka", timeout.Token);
            Assert.True(dispatched.Claimed > 0, $"C02 role version {version} was not dispatched.");
            Assert.Equal(0, dispatched.DeadLettered);
        }
    }
}
