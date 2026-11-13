using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_OIDC_family_logout_and_order_write_take_the_same_root_first(bool logoutFirst)
    {
        await using var h = await Seed();
        var db = h.Db;
        var connection = db.Database.GetConnectionString()!;
        const string client = "CP6.Web", secret = "owned-oidc-family-fixture";
        const string redirect = "https://iam.example.test/callback", signedOut = "https://iam.example.test/signed-out";
        var settings = IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant);
        settings["CrmOidc__Clients__0__SecretSha256"] = CrmOidcCrypto.Hash(secret);
        settings["CrmOidc__Clients__0__PostLogoutRedirectUris__0"] = signedOut;
        await using var host = await AuthenticationApiProcess.Start(connection, settings, allowAutoRedirect: false);
        var cookies = await Login(host, h);
        var other = await Login(host, h);
        var verifier = CrmOidcCrypto.RandomToken();
        var authorize = QueryHelpers.AddQueryString("connect/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = client, ["redirect_uri"] = redirect, ["response_type"] = "code",
            ["code_challenge_method"] = "S256", ["code_challenge"] = CrmOidcCrypto.Challenge(verifier),
            ["scope"] = "openid profile crm", ["state"] = CrmOidcCrypto.RandomToken(), ["nonce"] = CrmOidcCrypto.RandomToken(),
            ["organization_id"] = h.Tenant.ToString("D")
        });
        using var authorization = await host.Send(OperationsRequest(cookies, HttpMethod.Get, authorize));
        Assert.True(authorization.StatusCode == HttpStatusCode.Redirect, await OperationsFailure(authorization));
        using var callback = await host.Send(OperationsRequest(cookies, HttpMethod.Get, authorization.Headers.Location!.PathAndQuery));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        var code = QueryHelpers.ParseQuery(callback.Headers.Location!.Query)["code"].ToString();
        Assert.NotEmpty(code);
        using var exchange = await host.Send(ClientForm("connect/token", new()
        {
            ["grant_type"] = "authorization_code", ["code"] = code,
            ["redirect_uri"] = redirect, ["code_verifier"] = verifier
        }));
        Assert.True(exchange.StatusCode == HttpStatusCode.OK, await OperationsFailure(exchange));
        using var tokens = JsonDocument.Parse(await exchange.Content.ReadAsStringAsync());
        var hint = tokens.RootElement.GetProperty("id_token").GetString()!;
        var sid = Guid.Parse(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(hint)
            .Claims.Single(claim => claim.Type == "sid").Value);
        var aggregate = IdentityEventContracts.TokenAggregate(settings["CrmOidc__Issuer"], sid.ToString("D"));
        var hash = AuthenticationInteractionStoreV1.Hash(cookies["cp6_rt"]);
        var family = (await db.Sys_RefreshTokens.AsNoTracking().Where(r => r.TokenHash == hash)
            .Select(r => r.BrowserSessionId).SingleAsync())!.Value;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."T_DocSequence" ("Id", "FuncCode", "LastSeq", "CreateDate")
            VALUES ({Guid.NewGuid()}, 'ORD', 0, CURRENT_TIMESTAMP)
            ON CONFLICT ("FuncCode") DO NOTHING
            """);
        var before = await Number();
        var original = await ReadRevocation();
        Assert.Equal(new OidcRevocation(false, false, false, 0, 0), original);

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
                await using var row = new NpgsqlCommand(
                    """SELECT 1 FROM public."Sys_BrowserSessions" WHERE "Id"=@family FOR UPDATE""", barrier, hold);
                row.Parameters.AddWithValue("family", family);
                Assert.Equal(1, await row.ExecuteScalarAsync());
                loggingOut = Logout();
                firstWait = await WaitForFence(observer, barrier.ProcessID, "%Sys_BrowserSessions%FOR UPDATE%", null, loggingOut);
                writing = Reserve();
                rootWait = await WaitForFence(observer, firstWait.Backend, "%cp6_authentication_v1.acquire()%", null, writing);
            }
            else
            {
                await using var row = new NpgsqlCommand(
                    """SELECT 1 FROM public."T_DocSequence" WHERE "FuncCode"='ORD' FOR UPDATE""", barrier, hold);
                Assert.Equal(1, await row.ExecuteScalarAsync());
                writing = Reserve();
                firstWait = await WaitForFence(observer, barrier.ProcessID, "%INSERT INTO%T_DocSequence%", null, writing);
                loggingOut = Logout();
                rootWait = await WaitForFence(observer, firstWait.Backend, "%cp6_authentication_v1.acquire()%", null, loggingOut);
                // An authority waiter must leave the downstream family row available.
                await using var unlocked = new NpgsqlCommand(
                    """SELECT 1 FROM public."Sys_BrowserSessions" WHERE "Id"=@family FOR UPDATE NOWAIT""", observer);
                unlocked.Parameters.AddWithValue("family", family);
                Assert.Equal(1, await unlocked.ExecuteScalarAsync());
            }
            Assert.Equal(before, await Number());
            Assert.Equal(original, await ReadRevocation());
            await hold.RollbackAsync();
        }

        using var revoked = await loggingOut;
        Assert.True(revoked.StatusCode == HttpStatusCode.OK, await OperationsFailure(revoked));
        using var result = await writing;
        Assert.Equal(logoutFirst ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, result.StatusCode);
        var committed = await Number();
        Assert.Equal(before + (logoutFirst ? 0 : 1), committed);
        Assert.Equal(new OidcRevocation(true, true, true, 1, 1), await ReadRevocation());
        using var stale = await Reserve();
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(committed, await Number());
        using var unchanged = await host.Profile(other["cp6_at"]);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        using var repeat = await Logout();
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal(new OidcRevocation(true, true, true, 1, 1), await ReadRevocation());
        output.WriteLine(JsonSerializer.Serialize(new { LogoutFirst = logoutFirst, FirstWait = firstWait,
            RootWait = rootWait, Before = before, After = committed, Status = (int)result.StatusCode }));

        Task<HttpResponseMessage> Reserve() => host.Send(OperationsRequest(cookies, HttpMethod.Get, "api/orders/next-seq"));
        Task<HttpResponseMessage> Logout() => host.Send(ClientForm("connect/end-session", new()
        {
            ["id_token_hint"] = hint, ["post_logout_redirect_uri"] = signedOut
        }));
        HttpRequestMessage ClientForm(string path, Dictionary<string, string> data)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(data) };
            request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(client + ":" + secret)));
            return request;
        }
        Task<int> Number() => db.DocSequences.AsNoTracking().Where(row => row.FuncCode == "ORD")
            .Select(row => row.LastSeq).SingleAsync();
        async Task<OidcRevocation> ReadRevocation()
        {
            var json = await db.Database.SqlQuery<string>($"""
                SELECT json_build_object(
                  'Family', (SELECT "LoggedOutAtUtc" IS NOT NULL FROM public."Sys_BrowserSessions" WHERE "Id"={family}),
                  'Refresh', (SELECT "RevokedAt" IS NOT NULL FROM public."Sys_RefreshTokens" WHERE "TokenHash"={hash}),
                  'Grant', (SELECT "Revoked" FROM public."CrmOidcGrant" WHERE "Id"={sid}),
                  'Snapshot', COALESCE((SELECT "Version" FROM crm_identity."Snapshot"
                              WHERE "TenantId"={h.Tenant} AND "AggregateId"={aggregate}), 0),
                  'Outbox', (SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage"
                             WHERE "TenantId"={h.Tenant} AND "AggregateId"={aggregate}))::text AS "Value"
                """).SingleAsync();
            return JsonSerializer.Deserialize<OidcRevocation>(json)!;
        }
    }

    private sealed record OidcRevocation(bool Family, bool Refresh, bool Grant, int Snapshot, int Outbox);
}
