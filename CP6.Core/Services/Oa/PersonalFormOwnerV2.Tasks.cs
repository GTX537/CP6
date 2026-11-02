using System.Text;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.Contracts.Wire;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed record PersonalFormTaskContextV2(ApprovalTaskDecisionViewV2 Task, Guid FormDataId, Guid RootInstanceId,
    Guid FormVersionId, string Name, string SchemaJson, string DataJson, Dictionary<string,string> FieldMask);
public sealed record PersonalFormTaskResultV2(Guid CommandId, string State, Guid? VoteId,
    ApprovalTaskDecisionViewV2? Task, string QueryRef);

public sealed partial class PersonalFormOwnerV2
{
    private const string Decision="FORM_TASK_DECISION";
    private sealed record TaskSource(Guid Data,Guid Root,Guid FormHead,Guid FormVersion,string Publication,string Assignment);
    private sealed record TaskAuthority(PersonalFormAssignmentV2 Assignment,UserPermissionContext Actor,Guid Principal,
        Guid? OnBehalfOf,Wf_FlowDelegate? Grant,Dictionary<string,string> Mask);
    public Task<bool> IsPersonalTaskAsync(Guid task,CancellationToken ct=default)=>(from t in db.Wf_FlowTasks from i in db.Wf_FlowInstances
        where t.Id==task && t.InstanceId==i.Id && i.Creator==FlowEngine.PersonalFormRuntimeV2 select t.Id).AnyAsync(ct);
    private async Task<TaskSource> TaskSourceAsync(Guid task,CancellationToken ct)=>await db.Database.SqlQuery<TaskSource>($"""
        SELECT x.form_data_id AS "Data",x.root_id AS "Root",s.head_id AS "FormHead",s.form_version AS "FormVersion",
          v.snapshot_json AS "Publication",a.body AS "Assignment"
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
    private async Task<TaskAuthority> TaskAccess(TaskSource source,UserPermissionContext actor,Guid? behalf,bool write,CancellationToken ct)
    {
        await Action(actor,write?"approve":"query",ct,"oa-inbox");
        var assignment=Parse<PersonalFormAssignmentV2>(source.Assignment);var principal=behalf??actor.UserId;
        if(assignment.PrincipalId!=principal)throw Error("RESOURCE_NOT_FOUND",404);
        var instance=await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==assignment.InstanceId,ct);
        Wf_FlowDelegate? grant=null;
        if(principal!=actor.UserId)
        {
            if(!await db.Sys_Users.AnyAsync(u=>u.Id==principal && u.Enable && (u.LockedUntil==null||u.LockedUntil<=DateTime.Now),ct))throw Error("TASK_DUTY_REVOKED",403);
            await Action(await PostgreSqlPermissionReader.ReadAsync(db,principal,ct),write?"approve":"query",ct,"oa-inbox");
            var now=Clock().LocalDateTime;
            var grants=await db.Wf_FlowDelegates.AsNoTracking().Where(g=>g.GrantorId==principal && g.DelegateId==actor.UserId && g.Enable && g.ValidFrom<=now && now<=g.ValidTo)
                .OrderByDescending(g=>g.CreateDate).ThenBy(g=>g.Id).ToArrayAsync(ct);
            grant=grants.FirstOrDefault(g=>string.IsNullOrEmpty(g.Scope)||string.Equals(g.Scope,instance.FlowKey,StringComparison.Ordinal))??throw Error("DELEGATION_EXPIRED",403);
        }
        if(write)
        {
            var task=await db.Wf_FlowTasks.AsNoTracking().SingleAsync(t=>t.Id==assignment.TaskId,ct);
            if(task.Status!=FlowTaskStatus.Pending||instance.Status!=FlowInstanceStatus.Running)throw Error("TASK_NOT_PENDING",409);
            var duty=await new ApproverResolver(db,exactConditions:true).ResolveAsync(assignment.Rule,new(){StarterUserId=instance.StarterId,VarsJson=instance.VarsJson});
            if(!duty.ApproverIds.Contains(principal))throw Error("TASK_DUTY_REVOKED",403);
        }
        var mask=await Mask(actor,source.FormHead,await Publication(source.FormHead,source.FormVersion,ct),ct);
        return new(assignment,actor,principal,principal==actor.UserId?null:principal,grant,mask);
    }
    public async Task<PersonalFormTaskContextV2> TaskContextAsync(Guid task,Guid? behalf=null,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);
        await WriteReady(lease,ct);var actor=await Actor(ct);var source=await TaskSourceAsync(task,ct);var access=await TaskAccess(source,actor,behalf,true,ct);
        var form=await Publication(source.FormHead,source.FormVersion,ct);var data=await db.Wf_FormDatas.AsNoTracking().SingleAsync(f=>f.Id==source.Data,ct);
        var result=new PersonalFormTaskContextV2(await TaskView(task,ct),source.Data,source.Root,form.VersionId,form.Fields["Name"]!,
            ProjectSchema(form.Fields["SchemaJson"]!,access.Mask),PersonalFormValuesV2.Project(data.DataJson,access.Mask),access.Mask);
        await FinalTask(access,lease,ct);return result;
    }
    private async Task FinalTask(TaskAuthority access,ScopeAuthorizationLeaseV1 lease,CancellationToken ct)
    {
        await Final(access.Actor,lease,ct);
        if(access.Grant is {} grant && (Clock().LocalDateTime<grant.ValidFrom||Clock().LocalDateTime>grant.ValidTo))throw Error("DELEGATION_EXPIRED",403);
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
            await WriteReady(lease,ct);
            if(string.IsNullOrEmpty(ifMatch))throw Error("PRECONDITION_REQUIRED",428);
            if(ifMatch!="\""+input.TaskRowVersion+"\"")throw Error("PRECONDITION_FAILED",412);
            var source=await TaskSourceAsync(task,ct);var access=await TaskAccess(source,actor,behalf,true,ct);var before=await TaskView(task,ct);
            if(before.TaskRowVersion!=input.TaskRowVersion||before.InstanceRowVersion!=input.InstanceRowVersion)throw Error("PRECONDITION_FAILED",412);
            var write=Guid.NewGuid();await BeginCommand(write,actor.UserId,Decision,resource,command,null,source.Data,source.Root,task,ct);
            var engine=new FlowEngine(db,new ApproverResolver(db,exactConditions:true));
            var root=await engine.StagePersonalDecisionV2(fence.EnvironmentId,source.Data,source.Root,Parse<FlowPublicationV2>(source.Publication),
                access.Assignment,actor.UserId,access.OnBehalfOf,input.Decision=="approve",input.Comment,ct);
            await AssignmentNotifications(write,await engine.SavePersonalRuntime(write,ct),ct);
            var vote=Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_personal_form_v2.votes(id,command_id,task_id,body)
                VALUES({vote},{write},{task},{Encode(new{approved=input.Decision=="approve",input.Comment,Principal=access.Principal,Actual=actor.UserId,access.OnBehalfOf,access.Grant,access.Assignment,Scope=lease.Snapshot,Occurred=Clock()})})
                """,ct);
            await Notification(write,"TASK_DECIDED",actor.UserId,WfNotificationType.TaskHandled,"待办已办理",before.InstanceId,task,root.FlowKey,ct);
            if(root.Status is FlowInstanceStatus.Approved or FlowInstanceStatus.Rejected)await TerminalNotification(write,root,ct);
            await db.SaveChangesAsync(ct);
            var result=new PersonalFormTaskResultV2(input.CommandId,"COMPLETED",vote,await TaskView(task,ct),
                "/api/oa/v2/personal-forms/tasks/"+task+"/commands/"+input.CommandId);
            await Complete(write,command,task,result.Task.TaskRowVersion,result,new{Before=before,After=result.Task,access.Principal,Actor=actor.UserId,access.Grant,access.Mask,Scope=lease.Snapshot},ct);
            await FinalTask(access,lease,ct);committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
}
