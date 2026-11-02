using System.Diagnostics;
using System.Text.Json;
using CP6.Tests.Platform;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

internal sealed class OaNativeBrowserPostgresFactAttribute:FactAttribute
{
    public OaNativeBrowserPostgresFactAttribute()
    {
        if(!CurrentPermissionPostgresFactAttribute.Selected||Environment.GetEnvironmentVariable("CP6_OA_BROWSER_WEB_ROOT")==null)
            Skip="Explicit isolated browser acceptance requires PostgreSQL settings and CP6_OA_BROWSER_WEB_ROOT/CP6_OA_BROWSER_NODE.";
    }
}

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_Vue_authoring_publication_and_runtime_reach_real_PostgreSQL()
    {
        await using var h=await Seed();var runtime=await NativeFormSetup(h);await GrantManagement(h);
        var delegateId=await GrantDelegate(h,"browser-delegate");var delegatePassword=new BCryptPasswordHasher(4).Hash("Price1!Password");
        await h.Db.Sys_Users.Where(x=>x.Id==delegateId).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Password,delegatePassword));
        foreach(var (menu,path) in new[]{("oa-form-catalog","/oa/form-catalog"),("oa-inbox","/oa/inbox"),("oa-designer","/oa/designer"),("oa-flow-admin","/oa/flow-admin"),("oa-settings","/oa/settings")})
            await h.Db.Sys_Menus.Where(x=>x.MenuKey==menu).ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,path));
        var before=await runtime.Owner.ContextAsync("personal");
        using var result=await BrowserScenario(h,delegateId,"oa-native-browser.mjs","browser-chain",new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var form=FormAuthoring(h);var latest=await form.ReadHeadAsync("personal");
        Assert.Equal("Browser accepted form",latest.Fields["Name"]);
        Assert.True(latest.Published);
        Assert.NotEqual(before.FormVersionId,latest.DraftId);
        var accepted=result.RootElement.GetProperty("runtime");
        var instanceId=accepted.GetProperty("flowInstanceId").GetGuid();
        var formVersion=accepted.GetProperty("formVersionId").GetGuid();
        var flowVersion=accepted.GetProperty("flowVersionId").GetGuid();
        var instance=await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(x=>x.Id==instanceId);
        Assert.Equal(formVersion,instance.FormDefVersionId);Assert.Equal(flowVersion,instance.FlowDefVersionId);
        Assert.NotEqual(before.FormVersionId,formVersion);Assert.NotEqual(before.FlowVersionId,flowVersion);
        var prior=await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(x=>x.Id==runtime.Instance);
        Assert.Equal(before.FormVersionId,prior.FormDefVersionId);Assert.Equal(before.FlowVersionId,prior.FlowDefVersionId);
        var data=await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync(x=>x.Id==accepted.GetProperty("formDataId").GetGuid());
        Assert.Contains("Browser exact submitted value",data.DataJson);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_personal_form_v2.commands WHERE tenant_id={h.Tenant} AND operation='FORM_DRAFT_CREATE' AND completed").SingleAsync());
        var detail=await NativeInbox(h,runtime.Owner).DetailAsync(instanceId);var vote=Assert.Single(detail.Votes);
        Assert.True(vote.Approved);Assert.Equal(delegateId,vote.ActualUserId);Assert.Equal(h.User,vote.PrincipalId);Assert.Equal(h.User,vote.OnBehalfOfId);
        var read=Assert.Single(detail.Tasks).Read!;Assert.Equal(delegateId,read.ActualUserId);Assert.Equal(h.User,read.PrincipalId);
        Assert.Empty((await NativeInbox(h,runtime.Owner).DetailAsync(runtime.Instance)).Votes);
        Assert.False((await form.ActivationPreviewAsync("personal")).Current.Enabled);
    }
    private static async Task<JsonDocument> BrowserScenario(Harness h,Guid delegateId,string script,string tag,Dictionary<string,string> settings,Dictionary<string,string>? extra=null)
    {
        var web=Environment.GetEnvironmentVariable("CP6_OA_BROWSER_WEB_ROOT")!;
        var node=Environment.GetEnvironmentVariable("CP6_OA_BROWSER_NODE")??throw new InvalidOperationException("CP6_OA_BROWSER_NODE_REQUIRED");
        var output=Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!,tag);Directory.CreateDirectory(output);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);
        var start=new ProcessStartInfo(node){WorkingDirectory=web,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(Path.Combine(web,"scripts",script));
        foreach(var pair in new Dictionary<string,string>{["CP6_OA_BROWSER_API"]=host.Origin.ToString(),["CP6_OA_BROWSER_TENANT"]=h.Tenant.ToString("N"),
            ["CP6_OA_BROWSER_ACTOR"]=h.User.ToString("D"),["CP6_OA_BROWSER_DELEGATE"]=delegateId.ToString("D"),["CP6_OA_BROWSER_RESULTS"]=output,["VITE_API_TARGET"]=host.Origin.ToString()})start.Environment[pair.Key]=pair.Value;
        foreach(var pair in extra??[])start.Environment[pair.Key]=pair.Value;
        using var process=Process.Start(start)??throw new InvalidOperationException("OWNED_BROWSER_NOT_STARTED");
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(8));}
        finally
        {
            if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}
            await File.WriteAllTextAsync(Path.Combine(output,"browser.log"),await stdout+"\n"+await stderr);
        }
        Assert.True(process.ExitCode==0,await File.ReadAllTextAsync(Path.Combine(output,"browser.log")));
        var result=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"result.json")));
        Assert.Equal("PASS",result.RootElement.GetProperty("result").GetString());
        return result;
    }
}
