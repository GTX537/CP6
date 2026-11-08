using CP6.Core.Services.Integration;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
public sealed partial class RequirementSetService
{
    private static bool SameExclusion(RequirementSetMemberV1 a,RequirementSetMemberV1 b)
        =>a.Requirement==b.Requirement && a.Disposition==b.Disposition && a.Exclusion==b.Exclusion;
    private static RequirementExclusionObservationV1[] ObserveExclusions(RequirementSetSnapshotV1 value,RequirementSetMemberObservationV1[] members)
        =>value.Body.Members.Where(m=>m.Disposition=="EXCLUDED").Select(m=>{
            var observed=members.Single(o=>o.Requirement.Reference==m.Requirement);var clauses=RequirementApplicabilityV1.Clauses(observed);
            var state=m.Exclusion==null?"DECISION_REQUIRED":m.Exclusion.Source!=observed.Source.Reference || !observed.Source.IsCurrent?"SOURCE_CHANGED":
                clauses.Any(c=>c.Terms?.Necessity=="MANDATORY")?"MANDATORY":clauses.Any(c=>c.Terms?.Waivability=="PROHIBITED")?"PROHIBITED":
                clauses.Any(c=>c.Terms is not{Necessity:"OPTIONAL",Waivability:"SOURCE_DECISION_REQUIRED"})?"UNKNOWN":
                value.Body.Conflicts.Any(c=>c.Left.Id==m.Requirement.Id || c.Right.Id==m.Requirement.Id)
                    || observed.Requirement.Body.Requirement!.Relations.Any(r=>r.Kind=="CONFLICT")?"CONFLICT_RETAINED":"ACCEPTED";
            return new RequirementExclusionObservationV1(m.Requirement.Id,state,observed.Source.Reference,
                m.Exclusion==null?null:value.Reference,m.Exclusion==null?null:value.ActorId,m.Exclusion==null?null:value.RecordedAt);
        }).ToArray();
    private static async Task QualifyExclusionDecisions(OperationsAccessV1 access,RequirementSetSnapshotV1? previous,RequirementSetSnapshotV1 saved,CancellationToken ct)
    {
        var changed=saved.Body.Members.Where(m=>m.Exclusion!=null && (previous is null || previous.Body.Scope!=saved.Body.Scope || !previous.Body.Members.Any(p=>SameExclusion(p,m)))).ToArray();
        if(changed.Length==0)return;
        if(string.IsNullOrWhiteSpace(saved.Body.Scope))throw Error("REQUIREMENT_EXCLUSION_SCOPE_REQUIRED",422);
        var decider=await access.ActionAsync(OperationsOwnerV1.RequirementSet,"exclusion-decide",ct);
        decider.RequireRequirementSetFields(true);await decider.RequireRequirementSet(saved.Reference.Id,ct);
        decider.AtCommit(t=>decider.RequireRequirementSet(saved.Reference.Id,t));
        var sources=await access.ActionAsync(OperationsOwnerV1.EngineeringRequirement,"source-confirm",ct);sources.RequireRequirementFields(true);
        foreach(var member in changed)
        {
            var observation=saved.ExclusionStatus!.Single(x=>x.RequirementId==member.Requirement.Id);
            await sources.RequireEngineeringRequirement(observation.Source.Id,ct);sources.AtCommit(t=>sources.RequireEngineeringRequirement(observation.Source.Id,t));
            if(observation.State=="SOURCE_CHANGED")throw Error("REQUIREMENT_EXCLUSION_SOURCE_CHANGED",412);
            if(observation.State!="ACCEPTED")throw Error("REQUIREMENT_EXCLUSION_NOT_ALLOWED",422);
        }
    }
}
