using System.Reflection;
using CP6.Core.Services.Common;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Mes;
using CP6.Tests.Platform;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_machine_monitor_and_tenant_pause_share_the_commit_fence(bool pauseFirst)
    {
        await using var h = await SpaceCodeSeed();
        await using var administrator = await TenantManagementSeed();
        var db = h.Db;
        const string machineCode = "SIM-MONITOR";
        db.Machines.Add(new Machine
        {
            MachineCd = machineCode, MachineName = "Simulated idle machine",
            Creator = "pgt017-setup", Status = 1, ActiveFlg = true
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var enumeration = new HeldAbcTenantEnumeration(h.Tenant);
        var notifier = new Mock<IMesNotifier>();
        await using var services = new ServiceCollection()
            .AddSingleton<ITenantEnumerator>(enumeration)
            .AddScoped<ITenantContext, TenantContext>()
            .AddScoped(provider => server.Context(provider.GetRequiredService<ITenantContext>()))
            .AddSingleton(notifier.Object)
            .BuildServiceProvider();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        using var worker = new MachineStatusMonitor(scopeFactory, NullLogger<MachineStatusMonitor>.Instance);
        var scan = typeof(MachineStatusMonitor).GetMethod("ScanTenantAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Task Scan() => TenantScopeRunner.ForEachTenantAsync(scopeFactory,
            (tenantServices, _, ct) => (Task)scan.Invoke(worker, [tenantServices, ct])!);

        var connection = db.Database.GetConnectionString()!;
        var environment = IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant);
        if (Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT") is { Length: > 0 } port)
            environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + port;
        await using var host = await AuthenticationApiProcess.Start(connection, environment);
        var manager = await Login(host, administrator);
        using var initial = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var metadata = await NumberHttpData<TenantManagementSnapshotV1>(initial);
        var command = new TenantManagementCommandV1("pgt017-machine-monitor-" + pauseFirst, metadata.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(h, administrator.User);
        var original = await ReadMachine();
        Assert.Equal(1, original.Status);

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();

        var writing = Scan();
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
                    await using var lockMachine = new NpgsqlCommand("""LOCK TABLE public."M_Machine" IN SHARE MODE""", barrier, hold);
                    await lockMachine.ExecuteNonQueryAsync();
                    enumeration.Release.TrySetResult();
                    first = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%M_Machine%", null, writing);
                    pausing = Pause();
                    second = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, pausing);
                }

                output.WriteLine($"pauseFirst={pauseFirst}, first={first}, second={second}");
                Assert.Equal(original, await ReadMachine());
                Assert.Equal(authority, await ReadPauseAuthority(h, administrator.User));
                Assert.Empty(notifier.Invocations);
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
            if (pausing is not null)
            {
                using var releasedPause = await pausing;
                output.WriteLine($"Pause after release: {releasedPause.StatusCode}");
            }

            output.WriteLine($"Persisted application machine state: {await ReadMachine()}");
            throw;
        }

        await writing;
        using var response = await pausing!;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(response);
        CheckPause(authority, await ReadPauseAuthority(h, administrator.User), receipt, command);

        var final = await ReadMachine();
        if (pauseFirst)
            Assert.Equal(original, final);
        else
        {
            Assert.Equal(0, final.Status);
            Assert.Equal("MachineMonitor", final.Modifier);
            Assert.NotEqual(original.RowVersion, final.RowVersion);
        }
        notifier.Verify(n => n.NotifyMachineStatusChangedAsync(machineCode, 0), pauseFirst ? Times.Never() : Times.Once());

        await Scan();
        Assert.Equal(final, await ReadMachine());
        notifier.Verify(n => n.NotifyMachineStatusChangedAsync(machineCode, 0), pauseFirst ? Times.Never() : Times.Once());

        async Task<(int Status, string? Modifier, DateTime? ModifyDate, string RowVersion)> ReadMachine()
        {
            var row = await db.Machines.AsNoTracking().SingleAsync(m => m.MachineCd == machineCode);
            return (row.Status, row.Modifier, row.ModifyDate, Convert.ToHexString(row.RowVersion!));
        }

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(h.Tenant, "/commands"), command));
    }
}
