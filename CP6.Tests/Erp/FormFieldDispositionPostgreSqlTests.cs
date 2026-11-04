using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private FormFieldDispositionOwnerV1 Dispositions(Harness h)=>new(h.Db,PersonalForm(h),ScopeFence(h));
    private sealed record DispositionFixture(PersonalFormOwnerV2 Forms,FormAuthoringOwnerV2 Definitions,PersonalFormDraftV2 Draft,
        Guid Target,Guid Processor,FormDispositionRequestV1 Input,FormDispositionOperationV1 Requested);
    private async Task<DispositionFixture> PrepareDisposition(Harness h,string? targetSchema=null)
    {
        const string source="""{"fields":[{"name":"visible","type":"input"},{"name":"secret","type":"number"},{"name":"rows","type":"table","columns":[{"name":"keep","type":"input"},{"name":"private","type":"input"}]}]}""";
        var(forms,definitions,_)=await PreparePersonal(h,source);var context=await forms.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await forms.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,
            PersonalData("""{"visible":"current","secret":9007199254740993.0001,"rows":[{"keep":"one","private":"first secret"},{"keep":"two","private":"second secret"}]}"""),null))));
        var next=await definitions.CreateNextDraftAsync("personal",FormNextDraftCommand((await definitions.ReadHeadAsync("personal")).Version,context.FormVersionId));
        var saved=await definitions.SaveAsync("personal",FormAuthoringCommand(schema:targetSchema??"""{"fields":[{"name":"visible","type":"input"},{"name":"rows","type":"table","columns":[{"name":"keep","type":"input"}]}]}""",version:next.ResourceVersion,id:"next"));
        var target=(await definitions.PublishAsync("personal",FormPublicationCommand(await FormPreview(definitions,"personal",saved),id:"publish-next"))).Result!.Publication!;
        CP6.WebApi.Seed.FormDispositionPermissionSeed.EnsureSeeded(h.Db);h.Db.ChangeTracker.Clear();
        Assert.False(await h.Db.Sys_RoleActions.AnyAsync(x=>x.MenuId==744));
        var processor=Guid.NewGuid();h.Db.Sys_Roles.Add(new(){RoleId=73,RoleName="simulated qualified form processor"});
        h.Db.Sys_Users.Add(new(){Id=processor,UserName="form-processor",Password="unused",RoleId=73});
        h.Db.Sys_RoleMenus.Add(new(){RoleId=73,MenuId=744});
        foreach(var action in new[]{"query","approve","retain","revoke","hold"})h.Db.Sys_RoleActions.Add(new(){RoleId=73,MenuId=744,ActionCode=action});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var policy=(await forms.PolicyAsync("personal"))!;
        await forms.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,
            [new(72,new(){["secret"]="hidden",["rows"]="hidden"}),new(73,[])],policy.RetentionPolicyRef,30,true,"simulated qualified processing"));
        var input=new FormDispositionRequestV1(Guid.NewGuid(),draft.DraftId,Guid.Parse(draft.RowVersion),target.VersionId,"retain exact original history");
        return new(forms,definitions,draft,target.VersionId,processor,input,await Dispositions(h).RequestAsync(input));
    }
    [CurrentPermissionPostgresFact]
    public async Task FormDisposition_HTTP_request_uses_actual_history_and_never_discloses_hidden_loss()
    {
        const string schema="""{"fields":[{"name":"visible","type":"input"},{"name":"secret","type":"input"}]}""";
        await using var h=await Seed();var(owner,form,_)=await PreparePersonal(h,schema);var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("""{"visible":"keep","secret":"private source"}"""),null))));
        var next=await form.CreateNextDraftAsync("personal",FormNextDraftCommand((await form.ReadHeadAsync("personal")).Version,context.FormVersionId));
        var saved=await form.SaveAsync("personal",FormAuthoringCommand(schema:"""{"fields":[{"name":"visible","type":"input"}]}""",version:next.ResourceVersion,id:"next"));
        var target=(await form.PublishAsync("personal",FormPublicationCommand(await FormPreview(form,"personal",saved),id:"publish-next"))).Result!.Publication!;
        var policy=(await owner.PolicyAsync("personal"))!;
        await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){["secret"]="hidden"})],policy.RetentionPolicyRef,30,true,"simulated hidden loss"));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);var operation=Guid.NewGuid();
        using var response=await host.Send(BoundRequest(cookies,"/api/oa/form-field-dispositions/v1/requests",new{operationId=operation,draftId=draft.DraftId,expectedDraftVersion=Guid.Parse(draft.RowVersion),targetVersionId=target.VersionId,reason="retain exact source before rebase"}));
        var result=await PersonalHttpResult<JsonElement>(response);
        Assert.Equal("REQUESTED",result.GetProperty("request").GetProperty("state").GetString());
        Assert.DoesNotContain("private source",result.GetRawText());Assert.DoesNotContain("secret",result.GetRawText());
        Assert.Equal(draft.DraftId,result.GetProperty("request").GetProperty("draftId").GetGuid());
        Assert.Equal(0,(await owner.DraftAsync(draft.DraftId)).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormDisposition_qualified_actual_HTTP_approval_retention_and_current_field_rights_preserve_exact_original()
    {
        await using var h=await Seed();var f=await PrepareDisposition(h);var id=f.Requested.Request!.RequestId;var owner=Dispositions(h);
        var own=await owner.ReadAsync(id);Assert.Null(own.Losses);Assert.Null(own.HistoryId);
        Assert.Equal(f.Requested,await owner.RequestAsync(f.Input));
        Assert.Equal("COMMAND_BODY_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.RequestAsync(f.Input with{Reason="other intent"}))).Message);
        var peer=DecisionActor(h,f.Processor);var processor=Dispositions(peer);
        var detail=await processor.ReadAsync(id);Assert.Equal(3,detail.Losses!.Length);
        Assert.Equal("9007199254740993.0001",detail.Losses.Single(x=>x.Field=="secret").ValueJson);
        Assert.Contains(detail.Losses,x=>x.Path=="rows[1].private"&&x.ValueJson=="\"second secret\"");
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.RoleId,73));
        Assert.Equal("FORM_DISPOSITION_INDEPENDENT_APPROVER_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.DecideAsync(id,"approve",new(Guid.NewGuid(),own.Request.Version,"own approval")))).Message);
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.RoleId,72));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await DocumentReviewerLogin(host,h,f.Processor);var approve=new FormDispositionCommandV1(Guid.NewGuid(),own.Request.Version,"qualified independent review");
        const string root="/api/oa/form-field-dispositions/v1";
        using var csrf=await host.Send(PersonalHttp(cookies,root+"/requests/"+id+"/approve",JsonSerializer.SerializeToUtf8Bytes(approve,PersonalFormOwnerV2.Json),false));
        Assert.Equal(System.Net.HttpStatusCode.Forbidden,csrf.StatusCode);
        using var accepted=await host.Send(BoundRequest(cookies,root+"/requests/"+id+"/approve",approve));var approved=await PersonalHttpResult<FormDispositionOperationV1>(accepted);
        var retain=new FormDispositionCommandV1(Guid.NewGuid(),approved.Request!.Version,"retain according to simulated policy");
        using var retainedResponse=await host.Send(BoundRequest(cookies,root+"/requests/"+id+"/retain",retain));var retained=await PersonalHttpResult<FormDispositionOperationV1>(retainedResponse);
        Assert.Equal("RETAINED",retained.Request!.State);Assert.NotNull(retained.Request.DispositionRef);Assert.True(retained.Request.RetainUntilUtc> DateTimeOffset.UtcNow.AddDays(29));
        Assert.Equal(f.Draft.RowVersion,(await f.Forms.DraftAsync(f.Draft.DraftId)).RowVersion);
        Assert.Contains("9007199254740993.0001",await h.Db.Database.SqlQuery<string>($"SELECT after_json AS \"Value\" FROM cp6_personal_form_v2.history WHERE id={detail.HistoryId}").SingleAsync());
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==73&&x.MenuId==744&&x.ActionCode=="retain").ExecuteDeleteAsync();
        Assert.Equal(retained,await processor.OperationAsync(retain.OperationId));Assert.Equal(retained,await processor.DecideAsync(id,"retain",retain));
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=73,ResourceKey=PersonalFormOwnerV2.Resource,FieldName="DataJson",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var denied=await host.Send(BoundRequest(cookies,root+"/requests/"+id));Assert.Equal(System.Net.HttpStatusCode.Forbidden,denied.StatusCode);
        Assert.DoesNotContain("first secret",await denied.Content.ReadAsStringAsync());
        Assert.Equal("FORM_DISPOSITION_READ_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>processor.OperationAsync(retain.OperationId))).Message);
        Assert.Null((await owner.ReadAsync(id)).Losses);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormDisposition_hold_revoke_exact_versions_and_current_policy_do_not_invent_rebase_effect()
    {
        await using var h=await Seed();var f=await PrepareDisposition(h);var processor=Dispositions(DecisionActor(h,f.Processor));var request=f.Requested.Request!;
        var held=await processor.HoldAsync(request.RequestId,new(Guid.NewGuid(),null,true,"simulated investigation","preserve original"));
        Assert.Equal("FORM_DISPOSITION_ON_HOLD",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>processor.DecideAsync(request.RequestId,"approve",new(Guid.NewGuid(),request.Version,"blocked")))).Message);
        Assert.Equal("FORM_DISPOSITION_HOLD_VERSION_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>processor.HoldAsync(request.RequestId,new(Guid.NewGuid(),null,false,"investigation closed","stale hold version")))).Message);
        await processor.HoldAsync(request.RequestId,new(Guid.NewGuid(),held.Request!.Hold!.Version,false,"investigation closed","resume qualified processing"));
        var approved=await processor.DecideAsync(request.RequestId,"approve",new(Guid.NewGuid(),request.Version,"reviewed"));
        var revoked=await processor.DecideAsync(request.RequestId,"revoke",new(Guid.NewGuid(),approved.Request!.Version,"stop processing"));
        Assert.Null(revoked.Request!.DispositionRef);
        Assert.Equal("FORM_DISPOSITION_VERSION_CONFLICT",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>processor.DecideAsync(request.RequestId,"retain",new(Guid.NewGuid(),approved.Request.Version,"stale")))).Message);
        var current=await processor.ReadAsync(request.RequestId);Assert.Equal(new[]{"REQUESTED","APPROVED","REVOKED"},current.History!.Select(x=>x.State));
        var second=await Dispositions(h).RequestAsync(f.Input with{OperationId=Guid.NewGuid()});
        var policy=(await f.Forms.PolicyAsync("personal"))!;
        await f.Forms.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,policy.Roles,policy.RetentionPolicyRef,31,true,"changed policy"));
        Assert.Equal("FORM_DISPOSITION_POLICY_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>processor.DecideAsync(second.Request!.RequestId,"approve",new(Guid.NewGuid(),second.Request.Version,"stale policy")))).Message);
        var third=await Dispositions(h).RequestAsync(f.Input with{OperationId=Guid.NewGuid()});
        await f.Forms.SaveDraftAsync(f.Draft.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("""{"visible":"changed"}"""),null),"change-visible",f.Draft.RowVersion));
        Assert.Equal("FORM_DISPOSITION_SOURCE_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>processor.DecideAsync(third.Request!.RequestId,"approve",new(Guid.NewGuid(),third.Request.Version,"stale source")))).Message);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task FormDisposition_lost_commit_recovers_original_and_closed_operation_cannot_create_another_request()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var f=await PrepareDisposition(h);var owner=Dispositions(h);
        var closed=f.Input with{OperationId=Guid.NewGuid()};Assert.Equal("UNKNOWN",(await owner.OperationAsync(closed.OperationId)).State);
        Assert.Equal("CLOSED",(await owner.OperationAsync(closed.OperationId,true)).State);
        Assert.Equal("COMMAND_CLOSED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.RequestAsync(closed))).Message);
        var lost=f.Input with{OperationId=Guid.NewGuid()};fault.Armed=true;
        Assert.Equal("COMMAND_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.RequestAsync(lost))).Message);
        var original=await owner.OperationAsync(lost.OperationId);Assert.Equal("COMMITTED",original.State);Assert.Equal(original,await owner.RequestAsync(lost));
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_disposition_v1.requests WHERE tenant_id={h.Tenant}").SingleAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_form_disposition_v1.events WHERE tenant_id={h.Tenant}"));
    }
    [CurrentPermissionPostgresFact]
    public async Task FormDisposition_hold_and_retention_two_connections_serialize_at_the_actual_final_fence()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await PrepareDisposition(h);var actor=DecisionActor(h,f.Processor);var processor=Dispositions(actor);var id=f.Requested.Request!.RequestId;
        var approved=await processor.DecideAsync(id,"approve",new(Guid.NewGuid(),f.Requested.Request.Version,"reviewed"));
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hook.Before=async c=>{if(c.CommandText.Contains("INSERT INTO cp6_form_disposition_v1.holds")){entered.TrySetResult();await release.Task;}};
        var hold=processor.HoldAsync(id,new(Guid.NewGuid(),null,true,"simulated hold","block competing retention"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var competitor=Dispositions(actor with{Db=peer});
        var input=new FormDispositionCommandV1(Guid.NewGuid(),approved.Request!.Version,"competing retention");
        var losing=Record.ExceptionAsync(()=>competitor.DecideAsync(id,"retain",input));
        try{await Task.Delay(100);Assert.False(losing.IsCompleted);}finally{release.TrySetResult();}
        await hold;Assert.Equal("FORM_DISPOSITION_ON_HOLD",Assert.IsType<ScopeAuthorizationExceptionV1>(await losing).Message);
        Assert.Equal("UNKNOWN",(await competitor.OperationAsync(input.OperationId)).State);Assert.Equal("APPROVED",(await processor.ReadAsync(id)).Request.State);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormDisposition_missing_qualification_cross_tenant_and_failed_journal_leave_no_new_effect()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await PrepareDisposition(h);var owner=Dispositions(h);var id=f.Requested.Request!.RequestId;
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.DecideAsync(id,"retain",new(Guid.NewGuid(),f.Requested.Request.Version,"not qualified")))).Message);
        var otherUser=await GrantDelegate(h);Assert.Equal(403,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Dispositions(DecisionActor(h,otherUser)).ReadAsync(id))).Status);
        await using var other=await Seed();await PreparePersonal(other);Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Dispositions(other).ReadAsync(id))).Status);
        var input=f.Input with{OperationId=Guid.NewGuid()};
        hook.Before=c=>{if(c.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))throw new InvalidOperationException("injected journal outage");return Task.CompletedTask;};
        Assert.Equal("injected journal outage",(await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.RequestAsync(input))).Message);hook.Before=null;
        Assert.Equal("UNKNOWN",(await owner.OperationAsync(input.OperationId)).State);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_disposition_v1.requests WHERE tenant_id={h.Tenant}").SingleAsync());
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_form_disposition_v1.events DISABLE TRIGGER disposition_immutable");
        try{Assert.Equal("FORM_DISPOSITION_WRITER_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.RequestAsync(input))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_form_disposition_v1.events ENABLE TRIGGER disposition_immutable");}
    }
    [CurrentPermissionPostgresFact]
    public async Task FormDisposition_new_published_target_invalidates_approved_scope_and_revoke_preserves_original_approval()
    {
        await using var h=await Seed();var f=await PrepareDisposition(h);var owner=Dispositions(DecisionActor(h,f.Processor));var request=f.Requested.Request!;
        var input=new FormDispositionCommandV1(Guid.NewGuid(),request.Version,"approve exact target");var approved=await owner.DecideAsync(request.RequestId,"approve",input);
        var next=await f.Definitions.CreateNextDraftAsync("personal",FormNextDraftCommand((await f.Definitions.ReadHeadAsync("personal")).Version,f.Target,"newer-target"));
        var saved=await f.Definitions.SaveAsync("personal",FormAuthoringCommand(schema:"""{"fields":[{"name":"visible","type":"input"},{"name":"new","type":"input"}]}""",version:next.ResourceVersion,id:"save-newer-target"));
        await f.Definitions.PublishAsync("personal",FormPublicationCommand(await FormPreview(f.Definitions,"personal",saved),id:"publish-newer-target"));
        Assert.Equal("REBASE_TARGET_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.DecideAsync(request.RequestId,"retain",new(Guid.NewGuid(),approved.Request!.Version,"stale target")))).Message);
        var revoked=await owner.DecideAsync(request.RequestId,"revoke",new(Guid.NewGuid(),approved.Request!.Version,"revoke obsolete target"));Assert.Equal("REVOKED",revoked.Request!.State);
        Assert.Equal(approved,await owner.OperationAsync(input.OperationId));Assert.Equal(approved,await owner.DecideAsync(request.RequestId,"approve",input));
        Assert.Equal(f.Draft.RowVersion,(await f.Forms.DraftAsync(f.Draft.DraftId)).RowVersion);
    }
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_form_disposition_approval_retention_lost_response_and_revocation()
    {
        await using var h=await Seed();var f=await PrepareDisposition(h);var password=new BCryptPasswordHasher(4).Hash("Price1!Password");
        await h.Db.Sys_Users.Where(x=>x.Id==f.Processor).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.Password,password));
        using var result=await BrowserScenario(h,f.Processor,"oa-form-disposition-browser.mjs","form-disposition-browser",
            new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")},new(){["CP6_DISPOSITION_REQUEST"]=f.Requested.Request!.RequestId.ToString("D")});
        var current=await Dispositions(DecisionActor(h,f.Processor)).ReadAsync(f.Requested.Request.RequestId);
        Assert.Equal("REVOKED",current.Request.State);Assert.Equal(new[]{"REQUESTED","APPROVED","RETAINED","REVOKED"},current.History!.Select(x=>x.State));
        Assert.Equal(f.Draft.RowVersion,(await f.Forms.DraftAsync(f.Draft.DraftId)).RowVersion);Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
    }
}
