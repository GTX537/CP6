using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class ApprovalTaskDecisionOwnerV2
{
    private sealed record AppResolution(Guid Id,Guid Version,Guid Root,Guid Instance,Guid Cycle,string PolicyRef,string Slot,string State);
    private async Task<AppResolution> AppResolutionRow(Guid id,CancellationToken ct)=>await db.Database.SqlQuery<AppResolution>($"""
        SELECT id AS "Id",row_version AS "Version",root_id AS "Root",instance_id AS "Instance",execution_cycle_id AS "Cycle",
          route_policy_ref AS "PolicyRef",slot_json AS "Slot",state AS "State" FROM cp6_native_tasks_v2.pending_resolutions
        WHERE id={id} AND tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND source_kind<>'SFS_COMMAND'
        """).SingleOrDefaultAsync(ct)??throw Error("RESOURCE_NOT_FOUND",404);
    public Task<bool> IsAppResolutionAsync(Guid id,CancellationToken ct=default)=>db.Database.SqlQuery<bool>($"""
        SELECT EXISTS(SELECT FROM cp6_native_tasks_v2.pending_resolutions WHERE id={id} AND tenant_id={db.CurrentTenantId}
          AND environment_id={scope.EnvironmentId} AND source_kind<>'SFS_COMMAND') AS "Value"
        """).SingleAsync(ct);
    public Task<bool> IsAppInstanceAsync(Guid id,CancellationToken ct=default)=>db.Wf_FlowInstances.AnyAsync(i=>i.Id==id&&i.Creator==FlowEngine.ApprovalRuntimeV2,ct);
    private async Task<FlowPublicationV2> ResolutionPublication(AppResolution row,CancellationToken ct)=>Parse<FlowPublicationV2>(await db.Database.SqlQuery<string>($"""
        SELECT v.snapshot_json AS "Value" FROM cp6_approval_runtime_v2.instances i JOIN cp6_workflow_authoring_v2.versions v
          ON v.tenant_id=i.tenant_id AND v.environment_id=i.environment_id AND v.id=i.flow_version
        WHERE i.instance_id={row.Instance} AND i.tenant_id={db.CurrentTenantId} AND i.environment_id={scope.EnvironmentId}
        """).SingleAsync(ct));
    private async Task AppResolutionAccess(ApprovalSubmitCommandV2 original,Guid actor,CancellationToken ct)
    {await authority.RequireAction(actor,"resolve-approver",ct);await Business.Decision(original,null,false,ct);}
    private async Task<bool> AppResolutionCandidate(ApprovalSubmitCommandV2 original,FlowPublicationV2 publication,PendingApproverSlotV2 slot,Guid target,CancellationToken ct)
    {
        if(slot.Policy is not{Mode:"MANUAL_ASSIGNMENT"} policy)return false;
        if(policy.ExcludeStarter&&target==original.StarterId||policy.ExcludePublisher&&target==publication.PublisherRef)return false;
        var permission=await PostgreSqlPermissionReader.ReadAsync(db,target,ct);
        if(!permission.RoleIds.Intersect(policy.EligibleRoleIds).Any())return false;
        try{await AppTarget(original,target,ct);return true;}
        catch(ScopeAuthorizationExceptionV1 e)when(e.Status is 403 or 404 or 422){return false;}
    }
    private async Task<PendingResolutionContextV2> AppResolutionContext(AppResolution row,ApprovalSubmitCommandV2 original,CancellationToken ct)
    {
        var slot=Parse<PendingApproverSlotV2>(row.Slot);var publication=await ResolutionPublication(row,ct);
        var instance=await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==row.Instance,ct);var candidates=new List<NativeTaskCandidateV2>();var reasons=new List<string>();
        if(row.State!="PENDING")reasons.Add("RESOLUTION_ALREADY_COMPLETED");
        if(slot.Policy?.Mode!="MANUAL_ASSIGNMENT")reasons.Add("MANUAL_ASSIGNMENT_NOT_ALLOWED");
        if(reasons.Count==0)foreach(var user in await db.Sys_Users.AsNoTracking().Where(u=>u.Enable&&(u.LockedUntil==null||u.LockedUntil<=DateTime.Now)).Select(u=>new{u.Id,u.UserName}).ToArrayAsync(ct))
            if(await AppResolutionCandidate(original,publication,slot,user.Id,ct))candidates.Add(new(user.Id,user.UserName));
        if(candidates.Count==0)reasons.Add("NO_ELIGIBLE_APPROVER");
        return new(row.Id,row.Version.ToString("D"),row.Instance,Convert.ToBase64String(instance.RowVersion!),row.Cycle,slot.NodeId,slot.TokenId,
            slot.StageIndex,slot.StageRound,slot.FlowVersionId,row.PolicyRef,slot.Reason,row.State,candidates.Select(u=>u.UserId).Order().ToArray(),reasons.Count==0,reasons.ToArray(),candidates.OrderBy(u=>u.Name).ThenBy(u=>u.UserId).ToArray());
    }
    public async Task<PendingResolutionContextV2[]> PendingResolutionsAsync(Guid instance,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var fence=await scope.AcquireAsync(ct);await Ready(ct);var actor=await authority.CurrentActor(false,ct);
        var root=await db.Database.SqlQuery<Guid>($"SELECT root_instance_id AS \"Value\" FROM cp6_approval_runtime_v2.instances WHERE instance_id={instance} AND tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId}").SingleOrDefaultAsync(ct);
        var original=await OperationOriginal(root,ct);await AppResolutionAccess(original,actor,ct);
        var ids=await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_native_tasks_v2.pending_resolutions WHERE instance_id={instance} AND tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} ORDER BY created_at,id").ToArrayAsync(ct);
        var result=new List<PendingResolutionContextV2>();foreach(var id in ids)result.Add(await AppResolutionContext(await AppResolutionRow(id,ct),original,ct));
        await scope.ValidateAsync(fence,ct);return result.ToArray();
    }
    public async Task<PendingResolutionContextV2> ResolutionContextAsync(Guid id,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var fence=await scope.AcquireAsync(ct);await Ready(ct);var actor=await authority.CurrentActor(false,ct);
        var row=await AppResolutionRow(id,ct);var original=await OperationOriginal(row.Root,ct);await AppResolutionAccess(original,actor,ct);
        var result=await AppResolutionContext(row,original,ct);await scope.ValidateAsync(fence,ct);return result;
    }
    public async Task<PersonalFormResultV2> ResolveApproverAsync(Guid id,byte[] raw,CancellationToken ct=default)
    {
        var command=PersonalFormOwnerV2.CommandInput<ResolveApproverInputV2>(raw,true);var input=command.Body;OperationReason(input.Reason);
        if(!PersonalFormOwnerV2.RuntimeVersion(input.ExpectedInstanceVersion)||input.TargetPrincipalIds is not{Length:>=1 and <=100}||input.TargetPrincipalIds.Any(x=>x==Guid.Empty)
            ||!input.TargetPrincipalIds.SequenceEqual(input.TargetPrincipalIds.Distinct().Order()))throw Error("COMMAND_SHAPE_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var fence=await ReadOperationFence(ct);var actor=await authority.CurrentActor(false,ct);
            var row=await AppResolutionRow(id,ct);var original=await OperationOriginal(row.Root,ct);await AppResolutionAccess(original,actor,ct);
            var old=await OriginalOperation(actor,"OA_APPROVER_RESOLVE",id,command.Id,ct);
            if(old!=null){if(old.Closed)throw Error("COMMAND_CLOSED",409);if(old.Digest!=command.Digest)throw Error("COMMAND_BODY_CONFLICT",409);await scope.ValidateAsync(fence,ct);return Parse<PersonalFormResultV2>(old.Result);}
            await scope.AcquireAsync(ct);await Ready(ct);
            var slot=Parse<PendingApproverSlotV2>(row.Slot);var instance=await db.Wf_FlowInstances.SingleAsync(i=>i.Id==row.Instance,ct);var publication=await ResolutionPublication(row,ct);
            if(row.State!="PENDING")throw Error("RESOLUTION_ALREADY_COMPLETED",409);
            if(row.Version.ToString("D")!=command.Expected||Convert.ToBase64String(instance.RowVersion!)!=input.ExpectedInstanceVersion||row.Cycle!=input.ExecutionCycleId
                ||slot.NodeId!=input.NodeId||slot.TokenId!=input.TokenId||slot.StageIndex!=input.StageIndex||slot.StageRound!=input.StageRound||row.PolicyRef!=input.RoutePolicyRef
                ||!await db.Wf_FlowTokens.AnyAsync(t=>t.Id==slot.TokenId&&t.Status==FlowTokenStatus.Active&&t.NodeId==slot.NodeId,ct))throw Error("RESOLUTION_CONTEXT_CHANGED",409);
            if(slot.Policy?.Mode!="MANUAL_ASSIGNMENT")throw Error("MANUAL_ASSIGNMENT_NOT_ALLOWED",403);
            foreach(var target in input.TargetPrincipalIds)if(!await AppResolutionCandidate(original,publication,slot,target,ct))throw Error("TARGET_INELIGIBLE",422);
            var write=Guid.NewGuid();await BeginOperation(write,actor,"OA_APPROVER_RESOLVE",id,row.Root,null,command,ct);
            var engine=new FlowEngine(db,new ApproverResolver(db,exactConditions:true));await engine.StageAppResolveApproverV2(publication,original,slot,input.TargetPrincipalIds,actor,input.Reason,ct);
            var evidence=Json(new{Actual=actor,Targets=input.TargetPrincipalIds,Tasks=engine.CreatedApprovalAssignments.Select(a=>a.TaskId).ToArray(),input.RoutePolicyRef,input.Reason,Scope=fence.Snapshot,Occurred=DateTimeOffset.UtcNow});
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_native_tasks_v2.pending_resolutions SET state='RESOLVED',resolution_kind='APP_OPERATION',resolution_command={write},assignment_evidence={evidence} WHERE id={id}",ct);
            instance.Status=await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_native_tasks_v2.pending_resolutions WHERE instance_id={row.Instance} AND state='PENDING') AS \"Value\"").SingleAsync(ct)?FlowInstanceStatus.Suspended:FlowInstanceStatus.Running;
            await engine.SaveApprovalRuntime(ct);await db.SaveChangesAsync(ct);await engine.SaveApprovalAssignments(ct);
            foreach(var assignment in engine.CreatedApprovalAssignments)await OperationNotification(write,assignment.PrincipalId,assignment.InstanceId,assignment.TaskId,"TASK_ASSIGNED","您有新的恢复审批待办",ct);
            await db.SaveChangesAsync(ct);
            var result=await CompleteOperation(write,command,id,(await AppResolutionRow(id,ct)).Version.ToString("D"),
                new{ResolutionId=id,InstanceId=row.Instance,ExecutionCycleId=row.Cycle,InstanceStatus=instance.Status,InstanceVersion=Convert.ToBase64String(instance.RowVersion!),TaskIds=engine.CreatedApprovalAssignments.Select(a=>a.TaskId).Order().ToArray()},new{Assignment=evidence},ct);
            await AppResolutionAccess(original,actor,ct);foreach(var target in input.TargetPrincipalIds)if(!await AppResolutionCandidate(original,publication,slot,target,ct))throw Error("TARGET_INELIGIBLE",422);
            await scope.ValidateAsync(fence,ct);await Ready(ct);committing=true;await tx.CommitAsync(ct);db.ChangeTracker.Clear();return result;
        }
        catch(Exception e){await RollbackOperation(tx,committing,e);throw;}
    }
}
