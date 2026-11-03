using System.Text;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private PersonalFormOwnerV2 PersonalForm(Harness h,Func<CancellationToken,Task<Guid>>? actor=null,CP6.Core.Services.Pub.FormFileContentV2? files=null)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        catalog.RegisterScope(PersonalFormOwnerV2.Resource,"personal form",[1],1);
        catalog.RegisterFields(PersonalFormOwnerV2.Resource,PersonalFormOwnerV2.Fields.Select(f=>new FieldRegistry.Field(f,f)));catalog.Seal();
        return new(h.Db,new AuthoringHttpAccessor{HttpContext=h.Http},ScopeFence(h),actor??(_=>Task.FromResult(h.User)),Authoring(h),files){Catalog=catalog.Capture};
    }
    private async Task<(PersonalFormOwnerV2 Owner,FormAuthoringOwnerV2 Form,FlowAuthoringOwnerV2 Flow)> PreparePersonal(Harness h,string schema=FormAuthoringSchema)
    {
        var(flow,form)=await PrepareDependencies(h);
        var saved=await form.SaveAsync("personal",FormAuthoringCommand(schema:schema));
        var p=(await form.PublishAsync("personal",FormPublicationCommand(await FormPreview(form,"personal",saved)))).Result!.Publication!;
        await form.SetActivationAsync("personal",FormActivationCommand((await form.ActivationPreviewAsync("personal")).Current,p.VersionId));
        foreach(var(menu,actions)in new[]{("oa-form-catalog",new[]{"query","add","edit","del","submit"}),("oa-inbox",new[]{"query","approve"}),("pub-role-perm",new[]{"edit"})})
        {
            foreach(var action in actions)await EngineeringAction(h,menu,action);
            var row=await h.Db.Sys_Menus.SingleAsync(m=>m.MenuKey==menu);
            if(!await h.Db.Sys_RoleMenus.AnyAsync(m=>m.RoleId==72&&m.MenuId==row.MenuId))
                h.Db.Sys_RoleMenus.Add(new(){RoleId=72,MenuId=row.MenuId});
            await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        }
        var owner=PersonalForm(h);await owner.ConfigureAsync("personal",new(Guid.NewGuid(),"absent",true,true,[new(72,[])],"local-retention/1",30,true,"simulated business acceptance"));
        return(owner,form,flow);
    }
    private static byte[] PersonalCommand(object body,string id="create",string? token=null)=>JsonSerializer.SerializeToUtf8Bytes(new{commandId=id,expectedVersion=token,body},PersonalFormOwnerV2.Json);
    private static JsonElement PersonalData(string json)=>JsonSerializer.Deserialize<JsonElement>(json);
    private static PersonalFormDirectV2 PersonalDirect(PersonalFormContextV2 context,string data="{\"a\":\"saved\"}")=>new("DIRECT",context.FormVersionId,
        context.ActivationRevision,context.BindingRevision,context.FlowVersionId,context.FlowActivationRevision,PersonalData(data));
    private static PersonalFormDraftSubmitV2 PersonalSubmit(PersonalFormContextV2 context)=>new("DRAFT",context.FormVersionId,
        context.ActivationRevision,context.BindingRevision,context.FlowVersionId,context.FlowActivationRevision);
    private static T PersonalResult<T>(PersonalFormResultV2 value)=>value.Result!.Value.Deserialize<T>(PersonalFormOwnerV2.Json)!;

    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_unbound_submit_preserves_numbers_and_original_without_workflow_or_approval_claim()
    {
        const string schema="{\"fields\":[{\"name\":\"amount\",\"type\":\"number\"},{\"name\":\"sum\",\"type\":\"number\"}],\"rules\":[{\"then\":[{\"action\":\"compute\",\"target\":\"sum\",\"expr\":\"amount + 0.0001\"}]}]}";
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h,schema);var context=await owner.ContextAsync("personal");
        var raw=PersonalCommand(PersonalDirect(context,"{\"amount\":12345678901234567890.0001}"),"direct");
        var first=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal",raw));
        Assert.Equal("SUBMITTED_UNBOUND",first.SubmissionState);Assert.Null(first.FlowInstanceId);Assert.Null(first.FlowDefVersionId);
        var data=await h.Db.Wf_FormDatas.SingleAsync();Assert.Contains("12345678901234567890.0002",data.DataJson);
        Assert.Equal(context.FormVersionId,data.FormDefVersionId);Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
        Assert.Equal(first,PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal",raw)));
        Assert.Equal(first,PersonalResult<PersonalFormSubmissionV2>(await owner.OriginalAsync(PersonalFormOwnerV2.Direct,"personal","direct")));
        Assert.Equal(WfNotificationType.FormSubmitted,(await h.Db.Wf_Notifications.SingleAsync()).Type);
        Assert.Equal("COMMAND_BODY_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,"{\"amount\":2}"),"direct")))).Message);
        Assert.Equal(1,await h.Db.Wf_FormDatas.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_draft_partial_validation_CAS_tombstone_restore_and_original_are_owned()
    {
        const string schema="{\"fields\":[{\"name\":\"a\",\"type\":\"input\",\"required\":true},{\"name\":\"n\",\"type\":\"number\"}]}";
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h,schema);var context=await owner.ContextAsync("personal");
        var create=PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{}"),"  title  "));
        var first=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",create));
        Assert.Equal("title",first.Title);Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());Assert.Empty(await h.Db.Wf_Notifications.ToArrayAsync());
        Assert.Equal(first.DraftId,PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",create)).DraftId);
        var saved=PersonalResult<PersonalFormDraftV2>(await owner.SaveDraftAsync(first.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"a\":\"value\"}"),"title"),"save",first.RowVersion)));
        Assert.Equal("DRAFT_VERSION_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveDraftAsync(first.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{}"),null),"stale",first.RowVersion)))).Message);
        Assert.Equal("FORM_FIELD_TYPE_INVALID",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveDraftAsync(first.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"n\":\"bad\"}"),null),"invalid",saved.RowVersion)))).Message);
        var deleted=PersonalResult<PersonalFormDraftV2>(await owner.DeleteDraftAsync(first.DraftId,PersonalCommand(new PersonalFormReasonV2("cancel"),"delete",saved.RowVersion)));
        Assert.Equal(2,deleted.Status);Assert.Empty((await owner.DraftsAsync()).Items);
        var restored=PersonalResult<PersonalFormDraftV2>(await owner.RestoreDraftAsync(first.DraftId,PersonalCommand(new PersonalFormReasonV2("resume"),"restore",deleted.RowVersion)));
        Assert.Equal(0,restored.Status);Assert.Equal(saved.DataJson,restored.DataJson);Assert.NotEqual(deleted.RowVersion,restored.RowVersion);
        await using var other=await Seed();await PreparePersonal(other,schema);
        Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>PersonalForm(other).DraftAsync(first.DraftId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_bound_draft_submission_starts_exact_native_graph_and_records_real_human_task_decision()
    {
        await using var h=await Seed();var(owner,_,flow)=await PreparePersonal(h);
        var schema=JsonSerializer.Serialize(new{start="s",nodes=new object[]{new{id="s",type="start"},new{id="a",type="approval",approverStrategy="Specified",approverUserId=h.User},new{id="e",type="end"}},edges=new[]{new{from="s",to="a"},new{from="a",to="e"}}});
        var f=await flow.SaveAsync("personal-flow",DependentCommand("personal",schema:schema));
        var p=(await flow.PublishAsync("personal-flow",PublicationCommand(await Preview(flow,"personal-flow",f)))).Result!.Publication!;
        await flow.SetActivationAsync("personal-flow",ActivationCommand((await flow.ActivationPreviewAsync("personal-flow")).Current,p.VersionId));
        var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"a\":\"text\"}"),null))));
        var submitted=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDraftAsync(draft.DraftId,PersonalCommand(PersonalSubmit(context),"submit",draft.RowVersion)));
        var instance=await h.Db.Wf_FlowInstances.SingleAsync();var task=await h.Db.Wf_FlowTasks.SingleAsync();
        Assert.Equal(FlowEngine.PersonalFormRuntimeV2,instance.Creator);Assert.Equal(context.FormVersionId,instance.FormDefVersionId);
        Assert.Equal(p.VersionId,instance.FlowDefVersionId);Assert.Equal(submitted.FormDataId,instance.FormDataId);
        Assert.Equal(1,(await owner.DraftAsync(draft.DraftId)).Status);
        var unrelated=new Wf_FlowInstance{FlowKey="legacy-unrelated",StarterId=h.User};h.Db.Wf_FlowInstances.Add(unrelated);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var moved=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Wf_FlowTasks.Where(t=>t.Id==task.Id).ExecuteUpdateAsync(x=>x.SetProperty(t=>t.InstanceId,unrelated.Id)));
        Assert.Contains("FORM_RUNTIME_IDENTITY_IMMUTABLE",moved.MessageText);
        var movedHistory=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Wf_FlowHistories.Where(t=>t.InstanceId==instance.Id).ExecuteUpdateAsync(x=>x.SetProperty(t=>t.InstanceId,unrelated.Id)));
        Assert.Contains("FORM_RUNTIME_IDENTITY_IMMUTABLE",movedHistory.MessageText);
        var taskContext=await owner.TaskContextAsync(task.Id);var command=NativeVote(taskContext,"checked","approve");
        var closed=command with{CommandId=Guid.NewGuid()};
        Assert.Equal("CLOSED",(await owner.CloseAsync("SFS_TASK_DECISION",task.Id.ToString("D").ToUpperInvariant(),closed.CommandId.ToString("D"))).Outcome);
        Assert.Equal("CLOSED",(await NativeOriginal(owner,task.Id,closed.CommandId))!.State);
        Assert.Equal("COMMAND_CLOSED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>DecideNative(owner,task.Id,closed))).Message);
        var result=await DecideNative(owner,task.Id,command);
        Assert.Equal(FlowInstanceStatus.Approved,result.Task.InstanceStatus);Assert.Equal(FlowTaskStatus.Approved,result.Task.TaskStatus);
        Assert.Equal(result,await DecideNative(owner,task.Id,command));Assert.Equal(result,await NativeOriginal(owner,task.Id,command.CommandId));
        Assert.Equal(4,await h.Db.Wf_Notifications.CountAsync());
        var currentDraft=await owner.DraftAsync(draft.DraftId);
        Assert.Equal("DRAFT_ALREADY_SUBMITTED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDraftAsync(draft.DraftId,PersonalCommand(PersonalSubmit(context),"new",currentDraft.RowVersion)))).Message);
        var policy=(await owner.PolicyAsync("personal"))!;
        await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[],policy.RetentionPolicyRef,30,true,"revoke current form disclosure"));
        Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.OriginalAsync(PersonalFormOwnerV2.Submit,draft.DraftId.ToString("D"),"submit"))).Status);
    }
}
