using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApprovalTaskDecisionInputV2(Guid CommandId, string Decision, string Comment,
    string TaskRowVersion, string InstanceRowVersion);
public sealed record ApprovalTaskDecisionViewV2(Guid TaskId, Guid InstanceId, int TaskStatus, int InstanceStatus,
    string TaskRowVersion, string InstanceRowVersion);
public sealed record ApprovalTaskDecisionOutcomeV2(Guid VoteId, Guid? DecisionId, ApprovalTaskDecisionViewV2 Task);
public sealed record ApprovalTaskDecisionResultV2(Guid CommandId, string State, ApprovalTaskDecisionOutcomeV2 Result, string QueryRef);
internal sealed record ApprovalVoteV2(Guid Id, Guid CommandId, Guid TaskId, Guid InstanceId, bool Approved, string Comment,
    string RoleKey, string RequirementId, DateTimeOffset OccurredAt, ApprovalTaskAuthorityEvidenceV2 Authority,
    string BusinessReadEvidence);

/// <summary>TxD: actual kernel vote, original command, immutable evidence and terminal outbox
/// share one PostgreSQL transaction. No business callback or transport runs before commit.</summary>
public sealed partial class ApprovalTaskDecisionOwnerV2(CP6Context db, ScopeAuthorizationFenceV1 scope,
    ApprovalTaskAuthorityV2 authority, ToolDispositionApprovalOwnerV1 tool,ApprovalBusinessEvidenceV2? businesses=null)
{
    private ApprovalBusinessEvidenceV2 Business=>businesses??new(tool);
    private static ScopeAuthorizationExceptionV1 Error(string code, int status = 503) => new(code, status);
    private sealed record Source(byte[] Payload, string Ack, string Publication);
    private sealed record Command(Guid Actor, Guid Task, string Digest, string Result);
    private sealed record StoredVote(string Body, string Digest);
    private static T Parse<T>(string text) => JsonSerializer.Deserialize<T>(text, ApprovalBindingOwnerV2.Json)!;
    private static string Json(object value) => Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(value));
    private void Entry()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges()
            || System.Transactions.Transaction.Current != null) throw Error("APP_DECISION_TRANSACTION_REQUIRED");
    }
    private async Task Ready(CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_approval_decision_v2.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("APP_DECISION_STORE_UNAVAILABLE");
    }
    public Task<bool> IsNativeAsync(Guid task, CancellationToken ct = default)
        => (from t in db.Wf_FlowTasks from i in db.Wf_FlowInstances
            where t.Id == task && t.InstanceId == i.Id && i.Creator == FlowEngine.ApprovalRuntimeV2 select t.Id).AnyAsync(ct);
    private async Task<Source> ReadSource(Guid task, CancellationToken ct)
        => await db.Database.SqlQuery<Source>($"""
            SELECT p.payload AS "Payload",p.ack_json AS "Ack",v.snapshot_json AS "Publication"
            FROM public."Wf_FlowTask" t JOIN cp6_approval_start_v2.starts s ON s.instance_id=t."InstanceId" AND s.tenant_id=t."TenantId"
            JOIN cp6_approval_intake_v2.submissions p ON p.tenant_id=s.tenant_id AND p.environment_id=s.environment_id AND p.owner=s.owner AND p.id=s.submission_id
            JOIN cp6_workflow_authoring_v2.versions v ON v.tenant_id=s.tenant_id AND v.environment_id=s.environment_id AND v.head_id=s.flow_head AND v.id=s.flow_version
            WHERE t."Id"={task} AND s.tenant_id={db.CurrentTenantId} AND s.environment_id={scope.EnvironmentId}
            """).SingleOrDefaultAsync(ct) ?? throw Error("RESOURCE_NOT_FOUND", 404);
    private async Task<ApprovalTaskDecisionViewV2> View(Guid task, CancellationToken ct)
    {
        var t = await db.Wf_FlowTasks.AsNoTracking().SingleAsync(x => x.Id == task, ct);
        var i = await db.Wf_FlowInstances.AsNoTracking().SingleAsync(x => x.Id == t.InstanceId, ct);
        var version = await db.Database.SqlQuery<byte[]>($"""
            SELECT "ApprovalRowVersion" AS "Value" FROM public."Wf_FlowTask" WHERE "Id"={task} AND "TenantId"={db.CurrentTenantId}
            """).SingleAsync(ct);
        if (version.Length != 8 || i.RowVersion?.Length != 8) throw Error("APP_VERSION_UNAVAILABLE");
        return new(t.Id, i.Id, t.Status, i.Status, Convert.ToBase64String(version), Convert.ToBase64String(i.RowVersion));
    }
    private Task<Command?> Original(Guid id, CancellationToken ct)
        => db.Database.SqlQuery<Command>($"""
            SELECT actor_id AS "Actor",task_id AS "Task",request_digest AS "Digest",result_json AS "Result"
            FROM cp6_approval_decision_v2.commands WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND id={id} AND completed
            """).SingleOrDefaultAsync(ct);
    private async Task<ApprovalTaskDecisionResultV2> Disclose(Command command, Guid actor, CancellationToken ct)
    {
        if (command.Actor != actor) throw Error("RESOURCE_NOT_FOUND", 404);
        var original = ApprovalSubmissionWireV2.Parse((await ReadSource(command.Task, ct)).Payload).Value;
        await Business.Decision(original,null,false,ct); // Current business/field read, not old approve/grant authority.
        return Parse<ApprovalTaskDecisionResultV2>(command.Result);
    }
    public async Task<ApprovalTaskDecisionResultV2?> OriginalAsync(Guid id, CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lease = await scope.AcquireAsync(ct); await Ready(ct); var actor = await authority.CurrentActor(false, ct);
        await RequireNotClosed(id,actor,ct);
        var command = await Original(id, ct); var result = command == null ? null : await Disclose(command, actor, ct);
        await scope.ValidateAsync(lease, ct); await tx.CommitAsync(ct); return result;
    }
    public async Task<ApprovalTaskDecisionViewV2> ContextAsync(Guid task, Guid? behalf = null, CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lease = await authority.AcquireAsync(task, behalf, ct); await Ready(ct);
        await Business.Decision(ApprovalSubmissionWireV2.Parse((await ReadSource(task, ct)).Payload).Value,lease.Evidence.PrincipalId,false,ct);
        var result = await View(task, ct); await authority.ValidateAsync(lease, ct); await tx.CommitAsync(ct); return result;
    }
    private static void Input(ApprovalTaskDecisionInputV2 input)
    {
        if (input.CommandId == Guid.Empty || input.Decision is not ("approve" or "reject") || input.Comment == null
            || input.Comment.EnumerateRunes().Count() > 500 || input.Decision == "reject" && string.IsNullOrWhiteSpace(input.Comment))
            throw Error("DECISION_INPUT_INVALID", 422);
        try
        {
            _ = new UTF8Encoding(false, true).GetByteCount(input.Comment);
            foreach (var text in new[] { input.TaskRowVersion, input.InstanceRowVersion })
                if (Convert.FromBase64String(text).Length != 8 || Convert.ToBase64String(Convert.FromBase64String(text)) != text)
                    throw new FormatException();
        }
        catch (Exception e) when (e is FormatException or ArgumentNullException or EncoderFallbackException)
        { throw Error("DECISION_INPUT_INVALID", 422); }
    }
    public async Task<ApprovalTaskDecisionResultV2> DecideAsync(Guid task, ApprovalTaskDecisionInputV2 input,
        Guid? behalf = null, string? ifMatch = null, CancellationToken ct = default)
    {
        Entry(); Input(input);
        var request = Json(new { profile = "oa.app.task-command/2.0", taskId = task, onBehalfOf = behalf, input });
        var digest = ApprovalCanonicalV2.Hash(Encoding.UTF8.GetBytes(request));
        await using var tx = await db.Database.BeginTransactionAsync(ct); var committing = false;
        try
        {
            var fence = await scope.AcquireAsync(ct); await Ready(ct);
            var actor = await authority.CurrentActor(false, ct); await RequireNotClosed(input.CommandId,actor,ct);
            var originalCommand = await Original(input.CommandId, ct);
            if (originalCommand != null)
            {
                var originalResult = await Disclose(originalCommand, actor, ct);
                if (originalCommand.Digest != digest) throw Error("COMMAND_PAYLOAD_CONFLICT", 409);
                await scope.ValidateAsync(fence, ct); await tx.CommitAsync(ct); return originalResult;
            }
            if (string.IsNullOrEmpty(ifMatch)) throw Error("PRECONDITION_REQUIRED", 428);
            if (ifMatch != "\"" + input.TaskRowVersion + "\"") throw Error("PRECONDITION_FAILED", 412);
            var source = await ReadSource(task, ct); var original = ApprovalSubmissionWireV2.Parse(source.Payload).Value;
            if(original.Subject.BusinessOwner=="Main.Plm.ControlledDocument.v1" && string.IsNullOrWhiteSpace(input.Comment))
                throw Error("DOCUMENT_REVIEW_COMMENT_REQUIRED",422);
            var publication = Parse<FlowPublicationV2>(source.Publication); var ack = Parse<ApprovalSubmissionAckV2>(source.Ack);
            var lease = await authority.AcquireAsync(task, behalf, ct);
            var view = await View(task, ct);
            if (view.TaskRowVersion != input.TaskRowVersion || view.InstanceRowVersion != input.InstanceRowVersion)
                throw Error("PRECONDITION_FAILED", 412);
            if (publication.VersionId != ack.FlowDefVersionId || !publication.Fields.TryGetValue("SchemaJson", out var schema)
                || schema == null || publication.SchemaDigest != ApprovalCanonicalV2.Hash(Encoding.UTF8.GetBytes(schema)))
                throw Error("FLOW_VERSION_UNAVAILABLE");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_approval_decision_v2.commands(tenant_id,environment_id,id,instance_id,task_id,actor_id,request_body,request_digest)
                VALUES({db.CurrentTenantId},{scope.EnvironmentId},{input.CommandId},{view.InstanceId},{task},{actor},{request},{digest})
                """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT 1 FROM public."Wf_FlowInstance" WHERE "Id"={view.InstanceId} AND "TenantId"={db.CurrentTenantId} FOR UPDATE;
                SELECT 1 FROM public."Wf_FlowTask" WHERE "InstanceId"={view.InstanceId} AND "TenantId"={db.CurrentTenantId} ORDER BY "Id" FOR UPDATE;
                """, ct);
            var policy = await Business.Decision(original,lease.Evidence.PrincipalId,true,ct);
            var occurred = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT date_trunc('milliseconds',clock_timestamp()) AS \"Value\"").SingleAsync(ct);
            var last = await db.Database.SqlQuery<DateTimeOffset?>($"""
                SELECT max(occurred_at) AS "Value" FROM cp6_approval_decision_v2.votes WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND instance_id={view.InstanceId}
                """).SingleAsync(ct);
            // Compare this database's vote clock only; an Owner's frozen timestamp
            // may come from a different host and is not a clock-ordering authority.
            if (last != null && occurred < last) throw Error("APP_CLOCK_REGRESSION", 409);
            var engine = new FlowEngine(db, new ApproverResolver(db, exactConditions: true));
            var instance = await engine.StageApprovalDecisionV2(publication, original, lease.Evidence, input.Decision == "approve", input.Comment, ct);
            await db.SaveChangesAsync(ct); await engine.SaveApprovalAssignments(ct);
            var assignment = lease.Evidence.Assignment;
            // A task duty is the actual immutable routing rule at a pinned version/node/stage,
            // not a claimed IAM role inferred from the last actor. The full rule is retained.
            var role = "wf.duty/" + ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new { assignment.FlowVersionId, assignment.NodeId,
                assignment.StageIndex, assignment.StageCode, assignment.Rule }));
            var requirement = "wf.req/" + ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new { assignment.InstanceId, assignment.TokenId,
                assignment.NodeId, assignment.StageIndex, assignment.StageRound }));
            var vote = new ApprovalVoteV2(Guid.NewGuid(), input.CommandId, task, instance.Id, input.Decision == "approve", input.Comment,
                role, requirement, occurred, lease.Evidence, policy.ReadAuthority);
            var voteBody = Json(vote);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_approval_decision_v2.votes(tenant_id,environment_id,id,command_id,instance_id,task_id,principal_id,actual_id,approved,body,digest,occurred_at)
                VALUES({db.CurrentTenantId},{scope.EnvironmentId},{vote.Id},{input.CommandId},{instance.Id},{task},{lease.Evidence.PrincipalId},
                  {actor},{vote.Approved},{voteBody},{ApprovalCanonicalV2.Hash(Encoding.UTF8.GetBytes(voteBody))},{occurred})
                """, ct);
            Guid? decision = null;
            if (instance.Status is FlowInstanceStatus.Approved or FlowInstanceStatus.Rejected)
                decision = await Terminal(original, ack, instance, vote, policy, ct);
            var result = new ApprovalTaskDecisionResultV2(input.CommandId, "COMPLETED", new(vote.Id, decision, await View(task, ct)),
                "/api/oa/tasks/decision-commands/" + input.CommandId);
            var audit = Json(new { profile = "oa.app.txd/2.0", input.CommandId, vote.Id, decision, before = view, after = result.Result.Task,
                authority = lease.Evidence, policy = original.Policy, businessRead = policy.ReadAuthority });
            await authority.ValidateAsync(lease, ct, afterVote: true);
            var finalPolicy = await Business.Decision(original,lease.Evidence.PrincipalId,true,ct);
            if (Json(finalPolicy) != Json(policy)) throw Error("AUTHORITY_CHANGED", 409);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_approval_decision_v2.commands SET completed=true,result_json={Json(result)},audit_json={audit}
                WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND id={input.CommandId} AND NOT completed
                """, ct);
            await Ready(ct); ct.ThrowIfCancellationRequested(); committing = true; await tx.CommitAsync(ct); db.ChangeTracker.Clear(); return result;
        }
        catch (Exception error)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new ScopeAuthorizationExceptionV1("OUTCOME_UNKNOWN", 504, new AggregateException(error, rollback)); }
            finally { db.ChangeTracker.Clear(); }
            if (committing) throw new ScopeAuthorizationExceptionV1("OUTCOME_UNKNOWN", 504, error);
            throw;
        }
    }

    private async Task<Guid> Terminal(ApprovalSubmitCommandV2 original, ApprovalSubmissionAckV2 ack,
        Wf_FlowInstance instance, ApprovalVoteV2 terminal, ApprovalBusinessDecisionProofV2 policy, CancellationToken ct)
    {
        var stored = await db.Database.SqlQuery<StoredVote>($"""
            SELECT body AS "Body",digest AS "Digest" FROM cp6_approval_decision_v2.votes
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND instance_id={instance.Id} ORDER BY occurred_at,id
            """).ToArrayAsync(ct);
        var votes = stored.Select(x => {
            var v = Parse<ApprovalVoteV2>(x.Body);
            if (Json(v) != x.Body || ApprovalCanonicalV2.Hash(Encoding.UTF8.GetBytes(x.Body)) != x.Digest)
                throw Error("DECISION_EVIDENCE_INCOMPLETE", 422);
            return v;
        }).ToArray();
        var tasks = await db.Wf_FlowTasks.AsNoTracking().Where(x => x.InstanceId == instance.Id).ToArrayAsync(ct);
        foreach (var task in tasks.Where(t => t.Status is FlowTaskStatus.Approved or FlowTaskStatus.Rejected))
            if (!votes.Any(v => v.TaskId == task.Id && v.Authority.PrincipalId == task.AssigneeId
                && v.Approved == (task.Status == FlowTaskStatus.Approved))) throw Error("DECISION_EVIDENCE_INCOMPLETE", 422);
        if (votes.Length == 0 || tasks.Any(t => t.Status is FlowTaskStatus.Pending or FlowTaskStatus.Suspended))
            throw Error("DECISION_EVIDENCE_INCOMPLETE", 422);
        var approved = instance.Status == FlowInstanceStatus.Approved;
        if (approved && votes.Where(v => v.Approved).Select(v => v.Authority.PrincipalId).Distinct().Count() < policy.RequiredHumanApprovals)
            throw Error("DECISION_EVIDENCE_INCOMPLETE", 422);
        if (!approved && !votes.Any(v => !v.Approved)) throw Error("DECISION_EVIDENCE_INCOMPLETE", 422);
        var requirements = votes.Select(v => v.RequirementId).Distinct().Order(StringComparer.Ordinal).ToList();
        requirements.Add(approved ? policy.RequirementPrefix+".human-approvals/"+policy.RequiredHumanApprovals : policy.RequirementPrefix+".explicit-rejection/1");
        requirements.AddRange(await Business.Requirements(original,policy,votes,approved,ct));
        var decision = Guid.NewGuid(); var eventId = Guid.NewGuid();
        var decided = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT date_trunc('milliseconds',clock_timestamp()) AS \"Value\"").SingleAsync(ct);
        if (decided < terminal.OccurredAt) throw Error("APP_CLOCK_REGRESSION", 409);
        var evidence = Json(new { profile = "oa.app.decision-evidence/2.0", decisionId = decision,
            decisionPolicyVersion = original.Policy, frozenPolicy = policy.FrozenPolicy, votes, satisfiedRequirementIds = requirements,
            dutyMapping = "wf.pinned-routing-duty/1", terminalState = instance.Status });
        var proof = terminal.Authority;
        var actor = new { kind = "HUMAN_TASK", taskId = terminal.TaskId, roleKey = terminal.RoleKey,
            decisionPrincipalId = proof.PrincipalId, actualHandlerId = proof.ActualHandlerId, onBehalfOfId = proof.OnBehalfOfId,
            grantId = proof.GrantId, grantRevision = proof.GrantRevision,
            permissionEvidenceRef = new ApprovalRefV2("Main Workflow", terminal.Id.ToString(), "permission/1"),
            sodEvidenceRef = new ApprovalRefV2("Main Workflow", terminal.Id.ToString(), "sod/1"), occurredAt = terminal.OccurredAt };
        var wire = new { schema = "oa.app/2.0", scope = original.Scope, eventId, decisionId = decision,
            streamId = original.SubmissionId, sequence = "1", producerOwner = "Main Workflow", consumerOwner = original.Subject.BusinessOwner,
            submissionId = original.SubmissionId, instanceId = instance.Id, subject = original.Subject, policy = original.Policy,
            bindingRevisionId = ack.BindingRevisionId, bindingTimelineRevisionId = ack.BindingTimelineRevisionId,
            flowDefVersionId = ack.FlowDefVersionId, roundId = original.RoundId, engineering = original.Engineering,
            decision = approved ? "APPROVED" : "REJECTED", decisionMode = "HUMAN", decidedAt = decided, terminalActor = actor,
            decisionEvidenceSetRef = new ApprovalRefV2("Main Workflow", decision.ToString(), "evidence/1"),
            decisionEvidenceDigest = ApprovalCanonicalV2.Digest(Encoding.UTF8.GetBytes(evidence)), satisfiedRequirementIds = requirements,
            reason = approved ? null : terminal.Comment, resultRef = new ApprovalRefV2("Main Workflow", decision.ToString(), "1") };
        var node = JsonSerializer.SerializeToNode(wire, ApprovalBindingOwnerV2.Json)!.AsObject();
        node["payloadDigest"] = JsonSerializer.SerializeToNode(ApprovalCanonicalV2.Digest(ApprovalCanonicalV2.Bytes(wire)), ApprovalBindingOwnerV2.Json);
        var payload = ApprovalCanonicalV2.Bytes(node); var payloadDigest = ApprovalCanonicalV2.Hash(payload);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_decision_v2.decisions(tenant_id,environment_id,id,command_id,instance_id,submission_id,event_id,decision,evidence_json,evidence_digest,payload,payload_digest)
            VALUES({db.CurrentTenantId},{scope.EnvironmentId},{decision},{terminal.CommandId},{instance.Id},{original.SubmissionId},{eventId},
              {(approved ? "APPROVED" : "REJECTED")},{evidence},{ApprovalCanonicalV2.Hash(Encoding.UTF8.GetBytes(evidence))},{payload},{payloadDigest});
            INSERT INTO cp6_approval_decision_v2.outbox(tenant_id,environment_id,id,decision_id,submission_id,consumer_owner,sequence,payload,payload_digest)
            VALUES({db.CurrentTenantId},{scope.EnvironmentId},{eventId},{decision},{original.SubmissionId},{original.Subject.BusinessOwner},1,{payload},{payloadDigest});
            """, ct);
        return decision;
    }
}
