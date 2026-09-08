using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace CP6.Tests.Sys;

public class CrmOidcBridgeTests
{
    [Fact]
    public void Full_session_token_records_issuance_for_password_revocation_checks()
    {
        var raw = JwtHelper.GenerateToken(Guid.NewGuid().ToString(), "user",
            new string('s', 64), "cp6", "legacy", 15);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(raw);
        Assert.Contains(token.Claims, claim => claim.Type == "iat");
        Assert.Equal("HS256", token.Header.Alg);
    }

    [Fact]
    public void Key_ring_signs_real_RS256_and_rejects_wrong_audience_type_key_and_algorithm()
    {
        using var f = new Fixture();
        var jwt = f.Crypto.Sign("CP6.Web", [new Claim("sub", f.User.Id.ToString())], DateTimeOffset.UtcNow.AddMinutes(1), "at+jwt");
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        Assert.Equal("RS256", token.Header.Alg);
        Assert.Equal("current", token.Header.Kid);
        Assert.Equal(f.User.Id.ToString(), f.Crypto.Validate(jwt, "CP6.Web", "at+jwt").FindFirst("sub")!.Value);
        Assert.ThrowsAny<SecurityTokenException>(() => f.Crypto.Validate(jwt, "CP6.Services", "at+jwt"));
        Assert.ThrowsAny<SecurityTokenException>(() => f.Crypto.Validate(jwt, "CP6.Web", "JWT"));
        using var other = new Fixture();
        Assert.ThrowsAny<SecurityTokenException>(() => other.Crypto.Validate(jwt, "CP6.Web", "at+jwt"));
        var legacy = JwtHelper.GenerateToken(f.User.Id.ToString(), "u", new string('s', 64), "https://cp6.example", "CP6.Web", 1);
        Assert.ThrowsAny<SecurityTokenException>(() => f.Crypto.Validate(legacy, "CP6.Web", "at+jwt"));
        using var jwks = JsonDocument.Parse(JsonSerializer.Serialize(f.Crypto.Jwks()));
        var key = jwks.RootElement.GetProperty("keys")[0];
        Assert.Equal("RS256", key.GetProperty("alg").GetString());
        Assert.False(key.TryGetProperty("d", out _));
        Assert.False(key.TryGetProperty("p", out _));
    }

    [Theory]
    [InlineData("https://cp6.example", false, false, true)]
    [InlineData("http://localhost:5173", true, true, true)]
    [InlineData("http://localhost:5173", true, false, false)]
    [InlineData("https://cp6.example/path", false, false, false)]
    [InlineData("https://cp6.example/", false, false, false)]
    public void Enabled_configuration_requires_explicit_trusted_origin(string issuer, bool allowLoopback, bool development, bool valid)
    {
        using var f = new Fixture();
        f.Options.Issuer = issuer;
        f.Options.AllowInsecureLoopback = allowLoopback;
        if (valid) f.Options.Validate(development);
        else Assert.Throws<InvalidOperationException>(() => f.Options.Validate(development));
    }

    [Fact]
    public async Task Disabled_provider_has_no_discovery_or_authorization_surface()
    {
        using var f = new Fixture();
        f.Options.Enabled = false;
        Assert.IsType<NotFoundResult>(f.Controller.Discovery());
        Assert.IsType<NotFoundResult>(f.Controller.Jwks());
        Assert.IsType<NotFoundResult>(await f.Controller.AuthorizeClient());
        Assert.IsType<NotFoundResult>(await f.Controller.Token());
    }

