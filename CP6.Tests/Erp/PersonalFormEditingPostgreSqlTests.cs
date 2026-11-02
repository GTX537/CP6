using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_close_races_original_writer_and_never_reports_no_effect_after_commit()
    {
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");
        var first=PersonalCommand(PersonalDirect(context),"lost-before-send");
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.Direct,"personal","lost-before-send")).Outcome);
        Assert.Equal("CLOSED",(await owner.CloseAsync(PersonalFormOwnerV2.Direct,"personal","lost-before-send")).Outcome);
        Assert.Equal("COMMAND_CLOSED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDirectAsync("personal",first))).Message);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=PersonalForm(h with{Db=db});
        var command=PersonalCommand(PersonalDirect(context),"race-close");
        async Task<string> Write(){try{return(await owner.SubmitDirectAsync("personal",command)).Outcome;}catch(ScopeAuthorizationExceptionV1 e){return e.Message;}}
        var write=Write();var closing=peer.CloseAsync(PersonalFormOwnerV2.Direct,"personal","race-close");await Task.WhenAll(write,closing);
        var result=await closing;Assert.Equal(result.Outcome=="CLOSED"?"COMMAND_CLOSED":"SUCCEEDED",await write);
        Assert.Equal(result.Outcome=="CLOSED"?0:1,await h.Db.Wf_FormDatas.CountAsync());
        var replay=await owner.CloseAsync(PersonalFormOwnerV2.Direct,"personal","race-close");
        Assert.Equal(result.Outcome,replay.Outcome);Assert.Equal(result.ResourceId,replay.ResourceId);
        if(result.Outcome=="SUCCEEDED")Assert.Equal(PersonalResult<PersonalFormSubmissionV2>(result),PersonalResult<PersonalFormSubmissionV2>(replay));
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_readonly_defaults_exact_preview_and_current_reference_qualification()
    {
        const string schema="""{"Fields":[{"Name":"amount","Type":"number","Default":12345678901234567890.0001},{"Name":"fixed","Type":"number","Default":9007199254740993},{"Name":"person","Type":"user"},{"Name":"dept","Type":"dept"},{"Name":"sum","Type":"number"}],"Rules":[{"Then":[{"Action":"compute","Target":"sum","Expr":"amount + 0.0001"}]}]}""";
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h,schema);var policy=(await owner.PolicyAsync("personal"))!;
        await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){["fixed"]="readonly"})],policy.RetentionPolicyRef,30,true,"restrict default"));
        var dept=new Sys_Dept{Id=Guid.NewGuid(),DeptCode="D",DeptName="Active"};dept.Path="/"+dept.Id+"/";h.Db.Sys_Depts.Add(dept);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var context=await owner.ContextAsync("personal");
        var data=PersonalData("{\"amount\":12345678901234567890.0001,\"fixed\":9007199254740993,\"person\":\""+h.User+"\",\"dept\":\""+dept.Id+"\"}");
        Assert.Equal(h.User,Assert.Single(await owner.ReferencesAsync("personal","person","")).Id);
        Assert.Equal(dept.Id,Assert.Single(await owner.ReferencesAsync("personal","dept","")).Id);
        Assert.Equal("FIELD_REFERENCE_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.ReferencesAsync("personal","fixed",""))).Message);
        var preview=await owner.PreviewAsync("personal",new(context.FormVersionId,data));Assert.True(preview.Valid);Assert.Contains("12345678901234567890.0002",preview.DataJson);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Empty(await h.Db.Wf_Notifications.ToArrayAsync());
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,data,null))));
        Assert.Contains("9007199254740993",draft.DataJson);
        await h.Db.Sys_Depts.Where(x=>x.Id==dept.Id).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.Enable,false));
        Assert.Empty(await owner.ReferencesAsync("personal","dept",""));
        Assert.Equal("FORM_REFERENCE_INVALID",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDraftAsync(draft.DraftId,PersonalCommand(PersonalSubmit(context),"blocked-reference",draft.RowVersion)))).Message);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
        var bad=PersonalData("{\"person\":\""+Guid.NewGuid()+"\"}");
        Assert.Equal("FORM_REFERENCE_INVALID",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,bad,null),"foreign")))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_two_connections_submit_delete_and_save_CAS_have_one_winner()
    {
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"a\":\"draft\"}"),null))));
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=PersonalForm(h with{Db=db});
        async Task<string> Attempt(Func<Task<PersonalFormResultV2>> action){try{return(await action()).Outcome;}catch(ScopeAuthorizationExceptionV1 e){return e.Message;}}
        var results=await Task.WhenAll(Attempt(()=>owner.SubmitDraftAsync(draft.DraftId,PersonalCommand(PersonalSubmit(context),"submit",draft.RowVersion))),
            Attempt(()=>peer.DeleteDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormReasonV2("cancel"),"delete",draft.RowVersion))));
        Assert.True(results.Count(x=>x=="SUCCEEDED")==1,string.Join(",",results));
        Assert.Single(results.Where(x=>x is "DRAFT_DELETED" or "DRAFT_ALREADY_SUBMITTED" or "DRAFT_VERSION_CONFLICT"));
        var current=await owner.DraftAsync(draft.DraftId);Assert.Equal(current.Status==1?1:0,await h.Db.Wf_FormDatas.CountAsync());
        var second=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{}"),null),"second")));
        var saves=await Task.WhenAll(Attempt(()=>owner.SaveDraftAsync(second.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"a\":\"one\"}"),null),"one",second.RowVersion))),
            Attempt(()=>peer.SaveDraftAsync(second.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"a\":\"two\"}"),null),"two",second.RowVersion))));
        Assert.Contains("SUCCEEDED",saves);Assert.Contains("DRAFT_VERSION_CONFLICT",saves);
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_rebase_expiry_visible_loss_confirmation_and_exact_rule_recompute_preserve_original_history()
    {
        const string first="""{"fields":[{"name":"amount","type":"number"},{"name":"old","type":"input"}]}""";
        const string second="""{"fields":[{"name":"amount","type":"number"},{"name":"sum","type":"number"},{"name":"new","type":"input","default":"new default"}],"rules":[{"then":[{"action":"compute","target":"sum","expr":"amount + 0.01"}]}]}""";
        await using var h=await Seed();var(owner,form,_)=await PreparePersonal(h,first);var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"amount\":9007199254740993.01,\"old\":\"retained history\"}"),null))));
        var head=await form.ReadHeadAsync("personal");
        var next=await form.CreateNextDraftAsync("personal",FormNextDraftCommand(head.Version,context.FormVersionId));
        var saved=await form.SaveAsync("personal",FormAuthoringCommand(schema:second,version:next.ResourceVersion,id:"v2-save"));
        var publication=(await form.PublishAsync("personal",FormPublicationCommand(await FormPreview(form,"personal",saved),id:"v2-publish"))).Result!.Publication!;
        var preview=await owner.RebasePreviewAsync(draft.DraftId,new(publication.VersionId,draft.RowVersion));Assert.True(preview.RequiresConfirmation);
        var body=new PersonalFormRebaseInputV2(preview.PreviewId,preview.Digest,publication.VersionId,false);
        Assert.Equal("REBASE_CONFIRMATION_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.RebaseDraftAsync(draft.DraftId,PersonalCommand(body,"rebase",draft.RowVersion)))).Message);
        var now=DateTimeOffset.UtcNow;owner.Clock=()=>now.AddMinutes(11);
        Assert.Equal("REBASE_PREVIEW_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.RebaseDraftAsync(draft.DraftId,PersonalCommand(body with{ConfirmRemovedValues=true},"rebase",draft.RowVersion)))).Message);
        var at=DateTimeOffset.UtcNow;var calls=0;
        var expiring=PersonalForm(h,_=>{if(++calls==2)at=preview.ExpiresAtUtc.AddSeconds(1);return Task.FromResult(h.User);});expiring.Clock=()=>at;
        Assert.Equal("REBASE_PREVIEW_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>expiring.RebaseDraftAsync(draft.DraftId,PersonalCommand(body with{ConfirmRemovedValues=true},"rebase",draft.RowVersion)))).Message);
        Assert.Equal(draft.RowVersion,(await owner.DraftAsync(draft.DraftId)).RowVersion);
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.Rebase,draft.DraftId.ToString("D"),"rebase")).Outcome);
        owner.Clock=()=>DateTimeOffset.UtcNow;
        var result=PersonalResult<PersonalFormDraftV2>(await owner.RebaseDraftAsync(draft.DraftId,PersonalCommand(body with{ConfirmRemovedValues=true},"rebase",draft.RowVersion)));
        Assert.Equal(publication.VersionId,result.FormVersionId);Assert.Contains("9007199254740993.02",result.DataJson);Assert.Contains("new default",result.DataJson);Assert.DoesNotContain("retained history",result.DataJson);
        Assert.Contains("retained history",(await owner.OriginalAsync(PersonalFormOwnerV2.Create,"personal","create")).Result!.Value.GetRawText());
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_restore_retention_expiring_at_final_decision_rolls_back_tombstone_and_receipt()
    {
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{}"),null))));
        var deleted=PersonalResult<PersonalFormDraftV2>(await owner.DeleteDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormReasonV2("delete"),"delete",draft.RowVersion)));
        var at=DateTimeOffset.UtcNow;var calls=0;var expiring=PersonalForm(h,_=>{if(++calls==2)at=at.AddDays(31);return Task.FromResult(h.User);});expiring.Clock=()=>at;
        var raw=PersonalCommand(new PersonalFormReasonV2("restore"),"restore",deleted.RowVersion);
        Assert.Equal("DRAFT_RESTORE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>expiring.RestoreDraftAsync(draft.DraftId,raw))).Message);
        var current=await owner.DraftAsync(draft.DraftId);Assert.Equal(2,current.Status);Assert.Equal(deleted.RowVersion,current.RowVersion);
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.Restore,draft.DraftId.ToString("D"),"restore")).Outcome);
        Assert.Equal(0,PersonalResult<PersonalFormDraftV2>(await owner.RestoreDraftAsync(draft.DraftId,raw)).Status);
    }
}
