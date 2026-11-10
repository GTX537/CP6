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
    public async Task Lpn_create_and_replay_require_current_authority_before_writing()
    {
        await using var tenantUser = await Seed();
        await using var administrator = await TenantManagementSeed();
        var db = tenantUser.Db;
        var tenant = tenantUser.Tenant;
        await GrantMobile(tenantUser);
        if (!await db.Sys_MenuActions.AnyAsync(a => a.MenuId == 461 && a.ActionCode == "lpn-manage"))
            db.Sys_MenuActions.Add(new() { MenuId = 461, ActionCode = "lpn-manage", ActionName = "Manage LPN" });
        db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = 461, ActionCode = "lpn-manage" });
        db.WmsFeatureFlags.Add(new() { WarehouseCd = "W1", SerialLpnEnabled = true });
        db.Locations.Add(new() { WarehouseCd = "W1", LocationCd = "LPN-LOC", AreaCd = "LPN" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var writer = await Login(host, tenantUser);
        var manager = await Login(host, administrator);
        var original = Command("LPN-ORIGINAL");
        using var created = await Send(original);
        Assert.True(created.StatusCode == HttpStatusCode.OK, await OperationsFailure(created));
        var originalReply = await created.Content.ReadAsStringAsync();
        var before = await WriteSet();
        using (var snapshot = JsonDocument.Parse(before))
        {
            var root = snapshot.RootElement;
            Assert.Equal("price-owner", Assert.Single(root.GetProperty("Units").EnumerateArray()).GetProperty("Creator").GetString());
            Assert.Single(root.GetProperty("Closures").EnumerateArray());
            Assert.Equal("price-owner", Assert.Single(root.GetProperty("Events").EnumerateArray()).GetProperty("UserName").GetString());
            Assert.Single(root.GetProperty("Receipts").EnumerateArray());
        }
        using var replay = await Send(original);
        Assert.True(replay.StatusCode == HttpStatusCode.OK, await OperationsFailure(replay));
        Assert.Equal(originalReply, await replay.Content.ReadAsStringAsync());
        Assert.Equal(before, await WriteSet());

        var collision = Command("LPN-COLLISION");
        collision.OperationId = original.OperationId;
        await Rejected(collision, HttpStatusCode.Conflict, "WM-V2-OPERATION-ID-USED");
        var missing = Command("LPN-MISSING-LOCATION");
        missing.LocationCd = "MISSING";
        await Rejected(missing, HttpStatusCode.BadRequest, "WM-V2-LOCATION-NOT-FOUND");
        await db.WmsFeatureFlags.ExecuteUpdateAsync(s => s.SetProperty(f => f.SerialLpnEnabled, false));
        await Rejected(Command("LPN-DISABLED"), HttpStatusCode.Conflict, "WM-R2B-DISABLED");
        await db.WmsFeatureFlags.ExecuteUpdateAsync(s => s.SetProperty(f => f.SerialLpnEnabled, true));
        Assert.Equal(before, await WriteSet());

        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var businessBlocker = new NpgsqlConnection(connection);
        await using var tenantBlocker = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await businessBlocker.OpenAsync();
        await tenantBlocker.OpenAsync();
        await observer.OpenAsync();
        FenceWait pauseWait, createWait;
        Task<HttpResponseMessage> pending;
        TenantManagementReceiptV1 suspended;
        await using (var businessLock = await businessBlocker.BeginTransactionAsync())
        {
            await using var lockUnits = new NpgsqlCommand("""LOCK TABLE public."T_LogisticsUnit" IN ACCESS EXCLUSIVE MODE""", businessBlocker, businessLock);
            await lockUnits.ExecuteNonQueryAsync();
            try
            {
                await using var tenantLock = await tenantBlocker.BeginTransactionAsync();
                await using var lockTenant = new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""", tenantBlocker, tenantLock);
                lockTenant.Parameters.AddWithValue("tenant", tenant);
                Assert.Equal(1, await lockTenant.ExecuteScalarAsync());
                var pause = host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"),
                    new TenantManagementCommandV1("lpn-create-pause", active.Revision, "suspend", null)));
                try
                {
                    pauseWait = await WaitForFence(observer, tenantBlocker.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                    pending = Send(Command("LPN-AFTER-PAUSE"));
                    createWait = await ObserveLpnCreateWait(observer, pauseWait.Backend, businessBlocker.ProcessID, pending);
                }
                finally { await tenantLock.RollbackAsync(); }
                using var pauseResponse = await pause;
                suspended = await NumberHttpData<TenantManagementReceiptV1>(pauseResponse);
                Assert.False(suspended.Snapshot.Enable);
            }
            finally { await businessLock.RollbackAsync(); }
        }
        using var response = await pending;
        var after = await WriteSet();
        using var report = File.Open(Path.Combine(Environment.GetEnvironmentVariable(CurrentPermissionPostgresFactAttribute.RootVariable)!,
            "lpn-create-pause.json"), FileMode.CreateNew);
        await JsonSerializer.SerializeAsync(report, new
        {
            Tenant = tenant, pauseWait, createWait, suspended.Snapshot.Revision, suspended.CommittedAt,
            AuthorityWait = createWait.Blocker == pauseWait.Backend, Status = (int)response.StatusCode,
            Before = before, After = after
        });
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(response));
        Assert.Contains("REAUTH_REQUIRED", await response.Content.ReadAsStringAsync());
        Assert.Equal(pauseWait.Backend, createWait.Blocker);
        Assert.Equal(before, after);
        using var deniedReplay = await Send(original);
        Assert.True(deniedReplay.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(deniedReplay));
        Assert.Equal(before, await WriteSet());

        CreateLpnRequest Command(string number) => new()
        { OperationId = Guid.NewGuid(), LpnNo = number, ContainerType = "BOX", WarehouseCd = "W1", LocationCd = "LPN-LOC" };
        Task<HttpResponseMessage> Send(CreateLpnRequest command) => host.Send(OperationsRequest(writer,
            HttpMethod.Post, "api/v2/wms/lpns", command));
        async Task Rejected(CreateLpnRequest command, HttpStatusCode status, string code)
        {
            using var rejected = await Send(command);
            Assert.True(rejected.StatusCode == status, await OperationsFailure(rejected));
            using var failure = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
            Assert.Equal(code, failure.RootElement.GetProperty("code").GetString());
        }
        Task<string> WriteSet() => db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object(
              'Units', (SELECT coalesce(jsonb_agg(to_jsonb(u) ORDER BY u."Id"),'[]') FROM public."T_LogisticsUnit" u WHERE u."TenantId"={tenant}),
              'Closures', (SELECT coalesce(jsonb_agg(to_jsonb(c) ORDER BY c."Id"),'[]') FROM public."T_LpnClosure" c WHERE c."TenantId"={tenant}),
              'Events', (SELECT coalesce(jsonb_agg(to_jsonb(e) ORDER BY e."Id"),'[]') FROM public."T_LpnEvent" e WHERE e."TenantId"={tenant}),
              'Receipts', (SELECT coalesce(jsonb_agg(to_jsonb(r) ORDER BY r."Id"),'[]') FROM public."T_TaskCommandReceipt" r WHERE r."TenantId"={tenant})
            )::text AS "Value"
            """).SingleAsync();
    }

    private static async Task<FenceWait> ObserveLpnCreateWait(NpgsqlConnection observer, int pause, int business,
        Task<HttpResponseMessage> request)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var command = new NpgsqlCommand("""
            SELECT a.pid, b.blocker, a.usename, a.wait_event, clock_timestamp()
            FROM pg_stat_activity a CROSS JOIN LATERAL unnest(pg_blocking_pids(a.pid)) AS b(blocker)
            WHERE a.datname=current_database() AND a.wait_event_type='Lock'
              AND ((b.blocker=@pause AND a.query LIKE '%cp6_authentication_v1.acquire()%')
                OR (b.blocker=@business AND a.query LIKE '%T_LogisticsUnit%'))
            ORDER BY a.pid LIMIT 1
            """, observer);
        command.Parameters.AddWithValue("pause", pause);
        command.Parameters.AddWithValue("business", business);
        while (true)
        {
            await using (var rows = await command.ExecuteReaderAsync(deadline.Token))
                if (await rows.ReadAsync(deadline.Token))
                    return new(rows.GetInt32(0), rows.GetInt32(1), rows.GetString(2), rows.GetString(3), rows.GetFieldValue<DateTimeOffset>(4));
            if (request.IsCompleted) Assert.Fail("Create completed before the native wait: " + await OperationsFailure(await request));
            await Task.Delay(35, deadline.Token);
        }
    }
}
