using CP6.Core.Services.ErpIntegration;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.ErpIntegration.SqlTests;

[Collection(SqlDatabaseCollection.Name)]
public sealed class CommandRetryWorkerSqlTests(SqlDatabaseFixture database)
{
    [Fact]
    public async Task One_pass_skips_removed_tenant_backlog_and_gives_each_registered_tenant_a_bounded_retry_batch()
    {
        var s = new ErpScenario(database);
        await s.InitializeAsync();
        await s.RegisterPartnerAsync();
        await s.RegisterPartnerAsync(tenant: s.OtherTenant);
        var removedTenant = Guid.NewGuid();
        var now = s.Clock.GetUtcNow();
        // These valid original envelopes represent durable retries left by a previously registered tenant.
        // They are older than every active retry and exceed the worker's former global Take(16).
        s.Runtime.Options.Tenants.Add(removedTenant, "local");
        var removed = await SeedDueAsync(s, removedTenant, 18, now.AddHours(-3));
        var first = await SeedDueAsync(s, s.Tenant, 17, now.AddHours(-2));
        var second = await SeedDueAsync(s, s.OtherTenant, 17, now.AddHours(-1));
        s.Runtime.Options.Tenants.Remove(removedTenant);

        var handlerResolutions = 0;
        var services = new ServiceCollection();
        // Each scope resolves the real production handler and uses the real SQL-backed ERP services.
        // Counting resolutions detects even invalid removed-tenant attempts, which leave no SQL mutation.
        services.AddScoped(_ =>
        {
            Interlocked.Increment(ref handlerResolutions);
            return s.Handler();
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var worker = new ErpCommandRetryWorker(database, provider.GetRequiredService<IServiceScopeFactory>(),
            s.Runtime, NullLogger<ErpCommandRetryWorker>.Instance);

        await worker.RetryDueAsync(CancellationToken.None);

        foreach (var (tenant, original) in new[] { (s.Tenant, first), (s.OtherTenant, second) })
        {
            var rows = await ReceiptsAsync(s, tenant);
            Assert.Equal(16, rows.Count(x => x.Status == ErpInboxStatus.Processed));
            Assert.All(rows.Take(16), row =>
            {
                Assert.Equal(2, row.AttemptCount);
                Assert.Equal(now, row.ProcessedAtUtc);
                Assert.Null(row.RetryAtUtc);
                Assert.Null(row.ErrorCode);
            });
            Assert.Equal(original[^1].RowVersion, rows[^1].RowVersion);
            Assert.Equal(ErpInboxStatus.Processing, rows[^1].Status);
            await using var business = s.Db(tenant);
            Assert.Equal(1, (await business.BusinessPartners.SingleAsync(x => x.BpCd == s.PartnerKey)).Status);
            Assert.Equal(16, (await s.ResultsAsync(ErpEventContracts.BusinessPartnerSynchronized, tenant)).Length);
        }
        Assert.Equal(32, handlerResolutions);
        await AssertRemovedUntouchedAsync(s, removedTenant, removed);

        // Both tenants' remaining seventeenth commands progress on the next pass; processed rows are not retried.
        await worker.RetryDueAsync(CancellationToken.None);
        await worker.RetryDueAsync(CancellationToken.None);
        Assert.Equal(34, handlerResolutions);
        foreach (var tenant in new[] { s.Tenant, s.OtherTenant })
        {
            var rows = await ReceiptsAsync(s, tenant);
            Assert.Equal(17, rows.Length);
            Assert.All(rows, row =>
            {
                Assert.Equal(ErpInboxStatus.Processed, row.Status);
                Assert.Equal(2, row.AttemptCount);
            });
            Assert.Equal(17, (await s.ResultsAsync(ErpEventContracts.BusinessPartnerSynchronized, tenant)).Length);
            await using var queue = s.Queue();
            Assert.Equal(17, await queue.Requests.CountAsync(x => x.TenantId == tenant && x.Succeeded && x.Terminal));
        }
        await AssertRemovedUntouchedAsync(s, removedTenant, removed);
        Assert.Equal(0, s.ExternalCalls.Count);
    }

    private static async Task<ErpInboxReceipt[]> SeedDueAsync(ErpScenario s, Guid tenant, int count, DateTimeOffset due)
    {
        await using var queue = s.Queue();
        for (var version = 1; version <= count; version++)
        {
            var envelope = s.Envelope(new ErpBusinessPartnerRequested(tenant, Guid.NewGuid(), s.Account, version));
            Assert.True(s.Runtime.Validator.IsValid(envelope.Payload));
            queue.Inbox.Add(new()
            {
                MessageId = envelope.MessageId, TenantId = tenant, EventType = ErpEventContracts.BusinessPartnerRequested,
                AggregateId = s.Account, AggregateVersion = version, Payload = envelope.Payload.ToArray(),
                PayloadSha256 = ErpEventContracts.Hash(envelope.Payload.Span), Status = ErpInboxStatus.Processing,
                AttemptCount = 1, ReceivedAtUtc = due.AddMinutes(-1), RetryAtUtc = due.AddSeconds(version),
                ErrorCode = "C03_ERP_UNAVAILABLE"
            });
        }
        await queue.SaveChangesAsync();
        return await ReceiptsAsync(s, tenant);
    }

    private static async Task<ErpInboxReceipt[]> ReceiptsAsync(ErpScenario s, Guid tenant)
    {
        await using var queue = s.Queue();
        return await queue.Inbox.AsNoTracking().Where(x => x.TenantId == tenant)
            .OrderBy(x => x.AggregateVersion).ToArrayAsync();
    }

    private static async Task AssertRemovedUntouchedAsync(ErpScenario s, Guid tenant, ErpInboxReceipt[] original)
    {
        var rows = await ReceiptsAsync(s, tenant);
        Assert.Equal(original.Length, rows.Length);
        for (var index = 0; index < rows.Length; index++)
        {
            Assert.Equal(original[index].MessageId, rows[index].MessageId);
            Assert.Equal(original[index].RowVersion, rows[index].RowVersion);
            Assert.Equal(ErpInboxStatus.Processing, rows[index].Status);
            Assert.Equal(1, rows[index].AttemptCount);
        }
        await using var queue = s.Queue();
        Assert.Empty(await queue.Requests.Where(x => x.TenantId == tenant).ToArrayAsync());
        Assert.Empty(await s.ResultsAsync(tenant: tenant));
    }
}
