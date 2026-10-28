using CP6.Core.Services.Plm;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Wms;

/// <summary>Immutable 08Q approval evidence. This never grants 08E rule activation.</summary>
public sealed record LocationRuleApprovalV1(Guid Id,EngineeringSourceRefV1 LocationRule,EngineeringSourceRefV1 Policy,
    Guid SubmissionId,Guid DecisionId,bool Approved,Guid ActorId,DateTimeOffset AppliedAt);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationRuleApplicationCommandV1(Guid OperationId,Guid SubmissionId,
    [property:JsonRequired] Guid? ExpectedReceiptId,string Reason);
public sealed record LocationRuleApplicationContextV1(Guid LocationRuleId,Guid SubmissionId,string Etag,string State,
    Guid? ReceiptStreamId,Guid? DecisionId,string? Decision,ApprovalApplicationReceiptV2? Receipt,LocationRuleApprovalV1? Application);
public sealed record LocationRuleApplicationResultV1(Guid OperationId,Guid ObjectId,ApprovalApplicationReceiptV2 Receipt);
public sealed record LocationRuleApplicationOperationV1(Guid OperationId,string State,LocationRuleApplicationResultV1? Result=null);
public sealed record LocationRuleReviewDetailV1(LocationRuleReviewSubmissionV1 Submission,LocationRuleReviewPackageV1 Package,
    LocationRuleReviewPolicyViewV1 Policy,LocationRuleReviewSourceViewV1 LocationRule,LocationRuleReviewSourceViewV1? Previous,
    LocationRuleApplicationContextV1 Application);

