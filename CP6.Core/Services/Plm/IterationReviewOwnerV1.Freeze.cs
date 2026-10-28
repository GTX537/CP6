using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;

public sealed partial class IterationReviewOwnerV1
{
    private sealed record FreezeOriginals(byte[] Application,string ApplicationHash,byte[] Receipt,string ReceiptHash,byte[] Decision,string DecisionHash);
    internal bool Owns(CP6Context context)=>ReferenceEquals(db,context) && owner.Owns(db) && iterations.Owns(db) && candidates.Owns(db);
    internal async Task<EngineeringFreezeReviewV1> FreezeSource(OperationsAccessV1 root,Guid iteration,Guid submission,
        EngineeringSourceRefV1 candidate,bool hold,CancellationToken ct,bool registerFinal=true)
    {
        if(!Owns(db))throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
        var access=await root.SourceAsync(OperationsOwnerV1.EngineeringIteration,ct);
        var c=await ApplicationContextAsync(access,iteration,submission,false,ct);
        var package=IterationReviewStoreV1.ReadPackage(c.Submission);
        var selected=package.Candidates.SingleOrDefault(x=>x.Reference==candidate)??throw Error("ENGINEERING_FREEZE_CANDIDATE_NOT_REVIEWED",422);
        var applied=await c.Store.Application(submission,ct);
        if(applied==null || c.Source==null || c.Receipt==null)throw Error("ENGINEERING_FREEZE_APPLIED_REVIEW_REQUIRED",422);
        var policy=c.Context.Policies.SingleOrDefault(x=>x.Reference==package.Policy)??throw Error("ITERATION_REVIEW_POLICY_UNAVAILABLE",503);
        var proof=await new ApprovalDecisionSourceV2(db,scope).Read(ApprovalDecisionWireV2.Parse(ApprovalCanonicalV2.Bytes(c.Source.Decision)),ct);
        if(hold)
        {
            var work=c.Context.Iteration.Body.Iteration!;var link=work.CurrentReview;
            if(!applied.Approved || applied.BusinessStage!="REVIEW_APPROVED" || c.Source.Decision.Decision!="APPROVED"
                || c.Receipt.Application!="APPLIED" || c.Receipt.OutcomeCode!="ITERATION_APPROVED"
                || c.Receipt.DecisionId!=applied.DecisionId || c.Source.Decision.DecisionId!=applied.DecisionId
                || c.Context.Iteration.Reference!=applied.AppliedIteration || work.Stage!="REVIEW_APPROVED" || work.HoldState!="NORMAL"
                || link?.SubmissionId!=submission || link.RoundId!=c.Submission.Round || link.BindingId!=c.Submission.Binding
                || c.Control?.Submission!=submission || c.Control.State!="APPLIED")throw Error("ENGINEERING_FREEZE_REVIEW_CHANGED",412);
            if(await QualifyApplication(c,ct)!=null)throw Error("ENGINEERING_FREEZE_OPEN_ISSUES",422);
            // Recheck the actual recorded professional principals and handlers, not the freezer's identity.
            using var decisionEvidence=JsonDocument.Parse(proof.Evidence);
            var votes=decisionEvidence.RootElement.GetProperty("votes").Deserialize<ApprovalVoteV2[]>(ApprovalBindingOwnerV2.Json)
                ??throw Error("ITERATION_REVIEW_EVIDENCE_INVALID",503);
            await ValidateDecisionRequirements(c.Source.Submission,new(policy.Fact,1,"iteration",""),votes,true,ct);
            foreach(var vote in votes)
            {
                var evidence=JsonSerializer.Deserialize<ReviewDecisionReadV1>(vote.BusinessReadEvidence,ApprovalBindingOwnerV2.Json)
                    ??throw Error("ITERATION_REVIEW_EVIDENCE_INVALID",503);
                foreach(var actor in new[]{evidence.Actual,evidence.Principal}.Distinct())
                {
                    Task<OperationsOwnerV1.Authority> Current(string r,string action,CancellationToken t)=>Authority.IterationReviewAuthorityAsync(r,action,actor,t);
                    var authority=await Current(OperationsOwnerV1.EngineeringIteration,"review-decide",ct);
                    var professional=new OperationsAccessV1(db,owner,authority,storedSubjectSource:Current);
                    await DisclosePackage(professional,c.Submission,true,ct);
                    await iterations.ReviewSource(professional,iteration,true,ct);
                    if(evidence.Roles.Any(role=>!authority.Actor.RoleIds.Contains(role)))throw Error("ENGINEERING_FREEZE_REVIEW_AUTHORITY_CHANGED",403);
                    var expected=actor==evidence.Actual?evidence.ActualAuthority:evidence.PrincipalAuthority;
                    foreach(var original in expected)
                    {
                        var actual=professional.Authorities.FirstOrDefault(x=>x.Resource==original.Resource && x.Action==original.Action && x.Actor.UserId==actor);
                        if(actual==null || actual.Source.SourcePermissionVersion!=original.PermissionVersion || actual.OrganizationVersion!=original.OrganizationVersion)
                            throw Error("ENGINEERING_FREEZE_REVIEW_AUTHORITY_CHANGED",403);
                    }
                    await ValidateParticipant(professional,actor,ct);
                }
            }
        }
        var envelope=await c.Context.Store.Envelope(c.Submission,ct);
        var snapshot=Encoding.UTF8.GetString(Convert.FromBase64String(envelope.Snapshot.CanonicalBytesBase64));
        // OA and native engineering have distinct timestamp codecs. Preserve the exact stored
        // original bytes instead of treating a new JSON serialization as the original receipt.
        var originals=await db.Database.SqlQuery<FreezeOriginals>($"""
            SELECT a.body AS "Application",a.digest AS "ApplicationHash",r.payload AS "Receipt",r.payload_hash AS "ReceiptHash",f.payload AS "Decision",f.payload_hash AS "DecisionHash"
            FROM cp6_iteration_review_application_v1.applications a
            JOIN cp6_iteration_review_application_v1.receipts r ON r.environment_id=a.environment_id AND r.tenant_id=a.tenant_id AND r.id=a.receipt_id
            JOIN cp6_iteration_review_fact_v1.facts f ON f.environment_id=a.environment_id AND f.tenant_id=a.tenant_id AND f.decision_id=a.decision_id
            WHERE a.environment_id={Environment} AND a.tenant_id={db.CurrentTenantId} AND a.submission_id={submission} AND a.id={applied.Id} AND r.id={c.Receipt.ReceiptId}
            """).SingleAsync(ct);
        if(ApprovalCanonicalV2.Hash(originals.Application)!=originals.ApplicationHash || ApprovalCanonicalV2.Hash(originals.Receipt)!=originals.ReceiptHash
            || ApprovalCanonicalV2.Hash(originals.Decision)!=originals.DecisionHash || originals.DecisionHash!=c.Source.OriginalHash)
            throw Error("ENGINEERING_FREEZE_ORIGINAL_UNAVAILABLE",503);
        var result=new EngineeringFreezeReviewV1(c.Context.Iteration.Reference,c.Submission.Round,submission,
            new(IterationReviewStoreV1.PackageOwner,submission,"1",c.Submission.PackageDigest),Encoding.UTF8.GetString(c.Submission.Package),
            snapshot,Encoding.UTF8.GetString(c.Submission.Payload),policy,applied,c.Receipt,c.Source.Decision,c.Source.OriginalHash,proof.Evidence,
            Encoding.UTF8.GetString(originals.Application),Encoding.UTF8.GetString(originals.Receipt),Encoding.UTF8.GetString(originals.Decision),
            c.Control?.RowVersion??"",selected,c.Issues);
        if(hold && registerFinal)
        {
            var expected=ApprovalCanonicalV2.Hash(IterationReviewCanonicalV1.Bytes(result));
            root.AtCommit(async token=>{
                var current=await root.EngineeringFinalObservation((a,t)=>FreezeSource(a,iteration,submission,candidate,true,t,false),token);
                if(ApprovalCanonicalV2.Hash(IterationReviewCanonicalV1.Bytes(current))!=expected)throw Error("ENGINEERING_FREEZE_REVIEW_CHANGED_AT_COMMIT",412);
            });
        }
        return result;
    }
    internal async Task<EngineeringFreezeChoiceV1[]> FreezeChoices(OperationsAccessV1 root,Guid iteration,CancellationToken ct)
    {
        var a=await root.SourceAsync(OperationsOwnerV1.EngineeringIteration,ct);var c=await Prepare(a,iteration,false,ct);
        var result=new List<EngineeringFreezeChoiceV1>();
        foreach(var row in c.Submissions)
        {
            await DisclosePackage(a,row,false,ct);
            result.Add(new(iteration,await View(c.Store,row,c.Iteration.Body.Iteration!.Stage,ct),
                await ApplicationView(await ApplicationContextAsync(a,iteration,row.Id,false,ct),ct),IterationReviewStoreV1.ReadPackage(row).Candidates));
        }
        return result.ToArray();
    }
}
