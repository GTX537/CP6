using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.AspNetCore;
using CP6.Tests.Platform;
using CP6.WebApi.Controllers.Internal;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
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
namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task App_intake_real_http_service_signature_token_registry_scope_and_revocation_are_required()
    {
        await using var h=await Seed();var f=await IntakeSetup(h);var e=IntakeBody(f);
        using var rsa=RSA.Create(2048);var key=new RsaSecurityKey(rsa.ExportParameters(false)){KeyId="app-fixture"};
        var oidc=new CrmOidcOptions{Enabled=true,Issuer="https://app-fixture.example",AllowInsecureLoopback=true,ActiveKeyId=key.KeyId,
            Keys=[new(){Kid=key.KeyId,Pem=rsa.ExportRSAPrivateKeyPem()}],
            ServiceClients=[new(){ClientId="fixture-service",TenantId=h.Tenant,Enabled=true,AllowedScopes=["cp6.services"]}]};
        var options=new ApprovalIntakeOptionsV2{Enabled=true,EnvironmentId=ScopeEnvironment,Version="http-fixture",EvidenceMode="DEMO",
            Clients=[new(){ClientId="fixture-service",TenantId=h.Tenant,PrincipalId=f.Authority.Identity.Principal,CanSubmit=true}]};
        Assert.Throws<InvalidOperationException>(()=>new ApprovalIntakeRegistryV2(options,oidc)); // DEMO cannot be enabled in a non-development host.
        var registry=new ApprovalIntakeRegistryV2(options,oidc,development:true);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});
        builder.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,0));builder.Logging.ClearProviders();
        builder.Services.AddSingleton(oidc);builder.Services.AddSingleton(registry);builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ITenantContext>(_=>new TenantContext());
        builder.Services.AddScoped(sp=>server.Context(sp.GetRequiredService<ITenantContext>()));
        builder.Services.AddScoped<IApprovalServiceAuthorityV2,ApprovalIntakeServiceAuthorityV2>();
        builder.Services.AddScoped(sp=>{
            var db=sp.GetRequiredService<CP6Context>();var peer=h with{Db=db,Http=new DefaultHttpContext()};
            var catalog=new AuthorizationCatalogStoreV1();catalog.RegisterScope("wms-plate-mold","Physical tools",[1,2,3,4,5],1);
            catalog.RegisterFields("wms-plate-mold",ToolFields.Select(x=>new FieldRegistry.Field(x,x)));catalog.RegisterScope("space-site","Sites",[5],5);catalog.RegisterFields("space-site",[]);catalog.Seal();
            // There is no logged-in human here. Evidence derives its subject solely from the actual frozen Owner row.
            var operations=new OperationsOwnerV1(db,sp.GetRequiredService<IHttpContextAccessor>(),_=>throw new InvalidOperationException("NO_HUMAN_SESSION_IN_SERVICE_REQUEST"),catalog.Capture);
            return new ApprovalIntakeOwnerV2(db,ScopeFence(peer),sp.GetRequiredService<IApprovalServiceAuthorityV2>(),new ToolDispositionApprovalOwnerV1(db,operations,ScopeFence(peer)),Authoring(peer)){ReceiveClock=()=>f.ReceiveAt};
        });
        builder.Services.AddCp6JwtBearer(new Cp6JwtBearerProfile{Authority=oidc.Issuer,Issuer=oidc.Issuer,Audiences=["CP6.Services"],ClockSkew=TimeSpan.Zero},CrmIdentityController.Scheme);
        builder.Services.PostConfigure<JwtBearerOptions>(CrmIdentityController.Scheme,b=>{
            var config=new OpenIdConnectConfiguration{Issuer=oidc.Issuer};config.SigningKeys.Add(key);
            b.ConfigurationManager=new StaticConfigurationManager<OpenIdConnectConfiguration>(config);b.MapInboundClaims=false;
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(ApprovalIntakeControllerV2).Assembly);
        await using var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();
        using var client=new HttpClient{BaseAddress=new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
        using var signer=new CrmOidcCrypto(oidc);
        async Task<(string Token,string Id)> Token(bool record=true,string? tenant=null,string? subject=null)
        {
            var id=Guid.NewGuid().ToString("D");var expiry=DateTimeOffset.UtcNow.AddMinutes(5);
            var token=signer.Sign("CP6.Services",[new("sub",subject??"service:fixture-service"),new("client_id","fixture-service"),new("tenant_id",tenant??h.Tenant.ToString()),new("scope","cp6.services"),new("jti",id)],expiry,"at+jwt");
            if(record){h.Db.CrmServiceTokenRecords.Add(new CrmServiceTokenRecord{Issuer=oidc.Issuer,Jti=id,ClientId="fixture-service",TenantId=h.Tenant,ExpiresAtUtc=expiry});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();}
            return(token,id);
        }
        async Task<HttpResponseMessage> Send(string? token,byte[]? payload=null,string? original=null)
        {
            using var request=new HttpRequestMessage(original==null?HttpMethod.Post:HttpMethod.Get,"/internal/oa/app/v2/"+(original??"submissions"));
            if(token!=null)request.Headers.Authorization=new("Bearer",token);
            if(original==null){request.Content=new ByteArrayContent(payload??f.Payload);request.Content.Headers.ContentType=new("application/json");}
            return await client.SendAsync(request);
        }
        using(var missing=await Send(null))Assert.Equal(HttpStatusCode.Unauthorized,missing.StatusCode);
        using(var unrecorded=await Send((await Token(record:false)).Token))Assert.Equal(HttpStatusCode.Forbidden,unrecorded.StatusCode);
        using(var human=await Send((await Token(subject:h.User.ToString())).Token))Assert.Equal(HttpStatusCode.Forbidden,human.StatusCode);
        using(var foreign=await Send((await Token(tenant:Guid.NewGuid().ToString())).Token))Assert.Equal(HttpStatusCode.Forbidden,foreign.StatusCode);
        var valid=await Token();
        using(var bodyScope=await Send(valid.Token,ApprovalCanonicalV2.Bytes(e with{Scope=e.Scope with{TenantId=Guid.NewGuid()}})))Assert.Equal(HttpStatusCode.Forbidden,bodyScope.StatusCode);
        Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant}").ToArrayAsync());
        using(var accepted=await Send(valid.Token))
        {Assert.True(accepted.StatusCode==HttpStatusCode.Created,await accepted.Content.ReadAsStringAsync());Assert.True(accepted.Headers.CacheControl!.NoStore);}
        using(var original=await Send(valid.Token,original:"commands/"+e.CommandId))Assert.Equal(HttpStatusCode.OK,original.StatusCode);
        await h.Db.CrmServiceTokenRecords.Where(x=>x.Issuer==oidc.Issuer&&x.Jti==valid.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAtUtc,DateTimeOffset.UtcNow));
        using(var revoked=await Send(valid.Token,original:"commands/"+e.CommandId))Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
        using(var fresh=await Send((await Token()).Token,original:"commands/"+e.CommandId))Assert.Equal(HttpStatusCode.OK,fresh.StatusCode);
        await app.StopAsync();
    }
}
