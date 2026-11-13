using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private sealed class ServiceContextControllers:IApplicationFeatureProvider<ControllerFeature>
    {public void PopulateFeature(IEnumerable<ApplicationPart> parts,ControllerFeature feature){feature.Controllers.Clear();feature.Controllers.Add(typeof(CrmOidcController).GetTypeInfo());}}
    [CurrentPermissionPostgresFact]
    public Task Inquiry_service_context_actual_TLS_controller_to_separate_CRM_submit_transaction()=>ServiceContextHttp(false);
    [CurrentPermissionPostgresFact]
    public Task Inquiry_retention_actual_TLS_controller_to_CRM_current_service_transaction()=>ServiceContextHttp(true);
    [CurrentPermissionPostgresFact]
    public Task Inquiry_export_service_v2_actual_TLS_requires_independent_privacy_authority()=>ServiceContextHttp(false,true);
    [Pgt017MainFact]
    public Task PGT017_public_submit_and_Main_pause_commit() => ServiceContextHttp(false, tenantPause: true);
    [Pgt017MainFact]
    public Task PGT017_worker_and_Main_pause_commit()
    {
        var worker = Environment.GetEnvironmentVariable("CP6_PGT017_WORKER");
        Assert.Contains(worker, new[] { "export", "retention", "review" });
        return ServiceContextHttp(worker != "export", privacy: worker == "export", tenantPause: true);
    }

    private async Task ServiceContextHttp(bool retention, bool privacy = false, bool tenantPause = false)
    {
        var project=Environment.GetEnvironmentVariable("CP6_CRM_CROSS_PROJECT")??throw new InvalidOperationException("Owned CRM project required; no skip");
        var output=Environment.GetEnvironmentVariable("CP6_SERVICE_CONTEXT_OUTPUT")!;var folder=Path.GetDirectoryName(output)!;
        await using var h=await Seed();
        var pair = privacy ? await GrantPrivacy(h, 5) : retention && !tenantPause ? await GrantRetention(h) : await GrantIntake(h, 5);
        if (retention && tenantPause) await GrantRetention(h);
        var options=InquiryPermissionSigningTests.Options();
        using var key=InquiryPermissionSigningTests.Signer(options);var runtime=ContextRuntime(h.Tenant,options.Issuer);
        await new IdentityBootstrapService(h.Db,runtime).InitializeAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var config=ServiceRegistration(h,Guid.NewGuid());config.Oidc.Issuer=options.Issuer;
        config.Source.IdentityPublicationEnabled=true;config.Source.IdentityPublicationRevision=1;config.Source.Registrations[0].AuthenticationEpoch=Guid.NewGuid();
        var registry=new InquiryServicePrincipalRegistryV1(config.Source,config.Oidc);
        var publisherTenant=new TenantContext();await using(var db=server.Context(publisherTenant,runtime))
            await new InquiryServiceIdentityPublisherV1(db,publisherTenant,registry,runtime).PublishAsync(h.Tenant);
        var storeId = tenantPause ? Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!) : (Guid?)null;
        var sealConnection = tenantPause ? Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION")! : null;
        var stageDirectory = tenantPause ? Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")! : null;
        await using var binding = storeId is { } store
            ? new CrmInquiryTenantPauseV1(new Dictionary<Guid, CrmInquiryTenantPauseBindingV1>
                { [h.Tenant] = new() { StoreId = store, ConnectionString = sealConnection! } })
            : null;
        using var rsa=RSA.Create(2048);var certRequest=new CertificateRequest("CN=owned-W117-Main",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var names=new SubjectAlternativeNameBuilder();names.AddDnsName(new Uri(options.Issuer).Host);names.AddIpAddress(IPAddress.Loopback);certRequest.CertificateExtensions.Add(names.Build());
        using var created=certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));
        using var certificate=new X509Certificate2(created.Export(X509ContentType.Pfx));
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,0,l=>l.UseHttps(certificate)));
        builder.Services.AddScoped<TenantContext>();builder.Services.AddScoped<CP6Context>(sp=>server.Context(sp.GetRequiredService<TenantContext>(),runtime));
        builder.Services.AddScoped(sp=>new InquiryServicePermissionIssuerV1(sp.GetRequiredService<CP6Context>(),sp.GetRequiredService<TenantContext>(),registry,key,()=>privacy?PrivacyCatalog():IntakeCatalog(),runtime,binding));
        builder.Services.AddControllers().AddApplicationPart(typeof(CrmOidcController).Assembly).ConfigureApplicationPartManager(m=>m.FeatureProviders.Add(new ServiceContextControllers())).AddControllersAsServices();
        // Other controller actions are not exercised. Service authentication, publication gate,
        // source reads, permission decision, signing, model binding and TLS are all product code.
        builder.Services.RemoveAll<CrmOidcController>();
        builder.Services.AddTransient(sp=>new CrmOidcController(config.Oidc,null!,null!,null!,builder.Configuration,null!,null!,
            inquiryServicePermissions:sp.GetRequiredService<InquiryServicePermissionIssuerV1>()));
        await using var app=builder.Build();app.MapControllers();await app.StartAsync();
        try
        {
            var worker = tenantPause && (privacy || retention);
            var origin=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,UseProxy=false,
                ServerCertificateCustomValidationCallback=(_,cert,_,_)=>cert?.Thumbprint==certificate.Thumbprint}){BaseAddress=new(origin)};
            async Task<HttpResponseMessage> Send(object body,string? basic)
            {using var request=new HttpRequestMessage(HttpMethod.Post,privacy?"/connect/inquiry-service-context-v2":"/connect/inquiry-service-context"){Content=JsonContent.Create(body)};if(basic is not null)request.Headers.Add("Authorization",basic);return await http.SendAsync(request);}
            var command=new InquiryServiceContextRequestV1(privacy?"privacy:export":retention?"privacy:retention-worker":"intake:release",pair.SiteId,pair.FormId,ServiceNonce());
            using(var wrong=await Send(command,ServiceBasic("wrong").ToString()))Assert.Equal(HttpStatusCode.Unauthorized,wrong.StatusCode);
            using(var forged=await Send(new{command.Purpose,command.SiteId,command.FormId,command.RequestNonce,SubjectId=h.ActorId},ServiceBasic().ToString()))Assert.Equal(HttpStatusCode.BadRequest,forged.StatusCode);
            using (var good = await Send(command, ServiceBasic().ToString()))
            {
                Assert.Equal(HttpStatusCode.OK, good.StatusCode);
                Assert.True(good.Headers.CacheControl?.NoStore);
                if (storeId is { } expected && !worker)
                {
                    Assert.Equal($"{expected:D}/1", Assert.Single(good.Headers.GetValues("X-CP6-Inquiry-Tenant-Fence")));
                    var body = System.Text.Json.Nodes.JsonNode.Parse(await good.Content.ReadAsStringAsync())!.AsObject();
                    Assert.True(ContextSignature(body, options.PublicKeyBase64));
                }
            }
            if(privacy)
            {
                using var response=await Send(command,ServiceBasic().ToString());
                var body=System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
                Assert.Equal("cp6-inquiry-service-context-v2",body["schemaVersion"]!.GetValue<string>());
                Assert.True(ContextSignature(body,options.PublicKeyBase64,"cp6-inquiry-service-context-v2"));
                Assert.False(ContextSignature(body,options.PublicKeyBase64));
                using var projection=JsonDocument.Parse(body["permissionJson"]!.GetValue<string>());
                Assert.Equal("privacy:export",projection.RootElement.GetProperty("purpose").GetString());
                Assert.Equal(7,projection.RootElement.GetProperty("fieldPermissions").GetArrayLength());
            }
            var rows=await h.Db.CrmIdentitySnapshots.AsNoTracking().Where(x=>x.TenantId==h.Tenant).OrderBy(x=>x.AggregateId).ToArrayAsync();
            var run=(worker?"actual-main-worker-pause-":tenantPause?"actual-main-public-pause-":privacy?"actual-main-export-v2-":retention?"actual-main-retention-":"actual-main-submit-")+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ");
            var bridge=Path.Combine(folder,run+"-bridge.json");
            await File.WriteAllTextAsync(bridge,JsonSerializer.Serialize(new{issuer=options.Issuer,origin,thumbprint=certificate.Thumbprint,
                tenantId=h.Tenant,departmentId=h.Parent,subjectId=config.Source.Registrations[0].SubjectId,epoch=config.Source.Registrations[0].AuthenticationEpoch,
                siteId=pair.SiteId,formId=pair.FormId,clientId=ServiceClient,clientSecret=ServiceSecret,keyId=options.KeyId,publicKey=options.PublicKeyBase64,keyFromUtc=options.ActiveFromUtc,
                rows=rows.Select(x=>new{x.AggregateId,x.EventType,x.Version,x.PayloadSha256,x.IsDeleted,data=JsonDocument.Parse(x.PayloadJson).RootElement})},IdentityEventContracts.Json));
            var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"C:/Users/tt/.dotnet/dotnet.exe")
                {WorkingDirectory=Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(project)))!,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            var childTest = worker ? "PGT017_service_worker_actual_Main_TLS_and_pause"
                : tenantPause ? "PGT017_public_submit_actual_Main_TLS_and_pause"
                : privacy ? "Privacy_export_actual_Main_TLS_v2_current_service_and_CRM_artifact_transaction"
                : retention ? "Retention_actual_Main_TLS_current_authority_and_CRM_transaction"
                : "Public_submit_actual_Main_TLS_controller_PG_identity_and_CRM_complete_graph_commit";
            foreach(var arg in new[]{"test",project,"--no-restore","--artifacts-path",Environment.GetEnvironmentVariable("CP6_CRM_CROSS_ARTIFACTS")!,"--filter",
                "FullyQualifiedName~"+childTest,"--logger","trx;LogFileName="+run+".trx","--results-directory",folder,"-v:q","--disable-build-servers","-m:1","-p:UseSharedCompilation=false"})start.ArgumentList.Add(arg);
            if (tenantPause) start.ArgumentList.Add("--no-build");
            start.Environment["CP6_MAIN_SERVICE_BRIDGE"]=bridge;
            using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));
            string log;
            try
            {
                if (tenantPause)
                {
                    var environment = IdentityEnvironment(h.Tenant);
                    environment["CrmOidc__Issuer"] = options.Issuer;
                    var prefix = $"CrmInquiryTenantPause__Tenants__{h.Tenant:D}__";
                    environment[prefix + "StoreId"] = storeId!.Value.ToString("D");
                    environment[prefix + "ConnectionString"] = sealConnection!;
                    await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, environment);
                    await ExerciseCrmTenantPause(host, h, stageDirectory!, publicSubmission: true, childExited: child.WaitForExitAsync(timeout.Token));
                    using var revoked = await Send(command with { RequestNonce = ServiceNonce() }, ServiceBasic().ToString());
                    Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
                }
                await child.WaitForExitAsync(timeout.Token);
            }
            finally
            {
                if (!child.HasExited) { child.Kill(entireProcessTree:true); await child.WaitForExitAsync(); }
                log = await stdout + await stderr;
                await File.WriteAllTextAsync(Path.Combine(folder, run + ".log"), log);
                if (tenantPause) File.Delete(bridge);
            }
            Assert.True(child.ExitCode==0,log);
            var result=System.Xml.Linq.XDocument.Load(Path.Combine(folder,run+".trx"));
            var counters=result.Descendants().Single(x=>x.Name.LocalName=="Counters");
            Assert.Equal("1",counters.Attribute("total")!.Value);Assert.Equal("1",counters.Attribute("passed")!.Value);
            if(privacy && !tenantPause)
            {
                await h.Db.Sys_RoleActions.Where(x=>x.RoleId==71&&x.ActionCode=="export").ExecuteDeleteAsync();
                using var revoked=await Send(command,ServiceBasic().ToString());Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
                using var v1=await http.PostAsJsonAsync("/connect/inquiry-service-context",command);
                Assert.Equal(HttpStatusCode.BadRequest,v1.StatusCode);
            }
        }
        finally{await app.StopAsync();}
    }
}
