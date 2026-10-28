using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed record DocumentApprovalV1(Guid Id,EngineeringSourceRefV1 Document,EngineeringSourceRefV1 Policy,
    Guid SubmissionId,Guid DecisionId,bool Approved,Guid ActorId,DateTimeOffset AppliedAt);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DocumentApplicationCommandV1(Guid OperationId,Guid SubmissionId,
    [property:JsonRequired] Guid? ExpectedReceiptId,string Reason);
public sealed record DocumentApplicationContextV1(Guid DocumentId,Guid SubmissionId,string Etag,string State,
    Guid? ReceiptStreamId,Guid? DecisionId,string? Decision,ApprovalApplicationReceiptV2? Receipt,DocumentApprovalV1? Application);
public sealed record DocumentApplicationResultV1(Guid OperationId,Guid ObjectId,ApprovalApplicationReceiptV2 Receipt);
public sealed record DocumentApplicationOperationV1(Guid OperationId,string State,DocumentApplicationResultV1? Result=null);
public sealed record DocumentReviewDetailV1(DocumentReviewSubmissionV1 Submission,DocumentReviewPackageV1 Package,
    DocumentReviewPolicyViewV1 Policy,ControlledDocumentSnapshotV1 Document,ControlledDocumentSnapshotV1? Previous,
    DocumentApplicationContextV1 Application);

