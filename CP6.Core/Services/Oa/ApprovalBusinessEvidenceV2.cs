using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;

namespace CP6.Core.Services.Oa;

internal sealed record ApprovalBusinessIntakeProofV2(Guid FrozenSubmissionId,string Evidence);
internal sealed record ApprovalBusinessDecisionProofV2(object FrozenPolicy,int RequiredHumanApprovals,
    string RequirementPrefix,string ReadAuthority);

/// <summary>Explicit local participants selected by the frozen business Owner. No fallback
/// to another domain's policy and no caller-supplied evidence provider.</summary>
public sealed class ApprovalBusinessEvidenceV2(ToolDispositionApprovalOwnerV1 tools,ControlledDocumentReviewOwnerV1? documents=null)
{
    internal async Task<ApprovalBusinessIntakeProofV2> Intake(ApprovalSubmissionWireV2.Parsed incoming,CancellationToken ct)
    {
        if(incoming.Value.Subject.BusinessOwner=="Main.Wms.ToolDisposition.v1")
        {var p=await tools.VerifyIntakeEvidence(incoming,ct);return new(p.FrozenSubmissionId,p.Evidence);}
        if(incoming.Value.Subject.BusinessOwner==ControlledDocumentReviewStoreV1.Owner && documents!=null)
            return await documents.VerifyIntakeEvidence(incoming,ct);
        throw new ScopeAuthorizationExceptionV1("BUSINESS_EVIDENCE_OWNER_UNAVAILABLE",422);
    }
    internal async Task<ApprovalBusinessDecisionProofV2> Decision(ApprovalSubmitCommandV2 original,
        Guid? principal,bool deciding,CancellationToken ct)
    {
        if(original.Subject.BusinessOwner=="Main.Wms.ToolDisposition.v1")
        {var p=await tools.ReadDecisionPolicy(original,ct);return new(p.FrozenPolicy,p.FrozenPolicy.Body.RequiredHumanApprovals,"tool",p.ReadAuthority);}
        if(original.Subject.BusinessOwner==ControlledDocumentReviewStoreV1.Owner && documents!=null)
            return await documents.ReadDecisionPolicy(original,principal,deciding,ct);
        throw new ScopeAuthorizationExceptionV1("BUSINESS_EVIDENCE_OWNER_UNAVAILABLE",422);
    }
    internal Task<string[]> Requirements(ApprovalSubmitCommandV2 original,ApprovalBusinessDecisionProofV2 policy,
        ApprovalVoteV2[] votes,bool approved,CancellationToken ct)
    {
        if(original.Subject.BusinessOwner=="Main.Wms.ToolDisposition.v1")return Task.FromResult(Array.Empty<string>());
        if(original.Subject.BusinessOwner==ControlledDocumentReviewStoreV1.Owner && documents!=null)
            return documents.ValidateDecisionRequirements(original,policy,votes,approved,ct);
        throw new ScopeAuthorizationExceptionV1("BUSINESS_EVIDENCE_OWNER_UNAVAILABLE",422);
    }
}
