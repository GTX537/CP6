using CP6.Core.Services.Plm;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using static CP6.Core.Services.Wms.LocationRuleReviewStoreV1;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Wms;

public sealed partial class LocationRuleReviewOwnerV1
{
    private async Task<(LocationRuleReviewStoreV1 Store,SubmissionRow Row,ApprovalSubmitCommandV2 Original)> Frozen(
        ApprovalSubmitCommandV2 incoming,CancellationToken ct)
    {
        await scope.Root(ct);
        if(incoming.Scope!=new ApprovalScopeV2(Environment.ToString(),db.CurrentTenantId) || incoming.Subject.BusinessOwner!=LocationRuleReviewStoreV1.Owner
            || incoming.Subject.BizType!=BizType || incoming.Subject.SubjectKind!=SubjectKind || incoming.Engineering!=null
            || !Guid.TryParseExact(incoming.RoundId,"D",out var round) || round==Guid.Empty
            || incoming.SubmitAuthorizationRef.Owner!=AuthorizationOwner || incoming.SubmitAuthorizationRef.Version!="1"
            || !Guid.TryParseExact(incoming.SubmitAuthorizationRef.Id,"D",out var frozen))throw Error("LOCATION_RULE_REVIEW_EVIDENCE_INVALID",422);
        await new LocationRuleReviewOriginalsV1(db).Fence(false,ct);
        var store=new LocationRuleReviewStoreV1(db,Environment);await store.Fence(false,ct);
        var row=await store.Submission(frozen,ct)??throw Error("MISSING_EVIDENCE",422);
        var original=store.Envelope(row);
        var package=LocationRuleReviewStoreV1.ReadPackage(row);
        var originals=new LocationRuleReviewOriginalsV1(db);await originals.Fence(false,ct);
        var value=await originals.Read(row.LocationRule,row.Revision,ct);LocationRuleReviewSourceViewV1? previous=null;
        if(value.ParentVersion!=null)previous=await originals.Read(row.LocationRule,Revision(value.ParentVersion),ct);
        var changeDigest=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{previous=previous?.OriginalJson,current=value.OriginalJson}));
        if(package.LocationRule!=value.Reference || package.Previous!=previous?.Reference || package.ChangeDigest!=changeDigest
            || Json(package.Body)!=Json(JsonSerializer.Deserialize<JsonElement>(value.OriginalJson)))
            throw Error("LOCATION_RULE_REVIEW_PACKAGE_UNAVAILABLE",503);
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
            var final=await Authority.LocationRuleReviewAuthorityAsync(initial.Resource,initial.Action,actor,ct);
            if(final.Source.SourcePermissionVersion!=initial.Source.SourcePermissionVersion || final.OrganizationVersion!=initial.OrganizationVersion)
                throw Error("AUTHORITY_CHANGED",403);
        }
    }
    internal async Task<ApprovalBusinessIntakeProofV2> VerifyIntakeEvidence(ApprovalSubmissionWireV2.Parsed incoming,CancellationToken ct)
    {
        var (store,row,original)=await Frozen(incoming.Value,ct);
        var mode=await db.Database.SqlQuery<string>($"""
            SELECT evidence_mode AS "Value" FROM cp6_approval_binding_v2.registrations
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={Environment} AND owner={LocationRuleReviewStoreV1.Owner}
              AND biz_type={BizType} AND subject_kind={SubjectKind}
            """).SingleOrDefaultAsync(ct);
        if(mode!=locationRules.EvidenceMode)throw Error("LOCATION_RULE_EVIDENCE_MODE_CHANGED",422);
        Task<OperationsOwnerV1.Authority> Current(string r,string action,CancellationToken token)=>Authority.LocationRuleReviewAuthorityAsync(r,action,row.Actor,token);
        var authority=await Current(OperationsOwnerV1.LocationRule,"review-submit",ct);
        var access=new OperationsAccessV1(db,owner,authority,storedSubjectSource:Current);
        var c=await Prepare(access,row.LocationRule,false,ct);
        var view=store.View(row);await locationRules.ReviewSource(access,view.LocationRule,true,ct);
        RequireActive(c.LocationRule,row.Id);await RequirePolicy(c,view.Policy,ct);await ValidateParticipant(access,row.Actor,ct);
        return new(row.Id,Json(new{provider=SubmissionOwner,row.Id,row.LocationRule,row.Actor,row.Digest,
            originalAuthorization=row.Authorization,currentAuthorities=Witnesses(access),policy=view.Policy,original.Subject}));
    }
    private sealed record ReviewDecisionReadV1(string Profile,Guid LocationRule,EngineeringSourceRefV1 Reference,
        EngineeringSourceRefV1 Policy,Guid Actual,Guid Principal,int[] Roles,LocationRuleReviewAuthorityV1[] ActualAuthority,
        LocationRuleReviewAuthorityV1[] PrincipalAuthority);
    internal async Task<ApprovalBusinessDecisionProofV2> ReadDecisionPolicy(ApprovalSubmitCommandV2 incoming,Guid? principal,
        bool deciding,CancellationToken ct,bool approving=true)
    {
        var (store,row,original)=await Frozen(incoming,ct);var view=store.View(row);
        var action=deciding?"review-decide":"query";
        var authority=await Authority.AuthorityAsync(OperationsOwnerV1.LocationRule,action,ct);
        var access=new OperationsAccessV1(db,owner,authority);
        var locationRule=await locationRules.ReviewSource(access,view.LocationRule,deciding&&approving,ct);
        var policies=await store.Policies(row.LocationRule,ct);
        var policy=policies.SingleOrDefault(p=>p.Reference==view.Policy)??throw Error("LOCATION_RULE_POLICY_UNAVAILABLE",503);
        var roles=authority.Actor.RoleIds.Order().ToArray();LocationRuleReviewAuthorityV1[] principalAuthority=[];
        var principalId=principal??access.ActorId;
        if(deciding)
        {
            RequireActive(locationRule,row.Id);
            await RequireRoles(policy.Fact.Body.Requirements,ct);
            if(principalId!=access.ActorId)
            {
                Task<OperationsOwnerV1.Authority> Current(string r,string a,CancellationToken token)=>Authority.LocationRuleReviewAuthorityAsync(r,a,principalId,token);
                var assigned=await Current(OperationsOwnerV1.LocationRule,"review-decide",ct);
                var assignedAccess=new OperationsAccessV1(db,owner,assigned,storedSubjectSource:Current);
                await locationRules.ReviewSource(assignedAccess,view.LocationRule,approving,ct);
                await ValidateParticipant(assignedAccess,principalId,ct);
                roles=roles.Intersect(assigned.Actor.RoleIds).Order().ToArray();principalAuthority=Witnesses(assignedAccess);
            }
            if(!policy.Fact.Body.Requirements.Any(r=>roles.Contains(r.RoleId)))throw Error("LOCATION_RULE_PROFESSIONAL_ROLE_REQUIRED",403);
            if(policy.Fact.Body.SeparateAuthorAndReviewer && original.SeparationSubjectIds.Any(id=>id==access.ActorId || id==principalId))
                throw Error("LOCATION_RULE_REVIEW_SEPARATION_REQUIRED",403);
            // A retired policy cannot authorize a new signature. Historical votes are retained unchanged.
            await RequirePolicy(new(access,store,locationRule,policies,[]),view.Policy,ct);
        }
        await ValidateParticipant(access,access.ActorId,ct);
        var read=Json(new ReviewDecisionReadV1("Main.Wms.LocationRuleReview.decision-read/1",row.LocationRule,view.LocationRule,view.Policy,
            access.ActorId,principalId,roles,Witnesses(access),principalAuthority));
        return new(policy.Fact,1,"locationRule",read);
    }
    internal async Task<string[]> ValidateDecisionRequirements(ApprovalSubmitCommandV2 original,ApprovalBusinessDecisionProofV2 proof,
        ApprovalVoteV2[] votes,bool approved,CancellationToken ct)
    {
        if(proof.FrozenPolicy is not LocationRuleReviewPolicyV1 policy)throw Error("LOCATION_RULE_POLICY_UNAVAILABLE",503);
        var evidence=votes.Select(v=>{
            var r=JsonSerializer.Deserialize<ReviewDecisionReadV1>(v.BusinessReadEvidence,ApprovalBindingOwnerV2.Json)
                ??throw Error("LOCATION_RULE_REVIEW_EVIDENCE_INVALID",422);
            if(r.Profile!="Main.Wms.LocationRuleReview.decision-read/1" || r.LocationRule!=policy.LocationRuleId
                || r.Reference.Owner!=original.Subject.BusinessOwner || r.Reference.Id.ToString()!=original.Subject.SubjectId
                || r.Reference.Version!=original.Subject.BusinessRevision || r.Reference.Digest!=original.Subject.ContentDigest.Value
                || r.Policy!=Reference(policy) || r.Actual!=v.Authority.ActualHandlerId || r.Principal!=v.Authority.PrincipalId
                || string.IsNullOrWhiteSpace(v.Comment))throw Error("LOCATION_RULE_REVIEW_EVIDENCE_INVALID",422);
            return (Vote:v,Read:r);
        }).ToArray();
        if(!approved)return Array.Empty<string>();
        var authority=await Authority.AuthorityAsync(OperationsOwnerV1.LocationRule,"review-decide",ct);
        var access=new OperationsAccessV1(db,owner,authority);
        await locationRules.ReviewSource(access,new(original.Subject.BusinessOwner,Guid.Parse(original.Subject.SubjectId),original.Subject.BusinessRevision,original.Subject.ContentDigest.Value),true,ct);
        await ValidateParticipant(access,access.ActorId,ct);
        var requirements=new List<string>();
        foreach(var required in policy.Body.Requirements)
        {
            var actual=evidence.Where(x=>x.Vote.Approved && x.Read.Roles.Contains(required.RoleId)).Select(x=>x.Read.Principal).Distinct().Count();
            var handlers=evidence.Where(x=>x.Vote.Approved && x.Read.Roles.Contains(required.RoleId)).Select(x=>x.Read.Actual).Distinct().Count();
            if(actual<required.MinimumApprovals || handlers<required.MinimumApprovals)throw Error("LOCATION_RULE_PROFESSIONAL_REVIEW_INCOMPLETE",422);
            requirements.Add("location-rule.requirement/"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(required)));
        }
        return requirements.ToArray();
    }
}
