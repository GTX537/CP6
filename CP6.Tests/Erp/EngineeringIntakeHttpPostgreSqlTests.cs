using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Platform.AspNetCore;
using CP6.WebApi.Controllers.Internal;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using static CP6.Core.Services.Plm.EngineeringIntakeWireV1;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Wp0560_HTTP_holds_current_service_credential_and_returns_only_receipt_then_exact_original()
    {
        await using var h = await Seed();
        using var f = await PrepareIntakeHarnessAsync(h);
        f.Source.AllowOtherCurrentTokens = true;
        using var rsa = RSA.Create(2048);
        var jwtKey = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "simulated-transport" };
        var oidc = new CrmOidcOptions
        {
            Enabled = true, Issuer = f.Caller.Issuer, AllowInsecureLoopback = true, ActiveKeyId = jwtKey.KeyId,
            Keys = [new() { Kid = jwtKey.KeyId, Pem = rsa.ExportRSAPrivateKeyPem() }],
            ServiceClients = [new() { ClientId = f.Caller.ClientId, TenantId = h.Tenant, Enabled = true, AllowedScopes = ["cp6.services"] }]
        };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.ConfigureKestrel(listener => listener.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(oidc);
        builder.Services.AddSingleton<IDataProtectionProvider>(f.Encryption);
        builder.Services.AddSingleton<IEngineeringIntakeAuthorityV1>(f.Source);
        builder.Services.AddScoped<ITenantContext, TenantContext>();
        builder.Services.AddScoped(sp => server.Context(sp.GetRequiredService<ITenantContext>()));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(sp => new OperationsOwnerV1(sp.GetRequiredService<CP6Context>(),
            sp.GetRequiredService<IHttpContextAccessor>(), _ => throw new InvalidOperationException("无用户会话"), f.Catalog.Capture));
        builder.Services.AddScoped<EngineeringRequirementService>();
        builder.Services.AddScoped<EngineeringIntakeOriginalsV1>();
        builder.Services.AddScoped<EngineeringIntakeContextService>();
        builder.Services.AddCp6JwtBearer(new Cp6JwtBearerProfile
        {
            Authority = oidc.Issuer, Issuer = oidc.Issuer, Audiences = ["CP6.Services"], ClockSkew = TimeSpan.Zero
        }, CrmIdentityController.Scheme);
        builder.Services.PostConfigure<JwtBearerOptions>(CrmIdentityController.Scheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = oidc.Issuer };
            configuration.SigningKeys.Add(jwtKey);
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            options.MapInboundClaims = false;
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(EngineeringIntakeControllerV1).Assembly);
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient
        {
            BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
        };
        using var issuer = new CrmOidcCrypto(oidc);
        const string preparePath = "/internal/plm/v1/engineering-handoff-next-context/query";
        const string receivePath = "/internal/plm/v1/engineering-handoffs";
        const string originalPath = "/internal/plm/v1/engineering-handoff-originals/query";
        var nextBytes = Encode(f.EmptyQuery());
        using var missing = await Send(preparePath, nextBytes, null);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        using var unrecorded = await Send(preparePath, nextBytes, Token(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, unrecorded.StatusCode);
        var serviceToken = Token(f.Caller.TokenId);
        var otherTenantQuery = f.EmptyQuery() with { CaseNaturalKey = f.Natural with { TenantId = Guid.NewGuid() } };
        using var wrongTenant = await Send(preparePath, Encode(otherTenantQuery), serviceToken);
        Assert.Equal(HttpStatusCode.Forbidden, wrongTenant.StatusCode);

        using var prepared = await Send(preparePath, nextBytes, serviceToken);
        var response = await prepared.Content.ReadAsByteArrayAsync();
        Assert.True(prepared.StatusCode == HttpStatusCode.OK, System.Text.Encoding.UTF8.GetString(response));
        var next = Read<EngineeringNextContextV1>(response);
        Assert.Equal("ABSENT", next.PriorExecutionFence.Key);
        var envelope = f.Build(next);
        var payload = await f.PublishAsync(envelope);
        using var received = await Send(receivePath, payload, serviceToken);
        var receipt = await received.Content.ReadAsByteArrayAsync();
        Assert.True(received.StatusCode == HttpStatusCode.Accepted, System.Text.Encoding.UTF8.GetString(receipt));
        Assert.True(received.Headers.CacheControl?.NoStore);
        using (var json = JsonDocument.Parse(receipt))
        {
            Assert.Equal("UNKNOWN", json.RootElement.GetProperty("businessOutcome").GetString());
            Assert.Equal("RECEIPT_ONLY", json.RootElement.GetProperty("scope").GetString());
            Assert.False(json.RootElement.TryGetProperty("engineering", out _));
            Assert.False(json.RootElement.TryGetProperty("readiness", out _));
        }
        using var original = await Send(originalPath, Encode(EngineeringIntakeHarness.OriginalQuery(envelope)), serviceToken);
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        var page = Read<EngineeringIntakeOriginalPageV1>(await original.Content.ReadAsByteArrayAsync());
        Assert.Equal("ACCEPTED", IntakeResult(page).Outcome);
        await h.Db.CrmServiceTokenRecords.Where(x => x.Jti == f.Caller.TokenId.ToString("D"))
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAtUtc, DateTimeOffset.UtcNow));
        using var revoked = await Send(originalPath, Encode(EngineeringIntakeHarness.OriginalQuery(envelope)), serviceToken);
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        await app.StopAsync();

        string Token(Guid token) => issuer.Sign("CP6.Services",
            [new("sub", "service:" + f.Caller.ClientId), new("client_id", f.Caller.ClientId), new("tenant_id", h.Tenant.ToString("D")),
                new("scope", "cp6.services"), new("jti", token.ToString("D"))], f.Caller.ExpiresAt, "at+jwt");
        async Task<HttpResponseMessage> Send(string path, byte[] input, string? token)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(input) };
            message.Content.Headers.ContentType = new("application/json");
            if (token is not null) message.Headers.Authorization = new("Bearer", token);
            return await client.SendAsync(message);
        }
    }
}