    [Fact]
    public async Task Code_flow_binds_PKCE_client_redirect_nonce_and_source_lifetime()
    {
        using var f = new Fixture();
        var code = await f.AuthorizeAsync();
        Assert.DoesNotContain(code, f.Store.Grant!.CodeHash);
        f.Form(code, verifier: new string('x', 43));
        Assert.Equal("invalid_grant", Error(await f.Controller.Token()));
        f.Form(code, redirect: "https://crm.example/other");
        Assert.Equal("invalid_grant", Error(await f.Controller.Token()));
        f.Form(code, secret: "wrong");
        Assert.Equal("invalid_client", Error(await f.Controller.Token()));
        f.Form(code);
        var result = Assert.IsType<OkObjectResult>(await f.Controller.Token());
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        var access = payload.RootElement.GetProperty("access_token").GetString()!;
        var identity = payload.RootElement.GetProperty("id_token").GetString()!;
        var at = f.Crypto.Validate(access, "CP6.Web", "at+jwt");
        var id = f.Crypto.Validate(identity, "CP6.Web", "JWT");
        Assert.Equal(f.Nonce, id.FindFirst("nonce")?.Value);
        Assert.Equal(f.User.TenantId.ToString(), at.FindFirst("tenant_id")?.Value);
        Assert.Equal(f.Store.Grant.Id.ToString(), at.FindFirst("sid")?.Value);
        Assert.False(at.HasClaim(c => c.Type is "amr" or "acr"));
        Assert.True(long.Parse(at.FindFirst("exp")!.Value) <= new DateTimeOffset(f.Store.Grant.SourceExpiresAtUtc).ToUnixTimeSeconds());
        Assert.True(payload.RootElement.GetProperty("expires_in").GetInt32() <= 300);
        Assert.False(payload.RootElement.TryGetProperty("refresh_token", out _));
        f.Form(code);
        Assert.Equal("invalid_grant", Error(await f.Controller.Token()));
        f.Request.Headers.Authorization = "Bearer " + access;
        Assert.IsType<OkObjectResult>(await f.Controller.UserInfo());
        f.Request.Headers.Authorization = "Bearer " + identity;
        Assert.Equal("invalid_token", Error(await f.Controller.UserInfo()));
        f.Request.Headers.Authorization = "Bearer " + access;
        await f.Blacklist.Object.BlacklistAsync(f.Store.Grant.SourceJti, TimeSpan.FromMinutes(1));
        Assert.Equal("invalid_token", Error(await f.Controller.Context()));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("must-change")]
    [InlineData("impersonation")]
    [InlineData("old-version")]
    public async Task Restricted_or_old_browser_sessions_cannot_mint_a_code(string restriction)
    {
        using var f = new Fixture();
        f.AuthorizeRequest();
        f.Request.Headers.Cookie = "cp6_at=" + f.BindToken(restriction == "pending"
            ? JwtHelper.GeneratePendingToken(f.User.Id.ToString(), f.User.TenantId, "2fa_verify", "pending", Fixture.Secret, "legacy", "legacy", 5)
            : JwtHelper.GenerateToken(f.User.Id.ToString(), f.User.UserName, Fixture.Secret, "legacy", "legacy", 5,
                f.User.TenantId, "source", restriction == "must-change", impersonatorId: restriction == "impersonation" ? Guid.NewGuid() : null,
                authenticationVersion: restriction == "old-version" ? "old" : AuthSessionVersion.For(f.User)));
        var redirect = Assert.IsType<RedirectResult>(await f.Controller.AuthorizeClient());
        Assert.StartsWith("https://cp6.example/login?", redirect.Url);
        Assert.Contains("oidc_reauthenticate=1", redirect.Url);
        Assert.Null(f.Store.Grant);
    }

    [Fact]
    public async Task Current_access_version_cannot_upgrade_an_old_browser_authentication_family()
    {
        using var f = new Fixture();
        f.User.AuthenticationEpoch = Guid.NewGuid();
        f.User.TwoFactorEnabled = true;
        await f.Db.SaveChangesAsync();
        // Even a freshly signed current-state JWT cannot overwrite the family's original proof.
        f.AuthorizeRequest();

        var redirect = Assert.IsType<RedirectResult>(await f.Controller.AuthorizeClient());

        Assert.StartsWith("https://cp6.example/login?", redirect.Url);
        Assert.Contains("oidc_reauthenticate=1", redirect.Url);
        Assert.Null(f.Store.Grant);
    }

