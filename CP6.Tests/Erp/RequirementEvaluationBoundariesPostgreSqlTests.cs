using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task RequirementEvaluation_current_roles_policy_issuer_and_dependency_permission_guard_votes_and_originals()
    {
        await using var h=await Seed();var f=await EvaluationSetup(h);var o=Evaluations(h);await EvaluationReady(h,f);
        var reviewer=Evaluations(DecisionActor(h,f.First));var c=EvaluationCommand(await reviewer.ReadAsync(f.Definition.Reference.Id),"APPROVE");
        await h.Db.Sys_UserRoles.Where(x=>x.UserId==f.First&&x.RoleId==74).ExecuteDeleteAsync();
        Assert.Equal("REQUIREMENT_REVIEW_PROFESSIONAL_ROLE_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>reviewer.CommandAsync(c))).Message);
        var m=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="plm-engineering-requirement");
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==m.MenuId&&x.ActionCode=="review-policy").ExecuteDeleteAsync();
        var second=Evaluations(DecisionActor(h,f.Second));Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>second.CommandAsync(c with{OperationId=Guid.NewGuid()}))).Status);
        Assert.Empty((await o.ReadAsync(f.Definition.Reference.Id)).Votes);
        // An authorized professional can still return a package whose policy issuer was revoked.
        await second.CommandAsync(c with{OperationId=Guid.NewGuid(),Action="RETURN"});Assert.Equal("RETURNED",(await o.ReadAsync(f.Definition.Reference.Id)).Control!.State);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.RoleId==72&&x.ResourceKey=="plm-engineering-requirement").ExecuteUpdateAsync(x=>x.SetProperty(s=>s.ScopeType,1));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>second.ReadAsync(f.Definition.Reference.Id,"1"))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementEvaluation_competing_signatures_CAS_one_winner_then_exact_second_signature_completes()
    {
        await using var h=await Seed();var f=await EvaluationSetup(h);var ready=await EvaluationReady(h,f);var a=DecisionActor(h,f.First);var b=DecisionActor(h,f.Second);
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});b=b with{Db=peer};
        async Task<string> Save(Harness actor){try{return (await Evaluations(actor).CommandAsync(EvaluationCommand(ready,"APPROVE"))).State;}catch(OrderPriceCorrectionException e){return e.Message;}}
        var outcomes=await Task.WhenAll(Save(a),Save(b));Assert.Single(outcomes,x=>x=="IN_REVIEW");Assert.Single(outcomes,x=>x=="REQUIREMENT_REVIEW_CHANGED");
        var v=await Evaluations(h).ReadAsync(f.Definition.Reference.Id);var signed=Assert.Single(v.Votes).Body.ActorId;
        var remaining=Evaluations(signed==f.First?b:a);Assert.Equal("APPROVED",(await remaining.CommandAsync(EvaluationCommand(v,"APPROVE"))).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementEvaluation_missing_approval_or_close_record_never_claims_success_and_store_guard_loss_blocks_reads()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await EvaluationSetup(h);var o=Evaluations(h);await EvaluationReady(h,f);
        var first=Evaluations(DecisionActor(h,f.First));await first.CommandAsync(EvaluationCommand(await first.ReadAsync(f.Definition.Reference.Id),"APPROVE"));
        var second=Evaluations(DecisionActor(h,f.Second));var before=await second.ReadAsync(f.Definition.Reference.Id);var c=EvaluationCommand(before,"APPROVE");
        hook.Before=cmd=>{if(cmd.CommandText.Contains("INSERT INTO cp6_requirement_evaluation_v1.records")&&cmd.Parameters.Cast<System.Data.Common.DbParameter>().Any(p=>p.Value is string x&&x=="APPROVAL"))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
        Assert.NotNull(await Record.ExceptionAsync(()=>second.CommandAsync(c)));hook.Before=null;
        Assert.Equal(before.Etag,(await o.ReadAsync(f.Definition.Reference.Id)).Etag);Assert.Single((await o.ReadAsync(f.Definition.Reference.Id)).Votes);Assert.Equal("UNKNOWN",(await second.OperationAsync(c.OperationId)).State);
        Assert.Equal("APPROVED",(await second.CommandAsync(c)).State);
        var close=Guid.NewGuid();hook.Before=cmd=>{if(cmd.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
        Assert.Equal("ORIGINAL_OPERATION_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.OperationAsync(close,true))).Message);hook.Before=null;Assert.Equal("UNKNOWN",(await o.OperationAsync(close)).State);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_requirement_evaluation_v1.controls DISABLE TRIGGER requirement_review_complete");
        try{Assert.Equal("REQUIREMENT_REVIEW_STORE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ReadAsync(f.Definition.Reference.Id))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_requirement_evaluation_v1.controls ENABLE TRIGGER requirement_review_complete");}
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementEvaluation_native_hold_blocks_actual_withdrawal_until_consumer_transaction_finishes()
    {
        await using var h=await Seed();var f=await EvaluationSetup(h);var approved=await EvaluationApprove(h,f);var o=Evaluations(h);
        Task<RequirementEvaluationResultV1>? writer=null;var started=new TaskCompletionSource();var application="w195-requirement-withdraw-"+Guid.NewGuid().ToString("N");
        await EvaluationAuthority(h).ExecuteAsync("plm-engineering-requirement","query",false,async(a,t)=>{
            await o.HoldExact(a,f.Definition.Reference,approved.ControlReference!,t);
            writer=Task.Run(async()=>{await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});await peer.Database.OpenConnectionAsync();await peer.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('application_name',{application},false)");started.SetResult();return await Evaluations(h with{Db=peer}).CommandAsync(EvaluationCommand(approved,"WITHDRAW"));});
            await started.Task;var waiting=false;var until=DateTime.UtcNow.AddSeconds(8);
            while(!waiting&&DateTime.UtcNow<until){waiting=await h.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={application} AND wait_event_type='Lock') AS \"Value\"").SingleAsync(t);if(!waiting)await Task.Delay(25,t);}
            Assert.True(waiting,"Actual withdrawal must wait for the held consumer transaction");Assert.False(writer.IsCompleted);return true;
        });Assert.Equal("WITHDRAWN",(await writer!).State);
        Assert.Equal("REQUIREMENT_APPROVAL_SOURCE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>EvaluationAuthority(h).ExecuteAsync("plm-engineering-requirement","query",false,(a,t)=>o.HoldExact(a,f.Definition.Reference,approved.ControlReference!,t)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementEvaluation_relative_unit_base_change_and_unresolved_source_do_not_gain_approval()
    {
        await using var h=await Seed();var f=await EvaluationSetup(h);var units=Units(h);var basis=await MakeUnit(units,BaseUnit("N","FORCE"));
        var relative=await MakeUnit(units,BaseUnit("kN","FORCE") with{Mode="RATIO",BaseRef=basis.Reference,Numerator="1000"});
        var drafts=EvaluationDrafts(h);var criterion=new RequirementCriterionV1("force","QUANTITATIVE","Body","Source temperature","Load test","10.0000000000000000000000001",true,null,null,"kN","Proposal clause","Original decimal",Evaluation:new("requirement-evaluation/1",relative.Reference,false,true));
        await drafts.CommandAsync(ReviseRequirement(f.Definition,f.Definition.Body with{Requirement=f.Definition.Body.Requirement! with{Criteria=[criterion]}}));f=f with{Definition=await drafts.ReadAsync(f.Definition.Reference.Id)};
        var approved=await EvaluationApprove(h,f);Assert.Equal(2,approved.Packet!.Body.Inputs.Units.Length);await units.CommandAsync(Revise(basis,basis.Profile with{Enabled=false}));
        Assert.Contains((await Evaluations(h).SourceAsync(f.Definition.Reference)).Missing,x=>x=="UNIT_SOURCE_CHANGED:"+basis.Reference.Id);
        var unresolved=await CreateRequirement(drafts,SourceBody("UNRESOLVED"));var b=f.Definition.Body with{Requirement=f.Definition.Body.Requirement! with{Source=unresolved.Reference}};
        var other=await CreateRequirement(drafts,b);var view=await Evaluations(h).ReadAsync(other.Reference.Id);Assert.Contains("ORIGIN_CONFIRMATION_REQUIRED",view.Missing);
        Assert.Contains(view.Missing,x=>x.StartsWith("SOURCE_UNRESOLVED"));
    }
}
