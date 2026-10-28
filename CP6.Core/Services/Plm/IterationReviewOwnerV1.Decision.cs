using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using static CP6.Core.Services.Plm.IterationReviewStoreV1;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;

public sealed partial class IterationReviewOwnerV1
{
    private async Task<(IterationReviewStoreV1 Store,SubmissionRow Row,ApprovalSubmitCommandV2 Original)> Frozen(
        ApprovalSubmitCommandV2 incoming,CancellationToken ct)
    {
        await scope.Root(ct);
        var store=new IterationReviewStoreV1(db,Environment);await store.Fence(false,ct);
        var row=await store.Submission(incoming.SubmissionId,ct)??throw Error("MISSING_EVIDENCE",422);
        var original=await store.Envelope(row,ct);
        if(ApprovalSubmissionWireV2.Parse(ApprovalCanonicalV2.Bytes(incoming)).Core!=ApprovalSubmissionWireV2.Parse(row.Payload).Core)
            throw Error("SUBMISSION_PAYLOAD_CONFLICT",409);
        return(store,row,original);
    }
    private sealed record ReviewDecisionReadV1(string Profile,Guid Iteration,Guid Round,Guid Binding,EngineeringSourceRefV1 Package,
        EngineeringSourceRefV1 Policy,Guid Actual,Guid Principal,int[] Roles,IterationReviewAuthorityV1[] ActualAuthority,
        IterationReviewAuthorityV1[] PrincipalAuthority);
    internal async Task<ApprovalBusinessDecisionProofV2> ReadDecisionPolicy(ApprovalSubmitCommandV2 incoming,Guid? principal,
        bool deciding,CancellationToken ct,bool approving=true)
    {
        var (store,row,original)=await Frozen(incoming,ct);var package=ReadPackage(row);
        var authority=await Authority.AuthorityAsync(OperationsOwnerV1.EngineeringIteration,deciding?"review-decide":"query",ct);
        var access=new OperationsAccessV1(db,owner,authority);var c=await Prepare(access,row.Iteration,false,ct);
        await DisclosePackage(access,row,deciding&&approving,ct);
        var policy=c.Policies.SingleOrDefault(p=>p.Reference==package.Policy)??throw Error("ITERATION_REVIEW_POLICY_UNAVAILABLE",503);
        var principalId=principal??access.ActorId;var roles=authority.Actor.RoleIds.Order().ToArray();IterationReviewAuthorityV1[] delegated=[];
        if(deciding)
        {
            var link=c.Iteration.Body.Iteration!.CurrentReview;
            if(c.Iteration.Body.Iteration.Stage!="IN_REVIEW" || link?.SubmissionId!=row.Id || link.BindingId!=row.Binding || link.RoundId!=row.Round)
                throw Error("ITERATION_REVIEW_ROUND_CHANGED",409);
            if(!package.Reviewers.Contains(principalId))throw Error("ITERATION_REVIEW_ASSIGNED_REVIEWER_REQUIRED",403);
            if(original.SeparationSubjectIds.Any(x=>x==access.ActorId || x==principalId))throw Error("ITERATION_REVIEW_SEPARATION_REQUIRED",403);
            await Policy(c,package.Policy,ct);
            if(principalId!=access.ActorId)
            {
                Task<OperationsOwnerV1.Authority> Current(string r,string a,CancellationToken t)=>Authority.IterationReviewAuthorityAsync(r,a,principalId,t);
                var assigned=await Current(OperationsOwnerV1.EngineeringIteration,"review-decide",ct);
                var assignedAccess=new OperationsAccessV1(db,owner,assigned,storedSubjectSource:Current);
                await DisclosePackage(assignedAccess,row,approving,ct);
                await iterations.ReviewSource(assignedAccess,row.Iteration,approving,ct);
                await ValidateParticipant(assignedAccess,principalId,ct);
                roles=roles.Intersect(assigned.Actor.RoleIds).Order().ToArray();delegated=Witnesses(assignedAccess);
            }
            if(!policy.Fact.Body.Requirements.Any(r=>roles.Contains(r.RoleId)))throw Error("ITERATION_REVIEW_PROFESSIONAL_ROLE_REQUIRED",403);
        }
        await ValidateParticipant(access,access.ActorId,ct);
        var read=Json(new ReviewDecisionReadV1("Main.Plm.IterationReview.decision-read/1",row.Iteration,row.Round,row.Binding,
            new(PackageOwner,row.Id,"1",row.PackageDigest),package.Policy,access.ActorId,principalId,roles,Witnesses(access),delegated));
        return new(policy.Fact,1,"iteration",read);
    }
    internal async Task<string[]> ValidateDecisionRequirements(ApprovalSubmitCommandV2 original,ApprovalBusinessDecisionProofV2 proof,
        ApprovalVoteV2[] votes,bool approved,CancellationToken ct)
    {
        var (_,row,_)=await Frozen(original,ct);var package=ReadPackage(row);
        if(proof.FrozenPolicy is not IterationReviewPolicyV1 policy || Reference(policy)!=package.Policy)throw Error("ITERATION_REVIEW_POLICY_UNAVAILABLE",503);
        var evidence=votes.Select(v=>{
            var r=JsonSerializer.Deserialize<ReviewDecisionReadV1>(v.BusinessReadEvidence,ApprovalBindingOwnerV2.Json)??throw Error("ITERATION_REVIEW_EVIDENCE_INVALID",422);
            if(r.Profile!="Main.Plm.IterationReview.decision-read/1" || r.Iteration!=row.Iteration || r.Round!=row.Round || r.Binding!=row.Binding
                || r.Package!=new EngineeringSourceRefV1(PackageOwner,row.Id,"1",row.PackageDigest) || r.Policy!=package.Policy
                || r.Actual!=v.Authority.ActualHandlerId || r.Principal!=v.Authority.PrincipalId || !package.Reviewers.Contains(r.Principal)
                || original.SeparationSubjectIds.Any(x=>x==r.Principal || x==r.Actual) || string.IsNullOrWhiteSpace(v.Comment))throw Error("ITERATION_REVIEW_EVIDENCE_INVALID",422);
            return (Vote:v,Read:r);
        }).ToArray();
        if(!approved)return [];
        var result=new List<string>();
        foreach(var required in policy.Body.Requirements)
        {
            var matching=evidence.Where(x=>x.Vote.Approved && x.Read.Roles.Contains(required.RoleId)).ToArray();
            if(matching.Select(x=>x.Read.Principal).Distinct().Count()<required.MinimumApprovals
                || matching.Select(x=>x.Read.Actual).Distinct().Count()<required.MinimumApprovals)throw Error("ITERATION_REVIEW_PROFESSIONAL_REVIEW_INCOMPLETE",422);
            result.Add("iteration.requirement/"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(required)));
        }
        return result.ToArray();
    }
}
