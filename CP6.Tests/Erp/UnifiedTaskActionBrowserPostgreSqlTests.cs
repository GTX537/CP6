using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_APP_unified_task_context_decision_and_original_recovery()
    {
        await using var h=await Seed();var(_,first,_)=await DecisionSetup(h);var actor=DecisionActor(h,first);var c=await DecisionInput(actor,"reject");
        var hash=new BCryptPasswordHasher(4).Hash("Price1!Password");
        await h.Db.Sys_Users.Where(u=>u.Id==first).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Password,hash));
        var name=await h.Db.Sys_Users.Where(u=>u.Id==first).Select(u=>u.UserName).SingleAsync();
        await h.Db.Sys_Menus.Where(m=>m.MenuKey=="oa-inbox").ExecuteUpdateAsync(s=>s.SetProperty(m=>m.RoutePath,"/oa/inbox"));
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==c.Task);
        using var result=await BrowserScenario(h,first,"oa-app-task-browser.mjs","app-task-browser",new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")},
            new(){["CP6_APP_TASK"]=c.Task.ToString("D"),["CP6_APP_INSTANCE"]=task.InstanceId.ToString("D"),["CP6_APP_REVIEWER"]=name});
        var command=result.RootElement.GetProperty("commandId").GetGuid();var accepted=await Decisions(actor).OriginalAsync(command);
        Assert.Equal(2,accepted.Result.Task.InstanceStatus);Assert.NotNull(accepted.Result.DecisionId);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync());
        var raw=await h.Db.Database.SqlQuery<byte[]>($"SELECT wfc_raw_body AS \"Value\" FROM cp6_approval_decision_v2.commands WHERE id={command}").SingleAsync();
        using var envelope=JsonDocument.Parse(raw);Assert.Equal(string.Concat(Enumerable.Repeat("🙂",500)),envelope.RootElement.GetProperty("body").GetProperty("comment").GetString());
        Assert.False(envelope.RootElement.GetProperty("body").TryGetProperty("dataPatch",out _));
    }
}
