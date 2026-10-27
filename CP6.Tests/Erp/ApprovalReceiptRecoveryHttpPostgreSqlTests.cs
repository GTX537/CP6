using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
    public async Task Receipt_recovery_actual_TLS_C02_signed_chain_resumes_after_lost_page()
    {
        await using var h=await Seed();var f=await ReceiptSetup(h,3);await RecoveryGrant(h,f);var e=f.Receipts[^1];await f.Owner.ReceiveAsync(ReceiptBytes(e));
        var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        var origin="https://127.0.0.1:"+port;const string secret="explicit isolated delivery fixture secret";
        using var rsa=RSA.Create(2048);var key=new RsaSecurityKey(rsa.ExportParameters(false)){KeyId="app-fixture"};
        var certificateRequest=new CertificateRequest("CN=owned-W185-Recovery",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var names=new SubjectAlternativeNameBuilder();names.AddIpAddress(IPAddress.Loopback);certificateRequest.CertificateExtensions.Add(names.Build());
        using var created=certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));
        using var certificate=new X509Certificate2(created.Export(X509ContentType.Pfx));
        var oidc=new CrmOidcOptions{Enabled=true,Issuer=origin,AllowInsecureLoopback=true,ActiveKeyId=key.KeyId,
            Keys=[new(){Kid=key.KeyId,Pem=rsa.ExportRSAPrivateKeyPem()}],
            Organizations=[new(){TenantId=h.Tenant,Slug="fixture",Region="us",CrmEnabled=true}],
            ServiceClients=[new(){ClientId="workflow-producer",TenantId=h.Tenant,Enabled=true,SecretSha256=CrmOidcCrypto.Hash(secret),AllowedScopes=["cp6.services"]}]};
        var options=new ApprovalFactReceiverOptionsV2{Enabled=true,EnvironmentId=ScopeEnvironment,Version="http-fixture",EvidenceMode="DEMO",
            Clients=[new(){ClientId="workflow-producer",TenantId=h.Tenant,PrincipalId=f.Fact.Authority.Identity.Principal,CanReceive=true}]};
        Assert.Throws<InvalidOperationException>(()=>new ApprovalFactReceiverRegistryV2(options,oidc,false)); // DEMO cannot be enabled in a non-development host.
        var registry=new ApprovalFactReceiverRegistryV2(options,oidc,development:true);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});
        builder.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,port,l=>l.UseHttps(certificate)));builder.Logging.ClearProviders();
        builder.Services.AddSingleton(oidc);builder.Services.AddSingleton(registry);builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ITenantContext>(_=>new TenantContext{CurrentTenantId=h.Tenant});
        builder.Services.AddScoped(sp=>server.Context(sp.GetRequiredService<ITenantContext>()));
        builder.Services.AddScoped<ApprovalFactReceiverServiceAuthorityV2>();
        builder.Services.AddScoped(sp=>FactReceiver(h with{Db=sp.GetRequiredService<CP6Context>()},sp.GetRequiredService<ApprovalFactReceiverServiceAuthorityV2>()));
        builder.Services.Configure<SecurityOptions>(o=>o.Csrf.Enabled=true);
        builder.Services.AddCp6JwtBearer(new Cp6JwtBearerProfile{Authority=oidc.Issuer,Issuer=oidc.Issuer,Audiences=["CP6.Services"],ClockSkew=TimeSpan.Zero},CrmIdentityController.Scheme);
        builder.Services.PostConfigure<JwtBearerOptions>(CrmIdentityController.Scheme,b=>{
            var config=new OpenIdConnectConfiguration{Issuer=oidc.Issuer};config.SigningKeys.Add(key);
            b.ConfigurationManager=new StaticConfigurationManager<OpenIdConnectConfiguration>(config);b.MapInboundClaims=false;
        });
        // Existing C02 controller, native tenant directory, RSA issuer and actual PostgreSQL token record store.
        // Browser-login collaborators are strict mocks: they must never be invoked by client_credentials.
        builder.Services.AddSingleton(new CrmOidcCrypto(oidc));
        builder.Services.AddSingleton(Moq.Mock.Of<CP6.Core.Services.Sys.ITokenBlacklistService>(Moq.MockBehavior.Strict));
        builder.Services.AddSingleton(Moq.Mock.Of<CP6.Core.Services.Sys.IPasswordPolicyService>(Moq.MockBehavior.Strict));
        builder.Services.AddSingleton(Moq.Mock.Of<ICrmOidcGrantStore>(Moq.MockBehavior.Strict));
        builder.Services.AddSingleton(Moq.Mock.Of<IAuthCookieWriter>(Moq.MockBehavior.Strict));
        builder.Services.AddScoped<CrmOidcDirectory>();
        var identityOptions=new CP6.Core.Services.CrmIdentity.CrmIdentityOptions{Enabled=true,Issuer=origin,Tenants={[h.Tenant]="us"},ProjectionReaderClientIds=["workflow-producer"]};
        builder.Services.AddSingleton(new CP6.Core.Services.CrmIdentity.CrmIdentityRuntime(identityOptions,new CP6.Core.Services.CrmIdentity.IdentityEventValidator(
            CP6.Platform.Messaging.Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory,"contracts/events/platform")),origin)));
        builder.Services.AddScoped<CP6.Core.Services.CrmIdentity.ICrmServiceTokenRecordStore,CP6.Core.Services.CrmIdentity.CrmServiceTokenRecordStore>();
        builder.Services.AddScoped(sp=>new CrmOidcServiceTokens(oidc,sp.GetRequiredService<CrmOidcCrypto>(),sp.GetRequiredService<CrmOidcDirectory>(),
            records:sp.GetRequiredService<CP6.Core.Services.CrmIdentity.ICrmServiceTokenRecordStore>()));
        builder.Services.AddControllers().AddApplicationPart(typeof(ToolApprovalFactControllerV2).Assembly);
        await using var app=builder.Build();var posts=0;var lose=true;
        app.Use(async(context,next)=>{
            if(!context.Request.Path.Value!.EndsWith("/receipts",StringComparison.Ordinal)){await next(context);return;}
            posts++;var target=context.Response.Body;using var buffer=new MemoryStream();context.Response.Body=buffer;
            try{await next(context);if(lose&&context.Request.Query.ContainsKey("cursor")&&context.Response.StatusCode==200){lose=false;context.Abort();return;}buffer.Position=0;await buffer.CopyToAsync(target);}
            finally{context.Response.Body=target;}
        });
        app.UseAuthentication();app.UseAuthorization();app.UseMiddleware<CP6.WebApi.Middleware.CsrfMiddleware>();app.MapControllers();await app.StartAsync();
        var sendOptions=new ApprovalDecisionDeliveryOptionsV2{Enabled=true,Version="fixture-1",DestinationOrigin=origin,
            Clients=[new(){TenantId=h.Tenant,ClientId="workflow-producer",ClientSecret=secret}]};
        using(var strictTransport=new ApprovalReceiptChainTransportV2(sendOptions,oidc,registry,true))
            await Assert.ThrowsAsync<HttpRequestException>(()=>strictTransport.ReadAsync(strictTransport.Route(h.Tenant)!,e.ReceiptStreamId,"0",1,null,default));
        using var transport=new ApprovalReceiptChainTransportV2(sendOptions,oidc,registry,new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,
            ServerCertificateCustomValidationCallback=(request,cert,_,_)=>request.RequestUri?.GetLeftPart(UriPartial.Authority)==origin && cert!=null && cert.RawData.AsSpan().SequenceEqual(certificate.RawData)});
        var worker=new ApprovalReceiptRecoveryV2(h.Db,ScopeFence(h),transport){PageLimit=1};
        Assert.Equal("RECEIPT_RECOVERY_OUTCOME_UNKNOWN",(await worker.RunAsync(h.Tenant)).State);Assert.Equal(2,posts);
        Assert.Equal("1",(await f.Owner.CurrentAsync(e.ReceiptStreamId)).HighestContinuousRevision);await ReceiptCounts(h,2,1);
        await RecoveryDue(h);Assert.Equal("RECOVERED",(await worker.RunAsync(h.Tenant)).State);Assert.Equal(4,posts);
        Assert.True(await h.Db.CrmServiceTokenRecords.CountAsync(x=>x.TenantId==h.Tenant)>=4);
        Assert.Equal(e,(await f.Owner.CurrentAsync(e.ReceiptStreamId)).Receipt);await ReceiptCounts(h,3,3);await ApplicationCounts(h,3);
        oidc.ServiceClients[0].Enabled=false;Assert.Null(transport.Route(h.Tenant));
        Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());await app.StopAsync();
    }
}
