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
    public async Task App_receipt_real_http_current_service_and_subject_protect_pending_and_recorded_queries()
    {
        await using var h = await Seed(); var f = await ReceiptSetup(h, 2); var one = f.Receipts[0]; var two = f.Receipts[1];
        using var rsa = RSA.Create(2048); var key = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "receipt-fixture" };
        var oidc = new CrmOidcOptions { Enabled = true, Issuer = "https://receipt-fixture.example", AllowInsecureLoopback = true, ActiveKeyId = key.KeyId,
            Keys = [new() { Kid = key.KeyId, Pem = rsa.ExportRSAPrivateKeyPem() }],
            ServiceClients = [new() { ClientId = "fixture-service", TenantId = h.Tenant, Enabled = true, AllowedScopes = ["cp6.services"] }] };
        var options = new ApprovalIntakeOptionsV2 { Enabled = true, EnvironmentId = ScopeEnvironment, Version = "receipt-http", EvidenceMode = "DEMO",
            Clients = [new() { ClientId = "fixture-service", TenantId = h.Tenant, PrincipalId = f.Authority.Identity.Principal, CanSubmit = true }] };
        var registry = new ApprovalIntakeRegistryV2(options, oidc, true);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0)); builder.Logging.ClearProviders();
        builder.Services.AddSingleton(oidc); builder.Services.AddSingleton(registry); builder.Services.AddHttpContextAccessor();
        builder.Services.Configure<SecurityOptions>(o => o.Csrf.Enabled = true);
        builder.Services.AddScoped<ITenantContext>(_ => new TenantContext());
        builder.Services.AddScoped(sp => server.Context(sp.GetRequiredService<ITenantContext>()));
        builder.Services.AddScoped<IApprovalServiceAuthorityV2, ApprovalIntakeServiceAuthorityV2>();
        builder.Services.AddScoped(sp => ReceiptReceiver(h with { Db = sp.GetRequiredService<CP6Context>() }, sp.GetRequiredService<IApprovalServiceAuthorityV2>()));
        builder.Services.AddCp6JwtBearer(new Cp6JwtBearerProfile { Authority = oidc.Issuer, Issuer = oidc.Issuer, Audiences = ["CP6.Services"], ClockSkew = TimeSpan.Zero }, CrmIdentityController.Scheme);
        builder.Services.PostConfigure<JwtBearerOptions>(CrmIdentityController.Scheme, b => {
            var config = new OpenIdConnectConfiguration { Issuer = oidc.Issuer }; config.SigningKeys.Add(key);
            b.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(config); b.MapInboundClaims = false;
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(ApprovalReceiptControllerV2).Assembly);
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.UseMiddleware<CsrfMiddleware>(); app.MapControllers(); await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        using var signer = new CrmOidcCrypto(oidc);
        async Task<(string Token, string Id)> Token(bool record = true)
        {
            var id = Guid.NewGuid().ToString("D"); var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
            var token = signer.Sign("CP6.Services", [new("sub", "service:fixture-service"), new("client_id", "fixture-service"),
                new("tenant_id", h.Tenant.ToString()), new("scope", "cp6.services"), new("jti", id)], expiry, "at+jwt");
            if (record) { h.Db.CrmServiceTokenRecords.Add(new CrmServiceTokenRecord { Issuer = oidc.Issuer, Jti = id, ClientId = "fixture-service", TenantId = h.Tenant, ExpiresAtUtc = expiry }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); }
            return (token, id);
        }
        async Task<HttpResponseMessage> Send(string? token, string path, byte[]? payload = null)
        {
            using var request = new HttpRequestMessage(payload == null ? HttpMethod.Get : HttpMethod.Post, path);
            if (token != null) request.Headers.Authorization = new("Bearer", token);
            if (payload != null) { request.Content = new ByteArrayContent(payload); request.Content.Headers.ContentType = new("application/json"); }
            return await client.SendAsync(request);
        }
        var original = ApprovalReceiptIngestionV2.QueryRoute + two.ReceiptEventId;
        using (var anonymous = await Send(null, original)) Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using (var unregistered = await Send((await Token(false)).Token, original)) Assert.Equal(HttpStatusCode.Forbidden, unregistered.StatusCode);
        var valid = await Token();
        using (var missing = await Send(valid.Token, original))
        { Assert.Equal(HttpStatusCode.OK, missing.StatusCode); Assert.Equal("NOT_OBSERVED", JsonDocument.Parse(await missing.Content.ReadAsStringAsync()).RootElement.GetProperty("kind").GetString()); }
        using (var gap = await Send(valid.Token, ApprovalReceiptIngestionV2.Route, ReceiptBytes(two)))
        { Assert.True(gap.StatusCode == HttpStatusCode.Accepted, await gap.Content.ReadAsStringAsync()); Assert.True(gap.Headers.CacheControl!.NoStore); Assert.Equal("PENDING_GAP", JsonDocument.Parse(await gap.Content.ReadAsStringAsync()).RootElement.GetProperty("kind").GetString()); }
        using (var query = await Send(valid.Token, original)) Assert.Equal(HttpStatusCode.Accepted, query.StatusCode);
        using (var predecessor = await Send(valid.Token, ApprovalReceiptIngestionV2.Route, ReceiptBytes(one))) Assert.Equal(HttpStatusCode.OK, predecessor.StatusCode);
        using (var query = await Send(valid.Token, original))
        { Assert.Equal(HttpStatusCode.OK, query.StatusCode); Assert.Equal("RECORDED", JsonDocument.Parse(await query.Content.ReadAsStringAsync()).RootElement.GetProperty("kind").GetString()); }
        using (var current = await Send(valid.Token, ApprovalReceiptIngestionV2.ProjectionRoute + two.ReceiptStreamId))
        {
            Assert.Equal(HttpStatusCode.OK, current.StatusCode); var body = JsonDocument.Parse(await current.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(two, ApprovalApplicationReceiptWireV2.Parse(System.Text.Encoding.UTF8.GetBytes(body.GetProperty("receipt").GetRawText())));
        }
        using (var extra = await Send(valid.Token, original + "?tenantId=" + h.Tenant)) Assert.Equal(HttpStatusCode.UnprocessableEntity, extra.StatusCode);
        var bad = System.Text.Json.Nodes.JsonNode.Parse(ReceiptBytes(one))!.AsObject(); bad["forceApplied"] = true;
        using (var extra = await Send(valid.Token, ApprovalReceiptIngestionV2.Route, ApprovalCanonicalV2.Bytes(bad))) Assert.Equal(HttpStatusCode.UnprocessableEntity, extra.StatusCode);
        await h.Db.CrmServiceTokenRecords.Where(x => x.Issuer == oidc.Issuer && x.Jti == valid.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, DateTimeOffset.UtcNow));
        using (var revoked = await Send(valid.Token, original)) Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_receipt_v2.subject_grants SET can_query=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        using (var revoked = await Send((await Token()).Token, original)) Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        await ReceiptCounts(h, 2, 2); await app.StopAsync();
    }
}