    [Fact]
    public async Task Legacy_refresh_without_recorded_browser_authentication_requires_a_new_login()
    {
        using var f = new Fixture();
        foreach (var row in f.Db.Sys_RefreshTokens) row.BrowserSessionId = null;
        await f.Db.SaveChangesAsync();
        f.AuthorizeRequest();

        var redirect = Assert.IsType<RedirectResult>(await f.Controller.AuthorizeClient());

        Assert.StartsWith("https://cp6.example/login?", redirect.Url);
        Assert.Contains("oidc_reauthenticate=1", redirect.Url);
        Assert.Null(f.Store.Grant);
    }

    [Fact]
    public async Task Invalid_registration_does_not_redirect_and_missing_entitlement_denies()
    {
        using var f = new Fixture();
        f.AuthorizeRequest("https://evil.example/callback");
        Assert.Equal("invalid_request", Error(await f.Controller.AuthorizeClient()));
        f.Options.Organizations[0].CrmEnabled = false;
        f.AuthorizeRequest();
        var result = Assert.IsType<RedirectResult>(await f.Controller.AuthorizeClient());
        Assert.Contains("error=access_denied", result.Url);
        Assert.Null(f.Store.Grant);
    }

    [Theory]
    [InlineData("disabled-user")]
    [InlineData("disabled-tenant")]
    [InlineData("password-change")]
    [InlineData("twofactor-reset")]
    [InlineData("expired-code")]
    [InlineData("revoked-source")]
    public async Task Code_redemption_rechecks_live_authentication(string change)
    {
        using var f = new Fixture();
        var code = await f.AuthorizeAsync();
        if (change == "disabled-user") f.User.Enable = false;
        if (change == "disabled-tenant") f.Organization.Enable = false;
        if (change == "password-change") f.User.Password = "different-password-hash";
        if (change == "twofactor-reset") f.User.AuthenticationEpoch = Guid.NewGuid();
        if (change == "expired-code") f.Store.Grant!.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        if (change == "revoked-source") await f.Blacklist.Object.BlacklistAsync("source", TimeSpan.FromMinutes(1));
        await f.Db.SaveChangesAsync();
        f.Form(code);
        Assert.Equal("invalid_grant", Error(await f.Controller.Token()));
    }

