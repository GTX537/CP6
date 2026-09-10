using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace CP6.Tests.Sys;

public class CrmOidcServiceTokenTests
{
    [Fact]
    public async Task Valid_service_credentials_mint_only_a_bounded_service_access_token()
    {
        using var f = new Fixture();
        f.ServiceForm();

        var result = Assert.IsType<OkObjectResult>(await f.Controller.Token());

        using var body = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        var access = body.RootElement.GetProperty("access_token").GetString()!;
        Assert.Equal("Bearer", body.RootElement.GetProperty("token_type").GetString());
        Assert.Equal("cp6.services", body.RootElement.GetProperty("scope").GetString());
        Assert.Equal(300, body.RootElement.GetProperty("expires_in").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("id_token", out _));
        Assert.False(body.RootElement.TryGetProperty("refresh_token", out _));

        var principal = f.Crypto.Validate(access, "CP6.Services", "at+jwt");
        Assert.Equal("service:crm-worker", principal.FindFirst("sub")?.Value);
        Assert.Equal(f.TenantId.ToString(), principal.FindFirst("tenant_id")?.Value);
        Assert.Equal("crm-worker", principal.FindFirst("client_id")?.Value);
        Assert.Equal("cp6.services", principal.FindFirst("scope")?.Value);
        Assert.True(Guid.TryParse(principal.FindFirst("jti")?.Value, out _));
        Assert.False(principal.HasClaim(c => c.Type is "sid" or "role" or "roles" or "permission" or "amr" or "acr"));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(access);
        Assert.Equal("RS256", jwt.Header.Alg);
        Assert.Equal("current", jwt.Header.Kid);
        Assert.Equal("at+jwt", jwt.Header.Typ);
        Assert.Equal(300, long.Parse(principal.FindFirst("exp")!.Value)
            - long.Parse(principal.FindFirst("iat")!.Value));
        Assert.Equal("no-store", f.Controller.Response.Headers.CacheControl);
        Assert.Equal("no-cache", f.Controller.Response.Headers.Pragma);
    }

