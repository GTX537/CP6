using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wms;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;

public sealed record IterationApprovalV1(Guid Id,EngineeringSourceRefV1 Iteration,EngineeringSourceRefV1 Policy,
    Guid SubmissionId,Guid DecisionId,bool Approved,Guid ActorId,DateTimeOffset AppliedAt,EngineeringSourceRefV1 AppliedIteration,string BusinessStage);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IterationApplicationCommandV1(Guid OperationId,Guid SubmissionId,[property:JsonRequired] Guid? ExpectedReceiptId,string Reason);
public sealed record IterationApplicationContextV1(Guid IterationId,Guid SubmissionId,string Etag,string State,string? ControlRowVersion,
    string BusinessStage,Guid? ReceiptStreamId,Guid? DecisionId,string? Decision,ApprovalApplicationReceiptV2? Receipt,IterationApprovalV1? Application,
    ToolApplicationDeliveryViewV2? WithdrawalDelivery);
public sealed record IterationApplicationResultV1(Guid OperationId,Guid ObjectId,ApprovalApplicationReceiptV2 Receipt);
public sealed record IterationApplicationOperationV1(Guid OperationId,string State,IterationApplicationResultV1? Result=null);

public sealed partial class IterationReviewOwnerV1
{
    private sealed record ApplicationContext(Context Context,IterationReviewStoreV1.SubmissionRow Submission,
        IterationReviewApplicationStoreV1 Store,IterationReviewApplicationStoreV1.Source? Source,ApprovalApplicationReceiptV2? Receipt,
        IterationReviewControlStoreV1.Head? Control,IterationReviewIssueV1[] Issues)
    {
        internal string Etag=>"\"ia1-"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{Context.Etag,Submission.Id,
            Source?.OriginalHash,Receipt?.ReceiptId,Control?.RowVersion,Issues}))+"\"";
    }
    private async Task<ApplicationContext> ApplicationContextAsync(OperationsAccessV1 a,Guid id,Guid submission,bool write,CancellationToken ct)
    {
        var c=await Prepare(a,id,write,ct);var row=c.Submissions.SingleOrDefault(x=>x.Id==submission)??throw Error("RESOURCE_NOT_FOUND",404);
        await DisclosePackage(a,row,false,ct);
        var control=new IterationReviewControlStoreV1(db,Environment);var head=await control.Read(id,ct);var issues=await ReadIssues(c,ct);
        var store=new IterationReviewApplicationStoreV1(db,scope);await store.Ready(ct);var source=await store.ForSubmission(submission,ct);
        return new(c,row,store,source,source==null?null:await store.Latest(source.Stream,ct),head,issues);
    }
    private async Task<IterationApplicationContextV1> ApplicationView(ApplicationContext c,CancellationToken ct)
        =>new(c.Submission.Iteration,c.Submission.Id,c.Etag,c.Source==null?"NOT_RECEIVED":"RECEIVED",c.Control?.RowVersion,
            c.Context.Iteration.Body.Iteration!.Stage,c.Source?.Stream,c.Source?.Decision.DecisionId,c.Source?.Decision.Decision,c.Receipt,await c.Store.Application(c.Submission.Id,ct),
            await IterationWithdrawalDeliveryV1.ForSubmission(db,Environment,c.Submission.Id,ct));
    public Task<IterationApplicationContextV1> ApplicationAsync(Guid id,Guid submission,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",false,async(a,t)=>await ApplicationView(await ApplicationContextAsync(a,id,submission,false,t),t),ct);
    private async Task<string?> QualifyApplication(ApplicationContext c,CancellationToken ct)
    {
        var source=c.Source!;var row=c.Submission;var package=IterationReviewStoreV1.ReadPackage(row);
        if(source.EvidenceMode!=iterations.EvidenceMode)throw Error("ITERATION_REVIEW_EVIDENCE_MODE_CHANGED",422);
        if(source.Decision.Subject!=source.Submission.Subject || source.Decision.SubmissionId!=row.Id || source.Unit!="PLM_ITER_REVIEW_DECISION"
            || source.Decision.ConsumerOwner!=IterationReviewStoreV1.Owner)throw Error("ITERATION_REVIEW_DECISION_SOURCE_UNAVAILABLE",503);
        if(source.Decision.Decision is not("APPROVED" or "REJECTED"))throw Error("ITERATION_REVIEW_DECISION_NOT_APPLICABLE",422);
        if(source.Decision.Decision=="REJECTED")return null;
        if(c.Issues.Any(i=>i.Severity=="BLOCKING" && i.State!="VERIFIED_CLOSED"))return "BLOCKED_OPEN_ISSUES";
        await DisclosePackage(c.Context.Access,row,true,ct);
        var policy=await Policy(c.Context,package.Policy,ct);
        foreach(var requirement in policy.Fact.Body.Requirements)
            if(!source.Decision.SatisfiedRequirementIds.Contains("iteration.requirement/"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(requirement))))
                throw Error("ITERATION_REVIEW_PROFESSIONAL_REVIEW_INCOMPLETE",422);
        return null;
    }
    public Task<IterationApplicationResultV1> ApplyAsync(Guid id,string? etag,IterationApplicationCommandV1 input,CancellationToken ct=default)
    {
        if(input==null || id==Guid.Empty || input.OperationId==Guid.Empty || input.SubmissionId==Guid.Empty)throw Error("ITERATION_REVIEW_APPLICATION_COMMAND_INVALID",400);
        Text(input.Reason,true,1000);var command=OperationsOwnerV1.Freeze(input);
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract=IterationReviewApplicationStoreV1.Contract,environment=Environment,id,etag,command});
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"review-apply",true,async(a,t)=>{
            var c=await ApplicationContextAsync(a,id,command.SubmissionId,original==null,t);
            if(original!=null)return await DiscloseApplication(c,command.OperationId,original,bytes,t);
            if(string.IsNullOrEmpty(etag))throw Error("IF_MATCH_REQUIRED",428);
            if(etag!=c.Etag || c.Receipt?.ReceiptId!=command.ExpectedReceiptId)throw Error("ITERATION_REVIEW_APPLICATION_CONTEXT_CHANGED",412);
            a.RequireEngineeringIterationFields(true);
            var source=c.Source??throw Error("ITERATION_REVIEW_DECISION_NOT_RECEIVED",422);
            var receipt=c.Receipt;
            if(receipt?.Application is not("APPLIED" or "STALE" or "CONFLICT"))
            {
                if(receipt is {Application:"BLOCKED"} && receipt.ActionActorId!=a.ActorId)throw Error("ITERATION_REVIEW_ORIGINAL_APPLIER_REQUIRED",403);
                var current=c.Context.Iteration;var work=current.Body.Iteration!;var package=IterationReviewStoreV1.ReadPackage(c.Submission);
                string state,outcome;IterationApprovalV1? applied=null;
                if(c.Control?.Submission!=c.Submission.Id || c.Control.State!="IN_REVIEW" || work.CurrentReview?.SubmissionId!=c.Submission.Id
                    || work.CurrentReview.BindingId!=c.Submission.Binding || work.CurrentReview.RoundId!=c.Submission.Round || work.Stage!="IN_REVIEW")
                {state="STALE";outcome="STALE_RECEIPT";}
                else
                {
                    var blocked=await QualifyApplication(c,t);
                    if(blocked!=null){state="BLOCKED";outcome=blocked;}
                    else
                    {
                        var policy=c.Context.Policies.Single(p=>p.Reference==package.Policy);
                        var approved=source.Decision.Decision=="APPROVED";
                        var stage=approved?"REVIEW_APPROVED":policy.Fact.Body.RejectDisposition=="RETURN"?"IN_DESIGN":policy.Fact.Body.RejectDisposition=="REJECT"?"AWAITING_EVALUATION":null;
                        if(stage==null){state="BLOCKED";outcome="BLOCKED_POLICY_MAPPING";}
                        else
                        {
                            // Both withdrawal and application use the same actual control token and native iteration head in this transaction.
                            var next=await iterations.ReviewTransition(a,current,c.Submission.Id,command.OperationId,"review-apply",stage,stage=="IN_DESIGN",command.Reason,t);
                            await new IterationReviewControlStoreV1(db,Environment).Transition(c.Control,"APPLIED",t);
                            applied=new(Guid.NewGuid(),package.Iteration,package.Policy,c.Submission.Id,source.Decision.DecisionId,approved,a.ActorId,Now(),next.Reference,stage);
                            state="APPLIED";outcome=approved?"ITERATION_APPROVED":"ITERATION_REJECTED";
                        }
                    }
                }
                receipt=await c.Store.Append(source,a.ActorId,command.OperationId,c.Receipt,state,outcome,applied?.AppliedIteration.Version??current.Reference.Version,
                    new{profile="Main.Plm.IterationApplication/1",command,applied,beforeControl=c.Control?.RowVersion,issues=c.Issues,
                        authorities=Witnesses(a),source.OriginalHash,source.EvidenceMode},applied,t);
            }
            var result=new IterationApplicationResultV1(command.OperationId,id,receipt!);
            await journal.Store(IterationReviewApplicationStoreV1.Journal,IterationReviewApplicationStoreV1.Contract,command.OperationId,a.ActorId,bytes,result,[id],
                new{command,receipt!.ReceiptId,receipt.PayloadDigest,source.OriginalHash,authorities=Witnesses(a)},t);
            a.AtCommit(async token=>{
                await c.Store.Ready(token);
                if((await c.Store.ForSubmission(command.SubmissionId,token))?.OriginalHash!=source.OriginalHash
                    || (await c.Store.Latest(source.Stream,token))?.ReceiptId!=receipt.ReceiptId)throw Error("ITERATION_REVIEW_APPLICATION_CONTEXT_CHANGED",412);
                if(receipt.Application=="APPLIED" && source.Decision.Decision=="APPROVED")
                {
                    await a.EngineeringFinalObservation(async(observation,t)=>{
                        await DisclosePackage(observation,c.Submission,true,t);await Policy(c.Context with{Access=observation,Policies=await c.Context.Store.Policies(id,t)},
                            IterationReviewStoreV1.ReadPackage(c.Submission).Policy,t);return true;
                    },token);
                }
            });
            return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(IterationReviewApplicationStoreV1.Journal,command.OperationId,t);return original==null?"review-apply":"query";});
    }
    private async Task<IterationApplicationResultV1> DiscloseApplication(ApplicationContext c,Guid operation,MainOwnerOperationJournalV1.Original original,byte[]? bytes,CancellationToken ct)
    {
        if(original.Actor!=c.Context.Access.ActorId || original.Contract!=IterationReviewApplicationStoreV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null && !MainOwnerOperationJournalV1.Matches(original,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var r=JsonSerializer.Deserialize<IterationApplicationResultV1>(original.Result);
        if(original.State!="COMMITTED" || r==null || r.OperationId!=operation || r.ObjectId!=c.Submission.Iteration
            || !original.Objects.SequenceEqual(new[]{r.ObjectId}) || r.Receipt.SubmissionId!=c.Submission.Id
            || Json(await c.Store.Exact(r.Receipt.ReceiptId,ct))!=Json(r.Receipt))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        return r;
    }
    public Task<IterationApplicationOperationV1> ApplicationOperationAsync(Guid operation,bool close,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",close,async(a,t)=>{
            if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
            await scope.Root(t);a.RequireEngineeringIterationFields(false);var journal=new MainOwnerOperationJournalV1(db);
            var original=await journal.Read(IterationReviewApplicationStoreV1.Journal,operation,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=IterationReviewApplicationStoreV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new IterationApplicationOperationV1(operation,"CLOSED");
                var r=JsonSerializer.Deserialize<IterationApplicationResultV1>(original.Result)??throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
                return new IterationApplicationOperationV1(operation,"COMMITTED",await DiscloseApplication(await ApplicationContextAsync(a,r.ObjectId,r.Receipt.SubmissionId,false,t),operation,original,null,t));
            }
            if(!close)return new IterationApplicationOperationV1(operation,"UNKNOWN");
            var closed=new IterationApplicationOperationV1(operation,"CLOSED");
            await journal.Store(IterationReviewApplicationStoreV1.Journal,IterationReviewApplicationStoreV1.Contract,operation,a.ActorId,null,closed,[],new{action="close-uncommitted-iteration-application",a.ActorId},t);return closed;
        },ct,readOnlyAuthorityFence:!close);
}
