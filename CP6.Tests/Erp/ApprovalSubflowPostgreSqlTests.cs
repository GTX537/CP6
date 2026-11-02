using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record SubflowFixture(IntakeFixture Intake, Guid First, Guid Second,
        FlowPublicationV2 Child, FormPublicationV2 Form, FormAuthoringOwnerV2 Forms);

    private async Task<SubflowFixture> SubflowSetup(Harness h, string? rootSchema = null, bool nested = false, bool immediate = false)
    {
        await EngineeringAction(h,"oa-designer","form-save"); await EngineeringAction(h,"oa-designer","form-publish");
        await EngineeringAction(h,"oa-flow-admin","form-enable");
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        h.Db.Sys_Roles.Add(new(){RoleId=73,RoleName="subprocess reviewers"});
        h.Db.Sys_Users.AddRange(new(){Id=first,UserName="child-reviewer-1",Password="fixture",RoleId=73},
            new(){Id=second,UserName="child-reviewer-2",Password="fixture",RoleId=73});
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await TaskPermission(h,first); await TaskPermission(h,second); await EngineeringAction(h,"oa-inbox","query");
        // Freeze the business policy only after fixture IAM setup; changing the issuer's
        // permission witness afterward correctly invalidates its original policy.
        var f = await IntakeSetup(h); var forms = FormAuthoring(h);
        await forms.ConfigureAsync(new(Guid.NewGuid(),"absent",true,[],"runtime form"));
        await forms.ConfigurePublicationAsync(new(Guid.NewGuid(),"absent",true,false,[],"runtime form publication"));
        var form = await ActiveDependencyForm(forms);
        var schema = immediate ? """{"start":"s","nodes":[{"id":"s","type":"start"},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"e"}]}""" : StartSchema;
        var saved = await f.Flows.SaveAsync("child",DependentCommand(form:"form",schema:schema));
        var child = (await f.Flows.PublishAsync("child",PublicationCommand(await Preview(f.Flows,"child",saved)))).Result!.Publication!;
        await f.Flows.SetActivationAsync("child",ActivationCommand((await f.Flows.ActivationPreviewAsync("child")).Current,child.VersionId));
        if(nested) await ActiveDependencyFlow(f.Flows,"middle",child:"child");
        var draft = await f.Flows.CreateNextDraftAsync("binding-flow",NextDraftCommand((await f.Flows.ReadHeadAsync("binding-flow")).Version,f.Version));
        var root = await f.Flows.SaveAsync("binding-flow",DependentCommand(id:"root",token:draft.ResourceVersion,schema:rootSchema??ChildSchema(nested?"middle":"child")));
        var p = (await f.Flows.PublishAsync("binding-flow",PublicationCommand(await Preview(f.Flows,"binding-flow",root),"root-publish"))).Result!.Publication!;
        var binding = await f.Binding.SaveAsync(f.Bound.Result.BindingId,BindingEtag(f.Bound),BindingBytes(
            BindingInput(p.VersionId,f.ReceiveAt.AddSeconds(-1)) with{BizType="WMS_TOOL_DISCARD",SubjectKind="TOOL_DISPOSITION_REQUEST"}));
        f=f with{Version=p.VersionId,Bound=binding}; await f.Owner.SubmitAsync(f.Payload);
        return new(f,first,second,child,form,forms);
    }

    private static async Task<(ApprovalTaskDecisionInputV2 Input, Guid Task)> SubflowInput(Harness actor, Guid task, string decision="approve")
    {
        var view=await Decisions(actor).ContextAsync(task);
        return(new(Guid.NewGuid(),decision,"Review exact subprocess duty",view.TaskRowVersion,view.InstanceRowVersion),task);
    }

    [CurrentPermissionPostgresFact]
    public async Task App_subflow_nested_exact_pins_survive_disable_and_produce_one_original_root_decision()
    {
        await using var h=await Seed();var f=await SubflowSetup(h,nested:true);
        await f.Forms.SetActivationAsync("form",FormActivationCommand((await f.Forms.ActivationPreviewAsync("form")).Current,f.Form.VersionId,false,"off"));
        await f.Intake.Flows.SetActivationAsync("child",ActivationCommand((await f.Intake.Flows.ActivationPreviewAsync("child")).Current,f.Child.VersionId,false,"off"));
        var start=await Starter(h).RunAsync(h.Tenant);Assert.Equal("RUNNING",start.State);h.Db.ChangeTracker.Clear();
        var instances=await h.Db.Wf_FlowInstances.AsNoTracking().ToArrayAsync();Assert.Equal(3,instances.Length);
        var leaf=Assert.Single(instances,x=>x.FlowKey=="child");Assert.Equal(f.Child.VersionId,leaf.FlowDefVersionId);Assert.Equal(f.Form.VersionId,leaf.FormDefVersionId);
        Assert.All(instances,x=>Assert.Equal(FlowEngine.ApprovalRuntimeV2,x.Creator));
        Assert.Empty(await h.Db.Wf_FlowDefVersions.ToArrayAsync());Assert.Empty(await h.Db.Wf_ServiceJobs.ToArrayAsync());
        foreach(var reviewer in new[]{f.First,f.Second})
        {
            var actor=DecisionActor(h,reviewer);var command=await DecisionInput(actor);var result=await Vote(actor,command);
            Assert.Equal(result,await Decisions(actor).OriginalAsync(command.Input.CommandId));
            Assert.Equal(reviewer==f.Second,result.Result.DecisionId!=null);
        }
        Assert.All(await h.Db.Wf_FlowInstances.AsNoTracking().ToArrayAsync(),x=>Assert.Equal(FlowInstanceStatus.Approved,x.Status));
        var payload=await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync();
        var decision=ApprovalDecisionWireV2.Parse(payload).Value;
        Assert.Equal(start.InstanceId,decision.InstanceId);Assert.Equal(f.Intake.Version,decision.FlowDefVersionId);
        Assert.Equal(IntakeBody(f.Intake).SubmissionId,decision.SubmissionId);Assert.Equal("APPROVED",decision.Decision);
        Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task App_subflow_sequential_calls_use_distinct_slots_and_immediate_child_does_not_invent_a_vote()
    {
        const string sequential="""
            {"start":"s","nodes":[{"id":"s","type":"start"},{"id":"one","type":"subFlow","subFlowKey":"child"},{"id":"two","type":"subFlow","subFlowKey":"child"},{"id":"a","type":"approval","approverStrategy":"Role","approverRoleId":73},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"one"},{"from":"one","to":"two"},{"from":"two","to":"a"},{"from":"a","to":"e"}]}
            """;
        await using var h=await Seed();var f=await SubflowSetup(h,sequential,immediate:true);var start=await Starter(h).RunAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var children=await h.Db.Wf_FlowInstances.AsNoTracking().Where(x=>x.ParentInstanceId==start.InstanceId).ToArrayAsync();
        Assert.Equal(2,children.Length);Assert.Equal(2,children.Select(x=>x.ParentTokenId).Distinct().Count());
        Assert.All(children,x=>Assert.Equal(FlowInstanceStatus.Approved,x.Status));
        Assert.All(await h.Db.Wf_FlowTasks.ToArrayAsync(),x=>Assert.Equal(start.InstanceId,x.InstanceId));
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.votes WHERE tenant_id={h.Tenant}").SingleAsync());
        foreach(var reviewer in new[]{f.First,f.Second}){var actor=DecisionActor(h,reviewer);await Vote(actor,await DecisionInput(actor));}
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_decision_v2.decisions WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task App_subflow_start_fault_rolls_back_entire_graph_and_unknown_commit_reuses_original_root()
    {
        var hook=new StartWriteHook();var fault=new CommitFault();await using var h=await Seed(hook,fault);var f=await SubflowSetup(h,nested:true);
        hook.Before=command=>{if(command.CommandText.Contains("INSERT INTO cp6_approval_start_v2.starts"))throw new InvalidOperationException("subgraph before completion");return Task.CompletedTask;};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Starter(h).RunAsync(h.Tenant));
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_runtime_v2.children WHERE tenant_id={h.Tenant}").SingleAsync());
        hook.Before=null;fault.Armed=true;
        Assert.Equal("START_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Starter(h).RunAsync(h.Tenant))).Message);
        Assert.Equal("IDLE",(await Starter(h).RunAsync(h.Tenant)).State);
        Assert.Equal(3,await h.Db.Wf_FlowInstances.CountAsync());Assert.Equal(2,await h.Db.Wf_FlowTasks.CountAsync());
        Assert.Equal("RUNNING",(await f.Intake.Owner.CurrentAsync(IntakeBody(f.Intake).SubmissionId)).State);
    }
}
