using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;

internal sealed class IterationReviewControlStoreV1(CP6Context db,Guid environment)
{
    internal sealed record Head(Guid Iteration,Guid Submission,string State,byte[] Version)
    {internal string RowVersion=>Convert.ToBase64String(Version);}
    internal async Task Ready(CancellationToken ct)
    {
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_iteration_review_control_v1.effects_ready() AS \"Value\"").SingleAsync(ct))
            throw Error("ITERATION_REVIEW_CONTROL_UNAVAILABLE",503);
    }
    internal async Task<Head?> Read(Guid iteration,CancellationToken ct)
    {
        await Ready(ct);
        return await db.Database.SqlQuery<Head>($"""
            SELECT iteration_id AS "Iteration",submission_id AS "Submission",state AS "State",row_version AS "Version"
            FROM cp6_iteration_review_control_v1.heads WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND iteration_id={iteration}
            FOR UPDATE
            """).SingleOrDefaultAsync(ct);
    }
    internal async Task Begin(IterationReviewStoreV1.SubmissionRow row,CancellationToken ct)
    {
        var old=await Read(row.Iteration,ct);
        if(old?.State=="IN_REVIEW")throw Error("ITERATION_REVIEW_ALREADY_SUBMITTED",409);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_iteration_review_control_v1.heads(environment_id,tenant_id,iteration_id,submission_id,state)
            VALUES({environment},{db.CurrentTenantId},{row.Iteration},{row.Id},'IN_REVIEW')
            ON CONFLICT(environment_id,tenant_id,iteration_id) DO UPDATE SET submission_id=EXCLUDED.submission_id,state='IN_REVIEW'
            """,ct);
    }
    internal async Task<Head> Transition(Head expected,string state,CancellationToken ct)
    {
        if(state is not("APPLIED" or "WITHDRAWN"))throw Error("ITERATION_REVIEW_CONTROL_INVALID",503);
        if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_iteration_review_control_v1.heads SET state={state}
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND iteration_id={expected.Iteration}
              AND submission_id={expected.Submission} AND row_version={expected.Version} AND state='IN_REVIEW'
            """,ct)!=1)throw Error("ITERATION_REVIEW_CONTROL_CHANGED",409);
        return (await Read(expected.Iteration,ct))!;
    }
    private sealed record IssueRow(byte[] Body,string Digest);
    internal async Task<IterationReviewIssueV1[]> Issues(Guid iteration,CancellationToken ct)
    {
        await Ready(ct);
        var rows=await db.Database.SqlQuery<IssueRow>($"""
            SELECT DISTINCT ON(id) body AS "Body",digest AS "Digest" FROM cp6_iteration_review_control_v1.issues
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND iteration_id={iteration} ORDER BY id,revision DESC
            """).ToArrayAsync(ct);
        return rows.Select(r=>{
            var v=JsonSerializer.Deserialize<IterationReviewIssueV1>(r.Body,ApprovalBindingOwnerV2.Json);
            if(v==null || v.IterationId!=iteration || ApprovalCanonicalV2.Hash(r.Body)!=r.Digest || !ApprovalCanonicalV2.Bytes(v).AsSpan().SequenceEqual(r.Body))
                throw Error("ITERATION_REVIEW_ISSUE_UNAVAILABLE",503);
            return v;
        }).OrderBy(x=>x.Id).ToArray();
    }
    internal async Task Append(IterationReviewIssueV1 issue,Guid operation,Guid actor,object audit,CancellationToken ct)
    {
        var body=ApprovalCanonicalV2.Bytes(issue);var log=IterationReviewStoreV1.Json(audit);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_iteration_review_control_v1.issues(environment_id,tenant_id,iteration_id,id,revision,submission_id,operation_id,actor_id,body,digest,audit_json)
            VALUES({environment},{db.CurrentTenantId},{issue.IterationId},{issue.Id},{issue.Revision},{issue.SubmissionId},{operation},{actor},
              {body},{ApprovalCanonicalV2.Hash(body)},{log})
            """,ct);
    }
}
