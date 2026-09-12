using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Configuration;
using CP6.WebApi.Controllers.Integration;
using CP6.WebApi.Controllers.Internal;
using CP6.WebApi.Middleware;
using CP6.WebApi.Localization;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CP6.Tests.ErpIntegration;

/// <summary>
/// Real loopback HTTP, production controllers/registration and RS256/JWKS validation.
/// Discovery/JWKS responses and EF InMemory records are controlled fixtures; these tests do not prove SQL or ERP UAT.
/// </summary>
public sealed class ErpHttpAuthorizationTests
{
    private const string Issuer = "https://identity.cp6.test";
    private const string NativeIssuer = "https://native.cp6.test";
    private static readonly Guid First = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
    private static readonly Guid Second = Guid.Parse("bbbbbbbb-2222-4222-8222-222222222222");
    private const string PartnerPath = "/internal/erp/v1/business-partners/BP-FIRST";
    private const string ReplayPath = "/api/erp-integration/deadletters";
    private const string DeliveryReplayPath = "/api/erp-integration/delivery-deadletters";
    private const string DeliveryResultReplayPath = DeliveryReplayPath + "/results/cccccccc-3333-4333-8333-333333333333/replay";
    private const string DeliveryBridgeReplayPath = DeliveryReplayPath + "/bridges/SO-FIRST/replay";

