using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.ControlledDocumentReviewStoreV1;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class ControlledDocumentReviewOwnerV1(CP6Context db, OperationsOwnerV1 owner,
    ScopeAuthorizationFenceV1 scope, ControlledDocumentOwnerV1 documents)
{
    private OperationsOwnerV1 Authority=>owner.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private static string Json(object value)=>ControlledDocumentReviewStoreV1.Json(value);
    private Guid Environment=>scope.EnvironmentId;
    private static DateTimeOffset Now()=>DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    private sealed record Context(OperationsAccessV1 Access,ControlledDocumentReviewStoreV1 Store,
        ControlledDocumentSnapshotV1 Document,DocumentReviewPolicyViewV1[] Policies,SubmissionRow[] Submissions)
    {
        internal string Etag=>"\"dr1-"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new {Document.Reference,
            Policies=Policies.Select(p=>p.Reference),Submissions=Submissions.Select(s=>s.Digest)}))+"\"";
    }
    private static DocumentReviewAuthorityV1[] Witnesses(OperationsAccessV1 a)=>a.Authorities
        .DistinctBy(x=>(x.Resource,x.Action,x.Actor.UserId)).OrderBy(x=>x.Resource,StringComparer.Ordinal).ThenBy(x=>x.Action,StringComparer.Ordinal)
        .Select(x=>new DocumentReviewAuthorityV1(x.Resource,x.Action,x.Actor.UserId,x.Source.SourcePermissionVersion,x.OrganizationVersion)).ToArray();
    private async Task<Context> Prepare(OperationsAccessV1 a,Guid id,bool write,CancellationToken ct)
    {
        await scope.Root(ct);
        if(write){var lease=await scope.AcquireAsync(ct);a.AtCommit(t=>scope.ValidateAsync(lease,t));}
        var originals=new ControlledDocumentStoreV1(db);await originals.Fence(false,ct);
        await a.RequireControlledDocument(id,ct);
        var current=await originals.Read(id,null,ct);
        var observed=await documents.ReviewSource(a,current.Reference,false,ct);
        var store=new ControlledDocumentReviewStoreV1(db,Environment);await store.Fence(write,ct);
        return new(a,store,observed,await store.Policies(id,ct),await store.Submissions(id,ct));
    }
    public Task<DocumentReviewContextV1> ContextAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync(
        OperationsOwnerV1.ControlledDocument,"query",false,async(a,t)=>{
            var c=await Prepare(a,id,false,t);
            var submissions=new List<DocumentReviewSubmissionV1>();
            foreach(var row in c.Submissions)
            {
                submissions.Add(await SubmissionView(c.Store,row,t));
            }
            return new DocumentReviewContextV1(db.CurrentTenantId,id,c.Etag,c.Document,c.Policies,submissions.ToArray());
        },ct);
    public sealed record RoleChoice(int RoleId,string RoleName);
    public Task<RoleChoice[]> RolesAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(
        OperationsOwnerV1.ControlledDocument,"review-policy",false,async(a,t)=>{
            a.RequireDocumentFields(false);
            return await db.Sys_Roles.AsNoTracking().Where(x=>x.Enable).OrderBy(x=>x.RoleName).ThenBy(x=>x.RoleId)
                .Select(x=>new RoleChoice(x.RoleId,x.RoleName)).ToArrayAsync(t);
        },ct,readOnlyAuthorityFence:true);
    private static void Match(Context c,string? etag)
    {
        if(string.IsNullOrEmpty(etag))throw Error("IF_MATCH_REQUIRED",428);
        if(etag!=c.Etag)throw Error("DOCUMENT_REVIEW_CONTEXT_CHANGED",412);
    }
    private async Task RequireRoles(DocumentReviewRequirementV1[] requirements,CancellationToken ct)
    {
        var ids=requirements.Select(x=>x.RoleId).Distinct().ToArray();
        var found=await db.Sys_Roles.AsNoTracking().Where(x=>ids.Contains(x.RoleId)&&x.Enable).Select(x=>x.RoleId).ToArrayAsync(ct);
        if(!ids.Order().SequenceEqual(found.Order()))throw Error("DOCUMENT_REVIEW_ROLE_UNAVAILABLE",422);
    }
    private async Task<DocumentReviewPolicyViewV1> RequirePolicy(Context c,EngineeringSourceRefV1 reference,CancellationToken ct)
    {
        var p=c.Policies.LastOrDefault();var now=Now();
        if(p==null || p.Reference!=reference || p.Fact.Body.State!="ENABLED" || now<p.Fact.Body.EffectiveFrom || now>=p.Fact.Body.EffectiveTo)
            throw Error("DOCUMENT_REVIEW_POLICY_UNAVAILABLE",422);
        var initial=await Authority.DocumentReviewAuthorityAsync(OperationsOwnerV1.ControlledDocument,"review-policy",p.Fact.ActorId,ct);
        Task<OperationsOwnerV1.Authority> Current(string r,string action,CancellationToken token)=>Authority.DocumentReviewAuthorityAsync(r,action,p.Fact.ActorId,token);
        var policyAccess=new OperationsAccessV1(db,owner,initial,storedSubjectSource:Current);
        await policyAccess.RequireControlledDocument(c.Document.Reference.Id,ct);policyAccess.RequireDocumentFields(false);
        await RequireRoles(p.Fact.Body.Requirements,ct);
        return p;
    }
    private Task<DocumentReviewResultV1> Write<T>(Guid id,string? etag,Guid operation,string action,T command,
        Func<Context,CancellationToken,Task<DocumentReviewResultV1>> change,CancellationToken ct)
    {
        if(id==Guid.Empty || operation==Guid.Empty)throw Error("DOCUMENT_REVIEW_COMMAND_INVALID",400);
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract=Contract,environment=Environment,id,etag,action,command});
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.ControlledDocument,action,true,async(a,t)=>{
            var c=await Prepare(a,id,original==null,t);
            if(original!=null)return await Disclose(c,operation,original,bytes,t);
            a.RequireDocumentFields(false);
            var result=await change(c,t);
            await journal.Store(Journal,Contract,operation,a.ActorId,bytes,result,[id],new{action,a.ActorId,command,result.Reference},t);
            return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(Journal,operation,t);return original==null?action:"query";});
    }
    public Task<DocumentReviewResultV1> PolicyAsync(Guid id,string? etag,DocumentReviewPolicyCommandV1 input,CancellationToken ct=default)
    {
        var p=OperationsOwnerV1.Freeze(input);ControlledDocumentValuesV1.Text(p.Reason);
        if(p.State is not("ENABLED" or "REVOKED") || p.Requirements==null || p.Requirements.Length is <1 or >100
            || p.ExternalRequirements==null || p.ExternalRequirements.Length>100 || p.EffectiveTo<=p.EffectiveFrom
            || p.EffectiveFrom.Ticks%TimeSpan.TicksPerMillisecond!=0 || p.EffectiveTo.Ticks%TimeSpan.TicksPerMillisecond!=0)
            throw Error("DOCUMENT_REVIEW_POLICY_INVALID",400);
        foreach(var r in p.Requirements)
        {
            if(r==null || r.MinimumApprovals is <1 or >1000)throw Error("DOCUMENT_REVIEW_POLICY_INVALID",400);
            ControlledDocumentValuesV1.Text(r.Code);ControlledDocumentValuesV1.Text(r.Description);
        }
        if(p.Requirements.Select(r=>r.Code).Distinct(StringComparer.Ordinal).Count()!=p.Requirements.Length)throw Error("DOCUMENT_REVIEW_POLICY_INVALID",400);
        foreach(var r in p.ExternalRequirements)ControlledDocumentValuesV1.Text(r);
        return Write(id,etag,p.OperationId,"review-policy",p,async(c,t)=>{
            Match(c,etag);
            if(p.State=="ENABLED"){await RequireRoles(p.Requirements,t);c.Access.AtCommit(token=>RequireRoles(p.Requirements,token));}
            var fact=await c.Store.AddPolicy(new(Environment,db.CurrentTenantId,id,Guid.NewGuid(),c.Access.ActorId,Now(),Witnesses(c.Access),p),t);
            return new(p.OperationId,id,"POLICY",fact.Reference);
        },ct);
    }
    private async Task<DocumentReviewPackageV1> Package(Context c,DocumentReviewSubmitV1 input,CancellationToken ct)
    {
        var value=await documents.ReviewSource(c.Access,input.Document,true,ct);
        if(!value.IsCurrent)throw Error("DOCUMENT_SUBMIT_VERSION_CHANGED",412);
        if(value.Missing!.Any(x=>x is not("DOCUMENT_PROFESSIONAL_REVIEW_REQUIRED" or "LANGUAGE_AUTHORITY_REVIEW_REQUIRED")))
            throw Error("DOCUMENT_SUBMISSION_INCOMPLETE",422);
        EngineeringSourceRefV1? previous=null;string? priorJson=null;
        if(value.ParentVersion!=null)
        {
            var originals=new ControlledDocumentStoreV1(db);await originals.Fence(false,ct);
            var old=await originals.Read(value.Reference.Id,Revision(value.ParentVersion),ct);
            previous=old.Reference;priorJson=old.OriginalJson;
        }
        // Full immutable originals remain in DOC; the fixed pair and its digest prove which difference was reviewed.
        var changes=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{previous=priorJson,current=value.OriginalJson}));
        return new(value.Reference,previous,changes,input.ReviewBasis,input.DeclaredChangeRelations,value.Body.Source,value.Body.Contents);
    }
    public Task<DocumentReviewResultV1> SubmitAsync(Guid id,string? etag,DocumentReviewSubmitV1 input,CancellationToken ct=default)
    {
        var command=OperationsOwnerV1.Freeze(input);ControlledDocumentValuesV1.Text(command.Reason);ControlledDocumentValuesV1.Text(command.ReviewBasis);
        ControlledDocumentValuesV1.Ref(command.Document);
        if(command.Document.Id!=id || command.Policy==null || command.Policy.Owner!=PolicyOwner || command.Policy.Version!="1"
            || command.Policy.Id==Guid.Empty || command.ReviewRoundId==Guid.Empty || command.ReplacesSubmissionId==Guid.Empty
            || command.DeclaredChangeRelations==null || command.DeclaredChangeRelations.Length>100)throw Error("DOCUMENT_REVIEW_COMMAND_INVALID",400);
        foreach(var relation in command.DeclaredChangeRelations)ControlledDocumentValuesV1.Text(relation);
        return Write(id,etag,command.OperationId,"review-submit",command,async(c,t)=>{
            var policy=await RequirePolicy(c,command.Policy,t);var package=await Package(c,command,t);
            var packageBytes=ApprovalCanonicalV2.Bytes(package);var packageDigest=ApprovalCanonicalV2.Hash(packageBytes);
            // Covers the existing 8 MiB document plus bounded review declarations, including
            // supplementary Unicode escaping. The bounded FLOW snapshot carries references only.
            if(packageBytes.Length>16777216)throw Error("DOCUMENT_REVIEW_PACKAGE_TOO_LARGE",400);
            var authorization=Json(new{actorId=c.Access.ActorId,authorities=Witnesses(c.Access)});
            var subject=new ApprovalSubjectV2(ControlledDocumentReviewStoreV1.Owner,BizType,SubjectKind,id.ToString(),command.Document.Version,
                new("sha256","Main.Plm.ControlledDocument.raw-json/1",command.Document.Digest));
            var selected=new ApprovalPolicyV2(new(PolicyOwner,command.Policy.Id.ToString(),"1"),"1",new("sha256",ApprovalCanonicalV2.Codec,command.Policy.Digest));
            var round=command.ReviewRoundId.ToString();
            var intent=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{scope=new ApprovalScopeV2(Environment.ToString(),db.CurrentTenantId),subject,policy=selected,roundId=round}));
            var prior=c.Submissions.SingleOrDefault(s=>s.Intent==intent);
            var submission=prior?.Id??Guid.NewGuid();
            var snapshot=ApprovalCanonicalV2.Bytes(new DocumentReviewSnapshotV1(package.Document,package.Previous,package.ChangeDigest,
                new(PackageOwner,submission,"1",packageDigest),package.Source.Kind));
            if(prior!=null)
            {
                var old=c.Store.Envelope(prior);
                if(prior.Actor!=c.Access.ActorId || prior.Authorization!=authorization || old.Snapshot.CanonicalBytesBase64!=Convert.ToBase64String(snapshot)
                    || old.ReplacesSubmissionId!=command.ReplacesSubmissionId)throw Error("INTENT_PAYLOAD_CONFLICT",409);
                return new(command.OperationId,id,"SUBMIT",new(SubmissionOwner,prior.Id,"1",prior.Digest),c.Store.View(prior));
            }
            Match(c,etag);
            await RequireNewRound(c,command,t);
            var outbox=Guid.NewGuid();
            var separated=policy.Fact.Body.SeparateAuthorAndReviewer?new[]{c.Document.ActorId,c.Access.ActorId}.Distinct().Order().ToArray():Array.Empty<Guid>();
            var e=new ApprovalSubmitCommandV2("oa.app/2.0",new(Environment.ToString(),db.CurrentTenantId),command.OperationId,submission,subject,selected,
                round,command.ReplacesSubmissionId,ApprovalBindingTimelineBuilderV2.Version(Revision(command.Document.Version)),c.Access.ActorId,
                new(AuthorizationOwner,submission.ToString(),"1"),separated,new(SnapshotSchema,Convert.ToBase64String(snapshot),ApprovalCanonicalV2.Digest(snapshot),
                    [new(ControlledDocumentReviewStoreV1.Owner,id.ToString(),command.Document.Version),selected.Ref]),null,command.Reason,outbox,Now());
            var payload=ApprovalCanonicalV2.Bytes(e);var row=new SubmissionRow(submission,id,Revision(command.Document.Version),command.Policy.Id,c.Access.ActorId,
                command.OperationId,intent,payload,ApprovalCanonicalV2.Hash(payload),authorization,packageBytes,packageDigest);
            await c.Store.Add(row,t);
            c.Access.AtCommit(async token=>{
                var originals=new ControlledDocumentStoreV1(db);await originals.Fence(false,token);
                var document=await originals.Read(id,null,token);
                var current=c with {Document=document,Policies=await c.Store.Policies(id,token)};
                await RequirePolicy(current,command.Policy,token);
                if(document.Reference!=command.Document)throw Error("DOCUMENT_REVIEW_CONTEXT_CHANGED",412);
            });
            return new(command.OperationId,id,"SUBMIT",new(SubmissionOwner,submission,"1",row.Digest),c.Store.View(row));
        },ct);
    }
    private async Task RequireNewRound(Context c,DocumentReviewSubmitV1 input,CancellationToken ct)
    {
        var same=c.Submissions.Where(s=>s.Revision==Revision(input.Document.Version)).ToArray();
        if(same.Length==0)
        {if(input.ReplacesSubmissionId!=null)throw Error("DOCUMENT_REVIEW_REPLACEMENT_INVALID",422);return;}
        var replaced=same.SingleOrDefault(s=>s.Id==input.ReplacesSubmissionId)
            ??throw Error("DOCUMENT_REVIEW_ALREADY_SUBMITTED",409);
        if(same.Any(s=>c.Store.Envelope(s).ReplacesSubmissionId==replaced.Id))throw Error("DOCUMENT_REVIEW_ALREADY_REPLACED",409);
        var applications=new DocumentReviewApplicationStoreV1(db,scope);await applications.Ready(ct);
        foreach(var row in same)
        {
            var actual=await applications.Application(row.Id,ct);
            if(actual==null || actual.Approved)throw Error("DOCUMENT_REVIEW_REJECTION_REQUIRED",409);
        }
    }
    private async Task<DocumentReviewResultV1> Disclose(Context c,Guid operation,MainOwnerOperationJournalV1.Original original,byte[]? input,CancellationToken ct)
    {
        if(original.Actor!=c.Access.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null && !MainOwnerOperationJournalV1.Matches(original,input))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var r=JsonSerializer.Deserialize<DocumentReviewResultV1>(original.Result);
        if(original.State!="COMMITTED" || r==null || r.OperationId!=operation || r.DocumentId!=c.Document.Reference.Id
            || !original.Objects.SequenceEqual(new[]{r.DocumentId}))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        if(r.Kind=="POLICY")
        {if(!c.Policies.Any(p=>p.Reference==r.Reference) || r.Submission!=null)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);}
        else if(r.Kind=="SUBMIT")
        {
            var row=c.Submissions.SingleOrDefault(s=>s.Id==r.Reference.Id);
            if(row==null || row.Digest!=r.Reference.Digest || Json(c.Store.View(row))!=Json(r.Submission!))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
            await documents.ReviewSource(c.Access,c.Store.View(row).Document,false,ct);
        }
        else throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        return r;
    }
    public Task<DocumentReviewOperationV1> OperationAsync(Guid operation,bool close,CancellationToken ct=default)
    {
        if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ControlledDocument,"query",close,async(a,t)=>{
            a.RequireDocumentFields(false);var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(Journal,operation,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new DocumentReviewOperationV1(operation,"CLOSED");
                if(original.Objects.Length!=1)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
                return new DocumentReviewOperationV1(operation,"COMMITTED",await Disclose(await Prepare(a,original.Objects[0],false,t),operation,original,null,t));
            }
            if(!close)return new DocumentReviewOperationV1(operation,"UNKNOWN");
            var result=new DocumentReviewOperationV1(operation,"CLOSED");
            await journal.Store(Journal,Contract,operation,a.ActorId,null,result,[],new{action="close-uncommitted-document-review",a.ActorId},t);return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
