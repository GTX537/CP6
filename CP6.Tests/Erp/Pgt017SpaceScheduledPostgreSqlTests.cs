using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Space;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_scheduled_space_snapshot_and_tenant_pause_share_the_commit_fence(bool pauseFirst)
    {
        await using var h = await SpaceCodeSeed();
        await using var administrator = await TenantManagementSeed();
        var db = h.Db;
        var site = await db.Space_Sites.AsNoTracking().SingleAsync(row => row.SiteCode == "SIM");
        db.Space_AnalyticsConfigs.Add(new() { Id = Guid.NewGuid(), EnableScheduledSnapshot = true, ScheduledHourLocal = 0 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var enumeration = new HeldAbcTenantEnumeration(h.Tenant);
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ITenantEnumerator>(enumeration)
            .AddScoped<ITenantContext, TenantContext>()
            .AddScoped(provider => server.Context(provider.GetRequiredService<ITenantContext>()))
            .AddScoped<IWmsStockQuery, WmsStockQuery>()
            .AddScoped<IWmsAnalyticsQuery, WmsAnalyticsQuery>()
            .AddSingleton(new WfsInfraOptions())
            .AddScoped<ITenantClock, TenantClock>()
            .AddSingleton(TimeProvider.System)
            .AddScoped<ISpaceAnalyticsService, SpaceAnalyticsService>()
            .BuildServiceProvider();
        using var worker = new SpaceAbcSnapshotWorker(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SpaceAbcSnapshotWorker>.Instance);
        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));
        var manager = await Login(host, administrator);
        using var initial = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var tenant = await NumberHttpData<TenantManagementSnapshotV1>(initial);
        var command = new TenantManagementCommandV1("pgt017-scheduled-abc-" + pauseFirst, tenant.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(h, administrator.User);
        Assert.Equal(0, await Count());
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        var writing = worker.ProcessOnceAsync();
        Task<HttpResponseMessage>? pausing = null;
        try
        {
            await enumeration.Enumerated.Task.WaitAsync(TimeSpan.FromSeconds(8));
            await using var hold = await barrier.BeginTransactionAsync();
            try
            {
                FenceWait first;
                FenceWait second;
                if (pauseFirst)
                {
                    await using var lockTenant = new NpgsqlCommand(
                        """SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""", barrier, hold);
                    lockTenant.Parameters.AddWithValue("tenant", h.Tenant);
                    Assert.Equal(1, await lockTenant.ExecuteScalarAsync());
                    pausing = Pause();
                    first = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%Sys_Tenants%", null, pausing);
                    enumeration.Release.TrySetResult();
                    second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, writing);
                }
                else
                {
                    await using var lockBusiness = new NpgsqlCommand(
                        """LOCK TABLE public."Space_AbcSnapshot" IN SHARE MODE""", barrier, hold);
                    await lockBusiness.ExecuteNonQueryAsync();
                    enumeration.Release.TrySetResult();
                    first = await WaitForFence(observer, barrier.ProcessID, "%INSERT INTO%Space_AbcSnapshot%", null, writing);
                    pausing = Pause();
                    second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, pausing);
                }
                output.WriteLine($"pauseFirst={pauseFirst}, first={first}, second={second}");
                Assert.Equal(0, await Count());
                Assert.Equal(authority, await ReadPauseAuthority(h, administrator.User));
            }
            finally
            {
                enumeration.Release.TrySetResult();
                await hold.RollbackAsync();
            }
        }
        catch
        {
            enumeration.Release.TrySetResult();
            await writing;
            if (pausing != null)
            {
                using var releasedPause = await pausing;
                output.WriteLine($"Pause response after releasing the barrier: {releasedPause.StatusCode}");
            }
            output.WriteLine($"Scheduled snapshot count after releasing the barrier: {await Count()}");
            throw;
        }
        await writing;
        using var response = await pausing!;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(response);
        CheckPause(authority, await ReadPauseAuthority(h, administrator.User), receipt, command);
        var expected = pauseFirst ? 0 : 1;
        Assert.Equal(expected, await Count());
        var snapshot = await db.Space_AbcSnapshots.AsNoTracking().SingleOrDefaultAsync(row => row.SiteId == site.Id);
        if (snapshot != null)
        {
            Assert.Equal("SpaceAbcSnapshotWorker", snapshot.Creator);
            Assert.NotNull(snapshot.ScheduledDate);
        }
        await worker.ProcessOnceAsync(); // The enumerated tenant can remain in an already running worker cycle.
        Assert.Equal(expected, await Count());
        Assert.Equal(snapshot?.Id, await db.Space_AbcSnapshots.AsNoTracking().Where(row => row.SiteId == site.Id)
            .Select(row => (Guid?)row.Id).SingleOrDefaultAsync());

        Task<int> Count() => db.Space_AbcSnapshots.AsNoTracking().CountAsync(row => row.SiteId == site.Id && row.Trigger == "scheduled");
        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(h.Tenant, "/commands"), command));
    }

    private sealed class HeldAbcTenantEnumeration(Guid tenant) : ITenantEnumerator
    {
        internal TaskCompletionSource Enumerated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<Guid>> ListActiveAsync(CancellationToken ct = default)
        {
            Enumerated.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return [tenant];
        }
    }
}
