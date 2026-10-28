using CP6.Core.Services.Integration;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
public sealed record RequirementSetSourceProofV1(Guid EnvironmentId,Guid TenantId,string EvidenceMode,
    EngineeringSourceRefV1 RequirementSet,Guid? SubmissionId,Guid? DecisionId,Guid? ApprovalId,
    string Availability,string[] Missing,string Purpose="REQUIREMENT_DEFINITION_SET",string Observation="READ_ONLY_OBSERVATION");
public sealed partial class RequirementSetService
{
    internal async Task<RequirementSetSourceProofV1> Source(OperationsAccessV1 root,EngineeringSourceRefV1 reference,bool hold,CancellationToken ct)
    {
        var access=await root.SourceAsync(OperationsOwnerV1.RequirementSet,ct);
        var value=await ReviewSource(access,reference,hold,ct);
        var store=new RequirementSetReviewApplicationStoreV1(db,scope);await store.Ready(ct);
        var approval=await store.Approved(reference,ct);var missing=value.Missing.ToList();
        if(approval!=null)
        {
            var source=await store.ForSubmission(approval.SubmissionId,ct);
            if(source==null || source.Decision.DecisionId!=approval.DecisionId || source.EvidenceMode!=EvidenceMode)
                throw Error("REQUIREMENT_SET_APPROVAL_SOURCE_UNAVAILABLE",503);
        }
        if(hold && (approval==null || missing.Count>0))throw Error("REQUIREMENT_SET_APPROVAL_REQUIRED",422);
        return new(scope.EnvironmentId,db.CurrentTenantId,EvidenceMode,reference,approval?.SubmissionId,approval?.DecisionId,approval?.Id,
            missing.Count==0?"AVAILABLE":"UNAVAILABLE",missing.ToArray(),Observation:hold?"HELD_IN_CURRENT_UOW":"READ_ONLY_OBSERVATION");
    }
    internal Task<RequirementSetSourceProofV1> HoldApproved(OperationsAccessV1 root,EngineeringSourceRefV1 reference,CancellationToken ct)
        =>Source(root,reference,true,ct);
    public Task<RequirementSetSourceProofV1> SourceAsync(EngineeringSourceRefV1 reference,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.RequirementSet,"query",false,(a,t)=>Source(a,reference,false,t),ct);
}
