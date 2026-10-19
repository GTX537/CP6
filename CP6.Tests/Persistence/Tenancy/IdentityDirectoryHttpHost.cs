using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts.Context;
using CP6.WebApi.Configuration;
using CP6.WebApi.Controllers.Internal;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace CP6.Tests.Persistence.Tenancy;

// Isolated composition fixture, not a production configuration/registration entry point.
// Actual JWT validation/controller/current authority SQL are used. The trusted source and
// registration below are fixture-owned; auth SQL still uses a separate control connection.
internal sealed class IdentityDirectoryHttpHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly IdentityDirectoryPostgresFixture _postgres;
    private readonly CrmOidcCrypto _signer;
    internal HttpClient Client { get; }
    internal CrmOidcOptions Options { get; }
    internal CrmIdentityRuntime Runtime { get; }
    internal TenantConnectionManager Manager { get; }
    private IdentityDirectoryHttpHost(WebApplication app, HttpClient client, IdentityDirectoryPostgresFixture postgres,
        CrmOidcOptions options, CrmIdentityRuntime runtime, TenantConnectionManager manager)
    { _app = app; Client = client; _postgres = postgres; Options = options; Runtime = runtime; Manager = manager; _signer = new(options); }

    internal static async Task<IdentityDirectoryHttpHost> Start(IdentityDirectoryPostgresFixture postgres)
    {
        var runtime = IdentityDirectoryPostgresFixture.Runtime();
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "directory-fixture" };
        var options = new CrmOidcOptions
        {
            Enabled = true, Issuer = runtime.Options.Issuer, ActiveKeyId = key.KeyId,
            Keys = [new() { Kid = key.KeyId, Pem = rsa.ExportRSAPrivateKeyPem() }],
            Organizations = new[] { TenantStorageContext.A, TenantStorageContext.B }.Select(t =>
                new CrmOidcOrganization { TenantId = t, Slug = t.ToString("N"), Region = "us", CrmEnabled = true }).ToList(),
            ServiceClients = new[] { ("reader-a", TenantStorageContext.A), ("reader-b", TenantStorageContext.B), ("not-reader", TenantStorageContext.A) }
                .Select(x => new CrmOidcServiceClient { ClientId = x.Item1, TenantId = x.Item2, Enabled = true, AllowedScopes = ["cp6.services"] }).ToList()
        };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0)); builder.Logging.ClearProviders();
        await using var control = postgres.OwnerContext(TenantStorageContext.A);
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = control.Database.GetConnectionString(),
            ["Database:Provider"] = "PostgreSql", ["CrmIdentity:Enabled"] = "true",
            ["CrmIdentity:Tenants:" + TenantStorageContext.A] = "us", ["CrmIdentity:Tenants:" + TenantStorageContext.B] = "us",
            ["CrmIdentity:ProjectionReaderClientIds:0"] = "reader-a", ["CrmIdentity:ProjectionReaderClientIds:1"] = "reader-b"
        });
        var source = new DirectorySource(options);
        var manager = new TenantConnectionManager("main", source.Accessor, TenantIdentityDirectoryReaderPostgresTests.Catalog(),
            new TenantStorageCredentials { Resolve = (binding, _) => ValueTask.FromResult(binding.TenantId == TenantStorageContext.A ? postgres.PasswordA : postgres.PasswordB) }, new(2, 2, 1));
        builder.Services.AddSingleton(options);
        builder.Services.AddScoped<ITenantContext>(_ => new TenantContext { CurrentTenantId = source.Accessor.CurrentTrusted?.Context.TenantId ?? TenantContext.DefaultTenant });
        builder.Services.AddScoped(sp => postgres.Server.Context(sp.GetRequiredService<ITenantContext>()));
        builder.Services.AddScoped<ICrmOidcServiceDirectory, CrmOidcDirectory>();
        builder.Services.AddSingleton(Mock.Of<ITokenBlacklistService>(MockBehavior.Strict));
        builder.Services.AddSingleton(Mock.Of<IPasswordPolicyService>(MockBehavior.Strict));
        builder.Services.AddCrmIdentityEvents(builder.Configuration, options, new(DatabaseProvider.PostgreSql));
        builder.Services.RemoveAll<IHostedService>();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.RemoveAll<IIdentitySnapshotReader>();
        builder.Services.AddScoped<IIdentitySnapshotReader>(sp => new TenantIdentityDirectoryReader(manager,
            sp.GetRequiredService<CrmIdentityRuntime>(), sp.GetRequiredService<IDataProtectionProvider>()));
        // Use the actual bearer handler/profile with an explicit fixture trust key; no network JWKS.
        builder.Services.PostConfigure<JwtBearerOptions>(CrmIdentityController.Scheme, bearer =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = options.Issuer }; configuration.SigningKeys.Add(key);
            bearer.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            bearer.MapInboundClaims = false;
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(CrmIdentityController).Assembly);
        var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization();
        app.Use(async (http, next) =>
        {
            var input = await source.VerifyAsync(http, http.RequestAborted);
            // Invalid inputs still reach the real controller's challenge/forbid checks.
            if (input is null) { await next(http); return; }
            await new TrustedContextExecutorV1(source, source.Accessor).ExecuteAsync(
                ContextEntryKindV1.Http, http, source, _ => next(http), http.RequestAborted);
        });
        app.MapControllers();
        try
        {
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new HttpClient { BaseAddress = new(address), Timeout = TimeSpan.FromSeconds(15) }, postgres,
                options, app.Services.GetRequiredService<CrmIdentityRuntime>(), manager);
        }
        catch { await app.DisposeAsync(); await manager.DisposeAsync(); throw; }
    }

    internal async Task<(string Token, string Jti)> Token(Guid tenant, string? client = null, bool record = true,
        string audience = "CP6.Services", DateTimeOffset? expiry = null, string? tenantClaim = null, bool duplicateTenant = false)
    {
        client ??= tenant == TenantStorageContext.A ? "reader-a" : "reader-b";
        var id = Guid.NewGuid().ToString("D"); var expires = expiry ?? DateTimeOffset.UtcNow.AddMinutes(5);
        List<Claim> claims = [new("sub", "service:" + client), new("client_id", client), new("tenant_id", tenantClaim ?? tenant.ToString("D")),
            new("jti", id), new("scope", "cp6.services")];
        if (duplicateTenant) claims.Add(new("tenant_id", tenant.ToString("D")));
        using var historical = expires < DateTimeOffset.UtcNow ? new CrmOidcCrypto(Options, new SigningClock(expires.AddMinutes(-1))) : null;
        var token = (historical ?? _signer).Sign(audience, claims, expires, "at+jwt");
        if (record)
        {
            await using var db = _postgres.OwnerContext(tenant);
            await new CrmServiceTokenRecordStore(db, Runtime).RecordAsync(Options.Issuer, client, tenant, id, expires, default);
        }
        return (token, id);
    }
    internal async Task<HttpResponseMessage> Get(string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }
    public async ValueTask DisposeAsync()
    {
        Client.Dispose(); await _app.StopAsync(); await _app.DisposeAsync(); await Manager.DisposeAsync(); _signer.Dispose();
    }

    private sealed class SigningClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private sealed class DirectorySource(CrmOidcOptions options) : IContextSourceVerifierV1<HttpContext>, IContextBindingAuthorityV1
    {
        internal ContextScopeAccessorV1 Accessor { get; } = new();
        public async ValueTask<ContextResolutionInputV1?> VerifyAsync(HttpContext http, CancellationToken cancellationToken = default)
        {
            var authenticated = await http.AuthenticateAsync(CrmIdentityController.Scheme);
            var principal = authenticated.Principal;
            if (!authenticated.Succeeded || principal is null) return null;
            static string? Single(ClaimsPrincipal p, string name) => p.FindAll(name).ToArray() is { Length: 1 } claims ? claims[0].Value : null;
            var client = Single(principal, "client_id");
            if (!Guid.TryParseExact(Single(principal, "tenant_id"), "D", out var tenant) || tenant == Guid.Empty ||
                Single(principal, "tenant_id") != tenant.ToString("D") || Single(principal, "iss") != options.Issuer ||
                Single(principal, "sub") != "service:" + client || Single(principal, "scope") != "cp6.services" ||
                !Guid.TryParseExact(Single(principal, "jti"), "D", out var jti) || jti == Guid.Empty ||
                !options.ServiceClients.Any(c => c.ClientId == client && c.TenantId == tenant && c.Enabled)) return null;
            var now = DateTimeOffset.UtcNow;
            var actor = new ContextActorV1(tenant, options.Issuer, "service:" + client, ContextActorKindV1.Service, null);
            return new(new(ContextEntryKindV1.Http, "main", "fixture-directory-jwt-v1", "CP6.Services", "identity-directory:read",
                tenant, http.Request.Path.Value!, http.TraceIdentifier, ["cp6.services"]),
                new(actor, actor, "CP6.Services", "us", now, now.AddMinutes(1), "validated-bearer:" + jti, jti.ToString("D"), ["cp6.services"]));
        }
        public ValueTask<ContextBindingRegistrationV1?> ResolveCurrentAsync(ContextOperationV1 operation, AuthenticatedContextSourceV1 source,
            CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            if (operation.HostId != "main" || operation.ProfileId != "fixture-directory-jwt-v1" || operation.EntryKind != ContextEntryKindV1.Http ||
                operation.Purpose != "identity-directory:read" || operation.Audience != "CP6.Services" || operation.ExpectedTenantId != source.Actor.TenantId ||
                source.Actor.Issuer != options.Issuer || source.Actor != source.EffectiveActor || source.ExpiresAtUtc <= now ||
                !options.ServiceClients.Any(c => c.Enabled && c.TenantId == source.Actor.TenantId && "service:" + c.ClientId == source.Actor.Subject))
                return ValueTask.FromResult<ContextBindingRegistrationV1?>(null);
            return ValueTask.FromResult<ContextBindingRegistrationV1?>(new(source.Actor.TenantId, source.Actor.TenantId, "main", operation.ProfileId,
                options.Issuer, "us", "CP6.Services", operation.Purpose, ContextEntryKindV1.Http, source.Actor, source.Actor,
                "fixture-directory:" + source.Actor.Subject, "v1", now.AddMinutes(-1), now.AddMinutes(1), true, ["cp6.services"]));
        }
    }
}