    [Fact]
    public async Task Projection_uses_enabled_real_roles_departments_and_same_tenant_eligible_owners()
    {
        using var f = new Fixture();
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var outside = Guid.NewGuid();
        f.User.DeptId = root;
        f.Db.Sys_Depts.AddRange(new Sys_Dept { Id = root, DeptCode = "root", DeptName = "Root", Path = $"/{root}/" },
            new Sys_Dept { Id = child, DeptCode = "child", DeptName = "Child", ParentId = root, Path = $"/{root}/{child}/" },
            new Sys_Dept { Id = outside, DeptCode = "outside", DeptName = "Outside", Path = $"/{outside}/" });
        f.Db.Sys_RoleDataScopes.Add(new Sys_RoleDataScope { RoleId = 2, ResourceKey = "crm-lead", ScopeType = 3 });
        f.Db.Sys_Users.AddRange(new Sys_User { Id = Guid.NewGuid(), UserName = "eligible", Password = "hash", DeptId = child, RoleId = 3 },
            new Sys_User { Id = Guid.NewGuid(), UserName = "outside", Password = "hash", DeptId = outside, RoleId = 3 },
            new Sys_User { Id = Guid.NewGuid(), UserName = "disabled", Password = "hash", DeptId = child, RoleId = 3, Enable = false },
            new Sys_User { Id = Guid.NewGuid(), UserName = "without-role", Password = "hash", DeptId = child });
        await f.Db.SaveChangesAsync();
        var otherTenant = Guid.NewGuid();
        f.Tenant.CurrentTenantId = otherTenant;
        f.Db.Sys_Users.Add(new Sys_User { Id = Guid.NewGuid(), UserName = "foreign", Password = "hash", DeptId = child, RoleId = 3 });
        await f.Db.SaveChangesAsync();
        f.Tenant.CurrentTenantId = f.User.TenantId;
        var context = await f.Directory.ProjectAsync(f.User);
        Assert.True(context.IsSupervisor);
        Assert.Equal("departments", context.DataScope);
        Assert.Contains(root, context.DepartmentIds);
        Assert.Contains(child, context.DepartmentIds);
        Assert.DoesNotContain(outside, context.DepartmentIds);
        Assert.Contains(context.EligibleOwners, o => o.DisplayName == "eligible");
        Assert.DoesNotContain(context.EligibleOwners, o => o.DisplayName is "outside" or "disabled" or "foreign" or "without-role");
        Assert.DoesNotContain("crm-lead:merge", context.AllowedActions);
        f.Db.Sys_RoleActions.RemoveRange(f.Db.Sys_RoleActions.Where(a => a.ActionCode == "assign"));
        await f.Db.SaveChangesAsync();
        var revoked = await f.Directory.ProjectAsync(f.User);
        Assert.False(revoked.IsSupervisor);
        Assert.All(revoked.EligibleOwners, o => Assert.Equal(f.User.Id, o.SubjectId));
        Assert.DoesNotContain("crm-lead:assign", revoked.AllowedActions);
    }

    [Fact]
    public async Task Missing_department_path_does_not_expand_to_all_departments()
    {
        using var f = new Fixture();
        f.Db.Sys_RoleDataScopes.Add(new Sys_RoleDataScope { RoleId = 2, ResourceKey = "crm-lead", ScopeType = 3 });
        await f.Db.SaveChangesAsync();
        var context = await f.Directory.ProjectAsync(f.User);
        Assert.False(context.IsSupervisor);
        Assert.Empty(context.DepartmentIds);
        Assert.Empty(context.EligibleOwners);
    }

