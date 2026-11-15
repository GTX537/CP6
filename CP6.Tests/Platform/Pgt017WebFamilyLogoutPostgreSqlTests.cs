using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [Pgt017MainFact]
    public async Task PGT017_plain_Web_logout_routes_seal_the_original_CRM_browser_family()
    {
        var route = Environment.GetEnvironmentVariable("CP6_W665_ROUTE") ?? throw new InvalidOperationException("Expected owned Web exit case.");
        await using var h = await Seed();
        var pair = await GrantIntake(h);
        var user = await h.Db.Sys_Users.SingleAsync(u => u.Id == h.ActorId);
        user.AuthenticationEpoch = Guid.NewGuid();
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var options = InquiryPermissionSigningTests.Options();
        await new IdentityBootstrapService(h.Db, ContextRuntime(h.Tenant, options.Issuer)).InitializeAsync(h.Tenant);
        h.Db.ChangeTracker.Clear();

        const string redirect = "https://crm.cp6.test/callback";
        var clientSecret = CrmOidcCrypto.RandomToken();
        var store = Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!);
        var seal = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION"));
        var crmReaderConnection = Environment.GetEnvironmentVariable("CP6_W665_CRM_READER");
        var readerRole = new NpgsqlConnectionStringBuilder(crmReaderConnection);
        await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
        await crm.OpenAsync();
        Assert.Equal(("127.0.0.1", crm.Port, crm.Database, "w665_main_seal"), (seal.Host, seal.Port, seal.Database, seal.Username));
        Assert.Equal(("127.0.0.1", crm.Port, crm.Database), (readerRole.Host, readerRole.Port, readerRole.Database));
        Assert.Matches("^w51_runtime_[a-f0-9]{8}$", readerRole.Username!);
        await InstallUserSealFixture(crm, seal, h.Tenant, store);
        using (var quote = new NpgsqlCommandBuilder())
        {
            var runtimeRole = quote.QuoteIdentifier(readerRole.Username!);
            await using var grant = new NpgsqlCommand($"""
                GRANT USAGE ON SCHEMA crm_identity_lock_v1,crm_inquiry_tenant_pause_v1 TO {runtimeRole};
                GRANT EXECUTE ON FUNCTION crm_identity_lock_v1.hold_v1(uuid),
                  crm_inquiry_tenant_pause_v1.identity_current_v1(uuid,text,integer,text) TO {runtimeRole};
                """, crm);
            await grant.ExecuteNonQueryAsync();
        }

        var environment = IdentityEnvironment(h.Tenant);
        var binding = $"CrmInquiryTenantPause__Tenants__{h.Tenant:D}__";
        environment[binding + "StoreId"] = store.ToString("D");
        environment[binding + "ConnectionString"] = seal.ConnectionString;
        environment["CrmOidc__Issuer"] = options.Issuer;
        environment["CrmOidc__Clients__0__SecretSha256"] = CrmOidcCrypto.Hash(clientSecret);
        environment["CrmOidc__Clients__0__RedirectUris__0"] = redirect;
        environment["CrmOidc__TrustedTokenProxyAddresses__0"] = "127.0.0.1";
        environment["InquiryPermissionSigning__Enabled"] = "true";
        environment["InquiryPermissionSigning__Issuer"] = options.Issuer;
        environment["InquiryPermissionSigning__KeyId"] = options.KeyId;
        environment["InquiryPermissionSigning__PrivateKeyBase64"] = options.PrivateKeyBase64;
        environment["InquiryPermissionSigning__PublicKeyBase64"] = options.PublicKeyBase64;
        environment["InquiryPermissionSigning__ActiveFromUtc"] = options.ActiveFromUtc.ToString("O");
        environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT");
        environment["JWT__Secret"] = CrmOidcCrypto.RandomToken() + CrmOidcCrypto.RandomToken();

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, environment, allowAutoRedirect: false);
        var originKey = AuthenticationInteractionStoreV1.NewChallenge();
        var original = await SignIn(originKey);
        var beforeRotation = await Exchange(original);
        var firstRotation = await Rotate(original, AuthenticationInteractionStoreV1.NewChallenge());
        var closeKey = AuthenticationInteractionStoreV1.NewChallenge();
        var current = await Rotate(firstRotation, closeKey);
        var afterRotation = await Exchange(current);
        var other = await SignIn(AuthenticationInteractionStoreV1.NewChallenge());
        var independent = await Exchange(other);
        var family = await Family(original);
        Assert.Equal(family, await Family(current));
        Assert.NotEqual(family, await Family(other));

        var access = new JwtSecurityTokenHandler().ReadJwtToken(current["cp6_at"]);
        var refreshHash = AuthenticationInteractionStoreV1.Hash(current["cp6_rt"]);
        Assert.True(access.Claims.Single(c => c.Type == "cp6_session").Value == refreshHash);
        Assert.True(await h.Db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.access_sessions a
                JOIN cp6_authentication_v1.session_evidence s ON s.refresh_hash=a.refresh_hash
                JOIN public."Sys_RefreshTokens" r ON r."TokenHash"=s.refresh_hash
                WHERE a.access_jti={access.Id} AND s.refresh_hash={refreshHash} AND r."TenantId"={h.Tenant}
                  AND r."UserId"={h.ActorId} AND r."BrowserSessionId"={family} AND r."ClientKind"='Web') AS "Value"
            """).SingleAsync());

        var originalPermits = new[] { await SignedPermit(beforeRotation), await SignedPermit(afterRotation) };
        var otherPermit = await SignedPermit(independent);
        Assert.True(await GateCurrent(originalPermits[0]));
        Assert.True(await GateCurrent(originalPermits[1]));
        Assert.True(await GateCurrent(otherPermit));

        if (route == "completion")
        {
            using var missingCsrf = await host.Send(Request(HttpMethod.Post, "api/auth/completion/close", current, new { }, csrf: false));
            Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
        }
        if (route == "browser")
        {
            using var status = await host.Send(BrowserAction("status"));
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            Assert.Equal("COMPLETED", (await status.Content.ReadFromJsonAsync<BrowserAuthenticationStatusV1>())!.State);
        }

        var before = await ReadFamily();
        Assert.Equal(new WebFamilyReadback(3, 0, 2, 0, 0, 0, 0), before);
        using var close = await host.Send(Close());
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        if (route == "raw") Assert.Equal(4, close.Headers.GetValues("Set-Cookie").Count());
        else
        {
            Assert.Equal("CLOSED", (await close.Content.ReadFromJsonAsync<AuthenticationRecoveryStatusV1>())!.State);
            Assert.False(close.Headers.Contains("Set-Cookie"));
        }
        using var denied = await host.Profile(current["cp6_at"]);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        foreach (var old in new[] { beforeRotation, afterRotation })
        {
            using var reply = await Project(old);
            Assert.Equal(HttpStatusCode.Unauthorized, reply.StatusCode);
        }
        using var unaffected = await host.Profile(other["cp6_at"]);
        Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
        await SignedPermit(independent);
        Assert.True(await GateCurrent(otherPermit));

        var after = await ReadFamily();
        Assert.Equal(route == "raw" ? 3 : 1, after.Rejected);
        Assert.Equal(route == "completion" ? 0 : 1, after.LogoutAudits);
        var oldGrantStillCurrent = new[] { await GateCurrent(originalPermits[0]), await GateCurrent(originalPermits[1]) };
        var evidence = Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")!;
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "web-exit-facts.json"), JsonSerializer.Serialize(new
        {
            Route = route, Exit = (int)close.StatusCode, MainOriginal = (int)denied.StatusCode, MainOther = (int)unaffected.StatusCode,
            Before = before, After = after, OriginalCrmGate = oldGrantStillCurrent, OtherCrmGate = true
        }));
        Assert.All(oldGrantStillCurrent, currentGrant => Assert.False(currentGrant, "The Main-terminated original browser family still passes the native CRM identity gate."));
        Assert.Equal(new WebFamilyReadback(3, route == "raw" ? 3 : 1, 2, 2, 2, 2, route == "completion" ? 0 : 1), after);

        Task<Guid> Family(Dictionary<string, string> cookies)
        {
            var hash = AuthenticationInteractionStoreV1.Hash(cookies["cp6_rt"]);
            return h.Db.Sys_RefreshTokens.AsNoTracking().Where(x => x.TokenHash == hash && x.TenantId == h.Tenant && x.UserId == h.ActorId)
                .Select(x => x.BrowserSessionId!.Value).SingleAsync();
        }

        async Task<Dictionary<string, string>> SignIn(string key)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
            {
                Content = JsonContent.Create(new { UserName = "iam-admin", Password = "Iam1!Password", TenantCode = "iam_" + h.Tenant.ToString("N") })
            };
            request.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
            using var response = await host.Send(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return ReadCookies(response);
        }

        async Task<Dictionary<string, string>> Rotate(Dictionary<string, string> cookies, string key)
        {
            using var request = Request(HttpMethod.Post, "api/auth/refresh", cookies, new { });
            request.Headers.Add(AuthenticationRecoveryBindingV1.Header, key);
            using var response = await host.Send(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return ReadCookies(response);
        }

        static Dictionary<string, string> ReadCookies(HttpResponseMessage reply) => reply.Headers.GetValues("Set-Cookie")
            .Select(x => x.Split(';')[0].Split('=', 2)).Where(x => x[1].Length > 0).ToDictionary(x => x[0], x => x[1]);

        HttpRequestMessage BrowserAction(string action)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/completion/browser/" + action) { Content = JsonContent.Create(new { }) };
            request.Headers.Add(AuthenticationRecoveryBindingV1.Header, closeKey);
            return request;
        }

        HttpRequestMessage Close() => route switch
        {
            "raw" => Request(HttpMethod.Post, "api/auth/logout", current, new { }),
            "completion" => Request(HttpMethod.Post, "api/auth/completion/close", current, new { }),
            "browser" => BrowserAction("close"),
            _ => throw new InvalidOperationException("Unexpected owned Web exit case.")
        };

        async Task<string> Exchange(Dictionary<string, string> cookies)
        {
            var verifier = CrmOidcCrypto.RandomToken();
            var query = QueryHelpers.AddQueryString("connect/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = "CP6.Web", ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = "openid profile crm",
                ["code_challenge_method"] = "S256", ["code_challenge"] = CrmOidcCrypto.Challenge(verifier),
                ["state"] = CrmOidcCrypto.RandomToken(), ["nonce"] = CrmOidcCrypto.RandomToken()
            });
            using var entry = await host.Send(Request(HttpMethod.Get, query, cookies));
            Assert.Equal(HttpStatusCode.Redirect, entry.StatusCode);
            using var authorization = await host.Send(Request(HttpMethod.Get, entry.Headers.Location!.PathAndQuery, cookies));
            Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
            var code = QueryHelpers.ParseQuery(authorization.Headers.Location!.Query)["code"].ToString();
            Assert.NotEmpty(code);
            using var request = new HttpRequestMessage(HttpMethod.Post, "connect/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirect, ["code_verifier"] = verifier })
            };
            request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("CP6.Web:" + clientSecret)));
            using var response = await host.Send(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("access_token").GetString()!;
        }

        Task<HttpResponseMessage> Project(string bearer)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "connect/inquiry-human-context")
            {
                Content = JsonContent.Create(new InquiryHumanContextRequestV1("intake:reject", pair.SiteId, pair.FormId, ServiceNonce()))
            };
            request.Headers.Authorization = new("Bearer", bearer);
            request.Headers.Add("X-Forwarded-Proto", "https");
            return host.Send(request);
        }

        async Task<RetainedWebPermit> SignedPermit(string bearer)
        {
            using var response = await Project(bearer);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.StartsWith(store.ToString("D") + "/", response.Headers.GetValues("X-CP6-Inquiry-Tenant-Fence").Single());
            var raw = await response.Content.ReadAsStringAsync();
            Assert.True(ContextSignature(JsonNode.Parse(raw)!.AsObject(), options.PublicKeyBase64, InquiryPermissionSigningKeyV1.HumanContextVersion));
            using var body = JsonDocument.Parse(raw);
            var document = body.RootElement;
            Assert.Equal(h.Tenant, document.GetProperty("organizationId").GetGuid());
            Assert.Equal(h.ActorId, document.GetProperty("subjectId").GetGuid());
            var source = document.GetProperty("userSource");
            return new(source.GetProperty("aggregateId").GetString()!, source.GetProperty("version").GetInt32(),
                IdentityEventContracts.TokenAggregate(options.Issuer, GrantId(bearer).ToString("D")), document.GetProperty("expiresAtUtc").GetDateTimeOffset());
        }

        async Task<bool> GateCurrent(RetainedWebPermit permit)
        {
            Assert.True(permit.ExpiresAtUtc > DateTimeOffset.UtcNow);
            await using var runtime = new NpgsqlConnection(crmReaderConnection);
            await runtime.OpenAsync();
            await using var transaction = await runtime.BeginTransactionAsync();
            await using (var hold = new NpgsqlCommand("SET TRANSACTION READ ONLY; SELECT crm_identity_lock_v1.hold_v1(@tenant)", runtime, transaction))
            {
                hold.Parameters.AddWithValue("tenant", h.Tenant);
                await hold.ExecuteNonQueryAsync();
            }
            await using var read = new NpgsqlCommand("SELECT crm_inquiry_tenant_pause_v1.identity_current_v1(@tenant,@source,@version,@session)", runtime, transaction);
            read.Parameters.AddWithValue("tenant", h.Tenant);
            read.Parameters.AddWithValue("source", permit.Aggregate);
            read.Parameters.AddWithValue("version", permit.Version);
            read.Parameters.AddWithValue("session", permit.Session);
            var accepted = (bool)(await read.ExecuteScalarAsync())!;
            await transaction.CommitAsync();
            return accepted;
        }

        async Task<WebFamilyReadback> ReadFamily()
        {
            var json = await h.Db.Database.SqlQuery<string>($"""
                WITH family_rows AS (SELECT "TokenHash" FROM public."Sys_RefreshTokens"
                    WHERE "TenantId"={h.Tenant} AND "UserId"={h.ActorId} AND "BrowserSessionId"={family}),
                grants AS (SELECT g."Id",g."Revoked" FROM public."CrmOidcGrant" g JOIN family_rows r ON g."SourceRefreshHash"=r."TokenHash" COLLATE "C"
                    WHERE g."OrganizationId"={h.Tenant} AND g."SubjectId"={h.ActorId})
                SELECT json_build_object('Refresh',(SELECT count(*) FROM family_rows),
                    'Rejected',(SELECT count(*) FROM cp6_authentication_v1.session_evidence s JOIN family_rows r ON s.refresh_hash=r."TokenHash" WHERE s.rejected_at IS NOT NULL),
                    'Grants',(SELECT count(*) FROM grants), 'RevokedGrants',(SELECT count(*) FROM grants WHERE "Revoked"),
                    'Snapshots',(SELECT count(*) FROM crm_identity."Snapshot" s JOIN grants g ON split_part(s."AggregateId",':',3)=g."Id"::text
                        WHERE s."TenantId"={h.Tenant} AND s."AggregateId" LIKE 'token:%'),
                    'Outbox',(SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" s JOIN grants g ON split_part(s."AggregateId",':',3)=g."Id"::text
                        WHERE s."TenantId"={h.Tenant} AND s."AggregateId" LIKE 'token:%'),
                    'LogoutAudits',(SELECT count(*) FROM public."Sys_SecurityLogs" WHERE "TenantId"={h.Tenant} AND "UserId"={h.ActorId}
                        AND "EventType"={(int)SecurityEventType.Logout}))::text AS "Value"
                """).SingleAsync();
            return JsonSerializer.Deserialize<WebFamilyReadback>(json)!;
        }
    }

    private sealed record RetainedWebPermit(string Aggregate, int Version, string Session, DateTimeOffset ExpiresAtUtc);
    private sealed record WebFamilyReadback(int Refresh, int Rejected, int Grants, int RevokedGrants, int Snapshots, int Outbox, int LogoutAudits);
}
