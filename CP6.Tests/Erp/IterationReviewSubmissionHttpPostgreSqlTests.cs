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
using CP6.Core.Services.Pub;
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
    public async Task IterationReview_actual_C02_TLS_submission_intake_lost_ack_original_recovery_and_native_FLOW_start()
    {
        await using var h=await Seed();var f=await IterationReviewSetup(h,1);await PrepareIterationFlow(h,f);
        await IterationReviews(h,f.Files).SubmitAsync(f.Context.IterationId,f.Context.Etag,IterationSubmit(f));
        var payload=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_iteration_review_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var e=ApprovalSubmissionWireV2.Parse(payload).Value;
        var principal=await h.Db.Database.SqlQuery<Guid>($"SELECT service_principal AS \"Value\" FROM cp6_approval_binding_v2.registrations WHERE tenant_id={h.Tenant} AND owner={ApprovalBusinessProfileV2.IterationOwner}").SingleAsync();
        var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        var origin="https://127.0.0.1:"+port;const string secret="explicit isolated delivery fixture secret";
        using var rsa=RSA.Create(2048);var key=new RsaSecurityKey(rsa.ExportParameters(false)){KeyId="app-fixture"};
        var certificateRequest=new CertificateRequest("CN=owned-W199-Submission",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var names=new SubjectAlternativeNameBuilder();names.AddIpAddress(IPAddress.Loopback);certificateRequest.CertificateExtensions.Add(names.Build());
        using var created=certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));
        using var certificate=new X509Certificate2(created.Export(X509ContentType.Pfx));
        var oidc=new CrmOidcOptions{Enabled=true,Issuer=origin,AllowInsecureLoopback=true,ActiveKeyId=key.KeyId,
            Keys=[new(){Kid=key.KeyId,Pem=rsa.ExportRSAPrivateKeyPem()}],
            Organizations=[new(){TenantId=h.Tenant,Slug="fixture",Region="us",CrmEnabled=true}],
            ServiceClients=[new(){ClientId="fixture-service",TenantId=h.Tenant,Enabled=true,SecretSha256=CrmOidcCrypto.Hash(secret),AllowedScopes=["cp6.services"]}]};
        var options=new ApprovalIntakeOptionsV2{Enabled=true,EnvironmentId=ScopeEnvironment,Version="http-fixture",EvidenceMode="DEMO",
            Clients=[new(){ClientId="fixture-service",TenantId=h.Tenant,PrincipalId=principal,CanSubmit=true}]};
        Assert.Throws<InvalidOperationException>(()=>new ApprovalIntakeRegistryV2(options,oidc)); // DEMO cannot be enabled in a non-development host.
        var registry=new ApprovalIntakeRegistryV2(options,oidc,development:true);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});
        builder.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,port,l=>l.UseHttps(certificate)));builder.Logging.ClearProviders();
        builder.Services.AddSingleton(oidc);builder.Services.AddSingleton(registry);builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ITenantContext>(_=>new TenantContext{CurrentTenantId=h.Tenant});
        builder.Services.AddScoped(sp=>server.Context(sp.GetRequiredService<ITenantContext>()));
        builder.Services.AddScoped<IApprovalServiceAuthorityV2,ApprovalIntakeServiceAuthorityV2>();
        builder.Services.AddScoped(sp=>{
            var db=sp.GetRequiredService<CP6Context>();var peer=h with{Db=db,Http=new DefaultHttpContext()};
            var operations=CandidateAuthority(peer,_=>throw new InvalidOperationException("NO_HUMAN_SESSION_IN_SERVICE_REQUEST"));
            var scope=ScopeFence(peer);var tools=new ToolDispositionApprovalOwnerV1(db,operations,scope);
            var files=new FileContentOwnerV1(db,operations,f.Files,new ContentInspector());
            var documents=new ControlledDocumentOwnerV1(db,operations,files,scope);
            var iterations=new EngineeringIterationService(db,operations,Sets(peer),documents,scope);
            var checks=new EngineeringSourceCheckService(db,operations,documents,Specifications(peer,f.Files));
            var candidates=new IterationCandidateService(db,operations,iterations,checks,scope);
            var reviews=new IterationReviewOwnerV1(db,operations,scope,iterations,candidates);
            return new ApprovalIntakeOwnerV2(db,scope,sp.GetRequiredService<IApprovalServiceAuthorityV2>(),tools,Authoring(peer),new(tools,null,null,reviews)){ReceiveClock=()=>BindingTime().AddSeconds(2)};
        });
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
        var identityOptions=new CP6.Core.Services.CrmIdentity.CrmIdentityOptions{Enabled=true,Issuer=origin,Tenants={[h.Tenant]="us"},ProjectionReaderClientIds=["fixture-service"]};
        builder.Services.AddSingleton(new CP6.Core.Services.CrmIdentity.CrmIdentityRuntime(identityOptions,new CP6.Core.Services.CrmIdentity.IdentityEventValidator(
            CP6.Platform.Messaging.Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory,"contracts/events/platform")),origin)));
        builder.Services.AddScoped<CP6.Core.Services.CrmIdentity.ICrmServiceTokenRecordStore,CP6.Core.Services.CrmIdentity.CrmServiceTokenRecordStore>();
        builder.Services.AddScoped(sp=>new CrmOidcServiceTokens(oidc,sp.GetRequiredService<CrmOidcCrypto>(),sp.GetRequiredService<CrmOidcDirectory>(),
            records:sp.GetRequiredService<CP6.Core.Services.CrmIdentity.ICrmServiceTokenRecordStore>()));
        builder.Services.AddControllers().AddApplicationPart(typeof(ApprovalIntakeControllerV2).Assembly);
        await using var app=builder.Build();var posts=0;var lose=true;
        app.Use(async(context,next)=>{
            if(context.Request.Path!="/internal/oa/app/v2/submissions"){await next(context);return;}
            posts++;var target=context.Response.Body;using var buffer=new MemoryStream();context.Response.Body=buffer;
            try{await next(context);if(lose&&context.Response.StatusCode==201){lose=false;context.Abort();return;}buffer.Position=0;await buffer.CopyToAsync(target);}
            finally{context.Response.Body=target;}
        });
        app.UseAuthentication();app.UseAuthorization();app.MapControllers();await app.StartAsync();
        var sendOptions=new ToolApprovalDeliveryOptionsV1{Enabled=true,Version="fixture-1",DestinationOrigin=origin,
            Clients=[new(){TenantId=h.Tenant,ClientId="fixture-service",ClientSecret=secret}]};
        var deliveryRegistry=new ToolApprovalDeliveryRegistryV1(sendOptions,oidc,registry,development:true);
        using(var strictTransport=new ToolApprovalDeliveryTransportV1(deliveryRegistry))
            await Assert.ThrowsAsync<HttpRequestException>(()=>strictTransport.SendAsync(deliveryRegistry.Route(h.Tenant)!,e.CommandId,payload,true,default));
        using var transport=new IterationReviewSubmissionTransportV1(deliveryRegistry,new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,
            ServerCertificateCustomValidationCallback=(request,cert,_,_)=>request.RequestUri?.GetLeftPart(UriPartial.Authority)==origin && cert!=null && cert.RawData.AsSpan().SequenceEqual(certificate.RawData)});
        var worker=new IterationReviewDeliveryV1(h.Db,ScopeFence(h),transport);
        Assert.Equal("OUTCOME_UNKNOWN",(await worker.RunAsync(h.Tenant)).State);Assert.Equal(1,posts);
        Assert.Single(await h.Db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_approval_intake_v2.submissions WHERE tenant_id={h.Tenant} AND owner={ApprovalBusinessProfileV2.IterationOwner}").ToArrayAsync());
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="review-submit"||x.ActionCode=="review-policy").ExecuteDeleteAsync();await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_iteration_review_v1.delivery SET lease_until=clock_timestamp()-interval '1 second',next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant}");
        Assert.Equal("RECEIVED",(await worker.RunAsync(h.Tenant)).State);Assert.Equal(1,posts);
        Assert.True(await h.Db.CrmServiceTokenRecords.CountAsync(x=>x.TenantId==h.Tenant)>=3);
        var receipt=await ToolApprovalDeliveryV1.ReadAsync(h.Db,ScopeEnvironment,e.SubmissionId,default,ApprovalBusinessProfileV2.IterationOwner);
        Assert.Equal(e.SubmissionId,receipt!.Receipt!.SubmissionId);Assert.Null(receipt.Receipt.InstanceId);
        oidc.ServiceClients[0].Enabled=false;Assert.Null(transport.Route(h.Tenant));
        var started=await Starter(h).RunAsync(h.Tenant);Assert.Equal("RUNNING",started.State);Assert.Equal(e.SubmissionId,started.SubmissionId);
        Assert.Equal("IDLE",(await Starter(h).RunAsync(h.Tenant)).State);
        Assert.Equal("IN_REVIEW",(await Iterations(h,f.Files).ReadAsync(f.Context.IterationId)).Body.Iteration!.Stage);
        Assert.Equal("NOT_RECEIVED",(await IterationReviews(h,f.Files).SubmissionAsync(f.Context.IterationId,e.SubmissionId)).Submission.ApplicationState);
        await app.StopAsync();
    }
}
