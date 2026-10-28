using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;

namespace CP6.Core.Services.Oa;

internal sealed record ApprovalBusinessIntakeProofV2(Guid FrozenSubmissionId,string Evidence);
internal sealed record ApprovalBusinessDecisionProofV2(object FrozenPolicy,int RequiredHumanApprovals,
    string RequirementPrefix,string ReadAuthority);

/// <summary>Explicit local participants selected by the frozen business Owner. No fallback
/// to another domain's policy and no caller-supplied evidence provider.</summary>
public sealed class ApprovalBusinessEvidenceV2(ToolDispositionApprovalOwnerV1 tools,ControlledDocumentReviewOwnerV1? documents=null,RequirementSetReviewOwnerV1? sets=null,IterationReviewOwnerV1? iterations=null)
{
    internal Task<string> Withdrawal(ApprovalSubmitCommandV2 original,ApprovalWithdrawCommandV2 command,bool current,CancellationToken ct)
        =>original.Subject.BusinessOwner==IterationReviewStoreV1.Owner && iterations!=null
            ?iterations.WithdrawalEvidence(original,command,current,ct)
            :throw new ScopeAuthorizationExceptionV1("WITHDRAW_OWNER_NOT_ADOPTED",422);
    internal async Task<ApprovalBusinessIntakeProofV2> Intake(ApprovalSubmissionWireV2.Parsed incoming,CancellationToken ct)
    {
        if(incoming.Value.Subject.BusinessOwner=="Main.Wms.ToolDisposition.v1")
        {var p=await tools.VerifyIntakeEvidence(incoming,ct);return new(p.FrozenSubmissionId,p.Evidence);}
        if(incoming.Value.Subject.BusinessOwner==ControlledDocumentReviewStoreV1.Owner && documents!=null)
            return await documents.VerifyIntakeEvidence(incoming,ct);
        if(incoming.Value.Subject.BusinessOwner==RequirementSetReviewStoreV1.Owner && sets!=null)
            return await sets.VerifyIntakeEvidence(incoming,ct);
        if(incoming.Value.Subject.BusinessOwner==IterationReviewStoreV1.Owner && iterations!=null)
            return await iterations.VerifyIntakeEvidence(incoming,ct);
        throw new ScopeAuthorizationExceptionV1("BUSINESS_EVIDENCE_OWNER_UNAVAILABLE",422);
    }
    internal async Task<ApprovalBusinessDecisionProofV2> Decision(ApprovalSubmitCommandV2 original,
        Guid? principal,bool deciding,CancellationToken ct,bool approving=true)
    {
        if(original.Subject.BusinessOwner=="Main.Wms.ToolDisposition.v1")
        {var p=await tools.ReadDecisionPolicy(original,ct);return new(p.FrozenPolicy,p.FrozenPolicy.Body.RequiredHumanApprovals,"tool",p.ReadAuthority);}
        if(original.Subject.BusinessOwner==ControlledDocumentReviewStoreV1.Owner && documents!=null)
            return await documents.ReadDecisionPolicy(original,principal,deciding,ct);
        if(original.Subject.BusinessOwner==RequirementSetReviewStoreV1.Owner && sets!=null)
            return await sets.ReadDecisionPolicy(original,principal,deciding,ct,approving);
        if(original.Subject.BusinessOwner==IterationReviewStoreV1.Owner && iterations!=null)
            return await iterations.ReadDecisionPolicy(original,principal,deciding,ct,approving);
        throw new ScopeAuthorizationExceptionV1("BUSINESS_EVIDENCE_OWNER_UNAVAILABLE",422);
    }
    internal Task<string[]> Requirements(ApprovalSubmitCommandV2 original,ApprovalBusinessDecisionProofV2 policy,
        ApprovalVoteV2[] votes,bool approved,CancellationToken ct)
    {
        if(original.Subject.BusinessOwner=="Main.Wms.ToolDisposition.v1")return Task.FromResult(Array.Empty<string>());
        if(original.Subject.BusinessOwner==ControlledDocumentReviewStoreV1.Owner && documents!=null)
            return documents.ValidateDecisionRequirements(original,policy,votes,approved,ct);
        if(original.Subject.BusinessOwner==RequirementSetReviewStoreV1.Owner && sets!=null)
            return sets.ValidateDecisionRequirements(original,policy,votes,approved,ct);
        if(original.Subject.BusinessOwner==IterationReviewStoreV1.Owner && iterations!=null)
            return iterations.ValidateDecisionRequirements(original,policy,votes,approved,ct);
        throw new ScopeAuthorizationExceptionV1("BUSINESS_EVIDENCE_OWNER_UNAVAILABLE",422);
    }
}
