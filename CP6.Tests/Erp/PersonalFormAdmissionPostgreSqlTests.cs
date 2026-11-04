using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using System.Text.Json;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task AdmissionHumanFlow(FlowAuthoringOwnerV2 flow,Guid user,string key)
    {
        var schema=JsonSerializer.Serialize(new{start="s",nodes=new object[]{new{id="s",type="start"},new{id="a",type="approval",approverStrategy="Specified",approverUserId=user},new{id="e",type="end"}},edges=new[]{new{from="s",to="a"},new{from="a",to="e"}}});
        var saved=await flow.SaveAsync(key,DependentCommand("personal",schema:schema));
        var publication=(await flow.PublishAsync(key,PublicationCommand(await Preview(flow,key,saved)))).Result!.Publication!;
        await flow.SetActivationAsync(key,ActivationCommand((await flow.ActivationPreviewAsync(key)).Current,publication.VersionId));
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalAdmission_first_task_failure_rolls_back_bound_submission_and_same_intent_recovers()
    {
        await using var h=await Seed();var(owner,_,flow)=await PreparePersonal(h);
        await AdmissionHumanFlow(flow,h.User,"admission-bound");
        var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"a\":\"atomic task\"}"),null))));
        var command=PersonalCommand(PersonalSubmit(context),"task-failure",draft.RowVersion);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Wf_FlowTask\" ADD CONSTRAINT admission_task_unavailable CHECK(false) NOT VALID");
        try
        {
            var error=await Assert.ThrowsAsync<DbUpdateException>(()=>owner.SubmitDraftAsync(draft.DraftId,command));
            Assert.Contains("admission_task_unavailable",error.InnerException!.Message);
        }
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Wf_FlowTask\" DROP CONSTRAINT admission_task_unavailable");}
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
        Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowHistories.ToArrayAsync());
        Assert.Empty(await h.Db.Wf_Notifications.ToArrayAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_personal_form_v2.submissions WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_personal_form_v2.outbox o JOIN cp6_personal_form_v2.commands c ON c.id=o.command_id WHERE c.tenant_id={h.Tenant}").SingleAsync());
        var unchanged=await owner.DraftAsync(draft.DraftId);Assert.Equal(draft.RowVersion,unchanged.RowVersion);Assert.Equal(0,unchanged.Status);
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.Submit,draft.DraftId.ToString("D"),"task-failure")).Outcome);
        var accepted=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDraftAsync(draft.DraftId,command));
        Assert.Equal(accepted,PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDraftAsync(draft.DraftId,command)));
        Assert.Equal(accepted.FlowInstanceId,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).InstanceId);
        Assert.Equal(accepted.FormDataId,(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(1,(await owner.DraftAsync(draft.DraftId)).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task PersonalAdmission_two_distinct_commands_on_one_draft_create_one_native_submission_and_one_receipt()
    {
        await using var h=await Seed();var(owner,_,flow)=await PreparePersonal(h);
        await AdmissionHumanFlow(flow,h.User,"admission-race");var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"a\":\"one submission\"}"),null))));
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=PersonalForm(h with{Db=db});
        async Task<string> Attempt(PersonalFormOwnerV2 writer,string key)
        {
            try{return(await writer.SubmitDraftAsync(draft.DraftId,PersonalCommand(PersonalSubmit(context),key,draft.RowVersion))).Outcome;}
            catch(ScopeAuthorizationExceptionV1 e){return e.Message;}
        }
        var results=await Task.WhenAll(Attempt(owner,"first-submit"),Attempt(peer,"second-submit"));
        Assert.Single(results,x=>x=="SUCCEEDED");Assert.Single(results,x=>x=="DRAFT_ALREADY_SUBMITTED");
        var winner=results[0]=="SUCCEEDED"?"first-submit":"second-submit";var loser=results[0]=="SUCCEEDED"?"second-submit":"first-submit";
        var accepted=PersonalResult<PersonalFormSubmissionV2>(await owner.OriginalAsync(PersonalFormOwnerV2.Submit,draft.DraftId.ToString("D"),winner));
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.Submit,draft.DraftId.ToString("D"),loser)).Outcome);
        Assert.Equal(accepted,PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDraftAsync(draft.DraftId,PersonalCommand(PersonalSubmit(context),winner,draft.RowVersion))));
        Assert.Equal(accepted.FormDataId,(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(accepted.FlowInstanceId,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Id);
        Assert.Single(await h.Db.Wf_FlowTasks.AsNoTracking().ToArrayAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_personal_form_v2.submissions WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task PersonalAdmission_each_expected_context_mismatch_rejects_both_modes_without_consuming_draft()
    {
        await using var h=await Seed();var(owner,_,flow)=await PreparePersonal(h);
        await ActiveDependencyFlow(flow,"admission-context","personal");var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"a\":\"unchanged\"}"),null))));
        var valid=PersonalSubmit(context);var wrong=Guid.NewGuid();
        var changes=new[]{valid with{ExpectedFormVersionId=wrong},valid with{ExpectedActivationRevision=wrong.ToString("D")},
            valid with{ExpectedBindingRevision=wrong.ToString("D")},valid with{ExpectedFlowVersionId=wrong},valid with{ExpectedFlowActivationRevision=wrong.ToString("D")}};
        for(var i=0;i<changes.Length;i++)
        {
            var bad=changes[i];var direct=new PersonalFormDirectV2("DIRECT",bad.ExpectedFormVersionId,bad.ExpectedActivationRevision,
                bad.ExpectedBindingRevision,bad.ExpectedFlowVersionId,bad.ExpectedFlowActivationRevision,PersonalData("{\"a\":\"refused\"}"));
            Assert.Equal("FORM_CONTEXT_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDirectAsync("personal",PersonalCommand(direct,"direct-context-"+i)))).Message);
            Assert.Equal("FORM_CONTEXT_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDraftAsync(draft.DraftId,PersonalCommand(bad,"draft-context-"+i,draft.RowVersion)))).Message);
        }
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
        Assert.Empty(await h.Db.Wf_FlowTasks.ToArrayAsync());Assert.Empty(await h.Db.Wf_Notifications.ToArrayAsync());
        var after=await owner.DraftAsync(draft.DraftId);Assert.Equal(draft.RowVersion,after.RowVersion);Assert.Equal(draft.DataJson,after.DataJson);Assert.Equal(0,after.Status);
        Assert.Equal("SUCCEEDED",(await owner.SubmitDraftAsync(draft.DraftId,PersonalCommand(valid,"valid-after-context-rejection",draft.RowVersion))).Outcome);
    }
}
