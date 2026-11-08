using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<Dictionary<string,string>> RestoreHostSettings(Harness h,PrivacyRestoreOptionsV1 options)
    {
        var settings=PrivacyExecutionEnvironment(h.Tenant);var index=1;
        foreach(var tenant in (await RestoreRuntime(h)).Options.Tenants.Keys.Where(x=>x!=h.Tenant))
        {
            settings["CrmIdentity__Tenants__"+tenant.ToString("D")]="us";
            var prefix="CrmOidc__Organizations__"+index+++"__";
            settings[prefix+"TenantId"]=tenant.ToString("D");settings[prefix+"Slug"]="restore-"+tenant.ToString("N");settings[prefix+"Region"]="us";settings[prefix+"CrmEnabled"]="true";
        }
        foreach(var (id,registration) in options.Candidates)
        {
            var prefix="PrivacyRestore__Candidates__"+id.ToString("D")+"__";
            settings[prefix+"ConnectionString"]=registration.ConnectionString;settings[prefix+"RegistrationVersion"]=registration.RegistrationVersion.ToString("D");
            settings[prefix+"BackupSha256"]=registration.BackupSha256;settings[prefix+"DisplayName"]=registration.DisplayName;
        }
        settings["PrivacyRestore__ControlConnectionString"]=options.ControlConnectionString;return settings;
    }
    private static async Task<Dictionary<string,string>> RestoreNamedLogin(AuthenticationApiProcess host,Harness h,string name)
    {
        using var response=await host.Send(new HttpRequestMessage(HttpMethod.Post,"api/auth/login")
            {Content=JsonContent.Create(new{UserName=name,Password="Price1!Password",TenantCode=h.Tenant.ToString("N")})});
        Assert.True(response.StatusCode==HttpStatusCode.OK,await response.Content.ReadAsStringAsync());
        return response.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0].Split('=',2)).Where(x=>x[1].Length>0).ToDictionary(x=>x[0],x=>x[1]);
    }
    private static async Task<T> RestoreHttp<T>(AuthenticationApiProcess host,Dictionary<string,string> cookies,HttpMethod method,string path,object? body=null)
    {
        using var response=await host.Send(OperationsRequest(cookies,method,"/api/platform/privacy-dispositions-v1/restore/"+path,body));
        Assert.True(response.StatusCode==HttpStatusCode.OK,await response.Content.ReadAsStringAsync());Assert.True(response.Headers.CacheControl!.NoStore);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyRestore_actual_old_archive_candidate_is_quarantined_before_any_authentication_or_disclosure()
    {
        await using var h=await Seed();await PrivacyDispositionSetup(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);
        await using var host=await AuthenticationApiProcess.Start(candidate.Connection,PrivacyExecutionEnvironment(h.Tenant));
        using var response=await host.Send(new HttpRequestMessage(HttpMethod.Post,"api/auth/login")
            {Content=JsonContent.Create(new{UserName="price-owner",Password="Price1!Password",TenantCode=h.Tenant.ToString("N")})});
        Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
        Assert.Contains("PRIVACY_RESTORE",await response.Content.ReadAsStringAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task PrivacyRestore_actual_HTTP_independent_approval_reapplies_old_backup_and_only_current_source_proof_opens_candidate()
    {
        await using var h=await Seed();var setup=await PrivacyDispositionSetup(h);await EngineeringAction(h,"privacy-disposition","execute");await RestorePermissions(h);
        await using var candidate=await PrivacyRestoreCandidateFixture.Create(h.Db.Database.GetConnectionString()!);
        var runtime=await RestoreRuntime(h);await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant},runtime);var actor=h with{Db=db};var approver=setup.Approver with{Db=db};
        var privacy=await PrivacyApprovedPlan(actor,setup.Target,setup.Policy,approver);await PrivacyDispositionOwner(actor).ExecuteAsync(privacy.Plan,PrivacyExecuteCommand(privacy),PrivacySession(actor));
        var options=RestoreRegistration(candidate);
        await using var sourceHost=await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!,await RestoreHostSettings(actor,options));
        var targetOptions=new PrivacyRestoreOptionsV1{ControlConnectionString=db.Database.GetConnectionString()!};
        await using var targetHost=await AuthenticationApiProcess.Start(candidate.Connection,await RestoreHostSettings(actor,targetOptions));
        using(var closed=await targetHost.Send(new HttpRequestMessage(HttpMethod.Get,"api/auth/profile")))Assert.Equal(HttpStatusCode.ServiceUnavailable,closed.StatusCode);
        var cookies=await Login(sourceHost,h);var approverCookies=await RestoreNamedLogin(sourceHost,h,"privacy-approver");
        var context=await RestoreHttp<PrivacyRestoreContextV1>(sourceHost,cookies,HttpMethod.Get,"context");Assert.Contains(context.Candidates,x=>x.Id==candidate.Id);
        var preview=await RestoreHttp<PrivacyRestorePreviewV1>(sourceHost,cookies,HttpMethod.Get,$"candidates/{candidate.Id}/preview");
        var requested=await RestoreHttp<PrivacyRestoreMetaOperationV1>(sourceHost,cookies,HttpMethod.Post,"plans",new PrivacyRestorePlanRequestV1(Guid.NewGuid(),candidate.Id,Guid.NewGuid(),preview.ScopeDigest));
        var decision=new PrivacyDispositionDecisionV1(Guid.NewGuid(),requested.Version!.Value,"APPROVE",Guid.NewGuid());
        using(var self=await sourceHost.Send(OperationsRequest(cookies,HttpMethod.Post,$"/api/platform/privacy-dispositions-v1/restore/plans/{requested.PlanId}/decisions",decision)))Assert.Equal(HttpStatusCode.Forbidden,self.StatusCode);
        await RestoreHttp<PrivacyRestoreMetaOperationV1>(sourceHost,approverCookies,HttpMethod.Post,$"plans/{requested.PlanId}/decisions",decision);
        var plan=await RestoreHttp<PrivacyRestorePlanV1>(sourceHost,cookies,HttpMethod.Get,$"plans/{requested.PlanId}");var command=RestoreCommand(plan);
        var result=await RestoreHttp<PrivacyRestoreResultV1>(sourceHost,cookies,HttpMethod.Post,$"plans/{plan.PlanId}/execute",command);
        Assert.Equal("APPLIED",result.State);Assert.Equal("PARTIAL",result.AggregateState);Assert.Equal("REAPPLIED",Assert.Single(result.Units!,x=>x.SubjectId==setup.Target).Outcome);
        Assert.Equal(JsonSerializer.Serialize(result),JsonSerializer.Serialize(await RestoreHttp<PrivacyRestoreResultV1>(sourceHost,cookies,HttpMethod.Get,$"operations/{command.OperationId}")));
        var targetCookies=await Login(targetHost,h);
        using(var profile=await targetHost.Send(OperationsRequest(targetCookies,HttpMethod.Get,"/api/auth/profile")))Assert.Equal(HttpStatusCode.OK,profile.StatusCode);
        using(var wrongAuthority=await targetHost.Send(OperationsRequest(targetCookies,HttpMethod.Get,"/api/platform/privacy-dispositions-v1/restore/context")))
        {Assert.Equal(HttpStatusCode.Conflict,wrongAuthority.StatusCode);Assert.Contains("USE_SOURCE_PRIVACY_AUTHORITY",await wrongAuthority.Content.ReadAsStringAsync());}
        await using(var marked=PrivacyRestoreNativeV1.Context(candidate.Connection,h.Tenant))
        {
            await marked.Database.ExecuteSqlRawAsync($"COMMENT ON DATABASE \"{candidate.Name}\" IS NULL");
            try
            {
                using var missingMarker=await targetHost.Send(OperationsRequest(targetCookies,HttpMethod.Get,"/api/auth/profile"));
                Assert.Equal(HttpStatusCode.ServiceUnavailable,missingMarker.StatusCode);Assert.Contains("TARGET_MARKER_REQUIRED",await missingMarker.Content.ReadAsStringAsync());
            }
            finally{await marked.Database.ExecuteSqlRawAsync($"COMMENT ON DATABASE \"{candidate.Name}\" IS 'CP6-PRIVACY-RESTORE-CANDIDATE/v1/{candidate.Id:D}'");}
        }
        using(var markerRestored=await targetHost.Send(OperationsRequest(targetCookies,HttpMethod.Get,"/api/auth/profile")))Assert.Equal(HttpStatusCode.OK,markerRestored.StatusCode);
        using(var disposed=await targetHost.Send(new HttpRequestMessage(HttpMethod.Post,"api/auth/login"){Content=JsonContent.Create(new{UserName="simulated-private-subject",Password="Price1!Password",TenantCode=h.Tenant.ToString("N")})}))
        {Assert.Equal(HttpStatusCode.BadRequest,disposed.StatusCode);Assert.Contains("E-SEC-001",await disposed.Content.ReadAsStringAsync());Assert.False(disposed.Headers.Contains("Set-Cookie"));}
        var held=await PrivacyDispositionOwner(actor).SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,null,true,Guid.NewGuid(),PrivacyReviewTime()),PrivacySession(actor));
        using(var stale=await targetHost.Send(OperationsRequest(targetCookies,HttpMethod.Get,"/api/auth/profile")))Assert.Equal(HttpStatusCode.ServiceUnavailable,stale.StatusCode);
        await PrivacyDispositionOwner(actor).SetHoldAsync(new(Guid.NewGuid(),h.Tenant,setup.Target,held.Version,false,Guid.NewGuid(),null),PrivacySession(actor));
    }
}
