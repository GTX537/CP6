using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private const string InquirySignedOut = "https://crm.cp6.test/signed-out";
    private const string InquiryRevoker = "pgt017-inquiry-admin";

    private static async Task SeedInquiryUserRevoker(Harness h)
    {
        await GrantHttpPermissions(h);
        h.Db.Sys_Users.Add(new()
        {
            UserName = InquiryRevoker, RoleId = 71, Enable = true,
            Password = new BCryptPasswordHasher(4).Hash("Iam1!Password")
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }

    private static async Task ExerciseInquiryUserOrSessionRevocation(AuthenticationApiProcess host, Harness h,
        string bridge, string revocation, string hint, Dictionary<string, string> originalCookies, string clientSecret, string issuer)
    {
        Assert.Contains(revocation, new[] { "user", "session" });
        var sessionOnly = revocation == "session";
        var sid = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(hint).Claims.Single(c => c.Type == "sid").Value);
        var tokenAggregate = IdentityEventContracts.TokenAggregate(issuer, sid.ToString("D"));
        var userAggregate = $"user:{h.ActorId:D}";
        var refreshHash = AuthenticationInteractionStoreV1.Hash(originalCookies["cp6_rt"]);
        var family = (await h.Db.Sys_RefreshTokens.AsNoTracking().Where(r => r.TokenHash == refreshHash)
            .Select(r => r.BrowserSessionId).SingleAsync())!.Value;
        Dictionary<string, string>? manager = null;
        IamUserCommandV1? command = null;
        if (!sessionOnly)
        {
            using var login = await host.Send(new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
            {
                Content = JsonContent.Create(new { UserName = InquiryRevoker, Password = "Iam1!Password", TenantCode = $"iam_{h.Tenant:N}" })
            });
            Assert.True(login.StatusCode == HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
            manager = login.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0].Split('=', 2))
                .Where(x => x[1].Length > 0).ToDictionary(x => x[0], x => x[1]);
            using var read = await host.Send(Request(HttpMethod.Get, $"api/iam/users/editor?id={h.ActorId:D}", manager));
            Assert.True(read.StatusCode == HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
            var editor = (await read.Content.ReadFromJsonAsync<IamUserEditorV1>())!;
            var user = editor.User!;
            command = new()
            {
                Id = user.Id, UserName = user.UserName, NickName = user.NickName, DeptId = user.DeptId,
                ManagerId = user.ManagerId, Email = user.Email, RoleId = user.RoleId, RoleIds = user.RoleIds,
                Enable = false, OperationId = Guid.NewGuid(), ExpectedVersion = user.Version,
                ExpectedReferencesVersion = editor.ReferencesVersion, Reason = "PGT-017 simulated account revocation"
            };
        }
        var before = await Read();
        Assert.True(before.Enabled);
        Assert.False(before.GrantRevoked);
        Assert.False(before.FamilyRevoked);
        await WaitConfigurationBridge(bridge, "ready");
        Task<HttpResponseMessage> change;
        if (Environment.GetEnvironmentVariable("CP6_PGT017_ORDER") == "pause-first")
        {
            await WaitConfigurationBridge(bridge, "permission-held");
            change = Revoke();
        }
        else
        {
            await WaitConfigurationBridge(bridge, "owner-held");
            change = Revoke();
            try
            {
                await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
                await crm.OpenAsync();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await using var blocked = new NpgsqlCommand("""
                    SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                      WHERE datname=current_database() AND application_name='cp6-main-inquiry-tenant-pause-v1'
                        AND wait_event_type='Lock' AND cardinality(pg_blocking_pids(pid))>0)
                    """, crm);
                while (!(bool)(await blocked.ExecuteScalarAsync(deadline.Token))!)
                {
                    if (change.IsCompleted)
                    {
                        using var early = await change;
                        Assert.Fail($"Main {revocation} revocation returned HTTP {(int)early.StatusCode} while the CRM Owner was waiting.");
                    }
                    await Task.Delay(25, deadline.Token);
                }
                Assert.Equal(before, await Read());
            }
            finally { await File.WriteAllTextAsync(Path.Combine(bridge, "release-owner"), "PASS"); }
        }
        using var result = await change;
        Assert.True(result.StatusCode == HttpStatusCode.OK, await result.Content.ReadAsStringAsync());
        var after = await Read();
        Assert.Equal(sessionOnly, after.Enabled);
        Assert.True(after.GrantRevoked);
        Assert.Equal(sessionOnly, after.FamilyRevoked);
        Assert.Equal(sessionOnly, after.RefreshRevoked);
        Assert.Equal(before.TokenSnapshot + 1, after.TokenSnapshot);
        Assert.Equal(before.TokenOutbox + 1, after.TokenOutbox);
        Assert.Equal(before.UserSnapshot + (sessionOnly ? 0 : 1), after.UserSnapshot);
        Assert.Equal(before.UserOutbox + (sessionOnly ? 0 : 1), after.UserOutbox);
        Assert.Equal(before.Generation + (sessionOnly ? 1 : 2), after.Generation);
        Assert.Equal(sessionOnly ? 0 : 1, after.Audits);
        Assert.Equal(sessionOnly ? 0 : 1, after.Receipts);
        using var replay = await Revoke();
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        if (!sessionOnly) Assert.Equal(await result.Content.ReadFromJsonAsync<IamOperationV1>(),
            await replay.Content.ReadFromJsonAsync<IamOperationV1>());
        Assert.Equal(after, await Read());
        await File.WriteAllTextAsync(Path.Combine(bridge, "paused"), "PASS");
        await WaitConfigurationBridge(bridge, "finished");

        Task<HttpResponseMessage> Revoke()
        {
            if (!sessionOnly)
                return host.Send(Request(HttpMethod.Put, $"api/iam/users/{h.ActorId:D}", manager!, command));
            var request = new HttpRequestMessage(HttpMethod.Post, "connect/end-session")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["id_token_hint"] = hint, ["post_logout_redirect_uri"] = InquirySignedOut
                })
            };
            request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("CP6.Web:" + clientSecret)));
            return host.Send(request);
        }

        async Task<InquiryRevocationState> Read()
        {
            var tenant = h.Tenant;
            var operation = command?.OperationId ?? Guid.Empty;
            var json = await h.Db.Database.SqlQuery<string>($"""
                SELECT jsonb_build_object(
                    'Enabled', (SELECT "Enable" FROM public."Sys_Users" WHERE "Id"={h.ActorId} AND "TenantId"={tenant}),
                    'FamilyRevoked', (SELECT "LoggedOutAtUtc" IS NOT NULL FROM public."Sys_BrowserSessions" WHERE "Id"={family}),
                    'RefreshRevoked', (SELECT "RevokedAt" IS NOT NULL FROM public."Sys_RefreshTokens" WHERE "TokenHash"={refreshHash}),
                    'GrantRevoked', (SELECT "Revoked" FROM public."CrmOidcGrant" WHERE "Id"={sid}),
                    'UserSnapshot', (SELECT "Version" FROM crm_identity."Snapshot" WHERE "TenantId"={tenant} AND "AggregateId"={userAggregate}),
                    'TokenSnapshot', coalesce((SELECT "Version" FROM crm_identity."Snapshot" WHERE "TenantId"={tenant} AND "AggregateId"={tokenAggregate}),0),
                    'Generation', (SELECT "Generation" FROM public."CrmIdentityTenantGenerations" WHERE "TenantId"={tenant}),
                    'UserOutbox', (SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={tenant} AND "AggregateId"={userAggregate}),
                    'TokenOutbox', (SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={tenant} AND "AggregateId"={tokenAggregate}),
                    'Audits', (SELECT count(*) FROM public."Sys_SecurityLogs" WHERE "TenantId"={tenant}
                        AND "EventType"={(int)SecurityEventType.IamAdministrationChanged}
                        AND "ContextJson"::jsonb->>'operationId'={operation.ToString("D")}),
                    'Receipts', (SELECT count(*) FROM cp6_authentication_v1.iam_operations WHERE tenant_id={tenant} AND operation_id={operation}))::text AS "Value"
                """).SingleAsync();
            return JsonSerializer.Deserialize<InquiryRevocationState>(json)!;
        }
    }

    private sealed record InquiryRevocationState(bool Enabled, bool FamilyRevoked, bool RefreshRevoked, bool GrantRevoked,
        int UserSnapshot, int TokenSnapshot, long Generation, long UserOutbox, long TokenOutbox, long Audits, long Receipts);
}
