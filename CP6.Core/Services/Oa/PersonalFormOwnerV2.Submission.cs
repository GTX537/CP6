using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class PersonalFormOwnerV2
{
    private static void ExpectedContext(Admission source, Guid form, string activation, string? binding,
        Guid? flow, string? flowActivation)
    {
        if(form != source.Form.VersionId || activation != source.Activation.Revision.ToString("D")
            || binding != source.Binding?.Revision.ToString("D") || flow != source.Flow?.VersionId
            || flowActivation != source.Binding?.Revision.ToString("D")) throw Error("FORM_CONTEXT_CHANGED",409);
    }
    public Task<PersonalFormResultV2> SubmitDirectAsync(string key, byte[] raw, CancellationToken ct=default)
    {
        var command=CommandInput<PersonalFormDirectV2>(raw,false);
        if(command.Body.Kind!="DIRECT")throw Error("COMMAND_SHAPE_INVALID",400);
        return SubmitForm(key,null,command,ct);
    }
    public Task<PersonalFormResultV2> SubmitDraftAsync(Guid draft, byte[] raw, CancellationToken ct=default)
    {
        var command=CommandInput<PersonalFormDraftSubmitV2>(raw,true);
        if(command.Body.Kind!="DRAFT")throw Error("COMMAND_SHAPE_INVALID",400);
        return SubmitForm(null,draft,command,ct);
    }
    private async Task<PersonalFormResultV2> SubmitForm<T>(string? key, Guid? draft, Command<T> command, CancellationToken ct)
    {
        var operation=draft==null?Direct:Submit;var resource=draft?.ToString("D")??key!;
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await Action(actor,"query",ct);
            var old=await Original(actor.UserId,operation,resource,command.Id,ct);
            if(old!=null)
            {
                Match(old,command.Digest);
                var replay=await Disclose(old,actor,ct);await Final(actor,lease,ct,false);return replay;
            }
            await WriteReady(lease,ct);await Action(actor,"submit",ct);
            DraftRow? row=null;
            if(draft is Guid id)
            {
                row=await Owned(id,actor.UserId,ct);Active(row);Expected(row,command.Expected);
                key=(await Heads().SingleAsync(h=>h.Id==row.Head,ct)).Key;
            }
            var source=await Resolve(actor,key!,true,ct);string data;
            if(command.Body is PersonalFormDirectV2 direct)
            {
                ExpectedContext(source,direct.ExpectedFormVersionId,direct.ExpectedActivationRevision,direct.ExpectedBindingRevision,
                    direct.ExpectedFlowVersionId,direct.ExpectedFlowActivationRevision);
                var schema=source.Form.Fields["SchemaJson"]!;
                data=PersonalFormValuesV2.Merge(PersonalFormValuesV2.Schema(schema),direct.Data,PersonalFormValuesV2.Initial(schema),source.Mask);
            }
            else
            {
                var input=(PersonalFormDraftSubmitV2)(object)command.Body!;
                ExpectedContext(source,input.ExpectedFormVersionId,input.ExpectedActivationRevision,input.ExpectedBindingRevision,
                    input.ExpectedFlowVersionId,input.ExpectedFlowActivationRevision);
                if(row!.Version!=source.Form.VersionId)throw Error("DRAFT_REBASE_REQUIRED",409);
                data=row.Data;
            }
            var computed=PersonalFormValuesV2.Recompute(PersonalFormValuesV2.Schema(source.Form.Fields["SchemaJson"]!),data);
            // Error details can contain currently hidden field names. They remain a server concern.
            if(computed.Errors.Length!=0)throw Error("FORM_VALIDATION_FAILED",422);
            await References(PersonalFormValuesV2.Schema(source.Form.Fields["SchemaJson"]!),computed.Data,ct);
            var write=Guid.NewGuid();var dataId=Guid.NewGuid();Guid? root=source.Flow==null?null:Guid.NewGuid();var at=Clock();
            await BeginCommand(write,actor.UserId,operation,resource,command,draft,dataId,root,null,ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_personal_form_v2.submissions(form_data_id,tenant_id,environment_id,actor_id,command_id,draft_id,
                  head_id,form_version,form_activation,binding_revision,flow_head,flow_version,flow_activation,root_id,title,submitted_at)
                VALUES({dataId},{db.CurrentTenantId},{fence.EnvironmentId},{actor.UserId},{write},{draft},
                  {source.Head.Id},{source.Form.VersionId},{source.Activation.Revision},{source.Binding?.Revision},
                  {source.Flow?.DefinitionId},{source.Flow?.VersionId},{source.Binding?.Revision},{root},{row?.Title},{at})
                """,ct);
            db.Wf_FormDatas.Add(new Wf_FormData
            {
                Id=dataId,FormDefVersionId=source.Form.VersionId,FormKey=key!,FormVersion=source.Form.Version,
                SubmissionKey="pf:"+write.ToString("N"),RequestHash=command.Digest,SubmittedBy=actor.UserId,
                SubmittedAtUtc=at.UtcDateTime,DataJson=computed.Data,BizId=root?.ToString("D"),Creator=actor.UserId.ToString("D")
            });
            Wf_FlowInstance? runtime=null;
            if(source.Flow!=null)
            {
                var engine=new FlowEngine(db,new ApproverResolver(db,exactConditions:true));
                runtime=await engine.StagePersonalStartV2(fence.EnvironmentId,dataId,root!.Value,source.Flow,actor.UserId,computed.Data,ct);
                await AssignmentNotifications(write,await engine.SavePersonalRuntime(write,ct),ct);
            }
            string? token=null;
            if(row!=null)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_personal_form_v2.drafts SET status=1,form_data_id={dataId} WHERE id={row.Id}",ct);
                await History(write,row.Id,row.Snapshot,ct);token=(await Owned(row.Id,actor.UserId,ct)).Token.ToString("D");
            }
            await Notification(write,"FORM_SUBMITTED",actor.UserId,WfNotificationType.FormSubmitted,"表单已提交",root,null,source.Flow?.Key,ct);
            if(runtime?.Status is FlowInstanceStatus.Approved or FlowInstanceStatus.Rejected)await TerminalNotification(write,runtime,ct);
            await db.SaveChangesAsync(ct);
            var value=new PersonalFormSubmissionV2(dataId,source.Form.VersionId,source.Form.Version,root,source.Flow?.VersionId,
                source.Flow?.Version,"/api/oa/v2/personal-forms/commands/"+operation+"/"+Uri.EscapeDataString(resource)+"/"+command.Id,
                command.Id,source.Binding?.Revision.ToString("D"),root==null?"SUBMITTED_UNBOUND":"SUBMITTED");
            var result=await Complete(write,command,dataId,token,value,new{Scope=lease.Snapshot,actor.UserId,source.Policy.Version,
                Form=source.Form.VersionId,FormActivation=source.Activation.Revision,Binding=source.Binding?.Revision,Mask=source.Mask},ct);
            await Final(actor,lease,ct);committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
    private async Task AssignmentNotifications(Guid command,IReadOnlyList<PersonalFormAssignmentV2> assignments,CancellationToken ct)
    {
        foreach(var assignment in assignments)
        {
            var instance=db.Wf_FlowInstances.Local.Single(i=>i.Id==assignment.InstanceId);
            await Notification(command,"TASK_ASSIGNED",assignment.PrincipalId,WfNotificationType.TodoCreated,"您有新的待办",
                assignment.InstanceId,assignment.TaskId,instance.FlowKey,ct);
        }
        foreach(var instanceId in db.ChangeTracker.Entries<Wf_FlowHistory>().Where(e=>e.State==EntityState.Added&&e.Entity.Action=="branchPruned").Select(e=>e.Entity.InstanceId).Distinct().ToArray())
        {
            var instance=db.Wf_FlowInstances.Local.Single(i=>i.Id==instanceId);
            await Notification(command,"BRANCH_PRUNED",instance.StarterId,WfNotificationType.BranchPruned,"流程分支已终止",instance.Id,null,instance.FlowKey,ct);
        }
    }
    private Task TerminalNotification(Guid command,Wf_FlowInstance instance,CancellationToken ct)=>Notification(command,
        "FLOW_FINISHED",instance.StarterId,instance.Status==FlowInstanceStatus.Approved?WfNotificationType.FlowApproved:WfNotificationType.FlowRejected,
        instance.Status==FlowInstanceStatus.Approved?"您的申请已通过":"您的申请被驳回",instance.Id,null,instance.FlowKey,ct);
    private async Task Notification(Guid command,string kind,Guid recipient,int type,string title,Guid? instance,Guid? task,string? flow,CancellationToken ct)
    {
        var matrix=NotifyMatrix.Rows().Single(x=>x.TypeValue==type);var prefs=new PrefService(db);
        var inApp=matrix.InAppSupported && await prefs.IsEnabledAsync(recipient,matrix.TypeKey,NotifyMatrix.ChannelInApp);
        var email=matrix.EmailSupported && await prefs.IsEnabledAsync(recipient,matrix.TypeKey,NotifyMatrix.ChannelEmail);
        var id=Guid.NewGuid();var notification=new Wf_Notification
        {
            Id=id,UserId=recipient,Type=type,Title=title,Body=title,InstanceId=instance,TaskId=task,FlowKey=flow,
            EventKey="pf:"+id.ToString("N"),InAppRequested=inApp,EmailRequested=email,
            DispatchStatus=0,NextAttemptAtUtc=Clock().UtcDateTime
        };
        db.Wf_Notifications.Add(notification);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_personal_form_v2.outbox(id,command_id,kind,recipient_id,task_id,body,notification_id)
            VALUES({id},{command},{kind},{recipient},{task},{Encode(new{type,title,instance,task,flow,inApp,email})},{id})
            """,ct);
    }
}
