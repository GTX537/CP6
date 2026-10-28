using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed record RequirementSetApprovalV1(Guid Id,EngineeringSourceRefV1 RequirementSet,EngineeringSourceRefV1 Policy,
    Guid SubmissionId,Guid DecisionId,bool Approved,Guid ActorId,DateTimeOffset AppliedAt);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequirementSetApplicationCommandV1(Guid OperationId,Guid SubmissionId,
    [property:JsonRequired] Guid? ExpectedReceiptId,string Reason);
public sealed record RequirementSetApplicationContextV1(Guid RequirementSetId,Guid SubmissionId,string Etag,string State,
    Guid? ReceiptStreamId,Guid? DecisionId,string? Decision,ApprovalApplicationReceiptV2? Receipt,RequirementSetApprovalV1? Application);
public sealed record RequirementSetApplicationResultV1(Guid OperationId,Guid ObjectId,ApprovalApplicationReceiptV2 Receipt);
public sealed record RequirementSetApplicationOperationV1(Guid OperationId,string State,RequirementSetApplicationResultV1? Result=null);
public sealed record RequirementSetReviewDetailV1(RequirementSetReviewSubmissionV1 Submission,RequirementSetReviewPackageV1 Package,
    RequirementSetReviewPolicyViewV1 Policy,RequirementSetSnapshotV1 RequirementSet,RequirementSetSnapshotV1? Previous,
    RequirementSetApplicationContextV1 Application);

