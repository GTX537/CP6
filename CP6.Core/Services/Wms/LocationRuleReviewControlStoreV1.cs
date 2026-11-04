using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Wms;

/// <summary>The existing proposal revision is the control shared by proposal withdrawal and review application.
/// No second mutable head is introduced. int8send preserves the complete PostgreSQL bigint revision.</summary>
internal sealed class LocationRuleReviewControlStoreV1(CP6Context db,Guid environment)
{
    internal sealed record Head(Guid LocationRule,Guid Submission,Guid Location,long Revision,Guid Token,string NativeState,string State,byte[] Version)
    {internal string RowVersion=>Convert.ToBase64String(Version);}
    internal async Task Ready(CancellationToken ct)
    {
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_location_rule_review_control_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("LOCATION_RULE_REVIEW_CONTROL_UNAVAILABLE",503);
    }
    internal async Task<Head> Read(Guid submission,CancellationToken ct)
        =>await db.Database.SqlQuery<Head>($"""
            SELECT p.id AS "LocationRule",s.id AS "Submission",p.location_id AS "Location",p.revision AS "Revision",p.token AS "Token",
              p.state AS "NativeState",int8send(p.revision) AS "Version",
              CASE WHEN EXISTS(SELECT FROM cp6_location_rule_review_application_v1.applications a WHERE
                (a.environment_id,a.tenant_id,a.submission_id)=(s.environment_id,s.tenant_id,s.id)) THEN 'APPLIED'
                WHEN p.active_submission_id=s.id AND p.state='SUBMITTED' THEN 'IN_REVIEW'
                WHEN p.active_submission_id=s.id AND p.state='WITHDRAWN' THEN 'WITHDRAWN' ELSE 'NOT_CURRENT' END AS "State"
            FROM cp6_location_rule_review_v1.submissions s JOIN cp6_location_rule_v1.proposals p
              ON p.tenant_id=s.tenant_id AND p.id=s.location_rule_id
            WHERE s.environment_id={environment} AND s.tenant_id={db.CurrentTenantId} AND s.id={submission} FOR UPDATE OF p
            """).SingleOrDefaultAsync(ct)??throw Error("LOCATION_RULE_REVIEW_CONTROL_UNAVAILABLE",503);
    internal async Task<Head> Withdraw(Head before,Guid operation,Guid actor,object command,CancellationToken ct)
    {
        if(before.Revision==long.MaxValue)throw Error("RULE_VERSION_EXHAUSTED",503);
        var store=new LocationRuleStoreV1(db);await store.Fence(true,ct);
        if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_location_rule_v1.proposals SET revision=revision+1,token={Guid.NewGuid()},state='WITHDRAWN',operation_id={operation},actor_id={actor}
            WHERE tenant_id={db.CurrentTenantId} AND id={before.LocationRule} AND revision={before.Revision} AND token={before.Token}
              AND active_submission_id={before.Submission} AND state IN('SUBMITTED','WITHDRAWN')
            """,ct)!=1)throw Error("LOCATION_RULE_REVIEW_CONTROL_CHANGED",412);
        var native=await store.HeadFor(before.Location,before.LocationRule,ct);var original=await store.Read(native,null,ct);
        var result=new LocationRuleResultV1(operation,native.Location,original.Reference,native.Selector,native.Etag,native.State,"WITHDRAWN");
        await new MainOwnerOperationJournalV1(db).Store(LocationRuleValuesV1.Owner,LocationRuleValuesV1.Contract,operation,actor,
            MainOwnerOperationJournalV1.Encode(command),result,[native.Location,native.Id],
            new{profile="LOCATION-RULE-TRUSTED-WITHDRAWAL/1",submission=before.Submission,result},ct);
        return await Read(before.Submission,ct);
    }
}
