using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using static CP6.Core.Services.Plm.ControlledDocumentReviewStoreV1;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class ControlledDocumentReviewOwnerV1
{
    private async Task<(ControlledDocumentReviewStoreV1 Store,SubmissionRow Row,ApprovalSubmitCommandV2 Original)> Frozen(
        ApprovalSubmitCommandV2 incoming,CancellationToken ct)
    {
        await scope.Root(ct);
        if(incoming.Scope!=new ApprovalScopeV2(Environment.ToString(),db.CurrentTenantId) || incoming.Subject.BusinessOwner!=ControlledDocumentReviewStoreV1.Owner
            || incoming.Subject.BizType!=BizType || incoming.Subject.SubjectKind!=SubjectKind || incoming.Engineering!=null
            || !Guid.TryParseExact(incoming.RoundId,"D",out var round) || round==Guid.Empty
            || incoming.SubmitAuthorizationRef.Owner!=AuthorizationOwner || incoming.SubmitAuthorizationRef.Version!="1"
            || !Guid.TryParseExact(incoming.SubmitAuthorizationRef.Id,"D",out var frozen))throw Error("DOCUMENT_REVIEW_EVIDENCE_INVALID",422);
        await new ControlledDocumentStoreV1(db).Fence(false,ct);
        var store=new ControlledDocumentReviewStoreV1(db,Environment);await store.Fence(false,ct);
        var row=await store.Submission(frozen,ct)??throw Error("MISSING_EVIDENCE",422);
        var original=store.Envelope(row);
        var package=ControlledDocumentReviewStoreV1.ReadPackage(row);
        var originals=new ControlledDocumentStoreV1(db);await originals.Fence(false,ct);
        var value=await originals.Read(row.Document,row.Revision,ct);ControlledDocumentSnapshotV1? previous=null;
        if(value.ParentVersion!=null)previous=await originals.Read(row.Document,Revision(value.ParentVersion),ct);
        var changeDigest=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{previous=previous?.OriginalJson,current=value.OriginalJson}));
        if(package.Document!=value.Reference || package.Previous!=previous?.Reference || package.ChangeDigest!=changeDigest
            || Json(package.Source)!=Json(value.Body.Source) || Json(package.Contents)!=Json(value.Body.Contents))
            throw Error("DOCUMENT_REVIEW_PACKAGE_UNAVAILABLE",503);
        var originalParsed=ApprovalSubmissionWireV2.Parse(row.Payload);
        var parsed=ApprovalSubmissionWireV2.Parse(ApprovalCanonicalV2.Bytes(incoming));
        if((incoming.SubmissionId==row.Id?originalParsed.Core!=parsed.Core:originalParsed.IntentCore!=parsed.IntentCore))
            throw Error(incoming.SubmissionId==row.Id?"SUBMISSION_PAYLOAD_CONFLICT":"INTENT_PAYLOAD_CONFLICT",409);
        return(store,row,original);
    }
    private async Task ValidateParticipant(OperationsAccessV1 access,Guid actor,CancellationToken ct)
    {
        await access.ValidateFinalAsync(ct);
        foreach(var initial in access.Authorities)
        {
            var final=await Authority.DocumentReviewAuthorityAsync(initial.Resource,initial.Action,actor,ct);
            if(final.Source.SourcePermissionVersion!=initial.Source.SourcePermissionVersion || final.OrganizationVersion!=initial.OrganizationVersion)
                throw Error("AUTHORITY_CHANGED",403);
        }
    }
    internal async Task<ApprovalBusinessIntakeProofV2> VerifyIntakeEvidence(ApprovalSubmissionWireV2.Parsed incoming,CancellationToken ct)
    {
        var (store,row,original)=await Frozen(incoming.Value,ct);
        Task<OperationsOwnerV1.Authority> Current(string r,string action,CancellationToken token)=>Authority.DocumentReviewAuthorityAsync(r,action,row.Actor,token);
        var authority=await Current(OperationsOwnerV1.ControlledDocument,"review-submit",ct);
        var access=new OperationsAccessV1(db,owner,authority,storedSubjectSource:Current);
        var c=await Prepare(access,row.Document,false,ct);
        var view=store.View(row);await documents.ReviewSource(access,view.Document,true,ct);
        await RequirePolicy(c,view.Policy,ct);await ValidateParticipant(access,row.Actor,ct);
        return new(row.Id,Json(new{provider=SubmissionOwner,row.Id,row.Document,row.Actor,row.Digest,
            originalAuthorization=row.Authorization,currentAuthorities=Witnesses(access),policy=view.Policy,original.Subject}));
    }
    private sealed record ReviewDecisionReadV1(string Profile,Guid Document,EngineeringSourceRefV1 Reference,
        EngineeringSourceRefV1 Policy,Guid Actual,Guid Principal,int[] Roles,DocumentReviewAuthorityV1[] ActualAuthority,
        DocumentReviewAuthorityV1[] PrincipalAuthority);
    internal async Task<ApprovalBusinessDecisionProofV2> ReadDecisionPolicy(ApprovalSubmitCommandV2 incoming,Guid? principal,
        bool deciding,CancellationToken ct)
    {
        var (store,row,original)=await Frozen(incoming,ct);var view=store.View(row);
        var action=deciding?"review-decide":"query";
        var authority=await Authority.AuthorityAsync(OperationsOwnerV1.ControlledDocument,action,ct);
        var access=new OperationsAccessV1(db,owner,authority);
        var document=await documents.ReviewSource(access,view.Document,deciding,ct);
        var policies=await store.Policies(row.Document,ct);
        var policy=policies.SingleOrDefault(p=>p.Reference==view.Policy)??throw Error("DOCUMENT_POLICY_UNAVAILABLE",503);
        var roles=authority.Actor.RoleIds.Order().ToArray();DocumentReviewAuthorityV1[] principalAuthority=[];
        var principalId=principal??access.ActorId;
        if(deciding)
        {
            await RequireRoles(policy.Fact.Body.Requirements,ct);
            if(principalId!=access.ActorId)
            {
                Task<OperationsOwnerV1.Authority> Current(string r,string a,CancellationToken token)=>Authority.DocumentReviewAuthorityAsync(r,a,principalId,token);
                var assigned=await Current(OperationsOwnerV1.ControlledDocument,"review-decide",ct);
                var assignedAccess=new OperationsAccessV1(db,owner,assigned,storedSubjectSource:Current);
                await documents.ReviewSource(assignedAccess,view.Document,true,ct);
                await ValidateParticipant(assignedAccess,principalId,ct);
                roles=roles.Intersect(assigned.Actor.RoleIds).Order().ToArray();principalAuthority=Witnesses(assignedAccess);
            }
            if(!policy.Fact.Body.Requirements.Any(r=>roles.Contains(r.RoleId)))throw Error("DOCUMENT_PROFESSIONAL_ROLE_REQUIRED",403);
            if(policy.Fact.Body.SeparateAuthorAndReviewer && original.SeparationSubjectIds.Any(id=>id==access.ActorId || id==principalId))
                throw Error("DOCUMENT_REVIEW_SEPARATION_REQUIRED",403);
            // A retired policy cannot authorize a new signature. Historical votes are retained unchanged.
            await RequirePolicy(new(access,store,document,policies,[]),view.Policy,ct);
        }
        await ValidateParticipant(access,access.ActorId,ct);
        var read=Json(new ReviewDecisionReadV1("Main.Plm.DocumentReview.decision-read/1",row.Document,view.Document,view.Policy,
            access.ActorId,principalId,roles,Witnesses(access),principalAuthority));
        return new(policy.Fact,1,"document",read);
    }
    internal Task<string[]> ValidateDecisionRequirements(ApprovalSubmitCommandV2 original,ApprovalBusinessDecisionProofV2 proof,
        ApprovalVoteV2[] votes,bool approved,CancellationToken ct)
    {
        if(proof.FrozenPolicy is not DocumentReviewPolicyV1 policy)throw Error("DOCUMENT_POLICY_UNAVAILABLE",503);
        var evidence=votes.Select(v=>{
            var r=JsonSerializer.Deserialize<ReviewDecisionReadV1>(v.BusinessReadEvidence,ApprovalBindingOwnerV2.Json)
                ??throw Error("DOCUMENT_REVIEW_EVIDENCE_INVALID",422);
            if(r.Profile!="Main.Plm.DocumentReview.decision-read/1" || r.Document!=policy.DocumentId
                || r.Reference.Owner!=original.Subject.BusinessOwner || r.Reference.Id.ToString()!=original.Subject.SubjectId
                || r.Reference.Version!=original.Subject.BusinessRevision || r.Reference.Digest!=original.Subject.ContentDigest.Value
                || r.Policy!=Reference(policy) || r.Actual!=v.Authority.ActualHandlerId || r.Principal!=v.Authority.PrincipalId
                || string.IsNullOrWhiteSpace(v.Comment))throw Error("DOCUMENT_REVIEW_EVIDENCE_INVALID",422);
            return (Vote:v,Read:r);
        }).ToArray();
        if(!approved)return Task.FromResult(Array.Empty<string>());
        var requirements=new List<string>();
        foreach(var required in policy.Body.Requirements)
        {
            var actual=evidence.Where(x=>x.Vote.Approved && x.Read.Roles.Contains(required.RoleId)).Select(x=>x.Read.Principal).Distinct().Count();
            var handlers=evidence.Where(x=>x.Vote.Approved && x.Read.Roles.Contains(required.RoleId)).Select(x=>x.Read.Actual).Distinct().Count();
            if(actual<required.MinimumApprovals || handlers<required.MinimumApprovals)throw Error("DOCUMENT_PROFESSIONAL_REVIEW_INCOMPLETE",422);
            requirements.Add("document.requirement/"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(required)));
        }
        return Task.FromResult(requirements.ToArray());
    }
}
