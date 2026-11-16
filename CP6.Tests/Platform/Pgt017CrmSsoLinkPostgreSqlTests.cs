using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Erp;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [Pgt017SsoLinkFact]
    public async Task PGT017_approved_sso_link_preserves_the_original_crm_boundary()
    {
        var scenario = Environment.GetEnvironmentVariable("CP6_W673_SCENARIO");
        Assert.Contains(scenario, new[] { "cold", "owner-first", "pause-first" });
        var bridge = Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")!;
        await using var h = await Seed();
        var pair = await GrantIntake(h);
        await CopyRole71ForIndependentInquiry(h);
        var password = TestPassword();
        var hasher = new BCryptPasswordHasher(4);
        var target = await h.Db.Sys_Users.SingleAsync(row => row.Id == h.ActorId);
        target.Password = hasher.Hash(password);
        target.AuthenticationEpoch = Guid.NewGuid();
        target.Email = "first-link@example.invalid";
        var independent = new Sys_User
        {
            Id = Guid.NewGuid(), UserName = "w673-independent", Enable = true, DeptId = h.Parent, RoleId = 72,
            Password = hasher.Hash(password), AuthenticationEpoch = Guid.NewGuid()
        };
        h.Db.Sys_Users.Add(independent);
        if (scenario == "cold")
        {
            var supportMenu = await h.Db.Sys_Menus.SingleOrDefaultAsync(row => row.MenuKey == "sys-sso-config");
            if (supportMenu is null)
            {
                supportMenu = new Sys_Menu { MenuId = 9888007, MenuKey = "sys-sso-config", MenuName = "SSO", Enable = true };
                h.Db.Sys_Menus.Add(supportMenu);
            }
            h.Db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 72, MenuId = supportMenu.MenuId, ActionCode = "edit" });
        }
        h.Db.Sys_TenantSsoConfigs.Add(new Sys_TenantSsoConfig
        {
            TenantId = h.Tenant, Authority = SignedSsoExchange.Issuer, ClientId = SignedSsoExchange.Client,
            Enabled = true, AutoProvision = true, DefaultRoleId = 72, EmailClaim = "email", Scopes = "openid email",
            ClientSecretProtected = "synthetic-idp-only"
        });
        await h.Db.SaveChangesAsync();
        var policy = new SsoIdentityAdmissionPolicyV1(Guid.NewGuid(), Guid.NewGuid(), SignedSsoExchange.Issuer,
            SignedSsoExchange.Issuer, SignedSsoExchange.Client, "email", "verified-email", true, true, true,
            [72], Guid.NewGuid(), DateTimeOffset.UtcNow);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_authentication_v1.sso_admission_policies(tenant_id,document) VALUES({h.Tenant},{JsonSerializer.Serialize(policy)})
            """);
        var tenantCode = await h.Db.Sys_Tenants.Where(row => row.Id == h.Tenant).Select(row => row.TenantCode).SingleAsync();
        var signing = InquiryPermissionSigningTests.Options();
        await new IdentityBootstrapService(h.Db, ContextRuntime(h.Tenant, signing.Issuer)).InitializeAsync(h.Tenant);
        h.Db.ChangeTracker.Clear();

        var store = Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!);
        var seal = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION"));
        await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
        await crm.OpenAsync();
        if (scenario == "cold")
            await InstallUserSealFixture(crm, seal, h.Tenant, store);
        var ackPort = Environment.GetEnvironmentVariable("CP6_W673_CRM_ACK_PORT");
        await using var relay = scenario == "cold" && !string.IsNullOrEmpty(ackPort)
            ? new StockPostgresCommitRelay(seal.Port, seal.Username!, int.Parse(ackPort)) : null;
        var callbackConnection = new NpgsqlConnectionStringBuilder(seal.ConnectionString);
        if (relay is not null)
            callbackConnection.Port = relay.Port;
        var binding = new CrmInquiryTenantPauseBindingV1 { StoreId = store, ConnectionString = callbackConnection.ConnectionString };
        await using var fence = new CrmInquiryTenantPauseV1(new Dictionary<Guid, CrmInquiryTenantPauseBindingV1> { [h.Tenant] = binding });
        var tenantContext = new TenantContext { CurrentTenantId = h.Tenant };
        await using var callbackDb = server.Context(tenantContext, ContextRuntime(h.Tenant, signing.Issuer, fence));
        var idp = new SignedSsoExchange(callbackDb, tenantContext, hasher, tenantCode);

        var env = IdentityEnvironment(h.Tenant);
        var clientSecret = CrmOidcCrypto.RandomToken();
        env["CrmOidc__Issuer"] = signing.Issuer;
        env["CrmOidc__Clients__0__SecretSha256"] = CrmOidcCrypto.Hash(clientSecret);
        env["CrmOidc__Clients__0__RedirectUris__0"] = PrivacyCrmRedirect;
        env["CrmOidc__TrustedTokenProxyAddresses__0"] = "127.0.0.1";
        env["InquiryPermissionSigning__Enabled"] = "true";
        env["InquiryPermissionSigning__Issuer"] = signing.Issuer;
        env["InquiryPermissionSigning__KeyId"] = signing.KeyId;
        env["InquiryPermissionSigning__PrivateKeyBase64"] = signing.PrivateKeyBase64;
        env["InquiryPermissionSigning__PublicKeyBase64"] = signing.PublicKeyBase64;
        env["InquiryPermissionSigning__ActiveFromUtc"] = signing.ActiveFromUtc.ToString("O");
        env["Security__Password__BcryptWorkFactor"] = "4";
        env["ASPNETCORE_URLS"] = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT");
        env["JWT__Secret"] = CrmOidcCrypto.RandomToken() + CrmOidcCrypto.RandomToken();
        var registration = $"CrmInquiryTenantPause__Tenants__{h.Tenant:D}__";
        env[registration + "StoreId"] = store.ToString("D");
        env[registration + "ConnectionString"] = seal.ConnectionString;
        await using var api = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, env, allowAutoRedirect: false);
        var cookies = await Login(api, h, password, target.UserName);
        var independentCookies = await Login(api, h, password, independent.UserName);
        var grant = await ExchangePrivacyGrant(api, cookies, clientSecret);
        var secondGrant = await ExchangePrivacyGrant(api, cookies, clientSecret);
        var unaffectedGrant = await ExchangePrivacyGrant(api, independentCookies, clientSecret);
        var grantIds = new[] { GrantId(grant.Access), GrantId(secondGrant.Access) };
        var userAggregate = $"user:{target.Id:D}";
        var aggregates = new[] { userAggregate }.Concat(grantIds.Select(id => IdentityEventContracts.TokenAggregate(signing.Issuer, id.ToString("D")))).ToArray();
        var initial = await Effects();
        var initialFence = $"{store:D}/1";
        Assert.Equal(initialFence, await Sign(grant.Access, target.Id, "intake:reject"));
        Assert.Equal(initialFence, await Sign(secondGrant.Access, target.Id, "intake:reject"));
        Assert.Equal(initialFence, await Sign(unaffectedGrant.Access, independent.Id, "intake:reject"));

        const string subject = "verified-original-subject";
        if (scenario == "cold")
        {
            await SetSealLogin(false);
            try
            {
                await using var refused = new NpgsqlConnection(seal.ConnectionString);
                var refusal = await Assert.ThrowsAsync<PostgresException>(() => refused.OpenAsync());
                Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, refusal.SqlState);

                var withdrawn = await idp.Start(subject, target.Email!);
                await CheckPending(withdrawn);
                var neverReturned = await idp.Start(subject, target.Email!, withdrawn.Key);
                Assert.Equal(("UNKNOWN", withdrawn.Reference), await OriginalStatus(withdrawn));
                var closed = await Status(withdrawn, close: true);
                Assert.Equal(("CLOSED", withdrawn.Reference), (closed.IdentityState, closed.IdentityOperationRef));
                Assert.Equal(2, await ReceiptCount(withdrawn.Reference, neverReturned.Reference));
                Assert.Equal(initialFence, await Sign(grant.Access, target.Id, "intake:reject"));
                Assert.Equal(initial, await Effects());

                var earliest = DateTimeOffset.UtcNow;
                var original = await idp.Start(subject, target.Email!);
                await CheckPending(original);
                var accepted = await Pending();
                using var plan = JsonDocument.Parse(accepted.RequestProtected);
                var deadline = plan.RootElement.GetProperty("Deadline").GetDateTimeOffset();
                var pendingMinutes = new SecurityOptions().TwoFactor.PendingTokenMinutes;
                Assert.InRange(deadline, earliest.AddMinutes(pendingMinutes), DateTimeOffset.UtcNow.AddMinutes(pendingMinutes));
                await idp.Start(subject, target.Email!, original.Key); // A user returned before completing this IdP round trip.
                Assert.Equal(("UNKNOWN", original.Reference), await OriginalStatus(original));
                var otherBrowser = await idp.Start(subject, target.Email!);
                var wrongBinding = await Assert.ThrowsAsync<SsoMappingRecoveryRequiredV1>(otherBrowser.Callback);
                Assert.Equal(original.Reference, wrongBinding.OperationRef);
                Assert.Equal(0, await ReceiptCount(original.Reference, otherBrowser.Reference));

                var exchanges = idp.ExchangeCount;
                var reused = await Assert.ThrowsAsync<InvalidOperationException>(original.Callback);
                Assert.Equal("E-SEC-022", reused.Message);
                Assert.Equal(exchanges, idp.ExchangeCount);
                Assert.Equal(initial, await Effects());

                await SetSealLogin(true);
                if (relay is not null)
                {
                    var uncertain = await idp.Start(subject, target.Email!, original.Key);
                    relay.Arm();
                    var lost = await Assert.ThrowsAsync<SsoMappingRecoveryRequiredV1>(uncertain.Callback);
                    await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.Equal(original.Reference, lost.OperationRef);
                    Assert.Equal(("UNKNOWN", original.Reference), await OriginalStatus(original));
                    Assert.Equal(initial, await Effects());
                    Assert.Equal(new[] { initial.UserVersion + 1, 1, 1 }, await Floors());
                    var retained = await Pending();
                    Assert.True(retained.SealStarted);
                    Assert.Equal((accepted.OperationId, accepted.RequestProtected, accepted.SourceSeals),
                        (retained.OperationId, retained.RequestProtected, retained.SourceSeals));
                    using var close = Request(HttpMethod.Post, $"api/sys/sso-config/identity-operations/{original.Reference}/close", independentCookies);
                    using var protectedClosure = await api.Send(close);
                    Assert.Equal(HttpStatusCode.OK, protectedClosure.StatusCode);
                    Assert.Equal(new SsoMappingStatusV1(original.Reference, "UNKNOWN"),
                        await protectedClosure.Content.ReadFromJsonAsync<SsoMappingStatusV1>());
                }
                var httpPort = int.Parse(Environment.GetEnvironmentVariable("CP6_W673_SSO_HTTP_PORT")!);
                var retry = await idp.ResumeThroughWeb(subject, target.Email!, original.Key, httpPort);
                var evidence = await new AuthenticationInteractionStoreV1(h.Db).ReadSessionAsync(retry.AccessJti, default);
                Assert.NotNull(evidence);
                await CheckCommitted(original, evidence.Parse().Primary, freshSessions: 1);
                Assert.Equal(1, await ReceiptCount(original.Reference, retry.Reference));
                Assert.Equal(plan.RootElement.GetProperty("Epoch").GetGuid(), (await Effects()).Epoch);
                Assert.Null(await IamCrmUserSealV1.ReadAsync(h.Db, h.Tenant, accepted.OperationId, default));
                var replay = await idp.Start(subject, target.Email!);
                Assert.Equal(target.Id, (await replay.Callback()).UserId);
                Assert.Equal(1, (await Effects()).LinkAudits);
                await Save("sso-link-cold.json", new { Original = original.Reference, Withdrawn = withdrawn.Reference,
                    NativeCrmCommitAcknowledgementLost = relay is not null, HttpResume = retry.Reference, OriginalDeadline = deadline,
                    Before = initial, After = await Effects(), Floors = await Floors() });
            }
            finally { await SetSealLogin(true); }
        }
        else
        {
            var rows = await h.Db.CrmIdentitySnapshots.AsNoTracking().Where(row => row.TenantId == h.Tenant)
                .OrderBy(row => row.AggregateId).ToArrayAsync();
            var session = Path.Combine(bridge, "session.json");
            try
            {
                await File.WriteAllTextAsync(session + ".tmp", JsonSerializer.Serialize(new
                {
                    marker = "CP6-owned-inquiry-configuration-interop-v1", issuer = signing.Issuer, origin = api.Origin.ToString(),
                    accessToken = grant.Access, idToken = grant.Identity, tenantId = h.Tenant, subjectId = target.Id,
                    departmentId = h.Parent, siteId = pair.SiteId, formId = pair.FormId, keyId = signing.KeyId,
                    publicKeyBase64 = signing.PublicKeyBase64, activeFromUtc = signing.ActiveFromUtc, authenticationEpoch = initial.Epoch,
                    sourceRows = rows.Select(row => new { row.AggregateId, row.EventType, row.Version, row.PayloadSha256, row.IsDeleted, row.PayloadJson })
                }));
                File.Move(session + ".tmp", session);
                var ownerFirst = scenario == "owner-first";
                await WaitConfigurationBridge(bridge, "ready");
                await WaitConfigurationBridge(bridge, ownerFirst ? "owner-held" : "permission-held");
                Assert.All(await Floors(), floor => Assert.Equal(0, floor));
                var original = await idp.Start(subject, target.Email!);
                var callback = original.Callback();
                int? waitingSeal = null;
                if (ownerFirst)
                {
                    try
                    {
                        await using var wait = new NpgsqlCommand("""
                            SELECT pid FROM pg_stat_activity WHERE datname=current_database() AND usename=@login
                              AND application_name='cp6-main-inquiry-tenant-pause-v1' AND wait_event_type='Lock'
                              AND cardinality(pg_blocking_pids(pid))>0
                            """, crm);
                        wait.Parameters.AddWithValue("login", seal.Username!);
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                        while (waitingSeal is null)
                        {
                            waitingSeal = (int?)await wait.ExecuteScalarAsync(timeout.Token);
                            if (waitingSeal is not null)
                                break;
                            Assert.False(callback.IsCompleted, "The CRM Owner must hold the native write boundary.");
                            await Task.Delay(25, timeout.Token);
                        }
                        Assert.Equal(initial, await Effects());
                    }
                    finally { await Signal("release-owner"); }
                }
                await CheckCommitted(original, await callback);
                await Signal("paused");
                await WaitConfigurationBridge(bridge, "finished");
                await Save("sso-link-order.json", new { Scenario = scenario, WaitingSeal = waitingSeal, Original = original.Reference, Before = initial, After = await Effects(), Floors = await Floors() });
            }
            finally
            {
                File.Delete(session);
                File.Delete(session + ".tmp");
            }
        }

        async Task CheckPending(SsoLinkExchange attempt)
        {
            var denials = await SealDenials();
            var error = await Assert.ThrowsAsync<SsoMappingRecoveryRequiredV1>(attempt.Callback);
            Assert.Equal(attempt.Reference, error.OperationRef);
            Assert.True(await SealDenials() > denials, "The verified callback reached the dedicated CRM seal connection.");
            Assert.Equal(("UNKNOWN", attempt.Reference), await OriginalStatus(attempt));
            Assert.Equal(initial, await Effects());
            Assert.All(await Floors(), floor => Assert.Equal(0, floor));
            Assert.Null(await Sign(grant.Access, target.Id, "intake:reject"));
            Assert.Null(await Sign(secondGrant.Access, target.Id, "intake:reject"));
            Assert.Null(await Sign(grant.Access, target.Id, "intake:list"));
            Assert.Equal(initialFence, await Sign(unaffectedGrant.Access, independent.Id, "intake:reject"));
        }

        async Task CheckCommitted(SsoLinkExchange attempt, AuthenticationProofV1 proof, int freshSessions = 0)
        {
            Assert.Equal((h.Tenant, target.Id, AuthenticationPrimaryMethod.Sso, AuthenticationEntryServiceV1.WebBinding(attempt.Key)),
                (proof.TenantId, proof.UserId, proof.PrimaryMethod, proof.ClientBinding));
            var status = await Status(attempt);
            Assert.Equal(("IDENTITY_BOUND", attempt.Reference), (status.IdentityState, status.IdentityOperationRef));
            var after = await Effects();
            Assert.NotEqual(initial.Epoch, after.Epoch);
            Assert.Equal(subject, after.Subject);
            Assert.Equal(SignedSsoExchange.Issuer, after.Issuer);
            Assert.Equal((initial.UserVersion + 1, initial.Outbox + aggregates.Length, grantIds.Length, 1, initial.Refresh + freshSessions),
                (after.UserVersion, after.Outbox, after.RevokedGrants, after.LinkAudits, after.Refresh));
            Assert.Equal(new[] { after.UserVersion, 1, 1 }, await Floors());
            using var profile = await api.Profile(cookies["cp6_at"]);
            Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
            Assert.Equal(initialFence, await Sign(unaffectedGrant.Access, independent.Id, "intake:reject"));
        }

        Task<BrowserAuthenticationStatusV1> Status(SsoLinkExchange attempt, bool close = false)
            => new BrowserAuthenticationRecoveryV1(h.Db).ResolveAsync(attempt.Key, close, default);

        async Task<IamCrmUserSealV1> Pending() => Assert.IsType<IamCrmUserSealV1>(
            await IamCrmUserSealV1.ReadAsync(h.Db, h.Tenant, Guid.Empty, default, target.Id));

        async Task<(string? State, string? Reference)> OriginalStatus(SsoLinkExchange attempt)
        {
            var original = await Status(attempt);
            return (original.IdentityState, original.IdentityOperationRef);
        }

        Task<int> ReceiptCount(string original, string retry) => h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_authentication_v1.sso_mapping_receipts
            WHERE tenant_id={h.Tenant} AND operation_ref IN ({original},{retry})
            """).SingleAsync();

        Task<SsoLinkEffects> Effects() => h.Db.Database.SqlQuery<SsoLinkEffects>($"""
            SELECT u."AuthenticationEpoch" AS "Epoch",u."ExternalProvider" AS "Issuer",u."ExternalSubject" AS "Subject",
              (SELECT "Version" FROM crm_identity."Snapshot" s WHERE s."TenantId"={h.Tenant} AND s."AggregateId"={userAggregate}) AS "UserVersion",
              (SELECT count(*) FROM crm_identity."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant} AND "AggregateId"=ANY({aggregates}))+
              (SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant} AND "AggregateId"=ANY({aggregates})) AS "Outbox",
              (SELECT count(*)::integer FROM public."CrmOidcGrant" WHERE "OrganizationId"={h.Tenant} AND "Id"=ANY({grantIds}) AND "Revoked") AS "RevokedGrants",
              (SELECT count(*)::integer FROM public."Sys_SecurityLogs" WHERE "TenantId"={h.Tenant} AND "UserId"={target.Id}
                AND "EventType"={(int)SecurityEventType.SsoIdentityLinked}) AS "LinkAudits",
              (SELECT count(*)::integer FROM public."Sys_RefreshTokens" WHERE "TenantId"={h.Tenant} AND "UserId"={target.Id}) AS "Refresh"
            FROM public."Sys_Users" u WHERE u."TenantId"={h.Tenant} AND u."Id"={target.Id}
            """).SingleAsync();

        async Task<int[]> Floors()
        {
            await using var command = new NpgsqlCommand("""
                SELECT ARRAY(SELECT coalesce(f.minimum_version,0) FROM unnest(@aggregates::text[]) WITH ORDINALITY a(id,i)
                    LEFT JOIN crm_inquiry_tenant_pause_v1.identity_fences f ON f.tenant_id=@tenant AND f.aggregate_id=a.id ORDER BY a.i)
                """, crm);
            command.Parameters.AddWithValue("tenant", h.Tenant);
            command.Parameters.AddWithValue("aggregates", aggregates);
            return (int[])(await command.ExecuteScalarAsync())!;
        }

        async Task SetSealLogin(bool enabled)
        {
            using var quote = new NpgsqlCommandBuilder();
            await using var command = new NpgsqlCommand($"ALTER ROLE {quote.QuoteIdentifier(seal.Username!)} {(enabled ? "LOGIN" : "NOLOGIN")}", crm);
            await command.ExecuteNonQueryAsync();
        }

        async Task<int> SealDenials()
        {
            var log = Environment.GetEnvironmentVariable("CP6_W673_CRM_SERVER_LOG")!;
            await using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var count = 0;
            while (await reader.ReadLineAsync() is { } row)
                if (row.Contains($"role \"{seal.Username}\" is not permitted to log in", StringComparison.Ordinal))
                    count++;
            return count;
        }

        async Task<string?> Sign(string bearer, Guid user, string purpose)
        {
            var nonce = ServiceNonce();
            using var request = new HttpRequestMessage(HttpMethod.Post, "connect/inquiry-human-context")
            {
                Content = JsonContent.Create(new InquiryHumanContextRequestV1(purpose, pair.SiteId, pair.FormId, nonce))
            };
            request.Headers.Authorization = new("Bearer", bearer);
            request.Headers.Add("X-Forwarded-Proto", "https");
            using var response = await api.Send(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var value = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
            Assert.True(ContextSignature(value, signing.PublicKeyBase64, InquiryPermissionSigningKeyV1.HumanContextVersion));
            Assert.Equal((h.Tenant, user, purpose, nonce), (value["organizationId"]!.GetValue<Guid>(), value["subjectId"]!.GetValue<Guid>(),
                value["purpose"]!.GetValue<string>(), value["requestNonce"]!.GetValue<string>()));
            return response.Headers.TryGetValues("X-CP6-Inquiry-Tenant-Fence", out var values) ? Assert.Single(values) : null;
        }

        Task Signal(string stage) => File.WriteAllTextAsync(Path.Combine(bridge, stage), "PASS");
        Task Save(string name, object evidence) => File.WriteAllTextAsync(Path.Combine(bridge, name), JsonSerializer.Serialize(evidence));
    }

    private sealed record SsoLinkEffects(Guid Epoch, string? Issuer, string? Subject, int UserVersion, long Outbox,
        int RevokedGrants, int LinkAudits, int Refresh);

    private sealed record SsoLinkExchange(string Reference, string Key, Func<Task<AuthenticationProofV1>> Callback);

    // As in the SSO admission fixture, only IdP discovery/exchange is simulated. The application validates the JWT itself.
    private sealed class SignedSsoExchange
    {
        internal const string Issuer = "https://w673-idp.example.invalid";
        internal const string Client = "cp6-w673";
        private const string Redirect = "https://cp6.test/api/auth/sso/callback";
        private readonly SsoService sso;
        private readonly SymmetricSecurityKey signingKey = new(RandomNumberGenerator.GetBytes(32));
        private readonly Dictionary<string, string> codes = [];
        private readonly string tenantCode;
        private readonly CP6Context db;
        private readonly TenantContext tenant;
        private readonly BCryptPasswordHasher hasher;
        private readonly ITenantSsoConfigService configuration;
        private readonly IOptions<SecurityOptions> options;
        internal int ExchangeCount { get; private set; }

        internal SignedSsoExchange(CP6Context db, TenantContext tenant, BCryptPasswordHasher hasher, string tenantCode)
        {
            this.tenantCode = tenantCode;
            this.db = db;
            this.tenant = tenant;
            this.hasher = hasher;
            options = Options.Create(new SecurityOptions());
            options.Value.Sso.PublicBaseUrl = "https://cp6.test";
            var config = new Mock<ITenantSsoConfigService>();
            config.Setup(x => x.GetByTenantCodeAsync(tenantCode)).Returns(() => db.Sys_TenantSsoConfigs.AsNoTracking().SingleOrDefaultAsync());
            config.Setup(x => x.DecryptClientSecret(It.IsAny<Sys_TenantSsoConfig>())).Returns("fixture-secret");
            configuration = config.Object;
            var discovery = new Mock<IOidcDiscovery>();
            discovery.Setup(x => x.GetAsync(Issuer)).ReturnsAsync(new OidcEndpoints(Issuer + "/authorize", Issuer + "/token", Issuer, [signingKey]));
            var tokens = new Mock<ISsoTokenClient>();
            tokens.Setup(x => x.ExchangeCodeForIdTokenAsync(It.IsAny<OidcEndpoints>(), It.IsAny<Sys_TenantSsoConfig>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), Redirect)).ReturnsAsync((OidcEndpoints _, Sys_TenantSsoConfig _,
                    string _, string code, string _, string _) =>
            {
                ExchangeCount++;
                return codes.Remove(code, out var token) ? token : null;
            });
            var state = new SsoStateStore(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), options);
            sso = new SsoService(config.Object, state, discovery.Object, tokens.Object, db, new SecurityAuditService(db), tenant,
                hasher, new AuthenticationPolicyServiceV1(db, options), options);
        }

        internal async Task<SsoLinkExchange> Start(string subject, string email, string? originalKey = null)
        {
            var url = await sso.BuildAuthorizeUrlAsync(tenantCode, Redirect, null);
            var query = QueryHelpers.ParseQuery(new Uri(url).Query);
            var state = query["state"].ToString();
            var key = originalKey ?? AuthenticationInteractionStoreV1.NewChallenge();
            var binding = AuthenticationEntryServiceV1.WebBinding(key);
            await sso.BindStateAsync(state, "Web", binding);
            var code = Issue(query["nonce"].ToString(), subject, email);
            return new(AuthenticationInteractionStoreV1.Hash(state), key,
                () => sso.HandleVerifiedCallbackAsync(code, state, Redirect, "Web", binding));
        }

        internal async Task<(string Reference, string AccessJti)> ResumeThroughWeb(string subject, string email, string key, int port)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", Args = [] });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, port));
            builder.Configuration["JWT:Secret"] = CrmOidcCrypto.RandomToken() + CrmOidcCrypto.RandomToken();
            builder.Configuration["JWT:Issuer"] = "w673-sso-component";
            builder.Configuration["JWT:Audience"] = "w673-sso-component";
            builder.Services.AddAuthorization();
            builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly).AddControllersAsServices();
            var policies = new AuthenticationPolicyServiceV1(db, options);
            var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            var refresh = new RefreshTokenService(db, options, tenant);
            var audit = new SecurityAuditService(db);
            var login = new LoginSecurityService(db, options);
            var factors = new TwoFactorService(db, new TotpService(options), new LogEmailSender(NullLogger<LogEmailSender>.Instance), cache, audit, options);
            var permission = new Mock<ICurrentPermissionContext>();
            permission.Setup(x => x.PrewarmAsync(It.IsAny<Guid>())).Returns((Guid user) => new PermissionAggregator(db).BuildAsync(user));
            var sessions = new AuthSessionService(builder.Configuration, permission.Object, db, options);
            builder.Services.AddSingleton(policies);
            builder.Services.AddSingleton<IRefreshTokenService>(refresh);
            builder.Services.AddSingleton(new AuthenticationEntryServiceV1(db, tenant, new(db, policies), new(db),
                sessions, refresh, factors, login, Mock.Of<IClientDeviceService>()));
            builder.Services.AddTransient<AuthController>(_ => new(db, builder.Configuration, permission.Object, tenant, hasher,
                new PasswordPolicyService(db, options, hasher), login, audit, refresh, new CacheTokenBlacklistService(cache),
                new AuthCookieWriter(options), options, configuration, sso, factors, new PendingTokenStore(cache, options), sessions));
            await using var app = builder.Build();
            app.MapControllers();
            await app.StartAsync();
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
                { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            using var authorize = new HttpRequestMessage(HttpMethod.Get, "api/auth/sso/authorize?tenantCode=" + Uri.EscapeDataString(tenantCode));
            authorize.Headers.Add("X-CP6-Authentication-Completion", key);
            using var authorized = await client.SendAsync(authorize);
            Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
            var cookie = Assert.Single(authorized.Headers.GetValues("Set-Cookie"), value => value.StartsWith("cp6_sso=", StringComparison.Ordinal));
            Assert.StartsWith("cp6_sso=" + key + ";", cookie, StringComparison.Ordinal);
            var discoveryUrl = (await authorized.Content.ReadFromJsonAsync<JsonObject>())!["authorizeUrl"]!.GetValue<string>();
            var query = QueryHelpers.ParseQuery(new Uri(discoveryUrl).Query);
            var state = query["state"].ToString();
            var code = Issue(query["nonce"].ToString(), subject, email);
            using var callback = new HttpRequestMessage(HttpMethod.Get, $"api/auth/sso/callback?code={code}&state={Uri.EscapeDataString(state)}");
            callback.Headers.Add("Cookie", cookie.Split(';', 2)[0]);
            using var resumed = await client.SendAsync(callback);
            Assert.Equal(HttpStatusCode.Redirect, resumed.StatusCode);
            Assert.EndsWith("/sso/landing", resumed.Headers.Location!.AbsoluteUri, StringComparison.Ordinal);
            var access = Assert.Single(resumed.Headers.GetValues("Set-Cookie"), value => value.StartsWith("cp6_at=", StringComparison.Ordinal));
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(access.Split(';', 2)[0]["cp6_at=".Length..]);
            await app.StopAsync();
            return (AuthenticationInteractionStoreV1.Hash(state), jwt.Id);
        }

        private string Issue(string nonce, string subject, string email)
        {
            var code = Guid.NewGuid().ToString("N");
            var jwt = new JwtSecurityToken(Issuer, Client,
                [new Claim("sub", subject), new Claim("email", email), new Claim("nonce", nonce), new Claim("email_verified", "true")],
                expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
            codes.Add(code, new JwtSecurityTokenHandler().WriteToken(jwt));
            return code;
        }
    }
}

internal sealed class Pgt017SsoLinkFactAttribute : FactAttribute
{
    public Pgt017SsoLinkFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CP6_W673_SCENARIO")))
            Skip = "Requires the isolated W673 candidate Main and CRM PostgreSQL fixture.";
    }
}
