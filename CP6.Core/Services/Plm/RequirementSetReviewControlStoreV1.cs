using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

internal sealed class RequirementSetReviewControlStoreV1(CP6Context db,Guid environment)
{
    internal sealed record Head(Guid RequirementSet,Guid Submission,string State,byte[] Version)
    {internal string RowVersion=>Convert.ToBase64String(Version);}
    internal async Task Ready(CancellationToken ct)
    {
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_requirement_set_review_control_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("REQUIREMENT_SET_REVIEW_CONTROL_UNAVAILABLE",503);
    }
    internal async Task<Head> Read(Guid submission,CancellationToken ct)
    {
        return await db.Database.SqlQuery<Head>($"""
            SELECT requirement_set_id AS "RequirementSet",submission_id AS "Submission",state AS "State",row_version AS "Version"
            FROM cp6_requirement_set_review_control_v1.heads WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND submission_id={submission}
            FOR UPDATE
            """).SingleOrDefaultAsync(ct)??throw Error("REQUIREMENT_SET_REVIEW_CONTROL_UNAVAILABLE",503);
    }
    internal async Task Begin(RequirementSetReviewStoreV1.SubmissionRow row,CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_requirement_set_review_control_v1.heads(environment_id,tenant_id,requirement_set_id,submission_id,state,origin)
            VALUES({environment},{db.CurrentTenantId},{row.RequirementSet},{row.Id},'IN_REVIEW','SUBMISSION')
            """,ct);
    }
    internal async Task<Head> Transition(Head expected,string state,CancellationToken ct,bool reauthorize=false)
    {
        var before=reauthorize?"WITHDRAWN":"IN_REVIEW";
        if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_requirement_set_review_control_v1.heads SET state={state}
            WHERE environment_id={environment} AND tenant_id={db.CurrentTenantId} AND submission_id={expected.Submission}
              AND row_version={expected.Version} AND state={before}
            """,ct)!=1)throw Error("REQUIREMENT_SET_REVIEW_CONTROL_CHANGED",409);
        return await Read(expected.Submission,ct);
    }
}
