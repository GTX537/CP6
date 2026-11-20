using CP6.Core.Services.Common;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class MappingLeaseClock : TimeProvider
    {
        private long offset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref offset));
        internal void Advance(TimeSpan interval) => Interlocked.Add(ref offset, interval.Ticks);
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_03_expired_delivery_and_projection_leases_cannot_overwrite_new_attempts()
    {
        await using var h = await Seed();
        await using var fixture = await MappingOwnerFixture.Start(h.Tenant);
        await EnableMapping(h, fixture);
        var account = Guid.NewGuid();
        fixture.Account(account, "00000009");
        await using var api = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, fixture.Environment());
        var cookies = await Login(api, h);
        var bp = await AdoptMappingPartner(api, cookies, "0000899", "模拟租约归属");
        var probe = await SignedAccountObservation(api, cookies, account);
        var context = await ReadMappingContext(api, cookies, account, probe, MappingCriteria("0000899", "模拟租约归属"));
        await ApplyMap(api, cookies, await GoodMapPreview(api, cookies, MappingInput(context, "LINK", bp, "模拟租约映射")), Guid.NewGuid());
        var stored = Assert.Single(await StoredEvents(h, account));
        using var dp = MappingProtection(h);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(13) };
        var clock = new MappingLeaseClock();
        var adapter = fixture.Adapter(http);
        Assert.Null(await adapter.DeliverAsync(fixture.Binding("CRM_PROJECTION"), [.. stored.Body, 32], stored.Hash, default));
        Assert.Empty(fixture.Deliveries);

        async Task Pump(bool delivery)
        {
            await using var ctx = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
            var workspace = MappingService(h, ctx, dp.GetRequiredService<IDataProtectionProvider>(), adapter, clock);
            if (delivery) await workspace.RunMappingDeliveryOnce(default);
            else await workspace.RunMappingProjectionOnce(default);
        }

        var firstSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DelayDelivery = async attempt =>
        {
            if (attempt > 1) return 200;
            firstSent.TrySetResult();
            await releaseSent.Task.WaitAsync(TimeSpan.FromSeconds(9));
            return 409;
        };
        var lateDelivery = Pump(true);
        try
        {
            await firstSent.Task.WaitAsync(TimeSpan.FromSeconds(7));
            clock.Advance(TimeSpan.FromSeconds(31));
            await Pump(true);
        }
        finally { releaseSent.TrySetResult(); await lateDelivery; }

        Assert.Equal(2, fixture.Deliveries.Count);
        Assert.All(fixture.Deliveries, raw => Assert.Equal(stored.Body, raw));
        var transport = (await MappingHead(api, cookies, account)).Body;
        Assert.Equal("ACKED_PROCESSING", Assert.Single(transport.GetProperty("deliveries").EnumerateArray()).GetProperty("state").GetString());
        Assert.Equal("UNKNOWN", transport.GetProperty("projection").GetProperty("knowledge").GetString());
        Assert.False(await h.Db.Database.SqlQuery<bool>($"""
            SELECT owner_conflict AS "Value" FROM main_bp.mapping_slots WHERE tenant_id={h.Tenant} AND account_id={account}
            """).SingleAsync());

        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ProjectionResponses.Enqueue(async _ =>
        {
            firstRead.TrySetResult();
            await releaseRead.Task.WaitAsync(TimeSpan.FromSeconds(9));
            return MappingOwnerFixture.Projection(account, "1", "0000899", false);
        });
        var job = await ReconcileJob(api, cookies, account, "1");
        var lateResult = Pump(false);
        try
        {
            await firstRead.Task.WaitAsync(TimeSpan.FromSeconds(7));
            clock.Advance(TimeSpan.FromSeconds(31));
            fixture.ProjectionResponses.Enqueue(_ => Task.FromResult<object?>(null));
            await Pump(false);
            var latest = (await ReadReconcileJob(api, cookies, job)).Body;
            Assert.Equal("WAITING", latest.GetProperty("state").GetString());
            Assert.True(latest.GetProperty("currentApplicable").GetBoolean());
        }
        finally { releaseRead.TrySetResult(); await lateResult; }
        var current = (await MappingHead(api, cookies, account)).Body.GetProperty("projection");
        Assert.Equal("UNKNOWN", current.GetProperty("knowledge").GetString());
        Assert.Equal(new[] { "MATCHES_CURRENT", "UNKNOWN" }, await h.Db.Database.SqlQuery<string>($"""
            SELECT knowledge AS "Value" FROM main_bp.mapping_projection_reads WHERE tenant_id={h.Tenant} AND job_id={job}
            ORDER BY current_applicable
            """).ToArrayAsync());

        clock.Advance(TimeSpan.FromSeconds(61));
        await Pump(false);
        var recovered = (await ReadReconcileJob(api, cookies, job)).Body;
        Assert.Equal("COMPLETE", recovered.GetProperty("state").GetString());
        Assert.Equal("MATCHES_CURRENT", recovered.GetProperty("observation").GetProperty("knowledge").GetString());
        Assert.Equal(job, recovered.GetProperty("jobId").GetGuid());
        await DeviceEvidence("wp04-03-leases.json", new
        {
            sameDeliveryBodyAfterLeaseTakeover = true, invalidReencodingNotDispatched = true,
            lateDelivery409OnlyAttemptHistory = true, lateProjectionSuccessOnlyAttemptHistory = true,
            retryRecoveredSameJob = true, simulatedLeaseClock = true, eventId = stored.Id, job, hash = stored.Hash
        });
    }
}
