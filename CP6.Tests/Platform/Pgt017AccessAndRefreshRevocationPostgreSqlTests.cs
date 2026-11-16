using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Client;
using CP6.WebApi.Seed;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [Pgt017AccessAndRefreshFact]
    public async Task PGT017_access_logout_and_delivered_refresh_reuse_retire_only_their_original_CRM_sources()
    {
        var scenario = Environment.GetEnvironmentVariable("CP6_W667_SCENARIO")!;
        var baseline = Environment.GetEnvironmentVariable("CP6_W667_BASELINE") == "1";
        var exactAccess = scenario is "web-access" or "client-web-alias";
        var reuse = scenario is "web-reuse" or "native-reuse";
        var completionControlled = scenario == "rejected-completion";
        var nativeClosed = scenario == "ordinary-close";
        await using var h = await Seed();
        var pair = await GrantIntake(h);
        var user = await h.Db.Sys_Users.SingleAsync(u => u.Id == h.ActorId);
        user.AuthenticationEpoch = Guid.NewGuid();
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var signing = InquiryPermissionSigningTests.Options();
        await new IdentityBootstrapService(h.Db, ContextRuntime(h.Tenant, signing.Issuer)).InitializeAsync(h.Tenant);
        h.Db.ChangeTracker.Clear();
        const string redirect = "https://crm.cp6.test/callback";
        var clientSecret = CrmOidcCrypto.RandomToken();
        var store = Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!);
        var seal = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION"));
        var reader = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_W667_CRM_READER"));
        await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
        await crm.OpenAsync();
        Assert.Equal(("127.0.0.1", crm.Port, crm.Database, "w667_main_seal"), (seal.Host, seal.Port, seal.Database, seal.Username));
        Assert.Equal(("127.0.0.1", crm.Port, crm.Database), (reader.Host, reader.Port, reader.Database));
        Assert.Matches("^w51_runtime_[a-f0-9]{8}$", reader.Username!);
        await InstallUserSealFixture(crm, seal, h.Tenant, store);
        using (var quoter = new NpgsqlCommandBuilder())
        {
            var runtimeRole = quoter.QuoteIdentifier(reader.Username!);
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
        environment["CrmOidc__Issuer"] = signing.Issuer;
        environment["CrmOidc__Clients__0__SecretSha256"] = CrmOidcCrypto.Hash(clientSecret);
        environment["CrmOidc__Clients__0__RedirectUris__0"] = redirect;
        environment["CrmOidc__TrustedTokenProxyAddresses__0"] = "127.0.0.1";
        environment["InquiryPermissionSigning__Enabled"] = "true";
        environment["InquiryPermissionSigning__Issuer"] = signing.Issuer;
        environment["InquiryPermissionSigning__KeyId"] = signing.KeyId;
        environment["InquiryPermissionSigning__PrivateKeyBase64"] = signing.PrivateKeyBase64;
        environment["InquiryPermissionSigning__PublicKeyBase64"] = signing.PublicKeyBase64;
        environment["InquiryPermissionSigning__ActiveFromUtc"] = signing.ActiveFromUtc.ToString("O");
        environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT");
        environment["JWT__Secret"] = CrmOidcCrypto.RandomToken() + CrmOidcCrypto.RandomToken();
        environment["Kafka__BootstrapServers"] = "";
        environment["RabbitMQ__HostName"] = "";
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, environment, allowAutoRedirect: false);

        var original = await WebLogin();
        var first = await Authorize(original);
        var sameJti = await Authorize(original);
        using var webRotation = await host.Send(Request(HttpMethod.Post, "api/auth/refresh", original, new { }));
        Assert.Equal(HttpStatusCode.OK, webRotation.StatusCode);
        var current = Cookies(webRotation);
        await RequireDelivered(original["cp6_rt"], current["cp6_rt"], "Web");
        var laterJti = await Authorize(current);
        var other = await WebLogin();
        var otherFamily = await Authorize(other);
        var authorities = new[] { first, sameJti, laterJti, otherFamily };
        Assert.NotEqual(first.GrantId, sameJti.GrantId);
        Assert.Equal(first.Jti, sameJti.Jti);
        Assert.NotEqual(first.Jti, laterJti.Jti);
        Assert.Equal(first.Family, laterJti.Family);
        Assert.NotEqual(first.Family, otherFamily.Family);
        foreach (var permit in authorities) Assert.True(await GateCurrent(permit));

        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "w667-owned-windows", AppVersion = "1.0.0" };
        using var nativeLogin = await host.Send(Native("login", new NativeLoginRequest
        {
            Client = client, UserName = "iam-admin", Password = "Iam1!Password", TenantCode = "iam_" + h.Tenant.ToString("N")
        }));
        Assert.Equal(HttpStatusCode.OK, nativeLogin.StatusCode);
        var native = (await nativeLogin.Content.ReadFromJsonAsync<NativeAuthResult>())!.Session!;
        var currentNative = native;
        if (scenario == "native-reuse")
        {
            using var rotated = await host.Send(Native("refresh", new NativeRefreshRequest { Client = client, RefreshToken = native.RefreshToken }));
            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
            currentNative = (await rotated.Content.ReadFromJsonAsync<TokenSessionDto>())!;
            await RequireDelivered(native.RefreshToken, currentNative.RefreshToken, "Windows");
        }

        var nativeJti = AccessJti(currentNative.AccessToken);
        Assert.False(await h.Db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM public."CrmOidcGrant" WHERE "SourceJti"=CAST({nativeJti} AS bpchar)) AS "Value"
            """).SingleAsync());

        var forbidSeal = baseline || !exactAccess && !reuse;
        if (forbidSeal)
        {
            await using var disable = new NpgsqlCommand("ALTER ROLE w667_main_seal NOLOGIN", crm);
            await disable.ExecuteNonQueryAsync();
            await using var witness = new NpgsqlConnection(seal.ConnectionString);
            var refused = await Assert.ThrowsAsync<PostgresException>(() => witness.OpenAsync());
            Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, refused.SqlState);
        }
        var before = await ReadState();
        Assert.Equal(new SourceRetirementState(scenario == "native-reuse" ? 5 : 4, 3, 0, 0, 4, 0, 0, 0), before);
        using var beforeAlias = await host.Profile(original["cp6_at"]);
        Assert.Equal(HttpStatusCode.OK, beforeAlias.StatusCode);

        using var result = scenario switch
        {
            "web-access" => await host.Send(Request(HttpMethod.Post, "api/auth/logout", NoWebRefresh(), new { })),
            "client-web-alias" => await host.Send(Native("logout", new NativeLogoutRequest { Client = client }, original["cp6_at"])),
            "native-access" => await host.Send(Native("logout", new NativeLogoutRequest { Client = client }, native.AccessToken)),
            "unknown-cookie" => await host.Send(Request(HttpMethod.Post, "api/auth/logout",
                new Dictionary<string, string>(original) { ["cp6_rt"] = CrmOidcCrypto.RandomToken() }, new { })),
            "web-reuse" => await host.Send(Request(HttpMethod.Post, "api/auth/refresh", original, new { })),
            "native-reuse" => await host.Send(Native("refresh", new NativeRefreshRequest { Client = client, RefreshToken = native.RefreshToken })),
            "wrong-kind" => await host.Send(Native("refresh", new NativeRefreshRequest { Client = client, RefreshToken = original["cp6_rt"] })),
            "simultaneous-loser" => await SimultaneousLoser(),
            "rejected-completion" => await RejectedCompletion(),
            "ordinary-close" => await OrdinaryClose(),
            _ => throw new InvalidOperationException("Expected an owned access/refresh scenario.")
        };
        var expectedError = scenario switch
        {
            "web-reuse" or "native-reuse" => "E-SEC-008",
            "wrong-kind" => "E-SEC-024",
            "simultaneous-loser" or "rejected-completion" => "E-SEC-007",
            "ordinary-close" => "REAUTH_REQUIRED",
            _ => null
        };
        var expectedStatus = expectedError switch
        {
            "REAUTH_REQUIRED" => HttpStatusCode.Unauthorized,
            null => HttpStatusCode.OK,
            _ => HttpStatusCode.BadRequest
        };
        Assert.Equal(expectedStatus, result.StatusCode);
        var errorMessage = expectedError is null ? null : await RequireNativeError(result, expectedError);
        var cleared = result.Headers.TryGetValues("Set-Cookie", out var headers) ? headers.Count() : 0;
        Assert.Equal(scenario is "web-access" or "unknown-cookie" ? 4 : 0, cleared);

        var oldAccessDenied = reuse || exactAccess || scenario == "unknown-cookie";
        var currentNativeDenied = reuse || nativeClosed || scenario == "native-access";
        await RequireProfile(original["cp6_at"], !oldAccessDenied);
        await RequireProfile(current["cp6_at"], !reuse);
        await RequireProfile(other["cp6_at"], !reuse);
        await RequireProfile(currentNative.AccessToken, !currentNativeDenied);
        var after = await ReadState();
        Assert.Equal(reuse ? 0 : nativeClosed || completionControlled ? before.ActiveRefresh - 1 : before.ActiveRefresh, after.ActiveRefresh);
        Assert.Equal(scenario == "simultaneous-loser" || completionControlled ? before.RefreshRows + 1 : before.RefreshRows, after.RefreshRows);
        Assert.Equal(exactAccess || scenario == "native-access" ? 1 : 0, after.RejectedAliases);
        Assert.Equal(reuse ? before.RefreshRows : nativeClosed || completionControlled ? 1 : 0, after.RejectedGenerations);
        Assert.Equal(before.CrmGrants, after.CrmGrants);
        var nativeGates = new bool[authorities.Length];
        for (var i = 0; i < authorities.Length; i++) nativeGates[i] = await GateCurrent(authorities[i]);

        if (baseline) Assert.Equal(before with { RefreshRows = after.RefreshRows, ActiveRefresh = after.ActiveRefresh,
            RejectedAliases = after.RejectedAliases, RejectedGenerations = after.RejectedGenerations }, after);
        var proofDirectory = Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")!;
        Directory.CreateDirectory(proofDirectory);
        await File.WriteAllTextAsync(Path.Combine(proofDirectory, "source-retirement.json"), JsonSerializer.Serialize(new
        {
            Scenario = scenario, Baseline = baseline, Status = (int)result.StatusCode, Error = expectedError, ErrorMessage = errorMessage, Cleared = cleared,
            Before = before, After = after, NativeGates = nativeGates,
            RefusedSealWitness = forbidSeal, Scope = new { SameJti = 2, SameFamilyLaterJti = 1, OtherFamily = 1, NativeGrant = false }
        }));

        var affected = reuse ? 4 : exactAccess ? 2 : 0;
        for (var i = 0; i < nativeGates.Length; i++)
            Assert.True(nativeGates[i] == (i >= affected), $"Original CRM intake permission at source slot {i} has the wrong native disposition.");
        Assert.Equal((affected, affected, affected), (after.CrmRevoked, after.Snapshots, after.Outbox));

        static string AccessJti(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token).Id;

        static async Task<string> RequireNativeError(HttpResponseMessage response, string key)
        {
            var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal((int)response.StatusCode, envelope.GetProperty("code").GetInt32());
            var message = envelope.GetProperty("message").GetString()!;
            var translation = I18nSecScreenSeed.Items.Concat(I18nSecSsoScreenSeed.Items).SingleOrDefault(item => item.LangKey == key);
            var contract = translation is null ? new[] { key } : [key, translation.ZhCN, translation.ZhTW, translation.En, translation.Ja, translation.Ko];
            Assert.Contains(message, contract);
            return message;
        }

        static Dictionary<string, string> Cookies(HttpResponseMessage reply) => reply.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';')[0].Split('=', 2)).Where(pair => pair[1].Length > 0)
            .ToDictionary(pair => pair[0], pair => pair[1]);

        Dictionary<string, string> NoWebRefresh() => new() { ["cp6_at"] = original["cp6_at"], ["cp6_csrf"] = original["cp6_csrf"] };

        static HttpRequestMessage Native(string action, object body, string? bearer = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "api/client-auth/" + action) { Content = JsonContent.Create(body) };
            if (bearer is not null) request.Headers.Authorization = new("Bearer", bearer);
            return request;
        }

        async Task<Dictionary<string, string>> WebLogin()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
            {
                Content = JsonContent.Create(new { UserName = "iam-admin", Password = "Iam1!Password", TenantCode = "iam_" + h.Tenant.ToString("N") })
            };
            using var response = await host.Send(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return Cookies(response);
        }

        async Task<SourceRetirementPermit> Authorize(Dictionary<string, string> cookies)
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
            using var swap = new HttpRequestMessage(HttpMethod.Post, "connect/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirect, ["code_verifier"] = verifier })
            };
            swap.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("CP6.Web:" + clientSecret)));
            using var issued = await host.Send(swap);
            Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
            using var body = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
            var token = body.RootElement.GetProperty("access_token").GetString()!;
            var grantId = GrantId(token);
            var sourceJti = AccessJti(cookies["cp6_at"]);
            Assert.False(grantId.ToString("D") == sourceJti, "CRM grant identity and the source JWT's JTI are distinct.");
            var sourceHash = AuthenticationInteractionStoreV1.Hash(cookies["cp6_rt"]);
            var family = await h.Db.Database.SqlQuery<Guid>($"""
                SELECT r."BrowserSessionId" AS "Value" FROM public."CrmOidcGrant" g
                JOIN public."Sys_RefreshTokens" r ON g."SourceRefreshHash"=r."TokenHash" COLLATE "C"
                JOIN cp6_authentication_v1.access_sessions a ON a.refresh_hash=r."TokenHash"
                WHERE g."Id"={grantId} AND g."OrganizationId"={h.Tenant} AND g."SubjectId"={h.ActorId}
                  AND g."SourceJti"=CAST({sourceJti} AS bpchar) AND a.access_jti={sourceJti} AND r."TokenHash"={sourceHash}
                  AND r."TenantId"={h.Tenant} AND r."UserId"={h.ActorId}
                """).SingleAsync();
            using var context = new HttpRequestMessage(HttpMethod.Post, "connect/inquiry-human-context")
            {
                Content = JsonContent.Create(new InquiryHumanContextRequestV1("intake:reject", pair.SiteId, pair.FormId, ServiceNonce()))
            };
            context.Headers.Authorization = new("Bearer", token);
            context.Headers.Add("X-Forwarded-Proto", "https");
            using var signed = await host.Send(context);
            Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
            Assert.StartsWith(store.ToString("D") + "/", signed.Headers.GetValues("X-CP6-Inquiry-Tenant-Fence").Single());
            var json = JsonNode.Parse(await signed.Content.ReadAsStringAsync())!.AsObject();
            Assert.True(ContextSignature(json, signing.PublicKeyBase64, InquiryPermissionSigningKeyV1.HumanContextVersion));
            var userSource = json["userSource"]!;
            Assert.Equal(h.Tenant, json["organizationId"]!.GetValue<Guid>());
            Assert.Equal(h.ActorId, json["subjectId"]!.GetValue<Guid>());
            return new(grantId, sourceJti, family, userSource["aggregateId"]!.GetValue<string>(), userSource["version"]!.GetValue<int>(),
                IdentityEventContracts.TokenAggregate(signing.Issuer, grantId.ToString("D")), json["expiresAtUtc"]!.GetValue<DateTimeOffset>());
        }

        async Task RequireDelivered(string parent, string replacement, string clientKind)
        {
            var parentHash = AuthenticationInteractionStoreV1.Hash(parent);
            var nextHash = AuthenticationInteractionStoreV1.Hash(replacement);
            Assert.True(await h.Db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT 1 FROM public."Sys_RefreshTokens" p
                    JOIN public."Sys_RefreshTokens" r ON r."TokenHash"=p."ReplacedByTokenHash"
                    JOIN cp6_authentication_v1.session_evidence s ON s.refresh_hash=r."TokenHash"
                    WHERE p."TokenHash"={parentHash} AND r."TokenHash"={nextHash} AND s.parent_refresh_hash=p."TokenHash"
                      AND p."TenantId"={h.Tenant} AND p."UserId"={h.ActorId} AND r."ClientKind"={clientKind}
                      AND p."RevokedAt" IS NOT NULL AND r."RevokedAt" IS NULL AND NOT r."CompletionPending"
                      AND r."CompletionRejectedAtUtc" IS NULL AND s.rejected_at IS NULL) AS "Value"
                """).SingleAsync());
        }

        async Task RequireProfile(string token, bool allowed)
        {
            using var currentProfile = await host.Profile(token);
            Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, currentProfile.StatusCode);
        }

        async Task<bool> GateCurrent(SourceRetirementPermit permit)
        {
            Assert.True(permit.ExpiresAt > DateTimeOffset.UtcNow);
            await using var witness = new NpgsqlConnection(reader.ConnectionString);
            await witness.OpenAsync();
            await using var transaction = await witness.BeginTransactionAsync();
            await using (var hold = new NpgsqlCommand("SET TRANSACTION READ ONLY; SELECT crm_identity_lock_v1.hold_v1(@tenant)", witness, transaction))
            {
                hold.Parameters.AddWithValue("tenant", h.Tenant);
                await hold.ExecuteNonQueryAsync();
            }
            await using var read = new NpgsqlCommand("SELECT crm_inquiry_tenant_pause_v1.identity_current_v1(@tenant,@source,@version,@session)", witness, transaction);
            read.Parameters.AddWithValue("tenant", h.Tenant);
            read.Parameters.AddWithValue("source", permit.UserAggregate);
            read.Parameters.AddWithValue("version", permit.UserVersion);
            read.Parameters.AddWithValue("session", permit.SessionAggregate);
            var active = (bool)(await read.ExecuteScalarAsync())!;
            await transaction.CommitAsync();
            return active;
        }

        async Task<HttpResponseMessage> RejectedCompletion()
        {
            var service = new RefreshTokenService(h.Db, Options.Create(new SecurityOptions()), new TenantContext { CurrentTenantId = h.Tenant });
            var (replacement, _) = await service.RotateForCompletionAsync(native.RefreshToken, null, null, new(client.ClientKind, client.DeviceId, client.AppVersion));
            var replacementHash = AuthenticationInteractionStoreV1.Hash(replacement);
            Assert.True(await h.Db.Sys_RefreshTokens.AsNoTracking().Where(r => r.TokenHash == replacementHash)
                .Select(r => r.CompletionPending).SingleAsync());
            var refresh = new NativeRefreshRequest { Client = client, RefreshToken = native.RefreshToken };
            using (var pending = await host.Send(Native("refresh", refresh)))
            {
                Assert.Equal(HttpStatusCode.BadRequest, pending.StatusCode);
                await RequireNativeError(pending, "E-SEC-007");
            }
            await service.RejectCompletionAsync(replacement);
            return await host.Send(Native("refresh", refresh));
        }

        async Task<HttpResponseMessage> OrdinaryClose()
        {
            using var closed = await host.Send(Native("logout", new NativeLogoutRequest { Client = client, RefreshToken = native.RefreshToken }, native.AccessToken));
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            return await host.Send(Native("refresh", new NativeRefreshRequest { Client = client, RefreshToken = native.RefreshToken }));
        }

        async Task<HttpResponseMessage> SimultaneousLoser()
        {
            await using var main = new NpgsqlConnection(h.Db.Database.GetConnectionString());
            await main.OpenAsync();
            await using var lockRoot = await main.BeginTransactionAsync();
            await using (var held = new NpgsqlCommand("SELECT cp6_authentication_v1.acquire()", main, lockRoot))
                await held.ExecuteNonQueryAsync();
            using var left = Request(HttpMethod.Post, "api/auth/refresh", current, new { });
            using var right = Request(HttpMethod.Post, "api/auth/refresh", current, new { });
            var two = new[] { host.Send(left), host.Send(right) };
            try
            {
                await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
                await observer.OpenAsync();
                await using var waiting = new NpgsqlCommand("""
                    SELECT count(*)::int FROM pg_stat_activity
                    WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%cp6_authentication_v1.acquire()%'
                      AND cardinality(pg_blocking_pids(pid)) > 0
                    """, observer);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                while ((int)(await waiting.ExecuteScalarAsync(deadline.Token))! < 2) await Task.Delay(25, deadline.Token);
            }
            finally { await lockRoot.RollbackAsync(); }
            var replies = await Task.WhenAll(two);
            using var winner = Assert.Single(replies, r => r.StatusCode == HttpStatusCode.OK);
            var failed = Assert.Single(replies, r => r.StatusCode != HttpStatusCode.OK);
            await RequireDelivered(current["cp6_rt"], Cookies(winner)["cp6_rt"], "Web");
            return failed;
        }

        async Task<SourceRetirementState> ReadState()
        {
            var value = await h.Db.Database.SqlQuery<string>($"""
                WITH refresh AS (SELECT "TokenHash","RevokedAt" FROM public."Sys_RefreshTokens"
                    WHERE "TenantId"={h.Tenant} AND "UserId"={h.ActorId}),
                grants AS (SELECT "Id","Revoked" FROM public."CrmOidcGrant" WHERE "OrganizationId"={h.Tenant} AND "SubjectId"={h.ActorId})
                SELECT json_build_object('RefreshRows',(SELECT count(*) FROM refresh),
                    'ActiveRefresh',(SELECT count(*) FROM refresh WHERE "RevokedAt" IS NULL),
                    'RejectedAliases',(SELECT count(*) FROM cp6_authentication_v1.access_sessions a JOIN refresh r ON a.refresh_hash=r."TokenHash" WHERE a.revoked_at IS NOT NULL),
                    'RejectedGenerations',(SELECT count(*) FROM cp6_authentication_v1.session_evidence s JOIN refresh r ON s.refresh_hash=r."TokenHash" WHERE s.rejected_at IS NOT NULL),
                    'CrmGrants',(SELECT count(*) FROM grants),'CrmRevoked',(SELECT count(*) FROM grants WHERE "Revoked"),
                    'Snapshots',(SELECT count(*) FROM crm_identity."Snapshot" s JOIN grants g ON split_part(s."AggregateId",':',3)=g."Id"::text
                        WHERE s."TenantId"={h.Tenant} AND s."AggregateId" LIKE 'token:%'),
                    'Outbox',(SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" s JOIN grants g ON split_part(s."AggregateId",':',3)=g."Id"::text
                        WHERE s."TenantId"={h.Tenant} AND s."AggregateId" LIKE 'token:%'))::text AS "Value"
                """).SingleAsync();
            return JsonSerializer.Deserialize<SourceRetirementState>(value)!;
        }
    }

    private sealed record SourceRetirementPermit(Guid GrantId, string Jti, Guid Family, string UserAggregate, int UserVersion,
        string SessionAggregate, DateTimeOffset ExpiresAt);
    private sealed record SourceRetirementState(int RefreshRows, int ActiveRefresh, int RejectedAliases, int RejectedGenerations,
        int CrmGrants, int CrmRevoked, int Snapshots, int Outbox);
}

internal sealed class Pgt017AccessAndRefreshFactAttribute : FactAttribute
{
    public Pgt017AccessAndRefreshFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE") is null || Environment.GetEnvironmentVariable("CP6_W667_SCENARIO") is null)
            Skip = "Requires the owned Main and CRM PostgreSQL source-retirement verification.";
    }
}
