using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Controllers;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Middleware;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using OtpNet;

namespace CP6.Tests.Sys;

[CollectionDefinition("PasswordReloginHttp", DisableParallelization = true)]
public sealed class PasswordReloginHttpCollection;

[Collection("PasswordReloginHttp")]
public class PasswordChangeReloginHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Change_password_clears_final_jar_and_new_password_relogs(bool administrator)
    {
        await using var host = await Host.Start(administrator);
        Assert.Equal(HttpStatusCode.OK, (await host.Login()).StatusCode);
        var oldRefresh = host.Cookie(AuthCookieWriter.RefreshCookie)!;
        Assert.NotNull(host.Cookie(AuthCookieWriter.AccessCookie));
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/auth/profile")).StatusCode);
        var changed = await host.Post("/api/auth/change-password", new { currentPassword = Host.Password, newPassword = Host.NewPassword });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Empty(host.Jar.GetAllCookies().Cast<Cookie>().Select(c => c.Name).Where(n => n.StartsWith("cp6_")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/auth/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Login(password: Host.NewPassword)).StatusCode);
        var profile = await (await host.Client.GetAsync("/api/auth/profile")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(profile.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(administrator, profile.GetProperty("isPlatformAdmin").GetBoolean());
        using var scope = host.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(oldRefresh)));
        Assert.NotNull((await db.Sys_RefreshTokens.SingleAsync(t => t.TokenHash == tokenHash)).RevokedAt);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/API/AUTH/LOGIN/")]
    public async Task Old_cookie_allows_exact_relogin_and_binds_new_tenant(string path)
    {
        await using var host = await Host.Start();
        await host.Login();
        var response = await host.Login("other", path: path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(host.Cookie(AuthCookieWriter.AccessCookie));
        Assert.Equal(host.OtherTenant.ToString(), token.Claims.Single(c => c.Type == "tenant_id").Value);
        Assert.Equal("other", (await (await host.Client.GetAsync("/api/auth/profile")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userName").GetString());
    }

    [Fact]
    public async Task Old_cookie_language_reads_recover_but_management_and_other_routes_stay_blocked()
    {
        await using var host = await Host.Start();
        await host.Login();
        foreach (var path in new[] { "/api/lang/en", "/api/lang/en/ns/auth", "/api/lang/manifest", "/api/lang/published/missing/en" })
        {
            var response = await host.Client.GetAsync(path);
            Assert.DoesNotContain("E-SEC-009", await response.Content.ReadAsStringAsync());
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });
        }
        foreach (var path in new[] { "/api/lang", "/api/auth/profile", "/api/auth/login", "/api/auth/2fa/status", "/api/auth/sso/authorize", "/api/order" })
            Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync(path)).StatusCode);
        foreach (var path in new[] { "/api/auth/login-extra", "/api/auth/login/extra", "/api/auth/refresh", "/api/lang/review", "/api/lang/publish", "/api/auth/2fa/setup-self" })
        {
            var response = await host.Post(path, new { });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("E-SEC-009", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await host.Login()).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/auth/profile")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Valid_2fa_transition_replaces_old_session_and_preserves_new_pending_jar(bool enroll)
    {
        await using var host = await Host.Start(enroll: enroll);
        await host.Login();
        var response = await host.Login("factor");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("twoFactorRequired").GetBoolean());
        Assert.Null(host.Cookie(AuthCookieWriter.AccessCookie));
        Assert.Null(host.Cookie(AuthCookieWriter.RefreshCookie));
        var pending = host.Cookie(AuthCookieWriter.PendingCookie)!;
        Assert.NotNull(pending);
        Assert.NotNull(host.Cookie(AuthCookieWriter.CsrfCookie));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/auth/profile")).StatusCode);
        var route = enroll ? "/api/auth/2fa/enroll" : "/api/auth/2fa/verify";
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Post(route, new { code = "invalid" }, csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Post(route, new { code = "invalid" }, badCsrf: true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post(route, new { code = "invalid" })).StatusCode);
        using (var scope = host.App.Services.CreateScope())
        {
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(pending);
            var expired = JwtHelper.GeneratePendingToken("unused", host.OtherTenant, enroll ? "2fa_enroll" : "2fa_verify",
                jwt.Id, config["JWT:Secret"]!, "test", "test", -10);
            foreach (var rejected in new[] { "invalid", expired })
            {
                host.Jar.Add(host.Client.BaseAddress!, new Cookie(AuthCookieWriter.PendingCookie, rejected, "/api/auth"));
                var denied = await host.Post(route, new { code = "000000" });
                Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
                Assert.Contains("E-SEC-013", await denied.Content.ReadAsStringAsync());
                Assert.Null(host.Cookie(AuthCookieWriter.AccessCookie));
            }
            host.Jar.Add(host.Client.BaseAddress!, new Cookie(AuthCookieWriter.PendingCookie, pending, "/api/auth"));
        }
        var secret = Host.TotpSecret;
        if (enroll)
        {
            Assert.Equal(HttpStatusCode.OK, (await host.Post("/api/auth/2fa/setup", new { })).StatusCode);
            using var scope = host.App.Services.CreateScope();
            secret = (await scope.ServiceProvider.GetRequiredService<CP6Context>().Sys_Users.IgnoreQueryFilters().SingleAsync(u => u.UserName == "factor")).TwoFactorSecret!;
        }
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();
        Assert.Equal(HttpStatusCode.OK, (await host.Post(route, new { code })).StatusCode);
        Assert.NotNull(host.Cookie(AuthCookieWriter.AccessCookie));
        Assert.NotNull(host.Cookie(AuthCookieWriter.RefreshCookie));
        Assert.Null(host.Cookie(AuthCookieWriter.PendingCookie));
        host.Jar.Add(host.Client.BaseAddress!, new Cookie(AuthCookieWriter.PendingCookie, pending, "/api/auth"));
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Post(route, new { code })).StatusCode);
    }

    [Theory]
    [InlineData("wrong-password")]
    [InlineData("weak-password")]
    [InlineData("save-failure")]
    [InlineData("csrf-missing")]
    [InlineData("csrf-wrong")]
    public async Task Failed_change_does_not_clear_or_modify_current_session(string failure)
    {
        await using var host = await Host.Start();
        await host.Login();
        var before = host.Cookie(AuthCookieWriter.AccessCookie);
        host.Guard.Fail = failure == "save-failure";
        var response = await host.Post("/api/auth/change-password", new {
            currentPassword = failure == "wrong-password" ? "bad" : Host.Password,
            newPassword = failure == "weak-password" ? "weak" : Host.NewPassword },
            csrf: failure != "csrf-missing", badCsrf: failure == "csrf-wrong");
        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(before, host.Cookie(AuthCookieWriter.AccessCookie));
        Assert.NotNull(host.Cookie(AuthCookieWriter.RefreshCookie));
        using var scope = host.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
        Assert.True((await db.Sys_Users.SingleAsync(u => u.UserName == "user")).MustChangePassword);
        Assert.Empty(await db.Sys_PasswordHistories.ToListAsync());
        Assert.All(await db.Sys_RefreshTokens.ToListAsync(), t => Assert.Null(t.RevokedAt));
    }

    [Fact]
    public async Task Invalid_relogin_credentials_do_not_clear_an_existing_session()
    {
        await using var host = await Host.Start();
        await host.Login();
        var before = host.Cookie(AuthCookieWriter.AccessCookie);
        var response = await host.Login("factor", password: "wrong");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, host.Cookie(AuthCookieWriter.AccessCookie));
        Assert.Null(host.Cookie(AuthCookieWriter.PendingCookie));
    }

    private sealed class SaveGuard : SaveChangesInterceptor
    {
        public bool Fail;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => Fail ? throw new InvalidOperationException("Injected save failure") : ValueTask.FromResult(result);
    }
    private sealed class OnlyAuth : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { feature.Controllers.Clear(); feature.Controllers.Add(typeof(AuthController).GetTypeInfo()); feature.Controllers.Add(typeof(LangController).GetTypeInfo()); }
    }
    private sealed class Host : IAsyncDisposable
    {
        public const string Password = "Temporary1!", NewPassword = "Changed2!", TotpSecret = "JBSWY3DPEHPK3PXP";
        public WebApplication App = null!;
        public HttpClient Client = null!;
        public CookieContainer Jar = new();
        public SaveGuard Guard = new();
        public Guid OtherTenant = Guid.NewGuid();
        public string? Cookie(string name) => Jar.GetCookies(new Uri(Client.BaseAddress!, "/api/auth/login"))[name]?.Value;
        public Task<HttpResponseMessage> Login(string name = "user", string? password = null, string path = "/api/auth/login")
            => Client.PostAsJsonAsync(path, new { userName = name, password = password ?? Password, tenantCode = name == "user" ? "DEFAULT" : "OTHER" });
        public async Task<HttpResponseMessage> Post(string path, object value, bool csrf = true, bool badCsrf = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(value) };
            if (csrf) request.Headers.Add("X-CSRF-Token", badCsrf ? "wrong" : Cookie(AuthCookieWriter.CsrfCookie));
            return await Client.SendAsync(request);
        }
        public static async Task<Host> Start(bool administrator = false, bool enroll = false)
        {
            var host = new Host();
            var options = new DbContextOptionsBuilder<CP6Context>().UseInMemoryDatabase("relogin-" + Guid.NewGuid())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).AddInterceptors(host.Guard).Options;
            using (var db = new CP6Context(options))
            {
                db.Sys_Tenants.AddRange(new Sys_Tenant { Id = TenantContext.DefaultTenant, TenantCode = "DEFAULT", TenantName = "Default" },
                    new Sys_Tenant { Id = host.OtherTenant, TenantCode = "OTHER", TenantName = "Other", TwoFactorMode = enroll ? 2 : 1 });
                db.Sys_Roles.Add(new Sys_Role { RoleId = 1, RoleName = "administrator" });
                db.Sys_Users.Add(new Sys_User { UserName = "user", Password = new BCryptPasswordHasher(4).Hash(Password), MustChangePassword = true, IsPlatformAdmin = administrator, RoleId = administrator ? 1 : null });
                await db.SaveChangesAsync();
            }
            using (var db = new CP6Context(options, new TenantContext { CurrentTenantId = host.OtherTenant }))
            {
                db.Sys_Users.AddRange(new Sys_User { UserName = "other", Password = new BCryptPasswordHasher(4).Hash(Password) },
                    new Sys_User { UserName = "factor", Password = new BCryptPasswordHasher(4).Hash(Password), TwoFactorEnabled = !enroll, TwoFactorSecret = enroll ? null : TotpSecret });
                await db.SaveChangesAsync();
            }
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "PasswordReloginTests", Args = [] });
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:18238");
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["JWT:Secret"] = secret, ["JWT:Issuer"] = "test", ["JWT:Audience"] = "test", ["CrmOidc:Enabled"] = "false" });
            var services = builder.Services;
            services.Configure<SecurityOptions>(s => { s.Cookie.Secure = false; s.Csrf.Enabled = true; s.Password.BcryptWorkFactor = 4; });
            services.AddScoped<ITenantContext, TenantContext>();
            services.AddScoped(sp => new CP6Context(options, sp.GetRequiredService<ITenantContext>()));
            services.AddHttpContextAccessor(); services.AddDistributedMemoryCache();
            services.AddScoped<IPermissionAggregator, PermissionAggregator>(); services.AddScoped<ICurrentPermissionContext, CurrentPermissionContext>();
            services.AddScoped<IPasswordHasher>(_ => new BCryptPasswordHasher(4)); services.AddScoped<IPasswordPolicyService, PasswordPolicyService>();
            services.AddScoped<ILoginSecurityService, LoginSecurityService>(); services.AddScoped<ISecurityAuditService, SecurityAuditService>();
            services.AddScoped<IRefreshTokenService, RefreshTokenService>(); services.AddScoped<ITokenBlacklistService, CacheTokenBlacklistService>();
            services.AddScoped<IAuthCookieWriter, AuthCookieWriter>(); services.AddScoped<IAuthSessionService, AuthSessionService>();
            services.AddScoped<ITenantSsoConfigService, FakeTenantSsoConfigService>(); services.AddScoped<ISsoService, FakeSsoService>();
            services.AddScoped<ITotpService, TotpService>(); services.AddScoped<IEmailSender, LogEmailSender>();
            services.AddScoped<IPendingTokenStore, PendingTokenStore>(); services.AddScoped<ITwoFactorService, TwoFactorService>();
            services.AddSingleton<IStringLocalizer>(EchoStringLocalizer.Instance);
            var files = new Moq.Mock<IFileStore>();
            files.Setup(f => f.OpenReadAsync(Moq.It.IsAny<string>())).ThrowsAsync(new FileNotFoundException());
            services.AddSingleton<CacheService>(); services.AddSingleton<IFileStore>(files.Object); services.AddSingleton<LangPublishService>();
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => {
                o.TokenValidationParameters = new TokenValidationParameters { ValidIssuer = "test", ValidAudience = "test", IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) };
                o.Events = new JwtBearerEvents { OnMessageReceived = c => { c.Token = c.Request.Cookies[AuthCookieWriter.AccessCookie]; return Task.CompletedTask; },
                    OnTokenValidated = async c => { if (await c.HttpContext.RequestServices.GetRequiredService<ITokenBlacklistService>().IsBlacklistedAsync(c.Principal!.FindFirst("jti")!.Value)) c.Fail("revoked"); } };
            });
            services.AddAuthorization();
            services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyAuth()));
            host.App = builder.Build();
            host.App.Use(async (ctx, next) => { try { await next(); } catch (InvalidOperationException) { ctx.Response.StatusCode = 500; } });
            host.App.UseRouting(); host.App.UseAuthentication(); host.App.UseMiddleware<TenantMiddleware>();
            host.App.UseMiddleware<BizExceptionMiddleware>(); host.App.UseMiddleware<CsrfMiddleware>(); host.App.UseMiddleware<MustChangePasswordMiddleware>();
            host.App.UseAuthorization(); host.App.MapControllers();
            try { await host.App.StartAsync(); }
            catch { await host.App.DisposeAsync(); throw; }
            host.Client = new HttpClient(new HttpClientHandler { CookieContainer = host.Jar }) { BaseAddress = new Uri("http://127.0.0.1:18238"), Timeout = TimeSpan.FromSeconds(20) };
            return host;
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await App.StopAsync(); await App.DisposeAsync(); }
    }
}
