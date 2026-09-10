using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Platform.AspNetCore;
using CP6.WebApi.Controllers.Internal;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

sealed partial class IdentitySqlFixture
{
    public async Task HttpAuthorizationAsync()
    {
        var tenant = await SeedAsync();
        var otherTenant = await SeedAsync();
        var runtime = Runtime(tenant);
        runtime.Options.Tenants.Add(otherTenant, "us");
        runtime.Options.ProjectionReaderClientIds = ["reader-a", "reader-b"];
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var rsa = RSA.Create(2048);
        var oidc = new CrmOidcOptions
        {
            Enabled = true, Issuer = Issuer, ActiveKeyId = "c02-http-fixture",
            Keys = [new() { Kid = "c02-http-fixture", Pem = rsa.ExportRSAPrivateKeyPem() }],
            ServiceClients = [Client("reader-a", tenant), Client("reader-b", otherTenant), Client("not-reader", tenant)],
            Organizations = [new() { TenantId = tenant, Slug = "first", Region = "us", CrmEnabled = true },
                new() { TenantId = otherTenant, Slug = "second", Region = "us", CrmEnabled = true }]
        };
        CrmOidcServiceClient Client(string id, Guid tenantId) => new() { ClientId = id, TenantId = tenantId,
            SecretSha256 = CrmOidcCrypto.Hash(secret), Enabled = true, AllowedScopes = ["cp6.services"] };
        using var crypto = new CrmOidcCrypto(oidc);
        using var tlsKey = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=identity.cp6.test", tlsKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("identity.cp6.test");
        certificateRequest.CertificateExtensions.Add(san.Build());
        certificateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        certificateRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var generated = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var certificate = new X509Certificate2(generated.Export(X509ContentType.Pfx), (string?)null,
            OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        builder.Services.AddSingleton(oidc);
        builder.Services.AddSingleton(runtime);
        builder.Services.AddSingleton(protection);
        builder.Services.AddScoped(_ => new CP6Context(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(connection).Options,
            new TenantContext { CurrentTenantId = tenant }, identity: runtime));
        builder.Services.AddScoped<IdentitySnapshotReader>();
        builder.Services.AddScoped<CrmServiceTokenRecordStore>();
        builder.Services.AddScoped<CrmOidcDirectory>(p => new(p.GetRequiredService<CP6Context>(), new TenantContext { CurrentTenantId = tenant }, null!, null!, oidc));
        builder.Services.AddScoped<ICrmOidcServiceDirectory>(p => p.GetRequiredService<CrmOidcDirectory>());
        builder.Services.AddCp6JwtBearer(new Cp6JwtBearerProfile { Authority = Issuer, Issuer = Issuer, Audiences = ["CP6.Services"] }, CrmIdentityController.Scheme);
        var port = 0;
        // Only this generated certificate and fixed host are trusted by the two fixture clients.
        // Requests still cross real TCP/TLS and the published SDK's live discovery/JWKS manager.
        SocketsHttpHandler Handler() => new()
        {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
            ConnectCallback = async (context, cancellation) =>
            {
                if (context.DnsEndPoint.Host != "identity.cp6.test") throw new InvalidOperationException("C02_UNEXPECTED_FIXTURE_HOST");
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(IPAddress.Loopback, port, cancellation); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            },
            SslOptions = new() { RemoteCertificateValidationCallback = (_, presented, _, errors) =>
                presented is not null && (errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0 &&
                presented.GetRawCertData().AsSpan().SequenceEqual(certificate.RawData) }
        };
        using var backchannel = new HttpClient(Handler()) { Timeout = TimeSpan.FromSeconds(10) };
        builder.Services.Configure<JwtBearerOptions>(CrmIdentityController.Scheme, options => options.Backchannel = backchannel);
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(CrmIdentityController).Assembly)
            .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new IdentityControllersOnly())).AddControllersAsServices();
        builder.Services.RemoveAll<CrmOidcController>();
        builder.Services.AddTransient<CrmOidcController>(p => new(oidc, crypto, new SqlCrmOidcGrantStore(connection, runtime),
            p.GetRequiredService<CrmOidcDirectory>(), builder.Configuration, null!, null!,
            new CrmOidcServiceTokens(oidc, crypto, p.GetRequiredService<ICrmOidcServiceDirectory>(),
                records: p.GetRequiredService<CrmServiceTokenRecordStore>())));
        await using var app = builder.Build();
        Exception? serverFailure = null;
        app.Use(async (context, next) =>
        {
            try { await next(context); }
            catch (Exception ex) { serverFailure = ex; context.Response.StatusCode = 500; }
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        port = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()).Port;
        using var http = new HttpClient(Handler()) { BaseAddress = new Uri(Issuer), Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            async Task<string> Issue(string clientId)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["scope"] = "cp6.services" })
                };
                request.Headers.Authorization = Basic(clientId);
                using var response = await http.SendAsync(request);
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new InvalidOperationException($"real service token endpoint failed with HTTP {(int)response.StatusCode}", serverFailure);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.GetProperty("access_token").GetString()!;
            }
            AuthenticationHeaderValue Basic(string id) => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(
                Uri.EscapeDataString(id) + ":" + Uri.EscapeDataString(secret))));
            async Task<string> Read(string? token, string path, HttpStatusCode expected)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                if (token is not null) request.Headers.Authorization = new("Bearer", token);
                using var response = await http.SendAsync(request);
                Require(response.StatusCode == expected, $"identity HTTP status mismatch: expected {(int)expected}, actual {(int)response.StatusCode}");
                return await response.Content.ReadAsStringAsync();
            }
            var token = await Issue("reader-a");
            var other = await Issue("reader-b");
            var excluded = await Issue("not-reader");
            await Read(null, "/internal/crm/identity/versions", HttpStatusCode.Unauthorized);
            await Read(token, "/internal/crm/identity/versions", HttpStatusCode.ServiceUnavailable);
            await using (var db = Create(tenant)) await new IdentityBootstrapService(db, runtime).InitializeAsync(tenant);
            await using (var db = Create(otherTenant)) await new IdentityBootstrapService(db, runtime).InitializeAsync(otherTenant);
            var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
            var unsigned = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(new JwtHeader(), decoded.Payload));
            await Read(unsigned, "/internal/crm/identity/versions", HttpStatusCode.Unauthorized);
            var wrongAudience = crypto.Sign("CP6.Web", decoded.Claims.Where(c => c.Type is not ("iss" or "aud" or "iat" or "nbf" or "exp")), DateTimeOffset.UtcNow.AddMinutes(5), "at+jwt");
            await Read(wrongAudience, "/internal/crm/identity/versions", HttpStatusCode.Unauthorized);
            await Read(excluded, "/internal/crm/identity/versions", HttpStatusCode.Forbidden);
            var crossed = crypto.Sign("CP6.Services", decoded.Claims.Where(c => c.Type is not ("iss" or "aud" or "iat" or "nbf" or "exp" or "tenant_id"))
                .Append(new Claim("tenant_id", otherTenant.ToString("D"))), DateTimeOffset.UtcNow.AddMinutes(5), "at+jwt");
            await Read(crossed, "/internal/crm/identity/versions", HttpStatusCode.Forbidden);
            await Read(token, "/internal/crm/identity/versions?tenantId=" + otherTenant, HttpStatusCode.BadRequest);
            await Read(token, "/internal/crm/identity/versions?pageSize=201", HttpStatusCode.BadRequest);
            var baseline = await Read(token, "/internal/crm/identity/versions?pageSize=2", HttpStatusCode.OK);
            using var first = JsonDocument.Parse(baseline);
            Require(first.RootElement.GetProperty("tenantId").GetGuid() == tenant && first.RootElement.GetProperty("items").GetArrayLength() == 2,
                "HTTP reader crossed tenant or page size");
            var cursor = first.RootElement.GetProperty("nextCursor").GetString()!;
            await Read(other, "/internal/crm/identity/versions?cursor=" + Uri.EscapeDataString(cursor), HttpStatusCode.BadRequest);
            await Read(token, $"/internal/crm/identity/snapshots/tenant:{otherTenant:D}", HttpStatusCode.NotFound);
            await Read(token, $"/internal/crm/identity/snapshots/tenant:{tenant:D}", HttpStatusCode.OK);
            using var revoke = new HttpRequestMessage(HttpMethod.Post, "/connect/service-revocations")
            { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["jti"] = decoded.Id }) };
            revoke.Headers.Authorization = Basic("reader-a");
            using var revoked = await http.SendAsync(revoke);
            Require(revoked.StatusCode == HttpStatusCode.OK, "actual service revocation endpoint failed");
            await Read(token, "/internal/crm/identity/versions", HttpStatusCode.Forbidden);
            // Revocation is scoped to the presented client/tenant; another reader remains usable.
            await Read(other, "/internal/crm/identity/versions", HttpStatusCode.OK);
        }
        finally { await app.StopAsync(); }
    }

    private sealed class IdentityControllersOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            for (var i = feature.Controllers.Count - 1; i >= 0; i--)
                if (feature.Controllers[i].AsType() != typeof(CrmIdentityController) && feature.Controllers[i].AsType() != typeof(CrmOidcController))
                    feature.Controllers.RemoveAt(i);
        }
    }
}