public sealed partial class LocationRuleReviewOwnerV1
{
    private sealed record ApplicationContext(Context Context,LocationRuleReviewStoreV1.SubmissionRow Submission,
        LocationRuleReviewApplicationStoreV1 Store,LocationRuleReviewApplicationStoreV1.Source? Source,ApprovalApplicationReceiptV2? Receipt)
    {
        internal string Etag=>"\"da1-"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{Context.Etag,Submission.Id,
            Source?.OriginalHash,Receipt?.ReceiptId}))+"\"";
    }
    private async Task<ApplicationContext> ApplicationContextAsync(OperationsAccessV1 a,Guid locationRule,Guid submission,bool write,CancellationToken ct)
    {
        var c=await Prepare(a,locationRule,write,ct);
        var row=c.Submissions.SingleOrDefault(x=>x.Id==submission)??throw Error("RESOURCE_NOT_FOUND",404);
        await locationRules.ReviewSource(a,c.Store.View(row).LocationRule,false,ct);
        var store=new LocationRuleReviewApplicationStoreV1(db,scope);await store.Ready(ct);
        var source=await store.ForSubmission(submission,ct);
        return new(c,row,store,source,source==null?null:await store.Latest(source.Stream,ct));
    }
    private async Task<LocationRuleApplicationContextV1> ApplicationView(ApplicationContext c,CancellationToken ct)
        =>new(c.Submission.LocationRule,c.Submission.Id,c.Etag,c.Source==null?"NOT_RECEIVED":"RECEIVED",c.Source?.Stream,
            c.Source?.Decision.DecisionId,c.Source?.Decision.Decision,c.Receipt,await c.Store.Application(c.Submission.Id,ct));
    public Task<LocationRuleReviewDetailV1> SubmissionAsync(Guid locationRule,Guid submission,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.LocationRule,"query",false,async(a,t)=>{
            var c=await ApplicationContextAsync(a,locationRule,submission,false,t);var row=c.Submission;
            var package=LocationRuleReviewStoreV1.ReadPackage(row);
            var exact=await locationRules.ReviewSource(a,package.LocationRule,false,t);
            var previous=package.Previous==null?null:await locationRules.ReviewSource(a,package.Previous,false,t);
            var policy=c.Context.Policies.SingleOrDefault(p=>p.Reference==c.Context.Store.View(row).Policy)
                ??throw Error("LOCATION_RULE_POLICY_UNAVAILABLE",503);
            var app=await ApplicationView(c,t);
            var view=await SubmissionView(c.Context.Store,row,t);
            return new LocationRuleReviewDetailV1(view,package,policy,exact,previous,app);
        },ct);
    private async Task<LocationRuleReviewSubmissionV1> SubmissionView(LocationRuleReviewStoreV1 store,
        LocationRuleReviewStoreV1.SubmissionRow row,CancellationToken ct)
    {
        var transport=await CP6.Core.Services.Wms.ToolApprovalDeliveryV1.ReadAsync(db,Environment,row.Id,ct,LocationRuleReviewStoreV1.Owner);
        var applicationStore=new LocationRuleReviewApplicationStoreV1(db,scope);await applicationStore.Ready(ct);
        var application=await applicationStore.Application(row.Id,ct);
        var current=await new LocationRuleReviewOriginalsV1(db).Read(row.LocationRule,null,ct);
        var state=current.State=="WITHDRAWN"&&current.ActiveSubmissionId==row.Id?"WITHDRAWN":
            application==null?"SUBMITTED":application.Approved?"APPROVED_PENDING":(await store.Policies(row.LocationRule,ct)).Single(p=>p.Reference==application.Policy).Fact.Body.RejectionDisposition;
        return store.View(row) with {TransportState=transport?.State??"PENDING",IsApproved=application?.Approved==true,State=state};
    }
    private async Task QualifyApplication(ApplicationContext c,CancellationToken ct)
    {
        var source=c.Source!;if(source.EvidenceMode!=locationRules.EvidenceMode)throw Error("LOCATION_RULE_EVIDENCE_MODE_CHANGED",422);var view=c.Context.Store.View(c.Submission);
        if(source.Decision.Subject!=source.Submission.Subject || source.Decision.SubmissionId!=c.Submission.Id
            || source.Decision.ConsumerOwner!=LocationRuleReviewStoreV1.Owner)throw Error("LOCATION_RULE_DECISION_SOURCE_UNAVAILABLE",503);
        if(source.Decision.Decision is not ("APPROVED" or "REJECTED"))throw Error("LOCATION_RULE_DECISION_NOT_APPLICABLE",422);
        // A rejection preserves the signed refusal and does not confer use of unsafe content.
        if(source.Decision.Decision=="APPROVED")
        {
            var locationRule=await locationRules.ReviewSource(c.Context.Access,view.LocationRule,true,ct);
            if(locationRule.Missing!.Any(x=>x!="LOCATION_RULE_REVIEW_REQUIRED"))
                throw Error("LOCATION_RULE_SOURCE_INCOMPLETE",422);
            var policy=await RequirePolicy(c.Context,view.Policy,ct);
            foreach(var requirement in policy.Fact.Body.Requirements)
                if(!source.Decision.SatisfiedRequirementIds.Contains("location-rule.requirement/"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(requirement))))
                    throw Error("LOCATION_RULE_PROFESSIONAL_REVIEW_INCOMPLETE",422);
            c.Context.Access.AtCommit(async t=>{
                var current=c.Context with {Policies=await c.Context.Store.Policies(c.Submission.LocationRule,t)};
                await RequirePolicy(current,view.Policy,t);
            });
        }
    }
    public Task<LocationRuleApplicationResultV1> ApplyAsync(Guid locationRule,string? etag,LocationRuleApplicationCommandV1 command,CancellationToken ct=default)
    {
        if(locationRule==Guid.Empty || command.OperationId==Guid.Empty || command.SubmissionId==Guid.Empty)throw Error("LOCATION_RULE_APPLICATION_COMMAND_INVALID",400);
        LocationRuleReviewValuesV1.Text(command.Reason);
        if(string.IsNullOrWhiteSpace(command.Reason))throw Error("LOCATION_RULE_APPLICATION_REASON_REQUIRED",400);
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract=LocationRuleReviewApplicationStoreV1.Contract,environment=Environment,locationRule,etag,command});
        return Authority.ExecuteAsync(OperationsOwnerV1.LocationRule,"review-bind",true,async(a,t)=>{
            var c=await ApplicationContextAsync(a,locationRule,command.SubmissionId,original==null,t);
            if(original!=null)return await DiscloseApplication(c,command.OperationId,original,bytes,t);
            if(string.IsNullOrEmpty(etag))throw Error("IF_MATCH_REQUIRED",428);
            if(etag!=c.Etag || c.Receipt?.ReceiptId!=command.ExpectedReceiptId)throw Error("LOCATION_RULE_APPLICATION_CONTEXT_CHANGED",412);
            var source=c.Source??throw Error("LOCATION_RULE_DECISION_NOT_RECEIVED",422);
            ApprovalApplicationReceiptV2 receipt;
            if(c.Receipt is {Application:"APPLIED" or "STALE"})receipt=c.Receipt;
            else if(c.Context.LocationRule.State!="SUBMITTED" || c.Context.LocationRule.ActiveSubmissionId!=c.Submission.Id || c.Context.LocationRule.Reference!=c.Context.Store.View(c.Submission).LocationRule)
                receipt=await c.Store.Append(source,a.ActorId,command.OperationId,c.Receipt,"STALE","RULE_REVIEW_ROUND_NOT_CURRENT",
                    c.Context.LocationRule.Reference.Version,new{command,state=c.Context.LocationRule.State,source.OriginalHash},null,t);
            else
            {
                RequireActive(c.Context.LocationRule,c.Submission.Id);
                await QualifyApplication(c,t);
                var view=c.Context.Store.View(c.Submission);
                var applied=new LocationRuleApprovalV1(Guid.NewGuid(),view.LocationRule,view.Policy,source.Decision.SubmissionId,
                    source.Decision.DecisionId,source.Decision.Decision=="APPROVED",a.ActorId,Now());
                var policy=c.Context.Policies.Single(p=>p.Reference==view.Policy);
                var next=applied.Approved?"APPROVED_PENDING":policy.Fact.Body.RejectionDisposition;
                await Transition(c.Context.LocationRule,command.OperationId,a.ActorId,next,c.Submission.Id,new{command,etag},t);
                receipt=await c.Store.Append(source,a.ActorId,command.OperationId,c.Receipt,"APPLIED",
                    applied.Approved?"LOCATION_RULE_APPROVED":"LOCATION_RULE_REJECTED",c.Context.LocationRule.Reference.Version,
                    new{profile="Main.Wms.LocationRuleApplication/1",command,applied,authorities=Witnesses(a),source.OriginalHash,source.EvidenceMode},applied,t);
            }
            var result=new LocationRuleApplicationResultV1(command.OperationId,locationRule,receipt);
            await journal.Store(LocationRuleReviewApplicationStoreV1.Journal,LocationRuleReviewApplicationStoreV1.Contract,command.OperationId,
                a.ActorId,bytes,result,[locationRule],new{command,receipt.ReceiptId,receipt.PayloadDigest,source.OriginalHash},t);
            a.AtCommit(async token=>{
                await c.Store.Ready(token);
                if((await c.Store.ForSubmission(command.SubmissionId,token))?.OriginalHash!=source.OriginalHash
                    || (await c.Store.Latest(source.Stream,token))?.ReceiptId!=receipt.ReceiptId)
                    throw Error("LOCATION_RULE_APPLICATION_CONTEXT_CHANGED",412);
            });
            return result;
        },ct,actionAtFence:async t=>{
            original=await journal.Read(LocationRuleReviewApplicationStoreV1.Journal,command.OperationId,t);return original==null?"review-bind":"query";
        });
    }
    private async Task<LocationRuleApplicationResultV1> DiscloseApplication(ApplicationContext c,Guid operation,
        MainOwnerOperationJournalV1.Original original,byte[]? bytes,CancellationToken ct)
    {
        if(original.Actor!=c.Context.Access.ActorId || original.Contract!=LocationRuleReviewApplicationStoreV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null && !MainOwnerOperationJournalV1.Matches(original,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<LocationRuleApplicationResultV1>(original.Result);
        if(original.State!="COMMITTED" || result==null || result.OperationId!=operation || result.ObjectId!=c.Submission.LocationRule
            || !original.Objects.SequenceEqual(new[]{result.ObjectId}) || c.Submission.Id!=result.Receipt.SubmissionId
            || Json(await c.Store.Exact(result.Receipt.ReceiptId,ct))!=Json(result.Receipt))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        return result;
    }
    public Task<LocationRuleApplicationOperationV1> ApplicationOperationAsync(Guid operation,bool close,CancellationToken ct=default)
    {
        if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.LocationRule,"query",close,async(a,t)=>{
            a.RequireLocationRuleFields(false);var journal=new MainOwnerOperationJournalV1(db);
            var original=await journal.Read(LocationRuleReviewApplicationStoreV1.Journal,operation,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=LocationRuleReviewApplicationStoreV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new LocationRuleApplicationOperationV1(operation,"CLOSED");
                var result=JsonSerializer.Deserialize<LocationRuleApplicationResultV1>(original.Result)??throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
                var c=await ApplicationContextAsync(a,result.ObjectId,result.Receipt.SubmissionId,false,t);
                return new LocationRuleApplicationOperationV1(operation,"COMMITTED",await DiscloseApplication(c,operation,original,null,t));
            }
            if(!close)return new LocationRuleApplicationOperationV1(operation,"UNKNOWN");
            var closed=new LocationRuleApplicationOperationV1(operation,"CLOSED");
            await journal.Store(LocationRuleReviewApplicationStoreV1.Journal,LocationRuleReviewApplicationStoreV1.Contract,operation,a.ActorId,null,closed,[],
                new{action="close-uncommitted-location-rule-application",a.ActorId},t);
            var saved=await journal.Read(LocationRuleReviewApplicationStoreV1.Journal,operation,t);
            if(saved==null || saved.State!="CLOSED" || saved.Actor!=a.ActorId || saved.Contract!=LocationRuleReviewApplicationStoreV1.Contract)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
            return closed;
        },ct,readOnlyAuthorityFence:!close);
    }
}
