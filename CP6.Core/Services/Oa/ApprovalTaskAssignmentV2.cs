using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

// An assignment is an engine fact, not a vote or a claim that the business approval policy is satisfied.
internal sealed record ApprovalTaskAssignmentV2(string Profile, Guid TenantId, Guid EnvironmentId,
    Guid SubmissionId, Guid InstanceId, Guid TaskId, Guid TokenId, Guid FlowVersionId, string NodeId,
    int StageIndex, int StageRound, string? StageCode, string? Countersign, Guid PrincipalId,
    Guid[] Candidates, ApproverRule Rule, DateTimeOffset AssignedAt,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]Guid? CreatedPrincipalId=null,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]long AssignmentRevision=0);

internal static class ApprovalTaskAssignmentsV2
{
    internal static async Task Ready(CP6Context db, CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_approval_tasks_v2.ready() AND cp6_native_tasks_v2.ready() AS \"Value\"").SingleAsync(ct))
            throw new ScopeAuthorizationExceptionV1("APP_TASK_EVIDENCE_UNAVAILABLE");
    }
    internal static async Task Store(CP6Context db, IEnumerable<ApprovalTaskAssignmentV2> facts, CancellationToken ct)
    {
        foreach (var f in facts)
        {
            var bytes = ApprovalCanonicalV2.Bytes(f); var body = Encoding.UTF8.GetString(bytes);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_approval_tasks_v2.assignments(tenant_id,environment_id,submission_id,instance_id,task_id,token_id,flow_version,
                  principal_id,body,digest)
                VALUES({f.TenantId},{f.EnvironmentId},{f.SubmissionId},{f.InstanceId},{f.TaskId},{f.TokenId},{f.FlowVersionId},
                  {f.PrincipalId},{body},{ApprovalCanonicalV2.Hash(bytes)})
                """, ct);
        }
    }
    private sealed record Stored(string Body);
    internal static async Task<ApprovalTaskAssignmentV2> Read(CP6Context db, Guid environment, Guid task, CancellationToken ct)
    {
        await Ready(db, ct);
        var row = await db.Database.SqlQuery<Stored>($"""
            SELECT COALESCE((SELECT body FROM cp6_native_tasks_v2.app_assignment_changes WHERE task_id=a.task_id ORDER BY revision DESC LIMIT 1),a.body) AS "Body"
            FROM cp6_approval_tasks_v2.assignments a WHERE a.tenant_id={db.CurrentTenantId} AND a.environment_id={environment} AND a.task_id={task}
            """).SingleOrDefaultAsync(ct) ?? throw new ScopeAuthorizationExceptionV1("APP_TASK_EVIDENCE_REQUIRED", 409);
        // Immutable assignment and change rows are checked by their native writing boundaries.
        return JsonSerializer.Deserialize<ApprovalTaskAssignmentV2>(row.Body, ApprovalBindingOwnerV2.Json)!;
    }
}
