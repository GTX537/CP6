using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;
public sealed record ApprovalTaskCommandResolutionV2(Guid CommandId,string State,ApprovalTaskDecisionResultV2? Result=null);
public sealed record DocumentReviewTaskV1(Guid SubmissionId,Guid DocumentId,string DocumentVersion,ApprovalTaskDecisionViewV2 Task);
public sealed record RequirementSetReviewTaskV1(Guid SubmissionId,Guid RequirementSetId,string RequirementSetVersion,ApprovalTaskDecisionViewV2 Task);
public sealed record LocationRuleReviewTaskV1(Guid SubmissionId,Guid LocationRuleId,string LocationRuleVersion,ApprovalTaskDecisionViewV2 Task);
public sealed record IterationReviewTaskV1(Guid SubmissionId,Guid IterationId,ApprovalTaskDecisionViewV2 Task);
public sealed partial class ApprovalTaskDecisionOwnerV2
{
    private sealed record ClosedCommand(Guid Actor,Guid Task);
    private Task<ClosedCommand?> Closed(Guid id,CancellationToken ct)=>db.Database.SqlQuery<ClosedCommand>($"""
        SELECT actor_id AS "Actor",task_id AS "Task" FROM cp6_approval_decision_v2.closed_commands
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND id={id}
        """).SingleOrDefaultAsync(ct);
    private async Task RequireNotClosed(Guid id,Guid actor,CancellationToken ct)
    {
        var row=await Closed(id,ct);
        if(row!=null)throw Error(row.Actor==actor?"COMMAND_CLOSED":"RESOURCE_NOT_FOUND",row.Actor==actor?409:404);
    }
    public async Task<ApprovalTaskCommandResolutionV2> CloseAsync(Guid task,Guid command,CancellationToken ct=default)
    {
        if(task==Guid.Empty || command==Guid.Empty)throw Error("DECISION_INPUT_INVALID",422);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);
        var fence=await scope.AcquireAsync(ct);await Ready(ct);var actor=await authority.CurrentActor(false,ct);
        var original=await Original(command,ct);
        if(original!=null)
        {
            if(original.Task!=task)throw Error("COMMAND_PAYLOAD_CONFLICT",409);
            var result=await Disclose(original,actor,ct);await scope.ValidateAsync(fence,ct);await tx.CommitAsync(ct);
            return new(command,"COMPLETED",result);
        }
        var source=ApprovalSubmissionWireV2.Parse((await ReadSource(task,ct)).Payload).Value;
        await Business.Decision(source,null,false,ct);
        var closed=await Closed(command,ct);
        if(closed!=null && (closed.Actor!=actor || closed.Task!=task))throw Error("RESOURCE_NOT_FOUND",404);
        if(closed==null)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_approval_decision_v2.closed_commands(tenant_id,environment_id,id,actor_id,task_id,recorded_at)
                VALUES({db.CurrentTenantId},{scope.EnvironmentId},{command},{actor},{task},date_trunc('milliseconds',clock_timestamp()))
                """,ct);
        await Ready(ct);await scope.ValidateAsync(fence,ct);
        if(await authority.CurrentActor(false,ct)!=actor)throw Error("AUTHORITY_CHANGED",403);
        await tx.CommitAsync(ct);return new(command,"CLOSED");
    }
    public async Task<DocumentReviewTaskV1[]> DocumentTasksAsync(Guid document,Guid? behalf=null,CancellationToken ct=default)
    {
        if(document==Guid.Empty)throw Error("DECISION_INPUT_INVALID",422);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);
        var fence=await scope.AcquireAsync(ct);await Ready(ct);var actor=await authority.CurrentActor(false,ct);var principal=behalf??actor;
        var ids=await db.Database.SqlQuery<Guid>($"""
            SELECT t."Id" AS "Value" FROM public."Wf_FlowTask" t
            JOIN cp6_approval_start_v2.starts s ON s.tenant_id=t."TenantId" AND s.instance_id=t."InstanceId"
            JOIN cp6_document_review_v1.submissions d ON d.tenant_id=s.tenant_id AND d.environment_id=s.environment_id AND d.id=s.submission_id
            WHERE s.tenant_id={db.CurrentTenantId} AND s.environment_id={scope.EnvironmentId} AND s.owner={ApprovalBusinessProfileV2.DocumentOwner}
              AND d.document_id={document} AND t."Status"=0 AND t."AssigneeId"={principal} ORDER BY t."Id"
            """).ToArrayAsync(ct);
        var result=new List<DocumentReviewTaskV1>();
        foreach(var id in ids)
        {
            var lease=await authority.AcquireAsync(id,behalf,ct);
            var original=ApprovalSubmissionWireV2.Parse((await ReadSource(id,ct)).Payload).Value;
            await Business.Decision(original,lease.Evidence.PrincipalId,false,ct);
            result.Add(new(original.SubmissionId,document,original.Subject.BusinessRevision,await View(id,ct)));
            await authority.ValidateAsync(lease,ct);
        }
        await scope.ValidateAsync(fence,ct);await tx.CommitAsync(ct);return result.ToArray();
    }
    public async Task<RequirementSetReviewTaskV1[]> RequirementSetTasksAsync(Guid requirementSet,Guid? behalf=null,CancellationToken ct=default)
    {
        if(requirementSet==Guid.Empty)throw Error("DECISION_INPUT_INVALID",422);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);
        var fence=await scope.AcquireAsync(ct);await Ready(ct);var actor=await authority.CurrentActor(false,ct);var principal=behalf??actor;
        var ids=await db.Database.SqlQuery<Guid>($"""
            SELECT t."Id" AS "Value" FROM public."Wf_FlowTask" t
            JOIN cp6_approval_start_v2.starts s ON s.tenant_id=t."TenantId" AND s.instance_id=t."InstanceId"
            JOIN cp6_requirement_set_review_v1.submissions d ON d.tenant_id=s.tenant_id AND d.environment_id=s.environment_id AND d.id=s.submission_id
            WHERE s.tenant_id={db.CurrentTenantId} AND s.environment_id={scope.EnvironmentId} AND s.owner={ApprovalBusinessProfileV2.RequirementSetOwner}
              AND d.requirement_set_id={requirementSet} AND t."Status"=0 AND t."AssigneeId"={principal} ORDER BY t."Id"
            """).ToArrayAsync(ct);
        var result=new List<RequirementSetReviewTaskV1>();
        foreach(var id in ids)
        {
            var lease=await authority.AcquireAsync(id,behalf,ct);
            var original=ApprovalSubmissionWireV2.Parse((await ReadSource(id,ct)).Payload).Value;
            await Business.Decision(original,lease.Evidence.PrincipalId,false,ct);
            result.Add(new(original.SubmissionId,requirementSet,original.Subject.BusinessRevision,await View(id,ct)));
            await authority.ValidateAsync(lease,ct);
        }
        await scope.ValidateAsync(fence,ct);await tx.CommitAsync(ct);return result.ToArray();
    }
    public async Task<LocationRuleReviewTaskV1[]> LocationRuleTasksAsync(Guid locationRule,Guid? behalf=null,CancellationToken ct=default)
    {
        if(locationRule==Guid.Empty)throw Error("DECISION_INPUT_INVALID",422);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);
        var fence=await scope.AcquireAsync(ct);await Ready(ct);var actor=await authority.CurrentActor(false,ct);var principal=behalf??actor;
        var ids=await db.Database.SqlQuery<Guid>($"""
            SELECT t."Id" AS "Value" FROM public."Wf_FlowTask" t
            JOIN cp6_approval_start_v2.starts s ON s.tenant_id=t."TenantId" AND s.instance_id=t."InstanceId"
            JOIN cp6_location_rule_review_v1.submissions d ON d.tenant_id=s.tenant_id AND d.environment_id=s.environment_id AND d.id=s.submission_id
            WHERE s.tenant_id={db.CurrentTenantId} AND s.environment_id={scope.EnvironmentId} AND s.owner={ApprovalBusinessProfileV2.LocationRuleOwner}
              AND d.location_rule_id={locationRule} AND t."Status"=0 AND t."AssigneeId"={principal} ORDER BY t."Id"
            """).ToArrayAsync(ct);
        var result=new List<LocationRuleReviewTaskV1>();
        foreach(var id in ids)
        {
            var lease=await authority.AcquireAsync(id,behalf,ct);
            var original=ApprovalSubmissionWireV2.Parse((await ReadSource(id,ct)).Payload).Value;
            await Business.Decision(original,lease.Evidence.PrincipalId,false,ct);
            result.Add(new(original.SubmissionId,locationRule,original.Subject.BusinessRevision,await View(id,ct)));
            await authority.ValidateAsync(lease,ct);
        }
        await scope.ValidateAsync(fence,ct);await tx.CommitAsync(ct);return result.ToArray();
    }
    public async Task<IterationReviewTaskV1[]> IterationTasksAsync(Guid submission,Guid? behalf=null,CancellationToken ct=default)
    {
        if(submission==Guid.Empty)throw Error("DECISION_INPUT_INVALID",422);
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);
        var fence=await scope.AcquireAsync(ct);await Ready(ct);var actor=await authority.CurrentActor(false,ct);var principal=behalf??actor;
        var ids=await db.Database.SqlQuery<Guid>($"""
            SELECT t."Id" AS "Value" FROM public."Wf_FlowTask" t
            JOIN cp6_approval_start_v2.starts s ON s.tenant_id=t."TenantId" AND s.instance_id=t."InstanceId"
            JOIN cp6_iteration_review_v1.submissions d ON d.tenant_id=s.tenant_id AND d.environment_id=s.environment_id AND d.id=s.submission_id
            WHERE s.tenant_id={db.CurrentTenantId} AND s.environment_id={scope.EnvironmentId} AND s.owner={ApprovalBusinessProfileV2.IterationOwner}
              AND d.id={submission} AND t."Status"=0 AND t."AssigneeId"={principal} ORDER BY t."Id"
            """).ToArrayAsync(ct);
        var result=new List<IterationReviewTaskV1>();
        foreach(var id in ids)
        {
            var lease=await authority.AcquireAsync(id,behalf,ct);
            var original=ApprovalSubmissionWireV2.Parse((await ReadSource(id,ct)).Payload).Value;
            await Business.Decision(original,lease.Evidence.PrincipalId,false,ct);
            var iteration=await db.Database.SqlQuery<Guid>($"""
                SELECT iteration_id AS "Value" FROM cp6_iteration_review_v1.submissions WHERE environment_id={scope.EnvironmentId} AND tenant_id={db.CurrentTenantId} AND id={submission}
                """).SingleAsync(ct);
            result.Add(new(original.SubmissionId,iteration,await View(id,ct)));
            await authority.ValidateAsync(lease,ct);
        }
        await scope.ValidateAsync(fence,ct);await tx.CommitAsync(ct);return result.ToArray();
    }
}