    [Fact]
    public async Task Discovery_advertises_both_grants_and_the_service_scope()
    {
        using var f = new Fixture();

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            Assert.IsType<OkObjectResult>(f.Controller.Discovery()).Value));

        Assert.Equal(["authorization_code", "client_credentials"], json.RootElement
            .GetProperty("grant_types_supported").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Contains("cp6.services", json.RootElement.GetProperty("scopes_supported")
            .EnumerateArray().Select(x => x.GetString()));
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("browser-collision")]
    [InlineData("unsafe-id")]
    [InlineData("id-newline")]
    [InlineData("empty-tenant")]
    [InlineData("bad-hash")]
    [InlineData("hash-newline")]
    [InlineData("missing-scope")]
    [InlineData("duplicate-scope")]
    [InlineData("unknown-scope")]
    [InlineData("unmapped-tenant")]
    public void Enabled_configuration_rejects_invalid_service_registrations(string problem)
    {
        var options = ValidOptions();
        var client = options.ServiceClients.Single();
        switch (problem)
        {
            case "duplicate-id":
                options.ServiceClients.Add(new CrmOidcServiceClient
                {
                    ClientId = client.ClientId,
                    SecretSha256 = client.SecretSha256,
                    TenantId = client.TenantId,
                    Enabled = false,
                    AllowedScopes = ["cp6.services"]
                });
                break;
            case "browser-collision": client.ClientId = "CP6.Web"; break;
            case "unsafe-id": client.ClientId = new string('x', 81); break;
            case "id-newline": client.ClientId = "crm-worker\n"; break;
            case "empty-tenant": client.TenantId = Guid.Empty; break;
            case "bad-hash": client.SecretSha256 = "secret"; break;
            case "hash-newline": client.SecretSha256 = CrmOidcCrypto.Hash(Fixture.ServiceSecret) + "\n"; break;
            case "missing-scope": client.AllowedScopes = []; break;
            case "duplicate-scope": client.AllowedScopes = ["cp6.services", "cp6.services"]; break;
            case "unknown-scope": client.AllowedScopes = ["openid"]; break;
            case "unmapped-tenant": client.TenantId = Guid.NewGuid(); break;
        }

        Assert.Throws<InvalidOperationException>(() => options.Validate(development: false));
    }

    [Fact]
    public void Disabled_provider_keeps_service_registration_opt_in_and_unvalidated()
    {
        var defaults = new CrmOidcServiceClient();
        Assert.False(defaults.Enabled);
        Assert.Equal(Guid.Empty, defaults.TenantId);
        Assert.Empty(defaults.ClientId);
        Assert.Empty(defaults.SecretSha256);
        Assert.Empty(defaults.AllowedScopes);
        Assert.Empty(new CrmOidcOptions().TrustedTokenProxyAddresses);
        new CrmOidcOptions
        {
            ServiceClients = [new() { ClientId = "invalid id", SecretSha256 = "raw-secret" }]
        }.Validate(development: false);
    }

    [Theory]
    [InlineData("proxy.example")]
    [InlineData("10.0.0.0/8")]
    [InlineData("*")]
    [InlineData(" 127.0.0.1")]
    [InlineData("127.1")]
    [InlineData("2130706433")]
    [InlineData("0x7f000001")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void Trusted_token_proxy_configuration_rejects_nonliteral_or_unsafe_addresses(string address)
    {
        var options = ValidOptions();
        options.TrustedTokenProxyAddresses = [address];

        Assert.Throws<InvalidOperationException>(() => options.Validate(development: false));
    }

    [Fact]
    public void Trusted_token_proxy_configuration_rejects_canonical_duplicates()
    {
        var options = ValidOptions();
        options.TrustedTokenProxyAddresses = ["127.0.0.1", "::ffff:127.0.0.1"];

        Assert.Throws<InvalidOperationException>(() => options.Validate(development: false));
    }

    [Fact]
    public void Trusted_token_proxy_configuration_accepts_unique_full_IP_literals()
    {
        var options = ValidOptions();
        options.TrustedTokenProxyAddresses = ["10.20.30.40", "2001:db8::10"];

        options.Validate(development: false);
    }

    [Theory]
    [InlineData("wrong-secret")]
    [InlineData("missing-auth")]
    [InlineData("duplicate-auth")]
    public async Task Invalid_or_ambiguous_service_credentials_return_a_basic_challenge(string problem)
    {
        using var f = new Fixture();
        f.ServiceForm(secret: problem == "wrong-secret" ? "wrong" : Fixture.ServiceSecret);
        if (problem == "missing-auth") f.Request.Headers.Authorization = StringValues.Empty;
        if (problem == "duplicate-auth") f.Request.Headers.Authorization =
            new StringValues([f.Basic("crm-worker", Fixture.ServiceSecret), f.Basic("crm-worker", Fixture.ServiceSecret)]);

        var result = await f.Controller.Token();

        Assert.Equal("invalid_client", Error(result));
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal("Basic", f.Controller.Response.Headers.WWWAuthenticate);
        AssertNoStore(f.Controller);
    }

    [Theory]
    [InlineData("client")]
    [InlineData("tenant")]
    [InlineData("missing-tenant")]
    [InlineData("expired-tenant")]
    [InlineData("crm")]
    public async Task Authenticated_service_is_denied_when_registration_or_real_tenant_is_inactive(string inactive)
    {
        using var f = new Fixture();
        if (inactive == "client") f.Options.ServiceClients[0].Enabled = false;
        if (inactive == "tenant") f.Organization.Enable = false;
        if (inactive == "missing-tenant") f.Db.Sys_Tenants.Remove(f.Organization);
        if (inactive == "expired-tenant") f.Organization.ExpireDate = DateTime
            .SpecifyKind(f.Now.UtcDateTime, DateTimeKind.Utc).ToLocalTime().AddSeconds(-1);
        if (inactive == "crm") f.Options.Organizations[0].CrmEnabled = false;
        await f.Db.SaveChangesAsync();
        f.ServiceForm();

        var result = await f.Controller.Token();

        Assert.Equal("unauthorized_client", Error(result));
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(result).StatusCode);
        AssertNoStore(f.Controller);
    }

    [Theory]
    [InlineData("client_id")]
    [InlineData("client_secret")]
    [InlineData("code")]
    [InlineData("redirect_uri")]
    [InlineData("code_verifier")]
    [InlineData("audience")]
    [InlineData("tenant")]
    [InlineData("tenant_id")]
    [InlineData("unknown")]
    public async Task Service_grant_rejects_every_extra_form_field(string field)
    {
        using var f = new Fixture();
        f.ServiceForm(extra: new(field, "attacker-controlled"));

        Assert.Equal("invalid_request", Error(await f.Controller.Token()));
        AssertNoStore(f.Controller);
    }

    [Fact]
    public async Task Service_grant_rejects_duplicate_fields_and_oversized_body()
    {
        using var duplicate = new Fixture();
        duplicate.ServiceForm(scope: new StringValues(["cp6.services", "cp6.services"]));
        Assert.Equal("invalid_request", Error(await duplicate.Controller.Token()));

        using var oversized = new Fixture();
        oversized.ServiceForm();
        oversized.Request.ContentLength = 8193;
        Assert.Equal("invalid_request", Error(await oversized.Controller.Token()));
        AssertNoStore(oversized.Controller);
    }

    [Fact]
    public async Task Service_credentials_require_https_except_for_the_explicit_loopback_configuration()
    {
        using var remote = new Fixture();
        remote.ServiceForm();
        remote.Request.Scheme = "http";
        remote.Request.Host = new HostString("api.example");
        remote.Request.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10");
        Assert.Equal("invalid_request", Error(await remote.Controller.Token()));

        using var loopback = new Fixture();
        loopback.Options.Issuer = "http://127.0.0.1:5080";
        loopback.Options.AllowInsecureLoopback = true;
        loopback.ServiceForm();
        loopback.Request.Scheme = "http";
        loopback.Request.Host = new HostString("127.0.0.1", 5080);
        loopback.Request.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        Assert.IsType<OkObjectResult>(await loopback.Controller.Token());
    }

    [Fact]
    public async Task Service_credentials_accept_https_only_from_the_exact_trusted_proxy_peer()
    {
        using var f = new Fixture();
        f.Options.TrustedTokenProxyAddresses = ["10.20.30.40"];
        f.ServiceForm();
        f.Request.Scheme = "http";
        f.Request.Host = new HostString("internal-api");
        f.Request.HttpContext.Connection.RemoteIpAddress =
            System.Net.IPAddress.Parse("::ffff:10.20.30.40");
        f.Request.Headers["X-Forwarded-Proto"] = "https";

        Assert.IsType<OkObjectResult>(await f.Controller.Token());
    }

    [Theory]
    [InlineData("default")]
    [InlineData("untrusted")]
    [InlineData("missing-proto")]
    [InlineData("http-proto")]
    [InlineData("duplicate-proto")]
    [InlineData("comma-proto")]
    [InlineData("missing-peer")]
    [InlineData("spoofed-forwarded-for")]
    public async Task Service_credentials_reject_untrusted_or_ambiguous_proxy_transport(string problem)
    {
        using var f = new Fixture();
        if (problem != "default") f.Options.TrustedTokenProxyAddresses = ["10.20.30.40"];
        f.ServiceForm();
        f.Request.Scheme = "http";
        f.Request.Host = new HostString("internal-api");
        f.Request.HttpContext.Connection.RemoteIpAddress = problem switch
        {
            "missing-peer" => null,
            "untrusted" or "spoofed-forwarded-for" => System.Net.IPAddress.Parse("10.20.30.41"),
            _ => System.Net.IPAddress.Parse("10.20.30.40")
        };
        if (problem != "missing-proto") f.Request.Headers["X-Forwarded-Proto"] = problem switch
        {
            "http-proto" => "http",
            "duplicate-proto" => new StringValues(["https", "https"]),
            "comma-proto" => "https,http",
            _ => "https"
        };
        if (problem == "spoofed-forwarded-for")
            f.Request.Headers["X-Forwarded-For"] = "10.20.30.40";

        Assert.Equal("invalid_request", Error(await f.Controller.Token()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("openid")]
    [InlineData("cp6.services openid")]
    public async Task Service_grant_requires_the_exact_registered_scope(string? scope)
    {
        using var f = new Fixture();
        f.ServiceForm(scope: scope == null ? (StringValues?)null : new StringValues(scope),
            includeDefaultScope: scope != null);

        Assert.Equal("invalid_scope", Error(await f.Controller.Token()));
    }

    [Fact]
    public async Task Missing_and_unknown_grants_are_distinguished()
    {
        using var missing = new Fixture();
        missing.ServiceForm(grantType: null);
        Assert.Equal("invalid_request", Error(await missing.Controller.Token()));

        using var unknown = new Fixture();
        unknown.ServiceForm(grantType: "password");
        Assert.Equal("unsupported_grant_type", Error(await unknown.Controller.Token()));
    }

    [Fact]
    public async Task Authenticated_clients_cannot_cross_into_the_other_grant_type()
    {
        using var service = new Fixture();
        service.ServiceForm(grantType: "authorization_code");
        Assert.Equal("unauthorized_client", Error(await service.Controller.Token()));

        using var browser = new Fixture();
        browser.ServiceForm(clientId: "CP6.Web", secret: Fixture.BrowserSecret);
        Assert.Equal("unauthorized_client", Error(await browser.Controller.Token()));
    }

    [Fact]
    public async Task Service_credentials_and_tokens_are_rejected_by_browser_endpoints()
    {
        using var f = new Fixture();
        f.ServiceForm();
        using var issued = JsonDocument.Parse(JsonSerializer.Serialize(
            Assert.IsType<OkObjectResult>(await f.Controller.Token()).Value));
        var access = issued.RootElement.GetProperty("access_token").GetString()!;

        f.Request.Headers.Authorization = "Bearer " + access;
        Assert.Equal("invalid_token", Error(await f.Controller.UserInfo()));
        Assert.Equal("invalid_token", Error(await f.Controller.Context()));

        f.Request.Headers.Authorization = f.Basic("crm-worker", Fixture.ServiceSecret);
        f.Request.Form = new FormCollection(new Dictionary<string, StringValues>());
        Assert.Equal("unauthorized_client", Error(await f.Controller.EndSession()));

        f.Request.Method = "GET";
        f.Request.QueryString = QueryString.Create(new Dictionary<string, string?>
        {
            ["client_id"] = "crm-worker",
            ["response_type"] = "code",
            ["redirect_uri"] = "https://crm.example/signin-oidc"
        });
        Assert.Equal("invalid_request", Error(await f.Controller.AuthorizeClient()));
    }

    [Fact]
    public async Task Browser_and_service_audiences_remain_mutually_exclusive()
    {
        using var f = new Fixture();
        f.ServiceForm();
        using var issued = JsonDocument.Parse(JsonSerializer.Serialize(
            Assert.IsType<OkObjectResult>(await f.Controller.Token()).Value));
        var service = issued.RootElement.GetProperty("access_token").GetString()!;
        Assert.ThrowsAny<SecurityTokenException>(() => f.Crypto.Validate(service, "CP6.Web", "at+jwt"));

        var browser = f.Crypto.Sign("CP6.Web", [new Claim("sub", Guid.NewGuid().ToString())],
            f.Now.AddMinutes(1), "at+jwt");
        Assert.ThrowsAny<SecurityTokenException>(() => f.Crypto.Validate(browser, "CP6.Services", "at+jwt"));
    }

    [Fact]
    public async Task Database_failure_returns_a_generic_unavailable_error()
    {
        using var f = new Fixture(new ThrowingDirectory(new TestDbException()));
        f.ServiceForm();

        var result = await f.Controller.Token();

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal("temporarily_unavailable", Error(result));
        var serialized = JsonSerializer.Serialize(Assert.IsType<ObjectResult>(result).Value);
        Assert.DoesNotContain(Fixture.ServiceSecret, serialized);
        Assert.DoesNotContain(f.TenantId.ToString(), serialized);
        AssertNoStore(f.Controller);
    }

    [Theory]
    [InlineData("ef-db")]
    [InlineData("ef-timeout")]
    [InlineData("retry-db")]
    public async Task Recognizable_database_failure_wrappers_return_a_generic_unavailable_error(string wrapper)
    {
        Exception inner = wrapper == "ef-timeout" ? new TimeoutException() : new TestDbException();
        Exception failure = wrapper == "retry-db"
            ? new RetryLimitExceededException("retry exhausted", inner)
            : new InvalidOperationException("database execution failed", inner);
        using var f = new Fixture(new ThrowingDirectory(failure));
        f.ServiceForm();

        var result = await f.Controller.Token();

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal("temporarily_unavailable", Error(result));
        AssertNoStore(f.Controller);
    }

    [Fact]
    public async Task Programmer_failures_are_not_hidden_as_dependency_outages()
    {
        using var f = new Fixture(new ThrowingDirectory(
            new InvalidOperationException("bug", new ArgumentException("programmer input"))));
        f.ServiceForm();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Controller.Token());

        Assert.Equal("bug", error.Message);
    }

    private static string Error(IActionResult result)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<ObjectResult>(result).Value));
        return json.RootElement.GetProperty("error").GetString()!;
    }

    private static void AssertNoStore(ControllerBase controller)
    {
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
        Assert.Equal("no-cache", controller.Response.Headers.Pragma);
    }

    private static CrmOidcOptions ValidOptions()
    {
        var tenantId = Guid.NewGuid();
        using var rsa = RSA.Create(2048);
        return new CrmOidcOptions
        {
            Enabled = true,
            Issuer = "https://cp6.example",
            ActiveKeyId = "current",
            Keys = [new() { Kid = "current", Pem = rsa.ExportRSAPrivateKeyPem() }],
            Clients = [new()
            {
                ClientId = "CP6.Web", SecretSha256 = CrmOidcCrypto.Hash(Fixture.BrowserSecret),
                RedirectUris = ["https://crm.example/signin-oidc"],
                PostLogoutRedirectUris = ["https://crm.example/signed-out"]
            }],
            ServiceClients = [new()
            {
                ClientId = "crm-worker", SecretSha256 = CrmOidcCrypto.Hash(Fixture.ServiceSecret),
                TenantId = tenantId, Enabled = true, AllowedScopes = ["cp6.services"]
            }],
            Organizations = [new() { TenantId = tenantId, Slug = "acme", Region = "us", CrmEnabled = true }]
        };
    }

    internal sealed class Fixture : IDisposable
    {
        public const string ServiceSecret = "service-secret-with-enough-entropy-for-tests";
        public const string BrowserSecret = "browser-secret-with-enough-entropy-for-tests";
        public readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        public readonly CrmOidcOptions Options;
        public readonly CrmOidcCrypto Crypto;
        public readonly CP6Context Db;
        public readonly Sys_Tenant Organization;
        public readonly Guid TenantId;
        public readonly CrmOidcController Controller;
        public HttpRequest Request => Controller.Request;

        public Fixture(ICrmOidcServiceDirectory? serviceDirectory = null, ICrmServiceTokenRecordStore? records = null)
        {
            Options = ValidOptions();
            TenantId = Options.ServiceClients.Single().TenantId;
            var time = new FixedTimeProvider(Now);
            Crypto = new(Options, time);
            var tenant = new TenantContext { CurrentTenantId = TenantId };
            Db = new(new DbContextOptionsBuilder<CP6Context>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
            Organization = new Sys_Tenant
            {
                Id = TenantId,
                TenantCode = "acme",
                TenantName = "Acme",
                Enable = true
            };
            Db.Sys_Tenants.Add(Organization);
            Db.SaveChanges();
            var blacklist = Mock.Of<ITokenBlacklistService>();
            var directory = new CrmOidcDirectory(Db, tenant, blacklist,
                Mock.Of<IPasswordPolicyService>(), Options);
            var serviceTokens = new CrmOidcServiceTokens(Options, Crypto,
                serviceDirectory ?? directory, time, records);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT:Secret"] = new string('s', 64),
                ["JWT:Issuer"] = "legacy",
                ["JWT:Audience"] = "legacy"
            }).Build();
            Controller = new CrmOidcController(Options, Crypto, Mock.Of<ICrmOidcGrantStore>(), directory,
                config, blacklist, Mock.Of<IAuthCookieWriter>(), serviceTokens)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            Request.Scheme = "https";
        }

        public string Basic(string clientId, string secret) => "Basic " +
            Convert.ToBase64String(Encoding.UTF8.GetBytes(clientId + ":" + secret));

        public void ServiceForm(string? grantType = "client_credentials", StringValues? scope = null,
            bool includeDefaultScope = true, KeyValuePair<string, string>? extra = null,
            string clientId = "crm-worker", string secret = ServiceSecret)
        {
            Request.Method = "POST";
            Request.ContentType = "application/x-www-form-urlencoded";
            Request.Headers.Authorization = Basic(clientId, secret);
            var values = new Dictionary<string, StringValues>();
            if (grantType != null) values["grant_type"] = grantType;
            if (scope.HasValue) values["scope"] = scope.Value;
            else if (includeDefaultScope) values["scope"] = "cp6.services";
            if (extra.HasValue) values[extra.Value.Key] = extra.Value.Value;
            Request.Form = new FormCollection(values);
        }

        public void Dispose()
        {
            Db.Dispose();
            Crypto.Dispose();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ThrowingDirectory(Exception exception) : ICrmOidcServiceDirectory
    {
        public Task<bool> IsServiceTenantActiveAsync(Guid tenantId, DateTime utcNow,
            CancellationToken cancellationToken = default) => Task.FromException<bool>(exception);
    }

    private sealed class TestDbException : DbException;
}
