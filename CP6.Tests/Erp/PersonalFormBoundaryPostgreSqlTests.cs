using System.Text;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_context_change_closed_DTO_and_disabled_form_keep_draft_active()
    {
        await using var h=await Seed();var(owner,form,flow)=await PreparePersonal(h);var before=await owner.ContextAsync("personal");
        var created=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(before.FormVersionId,PersonalData("{\"a\":\"draft\"}"),null))));
        var missing=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(PersonalCommand(PersonalDirect(before))).Replace("\"expectedBindingRevision\":null,",""));
        Assert.Equal("COMMAND_SHAPE_INVALID",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDirectAsync("personal",missing))).Message);
        var extra=PersonalCommand(new{kind="DRAFT",expectedFormVersionId=before.FormVersionId,expectedActivationRevision=before.ActivationRevision,
            expectedBindingRevision=(string?)null,expectedFlowVersionId=(Guid?)null,expectedFlowActivationRevision=(string?)null,data=new{a="forged"}},"bad-draft",created.RowVersion);
        Assert.Equal("COMMAND_SHAPE_INVALID",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDraftAsync(created.DraftId,extra))).Message);
        var bound=await ActiveDependencyFlow(flow,"now-bound","personal");
        Assert.Equal("FORM_CONTEXT_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDraftAsync(created.DraftId,PersonalCommand(PersonalSubmit(before),"stale-context",created.RowVersion)))).Message);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Equal(0,(await owner.DraftAsync(created.DraftId)).Status);
        await form.SetActivationAsync("personal",FormActivationCommand((await form.ActivationPreviewAsync("personal")).Current,before.FormVersionId,false,"off"));
        var edited=PersonalResult<PersonalFormDraftV2>(await owner.SaveDraftAsync(created.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"a\":\"saved while disabled\"}"),null),"disabled-save",created.RowVersion)));
        Assert.Contains("saved while disabled",edited.DataJson);
        Assert.Equal("FORM_DISABLED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDraftAsync(created.DraftId,PersonalCommand(PersonalSubmit(before),"disabled-submit",edited.RowVersion)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_unknown_commit_survives_write_revoke_scope_disable_and_missing_writer_without_resubmission()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var(owner,_,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");
        var command=PersonalCommand(PersonalDirect(context),"lost");fault.Armed=true;
        Assert.Equal("COMMAND_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDirectAsync("personal",command))).Message);
        var accepted=(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).Id;
        await h.Db.Sys_RoleActions.Where(a=>a.ActionCode=="submit").ExecuteDeleteAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_scope_authorization_v1.fences SET enabled=false WHERE tenant_id={h.Tenant}");
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_personal_form_v2.outbox DISABLE TRIGGER personal_fact_guard");
        try
        {
            Assert.Equal(accepted,PersonalResult<PersonalFormSubmissionV2>(await owner.OriginalAsync(PersonalFormOwnerV2.Direct,"personal","lost")).FormDataId);
            Assert.Equal(accepted,PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal",command)).FormDataId);
            Assert.Equal("SCOPE_MAPPING_DISABLED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context),"new")))).Message);
        }
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_personal_form_v2.outbox ENABLE TRIGGER personal_fact_guard");}
        Assert.Equal(1,await h.Db.Wf_FormDatas.CountAsync());Assert.Equal(1,await h.Db.Wf_Notifications.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_notification_failure_rolls_back_native_data_draft_receipt_and_outbox_then_same_intent_can_commit()
    {
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"a\":\"draft\"}"),null))));
        var command=PersonalCommand(PersonalSubmit(context),"submit",draft.RowVersion);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Wf_Notification\" ADD CONSTRAINT personal_notification_unavailable CHECK(\"Type\"<>6) NOT VALID");
        try
        {
            var error=await Assert.ThrowsAsync<DbUpdateException>(()=>owner.SubmitDraftAsync(draft.DraftId,command));
            Assert.Contains("personal_notification_unavailable",error.InnerException!.Message);
        }
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Wf_Notification\" DROP CONSTRAINT personal_notification_unavailable");}
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Empty(await h.Db.Wf_Notifications.ToArrayAsync());
        Assert.Equal(draft.RowVersion,(await owner.DraftAsync(draft.DraftId)).RowVersion);
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.Submit,draft.DraftId.ToString("D"),"submit")).Outcome);
        Assert.Equal("SUCCEEDED",(await owner.SubmitDraftAsync(draft.DraftId,command)).Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_current_hidden_and_readonly_masks_preserve_data_and_filter_original_results()
    {
        const string schema="{\"Fields\":[{\"Name\":\"visible\",\"Type\":\"input\"},{\"Name\":\"secret\",\"Type\":\"input\"},{\"Name\":\"fixed\",\"Type\":\"number\"}],\"extension\":{\"K\":1,\"k\":2}}";
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h,schema);var context=await owner.ContextAsync("personal");
        var created=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"visible\":\"a\",\"secret\":\"do not reveal\",\"fixed\":12345678901234567890.1}"),null))));
        var policy=(await owner.PolicyAsync("personal"))!;
        await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){["secret"]="hidden",["fixed"]="readonly"})],policy.RetentionPolicyRef,30,true,"restrict current access"));
        var view=await owner.DraftAsync(created.DraftId);Assert.DoesNotContain("secret",view.SchemaJson);Assert.DoesNotContain("do not reveal",view.DataJson);
        var original=await owner.OriginalAsync(PersonalFormOwnerV2.Create,"personal","create");Assert.DoesNotContain("do not reveal",JsonSerializer.Serialize(original));
        var saved=await owner.SaveDraftAsync(created.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"visible\":\"b\"}"),null),"save",view.RowVersion));
        var stored=await h.Db.Database.SqlQuery<string>($"SELECT data_json AS \"Value\" FROM cp6_personal_form_v2.drafts WHERE id={created.DraftId}").SingleAsync();
        Assert.Contains("do not reveal",stored);Assert.Contains("12345678901234567890.1",stored);
        Assert.Equal("FIELD_WRITE_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveDraftAsync(created.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"fixed\":0}"),null),"deny",saved.ResourceVersion)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_final_scope_change_rolls_back_entire_submission_and_allows_clean_recovery()
    {
        await using var h=await Seed();var(original,_,_)=await PreparePersonal(h);var context=await original.ContextAsync("personal");var calls=0;
        var owner=PersonalForm(h,async _=>
        {
            if(++calls==2)await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_personal_form_v2.policy SET body=body WHERE tenant_id={h.Tenant}");
            return h.User;
        });
        var command=PersonalCommand(PersonalDirect(context),"aba");
        Assert.Equal("SCOPE_AUTHORITY_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDirectAsync("personal",command))).Message);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Empty(await h.Db.Wf_Notifications.ToArrayAsync());
        Assert.Equal("NOT_OBSERVED",(await original.OriginalAsync(PersonalFormOwnerV2.Direct,"personal","aba")).Outcome);
        Assert.Equal("SUCCEEDED",(await original.SubmitDirectAsync("personal",command)).Outcome);
    }
}
