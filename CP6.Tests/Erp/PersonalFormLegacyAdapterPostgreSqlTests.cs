using System.Net;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task LegacyPersonalForm_HTTP_full_UTF8_budget_keeps_original_data_and_rejects_one_extra_byte()
    {
        var source=FormDataAtLimit(fill:"中");await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h,source.Schema);var context=await owner.ContextAsync("personal");
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);
        HttpRequestMessage Request(JsonElement data,string key)
        {
            var request=LegacyFormHttp(cookies,"/api/oa/forms/personal/submissions",new{},key,context);
            request.Content=new StringContent("{\"Data\":"+data.GetRawText()+"}",System.Text.Encoding.UTF8,"application/json");return request;
        }
        using var sent=await host.Send(Request(source.Data,"full-legacy"));var accepted=await PersonalHttpResult<JsonElement>(sent);
        var stored=await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync();Assert.Equal(source.Data.GetRawText(),stored.DataJson);Assert.Equal(accepted.GetProperty("formDataId").GetGuid(),stored.Id);
        using var replay=await host.Send(Request(source.Data,"full-legacy"));Assert.Equal(stored.Id,(await PersonalHttpResult<JsonElement>(replay)).GetProperty("formDataId").GetGuid());
        using var excess=await host.Send(Request(FormDataAtLimit(1024*1024+1,"中").Data,"excess-legacy"));Assert.Equal(HttpStatusCode.UnprocessableEntity,excess.StatusCode);
        Assert.Equal("NOT_OBSERVED",(await owner.LegacyOriginalAsync(PersonalFormOwnerV2.Direct,"personal","excess-legacy")).Outcome);
        Assert.Equal(1,await h.Db.Wf_FormDatas.CountAsync());
    }

    private static HttpRequestMessage LegacyFormHttp(Dictionary<string,string> cookies,string path,object body,string key,
        PersonalFormContextV2? context=null,HttpMethod? method=null)
    {
        var request=BoundRequest(cookies,path,body);request.Headers.Add("Idempotency-Key",key);
        if(method!=null)request.Method=method;
        if(context!=null)
        {
            request.Headers.Add("X-Form-Version-Id",context.FormVersionId.ToString("D"));
            request.Headers.Add("X-Form-Activation-Revision",context.ActivationRevision);
            request.Headers.Add("X-Form-Binding-Revision",context.BindingRevision??"null");
            request.Headers.Add("X-Flow-Version-Id",context.FlowVersionId?.ToString("D")??"null");
            request.Headers.Add("X-Flow-Activation-Revision",context.FlowActivationRevision??"null");
        }
        return request;
    }

    [CurrentPermissionPostgresFact]
    public async Task LegacyPersonalForm_HTTP_create_save_delete_use_one_native_draft_and_original_intent()
    {
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);var body=new{data=PersonalData("{\"a\":\"legacy intent\"}"),title="same owner"};
        using var missing=await host.Send(LegacyFormHttp(cookies,"/api/oa/forms/personal/drafts",body,"old-create"));
        Assert.Equal(HttpStatusCode.Conflict,missing.StatusCode);Assert.Contains("E-WF-040",await missing.Content.ReadAsStringAsync());
        using var created=await host.Send(LegacyFormHttp(cookies,"/api/oa/forms/personal/drafts",body,"old-create",context));
        var draft=await PersonalHttpResult<JsonElement>(created);var id=draft.GetProperty("id").GetGuid();var version=draft.GetProperty("rowVersion").GetString()!;
        Assert.Equal(Guid.Parse((await owner.DraftAsync(id)).RowVersion).ToByteArray(),Convert.FromBase64String(version));
        Assert.Equal("legacy intent",JsonDocument.Parse((await owner.DraftAsync(id)).DataJson).RootElement.GetProperty("a").GetString());
        using var replay=await host.Send(LegacyFormHttp(cookies,"/api/oa/forms/personal/drafts",body,"old-create",context));
        Assert.Equal(id,(await PersonalHttpResult<JsonElement>(replay)).GetProperty("id").GetGuid());
        using var conflict=await host.Send(LegacyFormHttp(cookies,"/api/oa/forms/personal/drafts",new{data=PersonalData("{\"a\":\"changed\"}"),title="same owner"},"old-create",context));
        Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);Assert.Contains("E-WF-044",await conflict.Content.ReadAsStringAsync());
        using var list=await host.Send(BoundRequest(cookies,"/api/oa/drafts"));var page=await PersonalHttpResult<JsonElement>(list);
        Assert.Equal(1,page.GetProperty("total").GetInt32());Assert.False(page.GetProperty("items")[0].TryGetProperty("dataJson",out _));
        var saveBody=new{data=PersonalData("{\"a\":\"saved through old route\"}"),title="updated",rowVersion=version};
        using var saved=await host.Send(LegacyFormHttp(cookies,"/api/oa/drafts/"+id,saveBody,"old-save",method:HttpMethod.Put));
        var savedDraft=await PersonalHttpResult<JsonElement>(saved);var savedVersion=savedDraft.GetProperty("rowVersion").GetString()!;Assert.NotEqual(version,savedVersion);
        using var savedAgain=await host.Send(LegacyFormHttp(cookies,"/api/oa/drafts/"+id,saveBody,"old-save",method:HttpMethod.Put));
        Assert.Equal(savedVersion,(await PersonalHttpResult<JsonElement>(savedAgain)).GetProperty("rowVersion").GetString());
        using var stale=await host.Send(LegacyFormHttp(cookies,"/api/oa/drafts/"+id,saveBody,"stale-save",method:HttpMethod.Put));
        Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);Assert.Contains("E-WF-041",await stale.Content.ReadAsStringAsync());
        using var noToken=await host.Send(LegacyFormHttp(cookies,"/api/oa/drafts/"+id,new{},"old-delete",method:HttpMethod.Delete));
        Assert.Equal(HttpStatusCode.Conflict,noToken.StatusCode);
        for(var attempt=0;attempt<2;attempt++)
        {
            using var request=LegacyFormHttp(cookies,"/api/oa/drafts/"+id,new{},"old-delete",method:HttpMethod.Delete);request.Headers.TryAddWithoutValidation("If-Match","\""+savedVersion+"\"");
            using var removed=await host.Send(request);Assert.True(await PersonalHttpResult<bool>(removed));
        }
        var retained=await owner.DraftAsync(id);Assert.Equal(2,retained.Status);Assert.Contains("saved through old route",retained.DataJson);
        using var after=await host.Send(BoundRequest(cookies,"/api/oa/drafts"));Assert.Equal(0,(await PersonalHttpResult<JsonElement>(after)).GetProperty("total").GetInt32());
        Assert.Empty(await h.Db.Wf_FormDrafts.ToArrayAsync());Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task LegacyPersonalForm_HTTP_submissions_preserve_exact_context_numbers_and_actual_actor_slots()
    {
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h,"""{"fields":[{"name":"amount","type":"number"}]}""");
        var context=await owner.ContextAsync("personal");var other=new Sys_User{UserName="legacy-other",RoleId=72};h.Db.Sys_Users.Add(other);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);var otherCookies=await DocumentReviewerLogin(host,h,other.Id);
        var body=new{data=PersonalData("{\"amount\":9007199254740993.0001}"),draftId=(Guid?)null};
        using var drift=LegacyFormHttp(cookies,"/api/oa/forms/personal/submissions",body,"drift",context with{ActivationRevision=Guid.NewGuid().ToString("D")});
        using var refused=await host.Send(drift);Assert.Equal(HttpStatusCode.Conflict,refused.StatusCode);Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
        using var sent=await host.Send(LegacyFormHttp(cookies,"/api/oa/forms/personal/submissions",body,"same-key",context));
        var first=await PersonalHttpResult<JsonElement>(sent);var firstId=first.GetProperty("formDataId").GetGuid();Assert.Equal(JsonValueKind.Null,first.GetProperty("flowInstanceId").ValueKind);
        using var notTheirs=await host.Send(BoundRequest(otherCookies,"/api/oa/legacy-form-commands/FORM_DIRECT_SUBMIT/personal/same-key"));
        Assert.Equal("NOT_OBSERVED",(await PersonalHttpResult<PersonalFormResultV2>(notTheirs)).Outcome);
        using var theirs=await host.Send(LegacyFormHttp(otherCookies,"/api/oa/forms/personal/submissions",body,"same-key",context));
        var otherId=(await PersonalHttpResult<JsonElement>(theirs)).GetProperty("formDataId").GetGuid();Assert.NotEqual(firstId,otherId);
        Assert.Equal(other.Id,(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync(x=>x.Id==otherId)).SubmittedBy);
        using var replay=await host.Send(LegacyFormHttp(cookies,"/api/oa/forms/personal/submissions",new Dictionary<string,object>{{"Data",body.data}},"same-key",context));
        Assert.Equal(firstId,(await PersonalHttpResult<JsonElement>(replay)).GetProperty("formDataId").GetGuid());
        using var ambiguous=await host.Send(LegacyFormHttp(cookies,"/api/oa/forms/personal/submissions",new Dictionary<string,object>{{"data",body.data},{"Data",body.data}},"ambiguous",context));
        Assert.Equal(HttpStatusCode.BadRequest,ambiguous.StatusCode);
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,body.data,null))));
        using var substituted=await host.Send(LegacyFormHttp(cookies,"/api/oa/forms/personal/submissions",new{body.data,draftId=draft.DraftId},"wrong-route",context));
        Assert.Equal(HttpStatusCode.BadRequest,substituted.StatusCode);Assert.Equal(0,(await owner.DraftAsync(draft.DraftId)).Status);
        var submitBody=new{rowVersion=Convert.ToBase64String(Guid.Parse(draft.RowVersion).ToByteArray())};
        using var submitted=await host.Send(LegacyFormHttp(cookies,"/api/oa/drafts/"+draft.DraftId+"/submit",submitBody,"draft-key",context));
        var submittedId=(await PersonalHttpResult<JsonElement>(submitted)).GetProperty("formDataId").GetGuid();
        using var again=await host.Send(LegacyFormHttp(cookies,"/api/oa/drafts/"+draft.DraftId+"/submit",submitBody,"draft-key",context));
        Assert.Equal(submittedId,(await PersonalHttpResult<JsonElement>(again)).GetProperty("formDataId").GetGuid());
        using var different=await host.Send(LegacyFormHttp(cookies,"/api/oa/drafts/"+draft.DraftId+"/submit",submitBody,"different-key",context));
        Assert.Equal(HttpStatusCode.Conflict,different.StatusCode);Assert.Contains("DRAFT_ALREADY_SUBMITTED",await different.Content.ReadAsStringAsync());
        Assert.Equal(3,await h.Db.Wf_FormDatas.CountAsync());Assert.All(await h.Db.Wf_FormDatas.ToArrayAsync(),x=>Assert.Contains("9007199254740993.0001",x.DataJson));
        Assert.Equal(3,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.legacy_commands WHERE tenant_id={h.Tenant}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task LegacyPersonalForm_mapping_is_immutable_and_disabled_writer_rolls_back_without_native_effects()
    {
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h);var c=await owner.ContextAsync("personal");
        var raw=JsonSerializer.SerializeToUtf8Bytes(new{data=PersonalData("{\"a\":\"mapping\"}"),draftId=(Guid?)null});
        var headers=new LegacyPersonalFormHeadersV1("mapping-one",c.FormVersionId.ToString("D"),c.ActivationRevision,c.BindingRevision??"null",c.FlowVersionId?.ToString("D")??"null",c.FlowActivationRevision??"null");
        await owner.LegacyExecuteAsync("FORM_DIRECT_SUBMIT","personal",raw,headers);
        Assert.Equal(raw,await h.Db.Database.SqlQuery<byte[]>($"SELECT raw_request AS \"Value\" FROM cp6_personal_form_v2.legacy_commands WHERE tenant_id={h.Tenant}").SingleAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_personal_form_v2.legacy_commands SET request_hash='rewrite' WHERE tenant_id={h.Tenant}"));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_personal_form_v2.legacy_commands DISABLE TRIGGER personal_legacy_guard");
        try
        {
            var error=await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.LegacyExecuteAsync("FORM_DIRECT_SUBMIT","personal",raw,headers with{Key="guard-missing"}));
            Assert.Equal("FORM_LEGACY_WRITER_COVERAGE_UNAVAILABLE",error.Message);
            Assert.Equal("NOT_OBSERVED",(await owner.LegacyOriginalAsync("FORM_DIRECT_SUBMIT","personal","guard-missing")).Outcome);
            Assert.Equal(1,await h.Db.Wf_FormDatas.CountAsync());
        }
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_personal_form_v2.legacy_commands ENABLE TRIGGER personal_legacy_guard");}
        await owner.LegacyExecuteAsync("FORM_DIRECT_SUBMIT","personal",raw,headers with{Key="guard-missing"});
        Assert.Equal(2,await h.Db.Wf_FormDatas.CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task LegacyPersonalForm_HTTP_rebase_uses_exact_preview_target_and_returns_original_filtered_diagnostics()
    {
        const string source="""{"fields":[{"name":"amount","type":"number"},{"name":"old","type":"input"}]}""";
        const string target="""{"fields":[{"name":"amount","type":"number"},{"name":"required","type":"input","required":true}]}""";
        await using var h=await Seed();var(owner,form,_)=await PreparePersonal(h,source);var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"amount\":9007199254740993.0001,\"old\":\"retained original\"}"),null))));
        var next=await form.CreateNextDraftAsync("personal",FormNextDraftCommand((await form.ReadHeadAsync("personal")).Version,context.FormVersionId));
        var saved=await form.SaveAsync("personal",FormAuthoringCommand(schema:target,version:next.ResourceVersion,id:"next-save"));
        var publication=(await form.PublishAsync("personal",FormPublicationCommand(await FormPreview(form,"personal",saved),id:"next-publish"))).Result!.Publication!;
        var preview=await owner.RebasePreviewAsync(draft.DraftId,new(publication.VersionId,draft.RowVersion));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});var cookies=await Login(host,h);
        HttpRequestMessage Rebase(string key,int version,bool confirmed)
        {
            var message=LegacyFormHttp(cookies,"/api/oa/drafts/"+draft.DraftId+"/rebase",new{targetVersion=version,confirmRemovedValues=confirmed,rowVersion=Convert.ToBase64String(Guid.Parse(draft.RowVersion).ToByteArray())},key);
            message.Headers.Add("X-Form-Version-Id",publication.VersionId.ToString("D"));message.Headers.Add("X-Form-Rebase-Preview",preview.PreviewId.ToString("D"));message.Headers.Add("X-Form-Rebase-Digest",preview.Digest);return message;
        }
        using var mismatch=await host.Send(Rebase("wrong-target",999,true));Assert.Equal(HttpStatusCode.Conflict,mismatch.StatusCode);Assert.Contains("E-WF-040",await mismatch.Content.ReadAsStringAsync());
        using var unconfirmed=await host.Send(Rebase("unconfirmed",publication.Version,false));Assert.Equal(HttpStatusCode.UnprocessableEntity,unconfirmed.StatusCode);Assert.Contains("E-WF-048",await unconfirmed.Content.ReadAsStringAsync());
        using var changed=await host.Send(Rebase("exact-rebase",publication.Version,true));var result=await PersonalHttpResult<DraftRebaseResult>(changed);
        Assert.Contains("old",result.RemovedFields);Assert.Contains("FORM_VALIDATION_FAILED",result.ValidationErrors);Assert.Contains("9007199254740993.0001",result.DataJson);
        using var replay=await host.Send(Rebase("exact-rebase",publication.Version,true));var original=await PersonalHttpResult<DraftRebaseResult>(replay);
        Assert.Equal(result.RowVersion,original.RowVersion);Assert.Equal(result.DataJson,original.DataJson);
        var policy=(await owner.PolicyAsync("personal"))!;await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){["old"]="hidden"})],policy.RetentionPolicyRef,30,true,"current source disclosure"));
        Assert.Empty((await owner.LegacyRebaseResultAsync(draft.DraftId,"exact-rebase")).RemovedFields);
        Assert.Equal(0,(await owner.DraftAsync(draft.DraftId)).Status);Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task LegacyPersonalForm_HTTP_history_is_owned_metadata_and_old_submission_keys_are_never_new_native_intents()
    {
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");
        async Task<(Guid Draft,Guid Version)> Historical(CP6.Core.EFDbContext.CP6Context db,Guid actor,string key)
        {
            var head=new Wf_FormDef{Id=Guid.NewGuid(),FormKey=key,FormName="recorded form",SchemaJson=FormAuthoringSchema,Version=3};db.Wf_FormDefs.Add(head);await db.SaveChangesAsync();
            var version=new Wf_FormDefVersion{Id=Guid.NewGuid(),FormDefId=head.Id,Version=3,Status=1,FormNameSnapshot="backfilled",SchemaJson=FormAuthoringSchema,Creator="oa-p0-backfill"};db.Wf_FormDefVersions.Add(version);await db.SaveChangesAsync();
            var draft=new Wf_FormDraft{Id=Guid.NewGuid(),OwnerUserId=actor,FormDefId=head.Id,FormDefVersionId=version.Id,Title="historical title",DataJson="{\"a\":\"historical private content\"}"};db.Wf_FormDrafts.Add(draft);await db.SaveChangesAsync();db.ChangeTracker.Clear();return(draft.Id,version.Id);
        }
        var historical=await Historical(h.Db,h.User,"historical");await Historical(h.Db,Guid.NewGuid(),"another-owner");
        await using var other=await Seed();await Historical(other.Db,h.User,"another-tenant");
        h.Db.Wf_FormDatas.Add(new(){Id=Guid.NewGuid(),FormDefVersionId=historical.Version,FormKey="historical",FormVersion=3,SubmissionKey="archived-key",RequestHash=new string('a',64),SubmittedBy=h.User,SubmittedAtUtc=DateTime.UtcNow,DataJson="{\"a\":\"historical private content\"}"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});var cookies=await Login(host,h);
        using var response=await host.Send(BoundRequest(cookies,"/api/oa/legacy-drafts"));var text=await response.Content.ReadAsStringAsync();Assert.DoesNotContain("historical private content",text);
        var page=await PersonalHttpResult<LegacyPersonalDraftPageV1>(response);var item=Assert.Single(page.Items);Assert.Equal(historical.Draft,item.Id);Assert.Equal("LEGACY_HEAD_BACKFILL",item.Source);Assert.False(item.CanWrite);
        using var current=await host.Send(BoundRequest(cookies,"/api/oa/drafts"));Assert.Equal(0,(await PersonalHttpResult<JsonElement>(current)).GetProperty("total").GetInt32());
        using var original=await host.Send(BoundRequest(cookies,"/api/oa/legacy-form-commands/FORM_DIRECT_SUBMIT/personal/archived-key"));Assert.Equal(HttpStatusCode.Conflict,original.StatusCode);Assert.Contains("LEGACY_SUBMISSION_MAPPING_REQUIRED",await original.Content.ReadAsStringAsync());
        using var duplicate=await host.Send(LegacyFormHttp(cookies,"/api/oa/forms/personal/submissions",new{data=PersonalData("{\"a\":\"new input\"}"),draftId=(Guid?)null},"archived-key",context));Assert.Equal(HttpStatusCode.Conflict,duplicate.StatusCode);
        Assert.Equal(1,await h.Db.Wf_FormDatas.CountAsync());Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.Direct,"personal",PersonalFormOwnerV2.LegacyCommandId("archived-key"))).Outcome);
        Assert.Contains("historical private content",(await h.Db.Wf_FormDrafts.AsNoTracking().SingleAsync(x=>x.Id==historical.Draft)).DataJson);
    }
}
