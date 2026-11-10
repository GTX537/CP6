using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Client;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<Harness> DeviceLifecycleSeed(string publicKey)
    {
        var h = await Seed();
        await GrantMobile(h);
        if (!await h.Db.Sys_MenuActions.AnyAsync(a => a.MenuId == 461 && a.ActionCode == "device-manage"))
            h.Db.Sys_MenuActions.Add(new() { MenuId = 461, ActionCode = "device-manage", ActionName = "Device management" });
        h.Db.Sys_RoleActions.Add(new() { RoleId = 72, MenuId = 461, ActionCode = "device-manage" });
        h.Db.Warehouses.AddRange(new() { WarehouseCd = "W1", WarehouseName = "Device fixture source" },
            new() { WarehouseCd = "W2", WarehouseName = "Device fixture outside grant" });
        h.Db.Locations.Add(new() { WarehouseCd = "W1", LocationCd = "L1", AreaCd = "A" });
        foreach (var number in new[] { "D1", "D2" })
            h.Db.ClientDevices.Add(new() { DeviceId = number, Platform = "Windows", PublicKey = publicKey, WarehouseCd = "W1", AreaCd = "A",
                AppVersion = "1.0.0", ActivatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow, DeviceMode = ClientDeviceMode.Shared });
        h.Db.Sys_Users.Add(new() { UserName = "pin-target", Password = new BCryptPasswordHasher(4).Hash("Price1!Password"), RoleId = 72 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        return h;
    }

    [TenantManagementPostgresTheory]
    [InlineData("ticket")]
    [InlineData("update")]
    [InlineData("pin")]
    [InlineData("activate")]
    [InlineData("heartbeat")]
    public async Task Device_lifecycle_writes_require_current_authority_at_commit(string operation)
    {
        using var key = RSA.Create(2048);
        await using var h = await DeviceLifecycleSeed(key.ExportSubjectPublicKeyInfoPem());
        await using var administrator = await TenantManagementSeed();
        var db = h.Db;
        var tenant = h.Tenant;
        if (operation == "activate")
            await db.Sys_Tenants.Where(t => t.Id == tenant).ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpireDate, DateTime.SpecifyKind(DateTime.Now.AddHours(1), DateTimeKind.Unspecified)));
        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var writer = await Login(host, h);
        var manager = await Login(host, administrator);
        var nonceTable = await db.Database.SqlQuery<bool>($"SELECT to_regclass('cp6_authentication_v1.device_heartbeat_nonces') IS NOT NULL AS \"Value\"").SingleAsync();
        var ticket = await Ticket();
        var pendingTicket = await Ticket();
        if (operation == "ticket")
        {
            Assert.Equal("Windows", ticket.Platform); Assert.Equal(ClientDeviceMode.Shared, ticket.DeviceMode);
            Assert.True(ticket.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ticket.ActivationToken)));
            var row = await db.DeviceActivations.AsNoTracking().SingleAsync(a => a.TokenHash == hash);
            Assert.Equal("price-owner", row.Creator); Assert.NotEqual(ticket.ActivationToken, row.TokenHash);
        }
        else if (operation == "update")
        {
            var session = await DeviceNativeLogin(host, h, "D1");
            var refresh = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session.RefreshToken)));
            using var disabled = await host.Send(DeviceBearer(session.AccessToken, HttpMethod.Patch, "api/v2/admin/client-devices/D1",
                new UpdateClientDeviceRequest { RowVersion = await Version("D1"), Status = ClientDeviceStatus.Disabled, WarehouseCd = "W1", AreaCd = "A" }));
            Assert.True(disabled.StatusCode == HttpStatusCode.OK, await OperationsFailure(disabled));
            var device = await db.ClientDevices.AsNoTracking().SingleAsync(d => d.DeviceId == "D1");
            Assert.Equal("price-owner", device.DisabledBy); Assert.Null(device.FullAuthExpiresAt); Assert.Null(device.CurrentUser);
            Assert.NotNull(await db.Sys_RefreshTokens.Where(t => t.TokenHash == refresh).Select(t => t.RevokedAt).SingleAsync());
            using var staleSession = await host.Profile(session.AccessToken); Assert.Equal(HttpStatusCode.Unauthorized, staleSession.StatusCode);
            await Reject(() => Post(HttpMethod.Post, "api/client/devices/heartbeat", DeviceHeartbeat(key, "D1")), HttpStatusCode.Conflict, "WM-DEVICE-DISABLED");
            using var enabled = await Post(HttpMethod.Patch, "api/v2/admin/client-devices/D1", new UpdateClientDeviceRequest
                { RowVersion = await Version("D1"), Status = ClientDeviceStatus.Active, WarehouseCd = "W1", AreaCd = "A" });
            Assert.True(enabled.StatusCode == HttpStatusCode.OK, await OperationsFailure(enabled));
            Assert.Null(await db.ClientDevices.Where(d => d.DeviceId == "D1").Select(d => d.FullAuthExpiresAt).SingleAsync());
            using var stillDenied = await host.Profile(session.AccessToken); Assert.Equal(HttpStatusCode.Unauthorized, stillDenied.StatusCode);
        }
        else if (operation == "pin")
        {
            var old = writer["cp6_at"];
            using var changed = await Post(HttpMethod.Post, "api/v2/admin/client-devices/quick-pin",
                new SetWarehouseQuickPinRequest { UserName = "price-owner", BadgeNo = "self-badge", Pin = "654321" });
            Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
            var row = await db.Sys_Users.AsNoTracking().SingleAsync(u => u.Id == h.User);
            Assert.Equal("self-badge", row.BadgeNo); Assert.True(new BCryptPasswordHasher(4).Verify("654321", row.QuickPinHash!));
            Assert.Equal("price-owner", row.Modifier);
            using var oldSession = await host.Profile(old); Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
            writer = await Login(host, h);
        }
        else if (operation == "activate")
        {
            using var activated = await Anonymous(Activation(ticket, "ACT-1"));
            Assert.True(activated.StatusCode == HttpStatusCode.OK, await OperationsFailure(activated));
            var device = await db.ClientDevices.AsNoTracking().SingleAsync(d => d.DeviceId == "ACT-1");
            Assert.Equal("W1", device.WarehouseCd); Assert.Equal("A", device.AreaCd); Assert.Equal("Windows", device.Platform);
            using var duplicate = await Anonymous(Activation(ticket, "ACT-2"));
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
            Assert.False(await db.ClientDevices.AnyAsync(d => d.DeviceId == "ACT-2"));
        }
        else
        {
            var heartbeat = DeviceHeartbeat(key, "D1");
            using var recorded = await Post(HttpMethod.Post, "api/client/devices/heartbeat", heartbeat);
            Assert.True(recorded.StatusCode == HttpStatusCode.OK, await OperationsFailure(recorded));
            var row = await db.ClientDevices.AsNoTracking().SingleAsync(d => d.DeviceId == "D1");
            Assert.Equal(100, row.BatteryPercent); Assert.Equal("price-owner", row.CurrentUser); Assert.Equal("declared-only", row.CurrentTaskNo);
            var prior = await WriteSet();
            using var replay = await Post(HttpMethod.Post, "api/client/devices/heartbeat", heartbeat);
            Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode); Assert.Contains("WM-DEVICE-REPLAY", await replay.Content.ReadAsStringAsync());
            Assert.Equal(prior, await WriteSet());
        }

        var before = await WriteSet();
        var pendingBody = operation switch
        {
            "ticket" => (object)TicketRequest(),
            "update" => new UpdateClientDeviceRequest { RowVersion = await Version("D2"), Status = ClientDeviceStatus.Disabled, WarehouseCd = "W1", AreaCd = "A" },
            "pin" => new SetWarehouseQuickPinRequest { UserName = "pin-target", BadgeNo = "pending-badge", Pin = "123456" },
            "activate" => Activation(pendingTicket, "ACT-PENDING"),
            _ => DeviceHeartbeat(key, "D2")
        };
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business = new NpgsqlConnection(connection);
        await using var target = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await business.OpenAsync(); await target.OpenAsync(); await observer.OpenAsync();
        FenceWait pauseWait, commandWait;
        Task<HttpResponseMessage> pending;
        TenantManagementReceiptV1 suspended;
        var table = operation == "ticket" ? "T_DeviceActivation" : "T_ClientDevice";
        await using (var businessLock = await business.BeginTransactionAsync())
        {
            if (operation != "pin")
            {
                await using var lockBusiness = new NpgsqlCommand("LOCK TABLE public.\"" + table + "\" IN ACCESS EXCLUSIVE MODE", business, businessLock);
                await lockBusiness.ExecuteNonQueryAsync();
            }
            try
            {
                await using var tenantLock = await target.BeginTransactionAsync();
                await using var lockTenant = new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""", target, tenantLock);
                lockTenant.Parameters.AddWithValue("tenant", tenant); Assert.Equal(1, await lockTenant.ExecuteScalarAsync());
                var pause = host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"),
                    new TenantManagementCommandV1("device-" + operation, active.Revision, "suspend", null)));
                try
                {
                    pauseWait = await WaitForFence(observer, target.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                    pending = SendPending();
                    commandWait = await DeviceWait(observer, pauseWait.Backend, business.ProcessID, operation == "pin" ? "Sys_Users" : table, pending);
                }
                finally { await tenantLock.RollbackAsync(); }
                using var paused = await pause; suspended = await NumberHttpData<TenantManagementReceiptV1>(paused);
                Assert.False(suspended.Snapshot.Enable);
            }
            finally { await businessLock.RollbackAsync(); }
        }
        using var response = await pending;
        var after = await WriteSet();
        await DeviceEvidence("device-" + operation + "-pause.json", new
        { Tenant = tenant, Operation = operation, pauseWait, commandWait, suspended.Snapshot.Revision, suspended.CommittedAt,
            AuthorityWait = commandWait.Blocker == pauseWait.Backend, Status = (int)response.StatusCode, Before = before, After = after });
        Assert.True(response.StatusCode == (operation == "activate" ? HttpStatusCode.BadRequest : HttpStatusCode.Unauthorized), await OperationsFailure(response));
        Assert.Equal(pauseWait.Backend, commandWait.Blocker); Assert.Equal(before, after);
        using var deniedAgain = await SendPending();
        Assert.Equal(operation == "activate" ? HttpStatusCode.BadRequest : HttpStatusCode.Unauthorized, deniedAgain.StatusCode);
        Assert.Equal(before, await WriteSet());
        using var reactivated = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"),
            new TenantManagementCommandV1("device-reactivate-" + operation, suspended.Snapshot.Revision, "reactivate", null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(reactivated)).Snapshot.Enable);
        writer = await Login(host, h);

        if (operation == "ticket" || operation == "update")
        {
            await Reject(() => Post(HttpMethod.Post, "api/v2/admin/client-devices", TicketRequest("W2")), HttpStatusCode.Forbidden, "FORBIDDEN");
            await Reject(async () => await Post(HttpMethod.Patch, "api/v2/admin/client-devices/D2", new UpdateClientDeviceRequest
                { RowVersion = await Version("D2"), WarehouseCd = "W2", AreaCd = "A" }), HttpStatusCode.Forbidden, "FORBIDDEN");
            await Reject(() => Post(HttpMethod.Patch, "api/v2/admin/client-devices/D2", new UpdateClientDeviceRequest
                { RowVersion = Convert.ToBase64String(new byte[8]), WarehouseCd = "W1", AreaCd = "A" }), HttpStatusCode.Conflict, "WM-CONFLICT-ROW-VERSION");
        }
        if (operation == "update")
        {
            _ = await DeviceNativeLogin(host, h, "D1");
            Assert.NotNull(await db.ClientDevices.Where(d => d.DeviceId == "D1").Select(d => d.FullAuthExpiresAt).SingleAsync());
            using var personal = await Post(HttpMethod.Patch, "api/v2/admin/client-devices/D1", new UpdateClientDeviceRequest
                { RowVersion = await Version("D1"), DeviceMode = ClientDeviceMode.Personal, WarehouseCd = "W1", AreaCd = "A" });
            Assert.True(personal.StatusCode == HttpStatusCode.OK, await OperationsFailure(personal));
            Assert.Null(await db.ClientDevices.Where(d => d.DeviceId == "D1").Select(d => d.FullAuthExpiresAt).SingleAsync());
            _ = await DeviceNativeLogin(host, h, "D2");
            await using var fault = new NpgsqlConnection(connection); await fault.OpenAsync();
            await using var install = new NpgsqlCommand($$"""
                CREATE FUNCTION public.cp6_test_device_revoke() RETURNS trigger LANGUAGE plpgsql AS $fault$
                BEGIN RAISE EXCEPTION 'simulated device refresh revocation failure'; END $fault$;
                CREATE TRIGGER cp6_test_device_revoke BEFORE UPDATE ON public."Sys_RefreshTokens"
                FOR EACH ROW WHEN (OLD."TenantId"='{{tenant}}'::uuid AND OLD."DeviceId"='D2' AND NEW."RevokedAt" IS NOT NULL)
                EXECUTE FUNCTION public.cp6_test_device_revoke();
                """, fault);
            await install.ExecuteNonQueryAsync();
            try
            {
                await Reject(async () => await Post(HttpMethod.Patch, "api/v2/admin/client-devices/D2", new UpdateClientDeviceRequest
                    { RowVersion = await Version("D2"), Status = ClientDeviceStatus.Disabled, WarehouseCd = "W1", AreaCd = "A" }),
                    HttpStatusCode.ServiceUnavailable, "DEVICE_AUTHORITY_UNAVAILABLE");
            }
            finally
            {
                await using var remove = new NpgsqlCommand("DROP TRIGGER cp6_test_device_revoke ON public.\"Sys_RefreshTokens\"; DROP FUNCTION public.cp6_test_device_revoke()", fault);
                await remove.ExecuteNonQueryAsync();
            }
        }
        if (operation == "activate")
        {
            var invalid = Activation(pendingTicket, "INVALID-KEY"); invalid.PublicKey = "-----BEGIN PUBLIC KEY-----\nnot-a-valid-key\n-----END PUBLIC KEY-----";
            await Reject(() => Anonymous(invalid), HttpStatusCode.BadRequest, "WM-DEVICE-PUBLIC-KEY-INVALID");
            var wrong = Activation(pendingTicket, "WRONG-TENANT"); wrong.TenantCode = administrator.Tenant.ToString("N");
            await Reject(() => Anonymous(wrong), HttpStatusCode.BadRequest, "WM-DEVICE-ACTIVATION-TENANT");
            var platform = Activation(pendingTicket, "WRONG-PLATFORM"); platform.Platform = "Android";
            await Reject(() => Anonymous(platform), HttpStatusCode.BadRequest, "WM-DEVICE-PLATFORM-MISMATCH");
            var expiring = await Ticket();
            var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expiring.ActivationToken)));
            await db.DeviceActivations.Where(a => a.TokenHash == tokenHash).ExecuteUpdateAsync(s => s.SetProperty(a => a.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
            await Reject(() => Anonymous(Activation(expiring, "EXPIRED-TICKET")), HttpStatusCode.BadRequest, "WM-DEVICE-ACTIVATION-EXPIRED");
            var lifetime = await db.Sys_Tenants.Where(t => t.Id == tenant).Select(t => t.ExpireDate).SingleAsync();
            await db.Sys_Tenants.Where(t => t.Id == tenant).ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpireDate, DateTime.SpecifyKind(DateTime.Now.AddMinutes(-1), DateTimeKind.Unspecified)));
            await Reject(() => Anonymous(Activation(pendingTicket, "EXPIRED-TENANT")), HttpStatusCode.BadRequest, "WM-DEVICE-TENANT-DISABLED");
            await db.Sys_Tenants.Where(t => t.Id == tenant).ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpireDate, lifetime));
            writer = await Login(host, h);
            using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var ecTicket = await Ticket(); var ecActivation = Activation(ecTicket, "ACT-EC"); ecActivation.PublicKey = ec.ExportSubjectPublicKeyInfoPem();
            using var registeredEc = await Anonymous(ecActivation);
            Assert.True(registeredEc.StatusCode == HttpStatusCode.OK, await OperationsFailure(registeredEc));
            var observation = DeviceHeartbeat(key, "ACT-EC");
            observation.Signature = Convert.ToBase64String(ec.SignData(Encoding.UTF8.GetBytes($"{observation.DeviceId}|{observation.Timestamp.ToUnixTimeSeconds()}|{observation.Nonce}|{observation.AppVersion}"), HashAlgorithmName.SHA256));
            using var ecHeartbeat = await Post(HttpMethod.Post, "api/client/devices/heartbeat", observation);
            Assert.True(ecHeartbeat.StatusCode == HttpStatusCode.OK, await OperationsFailure(ecHeartbeat));
        }
        if (operation == "pin")
            await Reject(() => Post(HttpMethod.Post, "api/v2/admin/client-devices/quick-pin", new SetWarehouseQuickPinRequest
                { UserName = "missing", BadgeNo = "badge", Pin = "123456" }), HttpStatusCode.NotFound, "WM-DEVICE-USER-NOT-FOUND");
        if (operation == "heartbeat")
        {
            await db.Sys_RoleActions.Where(a => a.MenuId == 461 && a.ActionCode == "device-manage").ExecuteDeleteAsync();
            writer = await Login(host, h);
            using var ordinary = await Post(HttpMethod.Post, "api/client/devices/heartbeat", DeviceHeartbeat(key, "D1"));
            Assert.True(ordinary.StatusCode == HttpStatusCode.OK, await OperationsFailure(ordinary));
            var bad = DeviceHeartbeat(key, "D1"); bad.Signature = Convert.ToBase64String(new byte[256]);
            await Reject(() => Post(HttpMethod.Post, "api/client/devices/heartbeat", bad), HttpStatusCode.Conflict, "WM-DEVICE-SIGNATURE-INVALID");
            var old = DeviceHeartbeat(key, "D1"); old.Timestamp = DateTimeOffset.UtcNow.AddMinutes(-6);
            await Reject(() => Post(HttpMethod.Post, "api/client/devices/heartbeat", old), HttpStatusCode.Conflict, "WM-DEVICE-SIGNATURE-INVALID");
            Assert.Equal(0, await db.MobileTasks.CountAsync()); Assert.Equal(0, await db.TaskCommandReceipts.CountAsync());
        }

        Task<HttpResponseMessage> Post(HttpMethod method, string path, object body) => host.Send(OperationsRequest(writer, method, path, body));
        Task<HttpResponseMessage> Anonymous(object body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "api/client/devices/activate") { Content = JsonContent.Create(body) };
            request.Headers.Add("Cookie", "cp6_csrf=" + writer["cp6_csrf"]);
            request.Headers.Add("X-CSRF-Token", writer["cp6_csrf"]);
            return host.Send(request);
        }
        Task<HttpResponseMessage> SendPending() => operation == "activate" ? Anonymous(pendingBody) : Post(operation == "update" ? HttpMethod.Patch : HttpMethod.Post,
            operation switch { "ticket" => "api/v2/admin/client-devices", "update" => "api/v2/admin/client-devices/D2",
                "pin" => "api/v2/admin/client-devices/quick-pin", _ => "api/client/devices/heartbeat" }, pendingBody);
        async Task<DeviceActivationTicket> Ticket()
        {
            using var issued = await Post(HttpMethod.Post, "api/v2/admin/client-devices", TicketRequest());
            Assert.True(issued.StatusCode == HttpStatusCode.OK, await OperationsFailure(issued));
            return (await issued.Content.ReadFromJsonAsync<DeviceActivationTicket>())!;
        }
        ActivateClientDeviceRequest Activation(DeviceActivationTicket source, string number) => new()
        { ActivationToken = source.ActivationToken, TenantCode = tenant.ToString("N"), DeviceId = number,
            Platform = "Windows", PublicKey = key.ExportSubjectPublicKeyInfoPem(), AppVersion = "1.0.0" };
        async Task<string> Version(string number) => Convert.ToBase64String((await db.ClientDevices.AsNoTracking().Where(d => d.DeviceId == number).Select(d => d.RowVersion).SingleAsync())!);
        Task<string> WriteSet() => DeviceWriteSet(h, nonceTable);
        async Task Reject(Func<Task<HttpResponseMessage>> request, HttpStatusCode status, string error)
        {
            var prior = await WriteSet(); using var denied = await request();
            Assert.True(denied.StatusCode == status, await OperationsFailure(denied)); Assert.Contains(error, await denied.Content.ReadAsStringAsync());
            Assert.Equal(prior, await WriteSet());
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Device_heartbeat_nonce_is_shared_by_two_actual_hosts()
    {
        using var key = RSA.Create(2048);
        await using var h = await DeviceLifecycleSeed(key.ExportSubjectPublicKeyInfoPem());
        var connection = h.Db.Database.GetConnectionString()!;
        await using var first = await AuthenticationApiProcess.Start(connection);
        await using var second = await AuthenticationApiProcess.Start(connection);
        Assert.NotEqual(first.ProcessId, second.ProcessId);
        var one = await Login(first, h); var two = await Login(second, h);
        var request = DeviceHeartbeat(key, "D1");
        using var committed = await first.Send(OperationsRequest(one, HttpMethod.Post, "api/client/devices/heartbeat", request));
        Assert.True(committed.StatusCode == HttpStatusCode.OK, await OperationsFailure(committed));
        var nonceTable = await h.Db.Database.SqlQuery<bool>($"SELECT to_regclass('cp6_authentication_v1.device_heartbeat_nonces') IS NOT NULL AS \"Value\"").SingleAsync();
        var before = await DeviceWriteSet(h, nonceTable);
        using var replay = await second.Send(OperationsRequest(two, HttpMethod.Post, "api/client/devices/heartbeat", request));
        await DeviceEvidence("device-cross-host.json", new { first.ProcessId, SecondProcess = second.ProcessId,
            FirstStatus = (int)committed.StatusCode, SecondStatus = (int)replay.StatusCode, NonceTable = nonceTable, Before = before, After = await DeviceWriteSet(h, nonceTable) });
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode); Assert.Contains("WM-DEVICE-REPLAY", await replay.Content.ReadAsStringAsync());
        Assert.Equal(before, await DeviceWriteSet(h, nonceTable));
        var fresh = DeviceHeartbeat(key, "D2");
        await using var blocker = new NpgsqlConnection(connection); await blocker.OpenAsync();
        await using var observer = new NpgsqlConnection(connection); await observer.OpenAsync();
        Task<HttpResponseMessage> left, right;
        await using (var locked = await blocker.BeginTransactionAsync())
        {
            await using var acquire = new NpgsqlCommand("SELECT cp6_authentication_v1.acquire()", blocker, locked); await acquire.ExecuteScalarAsync();
            left = first.Send(OperationsRequest(one, HttpMethod.Post, "api/client/devices/heartbeat", fresh));
            right = second.Send(OperationsRequest(two, HttpMethod.Post, "api/client/devices/heartbeat", fresh));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (true)
            {
                await using var query = new NpgsqlCommand("""
                    WITH RECURSIVE chain(origin,pid,visited) AS (
                      SELECT a.pid,a.pid,ARRAY[a.pid] FROM pg_stat_activity a
                      WHERE a.datname=current_database() AND a.wait_event_type='Lock'
                        AND a.query LIKE '%cp6_authentication_v1.acquire()%'
                      UNION ALL SELECT c.origin,b.pid,c.visited||b.pid FROM chain c
                        CROSS JOIN LATERAL unnest(pg_blocking_pids(c.pid)) b(pid) WHERE NOT b.pid=ANY(c.visited))
                    SELECT coalesce(jsonb_agg(jsonb_build_object('RequestPid',origin,'WaitPath',visited) ORDER BY origin),'[]')::text
                    FROM chain WHERE pid=@blocker
                    """, observer);
                query.Parameters.AddWithValue("blocker", blocker.ProcessID);
                using var waits = JsonDocument.Parse((string)(await query.ExecuteScalarAsync(deadline.Token))!);
                if (waits.RootElement.GetArrayLength() == 2)
                {
                    await DeviceEvidence("device-cross-host-waits.json", new { RootPid = blocker.ProcessID, Paths = waits.RootElement });
                    break;
                }
                if (left.IsCompleted || right.IsCompleted) Assert.Fail("Both requests must enter the actual native root wait.");
                await Task.Delay(35, deadline.Token);
            }
            await locked.RollbackAsync();
        }
        using var l = await left; using var r = await right;
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, new[] { l.StatusCode, r.StatusCode }.Order().ToArray());
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_authentication_v1.device_heartbeat_nonces WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_authentication_v1.device_heartbeat_nonces WHERE tenant_id={h.Tenant} AND expires_at>clock_timestamp()+interval '9 minutes' AND expires_at<=clock_timestamp()+interval '10 minutes'").SingleAsync());
        await DeviceEvidence("device-cross-host-race.json", new { FirstProcess = first.ProcessId, SecondProcess = second.ProcessId,
            NativeWaits = 2, Statuses = new[] { (int)l.StatusCode, (int)r.StatusCode }, NonceRows = 2 });
    }

    [CurrentPermissionPostgresFact]
    public async Task Device_scope_change_invalidates_old_native_registration()
    {
        using var key = RSA.Create(2048);
        await using var h = await DeviceLifecycleSeed(key.ExportSubjectPublicKeyInfoPem());
        h.Db.WmsRoleScopes.Add(new() { RoleId = 72, WarehouseCd = "W2" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var writer = await Login(host, h);
        var native = await DeviceNativeLogin(host, h, "D1");
        using var current = await host.Profile(native.AccessToken); Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var before = await h.Db.Database.SqlQuery<Guid>($"SELECT revision AS \"Value\" FROM cp6_authentication_v1.device_versions WHERE tenant_id={h.Tenant} AND device_id='D1'").SingleAsync();
        using var changed = await host.Send(OperationsRequest(writer, HttpMethod.Patch, "api/v2/admin/client-devices/D1",
            new UpdateClientDeviceRequest { RowVersion = Convert.ToBase64String((await h.Db.ClientDevices.Where(d => d.DeviceId == "D1").Select(d => d.RowVersion).SingleAsync())!), WarehouseCd = "W2", AreaCd = "A" }));
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        using var old = await host.Profile(native.AccessToken);
        var after = await h.Db.Database.SqlQuery<Guid>($"SELECT revision AS \"Value\" FROM cp6_authentication_v1.device_versions WHERE tenant_id={h.Tenant} AND device_id='D1'").SingleAsync();
        await DeviceEvidence("device-scope-revision.json", new { Tenant = h.Tenant, Before = before, After = after, OldSessionStatus = (int)old.StatusCode });
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode); Assert.NotEqual(before, after);
    }

    [CurrentPermissionPostgresFact]
    public async Task Device_upgrade_preserves_rows_and_rejects_pre_upgrade_native_sessions()
    {
        const string baseline = "20261110020400_IamMembershipRoleTenantReferenceV1";
        const string latest = "20261110080000_DeviceHeartbeatNonceV1";
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        await using var template = server.Context(tenant);
        var connection = new NpgsqlConnectionStringBuilder(template.Database.GetConnectionString())
            { Database = "device_upgrade_" + Guid.NewGuid().ToString("N") };
        // This database is evidence in this run's owned, ephemeral PostgreSQL cluster.
        await template.Database.ExecuteSqlRawAsync("CREATE DATABASE " + connection.Database);
        var database = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var profile = DatabaseMigrationProfile.For(database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), database, connection.ConnectionString,
            profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options;
        await using var db = new CP6Context(options, tenant); db.Database.SetCommandTimeout(60);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync(baseline);
        using var key = RSA.Create(2048);
        db.Sys_Tenants.Add(new() { Id = tenant.CurrentTenantId, TenantCode = tenant.CurrentTenantId.ToString("N"), TenantName = "Owned device upgrade" });
        db.Sys_Roles.Add(new() { RoleId = 72, RoleName = "Existing device user" });
        db.Sys_Users.Add(new() { UserName = "price-owner", Password = new BCryptPasswordHasher(4).Hash("Price1!Password"), RoleId = 72 });
        db.ClientDevices.Add(new() { DeviceId = "D1", Platform = "Windows", PublicKey = key.ExportSubjectPublicKeyInfoPem(),
            WarehouseCd = "W1", AreaCd = "A", AppVersion = "1.0.0", ActivatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
            DeviceMode = ClientDeviceMode.Shared });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await using var host = await AuthenticationApiProcess.Start(connection.ConnectionString);
        var oldSession = await DeviceNativeLogin(host, tenant.CurrentTenantId, "D1");
        using var beforeProfile = await host.Profile(oldSession.AccessToken); Assert.Equal(HttpStatusCode.OK, beforeProfile.StatusCode);
        var before = await BusinessRow(); var previous = await RegistrationRevision();
        await migrator.MigrateAsync();
        Assert.Contains(latest, await db.Database.GetAppliedMigrationsAsync());
        using var rejected = await host.Profile(oldSession.AccessToken);
        var after = await BusinessRow(); var current = await RegistrationRevision();
        var nonces = await db.Database.SqlQuery<bool>($"SELECT to_regclass('cp6_authentication_v1.device_heartbeat_nonces') IS NOT NULL AS \"Value\"").SingleAsync();
        // Persist the pre-refresh comparison: a legal fresh login itself changes device full-auth fields.
        await DeviceEvidence("device-upgrade.json", new { Database = connection.Database, Baseline = baseline, Latest = latest,
            Before = before, After = after, PreviousRevision = previous, CurrentRevision = current, NonceTable = nonces,
            OldSessionStatus = (int)rejected.StatusCode });
        Assert.Equal(before, after); Assert.NotEqual(previous, current); Assert.True(nonces);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        var fresh = await DeviceNativeLogin(host, tenant.CurrentTenantId, "D1");
        using var restored = await host.Profile(fresh.AccessToken); Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        await DeviceEvidence("device-upgrade-login.json", new { FreshSessionStatus = (int)restored.StatusCode });

        Task<string> BusinessRow() => db.Database.SqlQuery<string>($"SELECT to_jsonb(d)::text AS \"Value\" FROM public.\"T_ClientDevice\" d WHERE d.\"TenantId\"={tenant.CurrentTenantId} AND d.\"DeviceId\"='D1'").SingleAsync();
        Task<Guid> RegistrationRevision() => db.Database.SqlQuery<Guid>($"SELECT revision AS \"Value\" FROM cp6_authentication_v1.device_versions WHERE tenant_id={tenant.CurrentTenantId} AND device_id='D1'").SingleAsync();
    }

    private static CreateDeviceActivationRequest TicketRequest(string warehouse = "W1") => new()
        { Platform = "windows", DeviceMode = "shared", WarehouseCd = warehouse, AreaCd = "A", ValidMinutes = 1 };
    private static ClientDeviceHeartbeatRequest DeviceHeartbeat(RSA key, string number)
    {
        var request = new ClientDeviceHeartbeatRequest { DeviceId = number, AppVersion = "2.0.0", Timestamp = DateTimeOffset.UtcNow,
            Nonce = Guid.NewGuid().ToString("N"), BatteryPercent = 120, CurrentTaskNo = "declared-only", NetworkType = "test" };
        request.Signature = Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes($"{number}|{request.Timestamp.ToUnixTimeSeconds()}|{request.Nonce}|{request.AppVersion}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return request;
    }
    private static HttpRequestMessage DeviceBearer(string token, HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request;
    }
    private static Task<TokenSessionDto> DeviceNativeLogin(AuthenticationApiProcess host, Harness h, string number)
        => DeviceNativeLogin(host, h.Tenant, number);
    private static async Task<TokenSessionDto> DeviceNativeLogin(AuthenticationApiProcess host, Guid tenant, string number)
    {
        using var response = await host.Send(new(HttpMethod.Post, "api/client-auth/login") { Content = JsonContent.Create(new NativeLoginRequest
        { TenantCode = tenant.ToString("N"), UserName = "price-owner", Password = "Price1!Password", Client = new() { ClientKind = "Windows", DeviceId = number, AppVersion = "1.0.0" } }) });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
        return (await response.Content.ReadFromJsonAsync<NativeAuthResult>())!.Session!;
    }
    private static async Task<FenceWait> DeviceWait(NpgsqlConnection observer, int pause, int business, string table, Task<HttpResponseMessage> pending)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true)
        {
            await using var query = new NpgsqlCommand("""
                SELECT a.pid,b.blocker,a.usename,a.wait_event,clock_timestamp() FROM pg_stat_activity a
                CROSS JOIN LATERAL unnest(pg_blocking_pids(a.pid)) b(blocker)
                WHERE a.datname=current_database() AND a.wait_event_type='Lock'
                AND ((b.blocker=@pause AND (a.query LIKE '%cp6_authentication_v1.acquire()%' OR a.query LIKE @table))
                  OR (b.blocker=@business AND a.query LIKE @table)) ORDER BY a.pid LIMIT 1
                """, observer);
            query.Parameters.AddWithValue("pause", pause); query.Parameters.AddWithValue("business", business); query.Parameters.AddWithValue("table", "%" + table + "%");
            await using (var rows = await query.ExecuteReaderAsync(deadline.Token))
                if (await rows.ReadAsync(deadline.Token)) return new(rows.GetInt32(0), rows.GetInt32(1), rows.GetString(2), rows.GetString(3), rows.GetFieldValue<DateTimeOffset>(4));
            if (pending.IsCompleted) Assert.Fail("Device request completed before its native wait: " + await OperationsFailure(await pending));
            await Task.Delay(35, deadline.Token);
        }
    }
    private static Task DeviceEvidence<T>(string name, T value) => File.WriteAllTextAsync(Path.Combine(
        Environment.GetEnvironmentVariable(CurrentPermissionPostgresFactAttribute.RootVariable)!, name), JsonSerializer.Serialize(value));
    private static async Task<string> DeviceWriteSet(Harness h, bool nonceTable)
    {
        var tenant = h.Tenant;
        var nonces = nonceTable ? await h.Db.Database.SqlQuery<string>($"SELECT coalesce(jsonb_agg(to_jsonb(n) ORDER BY n.device_id,n.nonce_hash),'[]')::text AS \"Value\" FROM cp6_authentication_v1.device_heartbeat_nonces n WHERE n.tenant_id={tenant}").SingleAsync() : "[]";
        return await h.Db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object(
              'Devices',(SELECT coalesce(jsonb_agg(to_jsonb(d) ORDER BY d."Id"),'[]') FROM public."T_ClientDevice" d WHERE d."TenantId"={tenant}),
              'Activations',(SELECT coalesce(jsonb_agg(to_jsonb(a) ORDER BY a."Id"),'[]') FROM public."T_DeviceActivation" a WHERE a."TenantId"={tenant}),
              'Users',(SELECT coalesce(jsonb_agg(to_jsonb(u) ORDER BY u."Id"),'[]') FROM public."Sys_Users" u WHERE u."TenantId"={tenant}),
              'Refresh',(SELECT coalesce(jsonb_agg(to_jsonb(r) ORDER BY r."Id"),'[]') FROM public."Sys_RefreshTokens" r WHERE r."TenantId"={tenant}),
              'Versions',(SELECT coalesce(jsonb_agg(to_jsonb(v) ORDER BY v.device_id),'[]') FROM cp6_authentication_v1.device_versions v WHERE v.tenant_id={tenant}),
              'Windows',(SELECT coalesce(jsonb_agg(to_jsonb(w) ORDER BY w.device_id),'[]') FROM cp6_authentication_v1.shared_windows w WHERE w.tenant_id={tenant}),
              'Nonces',CAST({nonces} AS jsonb))::text AS "Value"
            """).SingleAsync();
    }
}
