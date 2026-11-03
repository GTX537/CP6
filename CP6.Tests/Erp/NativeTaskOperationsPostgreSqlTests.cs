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
    private async Task NativeOperationPermissions(Harness h)
    {
        foreach(var action in new[]{"sendback","transfer","addsign","returned-edit","resubmit","resolve-approver"})
            await EngineeringAction(h,"oa-inbox",action);
    }
    private static byte[] SendBackCommand(NativeSendBackPreviewV2 p,string id="return")=>PersonalCommand(
        new NativeSendBackInputV2(p.TargetKind,p.TargetNodeId,p.PreviewDigest,p.TaskVersion,"return with exact preview"),id,p.InstanceVersion);

    [CurrentPermissionPostgresFact]
    public async Task NativeTask_transfer_preserves_created_duty_and_deadline_and_new_principal_can_decide()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);await NativeOperationPermissions(h);var target=await GrantDelegate(h);
        var before=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync();var context=await f.Owner.TaskContextAsync(f.Task);
        var raw=PersonalCommand(new NativeTransferInputV2(f.Task,target,"qualified target",context.Task.TaskRowVersion),"transfer",context.Task.InstanceRowVersion);
        var result=await f.Owner.TransferTaskAsync(f.Task,raw);var after=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync();
        Assert.Equal(target,after.AssigneeId);Assert.Equal(before.TokenId,after.TokenId);Assert.Equal(before.StageRound,after.StageRound);Assert.Equal(before.DueAt,after.DueAt);
        Assert.Equal(h.User,await h.Db.Database.SqlQuery<Guid>($"SELECT (body::jsonb->>'principalId')::uuid AS \"Value\" FROM cp6_personal_form_v2.assignments WHERE task_id={f.Task}").SingleAsync());
        Assert.Equal(h.User,await h.Db.Database.SqlQuery<Guid>($"SELECT (body::jsonb->>'createdPrincipalId')::uuid AS \"Value\" FROM cp6_personal_form_v2.assignment_changes WHERE task_id={f.Task}").SingleAsync());
        Assert.Equal(JsonSerializer.Serialize(result),JsonSerializer.Serialize(await f.Owner.TransferTaskAsync(f.Task,raw)));
        Assert.Equal(404,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.TaskContextAsync(f.Task))).Status);
        TaskActor(h,target);var targetOwner=PersonalForm(h with{User=target});var current=await targetOwner.TaskContextAsync(f.Task);
        var vote=NativeVote(current,"authorized transferee","approve");
        Assert.Equal(FlowInstanceStatus.Approved,(await DecideNative(targetOwner,f.Task,vote)).Task!.InstanceStatus);
        Assert.Contains(await h.Db.Wf_FlowFormTos.AsNoTracking().ToArrayAsync(),f=>f.Status==FlowFormToStatus.Transferred&&f.ExpectedHandlerId==h.User&&f.ActualHandlerId==h.User);
        Assert.Contains(await h.Db.Wf_FlowFormTos.AsNoTracking().ToArrayAsync(),f=>f.Status==FlowFormToStatus.Approved&&f.ExpectedHandlerId==target&&f.ActualHandlerId==target);
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeTask_before_addsign_reactivates_only_parent_and_preserves_created_assignments()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h,true);await NativeOperationPermissions(h);var target=await GrantDelegate(h);
        var context=await f.Owner.TaskContextAsync(f.Task);
        var raw=PersonalCommand(new NativeAddSignInputV2(f.Task,target,"before","additional review",context.Task.TaskRowVersion),"addsign",context.Task.InstanceRowVersion);
        var result=await f.Owner.AddSignTaskAsync(f.Task,raw);var added=result.Result!.Value.GetProperty("task").GetProperty("taskId").GetGuid();
        Assert.Equal(FlowTaskStatus.Suspended,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==f.Task)).Status);
        Assert.Equal(JsonSerializer.Serialize(result),JsonSerializer.Serialize(await f.Owner.AddSignTaskAsync(f.Task,raw)));
        TaskActor(h,target);var owner=PersonalForm(h with{User=target});var c=await owner.TaskContextAsync(added);
        var vote=NativeVote(c,"additional review complete","approve");
        Assert.Equal(FlowInstanceStatus.Running,(await DecideNative(owner,added,vote)).Task!.InstanceStatus);
        Assert.Equal(FlowTaskStatus.Pending,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==f.Task)).Status);
        Assert.Equal(2,await h.Db.Wf_FlowTasks.CountAsync(t=>t.Status==FlowTaskStatus.Pending));
    }

    [CurrentPermissionPostgresFact]
    public async Task NativeTask_return_edit_resubmit_keeps_identity_pins_and_original_results_with_new_cycle()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);await NativeOperationPermissions(h);
        var old=await f.Owner.TaskContextAsync(f.Task);var data=await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync();
        var preview=await f.Owner.SendBackPreviewAsync(f.Task,new("STARTER",null));
        Assert.True(preview.WholeInstanceRefill);Assert.Contains(f.Task,preview.AffectedTaskIds);
        var command=SendBackCommand(preview);var returned=await f.Owner.SendBackAsync(f.Task,command);
        Assert.Equal(FlowInstanceStatus.Draft,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(FlowTaskStatus.Cancelled,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(JsonSerializer.Serialize(returned,PersonalFormOwnerV2.Json),JsonSerializer.Serialize(await f.Owner.SendBackAsync(f.Task,command),PersonalFormOwnerV2.Json));
        var context=await f.Owner.ReturnedFormAsync(f.Instance);Assert.True(context.CanEdit);Assert.True(context.CanResubmit);
        Assert.Equal(data.Id,context.FormDataId);Assert.Equal(old.FormVersionId,context.FormDefVersionId);Assert.Equal(f.Instance,context.ExecutionCycleId);
        var edit=PersonalCommand(new ReturnedFormEditV2(context.ReturnRevision,context.FormDataVersion,PersonalData("{\"a\":\"edited\"}"),"correct data"),"edit-return",context.InstanceVersion);
        var edited=await f.Owner.EditReturnedAsync(f.Instance,edit);var editedValue=PersonalResult<ReturnedFormResultV2>(edited);
        Assert.Equal(FlowInstanceStatus.Draft,editedValue.InstanceStatus);Assert.NotEqual(context.FormDataVersion,editedValue.FormDataVersion);
        Assert.NotEqual(context.InstanceVersion,editedValue.InstanceVersion);Assert.Empty(editedValue.TaskIds);
        Assert.Equal("RETURN_CONTEXT_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.EditReturnedAsync(f.Instance,
            PersonalCommand(new ReturnedFormEditV2(context.ReturnRevision,context.FormDataVersion,PersonalData("{}"),null),"stale",context.InstanceVersion)))).Message);
        var submit=PersonalCommand(new ReturnedFormResubmitV2(context.ReturnRevision,editedValue.FormDataVersion,"resubmit"),"resubmit",editedValue.InstanceVersion);
        var resumed=await f.Owner.ResubmitReturnedAsync(f.Instance,submit);var resumedValue=PersonalResult<ReturnedFormResultV2>(resumed);
        Assert.NotEqual(context.ExecutionCycleId,resumedValue.ExecutionCycleId);Assert.Equal(f.Instance,resumedValue.InstanceId);
        Assert.Equal(data.Id,resumedValue.FormDataId);Assert.Equal(FlowInstanceStatus.Running,resumedValue.InstanceStatus);
        var task=Assert.Single(resumedValue.TaskIds);Assert.NotEqual(f.Task,task);
        Assert.Equal(JsonSerializer.Serialize(resumed,PersonalFormOwnerV2.Json),JsonSerializer.Serialize(await f.Owner.ResubmitReturnedAsync(f.Instance,submit),PersonalFormOwnerV2.Json));
        Assert.Equal(JsonSerializer.Serialize(edited,PersonalFormOwnerV2.Json),JsonSerializer.Serialize(await f.Owner.EditReturnedAsync(f.Instance,edit),PersonalFormOwnerV2.Json));
        Assert.Equal(JsonSerializer.Serialize(edited,PersonalFormOwnerV2.Json),JsonSerializer.Serialize(await f.Owner.OriginalAsync("SFS_RETURNED_EDIT",f.Instance.ToString(),"edit-return"),PersonalFormOwnerV2.Json));
        Assert.Equal(JsonSerializer.Serialize(returned,PersonalFormOwnerV2.Json),JsonSerializer.Serialize(await f.Owner.OriginalAsync("OA_TASK_SENDBACK",f.Task.ToString(),"return"),PersonalFormOwnerV2.Json));
        var current=await f.Owner.TaskContextAsync(task);Assert.Contains("edited",current.DataJson);
        Assert.Equal(context.FormDefVersionId,current.FormVersionId);
        var decision=NativeVote(current,"new cycle only","approve");
        var finished=await DecideNative(f.Owner,task,decision);
        Assert.Equal(FlowInstanceStatus.Approved,finished.Task!.InstanceStatus);
        Assert.Equal(1,await h.Db.Wf_FlowInstances.CountAsync());Assert.Equal(1,await h.Db.Wf_FormDatas.CountAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.votes WHERE task_id={task}").SingleAsync());
        Assert.Equal(data.DataJson,await h.Db.Database.SqlQuery<string>($"SELECT snapshot_json AS \"Value\" FROM cp6_personal_form_v2.returns WHERE instance_id={f.Instance}").SingleAsync());
        Assert.Equal(JsonSerializer.Serialize(resumed,PersonalFormOwnerV2.Json),JsonSerializer.Serialize(await f.Owner.ResubmitReturnedAsync(f.Instance,submit),PersonalFormOwnerV2.Json));
    }
}
