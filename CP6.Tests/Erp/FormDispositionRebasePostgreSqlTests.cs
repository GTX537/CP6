using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<FormDispositionRefV1> RetainRebase(Harness h,DispositionFixture f)
    {
        var owner=Dispositions(DecisionActor(h,f.Processor));var request=f.Requested.Request!;
        var approved=await owner.DecideAsync(request.RequestId,"approve",new(Guid.NewGuid(),request.Version,"qualified exact rebase review"));
        return (await owner.DecideAsync(request.RequestId,"retain",new(Guid.NewGuid(),approved.Request!.Version,"retain original and authorize exact losses"))).Request!.DispositionRef!;
    }
    private static byte[] ConsumeRebase(PersonalFormRebasePreviewV2 p,string command="consume")=>PersonalCommand(
        new PersonalFormRebaseInputV2(p.PreviewId,p.Digest,p.TargetVersionId,false,p.DispositionRef),command,p.ExpectedVersion);
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_form_disposition_rebase_hidden_request_qualified_processing_and_original_recovery()
    {
        await using var h=await Seed();var f=await PrepareDisposition(h);var password=new BCryptPasswordHasher(4).Hash("Price1!Password");
        await h.Db.Sys_Users.Where(x=>x.Id==f.Processor).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Password,password));
        await h.Db.Sys_Menus.Where(x=>x.MenuKey=="oa-inbox").ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,"/oa/inbox"));
        using var result=await BrowserScenario(h,f.Processor,"oa-form-disposition-rebase-browser.mjs","form-disposition-rebase-browser",
            new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")},new(){["CP6_DISPOSITION_REQUEST"]=f.Requested.Request!.RequestId.ToString("D"),["CP6_DISPOSITION_DRAFT"]=f.Draft.DraftId.ToString("D")});
        var current=(await Dispositions(h).ReadAsync(f.Requested.Request.RequestId)).Request;
        Assert.Equal("CONSUMED",current.State);Assert.Equal(h.User,current.Consumption!.ActualActorId);Assert.Equal(f.Processor,current.Consumption.ProcessorId);
        Assert.Equal(f.Target,(await f.Forms.DraftAsync(f.Draft.DraftId)).FormVersionId);Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task FormDispositionRebase_mixed_visible_loss_still_needs_confirmation_and_all_rows_survive()
    {
        await using var h=await Seed();var f=await PrepareDisposition(h,"""{"fields":[{"name":"visible","type":"number","default":2},{"name":"rows","type":"table","maxRows":1,"columns":[{"name":"keep","type":"input"}]}]}""");
        var reference=await RetainRebase(h,f);var id=f.Draft.DraftId;var p=await f.Forms.RebasePreviewAsync(id,new(f.Target,f.Draft.RowVersion,reference));
        Assert.True(p.RequiresConfirmation);Assert.Equal(new[]{"visible"},p.TypeChangedPaths);Assert.DoesNotContain("rows",JsonSerializer.Serialize(p,PersonalFormOwnerV2.Json));
        Assert.Equal("REBASE_CONFIRMATION_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Forms.RebaseDraftAsync(id,ConsumeRebase(p)))).Message);
        var blind=new PersonalFormRebaseInputV2(p.PreviewId,p.Digest,p.TargetVersionId,true);
        Assert.Equal("REBASE_HIDDEN_LOSS_BLOCKED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Forms.RebaseDraftAsync(id,PersonalCommand(blind,"blind",p.ExpectedVersion)))).Message);
        var result=await f.Forms.RebaseDraftAsync(id,PersonalCommand(blind with{DispositionRef=reference},"approved-visible",p.ExpectedVersion));Assert.Equal("SUCCEEDED",result.Outcome);
        var data=JsonDocument.Parse(await h.Db.Database.SqlQuery<string>($"SELECT data_json AS \"Value\" FROM cp6_personal_form_v2.drafts WHERE id={id}").SingleAsync());
        Assert.Equal(2,data.RootElement.GetProperty("rows").GetArrayLength());Assert.Equal(2,data.RootElement.GetProperty("visible").GetInt32());
    }
    [CurrentPermissionPostgresFact]
    public async Task FormDispositionRebase_source_policy_expiry_and_later_publication_invalidate_old_receipt()
    {
        await using(var h=await Seed())
        {
            var f=await PrepareDisposition(h);var reference=await RetainRebase(h,f);var id=f.Draft.DraftId;var p=await f.Forms.RebasePreviewAsync(id,new(f.Target,f.Draft.RowVersion,reference));
            var actualNow=DateTimeOffset.UtcNow;f.Forms.Clock=()=>actualNow.AddDays(31);
            Assert.Equal("FORM_DISPOSITION_RETENTION_EXPIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Forms.RebasePreviewAsync(id,new(f.Target,f.Draft.RowVersion,reference)))).Message);
            f.Forms.Clock=()=>DateTimeOffset.UtcNow;
            var policy=(await f.Forms.PolicyAsync("personal"))!;
            await f.Forms.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,policy.Roles,policy.RetentionPolicyRef,31,true,"policy changed after preview"));
            Assert.Equal("FORM_DISPOSITION_POLICY_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Forms.RebaseDraftAsync(id,ConsumeRebase(p)))).Message);
        }
        await using(var h=await Seed())
        {
            var f=await PrepareDisposition(h);var reference=await RetainRebase(h,f);var id=f.Draft.DraftId;
            var changed=PersonalResult<PersonalFormDraftV2>(await f.Forms.SaveDraftAsync(id,PersonalCommand(new PersonalFormSaveV2(PersonalData("""{"visible":"updated"}"""),null),"update-source",f.Draft.RowVersion)));
            Assert.Equal("FORM_DISPOSITION_SCOPE_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Forms.RebasePreviewAsync(id,new(f.Target,changed.RowVersion,reference)))).Message);
            var next=await f.Definitions.CreateNextDraftAsync("personal",FormNextDraftCommand((await f.Definitions.ReadHeadAsync("personal")).Version,f.Target,"third"));
            var saved=await f.Definitions.SaveAsync("personal",FormAuthoringCommand(schema:"""{"fields":[{"name":"visible","type":"input"}]}""",version:next.ResourceVersion,id:"save-third"));
            await f.Definitions.PublishAsync("personal",FormPublicationCommand(await FormPreview(f.Definitions,"personal",saved),id:"publish-third"));
            Assert.Equal("REBASE_TARGET_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Forms.RebasePreviewAsync(id,new(f.Target,changed.RowVersion,reference)))).Message);
            Assert.Equal(changed.RowVersion,(await f.Forms.DraftAsync(id)).RowVersion);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task FormDispositionRebase_new_hidden_request_is_committed_reused_and_does_not_upgrade_draft()
    {
        await using var h=await Seed();var f=await PrepareDisposition(h);var policy=(await f.Forms.PolicyAsync("personal"))!;
        await f.Forms.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,policy.Roles,policy.RetentionPolicyRef,31,true,"new exact retention policy"));
        var input=new PersonalFormRebasePreviewInputV2(f.Target,f.Draft.RowVersion);
        var first=await Assert.ThrowsAsync<PersonalFormDispositionRequiredV2>(()=>f.Forms.RebasePreviewAsync(f.Draft.DraftId,input));
        var second=await Assert.ThrowsAsync<PersonalFormDispositionRequiredV2>(()=>f.Forms.RebasePreviewAsync(f.Draft.DraftId,input));
        Assert.Equal(first.RequestId,second.RequestId);Assert.NotEqual(f.Requested.Request!.RequestId,first.RequestId);
        Assert.Equal("REQUESTED",(await Dispositions(h).ReadAsync(first.RequestId)).Request.State);
        Assert.Equal(f.Draft.RowVersion,(await f.Forms.DraftAsync(f.Draft.DraftId)).RowVersion);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_disposition_v1.requests WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task FormDispositionRebase_current_qualification_hold_revoke_and_receipt_scope_are_enforced_at_consumption()
    {
        await using var h=await Seed();var f=await PrepareDisposition(h);var reference=await RetainRebase(h,f);var id=f.Draft.DraftId;
        var preview=await f.Forms.RebasePreviewAsync(id,new(f.Target,f.Draft.RowVersion,reference));var command=ConsumeRebase(preview);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==73&&x.MenuId==744&&x.ActionCode=="retain").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Forms.RebaseDraftAsync(id,command))).Message);
        h.Db.Sys_RoleActions.Add(new(){RoleId=73,MenuId=744,ActionCode="retain"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=73,ResourceKey=PersonalFormOwnerV2.Resource,FieldName="DataJson",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FORM_DISPOSITION_READ_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Forms.RebaseDraftAsync(id,command))).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.RoleId==73&&x.ResourceKey==PersonalFormOwnerV2.Resource).ExecuteDeleteAsync();
        await h.Db.Sys_Users.Where(x=>x.Id==f.Processor).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Enable,false));
        Assert.Equal("FORM_DISPOSITION_PROCESSOR_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Forms.RebaseDraftAsync(id,command))).Message);
        await h.Db.Sys_Users.Where(x=>x.Id==f.Processor).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Enable,true));
        var processor=Dispositions(DecisionActor(h,f.Processor));
        var hold=await processor.HoldAsync(reference.RequestId,new(Guid.NewGuid(),null,true,"investigation","block rebase"));
        Assert.Equal("FORM_DISPOSITION_ON_HOLD",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Forms.RebaseDraftAsync(id,command))).Message);
        await processor.HoldAsync(reference.RequestId,new(Guid.NewGuid(),hold.Request!.Hold!.Version,false,"investigation ended","unblock"));
        var wrong=reference with{ReceiptVersion=Guid.NewGuid()};
        Assert.Equal("FORM_DISPOSITION_VERSION_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Forms.RebasePreviewAsync(id,new(f.Target,f.Draft.RowVersion,wrong)))).Message);
        var second=PersonalResult<PersonalFormDraftV2>(await f.Forms.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(f.Draft.FormVersionId,PersonalData("""{"visible":"other"}"""),null),"other-draft")));
        Assert.Equal("FORM_DISPOSITION_NOT_FOUND",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Forms.RebasePreviewAsync(second.DraftId,new(f.Target,second.RowVersion,reference)))).Message);
        await processor.DecideAsync(reference.RequestId,"revoke",new(Guid.NewGuid(),reference.ReceiptVersion,"withdraw processing authorization"));
        Assert.Equal("FORM_DISPOSITION_VERSION_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Forms.RebaseDraftAsync(id,command))).Message);
        Assert.Equal(f.Draft.RowVersion,(await f.Forms.DraftAsync(id)).RowVersion);
        Assert.Equal("NOT_OBSERVED",(await f.Forms.OriginalAsync("FORM_DRAFT_REBASE",id.ToString("D"),"consume")).Outcome);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormDispositionRebase_atomic_rollback_lost_commit_and_original_recovery_keep_one_consumption()
    {
        var hook=new StartWriteHook();var fault=new CommitFault();await using var h=await Seed(hook,fault);var f=await PrepareDisposition(h);var reference=await RetainRebase(h,f);var id=f.Draft.DraftId;
        var preview=await f.Forms.RebasePreviewAsync(id,new(f.Target,f.Draft.RowVersion,reference));var command=ConsumeRebase(preview);
        hook.Before=c=>{if(c.CommandText.Contains("UPDATE cp6_personal_form_v2.commands SET result_json"))throw new InvalidOperationException("injected final receipt failure");return Task.CompletedTask;};
        Assert.Equal("injected final receipt failure",(await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Forms.RebaseDraftAsync(id,command))).Message);hook.Before=null;
        Assert.Equal(f.Draft.RowVersion,(await f.Forms.DraftAsync(id)).RowVersion);Assert.Equal("RETAINED",(await Dispositions(h).ReadAsync(reference.RequestId)).Request.State);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_disposition_v1.consumptions WHERE tenant_id={h.Tenant}").SingleAsync());
        fault.Armed=true;Assert.Equal("COMMAND_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Forms.RebaseDraftAsync(id,command))).Message);
        var original=await f.Forms.OriginalAsync("FORM_DRAFT_REBASE",id.ToString("D"),"consume");Assert.Equal("SUCCEEDED",original.Outcome);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==73&&x.MenuId==744&&x.ActionCode=="retain").ExecuteDeleteAsync();
        Assert.Equal(original.ResourceVersion,(await f.Forms.RebaseDraftAsync(id,command)).ResourceVersion);
        var consumed=(await Dispositions(h).ReadAsync(reference.RequestId)).Request.Consumption!;
        Assert.Equal(f.Processor,consumed.ProcessorId);Assert.Equal(h.User,consumed.ActualActorId);Assert.Equal("consume",consumed.CommandId);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_disposition_v1.consumptions WHERE tenant_id={h.Tenant}").SingleAsync());
        var processor=Dispositions(DecisionActor(h,f.Processor));
        Assert.Equal("FORM_DISPOSITION_ALREADY_CONSUMED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>processor.DecideAsync(reference.RequestId,"revoke",new(Guid.NewGuid(),reference.ReceiptVersion,"too late")))).Message);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_form_disposition_v1.consumptions WHERE tenant_id={h.Tenant}"));
    }
    [CurrentPermissionPostgresFact]
    public async Task FormDispositionRebase_hold_wins_against_confirmation_on_independent_connection()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await PrepareDisposition(h);var reference=await RetainRebase(h,f);
        var preview=await f.Forms.RebasePreviewAsync(f.Draft.DraftId,new(f.Target,f.Draft.RowVersion,reference));
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hook.Before=async c=>{if(c.CommandText.Contains("INSERT INTO cp6_form_disposition_v1.holds")){entered.TrySetResult();await release.Task;}};
        var hold=Dispositions(DecisionActor(h,f.Processor)).HoldAsync(reference.RequestId,new(Guid.NewGuid(),null,true,"competing investigation","stop rebase"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var competitor=PersonalForm(h with{Db=peer});
        var losing=Record.ExceptionAsync(()=>competitor.RebaseDraftAsync(f.Draft.DraftId,ConsumeRebase(preview)));
        try{await Task.Delay(100);Assert.False(losing.IsCompleted);}finally{release.TrySetResult();}
        await hold;Assert.Equal("FORM_DISPOSITION_ON_HOLD",Assert.IsType<ScopeAuthorizationExceptionV1>(await losing).Message);
        Assert.Equal(f.Draft.RowVersion,(await f.Forms.DraftAsync(f.Draft.DraftId)).RowVersion);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormDispositionRebase_HTTP_hidden_loss_has_real_request_and_qualified_exact_consumption()
    {
        await using var h=await Seed();var f=await PrepareDisposition(h);var id=f.Draft.DraftId;
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);var url=$"/api/oa/v2/drafts/{id}/rebase-preview";
        using var blocked=await host.Send(BoundRequest(cookies,url,new{targetVersionId=f.Target,expectedVersion=f.Draft.RowVersion}));
        Assert.Equal(System.Net.HttpStatusCode.Forbidden,blocked.StatusCode);
        var text=await blocked.Content.ReadAsStringAsync();var problem=JsonDocument.Parse(text).RootElement;
        Assert.Equal("REBASE_HIDDEN_LOSS_BLOCKED",problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("dispositionRequestId",out var request),"Hidden loss must return a persisted controlled request reference.");
        Assert.Equal(f.Requested.Request!.RequestId,request.GetGuid());
        Assert.DoesNotContain("secret",text);Assert.DoesNotContain("private",text);Assert.DoesNotContain("rows",text);
        using var repeated=await host.Send(BoundRequest(cookies,url,new{targetVersionId=f.Target,expectedVersion=f.Draft.RowVersion}));
        Assert.Equal(request.GetGuid(),JsonDocument.Parse(await repeated.Content.ReadAsStringAsync()).RootElement.GetProperty("dispositionRequestId").GetGuid());
        var processor=Dispositions(DecisionActor(h,f.Processor));
        var approved=await processor.DecideAsync(request.GetGuid(),"approve",new(Guid.NewGuid(),f.Requested.Request.Version,"qualified exact rebase review"));
        var retained=await processor.DecideAsync(request.GetGuid(),"retain",new(Guid.NewGuid(),approved.Request!.Version,"retain original and authorize exact losses"));
        using var forged=await host.Send(BoundRequest(cookies,url,new{targetVersionId=f.Target,expectedVersion=f.Draft.RowVersion,
            dispositionRef=new{requestId=request.GetGuid(),receiptVersion=retained.Request!.Version,processorId=h.User}}));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest,forged.StatusCode);
        using var previewResponse=await host.Send(BoundRequest(cookies,url,new{targetVersionId=f.Target,expectedVersion=f.Draft.RowVersion,dispositionRef=retained.Request!.DispositionRef}));
        var preview=await PersonalHttpResult<JsonElement>(previewResponse);
        Assert.False(preview.GetProperty("requiresConfirmation").GetBoolean());
        Assert.DoesNotContain("secret",preview.GetRawText());Assert.DoesNotContain("private",preview.GetRawText());
        var body=new{previewId=preview.GetProperty("previewId").GetGuid(),previewDigest=preview.GetProperty("digest").GetString(),targetVersionId=f.Target,confirmRemovedValues=false,dispositionRef=retained.Request.DispositionRef};
        using var response=await host.Send(PersonalHttp(cookies,$"/api/oa/v2/drafts/{id}/rebase",PersonalCommand(body,"qualified-rebase",f.Draft.RowVersion)));
        var result=await PersonalHttpResult<PersonalFormResultV2>(response);Assert.Equal("SUCCEEDED",result.Outcome);
        var current=await f.Forms.DraftAsync(id);Assert.Equal(f.Target,current.FormVersionId);Assert.NotEqual(f.Draft.RowVersion,current.RowVersion);
        Assert.Equal("CONSUMED",(await Dispositions(h).ReadAsync(request.GetGuid())).Request.State);
        Assert.Equal(h.User,await h.Db.Database.SqlQuery<Guid>($"SELECT owner_id AS \"Value\" FROM cp6_personal_form_v2.drafts WHERE id={id}").SingleAsync());
        var data=await h.Db.Database.SqlQuery<string>($"SELECT data_json AS \"Value\" FROM cp6_personal_form_v2.drafts WHERE id={id}").SingleAsync();
        var rows=JsonDocument.Parse(data).RootElement.GetProperty("rows");Assert.Equal(2,rows.GetArrayLength());Assert.Equal("two",rows[1].GetProperty("keep").GetString());
        var history=await h.Db.Database.SqlQuery<string>($"SELECT before_json AS \"Value\" FROM cp6_personal_form_v2.history WHERE draft_id={id} AND before_json IS NOT NULL").SingleAsync();
        Assert.Contains("9007199254740993.0001",history);Assert.Contains("second secret",history);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
    }
}
