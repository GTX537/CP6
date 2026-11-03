using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task TaskDelegation_document_read_requires_both_current_business_field_owners_even_with_valid_read_grant()
    {
        await using var h=await Seed();var f=await DocumentDecisionSetup(h);var actual=await GrantDelegate(h);
        await CreateTaskGrant(h,f.First,actual,"binding-flow",new(["TASK_READ","TASK_REJECT"],["binding-flow"]));
        var actor=DecisionActor(h,actual);var tasks=DocumentDecisions(actor,f.Files);
        Assert.Single(await tasks.DocumentTasksAsync(f.Document,f.First));
        var inbox=NativeInbox(actor,business:new(ToolApproval(actor),DocumentReviews(actor,f.Files)));
        var item=Assert.Single((await inbox.ListAsync("pending",behalf:f.First)).Items);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=73,ResourceKey=CP6.Core.Services.Integration.OperationsOwnerV1.ControlledDocument,FieldName="Content",Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        // The actual delegate remains allowed to read the document; the original responsibility
        // holder has lost that field. A still-valid task grant cannot disclose through the delegate.
        Assert.NotNull(await DocumentReviews(actor,f.Files).ContextAsync(f.Document));
        Assert.Empty((await inbox.ListAsync("pending",behalf:f.First)).Items);
        Assert.Equal(403,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>inbox.DetailAsync(item.InstanceId,f.First))).Status);
    }
    private async Task<TaskDelegationRevisionV2> CreateTaskGrant(Harness h,Guid principal,Guid actual,string flow,TaskDelegationScopeV2? scope=null)
    {
        await GrantManagement(h);
        var input=GrantInput(actual,flow);if(scope!=null)input=input with{Scope=scope};
        return (await TaskGrants(DecisionActor(h,principal)).WriteAsync("CREATE",null,PersonalCommand(input,Guid.NewGuid().ToString("D")))).Result!;
    }
    [CurrentPermissionPostgresFact]
    public async Task TaskDelegation_app_dimensions_use_frozen_business_subject_and_intersect_with_flow_scope()
    {
        await using var h=await Seed();var(_,principal,_)=await DecisionSetup(h);await EngineeringAction(h,"oa-inbox","read");
        var actor=await GrantDelegate(h);var delegated=DecisionActor(h,actor);var task=await h.Db.Wf_FlowTasks.SingleAsync(t=>t.AssigneeId==principal);
        Assert.Null((await h.Db.Wf_FlowInstances.SingleAsync()).BizType);
        var grant=await CreateTaskGrant(h,principal,actor,"binding-flow",new(["TASK_READ","TASK_APPROVE"],["binding-flow"],["WMS_TOOL_DISCARD"]));
        var inbox=NativeInbox(delegated);Assert.Single((await inbox.ListAsync("pending",behalf:principal)).Items);
        var read=await inbox.MarkReadAsync("TASK",task.Id,PersonalCommand(new{},"app-delegated-read"),principal);
        Assert.Equal(grant.GrantId,read.Result!.GrantId);Assert.Equal(grant.Revision,read.Result.GrantRevision);
        var context=await Decisions(delegated).ContextAsync(task.Id,principal);
        // Both names are registered; a legitimate but different business type still cannot match.
        var owner=TaskGrants(DecisionActor(h,principal));
        var revised=(await owner.WriteAsync("REVISE",grant.GrantId,PersonalCommand(GrantInput(actor,"binding-flow") with{
            Scope=new(["TASK_READ","TASK_APPROVE"],["binding-flow"],["TEST_DISPOSITION"])},"other-business",grant.Version.ToString("D")))).Result!;
        Assert.Empty((await inbox.ListAsync("pending",behalf:principal)).Items);
        Assert.Equal("GRANT_SCOPE_DENIED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Decisions(delegated).ContextAsync(task.Id,principal))).Message);
        await owner.WriteAsync("REVISE",grant.GrantId,PersonalCommand(GrantInput(actor,"binding-flow") with{
            Scope=grant.Scope with{ActionKinds=["TASK_READ","TASK_REJECT"]}},"restore-business",revised.Version.ToString("D")));
        Assert.Equal(context,await Decisions(delegated).ContextAsync(task.Id,principal));
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=73,ResourceKey=CP6.Core.Services.Integration.OperationsOwnerV1.ToolInstance,FieldName="Remarks",Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Empty((await inbox.ListAsync("pending",behalf:principal)).Items);
        Assert.Equal(403,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>inbox.DetailAsync(task.InstanceId,principal))).Status);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.RoleId==73&&x.ResourceKey==CP6.Core.Services.Integration.OperationsOwnerV1.ToolInstance&&x.FieldName=="Remarks").ExecuteDeleteAsync();
        var decision=new ApprovalTaskDecisionInputV2(Guid.NewGuid(),"approve","current complete business grant",context.TaskRowVersion,context.InstanceRowVersion);
        Assert.Equal("GRANT_SCOPE_DENIED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Decisions(delegated).DecideAsync(task.Id,decision,principal,"\""+context.TaskRowVersion+"\""))).Message);
        decision=decision with{Decision="reject"};
        Assert.Equal("COMPLETED",(await Decisions(delegated).DecideAsync(task.Id,decision,principal,"\""+context.TaskRowVersion+"\"")).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task TaskDelegation_native_read_and_ordinary_vote_require_separate_current_actions_and_preserve_both_actors()
    {
        await using var h=await Seed();var runtime=await NativeFormSetup(h);await GrantManagement(h);var actual=await GrantDelegate(h);var owner=TaskGrants(h);
        var grant=(await owner.WriteAsync("CREATE",null,PersonalCommand(GrantInput(actual,actions:["TASK_READ"]),"read-only"))).Result!;
        TaskActor(h,actual);var form=PersonalForm(h with{User=actual});var inbox=NativeInbox(h,form);
        Assert.Empty((await inbox.ListAsync("pending")).Items);
        var page=await inbox.ListAsync("pending",behalf:h.User);Assert.Equal(actual,page.ActualUserId);Assert.Equal(h.User,page.EffectiveUserId);Assert.Single(page.Items);
        var detail=await inbox.DetailAsync(runtime.Instance,h.User);Assert.False(Assert.Single(detail.Tasks).CanRequestDecisionContext);
        var read=await inbox.MarkReadAsync("TASK",runtime.Task,PersonalCommand(new{},"delegate-read"),h.User);
        Assert.Equal(actual,read.Result!.ActualUserId);Assert.Equal(h.User,read.Result.PrincipalId);Assert.Equal(grant.GrantId,read.Result.GrantId);Assert.Equal(grant.Revision,read.Result.GrantRevision);
        var context=await form.TaskContextAsync(runtime.Task,h.User);
        var input=NativeVote(context,"explicit delegate decision","approve");
        Assert.Equal("GRANT_SCOPE_DENIED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>DecideNative(form,runtime.Task,input,h.User))).Message);
        TaskActor(h,h.User);var revised=(await owner.WriteAsync("REVISE",grant.GrantId,PersonalCommand(GrantInput(actual),"allow-approve",grant.Version.ToString("D")))).Result!;
        TaskActor(h,actual);Assert.True(Assert.Single((await inbox.DetailAsync(runtime.Instance,h.User)).Tasks).CanRequestDecisionContext);
        var result=await DecideNative(form,runtime.Task,input,h.User);Assert.Equal("COMPLETED",result.State);
        var vote=Assert.Single((await inbox.DetailAsync(runtime.Instance,h.User)).Votes);Assert.Equal(actual,vote.ActualUserId);Assert.Equal(h.User,vote.PrincipalId);Assert.Equal(h.User,vote.OnBehalfOfId);
        TaskActor(h,h.User);await owner.WriteAsync("REVOKE",grant.GrantId,PersonalCommand(new TaskDelegationRevokeV2("finished"),"revoke",revised.Version.ToString("D")));
        TaskActor(h,actual);Assert.Equal(403,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>inbox.ListAsync("done",behalf:h.User))).Status);
        Assert.Equal(result.VoteId,(await NativeOriginal(form,runtime.Task,input.CommandId))!.VoteId);
        Assert.Equal(h.User,(await h.Db.Wf_FlowTasks.SingleAsync(t=>t.Id==runtime.Task)).AssigneeId);
    }
    private TaskDelegationOwnerV2 TaskGrants(Harness h)=>new(h.Db,ScopeFence(h),PersonalForm(h,_=>Task.FromResult(
        Guid.Parse(h.Http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value))));
    private static async Task GrantManagement(Harness h)
    {
        foreach(var action in new[]{"query","delegate"})await EngineeringAction(h,"oa-settings",action);
        var menu=await h.Db.Sys_Menus.SingleAsync(m=>m.MenuKey=="oa-settings");
        if(!await h.Db.Sys_RoleMenus.AnyAsync(m=>m.MenuId==menu.MenuId&&m.RoleId==72))h.Db.Sys_RoleMenus.Add(new(){MenuId=menu.MenuId,RoleId=72});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }
    private static async Task<Guid> GrantDelegate(Harness h,string name="delegate-v2")
    {
        var id=Guid.NewGuid();h.Db.Sys_Users.Add(new(){Id=id,UserName=name,Password="unused",RoleId=72});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return id;
    }
    private static TaskDelegationInputV2 GrantInput(Guid actor,string flow="native-inbox",params string[] actions)
        =>new(actor,DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1),new(actions.Length==0?["TASK_READ","TASK_APPROVE"]:actions,[flow]),"explicit simulated task delegation");
    [CurrentPermissionPostgresFact]
    public async Task TaskDelegation_revision_revoke_preserves_history_original_and_never_changes_task_responsibility()
    {
        await using var h=await Seed();var runtime=await NativeFormSetup(h);await GrantManagement(h);var delegateId=await GrantDelegate(h);var owner=TaskGrants(h);
        var raw=PersonalCommand(GrantInput(delegateId),"grant");var created=await owner.WriteAsync("CREATE",null,raw);var grant=created.Result!;
        Assert.Equal(h.User,grant.GrantorId);Assert.Equal(h.User,(await h.Db.Wf_FlowTasks.SingleAsync(t=>t.Id==runtime.Task)).AssigneeId);
        Assert.Equal(JsonSerializer.Serialize(created),JsonSerializer.Serialize(await owner.WriteAsync("CREATE",null,raw)));
        var revised=await owner.WriteAsync("REVISE",grant.GrantId,PersonalCommand(GrantInput(delegateId,actions:["TASK_READ"]),"revise",grant.Version.ToString("D")));
        Assert.Equal("GRANT_VERSION_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.WriteAsync("REVOKE",grant.GrantId,PersonalCommand(new TaskDelegationRevokeV2("stale"),"stale",grant.Version.ToString("D"))))).Message);
        var revoked=await owner.WriteAsync("REVOKE",grant.GrantId,PersonalCommand(new TaskDelegationRevokeV2("stop delegating"),"revoke",revised.Result!.Version.ToString("D")));
        Assert.False(revoked.Result!.Enabled);Assert.Equal(h.User,revoked.Result.RevokedBy);Assert.NotNull(revoked.Result.RevokedAtUtc);
        Assert.Equal(new[]{"1","2","3"},(await owner.HistoryAsync(grant.GrantId)).Select(x=>x.Revision));
        Assert.Equal(JsonSerializer.Serialize(created),JsonSerializer.Serialize(await owner.OriginalAsync("CREATE","new","grant")));
        var menu=await h.Db.Sys_Menus.SingleAsync(m=>m.MenuKey=="oa-settings");
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.MenuId==menu.MenuId&&x.ActionCode=="delegate").ExecuteDeleteAsync();
        Assert.Equal("SUCCEEDED",(await owner.OriginalAsync("CREATE","new","grant")).State);
        Assert.Equal("CLOSED",(await owner.OriginalAsync("CREATE","new","never",true)).State);
        Assert.Equal("COMMAND_CLOSED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.WriteAsync("CREATE",null,PersonalCommand(GrantInput(delegateId),"never")))).Message);
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync("CREATE","new","unknown")).State);
        await using var other=await Seed();await NativeFormSetup(other);await GrantManagement(other);
        Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>TaskGrants(other).HistoryAsync(grant.GrantId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task TaskDelegation_closed_scope_requires_read_registered_exact_dimensions_and_rejects_chain_or_actor_override()
    {
        await using var h=await Seed();await NativeFormSetup(h);await GrantManagement(h);var actual=await GrantDelegate(h);var next=await GrantDelegate(h,"next-v2");var owner=TaskGrants(h);
        var input=GrantInput(actual);
        foreach(var scope in new TaskDelegationScopeV2[]{new(["TASK_APPROVE"],["native-inbox"]),new(["TASK_READ","WITHDRAW"],["native-inbox"]),new(["TASK_READ"],[]),new(["TASK_READ"]),new(["TASK_READ","TASK_READ"],["native-inbox"])})
            Assert.Equal("GRANT_SCOPE_INVALID",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.WriteAsync("CREATE",null,PersonalCommand(input with{Scope=scope},Guid.NewGuid().ToString())))).Message);
        Assert.Equal("GRANT_SCOPE_KEY_UNREGISTERED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.WriteAsync("CREATE",null,PersonalCommand(input with{Scope=new(["TASK_READ"],["Native-inbox"])},"case")))).Message);
        Assert.Equal("GRANT_INPUT_INVALID",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.WriteAsync("CREATE",null,PersonalCommand(input with{DelegateId=h.User},"self")))).Message);
        var granted=(await owner.WriteAsync("CREATE",null,PersonalCommand(input,"real"))).Result!;
        TaskActor(h,actual);
        Assert.Equal(granted.GrantId,Assert.Single((await owner.ListAsync()).Received).GrantId);
        Assert.Equal("GRANT_REDELEGATION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.WriteAsync("CREATE",null,PersonalCommand(GrantInput(next),"chain")))).Message);
        Assert.Equal("GRANT_REDELEGATION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.WriteAsync("CREATE",null,PersonalCommand(GrantInput(next),"override"),h.User))).Message);
        Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.WriteAsync("REVOKE",granted.GrantId,PersonalCommand(new TaskDelegationRevokeV2("not mine"),"foreign",granted.Version.ToString("D"))))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task TaskDelegation_current_single_grant_selection_is_ordinal_action_specific_and_expires_at_utc_end()
    {
        await using var h=await Seed();await NativeFormSetup(h);await GrantManagement(h);var actual=await GrantDelegate(h);var owner=TaskGrants(h);
        var input=GrantInput(actual);var one=(await owner.WriteAsync("CREATE",null,PersonalCommand(input,"one"))).Result!;
        var two=(await owner.WriteAsync("CREATE",null,PersonalCommand(input,"two"))).Result!;
        await using(var tx=await h.Db.Database.BeginTransactionAsync())
        {
            await ScopeFence(h).AcquireAsync();var reader=new TaskDelegationReaderV2(h.Db,ScopeEnvironment);
            var selected=await reader.Require(actual,h.User,"TASK_APPROVE","native-inbox",null,default);
            Assert.Equal(new[]{one.GrantId,two.GrantId}.OrderBy(x=>x.ToString("D"),StringComparer.Ordinal).First(),selected.GrantId);
            Assert.Null(await reader.Match(actual,h.User,"TASK_REJECT","native-inbox",null,default));
            Assert.Null(await reader.Match(actual,h.User,"TASK_APPROVE","NATIVE-INBOX",null,default));
            await reader.ValidateExact(selected,"TASK_APPROVE","native-inbox",null,default);await tx.RollbackAsync();
        }
        var end=DateTimeOffset.UtcNow.AddMinutes(-1);
        foreach(var g in new[]{one,two})await owner.WriteAsync("REVISE",g.GrantId,PersonalCommand(input with{ValidFromUtc=end.AddHours(-1),ValidToUtc=end},"expire-"+g.GrantId,g.Version.ToString("D")));
        await using(var tx=await h.Db.Database.BeginTransactionAsync())
        {
            await ScopeFence(h).AcquireAsync();var reader=new TaskDelegationReaderV2(h.Db,ScopeEnvironment);
            Assert.Equal("GRANT_SCOPE_DENIED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>reader.Require(actual,h.User,"TASK_READ","native-inbox",null,default))).Message);
            Assert.Equal("GRANT_AUTHORITY_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>reader.ValidateExact(one,"TASK_APPROVE","native-inbox",null,default))).Message);
            await tx.RollbackAsync();
        }
    }
}
