using System.Net;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task OaConn_trigger_retains_exact_form_and_requires_reviewer_current_original_read()
    {
        await using var h=await Seed();
        var (flow,form)=await PrepareDependencies(h);
        var publishedForm=await ActiveDependencyForm(form);
        var publication=await BindingFlow(flow,"form-trigger");
        await flow.SetActivationAsync("form-trigger",ActivationCommand((await flow.ActivationPreviewAsync("form-trigger")).Current,publication.VersionId));
        await ConnPeople(h);
        var formScope=h.Db.Sys_RoleDataScopes.Where(s=>s.RoleId==72 && s.ResourceKey==FormAuthoringOwnerV2.Resource);
        if(await formScope.AnyAsync()) await formScope.ExecuteUpdateAsync(s=>s.SetProperty(r=>r.ScopeType,5));
        else
        {
            h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey=FormAuthoringOwnerV2.Resource,ScopeType=5});
            await h.Db.SaveChangesAsync();
        }
        var profile=await ConnRegistration(h,"MESSAGE",new(Registration:"form-message-v1",Schema:"message-v1",VarsSchema:"vars-v1",
            RateLimitPolicy:"local",StarterAuthority:"local",StarterId:h.User));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,ConnSettings());
        var maker=new ConnApi(host,await Login(host,h));
        var checker=new ConnApi(host,await Login(host,"conn-reviewer","Reviewer1!Test",h.Tenant.ToString("N")));
        var trigger=await ConnCreate(maker,"TRIGGER",new("exact-form","MESSAGE",profile,publication.Key,publication.VersionId),"表单准确原文");
        var context=await checker.Data(trigger.Route+"/context");
        var pin=context.GetProperty("content").GetProperty("dependencies").EnumerateArray()
            .Single(d=>d.GetProperty("kind").GetString()=="FORM");
        Assert.Equal(publishedForm.VersionId,pin.GetProperty("version").GetGuid());
        var approved=await ConnReview(maker,checker,trigger,"PUBLISH");
        await formScope.ExecuteUpdateAsync(s=>s.SetProperty(r=>r.ScopeType,1));
        await checker.Reject(trigger.Route+"/context",null,HttpStatusCode.NotFound,"RESOURCE_NOT_FOUND");
        var retained=await maker.Data(trigger.Route+"/context");
        Assert.Equal(publishedForm.VersionId,retained.GetProperty("content").GetProperty("dependencies")[0].GetProperty("version").GetGuid());
        using var refused=await maker.Send(trigger.Route+"/publish",
            ConnCmd(trigger.Head,new ConnControlInputV1(trigger.Version,DecisionRef:approved)));
        Assert.Contains(refused.StatusCode,new[]{HttpStatusCode.Forbidden,HttpStatusCode.PreconditionFailed});
        using var body=JsonDocument.Parse(await refused.Content.ReadAsByteArrayAsync());
        Assert.Contains(body.RootElement.GetProperty("code").GetString(),new[]{"REVIEWER_UNAVAILABLE","REVIEW_STALE"});
        Assert.Equal("DRAFT",(await maker.Data(trigger.Route+"/context")).GetProperty("state").GetString());
    }
}
