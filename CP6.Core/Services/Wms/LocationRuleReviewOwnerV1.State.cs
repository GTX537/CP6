using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Wms;
public sealed partial class LocationRuleReviewOwnerV1
{
    private static void RequireActive(LocationRuleReviewSourceViewV1 value,Guid submission)
    {
        if(!value.IsCurrent||value.State!="SUBMITTED"||value.ActiveSubmissionId!=submission)
            throw Error("RULE_REVIEW_ROUND_NOT_CURRENT",409);
    }
    private async Task Transition(LocationRuleReviewSourceViewV1 expected,Guid operation,Guid actor,string next,Guid submission,object command,CancellationToken ct)
    {
        var store=new LocationRuleStoreV1(db);await store.Fence(true,ct);
        var h=await store.HeadFor(expected.Body.LocationId,expected.Reference.Id,ct);
        if(h.Etag!=expected.Etag)throw Error("RULE_PREDECESSOR_CHANGED",412);
        if(h.Revision==long.MaxValue)throw Error("RULE_VERSION_EXHAUSTED",503);
        if(next=="SUBMITTED"?h.State is not("DRAFT" or "RETURNED"):
            next is not("APPROVED_PENDING" or "REJECTED" or "RETURNED")||h.State!="SUBMITTED"||h.ActiveSubmissionId!=submission)
            throw Error("RULE_REVIEW_ROUND_NOT_CURRENT",409);
        if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_location_rule_v1.proposals SET revision=revision+1,token={Guid.NewGuid()},state={next},
              active_submission_id={submission},operation_id={operation},actor_id={actor}
            WHERE tenant_id={db.CurrentTenantId} AND id={h.Id} AND token={h.Token} AND revision={h.Revision}
            """,ct)!=1)throw Error("RULE_PREDECESSOR_CHANGED",412);
        h=await store.HeadFor(h.Location,h.Id,ct);var original=await store.Read(h,null,ct);
        var result=new LocationRuleResultV1(operation,h.Location,original.Reference,h.Selector,h.Etag,h.State,next);
        // The existing proposal journal and immutable review evidence commit together.
        await new MainOwnerOperationJournalV1(db).Store(LocationRuleValuesV1.Owner,LocationRuleValuesV1.Contract,operation,actor,
            MainOwnerOperationJournalV1.Encode(command),result,[h.Location,h.Id],new{profile="LOCATION-RULE-REVIEW-TRANSITION/1",submission,result},ct);
    }
}
