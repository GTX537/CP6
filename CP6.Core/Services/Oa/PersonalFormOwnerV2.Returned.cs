using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class PersonalFormOwnerV2
{
    private const string SendBack="OA_TASK_SENDBACK",ReturnedEdit="SFS_RETURNED_EDIT",ReturnedResubmit="SFS_RETURNED_RESUBMIT";
    private sealed record ReturnedSource(Guid Data,Guid Head,Guid FormVersion,string Publication,Guid Starter);
    private sealed record ReturnRecord(Guid Id,long Revision,Guid Cycle,string Snapshot,string Digest);
    private async Task<ReturnedSource> ReturnedSourceAsync(Guid instance,CancellationToken ct)=>await db.Database.SqlQuery<ReturnedSource>($"""
        SELECT s.form_data_id AS "Data",s.head_id AS "Head",s.form_version AS "FormVersion",v.snapshot_json AS "Publication",s.actor_id AS "Starter"
        FROM cp6_personal_form_v2.submissions s JOIN cp6_workflow_authoring_v2.versions v
          ON v.tenant_id=s.tenant_id AND v.environment_id=s.environment_id AND v.head_id=s.flow_head AND v.id=s.flow_version
        WHERE s.root_id={instance} AND s.tenant_id={db.CurrentTenantId} AND s.environment_id={fence.EnvironmentId}
        """).SingleOrDefaultAsync(ct)??throw Error("RETURNED_SFS_REQUIRED",404);
    private async Task<ReturnRecord> ReturnRecordAsync(Guid instance,CancellationToken ct)=>await db.Database.SqlQuery<ReturnRecord>($"""
        SELECT id AS "Id",revision AS "Revision",execution_cycle_id AS "Cycle",snapshot_json AS "Snapshot",snapshot_digest AS "Digest"
        FROM cp6_personal_form_v2.returns WHERE instance_id={instance} ORDER BY revision DESC LIMIT 1
        """).SingleOrDefaultAsync(ct)??throw Error("RETURN_CONTEXT_UNAVAILABLE",409);
    private async Task<Guid> ExecutionCycle(Guid root,CancellationToken ct)=>await db.Database.SqlQuery<Guid>($"""
        SELECT id AS "Value" FROM cp6_personal_form_v2.execution_cycles WHERE root_id={root} ORDER BY cycle_number DESC LIMIT 1
        """).SingleAsync(ct);
    private static void Reason(string? value,bool required)
    {
        if(required&&string.IsNullOrWhiteSpace(value)||value?.EnumerateRunes().Count()>1000)throw Error("REASON_INVALID",422);
    }
    private async Task<Dictionary<string,string>> ReturnedAccess(ReturnedSource source,UserPermissionContext actor,CancellationToken ct,string? action=null)
    {
        if(source.Starter!=actor.UserId)throw Error("RESOURCE_NOT_FOUND",404);
        await Action(actor,action??"query",ct,"oa-inbox");
        var policy=await Policy(source.Head,ct)??throw Error("FORM_USE_POLICY_REQUIRED",403);
        if(!policy.Enabled)throw Error("FORM_USE_REVOKED",403);
        var mask=await Mask(actor,source.Head,await Publication(source.Head,source.FormVersion,ct),ct);
        var flow=Parse<FlowPublicationV2>(source.Publication);var graph=JsonSerializer.Deserialize<FlowSchema>(flow.Fields["SchemaJson"]!,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var start=graph.Nodes.Single(n=>n.Id==(string.IsNullOrEmpty(graph.Start)?graph.Nodes[0].Id:graph.Start));
        if(start.FieldPerms is {} fields)foreach(var name in mask.Keys.ToArray())
        {
            var pinned=fields.GetValueOrDefault(name,"edit");
            mask[name]=mask[name]=="hidden"||pinned=="hidden"?"hidden":mask[name]=="readonly"||pinned=="readonly"?"readonly":"edit";
        }
        return mask;
    }
    public async Task<ReturnedFormContextV2> ReturnedFormAsync(Guid instance,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);
        var actor=await Actor(ct);var source=await ReturnedSourceAsync(instance,ct);var mask=await ReturnedAccess(source,actor,ct);
        var record=await ReturnRecordAsync(instance,ct);var i=await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==instance,ct);
        var data=await db.Wf_FormDatas.AsNoTracking().SingleAsync(f=>f.Id==source.Data,ct);var publication=await Publication(source.Head,source.FormVersion,ct);
        var reasons=new List<string>();if(i.Status!=FlowInstanceStatus.Draft)reasons.Add("ALREADY_RESUBMITTED");
        var editable=actor.ActionKeys.Contains("oa-inbox:returned-edit")&&reasons.Count==0;
        var resubmit=actor.ActionKeys.Contains("oa-inbox:resubmit")&&reasons.Count==0;
        if(!editable)reasons.Add("RETURNED_EDIT_UNAVAILABLE");if(!resubmit)reasons.Add("RETURNED_RESUBMIT_UNAVAILABLE");
        var result=new ReturnedFormContextV2(instance,data.Id,record.Revision,await ExecutionCycle(instance,ct),source.FormVersion,i.FlowDefVersionId!.Value,
            Convert.ToBase64String(i.RowVersion!),Convert.ToBase64String(data.RowVersion!),"returned-snapshot/"+record.Id,record.Digest,
            publication.Fields["Name"]!,ProjectSchema(publication.Fields["SchemaJson"]!,mask),PersonalFormValuesV2.Project(data.DataJson,mask),mask,editable,resubmit,reasons.ToArray(),i.Status);
        await Final(actor,lease,ct,false);return result;
    }
    public async Task<NativeSendBackPreviewV2> SendBackPreviewAsync(Guid task,NativeSendBackTargetV2 target,Guid? behalf=null,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);await WriteReady(lease,ct);
        var actor=await Actor(ct);var source=await TaskSourceAsync(task,ct);await Action(actor,"sendback",ct,"oa-inbox");
        var access=await TaskAccess(source,actor,behalf,true,ct,"TASK_SENDBACK");
        if(access.OnBehalfOf!=null)await Action(await PostgreSqlPermissionReader.ReadAsync(db,access.Principal,ct),"sendback",ct,"oa-inbox");
        var result=await new FlowEngine(db,new ApproverResolver(db,exactConditions:true)).PreviewSendBackV2(task,source.Root,Parse<FlowPublicationV2>(source.Publication),target,ct);
        await FinalTask(access,lease,ct);return result;
    }
    public async Task<PersonalFormResultV2> SendBackAsync(Guid task,byte[] raw,Guid? behalf=null,CancellationToken ct=default)
    {
        var command=CommandInput<NativeSendBackInputV2>(raw,true,true);Reason(command.Body.Comment,true);
        if(!RuntimeVersion(command.Body.ExpectedTaskVersion)||command.Body.PreviewDigest==null)throw Error("COMMAND_SHAPE_INVALID",400);
        // Acting-as changes the operation's meaning and must remain bound to the original slot.
        command=command with{Digest=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{command.Digest,behalf}))};
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await Action(actor,"query",ct,"oa-inbox");
            var old=await Original(actor.UserId,SendBack,task.ToString("D"),command.Id,ct);
            if(old!=null){Match(old,command.Digest);var replay=await DiscloseTaskOperation(old,actor,ct);await Final(actor,lease,ct,false);return replay;}
            await WriteReady(lease,ct);await Action(actor,"sendback",ct,"oa-inbox");var source=await TaskSourceAsync(task,ct);
            var access=await TaskAccess(source,actor,behalf,true,ct,"TASK_SENDBACK");var publication=Parse<FlowPublicationV2>(source.Publication);
            if(access.OnBehalfOf!=null)await Action(await PostgreSqlPermissionReader.ReadAsync(db,access.Principal,ct),"sendback",ct,"oa-inbox");
            var target=new NativeSendBackTargetV2(command.Body.TargetKind,command.Body.TargetNodeId);var engine=new FlowEngine(db,new ApproverResolver(db,exactConditions:true));
            var preview=await engine.PreviewSendBackV2(task,source.Root,publication,target,ct);
            if(preview.TaskVersion!=command.Body.ExpectedTaskVersion||preview.InstanceVersion!=command.Expected||preview.PreviewDigest!=command.Body.PreviewDigest)throw Error("SENDBACK_PREVIEW_CHANGED",409);
            var write=Guid.NewGuid();await BeginCommand(write,actor.UserId,SendBack,task.ToString("D"),command,null,source.Data,source.Root,task,ct);
            long? revision=null;
            if(target.TargetKind=="STARTER")
            {
                var data=await db.Wf_FormDatas.AsNoTracking().SingleAsync(f=>f.Id==source.Data,ct);
                revision=await db.Database.SqlQuery<long>($"SELECT COALESCE(max(revision),0)+1 AS \"Value\" FROM cp6_personal_form_v2.returns WHERE instance_id={source.Root}").SingleAsync(ct);
                var root=await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==source.Root,ct);var cycle=await ExecutionCycle(source.Root,ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_personal_form_v2.returns(id,instance_id,revision,command_id,execution_cycle_id,form_version,flow_version,form_data_version,snapshot_json,snapshot_digest,reason,actual_id)
                    VALUES({Guid.NewGuid()},{source.Root},{revision},{write},{cycle},{source.FormVersion},{root.FlowDefVersionId},{data.RowVersion},
                      {data.DataJson},{ApprovalCanonicalV2.Hash(System.Text.Encoding.UTF8.GetBytes(data.DataJson))},{command.Body.Comment},{actor.UserId})
                    """,ct);
            }
            var runtime=await engine.StagePersonalSendBackV2(fence.EnvironmentId,source.Data,source.Root,publication,task,actor.UserId,access.OnBehalfOf,target,command.Body.Comment,ct);
            await AssignmentNotifications(write,await engine.SavePersonalRuntime(write,ct),ct);
            await Notification(write,"TASK_SENT_BACK",runtime.StarterId,WfNotificationType.FormSubmitted,"流程已退回",runtime.Id,task,runtime.FlowKey,ct);
            await db.SaveChangesAsync(ct);
            var view=await TaskView(task,ct);var result=await Complete(write,command,source.Root,Convert.ToBase64String(runtime.RowVersion!),
                new{InstanceId=source.Root,InstanceStatus=runtime.Status,Task=view,ReturnRevision=revision,ExecutionCycleId=await ExecutionCycle(source.Root,ct)},
                new{preview,access.Principal,Actor=actor.UserId,access.Grant,Scope=lease.Snapshot},ct);
            await FinalTask(access,lease,ct);committing=true;await tx.CommitAsync(ct);db.ChangeTracker.Clear();return result;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
    public Task<PersonalFormResultV2> EditReturnedAsync(Guid instance,byte[] raw,CancellationToken ct=default)
        =>ChangeReturned(instance,CommandInput<ReturnedFormEditV2>(raw,true,true),false,ct);
    public Task<PersonalFormResultV2> ResubmitReturnedAsync(Guid instance,byte[] raw,CancellationToken ct=default)
        =>ChangeReturned(instance,CommandInput<ReturnedFormResubmitV2>(raw,true,true),true,ct);
    private async Task<PersonalFormResultV2> ChangeReturned<T>(Guid instance,Command<T> command,bool resubmit,CancellationToken ct)
    {
        var edit=command.Body as ReturnedFormEditV2;var send=command.Body as ReturnedFormResubmitV2;
        var revision=edit?.ReturnRevision??send!.ReturnRevision;var dataVersion=edit?.ExpectedFormDataVersion??send!.ExpectedFormDataVersion;
        if(revision<=0||!RuntimeVersion(dataVersion))throw Error("COMMAND_SHAPE_INVALID",400);Reason(edit?.Reason??send?.Comment,false);
        var operation=resubmit?ReturnedResubmit:ReturnedEdit;
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);var source=await ReturnedSourceAsync(instance,ct);
            var mask=await ReturnedAccess(source,actor,ct);var old=await Original(actor.UserId,operation,instance.ToString("D"),command.Id,ct);
            if(old!=null){Match(old,command.Digest);var replay=await DiscloseTaskOperation(old,actor,ct);await Final(actor,lease,ct,false);return replay;}
            await WriteReady(lease,ct);await Action(actor,resubmit?"resubmit":"returned-edit",ct,"oa-inbox");
            var record=await ReturnRecordAsync(instance,ct);var runtime=await db.Wf_FlowInstances.SingleAsync(i=>i.Id==instance,ct);
            if(runtime.Status!=FlowInstanceStatus.Draft)throw Error("ALREADY_RESUBMITTED",409);
            var data=await db.Wf_FormDatas.SingleAsync(f=>f.Id==source.Data,ct);
            if(record.Revision!=revision||Convert.ToBase64String(runtime.RowVersion!)!=command.Expected||Convert.ToBase64String(data.RowVersion!)!=dataVersion)throw Error("RETURN_CONTEXT_CHANGED",409);
            var publication=await Publication(source.Head,source.FormVersion,ct);var schema=PersonalFormValuesV2.Schema(publication.Fields["SchemaJson"]!);
            var before=data.DataJson;var merged=resubmit?before:PersonalFormValuesV2.Patch(schema,edit!.DataPatch,before,mask);
            var computed=PersonalFormValuesV2.Recompute(schema,merged);
            if(resubmit&&computed.Errors.Length>0)throw Error("FORM_VALIDATION_FAILED",422);
            await References(schema,computed.Data,ct);
            await CheckFiles(source.Head,source.FormVersion,"SUBMISSION",data.Id,actor.UserId,schema,computed.Data,before,resubmit,mask,ct);
            var write=Guid.NewGuid();await BeginCommand(write,actor.UserId,operation,instance.ToString("D"),command,null,data.Id,instance,null,ct);
            Guid cycle=record.Cycle;
            if(resubmit)
            {
                if(data.DataJson!=computed.Data)
                {data.DataJson=computed.Data;data.Modifier=actor.UserId.ToString("D");data.ModifyDate=DateTime.Now;}
                cycle=Guid.NewGuid();await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_personal_form_v2.execution_cycles(id,root_id,cycle_number,command_id,return_revision)
                    SELECT {cycle},{instance},max(cycle_number)+1,{write},{revision} FROM cp6_personal_form_v2.execution_cycles WHERE root_id={instance}
                    """,ct);
                var engine=new FlowEngine(db,new ApproverResolver(db,exactConditions:true));
                runtime=await engine.StagePersonalResubmitV2(fence.EnvironmentId,data.Id,instance,Parse<FlowPublicationV2>(source.Publication),actor.UserId,computed.Data,send!.Comment,ct);
                await AssignmentNotifications(write,await engine.SavePersonalRuntime(write,ct),ct);
                await Notification(write,"FORM_RESUBMITTED",actor.UserId,WfNotificationType.FormSubmitted,"表单已重新送审",instance,null,runtime.FlowKey,ct);
                if(runtime.Status is FlowInstanceStatus.Approved or FlowInstanceStatus.Rejected)await TerminalNotification(write,runtime,ct);
            }
            else
            {
                data.DataJson=computed.Data;data.Modifier=actor.UserId.ToString("D");data.ModifyDate=DateTime.Now;
                runtime.VarsJson=computed.Data;runtime.Modifier=actor.UserId.ToString("D");runtime.ModifyDate=DateTime.Now;
                db.Entry(data).Property(f=>f.DataJson).IsModified=true;
                db.Entry(runtime).Property(i=>i.VarsJson).IsModified=true;
            }
            await db.SaveChangesAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_personal_form_v2.returned_history(command_id,instance_id,return_revision,before_json,after_json,form_data_version,instance_version)
                VALUES({write},{instance},{revision},{before},{computed.Data},{data.RowVersion},{runtime.RowVersion})
                """,ct);
            await BindFiles(write,source.FormVersion,"SUBMISSION",data.Id,Convert.ToBase64String(data.RowVersion!),schema,computed.Data,ct);
            var tasks=await db.Wf_FlowTasks.AsNoTracking().Where(t=>t.InstanceId==instance&&t.Status==FlowTaskStatus.Pending).Select(t=>t.Id).ToArrayAsync(ct);
            // Field-specific validation is projected through the current context. Original results
            // contain no private field names or data that could outlive a field permission change.
            var resolutions=await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM cp6_native_tasks_v2.pending_resolutions WHERE root_id={instance} AND execution_cycle_id={cycle} AND state='PENDING' ORDER BY id").ToArrayAsync(ct);
            var value=new ReturnedFormResultV2(instance,data.Id,revision,cycle,runtime.Status,Convert.ToBase64String(runtime.RowVersion!),Convert.ToBase64String(data.RowVersion!),tasks,
                computed.Errors.Length==0?[]:["FORM_VALIDATION_FAILED"],source.FormVersion,runtime.FlowDefVersionId!.Value,resolutions);
            var result=await Complete(write,command,instance,value.InstanceVersion,value,new{actor.UserId,Scope=lease.Snapshot,record.Id,mask,Reason=edit?.Reason??send?.Comment},ct);
            await Final(actor,lease,ct);committing=true;await tx.CommitAsync(ct);db.ChangeTracker.Clear();return result;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
    private async Task<PersonalFormResultV2> DiscloseTaskOperation(StoredCommand old,UserPermissionContext actor,CancellationToken ct)
    {
        if(old.Operation is SendBack or Transfer or AddSign)
        {var source=await TaskSourceAsync(Guid.Parse(old.Resource),ct);await Mask(actor,source.FormHead,await Publication(source.FormHead,source.FormVersion,ct),ct);}
        else if(old.Operation==ResolveApprover){var row=await Resolution(Guid.Parse(old.Resource),ct);await ResolutionAccess(await PersonalInstanceSource(row.Instance,ct),actor,ct);}
        else await ReturnedAccess(await ReturnedSourceAsync(Guid.Parse(old.Resource),ct),actor,ct);
        return Parse<PersonalFormResultV2>(old.Result);
    }
}
