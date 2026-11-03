using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_management_batch_preview_lost_commit_reload_and_exact_item_results()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h,true);var target=await GrantDelegate(h,"browser-batch-target");
        await EngineeringAction(h,"oa-inbox","batch-transfer");await EngineeringAction(h,"user","query");
        await h.Db.Sys_Menus.Where(m=>m.MenuKey=="oa-inbox").ExecuteUpdateAsync(s=>s.SetProperty(m=>m.RoutePath,"/oa/inbox"));
        using var result=await BrowserScenario(h,h.User,"oa-task-batch-browser.mjs","task-batch-browser",
            new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")},new(){["CP6_TASK_INSTANCE"]=f.Instance.ToString("D")});
        Assert.Equal(2,await h.Db.Wf_FlowTasks.CountAsync(t=>t.InstanceId==f.Instance&&t.AssigneeId==target&&t.Status==0));
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.assignment_changes a JOIN public.\"Wf_FlowTask\" t ON t.\"Id\"=a.task_id WHERE t.\"InstanceId\"={f.Instance}").SingleAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.votes v JOIN cp6_personal_form_v2.commands c ON c.id=v.command_id WHERE c.root_id={f.Instance}").SingleAsync());
    }
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_returned_form_edit_resubmit_lost_response_and_same_instance_approval()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);await NativeOperationPermissions(h);
        await h.Db.Sys_Menus.Where(m=>m.MenuKey=="oa-inbox").ExecuteUpdateAsync(s=>s.SetProperty(m=>m.RoutePath,"/oa/inbox"));
        using var result=await BrowserScenario(h,h.User,"oa-task-recovery-browser.mjs","task-recovery-browser",
            new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")},new(){["CP6_TASK_INSTANCE"]=f.Instance.ToString("D")});
        Assert.Single(await h.Db.Wf_FlowInstances.ToArrayAsync());Assert.Single(await h.Db.Wf_FormDatas.ToArrayAsync());
        var instance=await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync();Assert.Equal(f.Instance,instance.Id);Assert.Equal(1,instance.Status);
        Assert.Equal(2,await h.Db.Wf_FlowTasks.CountAsync());Assert.Contains("Browser returned correction",(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.execution_cycles WHERE root_id={f.Instance}").SingleAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.commands WHERE root_id={f.Instance} AND operation='SFS_RETURNED_RESUBMIT' AND completed").SingleAsync());
        Assert.Single((await NativeInbox(h).DetailAsync(f.Instance)).Votes);
    }
}
