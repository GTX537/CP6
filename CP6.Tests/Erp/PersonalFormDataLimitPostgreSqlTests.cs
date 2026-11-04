using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.Contracts.Wire;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static (string Schema,JsonElement Data) FormDataAtLimit(int bytes=1024*1024,string fill="x")
    {
        var names=Enumerable.Range(0,110).Select(i=>"text"+i).ToArray();
        var fields=names.Select(n=>(object)new{name=n,type="input",maxLength=10000}).Append(new{name="amount",type="number"}).ToArray();
        var data=new JsonObject{["amount"]=JsonNode.Parse("9007199254740993.0001")};
        foreach(var name in names)data[name]="";
        var remaining=bytes-Encoding.UTF8.GetByteCount(data.ToJsonString());
        var width=Encoding.UTF8.GetByteCount(fill);
        foreach(var name in names){var length=Math.Min(remaining/width,10000/fill.Length);data[name]=string.Concat(Enumerable.Repeat(fill,length));remaining-=length*width;}
        if(remaining>0){data[names[^1]]=data[names[^1]]!.GetValue<string>()+new string('x',remaining);remaining=0;}
        Assert.Equal(0,remaining);
        var json="{"+string.Join(",",data.Select(p=>WorkflowCommandJsonV1.Quote(p.Key)+":"+(p.Value!.GetValueKind()==JsonValueKind.String?WorkflowCommandJsonV1.Quote(p.Value.GetValue<string>()):p.Value.ToJsonString())))+"}";
        Assert.Equal(bytes,Encoding.UTF8.GetByteCount(json));
        return(JsonSerializer.Serialize(new{fields}),PersonalData(json));
    }

    [CurrentPermissionPostgresFact]
    public async Task PersonalDataLimit_native_owner_accepts_full_data_budget_preserves_digest_and_rejects_excess_without_effect()
    {
        var source=FormDataAtLimit();await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h,source.Schema);var context=await owner.ContextAsync("personal");
        var input=PersonalDirect(context) with{Data=source.Data};var raw=PersonalCommand(input,"owner-large");
        Assert.True(raw.Length>1024*1024);
        // Unrelated WFC callers keep their existing generic default. Only the FORM Owner allocates envelope overhead.
        Assert.Throws<JsonException>(()=>{using var rejected=WorkflowCommandJsonV1.Parse(raw);});
        var accepted=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal",raw));
        Assert.Equal(accepted,PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal",raw)));
        var stored=await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync();
        Assert.Equal(source.Data.GetRawText(),stored.DataJson);Assert.Equal(WorkflowCommandJsonV1.Digest(JsonSerializer.SerializeToElement(input,PersonalFormOwnerV2.Json),null),stored.RequestHash);
        var tooLarge=FormDataAtLimit(1024*1024+1).Data;
        Assert.Equal("FORM_DATA_INVALID",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDirectAsync("personal",PersonalCommand(input with{Data=tooLarge},"over-limit")))).Message);
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.Direct,"personal","over-limit")).Outcome);
        Assert.Equal(accepted.FormDataId,(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).Id);
    }

    [CurrentPermissionPostgresFact]
    public async Task PersonalDataLimit_HTTP_direct_preview_create_save_and_draft_submit_accept_full_budget_and_keep_original()
    {
        var source=FormDataAtLimit();await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h,source.Schema);var context=await owner.ContextAsync("personal");
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);const string root="/api/oa/v2";
        var direct=PersonalCommand(PersonalDirect(context) with{Data=source.Data},"http-large");
        using var sent=await host.Send(PersonalHttp(cookies,root+"/forms/personal/submissions",direct));
        var accepted=PersonalResult<PersonalFormSubmissionV2>(await PersonalHttpResult<PersonalFormResultV2>(sent));
        using var original=await host.Send(BoundRequest(cookies,root+"/personal-forms/commands/FORM_DIRECT_SUBMIT/personal/http-large"));
        Assert.Equal(accepted,PersonalResult<PersonalFormSubmissionV2>(await PersonalHttpResult<PersonalFormResultV2>(original)));
        using var preview=await host.Send(PersonalHttp(cookies,root+"/forms/personal/preview",JsonSerializer.SerializeToUtf8Bytes(new PersonalFormPreviewInputV2(context.FormVersionId,source.Data),PersonalFormOwnerV2.Json)));
        Assert.Equal(source.Data.GetRawText(),(await PersonalHttpResult<PersonalFormPreviewV2>(preview)).DataJson);
        var create=PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,source.Data,new string('t',200)),"large-create");
        using var created=await host.Send(PersonalHttp(cookies,root+"/forms/personal/drafts",create));
        var draft=PersonalResult<PersonalFormDraftV2>(await PersonalHttpResult<PersonalFormResultV2>(created));
        using var saveRequest=PersonalHttp(cookies,root+"/drafts/"+draft.DraftId,PersonalCommand(new PersonalFormSaveV2(source.Data,"saved full data"),"large-save",draft.RowVersion));saveRequest.Method=HttpMethod.Put;
        using var saved=await host.Send(saveRequest);var updated=PersonalResult<PersonalFormDraftV2>(await PersonalHttpResult<PersonalFormResultV2>(saved));
        Assert.Equal(source.Data.GetRawText(),updated.DataJson);
        using var draftPreview=await host.Send(PersonalHttp(cookies,root+"/drafts/"+draft.DraftId+"/preview",JsonSerializer.SerializeToUtf8Bytes(new PersonalFormPreviewInputV2(context.FormVersionId,source.Data),PersonalFormOwnerV2.Json)));
        Assert.Equal(source.Data.GetRawText(),(await PersonalHttpResult<PersonalFormPreviewV2>(draftPreview)).DataJson);
        using var submitted=await host.Send(PersonalHttp(cookies,root+"/drafts/"+draft.DraftId+"/submit",PersonalCommand(PersonalSubmit(context),"large-draft-submit",updated.RowVersion)));
        var second=PersonalResult<PersonalFormSubmissionV2>(await PersonalHttpResult<PersonalFormResultV2>(submitted));Assert.NotEqual(accepted.FormDataId,second.FormDataId);
        using var excess=await host.Send(PersonalHttp(cookies,root+"/forms/personal/submissions",PersonalCommand(PersonalDirect(context) with{Data=FormDataAtLimit(1024*1024+1).Data},"http-over-limit")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,excess.StatusCode);Assert.Contains("FORM_DATA_INVALID",await excess.Content.ReadAsStringAsync());
        Assert.Equal(2,await h.Db.Wf_FormDatas.CountAsync());Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.Direct,"personal","http-over-limit")).Outcome);
    }

    [CurrentPermissionPostgresFact]
    public async Task PersonalDataLimit_HTTP_returned_and_SFS_patches_keep_full_legal_data_and_original_history()
    {
        var source=FormDataAtLimit();await using var h=await Seed();var f=await NativeFormSetup(h,formSchema:source.Schema,values:"{}");await NativeOperationPermissions(h);
        await f.Owner.SendBackAsync(f.Task,SendBackCommand(await f.Owner.SendBackPreviewAsync(f.Task,new("STARTER",null))));
        var returned=await f.Owner.ReturnedFormAsync(f.Instance);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);const string root="/api/oa/v2";
        var raw=PersonalCommand(new ReturnedFormEditV2(returned.ReturnRevision,returned.FormDataVersion,source.Data,"full legal returned patch"),"large-returned",returned.InstanceVersion);
        using var editRequest=PersonalHttp(cookies,root+"/instances/"+f.Instance+"/returned-form",raw);editRequest.Method=HttpMethod.Put;
        using var edited=await host.Send(editRequest);var value=PersonalResult<ReturnedFormResultV2>(await PersonalHttpResult<PersonalFormResultV2>(edited));
        Assert.Equal(source.Data.GetRawText(),(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson);
        using var replay=await host.Send(BoundRequest(cookies,root+"/personal-forms/commands/SFS_RETURNED_EDIT/"+f.Instance+"/large-returned"));
        var prior=PersonalResult<ReturnedFormResultV2>(await PersonalHttpResult<PersonalFormResultV2>(replay));Assert.Equal(value.FormDataVersion,prior.FormDataVersion);
        await f.Owner.ResubmitReturnedAsync(f.Instance,PersonalCommand(new ReturnedFormResubmitV2(returned.ReturnRevision,value.FormDataVersion,null),"resume-large",value.InstanceVersion));
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Status==0);var context=await f.Owner.TaskContextAsync(task.Id);
        var changed=JsonNode.Parse(source.Data.GetRawText())!.AsObject();changed["text0"]=new string('y',10000);var patch=PersonalData(changed.ToJsonString());
        var vote=SfsInput(context) with{DataPatch=patch};
        using var decision=await host.Send(PersonalHttp(cookies,root+"/personal-forms/tasks/"+task.Id+"/decisions",SfsCommand(vote,"large-vote")));
        var result=PersonalResult<SfsTaskDecisionResultV2>(await PersonalHttpResult<PersonalFormResultV2>(decision));
        Assert.Equal(1,result.Task.InstanceStatus);Assert.Equal(changed.ToJsonString(),(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson);
        using var originalVote=await host.Send(BoundRequest(cookies,root+"/personal-forms/commands/SFS_TASK_DECISION/"+task.Id+"/large-vote"));
        Assert.Equal(result,PersonalResult<SfsTaskDecisionResultV2>(await PersonalHttpResult<PersonalFormResultV2>(originalVote)));
        Assert.Equal("{}",await h.Db.Database.SqlQuery<string>($"SELECT snapshot_json AS \"Value\" FROM cp6_personal_form_v2.returns WHERE instance_id={f.Instance}").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task PersonalDataLimit_HTTP_full_UTF8_data_is_not_rejected_by_internal_unicode_escape_expansion()
    {
        var source=FormDataAtLimit(fill:"中");await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h,source.Schema);var context=await owner.ContextAsync("personal");
        var raw=JsonSerializer.SerializeToUtf8Bytes(new{commandId="utf8-full",expectedVersion=(string?)null,body=PersonalDirect(context) with{Data=source.Data}},
            new JsonSerializerOptions(PersonalFormOwnerV2.Json){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
        Assert.InRange(raw.Length,1024*1024,PersonalFormOwnerV2.DataRequestMaxBytes);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);
        using var response=await host.Send(PersonalHttp(cookies,"/api/oa/v2/forms/personal/submissions",raw));
        var accepted=PersonalResult<PersonalFormSubmissionV2>(await PersonalHttpResult<PersonalFormResultV2>(response));
        var stored=await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync();Assert.Equal(source.Data.GetRawText(),stored.DataJson);Assert.Equal(accepted.FormDataId,stored.Id);
        Assert.Equal(accepted,PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal",raw)));
    }

    [Theory]
    [InlineData("😀")]
    [InlineData("\uE000")]
    public void PersonalDataLimit_full_Unicode_values_survive_merge_patch_rules_and_projection(string fill)
    {
        var source=FormDataAtLimit(fill:fill);var schema=PersonalFormValuesV2.Schema(source.Schema);
        var mask=schema.Fields.ToDictionary(f=>f.Name,_=>"edit");var original=source.Data.GetRawText();
        Assert.Equal(original,PersonalFormValuesV2.Merge(schema,source.Data,null,mask));
        Assert.Equal(original,PersonalFormValuesV2.Patch(schema,source.Data,"{}",mask));
        Assert.Equal(original,PersonalFormValuesV2.Recompute(schema,original).Data);
        Assert.Equal(original,PersonalFormValuesV2.Project(original,mask));
        // A small legal patch still cannot make the combined persisted object exceed its budget.
        Assert.Equal("FORM_DATA_INVALID",Assert.Throws<ScopeAuthorizationExceptionV1>(()=>
            PersonalFormValuesV2.Patch(schema,PersonalData("{\"text109\":\"too large when combined\"}"),original,mask)).Message);
    }

    [Fact]
    public void PersonalDataLimit_projection_preserves_nested_strings_controls_exact_numbers_and_json_kinds()
    {
        const string input="""{"rows":[{"q\"\n":"😀中\b\f\n\r\t\u0000\"\\</script>","n":9007199254740993.0001},null,false,true],"nil":null,"empty":""}""";
        var actual=PersonalFormValuesV2.Project(input,new Dictionary<string,string>{{"rows","edit"},{"nil","edit"},{"empty","edit"}});
        using var before=JsonDocument.Parse(input);using var after=JsonDocument.Parse(actual);
        Assert.Equal(WorkflowCommandJsonV1.Canonical(before.RootElement),WorkflowCommandJsonV1.Canonical(after.RootElement));
        Assert.True(Encoding.UTF8.GetByteCount(actual)<=Encoding.UTF8.GetByteCount(input));
    }
}
