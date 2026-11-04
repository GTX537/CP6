using System.Net;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<(PersonalFormOwnerV2 Owner,PersonalFormDraftV2 Before,PersonalFormDraftV2 After,Guid History)> RecoverySnapshot(Harness h)
    {
        const string source="""{"fields":[{"name":"amount","type":"number"},{"name":"old","type":"input"}]}""";
        const string target="""{"fields":[{"name":"amount","type":"number"},{"name":"required","type":"input","required":true}]}""";
        var(owner,form,_)=await PreparePersonal(h,source);var context=await owner.ContextAsync("personal");
        var before=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"amount\":9007199254740993.0001,\"old\":\"precise original\"}"),"Original title"),"prepare-draft")));
        var next=await form.CreateNextDraftAsync("personal",FormNextDraftCommand((await form.ReadHeadAsync("personal")).Version,context.FormVersionId));
        var saved=await form.SaveAsync("personal",FormAuthoringCommand(schema:target,version:next.ResourceVersion,id:"next-save"));
        var publication=(await form.PublishAsync("personal",FormPublicationCommand(await FormPreview(form,"personal",saved),id:"next-publish"))).Result!.Publication!;
        var preview=await owner.RebasePreviewAsync(before.DraftId,new(publication.VersionId,before.RowVersion));
        var after=PersonalResult<PersonalFormDraftV2>(await owner.RebaseDraftAsync(before.DraftId,PersonalCommand(new PersonalFormRebaseInputV2(preview.PreviewId,preview.Digest,publication.VersionId,true),"source-rebase",before.RowVersion)));
        var history=await h.Db.Database.SqlQuery<Guid>($"SELECT h.id AS \"Value\" FROM cp6_personal_form_v2.history h JOIN cp6_personal_form_v2.commands c ON c.id=h.command_id WHERE h.draft_id={before.DraftId} AND c.command_id='source-rebase'").SingleAsync();
        return(owner,before,after,history);
    }
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_snapshot_recovery_cancel_lost_response_and_new_draft_focus_preserve_source()
    {
        await using var h=await Seed();var f=await RecoverySnapshot(h);
        foreach(var(menu,path)in new[]{("oa-form-catalog","/oa/form-catalog"),("oa-inbox","/oa/inbox")})
            await h.Db.Sys_Menus.Where(x=>x.MenuKey==menu).ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,path));
        using var result=await BrowserScenario(h,h.User,"oa-snapshot-recovery-browser.mjs","snapshot-recovery-browser",new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")},new(){["CP6_SNAPSHOT_DRAFT"]=f.After.DraftId.ToString("D")});
        var current=await f.Owner.DraftAsync(f.After.DraftId);Assert.Equal(f.After.RowVersion,current.RowVersion);Assert.Equal(f.After.DataJson,current.DataJson);
        var recovered=await f.Owner.DraftAsync(result.RootElement.GetProperty("recoveredDraftId").GetGuid());Assert.Equal(f.Before.DataJson,recovered.DataJson);Assert.Equal(f.Before.FormVersionId,recovered.FormVersionId);
        Assert.Equal(2,(await f.Owner.DraftsAsync()).Total);Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalSnapshotRecovery_HTTP_copies_exact_before_version_into_new_draft_without_rewriting_source()
    {
        await using var h=await Seed();var f=await RecoverySnapshot(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});var cookies=await Login(host,h);
        var root="/api/oa/v2/drafts/"+f.Before.DraftId;
        using var list=await host.Send(BoundRequest(cookies,root+"/rebase-history"));var sources=await PersonalHttpResult<JsonElement>(list);
        Assert.Equal(f.History,sources.GetProperty("items")[0].GetProperty("historyId").GetGuid());
        using var read=await host.Send(BoundRequest(cookies,root+"/rebase-history/"+f.History));var preview=await PersonalHttpResult<JsonElement>(read);
        Assert.Equal(f.Before.DataJson,preview.GetProperty("snapshot").GetProperty("dataJson").GetString());
        var sourceState=await h.Db.Database.SqlQuery<string>($"SELECT to_jsonb(d)::text AS \"Value\" FROM cp6_personal_form_v2.drafts d WHERE id={f.After.DraftId}").SingleAsync();
        var raw=PersonalCommand(new{historyId=f.History,reason="Recover exact original into another draft"},"recover-one",f.After.RowVersion);
        using var sent=await host.Send(PersonalHttp(cookies,root+"/recover-before-rebase",raw));var recovered=PersonalResult<PersonalFormDraftV2>(await PersonalHttpResult<PersonalFormResultV2>(sent));
        Assert.NotEqual(f.Before.DraftId,recovered.DraftId);Assert.Equal(f.Before.FormVersionId,recovered.FormVersionId);Assert.Equal(f.Before.DataJson,recovered.DataJson);Assert.Equal(f.Before.Title,recovered.Title);
        Assert.Equal(sourceState,await h.Db.Database.SqlQuery<string>($"SELECT to_jsonb(d)::text AS \"Value\" FROM cp6_personal_form_v2.drafts d WHERE id={f.After.DraftId}").SingleAsync());
        using var replay=await host.Send(PersonalHttp(cookies,root+"/recover-before-rebase",raw));Assert.Equal(recovered.DraftId,PersonalResult<PersonalFormDraftV2>(await PersonalHttpResult<PersonalFormResultV2>(replay)).DraftId);
        using var original=await host.Send(BoundRequest(cookies,"/api/oa/v2/personal-forms/commands/FORM_DRAFT_RECOVER_SNAPSHOT/"+f.Before.DraftId+"/recover-one"));
        Assert.Equal(recovered.DraftId,PersonalResult<PersonalFormDraftV2>(await PersonalHttpResult<PersonalFormResultV2>(original)).DraftId);
        Assert.Equal(2,(await f.Owner.DraftsAsync()).Total);Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
        var relation=await h.Db.Database.SqlQuery<Guid>($"SELECT source_history_id AS \"Value\" FROM cp6_personal_form_v2.draft_recoveries WHERE draft_id={recovered.DraftId}").SingleAsync();Assert.Equal(f.History,relation);
        using var conflict=await host.Send(PersonalHttp(cookies,root+"/recover-before-rebase",PersonalCommand(new{historyId=f.History,reason="different"},"recover-one",f.After.RowVersion)));Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalSnapshotRecovery_current_permissions_projection_CAS_and_closed_input_preserve_original_values()
    {
        await using var h=await Seed();var f=await RecoverySnapshot(h);var owner=f.Owner;
        byte[] Input(string id,string? token=null)=>PersonalCommand(new PersonalFormSnapshotRecoveryV2(f.History,"recover original"),id,token??f.After.RowVersion);
        Assert.Equal("COMMAND_SHAPE_INVALID",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.RecoverSnapshotAsync(f.After.DraftId,PersonalCommand(new{historyId=f.History,reason="forged",data=new{old="overwrite"}},"forged",f.After.RowVersion)))).Message);
        Assert.Equal("DRAFT_VERSION_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.RecoverSnapshotAsync(f.After.DraftId,Input("stale",f.Before.RowVersion)))).Message);
        Assert.Equal("FORM_RECOVERY_SOURCE_NOT_FOUND",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.RecoverSnapshotAsync(f.After.DraftId,PersonalCommand(new PersonalFormSnapshotRecoveryV2(Guid.NewGuid(),"missing"),"missing",f.After.RowVersion)))).Message);
        var policy=(await owner.PolicyAsync("personal"))!;
        await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){["old"]="hidden",["amount"]="readonly"})],policy.RetentionPolicyRef,30,true,"current projection"));
        var preview=await owner.RecoveryPreviewAsync(f.After.DraftId,f.History);Assert.DoesNotContain("precise original",preview.Snapshot.DataJson);Assert.Contains("9007199254740993.0001",preview.Snapshot.DataJson);
        await RemoveFormActions(h,"add");Assert.False((await owner.RebaseHistoryAsync(f.After.DraftId)).Items.Single().CanRecover);
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.RecoverSnapshotAsync(f.After.DraftId,Input("no-add")))).Message);
        await EngineeringAction(h,"oa-form-catalog","add");
        var restored=PersonalResult<PersonalFormDraftV2>(await owner.RecoverSnapshotAsync(f.After.DraftId,Input("masked")));
        Assert.DoesNotContain("precise original",restored.DataJson);Assert.Equal(f.Before.DataJson,await h.Db.Database.SqlQuery<string>($"SELECT data_json AS \"Value\" FROM cp6_personal_form_v2.drafts WHERE id={restored.DraftId}").SingleAsync());
        Assert.DoesNotContain("precise original",JsonSerializer.Serialize(await owner.OriginalAsync(PersonalFormOwnerV2.RecoverSnapshot,f.After.DraftId.ToString("D"),"masked")));
        await using var foreign=await Seed();var(other,_,_)=await PreparePersonal(foreign);
        Assert.Equal("DRAFT_NOT_FOUND",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>other.RecoveryPreviewAsync(f.After.DraftId,f.History))).Message);
        Assert.Equal(2,(await owner.DraftsAsync()).Total);
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalSnapshotRecovery_retention_and_final_expiry_or_receipt_failure_roll_back_all_new_facts()
    {
        await using var h=await Seed();var f=await RecoverySnapshot(h);var owner=f.Owner;var raw=PersonalCommand(new PersonalFormSnapshotRecoveryV2(f.History,"recover original"),"atomic-recovery",f.After.RowVersion);
        var at=DateTimeOffset.UtcNow;var calls=0;var expiring=PersonalForm(h,_=>{if(++calls==2)at=at.AddDays(31);return Task.FromResult(h.User);});expiring.Clock=()=>at;
        Assert.Equal("FORM_RECOVERY_RETENTION_EXPIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>expiring.RecoverSnapshotAsync(f.After.DraftId,raw))).Message);
        Assert.False((await expiring.RebaseHistoryAsync(f.After.DraftId)).Items.Single().CanRecover);
        Assert.Equal(1,(await owner.DraftsAsync()).Total);
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.RecoverSnapshot,f.After.DraftId.ToString("D"),"atomic-recovery")).Outcome);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_personal_form_v2.commands ADD CONSTRAINT recovery_receipt_unavailable CHECK(operation<>'FORM_DRAFT_RECOVER_SNAPSHOT' OR NOT completed) NOT VALID");
        try { var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>owner.RecoverSnapshotAsync(f.After.DraftId,raw));Assert.Contains("recovery_receipt_unavailable",error.MessageText); }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_personal_form_v2.commands DROP CONSTRAINT recovery_receipt_unavailable"); }
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.draft_recoveries WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(1,(await owner.DraftsAsync()).Total);Assert.Equal(f.After.RowVersion,(await owner.DraftAsync(f.After.DraftId)).RowVersion);
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.RecoverSnapshot,f.After.DraftId.ToString("D"),"atomic-recovery")).Outcome);
        Assert.Equal("SUCCEEDED",(await owner.RecoverSnapshotAsync(f.After.DraftId,raw)).Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalSnapshotRecovery_lost_commit_recovers_after_write_revoke_and_disabled_lineage_guard()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var f=await RecoverySnapshot(h);var raw=PersonalCommand(new PersonalFormSnapshotRecoveryV2(f.History,"recover original"),"lost-recovery",f.After.RowVersion);
        fault.Armed=true;Assert.Equal("COMMAND_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.RecoverSnapshotAsync(f.After.DraftId,raw))).Message);
        await RemoveFormActions(h,"add","edit");
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_personal_form_v2.draft_recoveries DISABLE TRIGGER personal_recovery_guard");
        try
        {
            var original=await f.Owner.OriginalAsync(PersonalFormOwnerV2.RecoverSnapshot,f.After.DraftId.ToString("D"),"lost-recovery");Assert.Equal("SUCCEEDED",original.Outcome);
            Assert.Equal(original.ResourceId,(await f.Owner.RecoverSnapshotAsync(f.After.DraftId,raw)).ResourceId);
            await EngineeringAction(h,"oa-form-catalog","add");await EngineeringAction(h,"oa-form-catalog","edit");
            Assert.Equal("FORM_RECOVERY_WRITER_COVERAGE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.RecoverSnapshotAsync(f.After.DraftId,PersonalCommand(new PersonalFormSnapshotRecoveryV2(f.History,"new intent"),"new-recovery",f.After.RowVersion)))).Message);
        }
        finally { await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_personal_form_v2.draft_recoveries ENABLE TRIGGER personal_recovery_guard"); }
        Assert.Equal(2,(await f.Owner.DraftsAsync()).Total);
        var failure=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_personal_form_v2.draft_recoveries WHERE tenant_id={h.Tenant}"));Assert.Contains("immutable",failure.MessageText,StringComparison.OrdinalIgnoreCase);
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalSnapshotRecovery_missing_ancestry_at_commit_rejects_orphan_copy_and_same_intent_recovers()
    {
        await using var h=await Seed();var f=await RecoverySnapshot(h);var raw=PersonalCommand(new PersonalFormSnapshotRecoveryV2(f.History,"recover exact ancestry"),"missing-ancestry",f.After.RowVersion);
        await h.Db.Database.ExecuteSqlRawAsync("CREATE FUNCTION public.skip_recovery_ancestry() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NULL; END $$; CREATE TRIGGER zz_skip_recovery_ancestry BEFORE INSERT ON cp6_personal_form_v2.draft_recoveries FOR EACH ROW EXECUTE FUNCTION public.skip_recovery_ancestry()");
        try { var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>f.Owner.RecoverSnapshotAsync(f.After.DraftId,raw));Assert.Contains("FORM_SNAPSHOT_RECOVERY_ANCESTRY_REQUIRED",error.MessageText); }
        finally { await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER zz_skip_recovery_ancestry ON cp6_personal_form_v2.draft_recoveries; DROP FUNCTION public.skip_recovery_ancestry()"); }
        Assert.Equal(1,(await f.Owner.DraftsAsync()).Total);Assert.Equal(f.After.RowVersion,(await f.Owner.DraftAsync(f.After.DraftId)).RowVersion);
        Assert.Equal("NOT_OBSERVED",(await f.Owner.OriginalAsync(PersonalFormOwnerV2.RecoverSnapshot,f.After.DraftId.ToString("D"),"missing-ancestry")).Outcome);
        Assert.Equal("SUCCEEDED",(await f.Owner.RecoverSnapshotAsync(f.After.DraftId,raw)).Outcome);
    }
}
