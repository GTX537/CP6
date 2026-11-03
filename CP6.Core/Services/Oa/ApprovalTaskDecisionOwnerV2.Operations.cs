using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class ApprovalTaskDecisionOwnerV2
{
    private sealed record OperationRow(Guid Id,Guid Actor,Guid Root,string Result,string Digest,bool Closed);
    private Task<OperationRow?> OriginalOperation(Guid actor,string operation,Guid resource,string key,CancellationToken ct)=>db.Database.SqlQuery<OperationRow>($"""
        SELECT id AS "Id",actor_id AS "Actor",root_id AS "Root",result_json AS "Result",digest AS "Digest",closed AS "Closed"
        FROM cp6_native_tasks_v2.commands WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId}
          AND actor_id={actor} AND operation={operation} AND resource_id={resource} AND command_key={key} AND completed
        """).SingleOrDefaultAsync(ct);
    private async Task<ApprovalSubmitCommandV2> OperationOriginal(Guid root,CancellationToken ct)
    {
        var raw=await db.Database.SqlQuery<byte[]>($"""
            SELECT p.payload AS "Value" FROM cp6_approval_runtime_v2.instances s JOIN cp6_approval_intake_v2.submissions p
              ON p.tenant_id=s.tenant_id AND p.environment_id=s.environment_id AND p.owner=s.owner AND p.id=s.submission_id
            WHERE s.instance_id={root} AND s.tenant_id={db.CurrentTenantId} AND s.environment_id={scope.EnvironmentId}
            """).SingleOrDefaultAsync(ct)??throw Error("RESOURCE_NOT_FOUND",404);
        return JsonSerializer.Deserialize<ApprovalSubmitCommandV2>(raw,ApprovalBindingOwnerV2.Json)!;
    }
    private Task BeginOperation<T>(Guid id,Guid actor,string operation,Guid resource,Guid root,Guid? task,
        PersonalFormOwnerV2.Command<T> command,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_native_tasks_v2.commands(id,tenant_id,environment_id,actor_id,operation,resource_id,command_key,digest,raw_body,expected_version,root_id,task_id)
        VALUES({id},{db.CurrentTenantId},{scope.EnvironmentId},{actor},{operation},{resource},{command.Id},{command.Digest},{command.Raw},{command.Expected},{root},{task})
        """,ct);
    private async Task<PersonalFormResultV2> CompleteOperation<T>(Guid id,PersonalFormOwnerV2.Command<T> command,Guid resource,string version,object value,object audit,CancellationToken ct)
    {
        var result=new PersonalFormResultV2(command.Id,"SUCCEEDED",resource,version,JsonSerializer.SerializeToElement(value,PersonalFormOwnerV2.Json),DateTimeOffset.UtcNow,command.Correlation);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_native_tasks_v2.commands SET result_json={Json(result)},audit_json={Json(audit)},completed=true WHERE id={id}",ct);return result;
    }
    private async Task OperationNotification(Guid command,Guid actor,Guid instance,Guid? task,string kind,string title,CancellationToken ct)
    {
        var matrix=NotifyMatrix.Rows().Single(x=>x.TypeValue==WfNotificationType.TodoCreated);var prefs=new PrefService(db);
        var inApp=matrix.InAppSupported&&await prefs.IsEnabledAsync(actor,matrix.TypeKey,NotifyMatrix.ChannelInApp);
        var email=matrix.EmailSupported&&await prefs.IsEnabledAsync(actor,matrix.TypeKey,NotifyMatrix.ChannelEmail);
        var id=Guid.NewGuid();db.Wf_Notifications.Add(new(){Id=id,UserId=actor,InstanceId=instance,TaskId=task,Type=WfNotificationType.TodoCreated,
            Title=title,Body=title,EventKey="native-task:"+id.ToString("N"),InAppRequested=inApp,EmailRequested=email,DispatchStatus=0,NextAttemptAtUtc=DateTime.UtcNow});
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_native_tasks_v2.notification_outbox(id,command_id,recipient_id,instance_id,task_id,kind) VALUES({id},{command},{actor},{instance},{task},{kind})",ct);
    }
    private static void OperationReason(string? reason)
    {if(string.IsNullOrWhiteSpace(reason)||reason.EnumerateRunes().Count()>1000)throw Error("REASON_INVALID",422);}
    private async Task AppTarget(ApprovalSubmitCommandV2 original,Guid target,CancellationToken ct)
    {
        if(original.SeparationSubjectIds.Contains(target))throw Error("SOD_VIOLATION",403);
        await authority.QualifiedTarget(target,ct);await Business.Decision(original,target,true,ct);
    }
    public async Task<NativeSendBackPreviewV2> SendBackPreviewAsync(Guid task,NativeSendBackTargetV2 target,Guid? behalf=null,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);await Ready(ct);var lease=await authority.AcquireAsync(task,behalf,ct,"TASK_SENDBACK");
        await authority.RequireAction(lease.Evidence.ActualHandlerId,"sendback",ct);await authority.RequireAction(lease.Evidence.PrincipalId,"sendback",ct);
        var source=await ReadSource(task,ct);var original=JsonSerializer.Deserialize<ApprovalSubmitCommandV2>(source.Payload,ApprovalBindingOwnerV2.Json)!;
        await Business.Decision(original,lease.Evidence.PrincipalId,false,ct);
        var result=await new FlowEngine(db,new ApproverResolver(db,exactConditions:true)).PreviewSendBackV2(task,source.Root,Parse<FlowPublicationV2>(source.Publication),target,ct);
        await authority.ValidateOperationAsync(lease,ct);return result;
    }
    public Task<PersonalFormResultV2> SendBackTaskAsync(Guid task,byte[] raw,Guid? behalf=null,CancellationToken ct=default)
        =>OperateTask(task,"OA_TASK_SENDBACK",PersonalFormOwnerV2.CommandInput<NativeSendBackInputV2>(raw,true,true),behalf,ct);
    public Task<PersonalFormResultV2> TransferTaskAsync(Guid task,byte[] raw,Guid? behalf=null,CancellationToken ct=default)
        =>OperateTask(task,"OA_TASK_TRANSFER",PersonalFormOwnerV2.CommandInput<NativeTransferInputV2>(raw,true,true),behalf,ct);
    public Task<PersonalFormResultV2> AddSignTaskAsync(Guid task,byte[] raw,Guid? behalf=null,CancellationToken ct=default)
        =>OperateTask(task,"OA_TASK_ADDSIGN",PersonalFormOwnerV2.CommandInput<NativeAddSignInputV2>(raw,true,true),behalf,ct);
    internal Task<PersonalFormResultV2> AdministrativeTransferTaskAsync(Guid task,Guid from,Guid batch,byte[] raw,CancellationToken ct)
        =>OperateTask(task,"OA_TASK_TRANSFER",PersonalFormOwnerV2.CommandInput<NativeTransferInputV2>(raw,true,true),null,ct,from,batch);
    internal async Task<ApprovalTaskDecisionViewV2> AdministrativeTransferContext(Guid task,Guid from,CancellationToken ct)
    {await authority.AcquireAdministrativeTransfer(task,from,ct);await AdministrativeTransferRead(task,ct);return await View(task,ct);}
    internal async Task AdministrativeTransferRead(Guid task,CancellationToken ct)
    {var actor=await authority.CurrentActor(false,ct);await authority.RequireAction(actor,"batch-transfer",ct);await Business.Decision(await OperationOriginal((await ReadSource(task,ct)).Root,ct),null,false,ct);}
    private async Task<PersonalFormResultV2> OperateTask<T>(Guid taskId,string operation,PersonalFormOwnerV2.Command<T> command,Guid? behalf,CancellationToken ct,Guid? administrativeFrom=null,Guid? batch=null)
    {
        var back=command.Body as NativeSendBackInputV2;var transfer=command.Body as NativeTransferInputV2;var add=command.Body as NativeAddSignInputV2;
        var reason=back?.Comment??transfer?.Reason??add!.Reason;OperationReason(reason);
        if(!PersonalFormOwnerV2.RuntimeVersion(back?.ExpectedTaskVersion??transfer?.ExpectedTaskVersion??add!.ExpectedTaskVersion))throw Error("COMMAND_SHAPE_INVALID",400);
        var action=back!=null?"sendback":transfer!=null?"transfer":"addsign";
        if(transfer!=null&&transfer.TaskId!=taskId||add!=null&&(add.TaskId!=taskId||add.Source is not("before" or "after")))throw Error("COMMAND_SHAPE_INVALID",400);
        if(add!=null&&behalf!=null)throw Error("ADDSIGN_DELEGATION_FORBIDDEN",403);
        command=command with{Digest=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{command.Digest,behalf,administrativeFrom,batch}))};
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var fence=await ReadOperationFence(ct);var actor=await authority.CurrentActor(false,ct);
            var old=await OriginalOperation(actor,operation,taskId,command.Id,ct);
            if(old!=null)
            {
                if(old.Closed)throw Error("COMMAND_CLOSED",409);if(old.Digest!=command.Digest)throw Error("COMMAND_BODY_CONFLICT",409);
                await Business.Decision(await OperationOriginal(old.Root,ct),null,false,ct);await scope.ValidateAsync(fence,ct);return Parse<PersonalFormResultV2>(old.Result);
            }
            await Ready(ct);
            if(batch!=null)await NativeBatchInstanceVersionV2.RequireWriter(db,ct);
            var lease=administrativeFrom is Guid from?await authority.AcquireAdministrativeTransfer(taskId,from,ct):await authority.AcquireAsync(taskId,behalf,ct,back!=null?"TASK_SENDBACK":transfer!=null?"TASK_TRANSFER":"TASK_READ");
            if(administrativeFrom==null){await authority.RequireAction(actor,action,ct);await authority.RequireAction(lease.Evidence.PrincipalId,action,ct);}
            var source=await ReadSource(taskId,ct);var original=JsonSerializer.Deserialize<ApprovalSubmitCommandV2>(source.Payload,ApprovalBindingOwnerV2.Json)!;
            var publication=Parse<FlowPublicationV2>(source.Publication);await Business.Decision(original,administrativeFrom!=null?null:lease.Evidence.PrincipalId,administrativeFrom==null,ct);
            var before=await View(taskId,ct);var expectedTask=back?.ExpectedTaskVersion??transfer?.ExpectedTaskVersion??add!.ExpectedTaskVersion;
            if(before.TaskRowVersion!=expectedTask||before.InstanceRowVersion!=command.Expected
                && !(batch is Guid batchId&&await NativeBatchInstanceVersionV2.OwnPriorResult(db,scope.EnvironmentId,actor,batchId,before.InstanceId,before.InstanceRowVersion,ct)))throw Error("PRECONDITION_FAILED",412);
            var engine=new FlowEngine(db,new ApproverResolver(db,exactConditions:true));NativeSendBackPreviewV2? preview=null;
            if(back!=null)
            {
                preview=await engine.PreviewSendBackV2(taskId,source.Root,publication,new(back.TargetKind,back.TargetNodeId),ct);
                if(preview.PreviewDigest!=back.PreviewDigest)throw Error("SENDBACK_PREVIEW_CHANGED",409);
            }
            var target=transfer?.ToUserId??add?.ToUserId;
            if(target is Guid to){if(to==lease.Evidence.PrincipalId||to==Guid.Empty)throw Error("TARGET_INELIGIBLE",422);await AppTarget(original,to,ct);}
            var write=Guid.NewGuid();await BeginOperation(write,actor,operation,taskId,source.Root,taskId,command,ct);
            var task=await db.Wf_FlowTasks.SingleAsync(t=>t.Id==taskId,ct);var instance=await db.Wf_FlowInstances.SingleAsync(i=>i.Id==task.InstanceId,ct);Guid resultTask=taskId;
            if(back!=null)await engine.StageAppSendBackV2(publication,original,taskId,actor,lease.Evidence.OnBehalfOfId,new(back.TargetKind,back.TargetNodeId),reason,ct);
            else if(transfer!=null)
            {
                var assignment=lease.Evidence.Assignment with{PrincipalId=transfer.ToUserId,CreatedPrincipalId=lease.Evidence.Assignment.CreatedPrincipalId??lease.Evidence.PrincipalId,
                    AssignmentRevision=lease.Evidence.Assignment.AssignmentRevision+1,Candidates=[transfer.ToUserId],Rule=new(ApproverStrategy.Specified,null,null,transfer.ToUserId),AssignedAt=DateTimeOffset.UtcNow};
                var body=Json(assignment);await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_native_tasks_v2.app_assignment_changes(task_id,revision,command_id,body,digest)
                    VALUES({taskId},{assignment.AssignmentRevision},{write},{body},{ApprovalCanonicalV2.Hash(System.Text.Encoding.UTF8.GetBytes(body))})
                    """,ct);
                await engine.StageNativeTransferV2(task,transfer.ToUserId,actor,lease.Evidence.OnBehalfOfId,reason,ct);
            }
            else
            {
                if(await db.Wf_FlowTasks.CountAsync(t=>t.InstanceId==instance.Id&&t.NodeId==task.NodeId&&t.TokenId==task.TokenId&&t.StageIndex==task.StageIndex&&t.StageRound==task.StageRound&&t.AddSignSource!=null,ct)>=10)throw Error("ADDSIGN_LIMIT",422);
                var added=await engine.StageAppAddSignV2(publication,original,task,add!.ToUserId,actor,add.Source,reason,lease.Evidence.Assignment.StageCode,ct);resultTask=added.Id;
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_native_tasks_v2.app_add_sign_links(task_id,parent_task_id,command_id,source) VALUES({added.Id},{taskId},{write},{add.Source})",ct);
            }
            instance.Modifier=actor.ToString("D");instance.ModifyDate=DateTime.Now;
            if(transfer==null)await engine.SaveApprovalRuntime(ct);
            await db.SaveChangesAsync(ct);if(transfer==null){await engine.SaveApprovalAssignments(ct);await engine.SavePendingApproversV2(write,"APP_OPERATION",ct);}
            var newTasks=await db.Wf_FlowTasks.AsNoTracking().Where(t=>t.InstanceId==instance.Id&&t.Status==FlowTaskStatus.Pending).ToArrayAsync(ct);
            if(back!=null)await OperationNotification(write,original.StarterId,source.Root,taskId,"TASK_SENT_BACK","流程已退回",ct);
            else await OperationNotification(write,target!.Value,instance.Id,resultTask,transfer!=null?"TASK_TRANSFERRED":"TASK_ASSIGNED","您有新的待办",ct);
            if(back!=null)foreach(var created in engine.CreatedApprovalAssignments)await OperationNotification(write,created.PrincipalId,created.InstanceId,created.TaskId,"TASK_ASSIGNED","您有新的退回待办",ct);
            await db.SaveChangesAsync(ct);var view=await View(resultTask,ct);var root=await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==source.Root,ct);
            var result=await CompleteOperation(write,command,resultTask,view.TaskRowVersion,new{Task=view,RootInstanceId=source.Root,InstanceStatus=root.Status,
                TaskIds=newTasks.Select(t=>t.Id).Order().ToArray(),RequiresOwnerRevision=back?.TargetKind=="STARTER"},new{Before=before,preview,Authority=lease.Evidence,Target=target,Reason=reason,Mode=administrativeFrom!=null?"ADMIN_REASSIGNMENT":"TASK_HOLDER"},ct);
            await authority.ValidateOperationAsync(lease,ct);await Business.Decision(original,administrativeFrom!=null?null:lease.Evidence.PrincipalId,administrativeFrom==null,ct);if(target is Guid user)await AppTarget(original,user,ct);
            await Ready(ct);committing=true;await tx.CommitAsync(ct);db.ChangeTracker.Clear();return result;
        }
        catch(Exception e)
        {await RollbackOperation(tx,committing,e);throw;}
    }
    private async Task RollbackOperation(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx,bool committing,Exception error)
    {
        try{await tx.RollbackAsync(CancellationToken.None);}
        catch(Exception rollback){throw new ScopeAuthorizationExceptionV1("COMMAND_OUTCOME_UNKNOWN",503,new AggregateException(error,rollback));}
        finally{db.ChangeTracker.Clear();}
        if(committing&&(error is not Npgsql.PostgresException pg||pg.SqlState=="40003"))throw new ScopeAuthorizationExceptionV1("COMMAND_OUTCOME_UNKNOWN",503,error);
    }
}
