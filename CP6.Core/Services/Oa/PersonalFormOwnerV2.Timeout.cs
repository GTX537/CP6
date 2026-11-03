using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class PersonalFormOwnerV2
{
    // NativeTimeoutOwner owns service authorization, lease and commit. This participant
    // never borrows an interactive user or writes a human vote.
    internal async Task<NativeTimeoutResultV2> ApplyTimeoutV2(NativeTimeoutResultV2 result,NativeDeadlinePolicyV2 policy,CancellationToken ct)
    {
        var source=await TaskSourceAsync(result.TaskId,ct);var assignment=Parse<PersonalFormAssignmentV2>(source.Assignment);
        var task=await db.Wf_FlowTasks.SingleAsync(t=>t.Id==result.TaskId,ct);var instance=await db.Wf_FlowInstances.SingleAsync(i=>i.Id==task.InstanceId,ct);
        string? blocked=null;
        if(policy.Action=="escalate")
        {
            if(policy.EscalateTo is not Guid target || target==Guid.Empty || target==task.AssigneeId)blocked="TARGET_INELIGIBLE";
            else try{await TargetAuthority(source,target,ct);}
            catch(ScopeAuthorizationExceptionV1 e)when(e.Message is "TARGET_INELIGIBLE" or "ACTION_FORBIDDEN" or "FORM_USE_POLICY_REQUIRED" or "FORM_USE_REVOKED" or "FORM_NOT_FOUND") {blocked=e.Message;}
        }
        var write=result.ExecutionId;var raw=ApprovalCanonicalV2.Bytes(new{result.ExecutionId,result.TaskId,result.Revision,result.Occurrence,policy});
        var command=new Command<NativeDeadlinePolicyV2>(result.ExecutionId.ToString("D"),null,policy,ApprovalCanonicalV2.Hash(raw),raw,null);
        await BeginCommand(write,result.ServiceId,policy.Action=="escalate"&&blocked==null?Transfer:"FORM_TIMEOUT",task.Id.ToString("D"),command,null,source.Data,source.Root,task.Id,ct);
        var engine=new FlowEngine(db,new ApproverResolver(db,exactConditions:true));
        var root=await db.Wf_FlowInstances.SingleAsync(i=>i.Id==source.Root,ct);
        if(blocked!=null)
        {
            result=result with{State="BLOCKED_TARGET",Reason=blocked};
            await Notification(write,"TIMEOUT_BLOCKED",instance.StarterId,WfNotificationType.TodoCreated,"超时升级受阻：目标无当前办理资格",instance.Id,task.Id,instance.FlowKey,ct);
        }
        else if(policy.Action=="remind")
            await Notification(write,"TIMEOUT_REMINDER",task.AssigneeId,WfNotificationType.TodoCreated,"待办已超时，请处理",instance.Id,task.Id,instance.FlowKey,ct);
        else if(policy.Action=="escalate")
        {
            var target=policy.EscalateTo!.Value;
            var changed=assignment with{PrincipalId=target,CreatedPrincipalId=assignment.CreatedPrincipalId??assignment.PrincipalId,
                AssignmentRevision=assignment.AssignmentRevision+1,Candidates=[target],Rule=new(ApproverStrategy.Specified,null,null,target),AssignedAt=result.OccurredAt};
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_personal_form_v2.assignment_changes(task_id,revision,command_id,body) VALUES({task.Id},{changed.AssignmentRevision},{write},{Encode(changed)})",ct);
            await engine.StageNativeTransferV2(task,target,result.ServiceId,null,"超时升级（SYSTEM_EXECUTION）",ct);
            instance.Modifier=result.ServiceId.ToString("D");instance.ModifyDate=DateTime.Now;
            await Notification(write,"TASK_TRANSFERRED",target,WfNotificationType.TodoCreated,"您有新的超时升级待办",instance.Id,task.Id,instance.FlowKey,ct);
        }
        else
        {
            var publication=Parse<FlowPublicationV2>(source.Publication);
            if(policy.Action is "approve" or "reject")root=await engine.StagePersonalDecisionV2(fence.EnvironmentId,source.Data,source.Root,publication,
                assignment,result.ServiceId,null,policy.Action=="approve","超时系统决定（SYSTEM_EXECUTION）",ct);
            else if(policy.Action=="errorEdge")root=await engine.StagePersonalTimeoutErrorV2(fence.EnvironmentId,source.Data,source.Root,publication,task.Id,result.ServiceId,ct);
            else throw Error("TIMEOUT_INVALID_POLICY",422);
            await AssignmentNotifications(write,await engine.SavePersonalRuntime(write,ct),ct);
            await Notification(write,"TIMEOUT_EXECUTED",instance.StarterId,WfNotificationType.TaskHandled,"超时政策已执行",instance.Id,task.Id,instance.FlowKey,ct);
            if(root.Status is FlowInstanceStatus.Approved or FlowInstanceStatus.Rejected)await TerminalNotification(write,root,ct);
        }
        if(blocked==null)result=result with{State="COMPLETED",Reason=null};
        result=result with{CommandId=write};
        await db.SaveChangesAsync(ct);
        await Complete(write,command,task.Id,(await TaskView(task.Id,ct)).TaskRowVersion,result,
            new{EvidenceKind="SYSTEM_EXECUTION",Trigger="TIMEOUT",result.ServiceId,result.ExecutionId,result.OriginalTaskVersion,result.OriginalInstanceVersion,Policy=policy,OriginalPrincipal=assignment.PrincipalId},ct);
        return result;
    }
}