    [Theory]
    [InlineData("valid", HttpStatusCode.OK)]
    [InlineData("wrong", HttpStatusCode.Unauthorized)]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    public async Task Csrf_enabled_cookie_free_event_ingress_reaches_its_sidecar_and_contract_checks(string? token, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.StartAsync(csrfEnabled: true);
        using var response = await fixture.EventPostAsync("/internal/erp/v1/events", token == "valid" ? new string('x', 32) : token);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            // Real ingress receives {}, so the actual contract guard drops it before creating a SQL transaction.
            Assert.Equal("DROP", body.RootElement.GetProperty("status").GetString());
        }
        await using var queue = fixture.Queue.CreateDbContext();
        Assert.Equal(3, await queue.Inbox.CountAsync());
        Assert.Empty(await queue.Requests.ToArrayAsync());
    }

    [Theory]
    [InlineData("/internal/erp/v1/events-extra")]
    [InlineData("/internal/erp/v1/events/admin")]
    public async Task Csrf_event_exemption_does_not_include_adjacent_or_child_paths(string path)
    {
        await using var fixture = await Fixture.StartAsync(csrfEnabled: true);
        using var response = await fixture.EventPostAsync(path, new string('x', 32));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("E-SEC-010", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Independent_service_scheme_validates_signature_issuer_audience_lifetime_and_claims_over_http()
    {
        await using var fixture = await Fixture.StartAsync();
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme,
            fixture.App.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultScheme);
        Assert.NotNull(await fixture.App.Services.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(ErpReadController.Scheme));
        await fixture.ExpectReadAsync("valid", fixture.ServiceToken(), HttpStatusCode.OK);

        using var wrongRsa = RSA.Create(2048);
        var rejected = new (string Name, string? Token, HttpStatusCode Status)[]
        {
            ("missing", null, HttpStatusCode.Unauthorized),
            ("native bearer cannot substitute for C03.Services", fixture.NativeToken("Admin"), HttpStatusCode.Unauthorized),
            ("audience", fixture.ServiceToken(change: p => p["aud"] = "CP6.Web"), HttpStatusCode.Unauthorized),
            ("issuer", fixture.ServiceToken(change: p => p["iss"] = NativeIssuer), HttpStatusCode.Unauthorized),
            ("signature", fixture.ServiceToken(key: new RsaSecurityKey(wrongRsa) { KeyId = "c03-http" }), HttpStatusCode.Unauthorized),
            ("unsigned", fixture.ServiceToken(unsigned: true), HttpStatusCode.Unauthorized),
            ("HMAC", fixture.ServiceToken(key: fixture.NativeKey, algorithm: SecurityAlgorithms.HmacSha256), HttpStatusCode.Unauthorized),
            ("expired", fixture.ServiceToken(change: p =>
            {
                p["iat"] = DateTimeOffset.UtcNow.AddMinutes(-12).ToUnixTimeSeconds();
                p["nbf"] = DateTimeOffset.UtcNow.AddMinutes(-11).ToUnixTimeSeconds();
                p["exp"] = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
            }), HttpStatusCode.Unauthorized),
            ("missing tenant", fixture.ServiceToken(change: p => p.Remove("tenant_id")), HttpStatusCode.Unauthorized),
            ("duplicate tenant", fixture.ServiceToken(change: p => p["tenant_id"] = new[] { First.ToString("D"), Second.ToString("D") }), HttpStatusCode.Unauthorized),
            ("noncanonical tenant", fixture.ServiceToken(change: p => p["tenant_id"] = First.ToString("D").ToUpperInvariant()), HttpStatusCode.Forbidden),
            ("client tenant mismatch", fixture.ServiceToken(change: p => p["tenant_id"] = Second.ToString("D")), HttpStatusCode.Forbidden),
            ("subject", fixture.ServiceToken(change: p => p["sub"] = "service:another-client"), HttpStatusCode.Forbidden),
            ("scope", fixture.ServiceToken(change: p => p["scope"] = "openid"), HttpStatusCode.Forbidden),
            ("missing client", fixture.ServiceToken(change: p => p.Remove("client_id")), HttpStatusCode.Forbidden),
            ("duplicate client", fixture.ServiceToken(change: p => p["client_id"] = new[] { "reader-a", "reader-a" }), HttpStatusCode.Forbidden),
            ("excluded client", fixture.ServiceToken(client: "not-reader"), HttpStatusCode.Forbidden),
            ("noncanonical jti", fixture.ServiceToken(change: p => p["jti"] = fixture.FirstJti.ToString("N")), HttpStatusCode.Forbidden)
        };
        foreach (var item in rejected) await fixture.ExpectReadAsync(item.Name, item.Token, item.Status);
        await fixture.ExpectReadAsync("valid after rejected tokens", fixture.ServiceToken(), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Read_rechecks_recorded_revocation_and_current_tenant_client_authority()
    {
        await using var fixture = await Fixture.StartAsync();
        var token = fixture.ServiceToken();
        await fixture.ExpectReadAsync("unrecorded", fixture.ServiceToken(change: p => p["jti"] = Guid.NewGuid().ToString("D")), HttpStatusCode.Forbidden);
        foreach (var failure in new[] { "revoked", "expired", "record-tenant", "record-client", "record-issuer" })
        {
            await fixture.ChangeDbAsync(async db =>
            {
                var record = await db.CrmServiceTokenRecords.SingleAsync(x => x.Jti == fixture.FirstJti.ToString("D"));
                if (failure == "revoked") record.RevokedAtUtc = DateTimeOffset.UtcNow;
                if (failure == "expired") record.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
                if (failure == "record-tenant") record.TenantId = Second;
                if (failure == "record-client") record.ClientId = "reader-b";
                // Issuer participates in the record key, so replace it without mutating a tracked key.
                if (failure == "record-issuer")
                {
                    db.CrmServiceTokenRecords.Remove(record);
                    db.CrmServiceTokenRecords.Add(new() { Issuer = NativeIssuer, Jti = record.Jti, ClientId = record.ClientId,
                        TenantId = record.TenantId, ExpiresAtUtc = record.ExpiresAtUtc });
                }
            });
            await fixture.ExpectReadAsync(failure, token, HttpStatusCode.Forbidden);
            await fixture.ResetFirstRecordAsync();
        }

        await fixture.ChangeDbAsync(async db => (await db.Sys_Tenants.SingleAsync(x => x.Id == First)).Enable = false);
        await fixture.ExpectReadAsync("disabled tenant", token, HttpStatusCode.Forbidden);
        await fixture.ChangeDbAsync(async db =>
        {
            var tenant = await db.Sys_Tenants.SingleAsync(x => x.Id == First);
            tenant.Enable = true; tenant.ExpireDate = DateTime.Now.AddMinutes(-1);
        });
        await fixture.ExpectReadAsync("expired tenant", token, HttpStatusCode.Forbidden);
        await fixture.ChangeDbAsync(async db => (await db.Sys_Tenants.SingleAsync(x => x.Id == First)).ExpireDate = null);
        fixture.Oidc.Organizations[0].CrmEnabled = false;
        await fixture.ExpectReadAsync("tenant no longer CRM enabled", token, HttpStatusCode.Forbidden);
        fixture.Oidc.Organizations[0].CrmEnabled = true;
        fixture.Oidc.ServiceClients[0].Enabled = false;
        await fixture.ExpectReadAsync("disabled client", token, HttpStatusCode.Forbidden);
        fixture.Oidc.ServiceClients[0].Enabled = true;
        fixture.Oidc.ServiceClients[0].AllowedScopes.Clear();
        await fixture.ExpectReadAsync("client scope removed", token, HttpStatusCode.Forbidden);
        fixture.Oidc.ServiceClients[0].AllowedScopes.Add("cp6.services");
        await fixture.ExpectReadAsync("authority restored", token, HttpStatusCode.OK);
        using var second = await fixture.ReadAsync("/internal/erp/v1/business-partners/BP-SECOND", fixture.ServiceToken(Second, "reader-b"), Second);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Request_metadata_is_single_valued_tenant_bound_and_not_overridden_by_query()
    {
        await using var fixture = await Fixture.StartAsync();
        foreach (var failure in new[] { "no-tenant", "wrong-tenant", "duplicate-tenant", "no-correlation", "duplicate-correlation", "invalid-correlation", "long-correlation", "query", "long-key" })
        {
            var path = failure switch
            {
                "query" => PartnerPath + "?tenantId=" + Second,
                "long-key" => "/internal/erp/v1/business-partners/" + new string('x', 21),
                _ => PartnerPath
            };
            using var response = await fixture.ReadAsync(path, fixture.ServiceToken(), change: request =>
            {
                if (failure is "no-tenant" or "wrong-tenant" or "duplicate-tenant") request.Headers.Remove("tenantid");
                if (failure == "wrong-tenant") request.Headers.Add("tenantid", Second.ToString("D"));
                if (failure == "duplicate-tenant") request.Headers.TryAddWithoutValidation("tenantid", new[] { First.ToString("D"), First.ToString("D") });
                if (failure is "no-correlation" or "duplicate-correlation" or "invalid-correlation" or "long-correlation") request.Headers.Remove("correlationid");
                if (failure == "duplicate-correlation") request.Headers.TryAddWithoutValidation("correlationid", new[] { "correlation-1", "correlation-2" });
                if (failure == "invalid-correlation") request.Headers.TryAddWithoutValidation("correlationid", "with spaces");
                if (failure == "long-correlation") request.Headers.Add("correlationid", new string('c', 129));
            });
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, failure + ": " + response.StatusCode);
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            Assert.Equal("C03_REQUEST_METADATA_INVALID", body.RootElement.GetProperty("code").GetString());
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
        using var valid = await fixture.ReadAsync(PartnerPath, fixture.ServiceToken());
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal("correlation-1", valid.Headers.GetValues("correlationid").Single());
    }

    [Fact]
    public async Task All_read_routes_return_only_matching_tenant_key_and_allowlisted_projection_fields()
    {
        await using var fixture = await Fixture.StartAsync();
        foreach (var (route, key) in new[] { ("business-partners", "BP"), ("quotations", "Q"), ("orders", "SO") })
        {
            using var own = await fixture.ReadAsync($"/internal/erp/v1/{route}/{key}-FIRST", fixture.ServiceToken());
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
            var text = await own.Content.ReadAsStringAsync();
            using var body = JsonDocument.Parse(text);
            Assert.Equal(First, body.RootElement.GetProperty("tenantId").GetGuid());
            Assert.Equal(key + "-FIRST", body.RootElement.GetProperty("key").GetString());
            Assert.Equal("AAAAAAAAAAE=", body.RootElement.GetProperty("version").GetString());
            Assert.DoesNotContain("OTHER_PRIVATE", text);
            Assert.DoesNotContain("address-secret", text);
            Assert.DoesNotContain("tax-secret", text);
            Assert.False(body.RootElement.TryGetProperty("details", out _));
            if (route == "orders") Assert.Equal(25m, body.RootElement.GetProperty("bookedAmount").GetDecimal());
            foreach (var denied in new[] { key + "-SECOND", key.ToLowerInvariant() + "-first", key + "-MISSING" })
            {
                using var response = await fixture.ReadAsync($"/internal/erp/v1/{route}/{denied}", fixture.ServiceToken());
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.DoesNotContain("OTHER_PRIVATE", await response.Content.ReadAsStringAsync());
            }
        }
        await fixture.ChangeDbAsync(async db =>
        {
            (await db.BusinessPartners.IgnoreQueryFilters().SingleAsync(x => x.BpCd == "BP-FIRST")).IsDeleted = true;
            (await db.Quotations.IgnoreQueryFilters().SingleAsync(x => x.QtnNo == "Q-FIRST")).IsDeleted = true;
            (await db.Orders.IgnoreQueryFilters().SingleAsync(x => x.WebOrderNo == "SO-FIRST")).IsDeleted = true;
        });
        foreach (var path in new[] { PartnerPath, "/internal/erp/v1/quotations/Q-FIRST", "/internal/erp/v1/orders/SO-FIRST" })
        {
            using var response = await fixture.ReadAsync(path, fixture.ServiceToken());
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task Replay_requires_human_admin_and_never_accepts_service_read_credentials()
    {
        await using var fixture = await Fixture.StartAsync();
        foreach (var (name, token, status) in new[]
        {
            ("unauthenticated", (string?)null, HttpStatusCode.Unauthorized),
            ("human nonadmin", fixture.NativeToken("User"), HttpStatusCode.Forbidden),
            ("service reader", fixture.ServiceToken(change: p => p["role"] = "Admin"), HttpStatusCode.Unauthorized),
            ("service subject with native audience and admin role", fixture.NativeToken("Admin", service: true), HttpStatusCode.Forbidden),
            ("unmapped tenant admin", fixture.NativeToken("Admin", tenant: Guid.NewGuid()), HttpStatusCode.Forbidden)
        })
        {
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post })
            {
                using var response = await fixture.ReplayAsync(method, token);
                Assert.True(response.StatusCode == status, name + " " + method + ": " + response.StatusCode);
            }
        }
        foreach (var role in new[] { "Admin", "1" })
        {
            using var list = await fixture.ReplayAsync(HttpMethod.Get, fixture.NativeToken(role));
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var body = await JsonDocument.ParseAsync(await list.Content.ReadAsStreamAsync());
            var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal("first-deadletter", item.GetProperty("messageId").GetString());
            Assert.False(item.TryGetProperty("payload", out _));
            Assert.DoesNotContain("OTHER_PRIVATE", body.RootElement.ToString());
            // An invalid command proves the actual admin action was reached without pretending to perform a SQL replay.
            using var post = await fixture.ReplayAsync(HttpMethod.Post, fixture.NativeToken(role));
            Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
            using var error = await JsonDocument.ParseAsync(await post.Content.ReadAsStreamAsync());
            Assert.Equal("C03_REPLAY_INPUT_INVALID", error.RootElement.GetProperty("code").GetString());
        }
        using var otherList = await fixture.ReplayAsync(HttpMethod.Get, fixture.NativeToken("Admin", tenant: Second));
        using var other = await JsonDocument.ParseAsync(await otherList.Content.ReadAsStreamAsync());
        Assert.Equal("second-deadletter", Assert.Single(other.RootElement.GetProperty("items").EnumerateArray()).GetProperty("messageId").GetString());
        await using var queue = fixture.Queue.CreateDbContext();
        Assert.Empty(await queue.ReplayAudits.ToArrayAsync());
        Assert.Equal(2, await queue.Inbox.CountAsync(x => x.Status == ErpInboxStatus.DeadLettered));
    }

    [Theory]
    [InlineData(DeliveryReplayPath)]
    [InlineData(DeliveryResultReplayPath)]
    [InlineData(DeliveryBridgeReplayPath)]
    public async Task Delivery_replay_routes_reject_anonymous_nonadmin_and_nonhuman_actors(string path)
    {
        await using var fixture = await Fixture.StartAsync();
        var method = path == DeliveryReplayPath ? HttpMethod.Get : HttpMethod.Post;
        foreach (var (name, token, status) in new[]
        {
            ("unauthenticated", (string?)null, HttpStatusCode.Unauthorized),
            ("human nonadmin", fixture.NativeToken("User"), HttpStatusCode.Forbidden),
            ("service reader with admin claim", fixture.ServiceToken(change: p => p["role"] = "Admin"), HttpStatusCode.Unauthorized),
            ("native service identity", fixture.NativeToken("Admin", service: true), HttpStatusCode.Forbidden),
            ("native service subject alone", fixture.NativeToken("Admin", change: claims =>
            {
                claims.RemoveAll(c => c.Type == "sub");
                claims.Add(new("sub", "service:reader-a"));
            }), HttpStatusCode.Forbidden),
            ("native client claim alone", fixture.NativeToken("Admin", change: claims => claims.Add(new("client_id", "reader-a"))), HttpStatusCode.Forbidden),
            ("native admin without actor", fixture.NativeToken("Admin", change: claims => claims.RemoveAll(c => c.Type == "sub")), HttpStatusCode.Forbidden),
            ("unmapped tenant admin", fixture.NativeToken("Admin", tenant: Guid.NewGuid()), HttpStatusCode.Forbidden)
        })
        {
            using var response = await fixture.DeliveryReplayAsync(method, path, token);
            Assert.True(response.StatusCode == status, name + " " + path + ": " + response.StatusCode);
        }
        await using var queue = fixture.Queue.CreateDbContext();
        Assert.Empty(await queue.Set<ErpDeliveryReplayAudit>().ToArrayAsync());
    }

    [Theory]
    [InlineData(DeliveryResultReplayPath)]
    [InlineData(DeliveryBridgeReplayPath)]
    public async Task Delivery_replay_admin_bearers_reach_the_real_service_input_guard_with_csrf_enabled(string path)
    {
        await using var fixture = await Fixture.StartAsync(csrfEnabled: true);
        foreach (var role in new[] { "Admin", "1" })
        {
            using var response = await fixture.DeliveryReplayAsync(HttpMethod.Post, path, fixture.NativeToken(role));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            Assert.Equal("C03_DELIVERY_REPLAY_INPUT_INVALID", body.RootElement.GetProperty("code").GetString());
        }
        // The real service rejects only the empty operation id before creating a SQL context or changing a delivery.
        // SQL listing/rescheduling is exercised by its separate real-SQL suite, not by this HTTP fixture.
        await using var queue = fixture.Queue.CreateDbContext();
        Assert.Empty(await queue.Set<ErpDeliveryReplayAudit>().ToArrayAsync());
    }

    [Theory]
    [InlineData(DeliveryResultReplayPath)]
    [InlineData(DeliveryBridgeReplayPath)]
    public async Task Delivery_replay_admin_cookies_require_csrf_before_the_real_service_input_guard(string path)
    {
        await using var fixture = await Fixture.StartAsync(csrfEnabled: true);
        foreach (var role in new[] { "Admin", "1" })
        {
            var token = fixture.NativeToken(role);
            using var denied = await fixture.DeliveryReplayAsync(HttpMethod.Post, path, token, cookie: true);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var deniedBody = await JsonDocument.ParseAsync(await denied.Content.ReadAsStreamAsync());
            Assert.Equal("E-SEC-010", deniedBody.RootElement.GetProperty("code").GetString());

            // The same cookie authenticates when the CSRF pair is present; a 500 is never treated as authorization success.
            using var allowed = await fixture.DeliveryReplayAsync(HttpMethod.Post, path, token, cookie: true, csrfToken: "csrf-fixture");
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
            using var allowedBody = await JsonDocument.ParseAsync(await allowed.Content.ReadAsStreamAsync());
            Assert.Equal("C03_DELIVERY_REPLAY_INPUT_INVALID", allowedBody.RootElement.GetProperty("code").GetString());
        }
        await using var queue = fixture.Queue.CreateDbContext();
        Assert.Empty(await queue.Set<ErpDeliveryReplayAudit>().ToArrayAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly RSA rsa = RSA.Create(2048);
        private readonly DbContextOptions<CP6Context> dbOptions = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase("c03-http-" + Guid.NewGuid().ToString("N")).Options;
        private HttpClient backchannel = null!;
        private HttpClient http = null!;
        public WebApplication App { get; private set; } = null!;
        public Guid FirstJti { get; } = Guid.NewGuid();
        private Guid SecondJti { get; } = Guid.NewGuid();
        public SymmetricSecurityKey NativeKey { get; } = new(RandomNumberGenerator.GetBytes(32));
        public QueueFactory Queue { get; } = new();
        public CrmOidcOptions Oidc { get; } = new()
        {
            Enabled = true, Issuer = ErpHttpAuthorizationTests.Issuer,
            Organizations = [new() { TenantId = First, Slug = "first", Region = "us", CrmEnabled = true },
                new() { TenantId = Second, Slug = "second", Region = "eu", CrmEnabled = true }],
            ServiceClients = [ServiceClient("reader-a", First), ServiceClient("reader-b", Second), ServiceClient("not-reader", First)]
        };

        public static async Task<Fixture> StartAsync(bool csrfEnabled = false)
        {
            var fixture = new Fixture();
            try { await fixture.InitializeAsync(csrfEnabled); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }

        private async Task InitializeAsync(bool csrfEnabled)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", Args = [] });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(ConfigurationValues());
            builder.Services.AddSingleton(Oidc);
            builder.Services.Configure<SecurityOptions>(options => options.Csrf.Enabled = csrfEnabled);
            builder.Services.AddScoped<ITenantContext, TenantContext>();
            builder.Services.AddScoped(p => new CP6Context(dbOptions, p.GetRequiredService<ITenantContext>()));
            builder.Services.AddScoped<ICrmOidcServiceDirectory>(p => new CrmOidcDirectory(p.GetRequiredService<CP6Context>(),
                p.GetRequiredService<ITenantContext>(), null!, null!, Oidc));
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new()
                {
                    ValidateIssuer = true, ValidateAudience = true, ValidateIssuerSigningKey = true, ValidateLifetime = true,
                    ValidIssuer = NativeIssuer, ValidAudience = "CP6.Web", IssuerSigningKey = NativeKey,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256], RoleClaimType = "role", NameClaimType = "sub"
                };
                // Use the native cookie token extraction from Program; the JWT handler still validates its signature.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (string.IsNullOrEmpty(context.Token)) context.Token = context.Request.Cookies[AuthCookieWriter.AccessCookie];
                        return Task.CompletedTask;
                    }
                };
            });
            builder.Services.AddErpIntegration(builder.Configuration, Oidc);
            // HTTP boundary tests must not start SQL, sidecar or bridge workers.
            foreach (var registration in builder.Services.Where(d => d.ServiceType == typeof(IHostedService) &&
                         d.ImplementationType?.Namespace == "CP6.WebApi.BackgroundServices").ToArray())
                builder.Services.Remove(registration);
            builder.Services.RemoveAll<IDbContextFactory<ErpIntegrationContext>>();
            builder.Services.AddSingleton<IDbContextFactory<ErpIntegrationContext>>(Queue);
            backchannel = new HttpClient(new DiscoveryResponses(rsa.ExportParameters(false)));
            builder.Services.Configure<JwtBearerOptions>(ErpReadController.Scheme, options => options.Backchannel = backchannel);
            builder.Services.AddAuthorization();
            builder.Services.AddControllers().AddApplicationPart(typeof(ErpReadController).Assembly)
                .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new BoundaryControllersOnly()));
            App = builder.Build();
            App.UseAuthentication();
            App.UseMiddleware<TenantMiddleware>();
            // Match production ordering: authentication/tenant, safe business-error mapping, CSRF, authorization.
            App.Use(async (context, next) =>
            {
                try { await next(); }
                catch (BizException exception)
                {
                    context.Response.StatusCode = exception.HttpStatus;
                    await context.Response.WriteAsJsonAsync(new { code = exception.Code });
                }
            });
            App.UseMiddleware<CsrfMiddleware>();
            App.UseAuthorization();
            App.MapControllers();
            App.MapErpIntegrationEvents();
            await SeedAsync();
            await App.StartAsync();
            var address = App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(15) };
        }

        public string ServiceToken(Guid? tenant = null, string client = "reader-a", Action<JwtPayload>? change = null,
            SecurityKey? key = null, bool unsigned = false, string algorithm = SecurityAlgorithms.RsaSha256)
        {
            var now = DateTimeOffset.UtcNow;
            var payload = new JwtPayload(Issuer, "CP6.Services",
                [new("sub", "service:" + client), new("tenant_id", (tenant ?? First).ToString("D")),
                    new("client_id", client), new("scope", "cp6.services"),
                    new("jti", (client == "reader-b" ? SecondJti : FirstJti).ToString("D"))],
                now.AddMinutes(-1).UtcDateTime, now.AddMinutes(5).UtcDateTime, now.UtcDateTime);
            change?.Invoke(payload);
            var header = unsigned ? new JwtHeader() : new JwtHeader(new SigningCredentials(key ?? new RsaSecurityKey(rsa) { KeyId = "c03-http" }, algorithm));
            header["typ"] = "at+jwt";
            return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
        }

        public string NativeToken(string role, bool service = false, Guid? tenant = null, Action<List<Claim>>? change = null)
        {
            var claims = new List<Claim> { new("sub", service ? "service:reader-a" : "erp-human-1"),
                new("role", role), new("tenant_id", (tenant ?? First).ToString("D")) };
            if (service) claims.Add(new("client_id", "reader-a"));
            change?.Invoke(claims);
            return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(NativeIssuer, "CP6.Web", claims,
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5), new SigningCredentials(NativeKey, SecurityAlgorithms.HmacSha256)));
        }

        public async Task ExpectReadAsync(string name, string? token, HttpStatusCode expected)
        {
            using var response = await ReadAsync(PartnerPath, token);
            Assert.True(response.StatusCode == expected, name + ": expected " + expected + ", got " + response.StatusCode);
            if (expected != HttpStatusCode.OK)
            {
                var text = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain("First display", text);
                Assert.DoesNotContain("address-secret", text);
            }
        }

        public async Task<HttpResponseMessage> ReadAsync(string path, string? token, Guid? tenant = null,
            Action<HttpRequestMessage>? change = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (token is not null) request.Headers.Authorization = new("Bearer", token);
            request.Headers.Add("tenantid", (tenant ?? First).ToString("D"));
            request.Headers.Add("correlationid", "correlation-1");
            change?.Invoke(request);
            return await http.SendAsync(request);
        }

        public async Task<HttpResponseMessage> ReplayAsync(HttpMethod method, string? token)
        {
            using var request = new HttpRequestMessage(method, method == HttpMethod.Get ? ReplayPath : ReplayPath + "/first-deadletter/replay");
            if (token is not null) request.Headers.Authorization = new("Bearer", token);
            if (method == HttpMethod.Post) request.Content = JsonContent.Create(new ErpReplayRequest(Guid.Empty,
                [0, 0, 0, 0, 0, 0, 0, 1], new string('a', 64), "dependency-recovered"));
            return await http.SendAsync(request);
        }

        public async Task<HttpResponseMessage> EventPostAsync(string path, string? sidecarToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            { Content = new StringContent("{}", Encoding.UTF8, "application/cloudevents+json") };
            if (sidecarToken is not null) request.Headers.Add("dapr-api-token", sidecarToken);
            request.Headers.Add("__topic", ErpEventContracts.RequestTopic);
            request.Headers.Add("__key", $"{First:D}:{Guid.NewGuid():D}");
            request.Headers.Add("pubsubname", "cp6-kafka-pubsub");
            Assert.False(request.Headers.Contains("Authorization"));
            Assert.False(request.Headers.Contains("Cookie"));
            return await http.SendAsync(request);
        }

        public async Task<HttpResponseMessage> DeliveryReplayAsync(HttpMethod method, string path, string? token,
            bool cookie = false, string? csrfToken = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (token is not null)
            {
                if (cookie)
                {
                    request.Headers.Add("Cookie", AuthCookieWriter.AccessCookie + "=" + token +
                        (csrfToken is null ? "" : "; " + AuthCookieWriter.CsrfCookie + "=" + csrfToken));
                    if (csrfToken is not null) request.Headers.Add("X-CSRF-Token", csrfToken);
                }
                else request.Headers.Authorization = new("Bearer", token);
            }
            if (method == HttpMethod.Post) request.Content = JsonContent.Create(new ErpReplayRequest(Guid.Empty,
                [0, 0, 0, 0, 0, 0, 0, 1], new string('a', 64), "dependency-recovered"));
            return await http.SendAsync(request);
        }

        public async Task ChangeDbAsync(Func<CP6Context, Task> change)
        {
            await using var db = new CP6Context(dbOptions);
            await change(db); await db.SaveChangesAsync();
        }

        public async Task ResetFirstRecordAsync() => await ChangeDbAsync(async db =>
        {
            var row = await db.CrmServiceTokenRecords.SingleOrDefaultAsync(x => x.Jti == FirstJti.ToString("D"));
            if (row is null || row.Issuer != Issuer)
            {
                if (row is not null) db.CrmServiceTokenRecords.Remove(row);
                db.CrmServiceTokenRecords.Add(TokenRecord(First, "reader-a", FirstJti));
            }
            else
            {
                row.TenantId = First; row.ClientId = "reader-a"; row.RevokedAtUtc = null;
                row.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10);
            }
        });

        private async Task SeedAsync()
        {
            await ChangeDbAsync(db =>
            {
                foreach (var (tenant, suffix) in new[] { (First, "FIRST"), (Second, "SECOND") })
                {
                    db.Sys_Tenants.Add(new() { Id = tenant, TenantCode = suffix, TenantName = suffix, Enable = true });
                    db.BusinessPartners.Add(new() { TenantId = tenant, BpCd = "BP-" + suffix, BpName = tenant == First ? "First display" : "OTHER_PRIVATE",
                        BaseCd = "B01", CustomerFlg = true, Status = 1, CurrencyCd = "USD", Addr1 = "address-secret", Ein = "tax-secret", RowVersion = [0, 0, 0, 0, 0, 0, 0, 1] });
                    db.Quotations.Add(new() { TenantId = tenant, QtnNo = "Q-" + suffix, CustomerCd = "BP-" + suffix,
                        CustomerName = tenant == First ? "First display" : "OTHER_PRIVATE", BaseCd = "B01", StaffCd = "S01", TotalAmount = 25m, CurrencyCd = "USD",
                        RowVersion = [0, 0, 0, 0, 0, 0, 0, 1] });
                    db.Orders.Add(new() { TenantId = tenant, WebOrderNo = "SO-" + suffix, CustomerCd = "BP-" + suffix,
                        OrderType = "10", CurrencyCd = "USD", RowVersion = [0, 0, 0, 0, 0, 0, 0, 1] });
                    db.OrderDetails.Add(new() { TenantId = tenant, WebOrderNo = "SO-" + suffix, WebOrderDetailNo = 1, ProductCd = "PRODUCT-1", Amount = 25m });
                }
                // A mismatched child must not leak into an otherwise valid parent's financial projection.
                db.OrderDetails.Add(new() { TenantId = Second, WebOrderNo = "SO-FIRST", WebOrderDetailNo = 2, ProductCd = "ALIEN", Amount = 999999m });
                db.CrmServiceTokenRecords.AddRange(TokenRecord(First, "reader-a", FirstJti), TokenRecord(Second, "reader-b", SecondJti));
                return Task.CompletedTask;
            });
            await using var queue = Queue.CreateDbContext();
            queue.Inbox.AddRange(new ErpInboxReceipt { TenantId = First, MessageId = "first-deadletter", EventType = ErpEventContracts.OrderRequested,
                    AggregateId = Guid.NewGuid(), AggregateVersion = 1, Status = ErpInboxStatus.DeadLettered, Payload = Encoding.UTF8.GetBytes("never return raw payload"), PayloadSha256 = new string('a', 64) },
                new ErpInboxReceipt { TenantId = Second, MessageId = "second-deadletter", EventType = ErpEventContracts.OrderRequested,
                    AggregateId = Guid.NewGuid(), AggregateVersion = 1, Status = ErpInboxStatus.DeadLettered, Payload = Encoding.UTF8.GetBytes("OTHER_PRIVATE"), PayloadSha256 = new string('b', 64) },
                new ErpInboxReceipt { TenantId = First, MessageId = "already-processed", EventType = ErpEventContracts.OrderRequested,
                    AggregateId = Guid.NewGuid(), AggregateVersion = 1, Status = ErpInboxStatus.Processed, PayloadSha256 = new string('c', 64) });
            await queue.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            http?.Dispose();
            if (App is not null) { await App.StopAsync(); await App.DisposeAsync(); }
            backchannel?.Dispose(); rsa.Dispose();
        }

        private static CrmServiceTokenRecord TokenRecord(Guid tenant, string client, Guid jti) => new()
        { Issuer = ErpHttpAuthorizationTests.Issuer, TenantId = tenant, ClientId = client, Jti = jti.ToString("D"), ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        private static CrmOidcServiceClient ServiceClient(string client, Guid tenant) => new()
        { ClientId = client, TenantId = tenant, Enabled = true, AllowedScopes = ["cp6.services"] };

        private static Dictionary<string, string?> ConfigurationValues() => new()
        {
            ["ErpIntegration:Enabled"] = "true", ["ErpIntegration:Tenants:" + First] = "us", ["ErpIntegration:Tenants:" + Second] = "eu",
            ["ErpIntegration:ReaderClientIds:0"] = "reader-a", ["ErpIntegration:ReaderClientIds:1"] = "reader-b",
            ["ErpIntegration:DaprAppToken"] = new string('x', 32), ["CrmIdentity:Enabled"] = "true",
            ["ErpIntegration:DaprApiToken"] = new string('y', 32),
            ["CrmIdentity:Tenants:" + First] = "us", ["CrmIdentity:Tenants:" + Second] = "eu",
            ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=unused-http-fixture;Integrated Security=True;MultipleActiveResultSets=False"
        };
    }

    private sealed class QueueFactory : IDbContextFactory<ErpIntegrationContext>
    {
        private readonly DbContextOptions<ErpIntegrationContext> options = new DbContextOptionsBuilder<ErpIntegrationContext>()
            .UseInMemoryDatabase("c03-replay-http-" + Guid.NewGuid().ToString("N")).Options;
        public ErpIntegrationContext CreateDbContext() => new(options);
    }

    private sealed class DiscoveryResponses(RSAParameters key) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object body = request.RequestUri?.AbsoluteUri switch
            {
                Issuer + "/.well-known/openid-configuration" => new { issuer = Issuer, jwks_uri = Issuer + "/.well-known/jwks.json" },
                Issuer + "/.well-known/jwks.json" => new { keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid = "c03-http", n = Base64UrlEncoder.Encode(key.Modulus!), e = Base64UrlEncoder.Encode(key.Exponent!) } } },
                _ => throw new InvalidOperationException("Unexpected OIDC fixture request.")
            };
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = JsonContent.Create(body) };
            response.Headers.CacheControl = new() { MaxAge = TimeSpan.FromMinutes(5) };
            return Task.FromResult(response);
        }
    }

    private sealed class BoundaryControllersOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.Where(c => c.AsType() != typeof(ErpReadController) &&
                         c.AsType() != typeof(ErpReplayController) && c.AsType() != typeof(ErpDeliveryReplayController)).ToArray())
                feature.Controllers.Remove(controller);
        }
    }
}
