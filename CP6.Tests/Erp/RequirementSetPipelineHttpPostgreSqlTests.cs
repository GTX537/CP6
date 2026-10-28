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
using CP6.Core.Services.Plm;
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
    public async Task RequirementSetReview_actual_C02_TLS_decision_receipt_and_chain_preserve_originals_after_lost_ack()
    {
        await using var h=await Seed();var f=await SetApplicationSetup(h);var e=f.Decision;
        var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        var origin="https://127.0.0.1:"+port;const string secret="explicit isolated delivery fixture secret";
        using var rsa=RSA.Create(2048);var key=new RsaSecurityKey(rsa.ExportParameters(false)){KeyId="app-fixture"};
        var certificateRequest=new CertificateRequest("CN=owned-W196-Main",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var names=new SubjectAlternativeNameBuilder();names.AddIpAddress(IPAddress.Loopback);certificateRequest.CertificateExtensions.Add(names.Build());
        using var created=certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));
        using var certificate=new X509Certificate2(created.Export(X509ContentType.Pfx));
        var oidc=new CrmOidcOptions{Enabled=true,Issuer=origin,AllowInsecureLoopback=true,ActiveKeyId=key.KeyId,
            Keys=[new(){Kid=key.KeyId,Pem=rsa.ExportRSAPrivateKeyPem()}],
            Organizations=[new(){TenantId=h.Tenant,Slug="fixture",Region="us",CrmEnabled=true}],
            ServiceClients=[new(){ClientId="requirementSet-workflow-producer",TenantId=h.Tenant,Enabled=true,SecretSha256=CrmOidcCrypto.Hash(secret),AllowedScopes=["cp6.services"]}]};
        var options=new ApprovalFactReceiverOptionsV2{Enabled=true,EnvironmentId=ScopeEnvironment,Version="http-fixture",EvidenceMode="DEMO",
            Clients=[new(){ClientId="requirementSet-workflow-producer",TenantId=h.Tenant,PrincipalId=f.Authority.Identity.Principal,CanReceive=true}]};
        Assert.Throws<InvalidOperationException>(()=>new ApprovalFactReceiverRegistryV2(options,oidc,false)); // DEMO cannot be enabled in a non-development host.
        var registry=new ApprovalFactReceiverRegistryV2(options,oidc,development:true);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});
        builder.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,port,l=>l.UseHttps(certificate)));builder.Logging.ClearProviders();
        builder.Services.AddSingleton(oidc);builder.Services.AddSingleton(registry);builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ITenantContext>(_=>new TenantContext{CurrentTenantId=h.Tenant});
        builder.Services.AddScoped(sp=>server.Context(sp.GetRequiredService<ITenantContext>()));
        builder.Services.AddScoped<ApprovalFactReceiverServiceAuthorityV2>();
        builder.Services.AddScoped(sp=>{
            var db=sp.GetRequiredService<CP6Context>();var scoped=ScopeFence(h with { Db=db });
            return new RequirementSetReviewFactReceiverV1(db,scoped,sp.GetRequiredService<ApprovalFactReceiverServiceAuthorityV2>(),new(db,scoped),FactCursorProtection);
        });
        var intakeOptions=new ApprovalIntakeOptionsV2{Enabled=true,EnvironmentId=ScopeEnvironment,Version="requirementSet-receipt-http",EvidenceMode="DEMO",
            Clients=[new(){ClientId="requirementSet-workflow-producer",TenantId=h.Tenant,PrincipalId=f.Authority.Identity.Principal,CanSubmit=true}]};
        var intakeRegistry=new ApprovalIntakeRegistryV2(intakeOptions,oidc,development:true);
        builder.Services.AddSingleton(intakeRegistry);
        builder.Services.AddScoped<IApprovalServiceAuthorityV2,ApprovalIntakeServiceAuthorityV2>();
        builder.Services.AddScoped(sp=>new ApprovalReceiptIngestionV2(sp.GetRequiredService<CP6Context>(),ScopeFence(h with { Db=sp.GetRequiredService<CP6Context>() }),sp.GetRequiredService<IApprovalServiceAuthorityV2>()));
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
        var identityOptions=new CP6.Core.Services.CrmIdentity.CrmIdentityOptions{Enabled=true,Issuer=origin,Tenants={[h.Tenant]="us"},ProjectionReaderClientIds=["requirementSet-workflow-producer"]};
        builder.Services.AddSingleton(new CP6.Core.Services.CrmIdentity.CrmIdentityRuntime(identityOptions,new CP6.Core.Services.CrmIdentity.IdentityEventValidator(
            CP6.Platform.Messaging.Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory,"contracts/events/platform")),origin)));
        builder.Services.AddScoped<CP6.Core.Services.CrmIdentity.ICrmServiceTokenRecordStore,CP6.Core.Services.CrmIdentity.CrmServiceTokenRecordStore>();
        builder.Services.AddScoped(sp=>new CrmOidcServiceTokens(oidc,sp.GetRequiredService<CrmOidcCrypto>(),sp.GetRequiredService<CrmOidcDirectory>(),
            records:sp.GetRequiredService<CP6.Core.Services.CrmIdentity.ICrmServiceTokenRecordStore>()));
        builder.Services.AddControllers().AddApplicationPart(typeof(RequirementSetReviewFactControllerV1).Assembly);
        await using var app=builder.Build();var posts=0;var receiptPosts=0;var lose=true;var loseReceipt=true;
        app.Use(async(context,next)=>{
            var decisionRequest=context.Request.Path==RequirementSetReviewFactReceiverV1.Route;
            var receiptRequest=context.Request.Path==ApprovalReceiptIngestionV2.Route;
            if(!decisionRequest&&!receiptRequest){await next(context);return;}
            if(decisionRequest)posts++;else receiptPosts++;var target=context.Response.Body;using var buffer=new MemoryStream();context.Response.Body=buffer;
            try{await next(context);if(context.Response.StatusCode==200 && (decisionRequest?lose:loseReceipt)){if(decisionRequest)lose=false;else loseReceipt=false;context.Abort();return;}buffer.Position=0;await buffer.CopyToAsync(target);}
            finally{context.Response.Body=target;}
        });
        app.UseAuthentication();app.UseAuthorization();app.UseMiddleware<CP6.WebApi.Middleware.CsrfMiddleware>();app.MapControllers();await app.StartAsync();
        var sendOptions=new ApprovalDecisionDeliveryOptionsV2{Enabled=true,Version="fixture-1",DestinationOrigin=origin,
            Clients=[new(){TenantId=h.Tenant,ClientId="requirementSet-workflow-producer",ClientSecret=secret}]};
        var deliveryRegistry=new ApprovalDecisionDeliveryRegistryV2(sendOptions,oidc,registry,development:true,businessOwner:ApprovalBusinessProfileV2.RequirementSetOwner);
        using(var strictTransport=new ApprovalDecisionDeliveryTransportV2(deliveryRegistry))
            await Assert.ThrowsAsync<HttpRequestException>(()=>strictTransport.SendAsync(deliveryRegistry.Route(h.Tenant)!,e.EventId,f.Payload,true,default));
        using var transport=new RequirementSetDecisionTransportV1(deliveryRegistry,new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,
            ServerCertificateCustomValidationCallback=(request,cert,_,_)=>request.RequestUri?.GetLeftPart(UriPartial.Authority)==origin && cert!=null && cert.RawData.AsSpan().SequenceEqual(certificate.RawData)});
        var worker=new RequirementSetDecisionDeliveryV1(h.Db,ScopeFence(h),transport);
        Assert.Equal("OUTCOME_UNKNOWN",(await worker.RunAsync(h.Tenant)).State);Assert.Equal(1,posts);
        Assert.Single(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_requirement_set_review_fact_v1.inbox WHERE tenant_id={h.Tenant}").ToArrayAsync());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_requirement_set_review_fact_v1.subject_grants SET can_receive=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");await DecisionDue(h);
        Assert.Equal("RECEIVED",(await worker.RunAsync(h.Tenant)).State);Assert.Equal(1,posts);
        Assert.True(await h.Db.CrmServiceTokenRecords.CountAsync(x=>x.TenantId==h.Tenant)>=3);
        var receipt=await ApprovalDecisionDeliveryV2.ReadAsync(h.Db,ScopeEnvironment,e.EventId,default);
        Assert.Equal(e.SubmissionId,receipt!.Receipt!.Value.GetProperty("submissionId").GetGuid());Assert.Equal("NOT_APPLIED",receipt.Receipt.Value.GetProperty("application").GetString());
        var owner=SetReviews(h);var detail=await owner.SubmissionAsync(f.Set.Source.Set.Reference.Id,f.Set.Submission);
        var applied=await owner.ApplyAsync(f.Set.Source.Set.Reference.Id,detail.Application.Etag,new(Guid.NewGuid(),f.Set.Submission,null,"Exact native approval after actual C02 delivery"));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_receipt_v2.registrations(environment_id,tenant_id,principal_id,owner,revision,enabled,can_receive,can_query,evidence_mode)
            VALUES({ScopeEnvironment},{h.Tenant},{f.Authority.Identity.Principal},{ApprovalBusinessProfileV2.RequirementSetOwner},{Guid.NewGuid()},true,true,true,'DEMO');
            INSERT INTO cp6_approval_receipt_v2.subject_grants(environment_id,tenant_id,principal_id,submission_id,owner,revision,enabled,can_receive,can_query)
            VALUES({ScopeEnvironment},{h.Tenant},{f.Authority.Identity.Principal},{f.Set.Submission},{ApprovalBusinessProfileV2.RequirementSetOwner},{Guid.NewGuid()},true,true,true);
            """);
        var receiptOptions=new ToolApprovalDeliveryOptionsV1{Enabled=true,Version="requirementSet-receipt-1",DestinationOrigin=origin,
            Clients=[new(){TenantId=h.Tenant,ClientId="requirementSet-workflow-producer",ClientSecret=secret}]};
        HttpClientHandler TrustedHandler()=>new(){AllowAutoRedirect=false,UseCookies=false,
            ServerCertificateCustomValidationCallback=(request,cert,_,_)=>request.RequestUri?.GetLeftPart(UriPartial.Authority)==origin && cert!=null && cert.RawData.AsSpan().SequenceEqual(certificate.RawData)};
        using var receiptTransport=new RequirementSetApplicationTransportV1(receiptOptions,oidc,intakeRegistry,TrustedHandler());
        var receiptWorker=new RequirementSetApplicationDeliveryV1(h.Db,ScopeFence(h),receiptTransport);
        Assert.Equal("OUTCOME_UNKNOWN",(await receiptWorker.RunAsync(h.Tenant)).State);Assert.Equal(1,receiptPosts);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_approval_receipt_v2.subject_grants SET can_receive=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant};
            UPDATE cp6_requirement_set_application_delivery_v1.delivery SET lease_until=clock_timestamp()-interval '1 second',next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant};
            """);
        Assert.Equal("RECORDED",(await receiptWorker.RunAsync(h.Tenant)).State);Assert.Equal(1,receiptPosts);
        var projection=await new ApprovalReceiptIngestionV2(h.Db,ScopeFence(h),f.Authority).CurrentAsync(applied.Receipt.ReceiptStreamId);
        Assert.Equal(applied.Receipt,projection.Receipt);Assert.False(projection.PendingGap);
        using var chainTransport=new RequirementSetReceiptChainTransportV1(deliveryRegistry,TrustedHandler());
        var chain=await chainTransport.ReadAsync(deliveryRegistry.Route(h.Tenant)!,applied.Receipt.ReceiptStreamId,"0",10,null,default);
        Assert.Equal(200,chain.Status);
        var page=JsonSerializer.Deserialize<ApprovalReceiptChainPageV2>(chain.Body,ApprovalBindingOwnerV2.Json)!;
        Assert.Single(page.Items);
        oidc.ServiceClients[0].Enabled=false;Assert.Null(transport.Route(h.Tenant));
        Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());await app.StopAsync();
    }
}
