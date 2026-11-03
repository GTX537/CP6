using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private NativeInboxOwnerV2 NativeInbox(Harness h,PersonalFormOwnerV2? form=null,ApprovalBusinessEvidenceV2? business=null)
        =>new(h.Db,ScopeFence(h),form??PersonalForm(h),business??new(ToolApproval(h)));
    private async Task<(PersonalFormOwnerV2 Owner,FlowAuthoringOwnerV2 Flow,Guid Instance,Guid Task)> NativeFormSetup(Harness h,bool parallel=false,string? title=null,Action<FlowSchema>? configure=null,Func<Task>? beforePublish=null,string formSchema=FormAuthoringSchema,string values="{\"a\":\"saved\"}",CP6.Core.Services.Pub.FormFileContentV2? files=null)
    {
        var(owner,_,flow)=await PreparePersonal(h,formSchema);
        if(files!=null)owner=PersonalForm(h,files:files);
        await EngineeringAction(h,"oa-inbox","read");
        if(beforePublish!=null)await beforePublish();
        var graph=new FlowSchema{Start="s",Nodes=[new(){Id="s",Type="start"},new(){Id="a",Type="approval",ApproverStrategy="Specified",ApproverUserId=h.User,CcUsers=[h.User]},new(){Id="e",Type="end"}],
            Edges=[new(){From="s",To="a"},new(){From="a",To="e"}]};
        if(parallel)
        {
            graph.Nodes.AddRange([new(){Id="split",Type="parallelSplit"},new(){Id="join",Type="parallelJoin"},new(){Id="b",Type="approval",ApproverStrategy="Specified",ApproverUserId=h.User,CcUsers=[h.User]}]);
            graph.Edges=[new(){From="s",To="split"},new(){From="split",To="a"},new(){From="split",To="b"},new(){From="a",To="join"},new(){From="b",To="join"},new(){From="join",To="e"}];
        }
        configure?.Invoke(graph);
        var schema=JsonSerializer.Serialize(graph,PersonalFormOwnerV2.Json);
        var saved=await flow.SaveAsync("native-inbox",DependentCommand("personal",schema:schema));
        var published=(await flow.PublishAsync("native-inbox",PublicationCommand(await Preview(flow,"native-inbox",saved)))).Result!.Publication!;
        await flow.SetActivationAsync("native-inbox",ActivationCommand((await flow.ActivationPreviewAsync("native-inbox")).Current,published.VersionId));
        var context=await owner.ContextAsync("personal");PersonalFormSubmissionV2 submitted;
        if(title==null)submitted=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,values),"start")));
        else
        {
            var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"a\":\"private field\"}"),title),"draft")));
            submitted=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDraftAsync(draft.DraftId,PersonalCommand(PersonalSubmit(context),"start",draft.RowVersion)));
        }
        return(owner,flow,submitted.FlowInstanceId!.Value,(await h.Db.Wf_FlowTasks.AsNoTracking().OrderBy(t=>t.Id).FirstAsync()).Id);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeInbox_read_is_explicit_first_fact_and_never_changes_decision_versions_or_votes()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);var inbox=NativeInbox(h,f.Owner);
        var before=await f.Owner.TaskContextAsync(f.Task);
        Assert.Equal(1,(await inbox.CountsAsync()).Pending);Assert.Equal(f.Task,Assert.Single(Assert.Single((await inbox.ListAsync("pending")).Items).TaskIds));
        var detail=await inbox.DetailAsync(f.Instance);Assert.Null(Assert.Single(detail.Tasks).Read);Assert.Empty(detail.Votes);
        Assert.Equal(before.FormVersionId,detail.Form!.FormVersionId);Assert.Equal("ROOT_SUBMISSION",detail.Form.DataSource);
        var command=PersonalCommand(new{},"first");var read=await inbox.MarkReadAsync("TASK",f.Task,command);
        Assert.Equal(h.User,read.Result!.ActualUserId);Assert.Equal(h.User,read.Result.PrincipalId);
        Assert.Equal(read,await inbox.MarkReadAsync("TASK",f.Task,command));
        Assert.Equal(read.Result,(await inbox.MarkReadAsync("TASK",f.Task,PersonalCommand(new{},"again"))).Result);
        Assert.Equal(read,await inbox.ReadOriginalAsync("TASK",f.Task,"first"));
        var after=await f.Owner.TaskContextAsync(f.Task);Assert.Equal(before.Task,after.Task);Assert.Equal(0,(await inbox.CountsAsync()).UnreadTasks);
        Assert.False((await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).IsRead);
        var decision=NativeVote(before,"real human vote","approve");
        await DecideNative(f.Owner,f.Task,decision);
        Assert.Equal(0,(await inbox.CountsAsync()).Pending);Assert.Equal(1,(await inbox.ListAsync("done")).TotalItems);
        var vote=Assert.Single((await inbox.DetailAsync(f.Instance)).Votes);Assert.Equal(h.User,vote.ActualUserId);Assert.Equal("HUMAN",vote.EvidenceKind);
        Assert.Equal(read,await inbox.ReadOriginalAsync("TASK",f.Task,"first"));
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeInbox_current_permission_filters_totals_search_details_and_original_before_disclosure()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);var inbox=NativeInbox(h,f.Owner);
        await inbox.MarkReadAsync("TASK",f.Task,PersonalCommand(new{},"read"));
        Assert.Equal(1,(await inbox.ListAsync("pending",search:"native-inbox")).TotalItems);
        var policy=(await f.Owner.PolicyAsync("personal"))!;
        await f.Owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[],policy.RetentionPolicyRef,30,true,"current access revoked"));
        Assert.Equal(0,(await inbox.CountsAsync()).Pending);Assert.Empty((await inbox.ListAsync("pending",search:"native-inbox")).Items);
        Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>inbox.DetailAsync(f.Instance))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>inbox.ReadOriginalAsync("TASK",f.Task,"read"))).Status);
        Assert.Equal("GRANT_PRINCIPAL_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>inbox.ListAsync("pending",behalf:Guid.NewGuid()))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeInbox_close_missing_guard_and_unknown_commit_preserve_original_read_identity()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var f=await NativeFormSetup(h);var inbox=NativeInbox(h,f.Owner);
        Assert.Equal("CLOSED",(await inbox.CloseReadAsync("TASK",f.Task,"closed")).State);
        Assert.Equal("COMMAND_CLOSED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>inbox.MarkReadAsync("TASK",f.Task,PersonalCommand(new{},"closed")))).Message);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_read_v2.facts DISABLE TRIGGER read_fact_writer");
        try{Assert.Equal("INBOX_READ_WRITER_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>inbox.MarkReadAsync("TASK",f.Task,PersonalCommand(new{},"lost")))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_workflow_read_v2.facts ENABLE TRIGGER read_fact_writer");}
        Assert.Equal("NOT_OBSERVED",(await inbox.ReadOriginalAsync("TASK",f.Task,"lost")).State);
        fault.Armed=true;
        Assert.Equal("COMMAND_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>inbox.MarkReadAsync("TASK",f.Task,PersonalCommand(new{},"lost")))).Message);
        var original=await inbox.ReadOriginalAsync("TASK",f.Task,"lost");Assert.Equal("READ",original.State);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_scope_authorization_v1.fences SET enabled=false WHERE tenant_id={h.Tenant}");
        Assert.Equal(original,await inbox.MarkReadAsync("TASK",f.Task,PersonalCommand(new{},"lost")));
        Assert.Equal("SCOPE_MAPPING_DISABLED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>inbox.MarkReadAsync("TASK",f.Task,PersonalCommand(new{},"new")))).Message);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_workflow_read_v2.facts WHERE tenant_id={h.Tenant}"));
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeInbox_merged_expanded_CC_and_current_hidden_search_share_the_exact_authorized_set()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h,true,"private-title");var inbox=NativeInbox(h,f.Owner);
        var expanded=await inbox.ListAsync("pending");var merged=await inbox.ListAsync("pending",rowMode:"merged");
        Assert.Equal(2,expanded.TotalItems);Assert.Equal(1,expanded.TotalGroups);Assert.Equal(2,Assert.Single(merged.Items).TaskIds.Length);
        Assert.Equal(expanded.Items.SelectMany(x=>x.TaskIds).Order(),merged.Items.SelectMany(x=>x.TaskIds).Order());
        var first=await inbox.ListAsync("pending",pageSize:1);var second=await inbox.ListAsync("pending",page:2,pageSize:1);
        Assert.NotEqual(Assert.Single(first.Items).TaskIds[0],Assert.Single(second.Items).TaskIds[0]);
        var cc=await inbox.ListAsync("cc");Assert.Equal(2,cc.TotalItems);
        var id=cc.Items[0].CcIds[0];await inbox.MarkReadAsync("CC",id,PersonalCommand(new{},"cc"));
        Assert.Equal(1,(await inbox.CountsAsync()).UnreadCc);Assert.All((await inbox.DetailAsync(f.Instance)).Tasks,t=>Assert.Null(t.Read));
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey=PersonalFormOwnerV2.Resource,FieldName="Title",Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var policy=(await f.Owner.PolicyAsync("personal"))!;
        await f.Owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){["a"]="hidden"})],policy.RetentionPolicyRef,30,true,"hide fields now"));
        Assert.Equal(0,(await inbox.ListAsync("pending",search:"private-title")).TotalItems);Assert.Equal(2,(await inbox.ListAsync("pending")).TotalItems);
        var current=await inbox.DetailAsync(f.Instance);Assert.Null(current.Form!.Title);Assert.DoesNotContain("private field",current.Form.DataJson);Assert.DoesNotContain("\"a\"",current.Form.SchemaJson);
        await using var other=await Seed();await PreparePersonal(other);var stranger=NativeInbox(other);
        Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>stranger.DetailAsync(f.Instance))).Status);
        Assert.Empty((await stranger.ListAsync("pending")).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeInbox_actual_definition_impact_and_readonly_pins_survive_disabled_new_entries()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);var forms=FormAuthoring(h);var inbox=NativeInbox(h,f.Owner);
        var context=await f.Owner.ContextAsync("personal");
        await f.Owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{}"),null),"active-draft"));
        var flow=await f.Flow.ActivationPreviewAsync("native-inbox");Assert.Equal(1,flow.InflightCount);Assert.Equal(1,flow.Impact!.RootInstances);Assert.Equal(0,flow.Impact.ChildInstances);Assert.Equal(1,flow.Impact.PersonalEntryBindings);
        var form=await forms.ActivationPreviewAsync("personal");Assert.Equal(1,form.InflightCount);Assert.Equal(1,form.Impact!.ActiveDrafts);Assert.Equal(1,form.Impact.PublishedParentVersions);
        await f.Flow.SetActivationAsync("native-inbox",ActivationCommand(flow.Current,flow.Current.PublishedVersionId!.Value,false,"stop"));
        await forms.SetActivationAsync("personal",FormActivationCommand(form.Current,context.FormVersionId,false,"stop"));
        Assert.Equal(1,(await f.Flow.ActivationPreviewAsync("native-inbox")).InflightCount);
        var detail=await inbox.DetailAsync(f.Instance);Assert.Equal(context.FlowVersionId,Assert.Single(detail.Graph).FlowVersionId);Assert.Equal(context.FormVersionId,detail.Form!.FormVersionId);
        await h.Db.Sys_RoleActions.Where(a=>a.ActionCode=="approve").ExecuteDeleteAsync();
        Assert.False(Assert.Single((await inbox.DetailAsync(f.Instance)).Tasks).CanRequestDecisionContext);
        Assert.Equal(1,(await inbox.ListAsync("pending")).TotalItems);
        var next=await forms.CreateNextDraftAsync("personal",FormNextDraftCommand((await forms.ReadHeadAsync("personal")).Version,context.FormVersionId));
        var preview=await forms.PreviewPublicationAsync("personal",new(next.Result!.Draft!.DraftId,next.Result.Draft.Version));
        Assert.Equal(1,preview.AffectedDraftCount);Assert.Equal(1,preview.Impact!.RunningInstances);
    }
    [CurrentPermissionPostgresFact]
    public async Task NativeInbox_APP_child_uses_current_business_owner_and_preserves_root_subject_and_child_pin()
    {
        await using var h=await Seed();await EngineeringAction(h,"oa-inbox","read");var f=await SubflowSetup(h,nested:true);await Starter(h).RunAsync(h.Tenant);h.Db.ChangeTracker.Clear();
        var actor=DecisionActor(h,f.First);var inbox=NativeInbox(actor);var item=Assert.Single((await inbox.ListAsync("pending")).Items);
        var view=await inbox.DetailAsync(item.InstanceId);Assert.Equal("APP",view.ContextKind);Assert.Equal(3,view.Graph.Length);Assert.Null(view.Form);
        Assert.Equal(f.Child.VersionId,view.Graph.Single(g=>g.InstanceId==item.InstanceId).FlowVersionId);
        Assert.Equal(IntakeBody(f.Intake).Subject.SubjectId,view.Business!.SubjectId);Assert.Equal("UNPROVEN",view.Business.ApplicationState);
        Assert.Equal(item.InstanceId,Assert.Single((await inbox.ListAsync("running")).Items).InstanceId);
        var impact=await f.Intake.Flows.ActivationPreviewAsync("child");Assert.Equal(1,impact.InflightCount);Assert.Equal(1,impact.Impact!.ChildInstances);
        Assert.Equal(2,impact.Impact.PublishedParentVersions);Assert.Equal(1,impact.Impact.RelatedAppBindings);
        var task=Assert.Single(view.Tasks.Where(t=>t.PrincipalId==actor.User));var read=await inbox.MarkReadAsync("TASK",task.TaskId,PersonalCommand(new{},"app-read"));
        Assert.Equal(actor.User,read.Result!.ActualUserId);Assert.Equal(0,task.Status);Assert.Empty(view.Votes);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=73,ResourceKey=CP6.Core.Services.Integration.OperationsOwnerV1.ToolInstance,FieldName="Remarks",Access=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Empty((await inbox.ListAsync("pending")).Items);Assert.Equal(0,(await inbox.CountsAsync()).Pending);
        Assert.Equal(403,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>inbox.DetailAsync(item.InstanceId))).Status);
    }
}
