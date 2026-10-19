using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Client;
using CP6.WebApi.Controllers;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace CP6.Tests.Platform;

[Collection("TenantManagementPostgres")]
public sealed class AuthenticationSsoPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Web_SSO_requires_original_browser_binding_then_returns_pending_without_auth_cookies()
    {
        await using var h = await Harness.Create(server, 2, false, false);
        var start = Assert.IsType<OkObjectResult>(await h.Web.SsoAuthorize(h.Organization.TenantCode));
        var url = (string)start.Value!.GetType().GetProperty("authorizeUrl")!.GetValue(start.Value)!;
        var query = HttpUtility.ParseQueryString(new Uri(url).Query); h.Nonce = query["nonce"]!;
        var browser = h.Web.Response.Headers.SetCookie.Single(x => x!.StartsWith("cp6_sso="))!.Split(';')[0];
        Assert.Contains("httponly", h.Web.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
        h.Web.Response.Headers.Clear(); h.Web.Request.Headers.Cookie = "cp6_sso=wrong-browser";
        var denied = Assert.IsType<RedirectResult>(await h.Web.SsoCallback("code", query["state"]!));
        Assert.Contains("error=E-SEC-022", denied.Url); Assert.Equal(0, h.ExchangeCount);
        h.Web.Response.Headers.Clear(); h.Web.Request.Headers.Cookie = browser;
        var pending = Assert.IsType<RedirectResult>(await h.Web.SsoCallback("code", query["state"]!));
        Assert.EndsWith("?challenge=2fa_enroll", pending.Url);
        var cookies = h.Web.Response.Headers.SetCookie.Select(x => x!.Split(';')[0].Split('=', 2)).GroupBy(x => x[0])
            .ToDictionary(x => x.Key, x => x.Last()[1]);
        Assert.NotEmpty(cookies["cp6_2fa"]); Assert.Empty(cookies["cp6_at"]); Assert.Empty(cookies["cp6_rt"]);
        Assert.False(await h.Db.Sys_RefreshTokens.AnyAsync(x => x.UserId == h.User.Id));
        Assert.False(await h.Db.Sys_SecurityLogs.AnyAsync(x => x.UserId == h.User.Id && x.EventType == (int)SecurityEventType.SsoLoginSuccess));
    }

    [TenantManagementPostgresTheory]
    [InlineData(0, false, false)]
    [InlineData(2, false, false)]
    [InlineData(0, true, false)]
    [InlineData(0, false, true)]
    public async Task Native_SSO_real_signature_and_original_proof_obey_local_factor_and_remediation(int mode, bool enabledFactor, bool remediation)
    {
        await using var h = await Harness.Create(server, mode, enabledFactor, remediation);
        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "sso-device", AppVersion = "1.0", PlatformVersion = "test" };
        var verifier = new string('v', 43);
        var started = (await h.Controller.SsoStart(new NativeSsoStartRequest { TenantCode = h.Organization.TenantCode,
            RedirectUri = "cp6://sso/callback", Client = client,
            CodeChallenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) })).Value!;
        var query = HttpUtility.ParseQueryString(new Uri(started.AuthorizeUrl).Query);
        h.Nonce = query["nonce"]!;
        var callback = Assert.IsType<RedirectResult>(await h.Controller.SsoCallback("code", query["state"]!));
        Assert.DoesNotContain("error=", callback.Url!);
        Assert.False(await h.Db.Sys_SecurityLogs.AnyAsync(x => x.UserId == h.User.Id && x.EventType == (int)SecurityEventType.SsoLoginSuccess));
        var grant = HttpUtility.ParseQueryString(new Uri(callback.Url!).Query)["grantCode"]!;
        var request = new NativeSsoExchangeRequest { GrantCode = grant, CodeVerifier = verifier, Client = client };
        if (remediation)
        {
            var error = await Assert.ThrowsAsync<CP6.WebApi.Localization.BizException>(() => h.Controller.SsoExchange(request));
            Assert.Equal("ACCOUNT_REMEDIATION_REQUIRED", error.Code); Assert.False(await h.Db.Sys_RefreshTokens.AnyAsync(x => x.UserId == h.User.Id));
            return;
        }
        var exchanged = (await h.Controller.SsoExchange(request)).Value!;
        if (mode == 2 || enabledFactor)
        {
            Assert.Null(exchanged.Session);
            Assert.Equal(enabledFactor ? "twoFactorRequired" : "enrollmentRequired", exchanged.State);
            Assert.False(await h.Db.Sys_RefreshTokens.AnyAsync(x => x.UserId == h.User.Id));
            if (!enabledFactor) await h.Controller.TwoFactorSetup(new NativeChallengeRequest { ChallengeToken = exchanged.ChallengeToken!, Client = client });
            var secret = (await h.Db.Sys_Users.AsNoTracking().SingleAsync(x => x.Id == h.User.Id)).TwoFactorSecret!;
            var factor = new NativeTwoFactorRequest { ChallengeToken = exchanged.ChallengeToken!, Client = client, Method = "totp",
                Code = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(secret)).ComputeTotp() };
            exchanged = (enabledFactor ? await h.Controller.TwoFactorVerify(factor) : await h.Controller.TwoFactorEnroll(factor)).Value!;
        }
        Assert.Equal("authenticated", exchanged.State); Assert.NotNull(exchanged.Session);
        Assert.False(exchanged.Session.Profile.MustChangePassword); // SSO placeholder password is deliberately expired.
        var receipt = await new AuthenticationInteractionStoreV1(h.Db).ReadSessionAsync(exchanged.Session.AccessToken);
        Assert.NotNull(receipt); var evidence = receipt.Parse();
        Assert.Equal(AuthenticationPrimaryMethod.Sso, evidence.Primary.PrimaryMethod);
        Assert.Equal(AuthenticationEntryServiceV1.Binding(client), evidence.Primary.ClientBinding);
        Assert.NotNull(evidence.Primary.SsoConfigurationVersion);
        Assert.Equal(enabledFactor || mode == 2, evidence.Factor != null);
        // The controller seam stops before MVC; confirm its pending output through the actual Owner adapter.
        await h.Refresh.CompleteOutputAsync(exchanged.Session.RefreshToken);
        var (rotated, _) = await h.Refresh.RotateAsync(exchanged.Session.RefreshToken, null, null,
            new RefreshTokenClientContext(client.ClientKind, client.DeviceId, client.AppVersion));
        var carried = (await new AuthenticationInteractionStoreV1(h.Db).ReadSessionByRefreshAsync(AuthenticationInteractionStoreV1.Hash(rotated)))!.Parse();
        Assert.Equal(evidence, carried); // Expired placeholder password never replaces the original SSO/factor proof.
        var recovery = new NativeCompletionRecoveryRequest { Kind = "native-grant", Credential = grant, CodeVerifier = verifier, Client = client };
        Assert.Equal("COMPLETED", (await h.Controller.CompletionStatus(recovery)).Value!.State);
        Assert.Equal("CLOSED", (await h.Controller.CloseCompletion(recovery)).Value!.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Refresh.RotateAsync(rotated, null, null,
            new RefreshTokenClientContext(client.ClientKind, client.DeviceId, client.AppVersion)));
        await Assert.ThrowsAsync<CP6.WebApi.Localization.BizException>(() => h.Controller.SsoExchange(request));
        Assert.Equal(1, h.ExchangeCount);
    }

    [TenantManagementPostgresTheory]
    [InlineData("policy")]
    [InlineData("signature")]
    [InlineData("unbound")]
    [InlineData("replay")]
    [InlineData("binding")]
    public async Task SSO_state_and_identity_admission_fail_closed(string change)
    {
        await using var h = await Harness.Create(server, 0, false, false);
        var url = await h.Sso.BuildAuthorizeUrlAsync(h.Organization.TenantCode, "https://cp6.test/callback", null);
        var query = HttpUtility.ParseQueryString(new Uri(url).Query); h.Nonce = query["nonce"]!;
        if (change == "policy") await h.Db.Sys_TenantSsoConfigs.Where(x => x.TenantId == h.User.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Scopes, "openid email changed"));
        if (change == "signature") h.InvalidSignature = true;
        if (change == "unbound") await h.Db.Sys_Users.Where(x => x.Id == h.User.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExternalSubject, (string?)null));
        var challenge = AuthenticationInteractionStoreV1.NewChallenge();
        await h.Sso.BindStateAsync(query["state"]!, "Web", AuthenticationEntryServiceV1.WebBinding(challenge));
        if (change == "binding") challenge = AuthenticationInteractionStoreV1.NewChallenge();
        Task<AuthenticationProofV1> Callback() => h.Sso.HandleVerifiedCallbackAsync("code", query["state"]!, "https://cp6.test/callback", "Web", AuthenticationEntryServiceV1.WebBinding(challenge));
        if (change == "replay")
        {
            Assert.Equal(AuthenticationPrimaryMethod.Sso, (await Callback()).PrimaryMethod);
            Assert.Equal("E-SEC-022", (await Assert.ThrowsAsync<InvalidOperationException>(Callback)).Message);
        }
        else
        {
            var expected = change switch { "policy" => "REAUTH_REQUIRED", "signature" => "E-SEC-024", "binding" => "E-SEC-022", _ => "SSO_IDENTITY_POLICY_UNAVAILABLE" };
            Assert.Equal(expected, (await Assert.ThrowsAsync<InvalidOperationException>(Callback)).Message);
        }
        Assert.False(await h.Db.Sys_RefreshTokens.AnyAsync(x => x.UserId == h.User.Id));
        Assert.Equal(change is "policy" or "binding" ? 0 : 1, h.ExchangeCount);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required CP6Context Db;
        public required Sys_User User;
        public required Sys_Tenant Organization;
        public required SsoService Sso;
        public required RefreshTokenService Refresh;
        public required ClientAuthController Controller;
        public required CP6.WebApi.Controllers.Sys.AuthController Web;
        public string Nonce = "";
        public int ExchangeCount;
        public bool InvalidSignature;
        private ServiceProvider? provider;
        private MemoryDistributedCache? cache;

        public static async Task<Harness> Create(TenantManagementPostgresFixture server, int mode, bool factor, bool remediation)
        {
            var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
            var db = server.Context(tenant);
            var organization = new Sys_Tenant { Id = tenant.CurrentTenantId, TenantCode = "sso_" + Guid.NewGuid().ToString("N"), TenantName = "SSO", TwoFactorMode = mode };
            var user = new Sys_User { TenantId = organization.Id, UserName = "sso-user", Email = "user@example.test", Enable = true,
                Password = "unusable-placeholder", PasswordChangedAt = DateTime.Now.AddDays(-365), MustChangePassword = remediation,
                ExternalProvider = "https://issuer.example.test", ExternalSubject = "bound-subject", TwoFactorEnabled = factor,
                TwoFactorSecret = factor ? OtpNet.Base32Encoding.ToString(OtpNet.KeyGeneration.GenerateRandomKey(20)) : null };
            var cfg = new Sys_TenantSsoConfig { TenantId = organization.Id, Authority = user.ExternalProvider, ClientId = "cp6", Enabled = true,
                Enforced = true, AutoProvision = true, EmailClaim = "email", Scopes = "openid email", ClientSecretProtected = "fixture" };
            db.Sys_Tenants.Add(organization); db.Sys_Users.Add(user); db.Sys_TenantSsoConfigs.Add(cfg); await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var options = Options.Create(new SecurityOptions()); options.Value.Password.ExpiryDays = 1;
            options.Value.NativeClient.AllowedRedirectUris = ["cp6://sso/callback"];
            var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            var state = new SsoStateStore(cache, options); var policies = new AuthenticationPolicyServiceV1(db, options);
            var configs = new Mock<ITenantSsoConfigService>();
            configs.Setup(x => x.GetByTenantCodeAsync(It.IsAny<string>())).Returns(() => db.Sys_TenantSsoConfigs.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == organization.Id));
            configs.Setup(x => x.GetByTenantIdAsync(organization.Id)).Returns(() => db.Sys_TenantSsoConfigs.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == organization.Id));
            configs.Setup(x => x.DecryptClientSecret(It.IsAny<Sys_TenantSsoConfig>())).Returns("test-secret");
            var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
            var discovery = new Mock<IOidcDiscovery>();
            discovery.Setup(x => x.GetAsync(cfg.Authority)).ReturnsAsync(new OidcEndpoints(cfg.Authority + "/authorize", cfg.Authority + "/token", cfg.Authority, new List<SecurityKey> { key }));
            var tokens = new Mock<ISsoTokenClient>();
            var audit = new SecurityAuditService(db); var hasher = new BCryptPasswordHasher(4);
            var sso = new SsoService(configs.Object, state, discovery.Object, tokens.Object, db, audit, tenant, hasher, policies, options);
            var twoFactor = new TwoFactorService(db, new TotpService(options), new LogEmailSender(NullLogger<LogEmailSender>.Instance), cache, audit, options);
            var refresh = new RefreshTokenService(db, options, tenant); var login = new LoginSecurityService(db, options);
            var sessions = new Mock<IAuthSessionService>();
            sessions.Setup(x => x.BuildAccessToken(It.IsAny<Sys_User>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>())).Returns<Sys_User,string,bool,bool>((_, jti, _, _) => jti);
            sessions.Setup(x => x.BuildProfileAsync(It.IsAny<Sys_User>(), It.IsAny<bool>())).Returns<Sys_User,bool>((u, restricted) => Task.FromResult(new ClientProfileDto { UserName = u.UserName, MustChangePassword = restricted }));
            sessions.SetupGet(x => x.AccessExpiresAt).Returns(() => DateTimeOffset.UtcNow.AddMinutes(15));
            sessions.SetupGet(x => x.RefreshExpiresAt).Returns(() => DateTimeOffset.UtcNow.AddDays(7));
            var devices = Mock.Of<IClientDeviceService>();
            var entry = new AuthenticationEntryServiceV1(db, tenant, new(db, policies), new(db), sessions.Object, refresh, twoFactor, login, devices);
            var services = new ServiceCollection().AddSingleton(entry).AddSingleton(policies).BuildServiceProvider();
            var controller = new ClientAuthController(db, tenant, hasher, new PasswordPolicyService(db, options, hasher), login, audit, refresh,
                new CacheTokenBlacklistService(cache), configs.Object, sso, state, new NativeSsoGrantStore(new MemoryNativeSsoGrantCache(), options),
                twoFactor, new PendingTokenStore(cache, options), sessions.Object, devices, options)
                { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } } };
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["JWT:Secret"] = "fixture-only-not-a-production-secret-123456789", ["JWT:Issuer"] = "cp6", ["JWT:Audience"] = "cp6" }).Build();
            var web = new CP6.WebApi.Controllers.Sys.AuthController(db, config, Mock.Of<ICurrentPermissionContext>(), tenant, hasher,
                new PasswordPolicyService(db, options, hasher), login, audit, refresh, new CacheTokenBlacklistService(cache), new AuthCookieWriter(options),
                options, configs.Object, sso, twoFactor, new PendingTokenStore(cache, options), sessions.Object)
                { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } } };
            var h = new Harness { Db = db, User = user, Organization = organization, Sso = sso, Refresh = refresh, Controller = controller, Web = web, provider = services, cache = cache };
            tokens.Setup(x => x.ExchangeCodeForIdTokenAsync(It.IsAny<OidcEndpoints>(), It.IsAny<Sys_TenantSsoConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(() =>
                {
                    h.ExchangeCount++;
                    var jwt = new JwtSecurityToken(cfg.Authority, cfg.ClientId, new[] { new Claim("sub", "bound-subject"), new Claim("email", user.Email!),
                        new Claim("email_verified", "true"), new Claim("nonce", h.Nonce) }, expires: DateTime.UtcNow.AddMinutes(5),
                        signingCredentials: new SigningCredentials(h.InvalidSignature ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) : key, SecurityAlgorithms.HmacSha256));
                    return Task.FromResult<string?>(new JwtSecurityTokenHandler().WriteToken(jwt));
                });
            return h;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); provider?.Dispose(); }
    }
}
