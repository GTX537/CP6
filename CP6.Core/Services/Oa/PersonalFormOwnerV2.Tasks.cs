using System.Text;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.Contracts.Wire;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed record PersonalFormTaskContextV2(ApprovalTaskDecisionViewV2 Task, Guid FormDataId, Guid RootInstanceId,
    Guid FormVersionId, string Name, string SchemaJson, string DataJson, Dictionary<string,string> FieldMask,string[] AllowedDecisions,
    string Kind,string FormDataVersion,string FormKey,Guid FlowVersionId,string NodeId,Guid TokenId,int StageIndex,int StageRound);
public sealed record PersonalFormTaskResultV2(Guid CommandId, string State, Guid? VoteId,
    ApprovalTaskDecisionViewV2? Task, string QueryRef);

public sealed partial class PersonalFormOwnerV2
{
    private const string Decision="FORM_TASK_DECISION";
    private sealed record TaskSource(Guid Data,Guid Root,Guid FormHead,Guid FormVersion,string Publication,string Assignment);
    private sealed record TaskAuthority(PersonalFormAssignmentV2 Assignment,UserPermissionContext Actor,Guid Principal,
        Guid? OnBehalfOf,TaskDelegationRevisionV2? Grant,Dictionary<string,string> Mask,string Action,string FlowKey,string? BusinessType);
    public Task<bool> IsPersonalTaskAsync(Guid task,CancellationToken ct=default)=>(from t in db.Wf_FlowTasks from i in db.Wf_FlowInstances
        where t.Id==task && t.InstanceId==i.Id && i.Creator==FlowEngine.PersonalFormRuntimeV2 select t.Id).AnyAsync(ct);
    private async Task<TaskSource> TaskSourceAsync(Guid task,CancellationToken ct)=>await db.Database.SqlQuery<TaskSource>($"""
        SELECT x.form_data_id AS "Data",x.root_id AS "Root",s.head_id AS "FormHead",s.form_version AS "FormVersion",
          v.snapshot_json AS "Publication",COALESCE((SELECT body FROM cp6_personal_form_v2.assignment_changes WHERE task_id=a.task_id ORDER BY revision DESC LIMIT 1),a.body) AS "Assignment"
        FROM cp6_personal_form_v2.assignments a JOIN cp6_personal_form_v2.instances x ON x.instance_id=a.instance_id
        JOIN cp6_personal_form_v2.submissions s ON s.form_data_id=x.form_data_id
        JOIN cp6_workflow_authoring_v2.versions v ON v.tenant_id=x.tenant_id AND v.environment_id=x.environment_id AND v.head_id=x.flow_head AND v.id=x.flow_version
        WHERE a.task_id={task} AND x.tenant_id={db.CurrentTenantId} AND x.environment_id={fence.EnvironmentId}
        """).SingleOrDefaultAsync(ct)??throw Error("RESOURCE_NOT_FOUND",404);
    private async Task<ApprovalTaskDecisionViewV2> TaskView(Guid task,CancellationToken ct)
    {
        var t=await db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==task,ct);
        var i=await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==t.InstanceId,ct);
        var token=await db.Database.SqlQuery<byte[]>($"SELECT \"ApprovalRowVersion\" AS \"Value\" FROM public.\"Wf_FlowTask\" WHERE \"Id\"={task} AND \"TenantId\"={db.CurrentTenantId}").SingleAsync(ct);
        return new(t.Id,i.Id,t.Status,i.Status,Convert.ToBase64String(token),Convert.ToBase64String(i.RowVersion!));
    }
    private async Task<TaskAuthority> TaskAccess(TaskSource source,UserPermissionContext actor,Guid? behalf,bool write,CancellationToken ct,string action="TASK_APPROVE")
    {
        await Action(actor,write?"approve":"query",ct,"oa-inbox");
        var assignment=Parse<PersonalFormAssignmentV2>(source.Assignment);var principal=behalf??actor.UserId;
        if(assignment.PrincipalId!=principal)throw Error("RESOURCE_NOT_FOUND",404);
        var instance=await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==assignment.InstanceId,ct);
        TaskDelegationRevisionV2? grant=null;UserPermissionContext? principalPermission=null;
        if(principal!=actor.UserId)
        {
            if(!await db.Sys_Users.AnyAsync(u=>u.Id==principal && u.Enable && (u.LockedUntil==null||u.LockedUntil<=DateTime.Now),ct))throw Error("TASK_DUTY_REVOKED",403);
            principalPermission=await PostgreSqlPermissionReader.ReadAsync(db,principal,ct);
            await Action(principalPermission,write?"approve":"query",ct,"oa-inbox");
            grant=await new TaskDelegationReaderV2(db,fence.EnvironmentId).Require(actor.UserId,principal,action,instance.FlowKey,instance.BizType,ct);
        }
        if(write)
        {
            var task=await db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==assignment.TaskId,ct);
            if(task.Status!=FlowTaskStatus.Pending||instance.Status!=FlowInstanceStatus.Running)throw Error("TASK_NOT_PENDING",409);
            var duty=await new ApproverResolver(db,exactConditions:true).ResolveAsync(assignment.Rule,new(){StarterUserId=instance.StarterId,VarsJson=instance.VarsJson});
            if(!duty.ApproverIds.Contains(principal))throw Error("TASK_DUTY_REVOKED",403);
        }
        var publication=await Publication(source.FormHead,source.FormVersion,ct);var mask=await Mask(actor,source.FormHead,publication,ct);
        if(principalPermission!=null)mask=IntersectMasks(mask,await Mask(principalPermission,source.FormHead,publication,ct));
        mask=PinnedTaskMask(source,mask);
        return new(assignment,actor,principal,principal==actor.UserId?null:principal,grant,mask,action,instance.FlowKey,instance.BizType);
    }
    private static Dictionary<string,string> PinnedTaskMask(TaskSource source,Dictionary<string,string> mask)
    {
        var graph=JsonSerializer.Deserialize<FlowSchema>(Parse<FlowPublicationV2>(source.Publication).Fields["SchemaJson"]!,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var node=graph.Nodes.Single(n=>n.Id==Parse<PersonalFormAssignmentV2>(source.Assignment).NodeId);
        return node.FieldPerms is {} fields?IntersectMasks(mask,mask.Keys.ToDictionary(k=>k,k=>fields.GetValueOrDefault(k,"edit"),StringComparer.Ordinal)):mask;
    }
    private async Task<TaskAuthority> TaskEditable(TaskSource source,UserPermissionContext actor,Guid? behalf,CancellationToken ct)
    {
        var access=await TaskAccess(source,actor,behalf,true,ct,"TASK_READ");
        if(access.OnBehalfOf==null)return access;
        var reader=new TaskDelegationReaderV2(db,fence.EnvironmentId);
        foreach(var action in new[]{"TASK_APPROVE","TASK_REJECT"})
            if(await reader.Match(actor.UserId,access.Principal,action,access.FlowKey,access.BusinessType,ct) is {} grant)
                return access with{Grant=grant,Action=action};
        throw Error("TASK_EDIT_FORBIDDEN",403);
    }
    public Task<PersonalFormTaskContextV2> TaskContextAsync(Guid task,Guid? behalf=null,CancellationToken ct=default)
        =>ReadTaskContext(task,behalf,(c,_)=>c,ct);
    private async Task<T> ReadTaskContext<T>(Guid task,Guid? behalf,Func<PersonalFormTaskContextV2,TaskAuthority,T> project,CancellationToken ct)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);
        await WriteReady(lease,ct);var actor=await Actor(ct);var source=await TaskSourceAsync(task,ct);var access=await TaskAccess(source,actor,behalf,true,ct,"TASK_READ");
        var form=await Publication(source.FormHead,source.FormVersion,ct);var data=await db.Wf_FormDatas.AsNoTracking().SingleAsync(f=>f.Id==source.Data,ct);
        var allowed=new List<string>();var reader=new TaskDelegationReaderV2(db,fence.EnvironmentId);
        foreach(var decision in new[]{"approve","reject"})
            if(access.OnBehalfOf==null||await reader.Match(actor.UserId,access.Principal,decision=="approve"?"TASK_APPROVE":"TASK_REJECT",access.FlowKey,access.BusinessType,ct)!=null)allowed.Add(decision);
        var result=new PersonalFormTaskContextV2(await TaskView(task,ct),source.Data,source.Root,form.VersionId,form.Fields["Name"]!,
            ProjectSchema(form.Fields["SchemaJson"]!,access.Mask),PersonalFormValuesV2.Project(data.DataJson,access.Mask),access.Mask,allowed.ToArray(),
            "SFS",Convert.ToBase64String(data.RowVersion!),form.Key,access.Assignment.FlowVersionId,access.Assignment.NodeId,
            access.Assignment.TokenId,access.Assignment.StageIndex,access.Assignment.StageRound);
        await FinalTask(access,lease,ct);return project(result,access);
    }
    private async Task FinalTask(TaskAuthority access,ScopeAuthorizationLeaseV1 lease,CancellationToken ct)
    {
        await Final(access.Actor,lease,ct);
        if(access.Grant is {} grant)await new TaskDelegationReaderV2(db,fence.EnvironmentId).ValidateExact(grant,access.Action,access.FlowKey,access.BusinessType,ct);
    }
    public async Task<PersonalFormTaskResultV2?> TaskOriginalAsync(Guid task,Guid command,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);
        var actor=await Actor(ct);await Action(actor,"query",ct,"oa-inbox");
        var old=await Original(actor.UserId,Decision,task.ToString("D"),command.ToString("D"),ct);
        PersonalFormTaskResultV2? result=null;
        if(old!=null)
        {
            var source=await TaskSourceAsync(task,ct);await Mask(actor,source.FormHead,await Publication(source.FormHead,source.FormVersion,ct),ct);
            result=old.Closed?new(command,"CLOSED",null,null,"/api/oa/v2/personal-forms/tasks/"+task+"/commands/"+command)
                :Parse<PersonalFormResultV2>(old.Result).Result!.Value.Deserialize<PersonalFormTaskResultV2>(Json)!;
        }
        await Final(actor,lease,ct,false);return result;
    }
    private static void DecisionInput(ApprovalTaskDecisionInputV2 input)
    {
        if(input.CommandId==Guid.Empty||input.Decision is not("approve" or "reject")||input.Comment==null
            ||input.Comment.EnumerateRunes().Count()>500||input.Decision=="reject"&&string.IsNullOrWhiteSpace(input.Comment))throw Error("DECISION_INPUT_INVALID",422);
        try
        {
            _=new UTF8Encoding(false,true).GetByteCount(input.Comment);
            foreach(var token in new[]{input.TaskRowVersion,input.InstanceRowVersion})
                if(Convert.FromBase64String(token).Length!=8||Convert.ToBase64String(Convert.FromBase64String(token))!=token)throw new FormatException();
        }
        catch(Exception e)when(e is FormatException or ArgumentNullException or EncoderFallbackException){throw Error("DECISION_INPUT_INVALID",422);}
    }
    public async Task<PersonalFormTaskResultV2> DecideTaskAsync(Guid task,ApprovalTaskDecisionInputV2 input,Guid? behalf=null,string? ifMatch=null,CancellationToken ct=default)
    {
        DecisionInput(input);Entry();var resource=task.ToString("D");
        var raw=JsonSerializer.SerializeToUtf8Bytes(new{task,behalf,input},Json);using var doc=JsonDocument.Parse(raw);
        var command=new Command<ApprovalTaskDecisionInputV2>(input.CommandId.ToString("D"),null,input,WorkflowCommandJsonV1.Digest(doc.RootElement,null),raw,null);
        await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await Action(actor,"query",ct,"oa-inbox");
            var old=await Original(actor.UserId,Decision,resource,command.Id,ct);
            if(old!=null)
            {
                Match(old,command.Digest);
                var originalSource=await TaskSourceAsync(task,ct);await Mask(actor,originalSource.FormHead,await Publication(originalSource.FormHead,originalSource.FormVersion,ct),ct);
                var replay=Parse<PersonalFormResultV2>(old.Result).Result!.Value.Deserialize<PersonalFormTaskResultV2>(Json)!;
                await Final(actor,lease,ct,false);return replay;
            }
            throw Error("SFS_THREE_VERSIONS_REQUIRED",410);
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
}
