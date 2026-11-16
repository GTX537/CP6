using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Client;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OtpNet;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [Pgt017MainFact]
    public async Task PGT017_two_factor_credential_mutations_preserve_the_current_user_crm_seal()
    {
        var mode = Environment.GetEnvironmentVariable("CP6_W668_2FA_MODE")!;
        var scenario = Environment.GetEnvironmentVariable("CP6_W668_SCENARIO");
        var close = scenario == "crm-unavailable-close";
        var credentialFirst = scenario == "credential-first";
        var unavailable = close || credentialFirst || scenario == "crm-unavailable";
        var native = mode.StartsWith("native-", StringComparison.Ordinal);
        var self = mode.EndsWith("-self", StringComparison.Ordinal) || mode.StartsWith("disable-self", StringComparison.Ordinal);
        var setup = mode.StartsWith("setup-", StringComparison.Ordinal) || mode.EndsWith("-setup", StringComparison.Ordinal);
        var disable = mode.StartsWith("disable-self", StringComparison.Ordinal);
        var email = mode == "disable-self-email";
        await using var h = await Seed();
        var pair = await GrantIntake(h);
        await GrantHttpPermissions(h);
        await CopyRole71ForIndependentInquiry(h);

        var secret = disable ? Base32Encoding.ToString(RandomNumberGenerator.GetBytes(20)) : null;
        var password = TestPassword();
        var managerPassword = TestPassword();
        var actor = await h.Db.Sys_Users.SingleAsync(row => row.Id == h.ActorId);
        var tenant = await h.Db.Sys_Tenants.SingleAsync(row => row.Id == h.Tenant);
        tenant.TwoFactorMode = self ? 1 : 2;
        var hasher = new BCryptPasswordHasher(4);
        actor.Password = hasher.Hash(password);
        actor.AuthenticationEpoch = Guid.NewGuid();
        actor.MustChangePassword = false;
        actor.TwoFactorEnabled = disable;
        actor.TwoFactorSecret = secret;
        actor.TwoFactorEnrolledAt = disable ? DateTime.Now : null;
        actor.Email = "w668-fixture@example.invalid";
        var independentFactor = self ? null : Base32Encoding.ToString(RandomNumberGenerator.GetBytes(20));
        var independentUser = new Sys_User
        {
            Id = Guid.NewGuid(), UserName = "w668-independent", Password = hasher.Hash(managerPassword), RoleId = 72,
            Enable = true, DeptId = h.Parent, AuthenticationEpoch = Guid.NewGuid(), TwoFactorEnabled = independentFactor is not null,
            TwoFactorSecret = independentFactor, TwoFactorEnrolledAt = independentFactor is null ? null : DateTime.Now
        };
        h.Db.Sys_Users.Add(independentUser);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var options = InquiryPermissionSigningTests.Options();
        await new IdentityBootstrapService(h.Db, ContextRuntime(h.Tenant, options.Issuer)).InitializeAsync(h.Tenant);
        h.Db.ChangeTracker.Clear();
        var store = Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!);
        var seal = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION"));
        await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
        await crm.OpenAsync();
        await InstallUserSealFixture(crm, seal, h.Tenant, store);
        await using var mail = email ? new FactorEmailMailbox(int.Parse(Environment.GetEnvironmentVariable("CP6_W668_SMTP_PORT")!)) : null;

        var environment = IdentityEnvironment(h.Tenant);
        var clientSecret = CrmOidcCrypto.RandomToken();
        const string redirect = "https://crm.cp6.test/callback";
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
        environment["Security__Password__BcryptWorkFactor"] = "4";
        environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT");
        environment["JWT__Secret"] = CrmOidcCrypto.RandomToken() + CrmOidcCrypto.RandomToken();
        if (mail is not null)
        {
            environment["Email__Smtp__Host"] = "127.0.0.1";
            environment["Email__Smtp__Port"] = mail.Port.ToString();
            environment["Email__Smtp__EnableSsl"] = "false";
            environment["Security__TwoFactor__EmailOtpLength"] = "6";
        }
        var prefix = $"CrmInquiryTenantPause__Tenants__{h.Tenant:D}__";
        environment[prefix + "ConnectionString"] = seal.ConnectionString;
        environment[prefix + "StoreId"] = store.ToString("D");
        var bridge = Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")!;
        Directory.CreateDirectory(bridge);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection, environment, allowAutoRedirect: false);
        var tenantCode = $"iam_{h.Tenant:N}";
        var originalBrowser = self ? await SignIn(host, actor.UserName, password, tenantCode, secret) : null;
        var independentBrowser = await SignIn(host, independentUser.UserName, managerPassword, tenantCode, independentFactor);
        var independentGrant = await Exchange(independentBrowser);
        var expectedFence = $"{store:D}/1";
        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "w668-owned-device", AppVersion = "1.0.0" };

        Dictionary<string, string>? pendingBrowser = null;
        string? challenge = null;
        if (!self)
        {
            if (native)
            {
                using var primary = await NativePost("api/client-auth/login",
                    new NativeLoginRequest { UserName = actor.UserName, Password = password, TenantCode = tenantCode, Client = client });
                Assert.Equal(HttpStatusCode.OK, primary.StatusCode);
                var original = (await primary.Content.ReadFromJsonAsync<NativeAuthResult>())!;
                Assert.Equal("enrollmentRequired", original.State);
                Assert.Null(original.Session);
                challenge = original.ChallengeToken;
            }
            else
            {
                using var primary = await NativePost("api/auth/login", new { UserName = actor.UserName, Password = password, TenantCode = tenantCode });
                Assert.Equal(HttpStatusCode.OK, primary.StatusCode);
                pendingBrowser = ReadCookies(primary);
                Assert.False(pendingBrowser.ContainsKey("cp6_at"));
                challenge = new JwtSecurityTokenHandler().ReadJwtToken(pendingBrowser["cp6_2fa"]).Id;
            }
            Assert.False(string.IsNullOrEmpty(challenge));
        }

        if (!setup && !disable)
        {
            using var preparation = await SendSetup();
            Assert.Equal(HttpStatusCode.OK, preparation.StatusCode);
            secret = await SetupSecret(preparation);
        }
        if (email)
        {
            using var send = await host.Send(Request(HttpMethod.Post, "api/auth/2fa/email-otp-self", originalBrowser!, new { }));
            Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        }
        var codeFromMail = mail is null ? null : await mail.Code.WaitAsync(TimeSpan.FromSeconds(10));
        var submittedCode = setup ? null : codeFromMail ?? new Totp(Base32Encoding.ToBytes(secret!)).ComputeTotp();
        var grants = self ? new[] { GrantId((await Exchange(originalBrowser!)).Access) } : Array.Empty<Guid>();
        var aggregates = new[] { $"user:{actor.Id:D}" }
            .Concat(grants.Select(id => IdentityEventContracts.TokenAggregate(options.Issuer, id.ToString("D")))).ToArray();
        Assert.Equal(expectedFence, await Sign(independentGrant.Access, independentUser.Id, "intake:reject"));
        var originalSecret = (await h.Db.Sys_Users.AsNoTracking().SingleAsync(row => row.Id == actor.Id)).TwoFactorSecret;
        var challengeHash = challenge is null ? null : AuthenticationInteractionStoreV1.Hash(challenge);
        var before = await MainFacts();
        var route = self ? "api/auth/2fa/" + (setup ? "setup-self" : disable ? "disable-self" : "enroll-self")
            : (native ? "api/client-auth/2fa/" : "api/auth/2fa/") + (setup ? "setup" : "enroll");
        var usesCrm = self || !setup;
        if (scenario == "authority-first")
        {
            var floors = await Floors();
            using var losing = await InMainOrder(authorityFirst: true);
            Assert.Equal(HttpStatusCode.Unauthorized, losing.StatusCode);
            Assert.Equal(before, await MainFacts());
            Assert.Equal(floors, await Floors());
            await Save("two-factor-main-authority-first.json", new { Mode = mode, Route = route, Before = before, After = await MainFacts(),
                CredentialHttp = (int)losing.StatusCode, Floors = await Floors() });
            return;
        }
        if (unavailable) await SetSealLogin(false);
        try
        {
            if (unavailable)
            {
                await using var broken = new NpgsqlConnection(seal.ConnectionString);
                var refused = await Assert.ThrowsAsync<PostgresException>(() => broken.OpenAsync());
                Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, refused.SqlState);
            }
            var total = await Denials();
            var floorsBefore = await Floors();
            using var response = await Mutate();
            var denials = await Denials() - total;
            var after = await MainFacts();
            var independentFence = await Sign(independentGrant.Access, independentUser.Id, "intake:reject");
            var floorsAfter = await Floors();
            var pending = await IamCrmUserSealV1.ReadAsync(h.Db, h.Tenant, Guid.Empty, CancellationToken.None, actor.Id);
            var envelope = await FailureEnvelope(response);
            var webAccess = response.Headers.TryGetValues("Set-Cookie", out var cookies)
                && cookies.Any(cookie => cookie.StartsWith("cp6_at=", StringComparison.Ordinal));
            string? status = null;
            if (unavailable && !self && !setup && response.StatusCode == HttpStatusCode.ServiceUnavailable)
                status = await Completion("status");
            await Save("two-factor-candidate.json", new
            {
                Mode = mode, Route = route, Tenant = h.Tenant, Subject = actor.Id, Grants = grants, DeniedConnections = denials,
                Http = (int)response.StatusCode, Envelope = envelope, WebAccessIssued = webAccess,
                IndependentFence = independentFence == expectedFence, MailedOneCode = mail is not null,
                Before = before, After = after, FloorsBefore = floorsBefore, FloorsAfter = floorsAfter,
                Pending = pending is null ? null : new { pending.OperationId, pending.UserId, pending.ActorId, pending.Kind, pending.StoreId, pending.SealStarted },
                CompletionStatus = status
            });
            Assert.Equal(expectedFence, independentFence);
            if (unavailable && usesCrm)
            {
                Assert.True(denials > 0, "The owned database role must refuse the actual CRM seal connection.");
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Equal(before with { Pending = after.Pending, Challenge = after.Challenge }, after);
                Assert.Equal(floorsBefore, floorsAfter);
                Assert.Equal(1, after.Pending);
                Assert.NotNull(pending);
                Assert.Equal((actor.Id, actor.Id, store), (pending.ActorId, pending.UserId, pending.StoreId));
                Assert.False(pending.SealStarted);
                Assert.False(webAccess);
                if (!self)
                {
                    Assert.Equal("COMPLETION_UNKNOWN", status);
                    Assert.Equal(before.Challenge!.Completion, after.Challenge!.Completion);
                    Assert.Equal(("completing", true, true, true, 1, 0),
                        (after.Challenge.State, after.Challenge.HasLease, after.Challenge.Prepared,
                            after.Challenge.HasFactor, after.Challenge.Consumptions, after.Challenge.Sessions));
                }
                if (close)
                {
                    Assert.Equal("CLOSED", await Completion("close"));
                    var closed = await MainFacts();
                    Assert.Equal(0, closed.Pending);
                    Assert.Equal(before.MainEffects, closed.MainEffects);
                    Assert.Equal(("rejected", false, true, 1, 0),
                        (closed.Challenge!.State, closed.Challenge.HasLease, closed.Challenge.HasFactor, closed.Challenge.Consumptions, closed.Challenge.Sessions));
                    Assert.Equal(floorsBefore, await Floors());
                    await Save("two-factor-closed.json", new { Mode = mode, Route = route, pending.OperationId, After = closed, Floors = await Floors() });
                    return;
                }
                await SetSealLogin(true);
                using var resumed = credentialFirst ? await InMainOrder(authorityFirst: false) : await Mutate();
                var recovered = await MainFacts();
                await CheckCommitted(resumed, recovered);
                var receipt = await h.Db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.iam_operations
                        WHERE tenant_id={h.Tenant} AND operation_id={pending.OperationId} AND actor_id={actor.Id} AND state='COMMITTED') AS "Value"
                    """).SingleAsync();
                Assert.True(receipt);
                await Save("two-factor-recovered.json", new { Mode = mode, Route = route, pending.OperationId, After = recovered,
                    OriginalReceipt = receipt, Floors = await Floors(), Http = (int)resumed.StatusCode });
            }
            else
            {
                Assert.Equal(0, denials);
                await CheckCommitted(response, after);
                if (!usesCrm) Assert.Equal(floorsBefore, floorsAfter);
            }
        }
        finally { if (unavailable) await SetSealLogin(true); }

        Task<HttpResponseMessage> NativePost(string path, object body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            return host.Send(request);
        }

        async Task<string> Completion(string action)
        {
            var endpoint = "completion/" + action;
            using var response = native
                ? await NativePost("api/client-auth/" + endpoint,
                    new NativeCompletionRecoveryRequest { Kind = "interaction", Credential = challenge!, Client = client })
                : await host.Send(Request(HttpMethod.Post, "api/auth/" + endpoint, pendingBrowser!, new { }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<AuthenticationRecoveryStatusV1>())!.State;
        }

        Task<HttpResponseMessage> SendSetup() => native
            ? NativePost("api/client-auth/2fa/setup", new NativeChallengeRequest { ChallengeToken = challenge!, Client = client })
            : host.Send(Request(HttpMethod.Post, self ? "api/auth/2fa/setup-self" : "api/auth/2fa/setup",
                self ? originalBrowser! : pendingBrowser!, new { }));

        Task<HttpResponseMessage> Mutate()
        {
            if (setup) return SendSetup();
            if (native) return NativePost(route, new NativeTwoFactorRequest { ChallengeToken = challenge!, Client = client, Code = submittedCode!, Method = "totp" });
            object body = disable ? new { CurrentPassword = password, Code = submittedCode, Method = email ? "email" : "totp" } : new { Code = submittedCode };
            return host.Send(Request(HttpMethod.Post, route, self ? originalBrowser! : pendingBrowser!, body));
        }

        async Task<HttpResponseMessage> InMainOrder(bool authorityFirst)
        {
            await using var held = new NpgsqlConnection(connection);
            await using var observer = new NpgsqlConnection(connection);
            await held.OpenAsync();
            await observer.OpenAsync();
            await using var transaction = await held.BeginTransactionAsync();
            var statement = authorityFirst ? """SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@target FOR UPDATE"""
                : """SELECT 1 FROM public."Sys_Users" WHERE "TenantId"=@tenant AND "Id"=@target FOR UPDATE""";
            await using var command = new NpgsqlCommand(statement, held, transaction);
            command.Parameters.AddWithValue("target", authorityFirst ? h.Tenant : actor.Id);
            if (!authorityFirst) command.Parameters.AddWithValue("tenant", h.Tenant);
            Assert.Equal(1, await command.ExecuteScalarAsync());
            var first = authorityFirst ? Policy() : Mutate();
            Task<HttpResponseMessage>? second = null;
            try
            {
                var firstWriter = await WaitForMainBlock(observer, held.ProcessID,
                    authorityFirst ? "%UPDATE%Sys_Tenants%" : "%UPDATE%Sys_Users%", first);
                second = authorityFirst ? Mutate() : Policy();
                var secondWriter = await WaitForMainBlock(observer, firstWriter, "%cp6_authentication_v1.acquire()%", second);
                var waiting = await MainFacts();
                Assert.Equal(before with { Pending = waiting.Pending }, waiting);
                await Save("two-factor-main-wait.json", new { Mode = mode, AuthorityFirst = authorityFirst, FirstWriter = firstWriter, SecondWriter = secondWriter });
            }
            finally { await transaction.RollbackAsync(); }
            using var authority = await (authorityFirst ? first : second!);
            Assert.Equal(HttpStatusCode.OK, authority.StatusCode);
            Assert.Equal(0, (await h.Db.Sys_Tenants.AsNoTracking().SingleAsync(row => row.Id == h.Tenant)).TwoFactorMode);
            return await (authorityFirst ? second! : first);

            Task<HttpResponseMessage> Policy() => host.Send(Request(HttpMethod.Put, "api/sys/two-factor-policy", independentBrowser, new { Mode = 0 }));
        }

        async Task CheckCommitted(HttpResponseMessage response, FactorMainProbe after)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, after.Pending);
            Assert.Equal(setup, after.CredentialRevision == before.CredentialRevision);
            Assert.Equal(setup, after.Epoch == before.Epoch);
            Assert.Equal(!disable && !setup, after.TwoFactor);
            Assert.Equal(!setup && !disable, after.OriginalSecret);
            if (setup)
            {
                Assert.NotEqual(before.PreparationRevision, after.PreparationRevision);
                Assert.True(await SetupSecret(response) == (await h.Db.Sys_Users.AsNoTracking().SingleAsync(row => row.Id == actor.Id)).TwoFactorSecret);
                if (!self)
                    Assert.Equal(("pending", true, 0, 0),
                        (after.Challenge!.State, after.Challenge.Prepared, after.Challenge.Consumptions, after.Challenge.Sessions));
            }
            if (self)
                Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"""
                    SELECT count(*)::int AS "Value" FROM public."CrmOidcGrant" WHERE "Id"=ANY({grants})
                        AND "OrganizationId"={h.Tenant} AND "SubjectId"={actor.Id} AND "Revoked"
                    """).SingleAsync());
            if (!self && !setup)
            {
                Assert.True(native ? (await response.Content.ReadFromJsonAsync<NativeAuthResult>())?.Session is not null
                    : ReadCookies(response).ContainsKey("cp6_at"));
                Assert.Equal(before.Challenge!.Completion, after.Challenge!.Completion);
                Assert.Equal(("completed", false, true, 1, 1),
                    (after.Challenge.State, after.Challenge.HasLease, after.Challenge.HasFactor, after.Challenge.Consumptions, after.Challenge.Sessions));
            }
        }

        async Task<FactorMainProbe> MainFacts()
        {
            var user = await h.Db.Sys_Users.AsNoTracking().SingleAsync(row => row.Id == actor.Id);
            var preparation = await h.Db.Database.SqlQuery<Guid>($"""
                SELECT revision AS "Value" FROM cp6_authentication_v1.preparation_versions WHERE user_id={actor.Id}
                """).SingleAsync();
            var effects = await h.Db.Database.SqlQuery<string>($"""
                SELECT jsonb_build_object(
                    'Refresh',(SELECT coalesce(jsonb_agg(jsonb_build_array("Id","ClientKind","BrowserSessionId","RevokedAt","CompletionPending") ORDER BY "Id"),'[]'::jsonb)
                        FROM public."Sys_RefreshTokens" WHERE "TenantId"={h.Tenant} AND "UserId"={actor.Id}),
                    'Grants',(SELECT coalesce(jsonb_agg(jsonb_build_array("Id","Consumed","Revoked") ORDER BY "Id"),'[]'::jsonb)
                        FROM public."CrmOidcGrant" WHERE "OrganizationId"={h.Tenant} AND "SubjectId"={actor.Id}),
                    'Sources',(SELECT coalesce(jsonb_agg(jsonb_build_array("AggregateId","Version","PayloadSha256") ORDER BY "AggregateId"),'[]'::jsonb)
                        FROM crm_identity."Snapshot" WHERE "TenantId"={h.Tenant} AND "AggregateId"=ANY({aggregates})),
                    'Generation',(SELECT "Generation" FROM public."CrmIdentityTenantGenerations" WHERE "TenantId"={h.Tenant}),
                    'Outbox',(SELECT count(*) FROM crm_identity."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant} AND "AggregateId"=ANY({aggregates}))
                      +(SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant} AND "AggregateId"=ANY({aggregates})),
                    'Audit',(SELECT coalesce(jsonb_agg(jsonb_build_array("Id","EventType","Reason","ClientIp" IS NOT NULL,"UserAgent" IS NOT NULL) ORDER BY "Id"),'[]'::jsonb)
                        FROM public."Sys_SecurityLogs" WHERE "TenantId"={h.Tenant} AND "UserId"={actor.Id}
                        AND "EventType"=ANY({new[] { (int)SecurityEventType.TwoFactorEnrolled, (int)SecurityEventType.TwoFactorReset,
                            (int)SecurityEventType.TwoFactorChallenged, (int)SecurityEventType.TwoFactorEmailOtpSent, (int)SecurityEventType.LoginSuccess }}))
                )::text AS "Value"
                """).SingleAsync();
            var pending = await h.Db.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value" FROM cp6_authentication_v1.pending_crm_user_seals WHERE tenant_id={h.Tenant} AND user_id={actor.Id}
                """).SingleAsync();
            var factor = challengeHash is null ? null : await h.Db.Database.SqlQuery<FactorInteractionProbe>($"""
                SELECT i.completion_id AS "Completion",i.state AS "State",i.lease IS NOT NULL AS "HasLease",
                    i.preparation_hash IS NOT NULL AS "Prepared",i.factor IS NOT NULL AS "HasFactor",
                    (SELECT count(*)::int FROM cp6_authentication_v1.consumed_totp c WHERE c.completion_id=i.completion_id) AS "Consumptions",
                    (SELECT count(*)::int FROM cp6_authentication_v1.session_evidence s WHERE s.completion_id=i.completion_id) AS "Sessions"
                FROM cp6_authentication_v1.interactions i WHERE i.challenge_hash={challengeHash}
                """).SingleAsync();
            return new(user.CredentialRevision, user.AuthenticationEpoch, preparation, user.TwoFactorEnabled, user.TwoFactorSecret == originalSecret,
                user.TwoFactorEnrolledAt, pending, effects, factor);
        }

        async Task<int[]> Floors()
        {
            await using var sql = new NpgsqlCommand("""
                SELECT ARRAY(SELECT coalesce(f.minimum_version,0) FROM unnest(@aggregates::text[]) WITH ORDINALITY a(aggregate,idx)
                    LEFT JOIN crm_inquiry_tenant_pause_v1.identity_fences f ON f.tenant_id=@tenant AND f.aggregate_id=a.aggregate ORDER BY a.idx)
                """, crm);
            sql.Parameters.AddWithValue("tenant", h.Tenant);
            sql.Parameters.AddWithValue("aggregates", aggregates);
            return (int[])(await sql.ExecuteScalarAsync())!;
        }

        async Task SetSealLogin(bool active)
        {
            using var quote = new NpgsqlCommandBuilder();
            var role = quote.QuoteIdentifier(seal.Username!);
            await using var sql = new NpgsqlCommand($"ALTER ROLE {role} {(active ? "LOGIN" : "NOLOGIN")}", crm);
            await sql.ExecuteNonQueryAsync();
        }

        async Task<int> Denials()
        {
            var log = Environment.GetEnvironmentVariable("CP6_W668_CRM_SERVER_LOG")!;
            await using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var total = 0;
            while (await reader.ReadLineAsync() is { } line)
                if (line.Contains($"role \"{seal.Username}\" is not permitted to log in", StringComparison.Ordinal)) total++;
            return total;
        }

        async Task<string?> Sign(string bearer, Guid expected, string purpose)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "connect/inquiry-human-context")
            {
                Content = JsonContent.Create(new InquiryHumanContextRequestV1(purpose, pair.SiteId, pair.FormId, ServiceNonce()))
            };
            request.Headers.Authorization = new("Bearer", bearer);
            request.Headers.Add("X-Forwarded-Proto", "https");
            using var response = await host.Send(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var signed = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
            Assert.True(ContextSignature(signed, options.PublicKeyBase64, InquiryPermissionSigningKeyV1.HumanContextVersion));
            Assert.Equal(expected, signed["subjectId"]!.GetValue<Guid>());
            Assert.Equal(purpose, signed["purpose"]!.GetValue<string>());
            return response.Headers.TryGetValues("X-CP6-Inquiry-Tenant-Fence", out var values) ? Assert.Single(values) : null;
        }

        async Task<(string Access, string Identity)> Exchange(Dictionary<string, string> cookies)
        {
            var verifier = CrmOidcCrypto.RandomToken();
            var query = QueryHelpers.AddQueryString("connect/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = "CP6.Web", ["redirect_uri"] = redirect, ["response_type"] = "code",
                ["scope"] = "openid profile crm", ["code_challenge"] = CrmOidcCrypto.Challenge(verifier),
                ["code_challenge_method"] = "S256", ["state"] = CrmOidcCrypto.RandomToken(), ["nonce"] = CrmOidcCrypto.RandomToken()
            });
            using var entry = await host.Send(Request(HttpMethod.Get, query, cookies));
            Assert.Equal(HttpStatusCode.Redirect, entry.StatusCode);
            using var approved = await host.Send(Request(HttpMethod.Get, entry.Headers.Location!.PathAndQuery, cookies));
            Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
            var result = QueryHelpers.ParseQuery(approved.Headers.Location!.Query);
            Assert.True(result.TryGetValue("code", out var code), "OIDC did not issue a code for the original Web family.");
            using var exchange = new HttpRequestMessage(HttpMethod.Post, "connect/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code", ["code"] = code.ToString(), ["code_verifier"] = verifier, ["redirect_uri"] = redirect
                })
            };
            exchange.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("CP6.Web:" + clientSecret)));
            using var redeemed = await host.Send(exchange);
            Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
            using var tokens = JsonDocument.Parse(await redeemed.Content.ReadAsStringAsync());
            return (tokens.RootElement.GetProperty("access_token").GetString()!, tokens.RootElement.GetProperty("id_token").GetString()!);
        }

        Task Save(string name, object result) => File.WriteAllTextAsync(Path.Combine(bridge, name),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<string> SetupSecret(HttpResponseMessage result)
    {
        using var body = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("secret").GetString()!;
    }

    private sealed record FactorMainProbe(Guid CredentialRevision, Guid Epoch, Guid PreparationRevision, bool TwoFactor, bool OriginalSecret,
        DateTime? EnrolledAt, int Pending, string MainEffects, FactorInteractionProbe? Challenge);

    private sealed record FactorInteractionProbe(Guid Completion, string State, bool HasLease, bool Prepared, bool HasFactor, int Consumptions, int Sessions);

    private sealed class FactorEmailMailbox : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource stopping = new();
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public Task<string> Code { get; }

        public FactorEmailMailbox(int port)
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            Code = ReceiveAsync(stopping.Token);
        }

        private async Task<string> ReceiveAsync(CancellationToken token)
        {
            using var socket = await listener.AcceptTcpClientAsync(token);
            await using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 localhost");
            while (await reader.ReadLineAsync(token) is { } line)
            {
                if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("354 End with dot");
                    var message = new StringBuilder();
                    while (await reader.ReadLineAsync(token) is { } part && part != ".") message.AppendLine(part);
                    await writer.WriteLineAsync("250 Accepted");
                    if (await reader.ReadLineAsync(token) is not null) await writer.WriteLineAsync("221 Closing");
                    var text = message.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
                    var split = text.IndexOf("\n\n", StringComparison.Ordinal);
                    Assert.True(split >= 0, "The simulation must receive a MIME body.");
                    var body = text[(split + 2)..];
                    if (text[..split].Contains("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase))
                        body = Encoding.UTF8.GetString(Convert.FromBase64String(body));
                    else
                        body = body.Replace("=\n", "", StringComparison.Ordinal);
                    var found = Regex.Match(body, @"(?<!\d)\d{6}(?!\d)");
                    Assert.True(found.Success, "The owned SMTP endpoint must receive the six-digit code.");
                    return found.Value;
                }
                await writer.WriteLineAsync("250 localhost");
            }
            throw new IOException("The owned SMTP connection closed before delivering a message.");
        }

        public async ValueTask DisposeAsync()
        {
            await stopping.CancelAsync();
            listener.Stop();
            try { await Code; }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            finally { stopping.Dispose(); }
        }
    }
}
