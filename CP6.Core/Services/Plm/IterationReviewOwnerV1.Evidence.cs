using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using static CP6.Core.Services.Plm.IterationReviewStoreV1;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;

public sealed partial class IterationReviewOwnerV1
{
    private async Task ValidateParticipant(OperationsAccessV1 access,Guid actor,CancellationToken ct)
    {
        await access.ValidateFinalAsync(ct);
        foreach(var initial in access.Authorities)
        {
            var final=await Authority.IterationReviewAuthorityAsync(initial.Resource,initial.Action,actor,ct);
            if(final.Source.SourcePermissionVersion!=initial.Source.SourcePermissionVersion || final.OrganizationVersion!=initial.OrganizationVersion)
                throw Error("AUTHORITY_CHANGED",403);
        }
    }
    internal async Task<ApprovalBusinessIntakeProofV2> VerifyIntakeEvidence(ApprovalSubmissionWireV2.Parsed incoming,CancellationToken ct)
    {
        await scope.Root(ct);var e=incoming.Value;
        if(e.Scope!=new ApprovalScopeV2(Environment.ToString(),db.CurrentTenantId) || e.Subject.BusinessOwner!=IterationReviewStoreV1.Owner
            || e.Subject.BizType!=BizType || e.Subject.SubjectKind!=SubjectKind || e.Engineering==null
            || e.SubmitAuthorizationRef.Owner!=AuthorizationOwner || e.SubmitAuthorizationRef.Version!="1"
            || !Guid.TryParseExact(e.SubmitAuthorizationRef.Id,"D",out var id))throw Error("ITERATION_REVIEW_EVIDENCE_INVALID",422);
        Task<OperationsOwnerV1.Authority> Current(string r,string action,CancellationToken t)=>Authority.IterationReviewAuthorityAsync(r,action,e.StarterId,t);
        var authority=await Current(OperationsOwnerV1.EngineeringIteration,"review-submit",ct);
        var access=new OperationsAccessV1(db,owner,authority,storedSubjectSource:Current);
        var store=new IterationReviewStoreV1(db,Environment);await store.Fence(false,ct);
        var row=await store.Submission(id,ct)??throw Error("MISSING_EVIDENCE",422);
        var original=await store.Envelope(row,ct);var parsed=ApprovalSubmissionWireV2.Parse(row.Payload);
        if(e.SubmissionId==row.Id?incoming.Core!=parsed.Core:incoming.IntentCore!=parsed.IntentCore)
            throw Error(e.SubmissionId==row.Id?"SUBMISSION_PAYLOAD_CONFLICT":"INTENT_PAYLOAD_CONFLICT",409);
        var c=await Prepare(access,row.Iteration,false,ct);var work=c.Iteration.Body.Iteration!;
        var package=ReadPackage(row);var link=work.CurrentReview;
        if(work.Stage!="IN_REVIEW" || link?.RoundId!=row.Round || link.BindingId!=row.Binding || link.SubmissionId!=row.Id
            || link.Package!=new EngineeringSourceRefV1(PackageOwner,row.Id,"1",row.PackageDigest))throw Error("ITERATION_REVIEW_ROUND_CHANGED",409);
        await DisclosePackage(access,row,true,ct);
        var policy=await Policy(c,package.Policy,ct);await Reviewers(c,package.Reviewers,policy,ct);
        if(Json(await Registration(ct))!=row.Registration)throw Error("ITERATION_REVIEW_REGISTRATION_CHANGED",412);
        await ValidateParticipant(access,row.Actor,ct);
        return new(row.Id,Json(new{provider=SubmissionOwner,row.Id,row.Iteration,row.Actor,row.Digest,row.Round,row.Binding,
            originalAuthorization=row.Authorization,currentAuthorities=Witnesses(access),policy=package.Policy,original.Subject}));
    }
}
