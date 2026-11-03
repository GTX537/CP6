using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class PersonalFormOwnerV2
{
    private const string Transfer="OA_TASK_TRANSFER",AddSign="OA_TASK_ADDSIGN";
    public Task<PersonalFormResultV2> TransferTaskAsync(Guid task,byte[] raw,Guid? behalf=null,CancellationToken ct=default)
        =>ChangeAssignment(task,CommandInput<NativeTransferInputV2>(raw,true,true),false,behalf,ct);
    public Task<PersonalFormResultV2> AddSignTaskAsync(Guid task,byte[] raw,Guid? behalf=null,CancellationToken ct=default)
        =>ChangeAssignment(task,CommandInput<NativeAddSignInputV2>(raw,true,true),true,behalf,ct);
    internal Task<PersonalFormResultV2> AdministrativeTransferTaskAsync(Guid task,Guid from,Guid batch,byte[] raw,CancellationToken ct)
        =>ChangeAssignment(task,CommandInput<NativeTransferInputV2>(raw,true,true),false,null,ct,from,batch);
    internal async Task<ApprovalTaskDecisionViewV2> AdministrativeTransferContext(Guid task,Guid from,CancellationToken ct)
    {await AdministrativeTaskAccess(await TaskSourceAsync(task,ct),await Actor(ct),from,ct);return await TaskView(task,ct);}
    internal async Task AdministrativeTransferRead(Guid task,CancellationToken ct)
    {var actor=await Actor(ct);await Action(actor,"batch-transfer",ct,"oa-inbox");var source=await TaskSourceAsync(task,ct);await Mask(actor,source.FormHead,await Publication(source.FormHead,source.FormVersion,ct),ct);}
    private async Task<TaskAuthority> AdministrativeTaskAccess(TaskSource source,UserPermissionContext actor,Guid from,CancellationToken ct)
    {
        await Action(actor,"batch-transfer",ct,"oa-inbox");
        var assignment=Parse<PersonalFormAssignmentV2>(source.Assignment);
        var task=await db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==assignment.TaskId,ct);
        var instance=await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==task.InstanceId,ct);
        if(assignment.PrincipalId!=from||task.AssigneeId!=from)throw Error("TASK_ASSIGNEE_CHANGED",409);
        if(task.Status!=FlowTaskStatus.Pending||instance.Status!=FlowInstanceStatus.Running)throw Error("TASK_NOT_PENDING",409);
        var policy=await Policy(source.FormHead,ct)??throw Error("FORM_USE_POLICY_REQUIRED",403);
        if(!policy.Enabled)throw Error("FORM_USE_REVOKED",403);
        var mask=await Mask(actor,source.FormHead,await Publication(source.FormHead,source.FormVersion,ct),ct);
        // Administrative reassignment is not delegation or a human vote. The former
        // assignee may have left; manager resource access and new duty remain mandatory.
        return new(assignment,actor,from,null,null,mask,"ADMIN_TRANSFER",instance.FlowKey,instance.BizType);
    }
    private async Task TargetAuthority(TaskSource source,Guid target,CancellationToken ct)
    {
        if(!await db.Sys_Users.AnyAsync(u=>u.Id==target&&u.Enable&&(u.LockedUntil==null||u.LockedUntil<=DateTime.Now),ct))throw Error("TARGET_INELIGIBLE",422);
        var permissions=await PostgreSqlPermissionReader.ReadAsync(db,target,ct);await Action(permissions,"approve",ct,"oa-inbox");
        var policy=await Policy(source.FormHead,ct)??throw Error("FORM_USE_POLICY_REQUIRED",403);
        if(!policy.Enabled)throw Error("FORM_USE_REVOKED",403);
        await Mask(permissions,source.FormHead,await Publication(source.FormHead,source.FormVersion,ct),ct);
    }
    private async Task<PersonalFormResultV2> ChangeAssignment<T>(Guid taskId,Command<T> command,bool addsign,Guid? behalf,CancellationToken ct,Guid? administrativeFrom=null,Guid? batch=null)
    {
        var transfer=command.Body as NativeTransferInputV2;var add=command.Body as NativeAddSignInputV2;
        var target=transfer?.ToUserId??add!.ToUserId;var reason=transfer?.Reason??add!.Reason;
        var expectedTask=transfer?.ExpectedTaskVersion??add!.ExpectedTaskVersion;
        if((transfer?.TaskId??add!.TaskId)!=taskId||target==Guid.Empty||!RuntimeVersion(expectedTask)||addsign&&add!.Source is not("before" or "after"))throw Error("COMMAND_SHAPE_INVALID",400);
        Reason(reason,true);if(addsign&&behalf!=null)throw Error("ADDSIGN_DELEGATION_FORBIDDEN",403);
        command=command with{Digest=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{command.Digest,behalf,administrativeFrom,batch}))};
        var operation=addsign?AddSign:Transfer;var action=addsign?"addsign":"transfer";
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await Action(actor,"query",ct,"oa-inbox");
            var old=await Original(actor.UserId,operation,taskId.ToString("D"),command.Id,ct);
            if(old!=null){Match(old,command.Digest);var replay=await DiscloseTaskOperation(old,actor,ct);await Final(actor,lease,ct,false);return replay;}
            await WriteReady(lease,ct);await Action(actor,administrativeFrom!=null?"batch-transfer":action,ct,"oa-inbox");var source=await TaskSourceAsync(taskId,ct);
            if(batch!=null)await NativeBatchInstanceVersionV2.RequireWriter(db,ct);
            var access=administrativeFrom is Guid from?await AdministrativeTaskAccess(source,actor,from,ct):await TaskAccess(source,actor,behalf,true,ct,addsign?"TASK_READ":"TASK_TRANSFER");
            if(access.OnBehalfOf!=null)await Action(await PostgreSqlPermissionReader.ReadAsync(db,access.Principal,ct),action,ct,"oa-inbox");
            var before=await TaskView(taskId,ct);
            if(before.TaskRowVersion!=expectedTask||before.InstanceRowVersion!=command.Expected
                && !(batch is Guid batchId&&await NativeBatchInstanceVersionV2.OwnPriorResult(db,fence.EnvironmentId,actor.UserId,batchId,before.InstanceId,before.InstanceRowVersion,ct)))throw Error("TASK_CONTEXT_CHANGED",409);
            if(target==access.Principal)throw Error("TARGET_INELIGIBLE",422);await TargetAuthority(source,target,ct);
            var task=await db.Wf_FlowTasks.SingleAsync(t=>t.Id==taskId,ct);var instance=await db.Wf_FlowInstances.SingleAsync(i=>i.Id==task.InstanceId,ct);
            if(addsign&&await db.Wf_FlowTasks.CountAsync(t=>t.InstanceId==task.InstanceId&&t.TokenId==task.TokenId&&t.NodeId==task.NodeId&&t.StageIndex==task.StageIndex&&t.StageRound==task.StageRound&&t.AddSignSource!=null,ct)>=10)throw Error("ADDSIGN_LIMIT",422);
            var write=Guid.NewGuid();await BeginCommand(write,actor.UserId,operation,taskId.ToString("D"),command,null,source.Data,source.Root,taskId,ct);
            var engine=new FlowEngine(db,new ApproverResolver(db,exactConditions:true));Guid resultTask=taskId;
            if(addsign)
            {
                var assigned=await engine.StagePersonalAddSignV2(fence.EnvironmentId,source.Data,source.Root,Parse<FlowPublicationV2>(source.Publication),task,target,actor.UserId,add!.Source,reason,access.Assignment.StageCode,ct);
                resultTask=assigned.Id;await AssignmentNotifications(write,await engine.SavePersonalRuntime(write,ct),ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_personal_form_v2.add_sign_links(task_id,parent_task_id,command_id,source)
                    VALUES({resultTask},{taskId},{write},{add.Source})
                    """,ct);
            }
            else
            {
                var assignment=access.Assignment with{PrincipalId=target,CreatedPrincipalId=access.Assignment.CreatedPrincipalId??access.Assignment.PrincipalId,
                    AssignmentRevision=access.Assignment.AssignmentRevision+1,Candidates=[target],Rule=new(ApproverStrategy.Specified,null,null,target),AssignedAt=Clock()};
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_personal_form_v2.assignment_changes(task_id,revision,command_id,body)
                    VALUES({taskId},{assignment.AssignmentRevision},{write},{Encode(assignment)})
                    """,ct);
                await engine.StageNativeTransferV2(task,target,actor.UserId,access.OnBehalfOf,reason,ct);
                await Notification(write,"TASK_TRANSFERRED",target,WfNotificationType.TodoCreated,"您有新的转交待办",instance.Id,taskId,instance.FlowKey,ct);
            }
            instance.Modifier=actor.UserId.ToString("D");instance.ModifyDate=DateTime.Now;
            await db.SaveChangesAsync(ct);
            var result=await Complete(write,command,resultTask,(await TaskView(resultTask,ct)).TaskRowVersion,
                new{Task=await TaskView(resultTask,ct),CreatedPrincipalId=addsign?target:access.Assignment.CreatedPrincipalId??access.Assignment.PrincipalId,CurrentPrincipalId=target},
                new{Before=before,Actual=actor.UserId,access.Principal,access.OnBehalfOf,access.Grant,Target=target,Reason=reason,Mode=administrativeFrom!=null?"ADMIN_REASSIGNMENT":"TASK_HOLDER",Scope=lease.Snapshot},ct);
            await FinalTask(access,lease,ct);await TargetAuthority(source,target,ct);committing=true;await tx.CommitAsync(ct);db.ChangeTracker.Clear();return result;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
}