public sealed partial class ControlledDocumentReviewOwnerV1
{
    private sealed record ApplicationContext(Context Context,ControlledDocumentReviewStoreV1.SubmissionRow Submission,
        DocumentReviewApplicationStoreV1 Store,DocumentReviewApplicationStoreV1.Source? Source,ApprovalApplicationReceiptV2? Receipt)
    {
        internal string Etag=>"\"da1-"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{Context.Etag,Submission.Id,
            Source?.OriginalHash,Receipt?.ReceiptId}))+"\"";
    }
    private async Task<ApplicationContext> ApplicationContextAsync(OperationsAccessV1 a,Guid document,Guid submission,bool write,CancellationToken ct)
    {
        var c=await Prepare(a,document,write,ct);
        var row=c.Submissions.SingleOrDefault(x=>x.Id==submission)??throw Error("RESOURCE_NOT_FOUND",404);
        await documents.ReviewSource(a,c.Store.View(row).Document,false,ct);
        var store=new DocumentReviewApplicationStoreV1(db,scope);await store.Ready(ct);
        var source=await store.ForSubmission(submission,ct);
        return new(c,row,store,source,source==null?null:await store.Latest(source.Stream,ct));
    }
    private async Task<DocumentApplicationContextV1> ApplicationView(ApplicationContext c,CancellationToken ct)
        =>new(c.Submission.Document,c.Submission.Id,c.Etag,c.Source==null?"NOT_RECEIVED":"RECEIVED",c.Source?.Stream,
            c.Source?.Decision.DecisionId,c.Source?.Decision.Decision,c.Receipt,await c.Store.Application(c.Submission.Id,ct));
    public Task<DocumentReviewDetailV1> SubmissionAsync(Guid document,Guid submission,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.ControlledDocument,"query",false,async(a,t)=>{
            var c=await ApplicationContextAsync(a,document,submission,false,t);var row=c.Submission;
            var package=ControlledDocumentReviewStoreV1.ReadPackage(row);
            var exact=await documents.ReviewSource(a,package.Document,false,t);
            var previous=package.Previous==null?null:await documents.ReviewSource(a,package.Previous,false,t);
            var policy=c.Context.Policies.SingleOrDefault(p=>p.Reference==c.Context.Store.View(row).Policy)
                ??throw Error("DOCUMENT_POLICY_UNAVAILABLE",503);
            var app=await ApplicationView(c,t);
            var view=await SubmissionView(c.Context.Store,row,t);
            return new DocumentReviewDetailV1(view,package,policy,exact,previous,app);
        },ct);
    private async Task<DocumentReviewSubmissionV1> SubmissionView(ControlledDocumentReviewStoreV1 store,
        ControlledDocumentReviewStoreV1.SubmissionRow row,CancellationToken ct)
    {
        var transport=await CP6.Core.Services.Wms.ToolApprovalDeliveryV1.ReadAsync(db,Environment,row.Id,ct,ControlledDocumentReviewStoreV1.Owner);
        var applicationStore=new DocumentReviewApplicationStoreV1(db,scope);await applicationStore.Ready(ct);
        var application=await applicationStore.Application(row.Id,ct);
        return store.View(row) with {TransportState=transport?.State??"PENDING",IsApproved=application?.Approved==true,
            State=application==null?"SUBMITTED":application.Approved?"APPROVED":"REJECTED"};
    }
    private async Task QualifyApplication(ApplicationContext c,CancellationToken ct)
    {
        var source=c.Source!;var view=c.Context.Store.View(c.Submission);
        if(source.Decision.Subject!=source.Submission.Subject || source.Decision.SubmissionId!=c.Submission.Id
            || source.Decision.ConsumerOwner!=ControlledDocumentReviewStoreV1.Owner)throw Error("DOCUMENT_DECISION_SOURCE_UNAVAILABLE",503);
        if(source.Decision.Decision is not ("APPROVED" or "REJECTED"))throw Error("DOCUMENT_DECISION_NOT_APPLICABLE",422);
        // A rejection preserves the signed refusal and does not confer use of unsafe content.
        if(source.Decision.Decision=="APPROVED")
        {
            var document=await documents.ReviewSource(c.Context.Access,view.Document,true,ct);
            if(document.Missing!.Any(x=>x is not ("DOCUMENT_PROFESSIONAL_REVIEW_REQUIRED" or "LANGUAGE_AUTHORITY_REVIEW_REQUIRED")))
                throw Error("DOCUMENT_SOURCE_INCOMPLETE",422);
            var policy=await RequirePolicy(c.Context,view.Policy,ct);
            foreach(var requirement in policy.Fact.Body.Requirements)
                if(!source.Decision.SatisfiedRequirementIds.Contains("document.requirement/"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(requirement))))
                    throw Error("DOCUMENT_PROFESSIONAL_REVIEW_INCOMPLETE",422);
            c.Context.Access.AtCommit(async t=>{
                var current=c.Context with {Policies=await c.Context.Store.Policies(c.Submission.Document,t)};
                await RequirePolicy(current,view.Policy,t);
            });
        }
    }
    public Task<DocumentApplicationResultV1> ApplyAsync(Guid document,string? etag,DocumentApplicationCommandV1 command,CancellationToken ct=default)
    {
        if(document==Guid.Empty || command.OperationId==Guid.Empty || command.SubmissionId==Guid.Empty)throw Error("DOCUMENT_APPLICATION_COMMAND_INVALID",400);
        ControlledDocumentValuesV1.Text(command.Reason);
        if(string.IsNullOrWhiteSpace(command.Reason))throw Error("DOCUMENT_APPLICATION_REASON_REQUIRED",400);
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract=DocumentReviewApplicationStoreV1.Contract,environment=Environment,document,etag,command});
        return Authority.ExecuteAsync(OperationsOwnerV1.ControlledDocument,"review-apply",true,async(a,t)=>{
            var c=await ApplicationContextAsync(a,document,command.SubmissionId,original==null,t);
            if(original!=null)return await DiscloseApplication(c,command.OperationId,original,bytes,t);
            if(string.IsNullOrEmpty(etag))throw Error("IF_MATCH_REQUIRED",428);
            if(etag!=c.Etag || c.Receipt?.ReceiptId!=command.ExpectedReceiptId)throw Error("DOCUMENT_APPLICATION_CONTEXT_CHANGED",412);
            var source=c.Source??throw Error("DOCUMENT_DECISION_NOT_RECEIVED",422);
            ApprovalApplicationReceiptV2 receipt;
            if(c.Receipt is {Application:"APPLIED"})receipt=c.Receipt;
            else
            {
                await QualifyApplication(c,t);
                var view=c.Context.Store.View(c.Submission);
                var applied=new DocumentApprovalV1(Guid.NewGuid(),view.Document,view.Policy,source.Decision.SubmissionId,
                    source.Decision.DecisionId,source.Decision.Decision=="APPROVED",a.ActorId,Now());
                receipt=await c.Store.Append(source,a.ActorId,command.OperationId,c.Receipt,"APPLIED",
                    applied.Approved?"DOCUMENT_APPROVED":"DOCUMENT_REJECTED",c.Context.Document.Reference.Version,
                    new{profile="Main.Plm.DocumentApplication/1",command,applied,authorities=Witnesses(a),source.OriginalHash,source.EvidenceMode},applied,t);
            }
            var result=new DocumentApplicationResultV1(command.OperationId,document,receipt);
            await journal.Store(DocumentReviewApplicationStoreV1.Journal,DocumentReviewApplicationStoreV1.Contract,command.OperationId,
                a.ActorId,bytes,result,[document],new{command,receipt.ReceiptId,receipt.PayloadDigest,source.OriginalHash},t);
            a.AtCommit(async token=>{
                await c.Store.Ready(token);
                if((await c.Store.ForSubmission(command.SubmissionId,token))?.OriginalHash!=source.OriginalHash
                    || (await c.Store.Latest(source.Stream,token))?.ReceiptId!=receipt.ReceiptId)
                    throw Error("DOCUMENT_APPLICATION_CONTEXT_CHANGED",412);
            });
            return result;
        },ct,actionAtFence:async t=>{
            original=await journal.Read(DocumentReviewApplicationStoreV1.Journal,command.OperationId,t);return original==null?"review-apply":"query";
        });
    }
    private async Task<DocumentApplicationResultV1> DiscloseApplication(ApplicationContext c,Guid operation,
        MainOwnerOperationJournalV1.Original original,byte[]? bytes,CancellationToken ct)
    {
        if(original.Actor!=c.Context.Access.ActorId || original.Contract!=DocumentReviewApplicationStoreV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null && !MainOwnerOperationJournalV1.Matches(original,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<DocumentApplicationResultV1>(original.Result);
        if(original.State!="COMMITTED" || result==null || result.OperationId!=operation || result.ObjectId!=c.Submission.Document
            || !original.Objects.SequenceEqual(new[]{result.ObjectId}) || c.Submission.Id!=result.Receipt.SubmissionId
            || Json(await c.Store.Exact(result.Receipt.ReceiptId,ct))!=Json(result.Receipt))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        return result;
    }
    public Task<DocumentApplicationOperationV1> ApplicationOperationAsync(Guid operation,bool close,CancellationToken ct=default)
    {
        if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ControlledDocument,"query",close,async(a,t)=>{
            a.RequireDocumentFields(false);var journal=new MainOwnerOperationJournalV1(db);
            var original=await journal.Read(DocumentReviewApplicationStoreV1.Journal,operation,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=DocumentReviewApplicationStoreV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new DocumentApplicationOperationV1(operation,"CLOSED");
                var result=JsonSerializer.Deserialize<DocumentApplicationResultV1>(original.Result)??throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
                var c=await ApplicationContextAsync(a,result.ObjectId,result.Receipt.SubmissionId,false,t);
                return new DocumentApplicationOperationV1(operation,"COMMITTED",await DiscloseApplication(c,operation,original,null,t));
            }
            if(!close)return new DocumentApplicationOperationV1(operation,"UNKNOWN");
            var closed=new DocumentApplicationOperationV1(operation,"CLOSED");
            await journal.Store(DocumentReviewApplicationStoreV1.Journal,DocumentReviewApplicationStoreV1.Contract,operation,a.ActorId,null,closed,[],
                new{action="close-uncommitted-document-application",a.ActorId},t);return closed;
        },ct,readOnlyAuthorityFence:!close);
    }
}
