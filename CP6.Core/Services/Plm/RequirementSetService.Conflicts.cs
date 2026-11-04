using CP6.Core.Services.Integration;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
public sealed partial class RequirementSetService
{
    private static RequirementConflictObservationV1[] ObserveConflicts(RequirementSetBodyV1 body,RequirementSetMemberObservationV1[] members)
        =>body.Conflicts.Select(c=>{
            var left=members.Single(m=>m.Requirement.Reference==c.Left);var right=members.Single(m=>m.Requirement.Reference==c.Right);
            var pairs=RequirementApplicabilityV1.Intersections(left,right);var intersection=pairs.State;
            var known=RequirementApplicabilityV1.KnownConstraints(left)&&RequirementApplicabilityV1.KnownConstraints(right);
            var state=c.Decision?.Kind switch {
                "SCOPE_SEPARATION" when intersection=="DISJOINT"=>"RESOLVED",
                "CLARIFY" when intersection=="OVERLAP" && known=>"RESOLVED",
                "ECM"=>"ECM_PENDING",
                "DEV" when RequirementApplicabilityV1.Clauses(left).Concat(RequirementApplicabilityV1.Clauses(right)).Any(x=>x.Terms?.Waivability=="PROHIBITED")=>"DEV_PROHIBITED",
                "DEV"=>"DEV_PENDING",_=>"UNRESOLVED"};
            return new RequirementConflictObservationV1(c.Id,intersection,state,left.Source.Reference,right.Source.Reference,pairs.PairCount,pairs.Examples);
        }).ToArray();
    private static async Task QualifyConflictDecisions(OperationsAccessV1 access,RequirementSetSnapshotV1? previous,RequirementSetSnapshotV1 saved,CancellationToken ct)
    {
        var changed=saved.Body.Conflicts.Where(c=>c.Decision!=null && previous?.Body.Conflicts.Any(p=>p==c)!=true).ToArray();
        if(changed.Length==0)return;
        var decider=await access.ActionAsync(OperationsOwnerV1.RequirementSet,"conflict-decide",ct);
        decider.RequireRequirementSetFields(true);await decider.RequireRequirementSet(saved.Reference.Id,ct);
        decider.AtCommit(t=>decider.RequireRequirementSet(saved.Reference.Id,t));
        var sources=await access.ActionAsync(OperationsOwnerV1.EngineeringRequirement,"source-confirm",ct);sources.RequireRequirementFields(true);
        foreach(var c in changed)
        {
            var observation=saved.ConflictStatus!.Single(x=>x.Id==c.Id);
            foreach(var source in new[]{observation.LeftSource,observation.RightSource}.Distinct())
            {await sources.RequireEngineeringRequirement(source.Id,ct);sources.AtCommit(t=>sources.RequireEngineeringRequirement(source.Id,t));}
            var left=saved.MemberStatus!.Single(x=>x.Requirement.Reference==c.Left);var right=saved.MemberStatus!.Single(x=>x.Requirement.Reference==c.Right);
            if(!left.Source.IsCurrent || !right.Source.IsCurrent)throw Error("REQUIREMENT_CONFLICT_SOURCE_CHANGED",412);
            if(c.Decision!.Kind is "CLARIFY" or "SCOPE_SEPARATION" && observation.State!="RESOLVED")throw Error("REQUIREMENT_CONFLICT_NOT_RESOLVED",422);
            if(c.Decision.Kind=="DEV" && (observation.Intersection=="UNKNOWN" || observation.State=="DEV_PROHIBITED"
                || !RequirementApplicabilityV1.KnownConstraints(left) || !RequirementApplicabilityV1.KnownConstraints(right)))
                throw Error("REQUIREMENT_CONFLICT_DEVIATION_NOT_ALLOWED",422);
        }
    }
}
