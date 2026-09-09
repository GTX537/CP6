using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Middleware;
using CP6.WebApi.Services;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace CP6.Oidc.IntegrationTests;

/// <summary>Real MVC/Kestrel and SQL issuer boundary. This is not a Platform/CRM consumer acceptance test.</summary>
public sealed class ServiceTokenHttpSqlTests : IAsyncLifetime
{
    private readonly string _database = "CP6OidcServiceTest_" + Guid.NewGuid().ToString("N");
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _otherTenant = Guid.NewGuid();
    private readonly string _secret = CrmOidcCrypto.RandomToken();
    private string _master = "";
    private string _connection = "";
    private WebApplication? _app;
    private HttpClient _client = null!;
    private CrmOidcOptions _options = null!;

    public async Task InitializeAsync()
    {
        var input = Environment.GetEnvironmentVariable("CP6_OIDC_TEST_SQL");
        if (string.IsNullOrWhiteSpace(input)) throw new InvalidOperationException("CP6_OIDC_TEST_SQL is required; HTTP/SQL tests never skip.");
        var connection = new SqlConnectionStringBuilder(input) { InitialCatalog = "master" };
        _master = connection.ConnectionString;
        await using var master = new SqlConnection(_master);
        await master.ExecuteAsync($"CREATE DATABASE [{_database}]");
        connection.InitialCatalog = _database;
        _connection = connection.ConnectionString;
        await using var model = new CP6Context(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(_connection).Options);
        await using var db = new SqlConnection(_connection);
        foreach (var batch in Regex.Split(model.Database.GenerateCreateScript(), @"^GO\s*$", RegexOptions.Multiline))
            if (batch.Contains("CREATE TABLE [Sys_Tenants]", StringComparison.Ordinal)) await db.ExecuteAsync(batch);
        await db.ExecuteAsync("""
            INSERT dbo.Sys_Tenants(Id,TenantCode,TenantName,Enable,TwoFactorMode,CreateDate)
            VALUES(@tenant,N'service-bound',N'Service test',1,0,GETDATE()),
                  (@other,N'other-tenant',N'Other tenant',1,0,GETDATE());
            """, new { tenant = _tenant, other = _otherTenant });

        using var key = RSA.Create(2048);
        _options = new CrmOidcOptions
        {
            Enabled = true,
            Issuer = "https://issuer.cp6.example",
            ActiveKeyId = "http-key",
            Keys = [new() { Kid = "http-key", Pem = key.ExportRSAPrivateKeyPem() }],
            Clients = [new() { SecretSha256 = CrmOidcCrypto.Hash(CrmOidcCrypto.RandomToken()), RedirectUris = ["https://crm.example/callback"] }],
            Organizations = [new() { TenantId = _tenant, Slug = "service-bound", Region = "us", CrmEnabled = true },
                new() { TenantId = _otherTenant, Slug = "other-tenant", Region = "us", CrmEnabled = true }],
            ServiceClients = [new() { ClientId = "crm-worker", SecretSha256 = CrmOidcCrypto.Hash(_secret),
                TenantId = _tenant, Enabled = true, AllowedScopes = ["cp6.services"] }],
            TrustedTokenProxyAddresses = ["127.0.0.1"]
        };
        _options.Validate(false);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(_options);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<CrmOidcCrypto>();
        builder.Services.AddScoped<CrmOidcDirectory>();
        builder.Services.AddScoped<ICrmOidcServiceDirectory>(services => services.GetRequiredService<CrmOidcDirectory>());
        builder.Services.AddScoped<CrmOidcServiceTokens>();
        builder.Services.AddScoped<ITenantContext, TenantContext>();
        builder.Services.AddDbContext<CP6Context>(o => o.UseSqlServer(_connection));
        builder.Services.AddScoped<ICrmOidcGrantStore>(_ => new SqlCrmOidcGrantStore(_connection));
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSingleton<ITokenBlacklistService, CacheTokenBlacklistService>();
        builder.Services.Configure<SecurityOptions>(_ => { });
        builder.Services.AddScoped<IPasswordPolicyService, PasswordPolicyService>();
        builder.Services.AddSingleton<IPasswordHasher>(services =>
            new BCryptPasswordHasher(services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SecurityOptions>>()));
        builder.Services.AddScoped<IAuthCookieWriter, AuthCookieWriter>();
        builder.Services.AddControllers().AddApplicationPart(typeof(CrmOidcController).Assembly)
            .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new IssuerControllerOnly()));
        _app = builder.Build();
        _app.UseMiddleware<TenantMiddleware>();
        _app.UseMiddleware<CsrfMiddleware>();
        _app.MapControllers();
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    [Fact]
    public async Task Http_token_is_tenant_bound_and_verifiable_using_only_published_JWKS()
    {
        using var response = await Token();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(response.Headers.Pragma, v => v.Name == "no-cache");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(payload.RootElement.TryGetProperty("id_token", out _));
        Assert.False(payload.RootElement.TryGetProperty("refresh_token", out _));
        var raw = payload.RootElement.GetProperty("access_token").GetString()!;
        var jwks = new JsonWebKeySet(await _client.GetStringAsync("/.well-known/jwks.json"));
        var validation = new TokenValidationParameters
        {
            ValidIssuer = _options.Issuer,
            ValidAudience = "CP6.Services",
            ValidTypes = ["at+jwt"],
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidateLifetime = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.Zero
        };
        var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(raw, validation, out _);
        Assert.Equal(_tenant.ToString(), principal.FindFirst("tenant_id")?.Value);
        Assert.Equal("service:crm-worker", principal.FindFirst("sub")?.Value);
        Assert.Equal("crm-worker", principal.FindFirst("client_id")?.Value);
        Assert.InRange(long.Parse(principal.FindFirst("exp")!.Value) - long.Parse(principal.FindFirst("iat")!.Value), 1L, 300L);
        Assert.InRange(payload.RootElement.GetProperty("expires_in").GetInt32(), 1, 300);
        Assert.False(principal.HasClaim(c => c.Type is "sid" or "permission" or "role"));
        using var userInfo = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        userInfo.Headers.Authorization = new("Bearer", raw);
        using var denied = await _client.SendAsync(userInfo);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Only_successful_public_JWKS_is_cacheable_over_HTTP()
    {
        using var jwks = await _client.GetAsync("/.well-known/jwks.json");
        Assert.Equal(HttpStatusCode.OK, jwks.StatusCode);
        Assert.True(jwks.Headers.CacheControl?.Public);
        Assert.True(jwks.Headers.CacheControl?.MustRevalidate);
        Assert.Equal(TimeSpan.FromSeconds(60), jwks.Headers.CacheControl?.MaxAge);
        Assert.False(jwks.Headers.CacheControl?.NoStore);
        using var discovery = await _client.GetAsync("/.well-known/openid-configuration");
        Assert.True(discovery.Headers.CacheControl?.NoStore);
        using var data = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync());
        Assert.Contains(data.RootElement.GetProperty("grant_types_supported").EnumerateArray(), v => v.GetString() == "client_credentials");
        _options.Enabled = false;
        using var disabled = await _client.GetAsync("/.well-known/jwks.json");
        Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
        Assert.True(disabled.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Missing_client_credentials_return_a_noncacheable_Basic_challenge()
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["scope"] = "cp6.services"
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token") { Content = form };
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_client", await Error(response));
        Assert.Contains(response.Headers.WwwAuthenticate, challenge => challenge.Scheme == "Basic");
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    [InlineData("multipart/form-data; boundary=cp6-test")]
    public async Task Token_rejects_non_form_content_types_with_a_noncacheable_OAuth_error(string? contentType)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(
                "grant_type=client_credentials&scope=cp6.services"))
        };
        if (contentType != null)
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("crm-worker:" + _secret)));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await Error(response));
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(response.Headers.Pragma, value => value.Name == "no-cache");
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("expired")]
    [InlineData("missing")]
    public async Task Issuance_rechecks_registered_tenant_in_SQL_even_when_another_tenant_is_active(string change)
    {
        using var before = await Token();
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        await using var db = new SqlConnection(_connection);
        var sql = change switch
        {
            "disabled" => "UPDATE dbo.Sys_Tenants SET Enable=0 WHERE Id=@id",
            "expired" => "UPDATE dbo.Sys_Tenants SET ExpireDate=DATEADD(minute,-1,GETDATE()) WHERE Id=@id",
            _ => "DELETE dbo.Sys_Tenants WHERE Id=@id"
        };
        await db.ExecuteAsync(sql, new { id = _tenant });
        using var denied = await Token();
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.Equal("unauthorized_client", await Error(denied));
        Assert.Equal(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM dbo.Sys_Tenants WHERE Id=@id AND Enable=1", new { id = _otherTenant }));
    }

    [Fact]
    public async Task Unexpired_legacy_local_tenant_expiry_is_not_reinterpreted_as_UTC()
    {
        await using var db = new SqlConnection(_connection);
        await db.ExecuteAsync("UPDATE dbo.Sys_Tenants SET ExpireDate=DATEADD(minute,1,GETDATE()) WHERE Id=@id", new { id = _tenant });
        using var response = await Token();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("tenant_id=00000000-0000-0000-0000-000000000001")]
    [InlineData("audience=CP6.Web")]
    [InlineData("scope=cp6.services")]
    [InlineData("client_secret=ignored")]
    public async Task HTTP_form_cannot_override_identity_or_repeat_fields(string extra)
    {
        using var response = await Token("grant_type=client_credentials&scope=cp6.services&" + extra);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await Error(response));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Oversized_HTTP_body_is_rejected_without_issuing_a_token(bool chunked)
    {
        using var response = await Token("grant_type=client_credentials&scope=" + new string('x', 9000), chunked);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await Error(response));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task SQL_failure_has_no_static_configuration_fallback_or_sensitive_error()
    {
        await using var db = new SqlConnection(_connection);
        await db.ExecuteAsync("DROP TABLE dbo.Sys_Tenants"); // Only this test's generated database.
        using var response = await Token();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("access_token", body);
        Assert.DoesNotContain(_database, body);
        Assert.DoesNotContain(_secret, body);
        Assert.DoesNotContain("Sys_Tenants", body);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Unavailable_SQL_catalog_wrapped_by_EF_returns_a_generic_503()
    {
        var original = _connection;
        var unavailableDatabase = "CP6OidcServiceTest_" + Guid.NewGuid().ToString("N");
        var unavailable = new SqlConnectionStringBuilder(original) { InitialCatalog = unavailableDatabase };
        _connection = unavailable.ConnectionString;
        try
        {
            using var response = await Token();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var error = JsonDocument.Parse(body);
            Assert.Equal("temporarily_unavailable", error.RootElement.GetProperty("error").GetString());
            Assert.DoesNotContain(unavailableDatabase, body);
            Assert.DoesNotContain(_secret, body);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
        finally
        {
            _connection = original;
        }
    }

    [Theory]
    [InlineData("default")]
    [InlineData("untrusted")]
    [InlineData("missing-proto")]
    [InlineData("http-proto")]
    [InlineData("duplicate-proto")]
    [InlineData("comma-proto")]
    [InlineData("spoofed-forwarded-for")]
    public async Task HTTP_proxy_transport_rejects_untrusted_or_ambiguous_forwarding(string problem)
    {
        if (problem == "default") _options.TrustedTokenProxyAddresses = [];
        if (problem is "untrusted" or "spoofed-forwarded-for")
            _options.TrustedTokenProxyAddresses = ["192.0.2.10"];
        var proto = problem switch
        {
            "missing-proto" => [],
            "http-proto" => ["http"],
            "duplicate-proto" => new[] { "https", "https" },
            "comma-proto" => ["https,http"],
            _ => new[] { "https" }
        };

        using var response = await Token(forwardedProto: proto,
            forwardedFor: problem == "spoofed-forwarded-for" ? "192.0.2.10" : null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await Error(response));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private Task<HttpResponseMessage> Token(string form = "grant_type=client_credentials&scope=cp6.services",
        bool chunked = false, string[]? forwardedProto = null, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("crm-worker:" + _secret)));
        request.Headers.TransferEncodingChunked = chunked;
        foreach (var value in forwardedProto ?? ["https"])
            request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", value);
        if (forwardedFor != null) request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        request.Headers.Add("X-Tenant-Id", _otherTenant.ToString());
        request.Headers.Add("X-User-Id", Guid.NewGuid().ToString());
        return Send(request);
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request)
    {
        using (request) return await _client.SendAsync(request);
    }

    private static async Task<string?> Error(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetString();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app != null) { await _app.StopAsync(); await _app.DisposeAsync(); }
        if (_master.Length == 0) return;
        await DropGeneratedDatabaseAsync(_database);
    }

    private async Task DropGeneratedDatabaseAsync(string database)
    {
        if (!Regex.IsMatch(database, "\\ACP6OidcServiceTest_[0-9a-f]{32}\\z"))
            throw new InvalidOperationException("Unsafe test database cleanup target.");
        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(_master);
        await master.ExecuteAsync($"IF DB_ID('{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END");
    }

    private sealed class IssuerControllerOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.Where(t => t.AsType() != typeof(CrmOidcController)).ToArray()) feature.Controllers.Remove(controller);
        }
    }
}