public sealed partial class RequirementSetReviewOwnerV1
{
    private sealed record ApplicationContext(Context Context,RequirementSetReviewStoreV1.SubmissionRow Submission,
        RequirementSetReviewApplicationStoreV1 Store,RequirementSetReviewApplicationStoreV1.Source? Source,ApprovalApplicationReceiptV2? Receipt)
    {
        internal string Etag=>"\"da1-"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{Context.Etag,Submission.Id,
            Source?.OriginalHash,Receipt?.ReceiptId}))+"\"";
    }
    private async Task<ApplicationContext> ApplicationContextAsync(OperationsAccessV1 a,Guid requirementSet,Guid submission,bool write,CancellationToken ct)
    {
        var c=await Prepare(a,requirementSet,write,ct);
        var row=c.Submissions.SingleOrDefault(x=>x.Id==submission)??throw Error("RESOURCE_NOT_FOUND",404);
        await requirementSets.ReviewSource(a,c.Store.View(row).RequirementSet,false,ct);
        var store=new RequirementSetReviewApplicationStoreV1(db,scope);await store.Ready(ct);
        var source=await store.ForSubmission(submission,ct);
        return new(c,row,store,source,source==null?null:await store.Latest(source.Stream,ct));
    }
    private async Task<RequirementSetApplicationContextV1> ApplicationView(ApplicationContext c,CancellationToken ct)
        =>new(c.Submission.RequirementSet,c.Submission.Id,c.Etag,c.Source==null?"NOT_RECEIVED":"RECEIVED",c.Source?.Stream,
            c.Source?.Decision.DecisionId,c.Source?.Decision.Decision,c.Receipt,await c.Store.Application(c.Submission.Id,ct));
    public Task<RequirementSetReviewDetailV1> SubmissionAsync(Guid requirementSet,Guid submission,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.RequirementSet,"query",false,async(a,t)=>{
            var c=await ApplicationContextAsync(a,requirementSet,submission,false,t);var row=c.Submission;
            var package=RequirementSetReviewStoreV1.ReadPackage(row);
            var exact=await requirementSets.ReviewSource(a,package.RequirementSet,false,t);
            var previous=package.Previous==null?null:await requirementSets.ReviewSource(a,package.Previous,false,t);
            var policy=c.Context.Policies.SingleOrDefault(p=>p.Reference==c.Context.Store.View(row).Policy)
                ??throw Error("REQUIREMENT_SET_POLICY_UNAVAILABLE",503);
            var app=await ApplicationView(c,t);
            var view=await SubmissionView(c.Context.Store,row,t);
            return new RequirementSetReviewDetailV1(view,package,policy,exact,previous,app);
        },ct);
    private async Task<RequirementSetReviewSubmissionV1> SubmissionView(RequirementSetReviewStoreV1 store,
        RequirementSetReviewStoreV1.SubmissionRow row,CancellationToken ct)
    {
        var transport=await CP6.Core.Services.Wms.ToolApprovalDeliveryV1.ReadAsync(db,Environment,row.Id,ct,RequirementSetReviewStoreV1.Owner);
        var applicationStore=new RequirementSetReviewApplicationStoreV1(db,scope);await applicationStore.Ready(ct);
        var application=await applicationStore.Application(row.Id,ct);
        return store.View(row) with {TransportState=transport?.State??"PENDING",IsApproved=application?.Approved==true,
            State=application==null?"SUBMITTED":application.Approved?"APPROVED":"REJECTED"};
    }
    private async Task QualifyApplication(ApplicationContext c,CancellationToken ct)
    {
        var source=c.Source!;if(source.EvidenceMode!=requirementSets.EvidenceMode)throw Error("REQUIREMENT_SET_EVIDENCE_MODE_CHANGED",422);var view=c.Context.Store.View(c.Submission);
        if(source.Decision.Subject!=source.Submission.Subject || source.Decision.SubmissionId!=c.Submission.Id
            || source.Decision.ConsumerOwner!=RequirementSetReviewStoreV1.Owner)throw Error("REQUIREMENT_SET_DECISION_SOURCE_UNAVAILABLE",503);
        if(source.Decision.Decision is not ("APPROVED" or "REJECTED"))throw Error("REQUIREMENT_SET_DECISION_NOT_APPLICABLE",422);
        // A rejection preserves the signed refusal and does not confer use of unsafe content.
        if(source.Decision.Decision=="APPROVED")
        {
            var requirementSet=await requirementSets.ReviewSource(c.Context.Access,view.RequirementSet,true,ct);
            if(requirementSet.Missing!.Any(x=>x!="REQUIREMENT_SET_REVIEW_REQUIRED"))
                throw Error("REQUIREMENT_SET_SOURCE_INCOMPLETE",422);
            var policy=await RequirePolicy(c.Context,view.Policy,ct);
            foreach(var requirement in policy.Fact.Body.Requirements)
                if(!source.Decision.SatisfiedRequirementIds.Contains("requirement-set.requirement/"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(requirement))))
                    throw Error("REQUIREMENT_SET_PROFESSIONAL_REVIEW_INCOMPLETE",422);
            c.Context.Access.AtCommit(async t=>{
                var current=c.Context with {Policies=await c.Context.Store.Policies(c.Submission.RequirementSet,t)};
                await RequirePolicy(current,view.Policy,t);
            });
        }
    }
    public Task<RequirementSetApplicationResultV1> ApplyAsync(Guid requirementSet,string? etag,RequirementSetApplicationCommandV1 command,CancellationToken ct=default)
    {
        if(requirementSet==Guid.Empty || command.OperationId==Guid.Empty || command.SubmissionId==Guid.Empty)throw Error("REQUIREMENT_SET_APPLICATION_COMMAND_INVALID",400);
        RequirementSetValuesV1.Text(command.Reason);
        if(string.IsNullOrWhiteSpace(command.Reason))throw Error("REQUIREMENT_SET_APPLICATION_REASON_REQUIRED",400);
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract=RequirementSetReviewApplicationStoreV1.Contract,environment=Environment,requirementSet,etag,command});
        return Authority.ExecuteAsync(OperationsOwnerV1.RequirementSet,"review-apply",true,async(a,t)=>{
            var c=await ApplicationContextAsync(a,requirementSet,command.SubmissionId,original==null,t);
            if(original!=null)return await DiscloseApplication(c,command.OperationId,original,bytes,t);
            if(string.IsNullOrEmpty(etag))throw Error("IF_MATCH_REQUIRED",428);
            if(etag!=c.Etag || c.Receipt?.ReceiptId!=command.ExpectedReceiptId)throw Error("REQUIREMENT_SET_APPLICATION_CONTEXT_CHANGED",412);
            var source=c.Source??throw Error("REQUIREMENT_SET_DECISION_NOT_RECEIVED",422);
            ApprovalApplicationReceiptV2 receipt;
            if(c.Receipt is {Application:"APPLIED"})receipt=c.Receipt;
            else
            {
                await QualifyApplication(c,t);
                var view=c.Context.Store.View(c.Submission);
                var applied=new RequirementSetApprovalV1(Guid.NewGuid(),view.RequirementSet,view.Policy,source.Decision.SubmissionId,
                    source.Decision.DecisionId,source.Decision.Decision=="APPROVED",a.ActorId,Now());
                receipt=await c.Store.Append(source,a.ActorId,command.OperationId,c.Receipt,"APPLIED",
                    applied.Approved?"REQUIREMENT_SET_APPROVED":"REQUIREMENT_SET_REJECTED",c.Context.RequirementSet.Reference.Version,
                    new{profile="Main.Plm.RequirementSetApplication/1",command,applied,authorities=Witnesses(a),source.OriginalHash,source.EvidenceMode},applied,t);
            }
            var result=new RequirementSetApplicationResultV1(command.OperationId,requirementSet,receipt);
            await journal.Store(RequirementSetReviewApplicationStoreV1.Journal,RequirementSetReviewApplicationStoreV1.Contract,command.OperationId,
                a.ActorId,bytes,result,[requirementSet],new{command,receipt.ReceiptId,receipt.PayloadDigest,source.OriginalHash},t);
            a.AtCommit(async token=>{
                await c.Store.Ready(token);
                if((await c.Store.ForSubmission(command.SubmissionId,token))?.OriginalHash!=source.OriginalHash
                    || (await c.Store.Latest(source.Stream,token))?.ReceiptId!=receipt.ReceiptId)
                    throw Error("REQUIREMENT_SET_APPLICATION_CONTEXT_CHANGED",412);
            });
            return result;
        },ct,actionAtFence:async t=>{
            original=await journal.Read(RequirementSetReviewApplicationStoreV1.Journal,command.OperationId,t);return original==null?"review-apply":"query";
        });
    }
    private async Task<RequirementSetApplicationResultV1> DiscloseApplication(ApplicationContext c,Guid operation,
        MainOwnerOperationJournalV1.Original original,byte[]? bytes,CancellationToken ct)
    {
        if(original.Actor!=c.Context.Access.ActorId || original.Contract!=RequirementSetReviewApplicationStoreV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null && !MainOwnerOperationJournalV1.Matches(original,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<RequirementSetApplicationResultV1>(original.Result);
        if(original.State!="COMMITTED" || result==null || result.OperationId!=operation || result.ObjectId!=c.Submission.RequirementSet
            || !original.Objects.SequenceEqual(new[]{result.ObjectId}) || c.Submission.Id!=result.Receipt.SubmissionId
            || Json(await c.Store.Exact(result.Receipt.ReceiptId,ct))!=Json(result.Receipt))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        return result;
    }
    public Task<RequirementSetApplicationOperationV1> ApplicationOperationAsync(Guid operation,bool close,CancellationToken ct=default)
    {
        if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.RequirementSet,"query",close,async(a,t)=>{
            a.RequireRequirementSetFields(false);var journal=new MainOwnerOperationJournalV1(db);
            var original=await journal.Read(RequirementSetReviewApplicationStoreV1.Journal,operation,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=RequirementSetReviewApplicationStoreV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new RequirementSetApplicationOperationV1(operation,"CLOSED");
                var result=JsonSerializer.Deserialize<RequirementSetApplicationResultV1>(original.Result)??throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
                var c=await ApplicationContextAsync(a,result.ObjectId,result.Receipt.SubmissionId,false,t);
                return new RequirementSetApplicationOperationV1(operation,"COMMITTED",await DiscloseApplication(c,operation,original,null,t));
            }
            if(!close)return new RequirementSetApplicationOperationV1(operation,"UNKNOWN");
            var closed=new RequirementSetApplicationOperationV1(operation,"CLOSED");
            await journal.Store(RequirementSetReviewApplicationStoreV1.Journal,RequirementSetReviewApplicationStoreV1.Contract,operation,a.ActorId,null,closed,[],
                new{action="close-uncommitted-requirement-set-application",a.ActorId},t);
            var saved=await journal.Read(RequirementSetReviewApplicationStoreV1.Journal,operation,t);
            if(saved==null || saved.State!="CLOSED" || saved.Actor!=a.ActorId || saved.Contract!=RequirementSetReviewApplicationStoreV1.Contract)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
            return closed;
        },ct,readOnlyAuthorityFence:!close);
    }
}
