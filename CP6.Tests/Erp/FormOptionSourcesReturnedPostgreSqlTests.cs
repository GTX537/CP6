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
    [CurrentPermissionPostgresFact]
    public async Task FormOptions_returned_edit_retains_old_value_resubmit_rechecks_and_explicit_correction_uses_same_pins()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);var(owner,_,flow)=await PreparePersonal(h,OptionFormSchema(type));await NativeOperationPermissions(h);
        var graph=JsonSerializer.Serialize(new{start="s",nodes=new object[]{new{id="s",type="start"},new{id="a",type="approval",approverStrategy="Specified",approverUserId=h.User},new{id="e",type="end"}},edges=new[]{new{from="s",to="a"},new{from="a",to="e"}}});
        var saved=await flow.SaveAsync("options-return",DependentCommand("personal",schema:graph));var publication=(await flow.PublishAsync("options-return",PublicationCommand(await Preview(flow,"options-return",saved)))).Result!.Publication!;
        await flow.SetActivationAsync("options-return",ActivationCommand((await flow.ActivationPreviewAsync("options-return")).Current,publication.VersionId));
        var context=await owner.ContextAsync("personal");var submitted=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,"{\"choice\":\"A\"}"),"start")));
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync();var returned=await owner.SendBackPreviewAsync(task.Id,new("STARTER",null));await owner.SendBackAsync(task.Id,SendBackCommand(returned));
        var instance=submitted.FlowInstanceId!.Value;var source=await owner.ReturnedFormAsync(instance);
        var query=new FormOptionQueryV1(new("RETURNED",source.FormDefVersionId,Id:instance),["choice"]);Assert.Equal(4,(await owner.OptionsAsync(query)).Items.Length);
        var code=(await h.Db.Sys_DictTypes.AsNoTracking().SingleAsync(t=>t.Id==type)).TypeCode;var item=(await h.Db.Sys_DictDatas.AsNoTracking().Where(d=>d.TypeCode==code).ToArrayAsync()).Single(d=>d.Value=="A");
        await h.Db.Sys_DictDatas.Where(d=>d.Id==item.Id).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Enable,false));
        var edited=PersonalResult<ReturnedFormResultV2>(await owner.EditReturnedAsync(instance,PersonalCommand(new ReturnedFormEditV2(source.ReturnRevision,source.FormDataVersion,PersonalData("{\"a\":\"unrelated\"}"),null),"retain",source.InstanceVersion)));
        Assert.Contains("\"choice\":\"A\"",(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson);
        Assert.Equal("FORM_OPTION_VALUE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.ResubmitReturnedAsync(instance,PersonalCommand(new ReturnedFormResubmitV2(source.ReturnRevision,edited.FormDataVersion,null),"reject-stale",edited.InstanceVersion)))).Message);
        var corrected=PersonalResult<ReturnedFormResultV2>(await owner.EditReturnedAsync(instance,PersonalCommand(new ReturnedFormEditV2(source.ReturnRevision,edited.FormDataVersion,PersonalData("{\"choice\":\"1.0\"}"),null),"correct",edited.InstanceVersion)));
        var resumed=PersonalResult<ReturnedFormResultV2>(await owner.ResubmitReturnedAsync(instance,PersonalCommand(new ReturnedFormResubmitV2(source.ReturnRevision,corrected.FormDataVersion,null),"accept",corrected.InstanceVersion)));
        Assert.Equal(instance,resumed.InstanceId);Assert.Equal(submitted.FormDataId,resumed.FormDataId);Assert.Single(resumed.TaskIds);
        var current=await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync();Assert.Equal(context.FormVersionId,current.FormDefVersionId);Assert.Equal(context.FlowVersionId,current.FlowDefVersionId);Assert.Equal(FlowInstanceStatus.Running,current.Status);
        Assert.Contains("\"choice\":\"A\"",await h.Db.Database.SqlQuery<string>($"SELECT snapshot_json AS \"Value\" FROM cp6_personal_form_v2.returns WHERE instance_id={instance}").SingleAsync());
        Assert.Equal("ALREADY_RESUBMITTED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.OptionsAsync(query))).Message);
    }
}
