using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task RequirementExclusion_mandatory_prohibited_or_unknown_source_cannot_be_waived_by_free_text()
    {
        foreach(var pair in new[]{("MANDATORY","SOURCE_DECISION_REQUIRED"),("OPTIONAL","PROHIBITED"),("UNKNOWN","SOURCE_DECISION_REQUIRED"),("OPTIONAL","UNKNOWN")})
        {
            await using var h=await Seed();var f=await ExclusionSetup(h,pair.Item1,pair.Item2);var owner=Sets(h);var command=SetRevise(f.Set,ExcludedBody(f));
            Assert.Equal(422,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(command))).Status);
            Assert.Equal("UNKNOWN",(await owner.OperationAsync(command.OperationId,false)).State);Assert.Equal("1",(await owner.ReadAsync(f.Set.Reference.Id)).Reference.Version);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementExclusion_wrong_or_changed_source_never_authorizes_new_effect()
    {
        await using var h=await Seed();var f=await ExclusionSetup(h);var owner=Sets(h);
        var noScope=SetRevise(f.Set,ExcludedBody(f) with{Scope=""});Assert.Equal(422,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(noScope))).Status);
        var wrong=SetRevise(f.Set,ExcludedBody(f,f.Member.Definition.Reference));Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(wrong))).Status);
        await EvaluationDrafts(h).CommandAsync(ReviseRequirement(f.Member.Source,f.Member.Source.Body with{Name="Source authority changes exact version"}));
        var stale=SetRevise(f.Set,ExcludedBody(f));Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(stale))).Status);
        Assert.Equal("UNKNOWN",(await owner.OperationAsync(stale.OperationId,false)).State);Assert.Equal("1",(await owner.ReadAsync(f.Set.Reference.Id)).Reference.Version);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementExclusion_source_authority_receipt_failure_and_inherited_original_actor_remain_separate()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await ExclusionSetup(h);var owner=Sets(h);var command=SetRevise(f.Set,ExcludedBody(f));
        var sourceMenu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="plm-engineering-requirement");
        var setMenu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="plm-requirement-set");
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.MenuId==setMenu.MenuId&&x.ActionCode=="exclusion-decide").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(command))).Status);
        await EngineeringAction(h,"plm-requirement-set","exclusion-decide");
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.MenuId==sourceMenu.MenuId&&x.ActionCode=="source-confirm").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(command))).Status);
        await EngineeringAction(h,"plm-engineering-requirement","source-confirm");
        hook.Before=c=>{if(c.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))throw new IOException("W325 receipt unavailable");return Task.CompletedTask;};
        var error=await Record.ExceptionAsync(()=>owner.CommandAsync(command));hook.Before=null;Assert.Contains("W325 receipt unavailable",error!.ToString());
        Assert.Equal("UNKNOWN",(await owner.OperationAsync(command.OperationId,false)).State);Assert.Equal("1",(await owner.ReadAsync(f.Set.Reference.Id)).Reference.Version);
        var result=await owner.CommandAsync(command);var accepted=await owner.ReadAsync(f.Set.Reference.Id);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.MenuId==setMenu.MenuId&&x.ActionCode=="exclusion-decide" || x.RoleId==72&&x.MenuId==sourceMenu.MenuId&&x.ActionCode=="source-confirm").ExecuteDeleteAsync();
        Assert.Equal(result,await owner.CommandAsync(command));
        await Sets(DecisionActor(h,f.Member.First)).CommandAsync(SetRevise(accepted,accepted.Body with{Name="A different editor retains the original exact decision"}));
        var inherited=await owner.ReadAsync(f.Set.Reference.Id);var original=Assert.Single(inherited.ExclusionStatus!);
        Assert.Equal("ACCEPTED",original.State);Assert.Equal(accepted.Reference,original.DecisionVersion);Assert.Equal(h.User,original.DecisionActor);Assert.Equal(f.Member.First,inherited.ActorId);
        // Changing the actual application scope is a new source decision, not ordinary inheritance.
        var changedScope=SetRevise(inherited,inherited.Body with{Scope="A different application scope"});
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(changedScope))).Status);
        Assert.Equal(inherited.Reference,(await owner.ReadAsync(f.Set.Reference.Id)).Reference);
        await EngineeringAction(h,"plm-requirement-set","exclusion-decide");await EngineeringAction(h,"plm-engineering-requirement","source-confirm");
        await owner.CommandAsync(changedScope);var renewed=await owner.ReadAsync(f.Set.Reference.Id);
        Assert.Equal(renewed.Reference,Assert.Single(renewed.ExclusionStatus!).DecisionVersion);Assert.Equal(h.User,Assert.Single(renewed.ExclusionStatus!).DecisionActor);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementExclusion_final_authority_failure_rolls_back_and_two_connections_have_one_successor()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var f=await ExclusionSetup(h);var owner=Sets(h);var command=SetRevise(f.Set,ExcludedBody(f));var reached=false;
        hook.Before=async c=>{
            if(!c.CommandText.Contains("WITH field_subject"))return;
            await using var probe=c.Connection!.CreateCommand();probe.Transaction=c.Transaction;
            probe.CommandText=$"SELECT EXISTS(SELECT FROM cp6_requirement_set_v1.versions WHERE tenant_id='{h.Tenant}' AND object_id='{f.Set.Reference.Id}' AND revision=2)";
            if((bool)(await probe.ExecuteScalarAsync())!){reached=true;throw new IOException("W325 final authority unavailable");}
        };
        try{Assert.Contains("W325 final authority unavailable",(await Record.ExceptionAsync(()=>owner.CommandAsync(command)))!.ToString());}finally{hook.Before=null;}
        Assert.True(reached);Assert.Equal("1",(await owner.ReadAsync(f.Set.Reference.Id)).Reference.Version);Assert.Equal("UNKNOWN",(await owner.OperationAsync(command.OperationId,false)).State);
        await using var db=server.Context(new CP6.Core.Services.Common.TenantContext{CurrentTenantId=h.Tenant});var peer=Sets(h with{Db=db});
        async Task<string> Write(RequirementSetService target,RequirementSetCommandV1 c){try{return(await target.CommandAsync(c)).Version;}catch(OrderPriceCorrectionException e){return e.Message;}}
        var outcomes=await Task.WhenAll(Write(owner,command),Write(peer,command with{OperationId=Guid.NewGuid()}));Assert.Single(outcomes,x=>x=="2");Assert.Single(outcomes,x=>x=="REQUIREMENT_SET_VERSION_CHANGED");
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementExclusion_cannot_hide_current_or_ancestral_conflicts_by_marking_a_member_optional()
    {
        await using var h=await Seed();var f=await ExclusionSetup(h);var owner=Sets(h);
        var definition=await CreateRequirement(EvaluationDrafts(h),f.Member.Definition.Body with{Name="Other exact requirement"});
        var approval=await EvaluationApprove(h,f.Member with{Definition=definition});var left=f.Set.Body.Members[0];
        var right=new RequirementSetMemberV1(definition.Reference,approval.ControlReference,approval.CurrentApproval!.Reference,"INCLUDED","Keep the other obligation");
        await owner.CommandAsync(SetRevise(f.Set,f.Set.Body with{Members=[left,right],Conflicts=[new("C1",left.Requirement,right.Requirement,"Unresolved adverse intersection","UNRESOLVED","Must remain visible")]}));
        var conflicted=await owner.ReadAsync(f.Set.Reference.Id);var body=ExcludedBody(f with{Set=conflicted});
        Assert.Equal(422,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(SetRevise(conflicted,body)))).Status);
        await owner.CommandAsync(SetRevise(conflicted,conflicted.Body with{Conflicts=[]}));var hidden=await owner.ReadAsync(f.Set.Reference.Id);
        await owner.CommandAsync(SetRevise(hidden,ExcludedBody(f with{Set=hidden})));var retained=await owner.ReadAsync(f.Set.Reference.Id);
        Assert.Contains("PREVIOUS_CONFLICT_OMITTED:C1",retained.Missing);Assert.Equal(2,retained.Body.Members.Length);
        var ready=await SetReady(h,retained.Reference.Id);Assert.Equal(422,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>SetReviews(h).SubmitAsync(retained.Reference.Id,ready.Etag,SetSubmit(ready)))).Status);
    }
}
