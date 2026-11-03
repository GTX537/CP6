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
    public async Task NativeTask_returned_patch_keeps_omitted_hidden_precise_values_and_pinned_start_mask_then_validates_required()
    {
        const string schema="""{"fields":[{"name":"a","type":"input","required":true},{"name":"amount","type":"number"},{"name":"secret","type":"input"},{"name":"fixed","type":"input"}]}""";
        await using var h=await Seed();var(owner,form,flow)=await PreparePersonal(h,schema);await NativeOperationPermissions(h);
        var graph=LinearNative(h.User);graph.Nodes[0].FieldPerms=new(){["fixed"]="readonly"};
        var save=await flow.SaveAsync("patch",DependentCommand("personal",schema:JsonSerializer.Serialize(graph,PersonalFormOwnerV2.Json)));
        var publication=(await flow.PublishAsync("patch",PublicationCommand(await Preview(flow,"patch",save)))).Result!.Publication!;
        await flow.SetActivationAsync("patch",ActivationCommand((await flow.ActivationPreviewAsync("patch")).Current,publication.VersionId));
        var data="""{"a":"original","amount":9007199254740993.123456789,"secret":"original hidden","fixed":"pinned readonly"}""";
        var submitted=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(await owner.ContextAsync("personal"),data),"patch-start")));
        var task=(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Id;var root=submitted.FlowInstanceId!.Value;
        await owner.SendBackAsync(task,SendBackCommand(await owner.SendBackPreviewAsync(task,new("STARTER",null))));
        var policy=(await owner.PolicyAsync("personal"))!;
        await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){["secret"]="hidden"})],policy.RetentionPolicyRef,30,true,"restrict current returned fields"));
        var c=await owner.ReturnedFormAsync(root);Assert.Equal("readonly",c.FieldMask["fixed"]);Assert.DoesNotContain("original hidden",c.DataJson);
        async Task<PersonalFormResultV2> Edit(string patch,string id)=>await owner.EditReturnedAsync(root,PersonalCommand(new ReturnedFormEditV2(c.ReturnRevision,c.FormDataVersion,PersonalData(patch),"returned patch"),id,c.InstanceVersion));
        Assert.Equal("FIELD_WRITE_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Edit("{\"fixed\":\"pinned readonly\"}","readonly"))).Message);
        Assert.Equal("FIELD_WRITE_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>Edit("{\"secret\":null}","hidden"))).Message);
        var saved=PersonalResult<ReturnedFormResultV2>(await Edit("{\"a\":null}","partial"));Assert.Contains("FORM_VALIDATION_FAILED",saved.ValidationErrors);
        using(var current=JsonDocument.Parse((await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson))
        {Assert.Equal("9007199254740993.123456789",current.RootElement.GetProperty("amount").GetRawText());Assert.Equal("original hidden",current.RootElement.GetProperty("secret").GetString());Assert.Equal(JsonValueKind.Null,current.RootElement.GetProperty("a").ValueKind);}
        Assert.Equal(422,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.ResubmitReturnedAsync(root,
            PersonalCommand(new ReturnedFormResubmitV2(c.ReturnRevision,saved.FormDataVersion,null),"invalid",saved.InstanceVersion)))).Status);
        c=await owner.ReturnedFormAsync(root);var valid=PersonalResult<ReturnedFormResultV2>(await Edit("{\"a\":\"corrected\"}","correct"));
        var flowActivation=(await flow.ActivationPreviewAsync("patch")).Current;
        await flow.SetActivationAsync("patch",ActivationCommand(flowActivation,publication.VersionId,false,"stop-new-starts"));
        var formActivation=(await form.ActivationPreviewAsync("personal")).Current;
        await form.SetActivationAsync("personal",FormActivationCommand(formActivation,submitted.FormDefVersionId,false,"stop-new-forms"));
        var resumed=PersonalResult<ReturnedFormResultV2>(await owner.ResubmitReturnedAsync(root,PersonalCommand(new ReturnedFormResubmitV2(c.ReturnRevision,valid.FormDataVersion,null),"same-pin",valid.InstanceVersion)));
        Assert.Equal(FlowInstanceStatus.Running,resumed.InstanceStatus);Assert.Equal(root,resumed.InstanceId);
        Assert.Equal("RETURNED_CURRENT_CYCLE",(await NativeInbox(h).DetailAsync(root)).Form!.DataSource);
        Assert.Equal(data,await h.Db.Database.SqlQuery<string>($"SELECT snapshot_json AS \"Value\" FROM cp6_personal_form_v2.returns WHERE instance_id={root}").SingleAsync());
    }
}
