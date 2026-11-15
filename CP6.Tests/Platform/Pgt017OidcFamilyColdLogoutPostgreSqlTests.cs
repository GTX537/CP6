using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Tests.Erp;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [Pgt017MainFact]
    public async Task PGT017_OIDC_family_cold_seal_refusal_stops_only_original_CRM_admission()
    {
        await using var h = await Seed();
        var user = await h.Db.Sys_Users.SingleAsync(u => u.Id == h.ActorId);
        user.AuthenticationEpoch = Guid.NewGuid();
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var pair = (await GrantInquiry(h))[0];
        var options = InquiryPermissionSigningTests.Options();
        var clientSecret = CrmOidcCrypto.RandomToken();
        const string redirect = "https://crm.cp6.test/callback";
        const string signOut = "https://crm.cp6.test/crm/logged-out";
        const string publicOrigin = "https://crm.cp6.test";
        var recovery = Environment.GetEnvironmentVariable("CP6_W661_RECOVERY");
        var store = Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!);
        var seal = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION"));
        var environment = IdentityEnvironment(h.Tenant);
        var binding = $"CrmInquiryTenantPause__Tenants__{h.Tenant:D}__";
        environment[binding + "StoreId"] = store.ToString("D");
        environment["CrmOidc__Issuer"] = options.Issuer;
        environment["CrmOidc__Clients__0__SecretSha256"] = CrmOidcCrypto.Hash(clientSecret);
        environment["CrmOidc__Clients__0__RedirectUris__0"] = redirect;
        environment["CrmOidc__Clients__0__PostLogoutRedirectUris__0"] = signOut;
        environment["CrmOidc__TrustedTokenProxyAddresses__0"] = "127.0.0.1";
        environment["InquiryPermissionSigning__Enabled"] = "true";
        environment["InquiryPermissionSigning__Issuer"] = options.Issuer;
        environment["InquiryPermissionSigning__KeyId"] = options.KeyId;
        environment["InquiryPermissionSigning__PrivateKeyBase64"] = options.PrivateKeyBase64;
        environment["InquiryPermissionSigning__PublicKeyBase64"] = options.PublicKeyBase64;
        environment["InquiryPermissionSigning__ActiveFromUtc"] = options.ActiveFromUtc.ToString("O");
        environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT");
        environment["JWT__Secret"] = CrmOidcCrypto.RandomToken() + CrmOidcCrypto.RandomToken();
        environment["Kafka__BootstrapServers"] = "";
        environment["RabbitMQ__HostName"] = "";
        environment["CrmIdentity__DaprHttpEndpoint"] = "http://127.0.0.1:57278";
        environment["CrmIdentity__DaprGrpcEndpoint"] = "http://127.0.0.1:57279";

        var crmConnection = Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER");
        await using var crm = new NpgsqlConnection(crmConnection);
        await crm.OpenAsync();
        Assert.Equal(("127.0.0.1", crm.Port, crm.Database, "w661_main_seal"), (seal.Host, seal.Port, seal.Database, seal.Username));
        Assert.Matches("^[a-f0-9]{64}$", seal.Password!);
        await using (var prepare = new NpgsqlCommand($"CREATE ROLE w661_main_seal NOLOGIN PASSWORD '{seal.Password}'", crm))
            await prepare.ExecuteNonQueryAsync();

        var main = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString());
        await using var mainRelay = recovery == "ack" ? new StockPostgresCommitRelay(main.Port, main.Username!,
            int.Parse(Environment.GetEnvironmentVariable("CP6_W661_MAIN_ACK_PORT")!)) : null;
        await using var crmRelay = recovery == "ack" ? new StockPostgresCommitRelay(seal.Port, seal.Username!,
            int.Parse(Environment.GetEnvironmentVariable("CP6_W661_CRM_ACK_PORT")!)) : null;
        if (mainRelay is not null)
        {
            main.Port = mainRelay.Port;
            main.SslMode = SslMode.Disable;
        }
        environment[binding + "ConnectionString"] = crmRelay is null ? seal.ConnectionString
            : new NpgsqlConnectionStringBuilder(seal.ConnectionString) { Port = crmRelay.Port }.ConnectionString;
        await using var host = await StartMain();
        var original = await Login(host, h);
        var unaffected = await Login(host, h);
        var first = await Redeem(host, await Authorize(host, original));
        var second = await Redeem(host, await Authorize(host, original));
        var oneUnused = await Authorize(host, original);
        var unusedGrantId = await h.Db.Database.SqlQuery<Guid>($"""
            SELECT "Id" AS "Value" FROM public."CrmOidcGrant" WHERE "CodeHash"=CAST({CrmOidcCrypto.Hash(oneUnused.Code)} AS bpchar)
            """).SingleAsync();
        var other = await Redeem(host, await Authorize(host, unaffected));
        var ownerHash = AuthenticationInteractionStoreV1.Hash(original["cp6_rt"]);
        var family = await h.Db.Sys_RefreshTokens.AsNoTracking().Where(x => x.TokenHash == ownerHash)
            .Select(x => x.BrowserSessionId).SingleAsync();
        Assert.NotNull(family);
        var snapshot = await ReadFamily();
        Assert.Equal(new FamilyReadback(false, 0, 3, 1, 0, 0, 0), snapshot);
        await RequireFence(host, first.Access, "configuration:write", expected: true);
        await RequireFence(host, second.Access, "configuration:write", expected: true);
        await RequireFence(host, other.Access, "configuration:write", expected: true);

        await using (var denied = new NpgsqlConnection(seal.ConnectionString))
        {
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => denied.OpenAsync());
            Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, refusal.SqlState);
        }

        using (var signer = new CrmOidcCrypto(new CrmOidcOptions
        {
            Enabled = true, Issuer = options.Issuer, ActiveKeyId = environment["CrmOidc__ActiveKeyId"],
            Keys = [new() { Kid = environment["CrmOidc__Keys__0__Kid"], Pem = environment["CrmOidc__Keys__0__Pem"] }]
        }))
        {
            var originalClaims = new JwtSecurityTokenHandler().ReadJwtToken(first.Identity).Claims
                .Where(x => x.Type is "sub" or "sid" or "tenant_id");
            var wrongNonce = signer.Sign("CP6.Web", originalClaims.Append(new Claim("nonce", CrmOidcCrypto.RandomToken())),
                DateTimeOffset.UtcNow.AddMinutes(2), "JWT");
            using var mismatch = await RequestLogout(host, wrongNonce);
            Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
            Assert.Equal(0, await PendingCount());
        }

        await LogoutViaCrm(host, first);
        Assert.Equal(1, await PendingCount());
        var originalIntent = await IntentHash();
        var revision = await CrmRevision();
        Assert.Equal(snapshot, await ReadFamily());
        Assert.Equal(0, await TicketCount());
        using (var response = await RequestLogout(host, first.Identity))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("temporarily_unavailable", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        await RequireFence(host, other.Access, "configuration:write", expected: true);
        await RequireFence(host, first.Access, "configuration:read", expected: false);
        await RequireFence(host, first.Access, "configuration:write", expected: false);
        await RequireFence(host, second.Access, "configuration:write", expected: false);
        await Authorize(host, original, allow: false);

        var originalPid = host.ProcessId;
        await host.StopAsync();
        if (recovery == "worker") environment["Startup__SkipHostedServices"] = "false";
        await using var restarted = await StartMain();
        Assert.NotEqual(originalPid, restarted.ProcessId);
        Assert.Equal(1, await PendingCount());
        Assert.Equal(originalIntent, await IntentHash());
        Assert.Equal(snapshot, await ReadFamily());
        await RequireFence(restarted, first.Access, "configuration:write", expected: false);
        var otherFresh = await Redeem(restarted, await Authorize(restarted, unaffected));
        await RequireFence(restarted, otherFresh.Access, "configuration:write", expected: true);
        using (var wrongOriginal = await RequestLogout(restarted, second.Identity, "different-original"))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, wrongOriginal.StatusCode);
        Assert.Equal(1, await PendingCount());

        await using (var permit = new NpgsqlCommand("ALTER ROLE w661_main_seal LOGIN", crm))
            await permit.ExecuteNonQueryAsync();

        if (crmRelay is not null && mainRelay is not null)
        {
            crmRelay.Arm();
            using (var unconfirmed = await RequestLogout(restarted, first.Identity))
            {
                await crmRelay.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
                Assert.Equal(HttpStatusCode.ServiceUnavailable, unconfirmed.StatusCode);
            }
            Assert.Equal(1, await PendingCount());
            Assert.Equal(originalIntent, await IntentHash());
            Assert.Equal(snapshot, await ReadFamily());
            Assert.Equal(0, await TicketCount());
            Assert.Equal(3, await NativeSeals());
            Assert.Equal(revision + 3, await CrmRevision());
            await RequireFence(restarted, second.Access, "configuration:write", expected: false);

            await using var barrier = new NpgsqlConnection(crmConnection);
            await barrier.OpenAsync();
            await using var tx = await barrier.BeginTransactionAsync();
            await using (var hold = new NpgsqlCommand("SELECT crm_identity_lock_v1.hold_v1(@tenant)", barrier, tx))
            {
                hold.Parameters.AddWithValue("tenant", h.Tenant);
                await hold.ExecuteNonQueryAsync();
            }
            var pendingMain = RequestLogout(restarted, first.Identity);
            try
            {
                await WaitForMainBlock(crm, barrier.ProcessID, "%advance_identity_v1%", pendingMain);
                mainRelay.Arm();
            }
            finally { await tx.RollbackAsync(); }
            using var outcome = await pendingMain;
            await mainRelay.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, outcome.StatusCode);
            Assert.Equal(0, await PendingCount());
            Assert.Equal(0, await TicketCount());
            Assert.Equal(new FamilyReadback(true, 1, 3, 1, 3, 3, 3), await ReadFamily());
            Assert.Equal(revision + 6, await CrmRevision());
        }
        else if (recovery == "worker")
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            while (await PendingCount() != 0) await Task.Delay(100, deadline.Token);
            Assert.Equal(new FamilyReadback(true, 1, 3, 1, 3, 3, 3), await ReadFamily());
            Assert.Equal(0, await TicketCount());
            Assert.Equal(revision + 3, await CrmRevision());
        }

        using var completed = await RequestLogout(restarted, first.Identity);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(0, await PendingCount());
        var committed = await ReadFamily();
        Assert.Equal(new FamilyReadback(true, 1, 3, 1, 3, 3, 3), committed);
        Assert.Equal(3, await NativeSeals());
        using (var refusedCode = await Exchange(restarted, oneUnused))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refusedCode.StatusCode);
            Assert.Contains("invalid_grant", await refusedCode.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        using var again = await RequestLogout(restarted, first.Identity);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var continuation = await Continuation(completed);
        Assert.True(continuation == await Continuation(again), "A live continuation must recover its exact original response.");
        Assert.Equal(1, await TicketCount());
        var ticket = QueryHelpers.ParseQuery(new Uri(continuation).Query)["ticket"].ToString();
        Assert.InRange(await h.Db.Database.SqlQuery<double>($"""
            SELECT extract(epoch FROM ("ExpiresAtUtc"-clock_timestamp()))::float8 AS "Value"
            FROM public."CrmOidcLogout" WHERE "TicketHash"=CAST({CrmOidcCrypto.Hash(ticket)} AS bpchar)
            """).SingleAsync(), 0, 60);
        using (var foreign = await restarted.Send(Request(HttpMethod.Get, "api/auth/crm-logout-complete?ticket=" + ticket, unaffected)))
        {
            Assert.Equal(HttpStatusCode.Redirect, foreign.StatusCode);
            Assert.Equal(signOut, foreign.Headers.Location!.ToString());
            Assert.False(foreign.Headers.Contains("Set-Cookie"));
        }
        using (var used = await restarted.Send(Request(HttpMethod.Get, "api/auth/crm-logout-complete?ticket=" + ticket, original)))
            Assert.Equal(HttpStatusCode.BadRequest, used.StatusCode);
        Assert.Equal(0, await TicketCount());
        using (var resume = await RequestLogout(restarted, first.Identity))
        {
            Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
            var nextTicket = QueryHelpers.ParseQuery(new Uri(await Continuation(resume)).Query)["ticket"].ToString();
            Assert.True(nextTicket != ticket, "A fresh browser action must never reuse a consumed ticket.");
            using var matching = await restarted.Send(Request(HttpMethod.Get, "api/auth/crm-logout-complete?ticket=" + nextTicket, original));
            Assert.Equal(HttpStatusCode.Redirect, matching.StatusCode);
            Assert.Equal(signOut, matching.Headers.Location!.ToString());
            Assert.Contains(matching.Headers.GetValues("Set-Cookie"), value => value.StartsWith("cp6_rt=;", StringComparison.Ordinal));
        }
        Assert.Equal(committed, await ReadFamily());
        Assert.Equal(3, await NativeSeals());
        Assert.Equal(revision + (recovery == "ack" ? 6 : 3), await CrmRevision());
        await RequireFence(restarted, otherFresh.Access, "configuration:write", expected: true);

        Task<AuthenticationApiProcess> StartMain() => AuthenticationApiProcess.Start(main.ConnectionString, environment, allowAutoRedirect: false);

        Task<HttpResponseMessage> RequestLogout(AuthenticationApiProcess api, string hint, string state = "")
            => api.Send(ClientForm("connect/end-session", new Dictionary<string,string>
            {
                ["id_token_hint"] = hint, ["post_logout_redirect_uri"] = signOut, ["state"] = state
            }));

        Task<int> PendingCount() => h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_authentication_v1.crm_oidc_family_logouts_v1
            WHERE tenant_id={h.Tenant} AND family_id={family} AND completed_at_utc IS NULL
            """).SingleAsync();

        Task<string> IntentHash() => h.Db.Database.SqlQuery<string>($"""
            SELECT request_hash || ':' || grant_plan::text AS "Value" FROM cp6_authentication_v1.crm_oidc_family_logouts_v1
            WHERE tenant_id={h.Tenant} AND family_id={family}
            """).SingleAsync();

        async Task<long> CrmRevision()
        {
            await using var command = new NpgsqlCommand("SELECT \"Revision\" FROM crm_auth.\"IdentityFenceV1\" WHERE \"TenantId\"=@tenant", crm);
            command.Parameters.AddWithValue("tenant", h.Tenant);
            return (long)(await command.ExecuteScalarAsync())!;
        }

        Task<int> TicketCount() => h.Db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM public.\"CrmOidcLogout\"").SingleAsync();

        async Task<int> NativeSeals()
        {
            var reader = new JwtSecurityTokenHandler();
            var expected = new[] { reader.ReadJwtToken(first.Access).Id, reader.ReadJwtToken(second.Access).Id, unusedGrantId.ToString("D") }
                .Select(jti => IdentityEventContracts.TokenAggregate(options.Issuer, jti)).ToArray();
            await using var command = new NpgsqlCommand("""
                SELECT count(*)::int FROM crm_inquiry_tenant_pause_v1.identity_fences
                WHERE tenant_id=@tenant AND aggregate_id=ANY(@expected) AND minimum_version=1
                """, crm);
            command.Parameters.AddWithValue("tenant", h.Tenant);
            command.Parameters.AddWithValue("expected", expected);
            return (int)(await command.ExecuteScalarAsync())!;
        }

        static async Task<string> Continuation(HttpResponseMessage response)
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("logoutContinuationUri").GetString()!;
        }

        async Task<(string Code, string Verifier)> Authorize(AuthenticationApiProcess api, Dictionary<string,string> cookies, bool allow = true)
        {
            var verifier = CrmOidcCrypto.RandomToken();
            var query = QueryHelpers.AddQueryString("connect/authorize", new Dictionary<string,string?>
            {
                ["client_id"] = "CP6.Web", ["response_type"] = "code", ["redirect_uri"] = redirect,
                ["code_challenge_method"] = "S256", ["code_challenge"] = CrmOidcCrypto.Challenge(verifier),
                ["state"] = CrmOidcCrypto.RandomToken(), ["nonce"] = CrmOidcCrypto.RandomToken(), ["scope"] = "openid profile crm"
            });
            using var initial = await api.Send(Request(HttpMethod.Get, query, cookies));
            Assert.Equal(HttpStatusCode.Redirect, initial.StatusCode);
            using var result = await api.Send(Request(HttpMethod.Get, initial.Headers.Location!.PathAndQuery, cookies));
            Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
            var values = QueryHelpers.ParseQuery(result.Headers.Location!.Query);
            if (!allow)
            {
                Assert.Equal("login_required", values["error"].ToString());
                Assert.False(values.ContainsKey("code"));
                return default;
            }
            var code = values["code"].ToString();
            Assert.NotEmpty(code);
            return (code, verifier);
        }

        async Task<(string Access, string Identity)> Redeem(AuthenticationApiProcess api, (string Code, string Verifier) code)
        {
            using var result = await Exchange(api, code);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            using var body = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
            return (body.RootElement.GetProperty("access_token").GetString()!, body.RootElement.GetProperty("id_token").GetString()!);
        }

        Task<HttpResponseMessage> Exchange(AuthenticationApiProcess api, (string Code, string Verifier) code)
            => api.Send(ClientForm("connect/token", new Dictionary<string,string>
            {
                ["grant_type"] = "authorization_code", ["code"] = code.Code,
                ["redirect_uri"] = redirect, ["code_verifier"] = code.Verifier
            }));

        HttpRequestMessage ClientForm(string path, Dictionary<string,string> fields)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(fields) };
            request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("CP6.Web:" + clientSecret)));
            return request;
        }

        async Task RequireFence(AuthenticationApiProcess api, string bearer, string purpose, bool expected)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "connect/inquiry-permission")
                { Content = JsonContent.Create(new InquiryPermissionRequestV1(purpose, pair.SiteId, pair.FormId)) };
            request.Headers.Authorization = new("Bearer", bearer);
            request.Headers.Add("X-Forwarded-Proto", "https");
            using var result = await api.Send(request);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.True(result.Headers.Contains("X-CP6-Inquiry-Tenant-Fence") == expected,
                "CRM seal login was refused before any grant could commit. Pending logout must suppress fresh writes only for its own browser family.");
        }

        async Task LogoutViaCrm(AuthenticationApiProcess main, (string Access, string Identity) tokens)
        {
            var work = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!, "actual-crm-bff");
            Directory.CreateDirectory(work);
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")!)
            {
                WorkingDirectory = work, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            var bffHost = Environment.GetEnvironmentVariable("CP6_W661_CRM_BFF_HOST_DLL")!;
            var frozenRuntime = Path.GetDirectoryName(bffHost)!;
            start.ArgumentList.Add("exec");
            start.ArgumentList.Add("--depsfile");
            start.ArgumentList.Add(Path.Combine(frozenRuntime, "CP6.CRM.Api.deps.json"));
            start.ArgumentList.Add("--runtimeconfig");
            start.ArgumentList.Add(Path.Combine(frozenRuntime, "CP6.CRM.Api.runtimeconfig.json"));
            start.ArgumentList.Add(bffHost);
            var bffPort = int.Parse(Environment.GetEnvironmentVariable("CP6_W661_BFF_PORT")!);
            var csrf = CrmOidcCrypto.RandomToken();
            var cookie = CrmOidcCrypto.RandomToken();
            var hmac = RandomNumberGenerator.GetBytes(32);
            var token = new JwtSecurityTokenHandler().ReadJwtToken(tokens.Access);
            var authEpoch = await h.Db.Sys_Users.AsNoTracking().Where(u => u.Id == h.ActorId).Select(u => u.AuthenticationEpoch).SingleAsync();
            foreach (var pair in new Dictionary<string,string>
            {
                ["ISSUER"] = options.Issuer, ["MAIN_ORIGIN"] = main.Origin.ToString(), ["BFF_PUBLIC_ORIGIN"] = publicOrigin,
                ["TENANT"] = h.Tenant.ToString("D"), ["SUBJECT"] = h.ActorId.ToString("D"), ["EPOCH"] = authEpoch.ToString("D"),
                ["STORE"] = store.ToString("D"), ["CLIENT_SECRET"] = clientSecret, ["BFF_HMAC"] = Convert.ToBase64String(hmac),
                ["ACCESS"] = tokens.Access, ["ID_HINT"] = tokens.Identity, ["SESSION"] = cookie, ["CSRF"] = csrf,
                ["ACCESS_EXPIRY"] = token.Claims.Single(x => x.Type == "exp").Value,
                ["ACCESS_ISSUED"] = token.Claims.Single(x => x.Type == "iat").Value,
                ["GRANT"] = token.Id, ["PG_SETTINGS"] = Environment.GetEnvironmentVariable("CP6_CRM_PG_SETTINGS")!
            }) start.Environment["CP6_W661_" + pair.Key] = pair.Value;
            using var child = new Process { StartInfo = start, EnableRaisingEvents = true };
            var log = new StringBuilder();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            child.OutputDataReceived += (_, evt) => Observe(evt.Data);
            child.ErrorDataReceived += (_, evt) => Observe(evt.Data);
            child.Exited += (_, _) => ready.TrySetException(new InvalidOperationException("Owned CRM BFF stopped before readiness."));
            Assert.True(child.Start());
            child.BeginOutputReadLine();
            child.BeginErrorReadLine();
            try
            {
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
                const string path = "/crm/logout";
                using var client = new HttpClient(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false, UseProxy = false });
                using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{bffPort}{path}")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string,string> { ["csrfToken"] = csrf })
                };
                var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                var nonce = CrmOidcCrypto.RandomToken();
                var fingerprint = new string('a', 64);
                var cookieHeader = "__Host-cp6-crm-session=" + cookie;
                var bodyHash = Convert.ToHexString(SHA256.HashData(await request.Content.ReadAsByteArrayAsync())).ToLowerInvariant();
                var signed = string.Join('\n', "CP6.CRM.BFF.v1", "POST", path, bodyHash, time, nonce, fingerprint,
                    publicOrigin, cookieHeader, request.Content.Headers.ContentType!.ToString(), "", "", "");
                request.Headers.Add("Origin", publicOrigin);
                request.Headers.Add("Cookie", cookieHeader);
                request.Headers.Add("X-CP6-BFF-Time", time);
                request.Headers.Add("X-CP6-BFF-Nonce", nonce);
                request.Headers.Add("X-CP6-BFF-Client", fingerprint);
                request.Headers.Add("X-CP6-BFF-Signature", Convert.ToHexString(HMACSHA256.HashData(hmac, Encoding.UTF8.GetBytes(signed))).ToLowerInvariant());
                using var result = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, result.StatusCode);
                Assert.Contains("CP6 统一登录暂时未能退出", await result.Content.ReadAsStringAsync(), StringComparison.Ordinal);
                Assert.Null(result.Headers.Location);
                Assert.Contains(result.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-cp6-crm-session=;", StringComparison.Ordinal));
                await using var deleted = new NpgsqlCommand("SELECT NOT EXISTS(SELECT 1 FROM crm_auth.\"AuthStatesV1\" WHERE \"Kind\"='session' AND \"KeyHash\"=@key)", crm);
                deleted.Parameters.AddWithValue("key", CrmOidcCrypto.Hash(cookie));
                Assert.True((bool)(await deleted.ExecuteScalarAsync())!);
            }
            finally
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
                lock (log) File.WriteAllText(Path.Combine(work, "logout.log"), log.ToString());
            }

            void Observe(string? line)
            {
                if (line is null) return;
                lock (log) log.AppendLine(line.Replace(clientSecret, "[owned client]").Replace(tokens.Access, "[owned access]")
                    .Replace(tokens.Identity, "[owned logout hint]").Replace(cookie, "[owned cookie]").Replace(csrf, "[owned csrf]"));
                if (line == "OWNED_W661_BFF_READY") ready.TrySetResult();
            }
        }

        async Task<FamilyReadback> ReadFamily()
        {
            var json = await h.Db.Database.SqlQuery<string>($"""
                WITH grants AS (
                    SELECT g."Id",g."Revoked",g."Consumed" FROM public."CrmOidcGrant" g
                    JOIN public."Sys_RefreshTokens" r ON g."SourceRefreshHash"=r."TokenHash" COLLATE "C"
                    WHERE r."BrowserSessionId"={family} AND g."OrganizationId"={h.Tenant} AND g."SubjectId"={h.ActorId}
                ) SELECT json_build_object(
                    'LoggedOut',(SELECT "LoggedOutAtUtc" IS NOT NULL FROM public."Sys_BrowserSessions" WHERE "Id"={family}),
                    'RevokedRefresh',(SELECT count(*) FROM public."Sys_RefreshTokens" WHERE "BrowserSessionId"={family} AND "RevokedAt" IS NOT NULL),
                    'Grants',(SELECT count(*) FROM grants),
                    'Unused',(SELECT count(*) FROM grants WHERE NOT "Consumed"),
                    'Revoked',(SELECT count(*) FROM grants WHERE "Revoked"),
                    'Snapshots',(SELECT count(*) FROM crm_identity."Snapshot" s JOIN grants g ON split_part(s."AggregateId", ':', 3)=g."Id"::text
                        WHERE s."TenantId"={h.Tenant} AND s."AggregateId" LIKE 'token:%'),
                    'PriorityOutbox',(SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" s JOIN grants g ON split_part(s."AggregateId", ':', 3)=g."Id"::text
                        WHERE s."TenantId"={h.Tenant} AND s."AggregateId" LIKE 'token:%'))::text AS "Value"
                """).SingleAsync();
            return JsonSerializer.Deserialize<FamilyReadback>(json)!;
        }
    }

    private sealed record FamilyReadback(bool LoggedOut, int RevokedRefresh, int Grants, int Unused, int Revoked, int Snapshots, int PriorityOutbox);
}
