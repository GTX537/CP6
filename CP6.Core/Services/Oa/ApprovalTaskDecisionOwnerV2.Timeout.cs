using System.Text;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;
public sealed partial class ApprovalTaskDecisionOwnerV2
{
    internal async Task<NativeTimeoutResultV2> ApplyTimeoutV2(NativeTimeoutResultV2 result,NativeDeadlinePolicyV2 policy,CancellationToken ct)
    {
        var source=await ReadSource(result.TaskId,ct);var original=System.Text.Json.JsonSerializer.Deserialize<ApprovalSubmitCommandV2>(source.Payload,ApprovalBindingOwnerV2.Json)!;
        var assignment=await ApprovalTaskAssignmentsV2.Read(db,scope.EnvironmentId,result.TaskId,ct);
        var task=await db.Wf_FlowTasks.SingleAsync(t=>t.Id==result.TaskId,ct);var instance=await db.Wf_FlowInstances.SingleAsync(i=>i.Id==task.InstanceId,ct);
        string? blocked=policy.Action is "approve" or "reject"?"BUSINESS_REQUIRES_HUMAN_EVIDENCE":null;
        if(policy.Action=="escalate")
        {
            if(policy.EscalateTo is not Guid target || target==Guid.Empty || target==task.AssigneeId)blocked="TARGET_INELIGIBLE";
            else try{await AppTarget(original,target,ct);}
            catch(ScopeAuthorizationExceptionV1 e)when(e.Status is 403 or 404 or 422){blocked=e.Message;}
        }
        var raw=ApprovalCanonicalV2.Bytes(new{result.ExecutionId,result.TaskId,result.Revision,result.Occurrence,policy});
        var command=new PersonalFormOwnerV2.Command<NativeDeadlinePolicyV2>(result.ExecutionId.ToString("D"),null,policy,ApprovalCanonicalV2.Hash(raw),raw,null);
        var write=result.ExecutionId;
        await BeginOperation(write,result.ServiceId,policy.Action=="escalate"&&blocked==null?"OA_TASK_TRANSFER":"OA_TASK_TIMEOUT",task.Id,source.Root,task.Id,command,ct);
        var engine=new FlowEngine(db,new ApproverResolver(db,exactConditions:true));
        if(blocked==null&&policy.Action=="errorEdge")
        {
            // A legal error edge may still drain the business root. Without an adopted
            // system terminal contract it cannot replace the required human decision.
            await db.Database.CurrentTransaction!.CreateSavepointAsync("timeout_error_edge",ct);
            var root=await engine.StageAppTimeoutErrorV2(Parse<FlowPublicationV2>(source.Publication),original,task.Id,result.ServiceId,ct);
            if(root.Status is FlowInstanceStatus.Approved or FlowInstanceStatus.Rejected)
            {
                await db.Database.CurrentTransaction.RollbackToSavepointAsync("timeout_error_edge",ct);db.ChangeTracker.Clear();
                blocked="ERROR_EDGE_REQUIRES_BUSINESS_TERMINAL_AUTHORITY";
            }
            else
            {
                await engine.SaveApprovalRuntime(ct);await db.SaveChangesAsync(ct);await engine.SaveApprovalAssignments(ct);
                await engine.SavePendingApproversV2(write,"APP_OPERATION",ct);
                foreach(var created in engine.CreatedApprovalAssignments)
                    await OperationNotification(write,created.PrincipalId,created.InstanceId,created.TaskId,"TASK_ASSIGNED","超时错误边产生新待办",ct);
            }
            await db.Database.CurrentTransaction.ReleaseSavepointAsync("timeout_error_edge",ct);
        }
        if(blocked!=null)
        {
            result=result with{State=policy.Action=="escalate"?"BLOCKED_TARGET":"BLOCKED_POLICY",Reason=blocked};
            await OperationNotification(write,original.StarterId,instance.Id,task.Id,"TIMEOUT_BLOCKED","超时动作受阻，待办仍需办理",ct);
        }
        else
        {
            if(policy.Action=="escalate")
            {
                var target=policy.EscalateTo!.Value;
                var changed=assignment with{PrincipalId=target,CreatedPrincipalId=assignment.CreatedPrincipalId??assignment.PrincipalId,
                    AssignmentRevision=assignment.AssignmentRevision+1,Candidates=[target],Rule=new(ApproverStrategy.Specified,null,null,target),AssignedAt=result.OccurredAt};
                var body=Json(changed);
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_native_tasks_v2.app_assignment_changes(task_id,revision,command_id,body,digest) VALUES({task.Id},{changed.AssignmentRevision},{write},{body},{ApprovalCanonicalV2.Hash(Encoding.UTF8.GetBytes(body))})",ct);
                await engine.StageNativeTransferV2(task,target,result.ServiceId,null,"超时升级（SYSTEM_EXECUTION）",ct);
                instance.Modifier=result.ServiceId.ToString("D");instance.ModifyDate=DateTime.Now;
                await OperationNotification(write,target,instance.Id,task.Id,"TIMEOUT_ESCALATED","您有新的超时升级待办",ct);
            }
            else if(policy.Action=="remind")await OperationNotification(write,task.AssigneeId,instance.Id,task.Id,"TIMEOUT_REMINDER","待办已超时，请处理",ct);
            else if(policy.Action!="errorEdge")throw Error("TIMEOUT_INVALID_POLICY",422);
            result=result with{State="COMPLETED",Reason=null};
        }
        result=result with{CommandId=write};await db.SaveChangesAsync(ct);
        await CompleteOperation(write,command,task.Id,(await View(task.Id,ct)).TaskRowVersion,result,
            new{EvidenceKind="SYSTEM_EXECUTION",Trigger="TIMEOUT",result.ServiceId,result.ExecutionId,result.OriginalTaskVersion,result.OriginalInstanceVersion,Policy=policy,OriginalPrincipal=assignment.PrincipalId},ct);
        return result;
    }
}
