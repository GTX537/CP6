using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.AspNetCore;
using CP6.Tests.Platform;
using CP6.WebApi.Controllers.Internal;
using CP6.WebApi.Middleware;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task App_fact_real_http_registered_service_token_and_subject_grant_protect_receive_and_queries()
    {
        await using var h = await Seed(); var f = await FactSetup(h);
        using var rsa = RSA.Create(2048); var key = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "fact-fixture" };
        var oidc = new CrmOidcOptions { Enabled = true, Issuer = "https://fact-fixture.example", AllowInsecureLoopback = true, ActiveKeyId = key.KeyId,
            Keys = [new() { Kid = key.KeyId, Pem = rsa.ExportRSAPrivateKeyPem() }],
            ServiceClients = [new() { ClientId = "workflow-producer", TenantId = h.Tenant, Enabled = true, AllowedScopes = ["cp6.services"] }] };
        var options = new ApprovalFactReceiverOptionsV2 { Enabled = true, EnvironmentId = ScopeEnvironment, Version = "fact-http", EvidenceMode = "DEMO",
            Clients = [new() { ClientId = "workflow-producer", TenantId = h.Tenant, PrincipalId = f.Authority.Identity.Principal, CanReceive = true }] };
        Assert.Throws<InvalidOperationException>(() => new ApprovalFactReceiverRegistryV2(options, oidc, false));
        var registry = new ApprovalFactReceiverRegistryV2(options, oidc, true);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0)); builder.Logging.ClearProviders();
        builder.Services.AddSingleton(oidc); builder.Services.AddSingleton(registry); builder.Services.AddHttpContextAccessor();
        builder.Services.Configure<SecurityOptions>(o => o.Csrf.Enabled = true);
        builder.Services.AddScoped<ITenantContext>(_ => new TenantContext());
        builder.Services.AddScoped(sp => server.Context(sp.GetRequiredService<ITenantContext>()));
        builder.Services.AddScoped<ApprovalFactReceiverServiceAuthorityV2>();
        builder.Services.AddScoped(sp => FactReceiver(h with { Db = sp.GetRequiredService<CP6Context>() }, sp.GetRequiredService<ApprovalFactReceiverServiceAuthorityV2>()));
        builder.Services.AddCp6JwtBearer(new Cp6JwtBearerProfile { Authority = oidc.Issuer, Issuer = oidc.Issuer, Audiences = ["CP6.Services"], ClockSkew = TimeSpan.Zero }, CrmIdentityController.Scheme);
        builder.Services.PostConfigure<JwtBearerOptions>(CrmIdentityController.Scheme, b => {
            var config = new OpenIdConnectConfiguration { Issuer = oidc.Issuer }; config.SigningKeys.Add(key);
            b.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(config); b.MapInboundClaims = false;
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(ToolApprovalFactControllerV2).Assembly);
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.UseMiddleware<CsrfMiddleware>(); app.MapControllers(); await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        using var signer = new CrmOidcCrypto(oidc);
        async Task<(string Token, string Id)> Token(bool record = true, string? subject = null, string? tenant = null)
        {
            var id = Guid.NewGuid().ToString("D"); var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
            var token = signer.Sign("CP6.Services", [new("sub", subject ?? "service:workflow-producer"), new("client_id", "workflow-producer"),
                new("tenant_id", tenant ?? h.Tenant.ToString()), new("scope", "cp6.services"), new("jti", id)], expiry, "at+jwt");
            if (record) { h.Db.CrmServiceTokenRecords.Add(new CrmServiceTokenRecord { Issuer = oidc.Issuer, Jti = id, ClientId = "workflow-producer", TenantId = h.Tenant, ExpiresAtUtc = expiry }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); }
            return (token, id);
        }
        async Task<HttpResponseMessage> Send(string? token, string? path = null, bool cookie = false)
        {
            using var request = new HttpRequestMessage(path == null ? HttpMethod.Post : HttpMethod.Get, path ?? ToolApprovalFactReceiverV2.Route);
            if (token != null) request.Headers.Authorization = new("Bearer", token);
            if (cookie) { request.Headers.Add("Cookie", "cp6_at=pretend-human; cp6_csrf=same"); request.Headers.Add("X-CSRF-Token", "same"); }
            if (path == null) { request.Content = new ByteArrayContent(f.Payload); request.Content.Headers.ContentType = new("application/json"); }
            return await client.SendAsync(request);
        }
        var originalPath = ToolApprovalFactReceiverV2.Route + "/" + f.Decision.EventId;
        using (var missing = await Send(null, originalPath)) Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        using (var cookie = await Send(null, cookie: true)) Assert.Equal(HttpStatusCode.Unauthorized, cookie.StatusCode);
        using (var noRecord = await Send((await Token(false)).Token)) Assert.Equal(HttpStatusCode.Forbidden, noRecord.StatusCode);
        using (var human = await Send((await Token(subject: h.User.ToString())).Token)) Assert.Equal(HttpStatusCode.Forbidden, human.StatusCode);
        using (var foreign = await Send((await Token(tenant: Guid.NewGuid().ToString())).Token)) Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        var valid = await Token(); ApprovalFactAckV2 ack;
        using (var accepted = await Send(valid.Token))
        {
            Assert.True(accepted.StatusCode == HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync()); Assert.True(accepted.Headers.CacheControl!.NoStore);
            ack = JsonSerializer.Deserialize<ApprovalFactAckV2>(await accepted.Content.ReadAsStringAsync(), ApprovalBindingOwnerV2.Json)!;
            Assert.Equal("NOT_APPLIED", ack.Application);
        }
        using (var original = await Send(valid.Token, originalPath)) Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        using (var application = await Send(valid.Token, ack.ReceiptQueryRef))
        { Assert.Equal(HttpStatusCode.OK, application.StatusCode); Assert.Equal("NOT_APPLIED", JsonDocument.Parse(await application.Content.ReadAsStringAsync()).RootElement.GetProperty("kind").GetString()); }
        await h.Db.CrmServiceTokenRecords.Where(x => x.Issuer == oidc.Issuer && x.Jti == valid.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, DateTimeOffset.UtcNow));
        using (var revoked = await Send(valid.Token, originalPath)) Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        using (var fresh = await Send((await Token()).Token, originalPath)) Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.subject_grants SET can_query=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        using (var noSubject = await Send((await Token()).Token, ack.ReceiptQueryRef)) Assert.Equal(HttpStatusCode.NotFound, noSubject.StatusCode);
        await app.StopAsync();
    }
}
