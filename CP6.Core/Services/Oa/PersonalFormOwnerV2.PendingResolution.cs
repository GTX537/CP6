using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class PersonalFormOwnerV2
{
    private const string ResolveApprover="OA_APPROVER_RESOLVE";
    private sealed record ResolutionRow(Guid Id,Guid Version,Guid Root,Guid Instance,Guid Cycle,string PolicyRef,string Slot,string State);
    private async Task<ResolutionRow> Resolution(Guid id,CancellationToken ct)=>await db.Database.SqlQuery<ResolutionRow>($"""
        SELECT id AS "Id",row_version AS "Version",root_id AS "Root",instance_id AS "Instance",execution_cycle_id AS "Cycle",
          route_policy_ref AS "PolicyRef",slot_json AS "Slot",state AS "State" FROM cp6_native_tasks_v2.pending_resolutions
        WHERE id={id} AND tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND source_kind='SFS_COMMAND'
        """).SingleOrDefaultAsync(ct)??throw Error("RESOURCE_NOT_FOUND",404);
    private async Task<TaskSource> PersonalInstanceSource(Guid instance,CancellationToken ct)=>await db.Database.SqlQuery<TaskSource>($"""
        SELECT x.form_data_id AS "Data",x.root_id AS "Root",s.head_id AS "FormHead",s.form_version AS "FormVersion",
          v.snapshot_json AS "Publication",'' AS "Assignment" FROM cp6_personal_form_v2.instances x
        JOIN cp6_personal_form_v2.submissions s ON s.form_data_id=x.form_data_id
        JOIN cp6_workflow_authoring_v2.versions v ON v.tenant_id=x.tenant_id AND v.environment_id=x.environment_id AND v.id=x.flow_version
        WHERE x.instance_id={instance} AND x.tenant_id={db.CurrentTenantId} AND x.environment_id={fence.EnvironmentId}
        """).SingleOrDefaultAsync(ct)??throw Error("RESOURCE_NOT_FOUND",404);
    private async Task ResolutionAccess(TaskSource source,UserPermissionContext actor,CancellationToken ct)
    {
        await Action(actor,"query",ct,"oa-inbox");await Action(actor,"resolve-approver",ct,"oa-inbox");
        if(!(await Policy(source.FormHead,ct)??throw Error("FORM_USE_POLICY_REQUIRED",403)).Enabled)throw Error("FORM_USE_REVOKED",403);
        await Mask(actor,source.FormHead,await Publication(source.FormHead,source.FormVersion,ct),ct);
    }
    private async Task<bool> ResolutionCandidate(TaskSource source,PendingApproverSlotV2 slot,Guid candidate,CancellationToken ct)
    {
        if(slot.Policy is not{Mode:"MANUAL_ASSIGNMENT"} policy)return false;
        var instance=await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==slot.InstanceId,ct);
        var publication=Parse<FlowPublicationV2>(source.Publication);
        if(policy.ExcludeStarter&&candidate==instance.StarterId||policy.ExcludePublisher&&candidate==publication.PublisherRef)return false;
        var permission=await PostgreSqlPermissionReader.ReadAsync(db,candidate,ct);
        if(!permission.RoleIds.Intersect(policy.EligibleRoleIds).Any())return false;
        try{await TargetAuthority(source,candidate,ct);return true;}
        catch(ScopeAuthorizationExceptionV1 e)when(e.Status is 403 or 404 or 422){return false;}
    }
    private async Task<PendingResolutionContextV2> ResolutionContext(ResolutionRow row,TaskSource source,CancellationToken ct)
    {
        var slot=Parse<PendingApproverSlotV2>(row.Slot);var instance=await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==row.Instance,ct);
        var candidates=new List<NativeTaskCandidateV2>();var reasons=new List<string>();
        if(row.State!="PENDING")reasons.Add("RESOLUTION_ALREADY_COMPLETED");
        if(slot.Policy?.Mode!="MANUAL_ASSIGNMENT")reasons.Add("MANUAL_ASSIGNMENT_NOT_ALLOWED");
        if(reasons.Count==0)
            foreach(var user in await db.Sys_Users.AsNoTracking().Where(u=>u.Enable&&(u.LockedUntil==null||u.LockedUntil<=DateTime.Now)).Select(u=>new{u.Id,u.UserName}).ToArrayAsync(ct))
                if(await ResolutionCandidate(source,slot,user.Id,ct))candidates.Add(new(user.Id,user.UserName));
        if(candidates.Count==0)reasons.Add("NO_ELIGIBLE_APPROVER");
        return new(row.Id,row.Version.ToString("D"),row.Instance,Convert.ToBase64String(instance.RowVersion!),row.Cycle,slot.NodeId,slot.TokenId,
            slot.StageIndex,slot.StageRound,slot.FlowVersionId,row.PolicyRef,slot.Reason,row.State,candidates.Select(u=>u.UserId).Order().ToArray(),reasons.Count==0,reasons.ToArray(),candidates.OrderBy(u=>u.Name).ThenBy(u=>u.UserId).ToArray());
    }
    public async Task<PendingResolutionContextV2[]> PendingResolutionsAsync(Guid instance,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);var actor=await Actor(ct);
        var source=await PersonalInstanceSource(instance,ct);await ResolutionAccess(source,actor,ct);
        var ids=await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_native_tasks_v2.pending_resolutions WHERE instance_id={instance} AND tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} ORDER BY created_at,id").ToArrayAsync(ct);
        var result=new List<PendingResolutionContextV2>();foreach(var id in ids)result.Add(await ResolutionContext(await Resolution(id,ct),source,ct));
        await Final(actor,lease,ct,false);return result.ToArray();
    }
    public async Task<PendingResolutionContextV2> ResolutionContextAsync(Guid id,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);var actor=await Actor(ct);
        var row=await Resolution(id,ct);var source=await PersonalInstanceSource(row.Instance,ct);await ResolutionAccess(source,actor,ct);
        var result=await ResolutionContext(row,source,ct);await Final(actor,lease,ct,false);return result;
    }
    public async Task<PersonalFormResultV2> ResolveApproverAsync(Guid id,byte[] raw,CancellationToken ct=default)
    {
        var command=CommandInput<ResolveApproverInputV2>(raw,true);var input=command.Body;Reason(input.Reason,true);
        if(!RuntimeVersion(input.ExpectedInstanceVersion)||input.TargetPrincipalIds is not{Length:>=1 and <=100}
            ||input.TargetPrincipalIds.Any(x=>x==Guid.Empty)||!input.TargetPrincipalIds.SequenceEqual(input.TargetPrincipalIds.Distinct().Order()))throw Error("COMMAND_SHAPE_INVALID",400);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);var row=await Resolution(id,ct);var source=await PersonalInstanceSource(row.Instance,ct);
            await ResolutionAccess(source,actor,ct);var old=await Original(actor.UserId,ResolveApprover,id.ToString("D"),command.Id,ct);
            if(old!=null){Match(old,command.Digest);await Final(actor,lease,ct,false);return Parse<PersonalFormResultV2>(old.Result);}
            await WriteReady(lease,ct);var slot=Parse<PendingApproverSlotV2>(row.Slot);var instance=await db.Wf_FlowInstances.SingleAsync(i=>i.Id==row.Instance,ct);
            if(row.State!="PENDING")throw Error("RESOLUTION_ALREADY_COMPLETED",409);
            if(row.Version.ToString("D")!=command.Expected||Convert.ToBase64String(instance.RowVersion!)!=input.ExpectedInstanceVersion
                ||row.Cycle!=input.ExecutionCycleId||slot.NodeId!=input.NodeId||slot.TokenId!=input.TokenId||slot.StageIndex!=input.StageIndex||slot.StageRound!=input.StageRound
                ||row.PolicyRef!=input.RoutePolicyRef||instance.FlowDefVersionId!=slot.FlowVersionId||await ExecutionCycle(row.Root,ct)!=row.Cycle
                ||!await db.Wf_FlowTokens.AnyAsync(t=>t.Id==slot.TokenId&&t.Status==FlowTokenStatus.Active&&t.NodeId==slot.NodeId,ct))throw Error("RESOLUTION_CONTEXT_CHANGED",409);
            if(slot.Policy?.Mode!="MANUAL_ASSIGNMENT")throw Error("MANUAL_ASSIGNMENT_NOT_ALLOWED",403);
            foreach(var target in input.TargetPrincipalIds)if(!await ResolutionCandidate(source,slot,target,ct))throw Error("TARGET_INELIGIBLE",422);
            var write=Guid.NewGuid();await BeginCommand(write,actor.UserId,ResolveApprover,id.ToString("D"),command,null,source.Data,source.Root,null,ct);
            var engine=new FlowEngine(db,new ApproverResolver(db,exactConditions:true));
            var assignments=await engine.StagePersonalResolveApproverV2(fence.EnvironmentId,source.Data,source.Root,Parse<FlowPublicationV2>(source.Publication),slot,input.TargetPrincipalIds,actor.UserId,input.Reason,ct);
            var evidence=Encode(new{Actual=actor.UserId,Targets=input.TargetPrincipalIds,Tasks=assignments.Select(a=>a.TaskId).ToArray(),input.RoutePolicyRef,input.Reason,Scope=lease.Snapshot,Permission=actor.ActionKeys.Order().ToArray(),Occurred=Clock()});
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_native_tasks_v2.pending_resolutions SET state='RESOLVED',resolution_kind='SFS_COMMAND',resolution_command={write},assignment_evidence={evidence}
                WHERE id={id}
                """,ct);
            instance.Status=await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_native_tasks_v2.pending_resolutions WHERE instance_id={row.Instance} AND state='PENDING') AS \"Value\"").SingleAsync(ct)?FlowInstanceStatus.Suspended:FlowInstanceStatus.Running;
            await AssignmentNotifications(write,await engine.SavePersonalRuntime(write,ct),ct);await db.SaveChangesAsync(ct);
            var result=await Complete(write,command,id,(await Resolution(id,ct)).Version.ToString("D"),
                new{ResolutionId=id,InstanceId=row.Instance,ExecutionCycleId=row.Cycle,InstanceStatus=instance.Status,InstanceVersion=Convert.ToBase64String(instance.RowVersion!),TaskIds=assignments.Select(a=>a.TaskId).Order().ToArray()},
                new{Assignment=evidence,SourceCommand=id,Actor=actor.UserId},ct);
            await Final(actor,lease,ct);committing=true;await tx.CommitAsync(ct);db.ChangeTracker.Clear();return result;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
}
