using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Lpn_policy_admitted_before_suspend_cannot_write_after_suspend_commits()
    {
        await using var tenantUser = await Seed();
        await using var administrator = await TenantManagementSeed();
        var db = tenantUser.Db;
        var tenant = tenantUser.Tenant;
        await GrantMobile(tenantUser);
        if (!await db.Sys_MenuActions.AnyAsync(action => action.MenuId == 461 && action.ActionCode == "lpn-manage"))
            db.Sys_MenuActions.Add(new() { MenuId = 461, ActionCode = "lpn-manage", ActionName = "Manage LPN" });
        db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = 461, ActionCode = "lpn-manage" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var writer = await Login(host, tenantUser);
        var manager = await Login(host, administrator);
        using var seedPolicy = await SavePolicy(false, true);
        Assert.True(seedPolicy.StatusCode == HttpStatusCode.OK, await OperationsFailure(seedPolicy));
        var before = await ReadPolicies();
        using (var snapshot = JsonDocument.Parse(before))
        {
            var row = Assert.Single(snapshot.RootElement.EnumerateArray());
            Assert.False(row.GetProperty("AllowMixedProducts").GetBoolean());
            Assert.True(row.GetProperty("AllowMixedLots").GetBoolean());
            Assert.Equal("price-owner", row.GetProperty("Creator").GetString());
        }

        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var policyBlocker = new NpgsqlConnection(connection);
        await using var tenantBlocker = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await policyBlocker.OpenAsync();
        await tenantBlocker.OpenAsync();
        await observer.OpenAsync();

        FenceWait pauseWait, policyWait;
        Task<HttpResponseMessage> pending;
        TenantManagementReceiptV1 suspended;
        await using (var policyLock = await policyBlocker.BeginTransactionAsync())
        {
            await using var lockPolicy = new NpgsqlCommand("""LOCK TABLE public."T_LpnPolicy" IN ACCESS EXCLUSIVE MODE""", policyBlocker, policyLock);
            await lockPolicy.ExecuteNonQueryAsync();
            try
            {
                await using var tenantLock = await tenantBlocker.BeginTransactionAsync();
                await using var lockTenant = new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""", tenantBlocker, tenantLock);
                lockTenant.Parameters.AddWithValue("tenant", tenant);
                Assert.Equal(1, await lockTenant.ExecuteScalarAsync());
                var pause = host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"),
                    new TenantManagementCommandV1("lpn-policy-pause", active.Revision, "suspend", null)));
                try
                {
                    pauseWait = await WaitForFence(observer, tenantBlocker.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                    pending = SavePolicy(true, false);
                    policyWait = await ObserveLpnPolicyWait(observer, pauseWait.Backend, policyBlocker.ProcessID, pending);
                }
                finally { await tenantLock.RollbackAsync(); }

                using var pauseResponse = await pause;
                suspended = await NumberHttpData<TenantManagementReceiptV1>(pauseResponse);
                Assert.False(suspended.Snapshot.Enable);
            }
            finally { await policyLock.RollbackAsync(); }
        }

        using var response = await pending;
        var after = await ReadPolicies();
        using var report = File.Open(Path.Combine(Environment.GetEnvironmentVariable(CurrentPermissionPostgresFactAttribute.RootVariable)!,
            "lpn-policy-pause.json"), FileMode.CreateNew);
        await JsonSerializer.SerializeAsync(report, new
        {
            Tenant = tenant, pauseWait, policyWait, suspended.Snapshot.Revision, suspended.CommittedAt,
            AuthorityWait = policyWait.Blocker == pauseWait.Backend, Status = (int)response.StatusCode,
            PolicyBefore = before, PolicyAfter = after
        });

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(response));
        Assert.Contains("REAUTH_REQUIRED", await response.Content.ReadAsStringAsync());
        Assert.Equal(pauseWait.Backend, policyWait.Blocker);
        Assert.Equal(before, after);

        Task<HttpResponseMessage> SavePolicy(bool products, bool lots) => host.Send(OperationsRequest(writer, HttpMethod.Post,
            "api/v2/wms/lpns/policies", new LpnPolicyRequest
            { WarehouseCd = "W1", ContainerType = "BOX", AllowMixedProducts = products, AllowMixedLots = lots }));

        Task<string> ReadPolicies() => db.Database.SqlQuery<string>($"""
            SELECT coalesce(jsonb_agg(to_jsonb(p) ORDER BY p."Id"),'[]'::jsonb)::text AS "Value"
            FROM public."T_LpnPolicy" p WHERE p."TenantId"={tenant}
            """).SingleAsync();
    }

    private static async Task<FenceWait> ObserveLpnPolicyWait(NpgsqlConnection observer, int pause, int policy,
        Task<HttpResponseMessage> request)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var command = new NpgsqlCommand("""
            SELECT a.pid, b.blocker, a.usename, a.wait_event, clock_timestamp()
            FROM pg_stat_activity a CROSS JOIN LATERAL unnest(pg_blocking_pids(a.pid)) AS b(blocker)
            WHERE a.datname=current_database() AND a.wait_event_type='Lock'
              AND ((b.blocker=@pause AND a.query LIKE '%cp6_authentication_v1.acquire()%')
                OR (b.blocker=@policy AND a.query LIKE '%T_LpnPolicy%'))
            ORDER BY a.pid LIMIT 1
            """, observer);
        command.Parameters.AddWithValue("pause", pause);
        command.Parameters.AddWithValue("policy", policy);
        while (true)
        {
            await using (var rows = await command.ExecuteReaderAsync(deadline.Token))
                if (await rows.ReadAsync(deadline.Token))
                    return new(rows.GetInt32(0), rows.GetInt32(1), rows.GetString(2), rows.GetString(3), rows.GetFieldValue<DateTimeOffset>(4));
            if (request.IsCompleted) Assert.Fail("Policy request completed before the native wait: " + await OperationsFailure(await request));
            await Task.Delay(35, deadline.Token);
        }
    }
}
