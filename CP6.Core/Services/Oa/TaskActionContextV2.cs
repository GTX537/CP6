using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed record TaskActionContextV2(Guid TaskId,Guid InstanceId,string ContextKind,Guid FlowDefVersionId,Guid? FormDefVersionId,
    string TaskRowVersion,string InstanceRowVersion,string? FormDataVersion,Guid TokenId,string NodeId,int StageIndex,int StageRound,
    Guid ExpectedAssigneeId,Guid CurrentPrincipalId,Guid CreatedPrincipalId,Guid ActualHandlerId,Guid? OnBehalfOfId,
    int CurrentStatus,int InstanceStatus,string[] AllowedActions,Dictionary<string,string> FieldMask,Guid? GrantId,string? GrantRevision,
    ApprovalRefV2? ApprovalSubjectRef,PersonalFormTaskContextV2? Form,bool CommentRequired,int CommentLimit,string CommandIdProfile);

public sealed partial class PersonalFormOwnerV2
{
    public Task<TaskActionContextV2> ActionContextAsync(Guid task,Guid? behalf=null,CancellationToken ct=default)
        =>ReadTaskContext(task,behalf,(c,a)=>new TaskActionContextV2(c.Task.TaskId,c.Task.InstanceId,"SFS",c.FlowVersionId,c.FormVersionId,
            c.Task.TaskRowVersion,c.Task.InstanceRowVersion,c.FormDataVersion,c.TokenId,c.NodeId,c.StageIndex,c.StageRound,
            a.Assignment.PrincipalId,a.Principal,a.Assignment.CreatedPrincipalId??a.Assignment.PrincipalId,a.Actor.UserId,a.OnBehalfOf,
            c.Task.TaskStatus,c.Task.InstanceStatus,c.AllowedDecisions.Select(d=>d.ToUpperInvariant()).ToArray(),a.Mask,a.Grant?.GrantId,a.Grant?.Revision,
            null,c,false,1000,"WFC_STRING_1"),ct);
}
public sealed partial class ApprovalTaskDecisionOwnerV2
{
    private static bool RequiredReviewComment(ApprovalSubmitCommandV2 original)=>original.Subject.BusinessOwner is
        "Main.Plm.ControlledDocument.v1" or "Main.Plm.RequirementSet.v1" or "Main.Plm.EngineeringIteration.v1" or "Main.Wms.LocationRuleProposal.v1";
    public async Task<TaskActionContextV2> ActionContextAsync(Guid task,Guid? behalf=null,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);
        var lease=await authority.AcquireAsync(task,behalf,ct,"TASK_READ");await Ready(ct);var source=await ReadSource(task,ct);
        var original=ApprovalSubmissionWireV2.Parse(source.Payload).Value;await Business.Decision(original,lease.Evidence.PrincipalId,false,ct);
        var view=await View(task,ct);var a=lease.Evidence.Assignment;var proof=lease.Evidence;
        var instance=await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==view.InstanceId,ct);
        var allowed=new List<string>();var reader=new TaskDelegationReaderV2(db,scope.EnvironmentId);
        foreach(var decision in new[]{"APPROVE","REJECT"})
            if(proof.OnBehalfOfId==null||await reader.Match(proof.ActualHandlerId,proof.PrincipalId,"TASK_"+decision,instance.FlowKey,original.Subject.BizType,ct)!=null)allowed.Add(decision);
        var result=new TaskActionContextV2(task,view.InstanceId,"APP",a.FlowVersionId,instance.FormDefVersionId,view.TaskRowVersion,view.InstanceRowVersion,null,
            a.TokenId,a.NodeId,a.StageIndex,a.StageRound,a.PrincipalId,proof.PrincipalId,a.CreatedPrincipalId??a.PrincipalId,proof.ActualHandlerId,proof.OnBehalfOfId,
            view.TaskStatus,view.InstanceStatus,allowed.ToArray(),new(),proof.GrantId,proof.GrantRevision,
            new(original.Subject.BusinessOwner,original.Subject.SubjectId,original.Subject.BusinessRevision),null,RequiredReviewComment(original),500,"APP_ACCEPTED_GUID_2");
        await authority.ValidateAsync(lease,ct);await tx.CommitAsync(ct);return result;
    }
}
