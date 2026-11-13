using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using CP6.Core.Services.Sys;
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
    [CurrentPermissionPostgresFact]
    public async Task Access_revocation_migration_preserves_existing_issuance_and_refresh_evidence()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        await using var owner = server.Context(tenant);
        await using var connection = new NpgsqlConnection(owner.Database.GetConnectionString());
        await connection.OpenAsync();
        var database = "w536_access_" + Guid.NewGuid().ToString("N");
        using var names = new NpgsqlCommandBuilder();
        await using (var create = new NpgsqlCommand("CREATE DATABASE " + names.QuoteIdentifier(database), connection))
            await create.ExecuteNonQueryAsync();

        await using var db = server.Context(tenant);
        db.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(owner.Database.GetConnectionString())
            { Database = database }.ConnectionString);
        var migrations = db.GetService<IMigrator>();
        await migrations.MigrateAsync("20261113090000_RmaTenantModelV2Shared");
        var jti = Guid.NewGuid().ToString();
        var refreshHash = AuthenticationInteractionStoreV1.Hash(Guid.NewGuid().ToString());
        var completion = Guid.NewGuid();
        const string evidence = "{}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_authentication_v1.interactions (completion_id,challenge_hash,proof,purpose,state)
            VALUES ({completion},{Guid.NewGuid().ToString()},{evidence}::jsonb,'primary','completed');
            INSERT INTO cp6_authentication_v1.session_evidence (refresh_hash,access_jti,completion_id,evidence)
            VALUES ({refreshHash},{jti},{completion},{evidence}::jsonb);
            INSERT INTO cp6_authentication_v1.access_sessions (access_jti,refresh_hash)
            VALUES ({jti},{refreshHash});
            """);
        var before = await Alias();

        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(before, await Alias());
        Assert.False(await new AuthenticationInteractionStoreV1(db).IsAccessRevokedAsync(jti));
        Assert.Equal(1, await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_authentication_v1.session_evidence
            WHERE completion_id={completion} AND refresh_hash={refreshHash} AND access_jti={jti}
            """).SingleAsync());
        Assert.Throws<NotSupportedException>(() => migrations.GenerateScript(
            "20261113133000_AccessSessionRevocationV1", "20261113090000_RmaTenantModelV2Shared"));

        Task<string> Alias() => db.Database.SqlQuery<string>($"""
            SELECT refresh_hash AS "Value" FROM cp6_authentication_v1.access_sessions WHERE access_jti={jti}
            """).SingleAsync();
    }

    [TenantManagementPostgresTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PGT017_access_only_logout_and_business_share_the_commit_fence(bool native, bool logoutFirst)
    {
        await using var h = await Seed();
        var db = h.Db;
        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "access-logout", AppVersion = "1.0.0" };
        if (native)
        {
            db.ClientDevices.Add(new ClientDevice
            {
                DeviceId = client.DeviceId, Platform = client.ClientKind, PublicKey = "fixture",
                DeviceMode = ClientDeviceMode.Shared, Status = ClientDeviceStatus.Active, ActivatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection);
        var healthy = await Login(host, h);
        var web = native ? null : await Login(host, h);
        var session = native ? await DeviceNativeLogin(host, h, client.DeviceId) : null;
        var access = native ? session!.AccessToken : web!["cp6_at"];
        var rawRefresh = native ? session!.RefreshToken : web!["cp6_rt"];
        var jti = new JwtSecurityTokenHandler().ReadJwtToken(access).Id;
        var refreshHash = AuthenticationInteractionStoreV1.Hash(rawRefresh);
        var accessOnly = web?.Where(x => x.Key != "cp6_rt").ToDictionary();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."T_DocSequence" ("Id", "FuncCode", "LastSeq", "CreateDate")
            VALUES ({Guid.NewGuid()}, 'ORD', 0, CURRENT_TIMESTAMP)
            ON CONFLICT ("FuncCode") DO NOTHING
            """);
        var before = await Number();
        var audits = await LogoutAudits();
        var store = new AuthenticationInteractionStoreV1(db);
        Assert.False((await store.ReadSessionAsync(jti))!.Rejected);

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> writing;
        Task<HttpResponseMessage> loggingOut;
        FenceWait firstWait;
        FenceWait rootWait;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            if (logoutFirst)
            {
                await using var lockRow = new NpgsqlCommand(
                    "SELECT 1 FROM cp6_authentication_v1.access_sessions WHERE access_jti=@jti FOR UPDATE", barrier, hold);
                lockRow.Parameters.AddWithValue("jti", jti);
                Assert.Equal(1, await lockRow.ExecuteScalarAsync());
                loggingOut = Logout();
                firstWait = await WaitForFence(observer, barrier.ProcessID, "%UPDATE%access_sessions%", null, loggingOut);
                writing = Reserve();
                rootWait = await WaitForFence(observer, firstWait.Backend, "%cp6_authentication_v1.acquire()%", null, writing);
            }
            else
            {
                await using var lockRow = new NpgsqlCommand(
                    """SELECT 1 FROM public."T_DocSequence" WHERE "FuncCode"='ORD' FOR UPDATE""", barrier, hold);
                Assert.Equal(1, await lockRow.ExecuteScalarAsync());
                writing = Reserve();
                firstWait = await WaitForFence(observer, barrier.ProcessID, "%INSERT INTO%T_DocSequence%", null, writing);
                loggingOut = Logout();
                rootWait = await WaitForFence(observer, firstWait.Backend, "%cp6_authentication_v1.acquire()%", null, loggingOut);
            }
            Assert.Equal(before, await Number());
            Assert.Equal(audits, await LogoutAudits());
            Assert.False((await store.ReadSessionAsync(jti))!.Rejected);
            await hold.RollbackAsync();
        }

        using var loggedOut = await loggingOut;
        Assert.True(loggedOut.StatusCode == HttpStatusCode.OK, await OperationsFailure(loggedOut));
        using var result = await writing;
        Assert.Equal(logoutFirst ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, result.StatusCode);
        var after = await Number();
        Assert.Equal(before + (logoutFirst ? 0 : 1), after);
        Assert.Equal(audits + 1, await LogoutAudits());
        Assert.True((await store.ReadSessionAsync(jti))!.Rejected);
        Assert.False((await store.ReadSessionByRefreshAsync(refreshHash))!.Rejected);
        Assert.Null(await db.Sys_RefreshTokens.AsNoTracking().Where(row => row.TokenHash == refreshHash)
            .Select(row => row.RevokedAt).SingleAsync());
        using var stale = await Reserve();
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(after, await Number());

        using var alive = await host.Profile(healthy["cp6_at"]);
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
        using var refreshed = await host.Send(native
            ? new(HttpMethod.Post, "api/client-auth/refresh") { Content = System.Net.Http.Json.JsonContent.Create(
                new NativeRefreshRequest { Client = client, RefreshToken = rawRefresh }) }
            : OperationsRequest(web!, HttpMethod.Post, "api/auth/refresh", new { }));
        Assert.True(refreshed.StatusCode == HttpStatusCode.OK, await OperationsFailure(refreshed));
        output.WriteLine(JsonSerializer.Serialize(new { Native = native, LogoutFirst = logoutFirst,
            FirstWait = firstWait, RootWait = rootWait, Before = before, After = after, Status = (int)result.StatusCode }));

        Task<int> Number() => db.DocSequences.AsNoTracking().Where(row => row.FuncCode == "ORD")
            .Select(row => row.LastSeq).SingleAsync();
        Task<int> LogoutAudits() => db.Sys_SecurityLogs.CountAsync(row =>
            row.UserId == h.User && row.EventType == (int)SecurityEventType.Logout);
        Task<HttpResponseMessage> Reserve()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "api/orders/next-seq");
            request.Headers.Authorization = new("Bearer", access);
            return host.Send(request);
        }
        Task<HttpResponseMessage> Logout() => host.Send(native
            ? DeviceBearer(access, HttpMethod.Post, "api/client-auth/logout", new NativeLogoutRequest { Client = client })
            : OperationsRequest(accessOnly!, HttpMethod.Post, "api/auth/logout", new { }));
    }
}