    [Fact]
    public async Task End_session_requires_signed_hint_and_exact_registered_return()
    {
        using var f = new Fixture();
        var code = await f.AuthorizeAsync();
        f.Form(code);
        using var tokens = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(await f.Controller.Token()).Value));
        var identity = tokens.RootElement.GetProperty("id_token").GetString()!;
        f.LogoutForm(identity, "https://evil.example");
        Assert.Equal("invalid_request", Error(await f.Controller.EndSession()));
        Assert.False(f.Store.Grant!.Revoked);
        f.LogoutForm(identity);
        var response = Assert.IsType<OkObjectResult>(await f.Controller.EndSession());
        using var completion = JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
        var url = completion.RootElement.GetProperty("logoutContinuationUri").GetString()!;
        Assert.DoesNotContain(identity, url);
        var ticket = QueryHelpers.ParseQuery(new Uri(url).Query)["ticket"].ToString();
        Assert.IsType<RedirectResult>(await f.Controller.LogoutComplete(ticket));
        Assert.Contains(f.Controller.Response.Headers.SetCookie, c => c!.StartsWith("cp6_rt=;", StringComparison.Ordinal));
        Assert.Equal("invalid_request", Error(await f.Controller.LogoutComplete(ticket)));
        Assert.True(f.Store.Grant.Revoked);
        Assert.True(await f.Blacklist.Object.IsBlacklistedAsync("source"));
        Assert.False(await f.Directory.RefreshFamilyMatchesAsync(f.Store.Grant));
    }

    private static string Error(IActionResult result)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<ObjectResult>(result).Value));
        return json.RootElement.GetProperty("error").GetString()!;
    }

    [Fact]
    public async Task Expired_ID_hint_only_revokes_its_bound_family_and_does_not_clear_another_session()
    {
        using var f = new Fixture();
        var code = await f.AuthorizeAsync();
        f.Form(code);
        Assert.IsType<OkObjectResult>(await f.Controller.Token());
        var grant = f.Store.Grant!;
        var otherRaw = CrmOidcCrypto.RandomToken();
        var other = new Sys_RefreshToken { UserId = f.User.Id, TokenHash = CrmOidcCrypto.Hash(otherRaw), ClientKind = "Web", ExpiresAt = DateTime.Now.AddDays(1) };
        f.Db.Sys_RefreshTokens.Add(other);
        await f.Db.SaveChangesAsync();
        using var rsa = RSA.Create();
        rsa.ImportFromPem(f.Options.Keys[0].Pem);
        var jwt = new JwtSecurityToken(f.Options.Issuer, "CP6.Web", [new Claim("sub", grant.SubjectId.ToString()), new Claim("tenant_id", grant.OrganizationId.ToString()), new Claim("sid", grant.Id.ToString())],
            DateTime.UtcNow.AddMinutes(-3), DateTime.UtcNow.AddMinutes(-1), new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "current" }, SecurityAlgorithms.RsaSha256));
        var hint = new JwtSecurityTokenHandler().WriteToken(jwt);
        Assert.ThrowsAny<SecurityTokenException>(() => f.Crypto.Validate(hint, "CP6.Web", "JWT"));
        f.LogoutForm(hint);
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(await f.Controller.EndSession()).Value));
        var continuation = response.RootElement.GetProperty("logoutContinuationUri").GetString()!;
        var ticket = QueryHelpers.ParseQuery(new Uri(continuation).Query)["ticket"].ToString();
        f.Request.Headers.Cookie = "cp6_rt=" + otherRaw;
        Assert.IsType<RedirectResult>(await f.Controller.LogoutComplete(ticket));
        Assert.Equal(0, f.Controller.Response.Headers.SetCookie.Count);
        Assert.Null(other.RevokedAt);
        Assert.False(await f.Directory.RefreshFamilyMatchesAsync(grant));
    }

    [Fact]
    public async Task Browser_source_cannot_mix_access_and_refresh_tokens_from_different_sessions()
    {
        using var f = new Fixture();
        f.AuthorizeRequest();
        f.Request.Path = "/api/auth/crm-authorize";
        var otherRaw = CrmOidcCrypto.RandomToken();
        f.Db.Sys_RefreshTokens.Add(new Sys_RefreshToken { UserId = f.User.Id, TokenHash = CrmOidcCrypto.Hash(otherRaw), ClientKind = "Web", ExpiresAt = DateTime.Now.AddDays(1) });
        await f.Db.SaveChangesAsync();
        f.Request.Headers.Cookie = f.Request.Headers.Cookie.ToString().Replace(f.RefreshRaw, otherRaw, StringComparison.Ordinal);
        var result = Assert.IsType<RedirectResult>(await f.Controller.AuthorizeClient());
        Assert.Contains("error=login_required", result.Url);
        Assert.Null(f.Store.Grant);
    }

    [Fact]
    public async Task Requested_organization_is_bound_to_existing_single_tenant_membership()
    {
        using var f = new Fixture();
        f.AuthorizeRequest();
        f.Request.QueryString = f.Request.QueryString.Add("organization_id", Guid.NewGuid().ToString());
        var redirect = Assert.IsType<RedirectResult>(await f.Controller.AuthorizeClient());
        Assert.Contains("error=access_denied", redirect.Url);
        Assert.Null(f.Store.Grant);
        Assert.IsType<OkObjectResult>(await f.Controller.Organization("acme", "CP6.Web"));
        Assert.IsType<NotFoundResult>(await f.Controller.Organization("acme", "unknown-client"));
        f.Organization.Enable = false;
        await f.Db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await f.Controller.Organization("acme", "CP6.Web"));
    }

    [Fact]
    public void Only_enabled_bridge_binds_browser_cookies_to_refresh_family()
    {
        using var f = new Fixture();
        var raw = JwtHelper.GenerateToken(f.User.Id.ToString(), "u", Fixture.Secret, "legacy", "legacy", 5);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CrmOidc:Enabled"] = "true", ["JWT:Secret"] = Fixture.Secret }).Build();
        var context = new DefaultHttpContext();
        var writer = new AuthCookieWriter(Microsoft.Extensions.Options.Options.Create(new SecurityOptions()), config);
        writer.WriteAuthCookies(context.Response, raw, f.RefreshRaw, "csrf");
        var cookie = context.Response.Headers.SetCookie.Single(x => x!.StartsWith("cp6_at=", StringComparison.Ordinal))!;
        var token = new JwtSecurityTokenHandler().ReadJwtToken(cookie["cp6_at=".Length..].Split(';')[0]);
        Assert.Equal(CrmOidcCrypto.Hash(f.RefreshRaw), token.Claims.Single(c => c.Type == "cp6_session").Value);
        Assert.Equal("HS256", token.Header.Alg);
        config["CrmOidc:Enabled"] = "false";
        context = new DefaultHttpContext();
        writer.WriteAuthCookies(context.Response, raw, f.RefreshRaw, "csrf");
        cookie = context.Response.Headers.SetCookie.Single(x => x!.StartsWith("cp6_at=", StringComparison.Ordinal))!;
        Assert.StartsWith("cp6_at=" + raw + ";", cookie);
    }

    private sealed class Fixture : IDisposable
    {
        public const string Secret = "source-session-only-test-key-012345678901234567890123456789012345";
        public readonly string Verifier = CrmOidcCrypto.RandomToken();
        public readonly string Nonce = CrmOidcCrypto.RandomToken();
        public readonly string RefreshRaw = CrmOidcCrypto.RandomToken();
        public readonly CrmOidcOptions Options;
        public readonly CrmOidcCrypto Crypto;
        public readonly CP6Context Db;
        public readonly TenantContext Tenant = new() { CurrentTenantId = Guid.NewGuid() };
        public readonly Sys_User User;
        public readonly Sys_Tenant Organization;
        public readonly Mock<ITokenBlacklistService> Blacklist = new();
        public readonly MemoryGrants Store = new();
        public readonly CrmOidcDirectory Directory;
        public readonly CrmOidcController Controller;
        public HttpRequest Request => Controller.Request;
        public Fixture()
        {
            using var rsa = RSA.Create(2048);
            Options = new CrmOidcOptions { Enabled = true, Issuer = "https://cp6.example", ActiveKeyId = "current",
                Keys = [new() { Kid = "current", Pem = rsa.ExportRSAPrivateKeyPem() }],
                Clients = [new() { SecretSha256 = CrmOidcCrypto.Hash("test-client-secret"), RedirectUris = ["https://crm.example/signin-oidc"], PostLogoutRedirectUris = ["https://crm.example/signed-out"] }],
                Organizations = [new() { TenantId = Tenant.CurrentTenantId, Slug = "acme", Region = "us", CrmEnabled = true }] };
            Crypto = new(Options);
            Db = new(new DbContextOptionsBuilder<CP6Context>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Tenant);
            Organization = new() { Id = Tenant.CurrentTenantId, TenantName = "Acme", TenantCode = "acme" };
            User = new() { Id = Guid.NewGuid(), TenantId = Tenant.CurrentTenantId, UserName = "user", Password = "hash", RoleId = 2 };
            Db.Sys_Tenants.Add(Organization);
            Db.Sys_Users.Add(User);
            var browserSession = new Sys_BrowserSession { Id = Guid.NewGuid(), UserId = User.Id, AuthenticationVersion = AuthSessionVersion.For(User) };
            Db.Sys_BrowserSessions.Add(browserSession);
            Db.Sys_RefreshTokens.Add(new Sys_RefreshToken { UserId = User.Id, BrowserSessionId = browserSession.Id,
                TokenHash = CrmOidcCrypto.Hash(RefreshRaw), ExpiresAt = DateTime.Now.AddDays(1), ClientKind = "Web" });
            Db.Sys_Roles.AddRange(new Sys_Role { RoleId = 2, RoleName = "Supervisor" }, new Sys_Role { RoleId = 3, RoleName = "Sales" }, new Sys_Role { RoleId = 4, RoleName = "Disabled", Enable = false });
            Db.Sys_UserRoles.Add(new Sys_UserRole { UserId = User.Id, RoleId = 4 });
            Db.Sys_Menus.Add(new Sys_Menu { MenuId = 802, MenuName = "Lead", MenuKey = "crm-lead" });
            Db.Sys_RoleActions.AddRange(new Sys_RoleAction { RoleId = 2, MenuId = 802, ActionCode = "query" }, new Sys_RoleAction { RoleId = 2, MenuId = 802, ActionCode = "assign" },
                new Sys_RoleAction { RoleId = 2, MenuId = 802, ActionCode = "edit" },
                new Sys_RoleAction { RoleId = 3, MenuId = 802, ActionCode = "edit" },
                new Sys_RoleAction { RoleId = 3, MenuId = 802, ActionCode = "query" }, new Sys_RoleAction { RoleId = 4, MenuId = 802, ActionCode = "merge" });
            Db.SaveChanges();
            Store.Db = Db;
            var revoked = new HashSet<string>();
            Blacklist.Setup(b => b.IsBlacklistedAsync(It.IsAny<string>())).Returns((string jti) => Task.FromResult(revoked.Contains(jti)));
            Blacklist.Setup(b => b.BlacklistAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).Callback((string jti, TimeSpan _) => revoked.Add(jti)).Returns(Task.CompletedTask);
            var security = Microsoft.Extensions.Options.Options.Create(new SecurityOptions());
            Directory = new(Db, Tenant, Blacklist.Object, new PasswordPolicyService(Db, security, new BCryptPasswordHasher()), Options);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["JWT:Secret"] = Secret, ["JWT:Issuer"] = "legacy", ["JWT:Audience"] = "legacy" }).Build();
            Controller = new(Options, Crypto, Store, Directory, config, Blacklist.Object, new AuthCookieWriter(security))
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        }
        public void AuthorizeRequest(string redirect = "https://crm.example/signin-oidc")
        {
            Request.Method = "GET";
            Request.Path = "/connect/authorize";
            Request.QueryString = QueryString.Create(new Dictionary<string, string?> { ["client_id"] = "CP6.Web", ["response_type"] = "code", ["scope"] = "openid profile crm", ["redirect_uri"] = redirect,
                ["state"] = "a-random-state-value", ["nonce"] = Nonce, ["code_challenge"] = CrmOidcCrypto.Challenge(Verifier), ["code_challenge_method"] = "S256" });
            Request.Headers.Cookie = "cp6_at=" + BindToken(JwtHelper.GenerateToken(User.Id.ToString(), User.UserName, Secret, "legacy", "legacy", 2, User.TenantId, "source", authenticationVersion: AuthSessionVersion.For(User))) + "; cp6_rt=" + RefreshRaw;
        }
        public async Task<string> AuthorizeAsync()
        {
            AuthorizeRequest();
            var local = Assert.IsType<RedirectResult>(await Controller.AuthorizeClient());
            Assert.StartsWith("https://cp6.example/api/auth/crm-authorize?", local.Url);
            Request.Path = "/api/auth/crm-authorize";
            var redirect = Assert.IsType<RedirectResult>(await Controller.AuthorizeClient());
            return QueryHelpers.ParseQuery(new Uri(redirect.Url!).Query)["code"].ToString();
        }
        public string BindToken(string raw)
        {
            var token = new JwtSecurityTokenHandler().ReadJwtToken(raw);
            token.Payload["cp6_session"] = CrmOidcCrypto.Hash(RefreshRaw);
            return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(new JwtHeader(new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256)), token.Payload));
        }
        public void LogoutForm(string identity, string redirect = "https://crm.example/signed-out")
        {
            Request.Method = "POST";
            Request.ContentType = "application/x-www-form-urlencoded";
            Request.Headers.Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("CP6.Web:test-client-secret"));
            Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["id_token_hint"] = identity, ["post_logout_redirect_uri"] = redirect });
        }
        public void Form(string code, string? verifier = null, string redirect = "https://crm.example/signin-oidc", string secret = "test-client-secret")
        {
            Request.Method = "POST";
            Request.ContentType = "application/x-www-form-urlencoded";
            Request.Headers.Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("CP6.Web:" + secret));
            Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["grant_type"] = "authorization_code", ["code"] = code, ["code_verifier"] = verifier ?? Verifier, ["redirect_uri"] = redirect });
        }
        public void Dispose() { Db.Dispose(); Crypto.Dispose(); }
    }

    private sealed class MemoryGrants : ICrmOidcGrantStore
    {
        public CP6Context? Db;
        public CrmOidcGrant? Grant;
        public CrmOidcLogout? Logout;
        public Task SaveAsync(CrmOidcGrant grant) { Grant = grant; return Task.CompletedTask; }
        public Task<CrmOidcGrant?> ConsumeAsync(string hash, string clientId, string redirectUri, string challenge)
        {
            if (Grant == null || Grant.Consumed || Grant.Revoked || Grant.CodeHash != hash || Grant.ClientId != clientId
                || Grant.RedirectUri != redirectUri || Grant.Challenge != challenge || Grant.ExpiresAtUtc <= DateTime.UtcNow) return Task.FromResult<CrmOidcGrant?>(null);
            Grant.Consumed = true;
            return Task.FromResult<CrmOidcGrant?>(Grant);
        }
        public Task<CrmOidcGrant?> FindSessionAsync(Guid id) => Task.FromResult(Grant?.Id == id && Grant.Consumed && !Grant.Revoked ? Grant : null);
        public Task RevokeAsync(Guid id) { if (Grant?.Id == id) Grant.Revoked = true; return Task.CompletedTask; }
        public Task<CrmOidcGrant?> FindForLogoutAsync(Guid id) => Task.FromResult(Grant?.Id == id ? Grant : null);
        public async Task RevokeSourceFamilyAsync(CrmOidcGrant grant)
        {
            var source = Db!.Sys_RefreshTokens.IgnoreQueryFilters().Single(r => r.TokenHash == grant.SourceRefreshHash
                && r.UserId == grant.SubjectId && r.TenantId == grant.OrganizationId);
            var session = Db.Sys_BrowserSessions.IgnoreQueryFilters().Single(s => s.Id == source.BrowserSessionId);
            session.LoggedOutAtUtc = DateTime.UtcNow;
            foreach (var row in Db.Sys_RefreshTokens.IgnoreQueryFilters().Where(r => r.BrowserSessionId == session.Id
                && r.UserId == grant.SubjectId && r.TenantId == grant.OrganizationId)) row.RevokedAt ??= DateTime.Now;
            await Db!.SaveChangesAsync();
            grant.Revoked = true;
        }
        public Task SaveLogoutAsync(CrmOidcLogout ticket) { Logout = ticket; return Task.CompletedTask; }
        public Task<CrmOidcLogout?> ConsumeLogoutAsync(string hash)
        {
            var result = Logout?.TicketHash == hash && Logout.ExpiresAtUtc > DateTime.UtcNow ? Logout : null;
            if (result != null) Logout = null;
            return Task.FromResult(result);
        }
    }
}
