using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.WebApi.Configuration;
using CP6.WebApi.Controllers.Internal;
using CP6.WebApi.Services;
using CP6.WebApi.Services.IdentityDirectoryTenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

// Actual product registration, source verifier, authority and controller. Only the isolated
// issuer key/PG data and unavailable browser-only dependencies are fixtures.
internal sealed class IdentityDirectoryConfiguredHttpHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly IdentityDirectoryConfiguredPostgresFixture _postgres;
    private readonly CrmOidcCrypto _signer;
    private readonly OwnerQueryGuard _owner;
    internal HttpClient Client { get; }
    internal CrmOidcOptions Options { get; }
    internal CrmIdentityRuntime Runtime { get; }
    internal IdentityDirectoryTenantHost Host { get; }
    internal int OwnerQueries => _owner.Calls;
    private IdentityDirectoryConfiguredHttpHost(WebApplication app, HttpClient client, IdentityDirectoryConfiguredPostgresFixture postgres,
        CrmOidcOptions options, CrmIdentityRuntime runtime, IdentityDirectoryTenantHost host, OwnerQueryGuard owner)
    { _app = app; Client = client; _postgres = postgres; Options = options; Runtime = runtime; Host = host; _owner = owner; _signer = new(options); }

    internal static async Task<IdentityDirectoryConfiguredHttpHost> Start(IdentityDirectoryConfiguredPostgresFixture postgres,
        IReadOnlyDictionary<string, string?>? overrides = null, TimeProvider? clock = null)
    {
        var runtime = IdentityDirectoryPostgresFixture.Runtime(); using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "configured-directory-fixture" };
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
        var now = (clock ?? TimeProvider.System).GetUtcNow(); const string p = IdentityDirectoryConfigurationTests.Prefix;
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = control.Database.GetConnectionString(), ["Database:Provider"] = "PostgreSql",
            ["CrmIdentity:Enabled"] = "true", ["CrmIdentity:Tenants:" + TenantStorageContext.A] = "us", ["CrmIdentity:Tenants:" + TenantStorageContext.B] = "us",
            ["CrmIdentity:ProjectionReaderClientIds:0"] = "reader-a", ["CrmIdentity:ProjectionReaderClientIds:1"] = "reader-b",
            [p + "Enabled"] = "true", [p + "HostId"] = "main", [p + "Revision"] = "r1", [p + "ValidFromUtc"] = now.AddHours(-1).ToString("O"),
            [p + "ValidUntilUtc"] = now.AddHours(1).ToString("O"), [p + "Server"] = postgres.Identity.Server, [p + "Port"] = postgres.Identity.Port.ToString(),
            [p + "Database"] = postgres.Identity.Database, [p + "DatabaseOid"] = postgres.Identity.DatabaseOid.ToString(),
            [p + "SystemIdentifier"] = postgres.Identity.SystemIdentifier, [p + "InstallationId"] = postgres.Identity.InstallationId.ToString("D"),
            [p + "Fingerprint"] = postgres.Identity.Fingerprint, [p + "SslMode"] = "Disable"
        };
        for (var i = 0; i < 2; i++)
        {
            values[p + $"Tenants:{i}:TenantId"] = (i == 0 ? TenantStorageContext.A : TenantStorageContext.B).ToString("D");
            values[p + $"Tenants:{i}:Login"] = i == 0 ? "directory_a" : "directory_b";
            values[p + $"Tenants:{i}:Password"] = i == 0 ? postgres.PasswordA : postgres.PasswordB;
            values[p + $"Tenants:{i}:CredentialVersion"] = "v1";
            values[p + $"Clients:{i}:ClientId"] = i == 0 ? "reader-a" : "reader-b";
            values[p + $"Clients:{i}:RegistrationVersion"] = "v1";
        }
        if (overrides is not null) foreach (var (name, value) in overrides) values[name] = value;
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(values);
        var owner = new OwnerQueryGuard(); builder.Services.AddSingleton(options);
        if (clock is not null) builder.Services.AddSingleton(clock);
        builder.Services.AddScoped<ITenantContext>(_ => new TenantContext());
        builder.Services.AddScoped(sp => postgres.Server.Context(sp.GetRequiredService<ITenantContext>(), null, owner));
        builder.Services.AddScoped<ICrmOidcServiceDirectory, CrmOidcDirectory>();
        builder.Services.AddSingleton(Mock.Of<ITokenBlacklistService>(MockBehavior.Strict));
        builder.Services.AddSingleton(Mock.Of<IPasswordPolicyService>(MockBehavior.Strict));
        builder.Services.AddCrmIdentityEvents(builder.Configuration, options, new(DatabaseProvider.PostgreSql));
        builder.Services.RemoveAll<IHostedService>();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        // Keep the actual JWT claims validator; replace only remote JWKS with the fixture public key.
        builder.Services.PostConfigure<JwtBearerOptions>(CrmIdentityController.Scheme, bearer =>
        {
            var signing = new OpenIdConnectConfiguration { Issuer = options.Issuer }; signing.SigningKeys.Add(key);
            bearer.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(signing); bearer.MapInboundClaims = false;
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(CrmIdentityController).Assembly);
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        try
        {
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new HttpClient { BaseAddress = new(address), Timeout = TimeSpan.FromSeconds(20) }, postgres, options,
                app.Services.GetRequiredService<CrmIdentityRuntime>(), app.Services.GetRequiredService<IdentityDirectoryTenantHost>(), owner);
        }
        catch { await app.DisposeAsync(); throw; }
    }
    internal async Task<(string Token, string Jti)> Token(Guid tenant, string? client = null, bool record = true,
        string audience = "CP6.Services", DateTimeOffset? expiry = null, string? tenantClaim = null, bool duplicateTenant = false)
    {
        client ??= tenant == TenantStorageContext.A ? "reader-a" : "reader-b";
        var id = Guid.NewGuid().ToString("D"); var expires = expiry ?? DateTimeOffset.UtcNow.AddMinutes(5);
        List<Claim> claims = [new("sub", "service:" + client), new("client_id", client), new("tenant_id", tenantClaim ?? tenant.ToString("D")), new("jti", id), new("scope", "cp6.services")];
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
    { Client.Dispose(); await _app.StopAsync(); await _app.DisposeAsync(); _signer.Dispose(); }
    private sealed class SigningClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class OwnerQueryGuard : DbCommandInterceptor
    {
        private int _calls;
        internal int Calls => Volatile.Read(ref _calls);
        private Exception Rejected() { Interlocked.Increment(ref _calls); return new InvalidOperationException("OWNER_DIRECTORY_QUERY_FORBIDDEN"); }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r) => throw Rejected();
        public override InterceptionResult<int> NonQueryExecuting(DbCommand c, CommandEventData e, InterceptionResult<int> r) => throw Rejected();
        public override InterceptionResult<object> ScalarExecuting(DbCommand c, CommandEventData e, InterceptionResult<object> r) => throw Rejected();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r, CancellationToken ct = default) => throw Rejected();
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<int> r, CancellationToken ct = default) => throw Rejected();
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<object> r, CancellationToken ct = default) => throw Rejected();
    }
}
