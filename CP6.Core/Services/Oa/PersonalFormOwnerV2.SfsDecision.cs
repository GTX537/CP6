using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SfsTaskDecisionInputV2(string Kind,Guid TaskId,string Decision,JsonElement DataPatch,
    string ExpectedTaskVersion,string ExpectedInstanceVersion,string ExpectedFormDataVersion,string? Comment=null);
public sealed record SfsTaskDecisionResultV2(Guid VoteId,ApprovalTaskDecisionViewV2 Task,Guid FormDataId,string FormDataVersion);

public sealed partial class PersonalFormOwnerV2
{
    internal const string SfsDecision="SFS_TASK_DECISION";
    public async Task<PersonalFormResultV2> DecideSfsTaskAsync(Guid task,byte[] raw,Guid? behalf=null,CancellationToken ct=default)
    {
        var command=CommandInput<SfsTaskDecisionInputV2>(raw,true,true);var input=command.Body;
        if(input.Kind!="SFS"||input.TaskId!=task||task==Guid.Empty||input.Decision is not("APPROVE" or "REJECT")
            ||!RuntimeVersion(input.ExpectedTaskVersion)||!RuntimeVersion(input.ExpectedInstanceVersion)||!RuntimeVersion(input.ExpectedFormDataVersion)
            ||input.DataPatch.ValueKind!=JsonValueKind.Object)throw Error("COMMAND_SHAPE_INVALID",400);
        if(command.Expected!=input.ExpectedTaskVersion)throw Error("PRECONDITION_MISMATCH",400);
        Reason(input.Comment,false);
        command=command with{Digest=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{command.Digest,behalf}))};
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await Action(actor,"query",ct,"oa-inbox");
            var old=await Original(actor.UserId,SfsDecision,task.ToString("D"),command.Id,ct);
            if(old!=null){Match(old,command.Digest);var replay=await DiscloseTaskOperation(old,actor,ct);await Final(actor,lease,ct,false);return replay;}
            await WriteReady(lease,ct);var source=await TaskSourceAsync(task,ct);
            var access=await TaskAccess(source,actor,behalf,true,ct,input.Decision=="APPROVE"?"TASK_APPROVE":"TASK_REJECT");
            var before=await TaskView(task,ct);var data=await db.Wf_FormDatas.SingleAsync(f=>f.Id==source.Data,ct);
            if(before.TaskRowVersion!=input.ExpectedTaskVersion||before.InstanceRowVersion!=input.ExpectedInstanceVersion
                ||Convert.ToBase64String(data.RowVersion!)!=input.ExpectedFormDataVersion)throw Error("TASK_CONTEXT_CHANGED",409);
            var form=await Publication(source.FormHead,source.FormVersion,ct);var schema=PersonalFormValuesV2.Schema(form.Fields["SchemaJson"]!);
            var beforeData=data.DataJson;var patch=PersonalFormValuesV2.Patch(schema,input.DataPatch,beforeData,access.Mask);
            var computed=PersonalFormValuesV2.Recompute(schema,patch);
            if(computed.Errors.Length>0)throw Error("FORM_VALIDATION_FAILED",422);
            await References(schema,computed.Data,ct);await CheckOptions(schema,computed.Data,beforeData,true,ct);
            await CheckFiles(source.FormHead,source.FormVersion,"SUBMISSION",data.Id,actor.UserId,schema,computed.Data,beforeData,true,access.Mask,ct);
            var write=Guid.NewGuid();await BeginCommand(write,actor.UserId,SfsDecision,task.ToString("D"),command,null,data.Id,source.Root,task,ct);
            if(beforeData!=computed.Data)
            {
                data.DataJson=computed.Data;data.Modifier=actor.UserId.ToString("D");data.ModifyDate=DateTime.Now;
                // A shared form edit changes live consumers' versions. Preserve unchanged mapped
                // variables and child collection coordinates; propagate the form delta, including computes.
                var instances=await db.Wf_FlowInstances.Where(i=>i.FormDataId==data.Id&&i.Creator==FlowEngine.PersonalFormRuntimeV2
                    &&(i.Status==FlowInstanceStatus.Running||i.Status==FlowInstanceStatus.Suspended)).ToArrayAsync(ct);
                foreach(var instance in instances)
                {
                    instance.VarsJson=MergeTaskFormDelta(instance.VarsJson,beforeData,computed.Data,instance.ParentInstanceId!=null);
                    instance.Modifier=actor.UserId.ToString("D");instance.ModifyDate=DateTime.Now;
                }
            }
            var engine=new FlowEngine(db,new ApproverResolver(db,exactConditions:true));
            var root=await engine.StagePersonalDecisionV2(fence.EnvironmentId,source.Data,source.Root,Parse<FlowPublicationV2>(source.Publication),
                access.Assignment,actor.UserId,access.OnBehalfOf,input.Decision=="APPROVE",input.Comment??"",ct);
            await AssignmentNotifications(write,await engine.SavePersonalRuntime(write,ct),ct);
            var vote=Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_personal_form_v2.votes(id,command_id,task_id,body)
                VALUES({vote},{write},{task},{Encode(new{approved=input.Decision=="APPROVE",input.Comment,Principal=access.Principal,Actual=actor.UserId,
                    access.OnBehalfOf,access.Grant,access.Assignment,Scope=lease.Snapshot,Occurred=Clock(),BeforeData=beforeData,AfterData=computed.Data,
                    BeforeFormDataVersion=input.ExpectedFormDataVersion,FormDataVersion=Convert.ToBase64String(data.RowVersion!),Before=before})})
                """,ct);
            await BindFiles(write,source.FormVersion,"SUBMISSION",data.Id,Convert.ToBase64String(data.RowVersion!),schema,computed.Data,ct);
            await Notification(write,"TASK_DECIDED",actor.UserId,WfNotificationType.TaskHandled,"待办已办理",before.InstanceId,task,root.FlowKey,ct);
            if(root.Status is FlowInstanceStatus.Approved or FlowInstanceStatus.Rejected)await TerminalNotification(write,root,ct);
            await db.SaveChangesAsync(ct);
            var value=new SfsTaskDecisionResultV2(vote,await TaskView(task,ct),data.Id,Convert.ToBase64String(data.RowVersion!));
            var result=await Complete(write,command,task,value.Task.TaskRowVersion,value,
                new{Before=before,After=value.Task,access.Principal,Actor=actor.UserId,access.Grant,access.Mask,Scope=lease.Snapshot},ct);
            await FinalTask(access,lease,ct);committing=true;await tx.CommitAsync(ct);db.ChangeTracker.Clear();return result;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
    private static string MergeTaskFormDelta(string vars,string before,string after,bool child)
    {
        var result=JsonNode.Parse(vars)!.AsObject();var previous=JsonNode.Parse(before)!.AsObject();var current=JsonNode.Parse(after)!.AsObject();
        // Root form fields named item/itemIndex remain legal. In a collection child these names
        // identify its frozen input row, so only the shared FormData/root vars receive such edits.
        var collection=child&&result.ContainsKey("item")&&result.ContainsKey("itemIndex");
        bool Coordinate(string name)=>collection&&name is "item" or "itemIndex";
        foreach(var field in current)
            if(!Coordinate(field.Key)&&(!previous.TryGetPropertyValue(field.Key,out var value)||!JsonNode.DeepEquals(value,field.Value)))result[field.Key]=field.Value?.DeepClone();
        foreach(var name in previous.Select(f=>f.Key).Except(current.Select(f=>f.Key)).Where(n=>!Coordinate(n)))result.Remove(name);
        return result.ToJsonString();
    }
}
