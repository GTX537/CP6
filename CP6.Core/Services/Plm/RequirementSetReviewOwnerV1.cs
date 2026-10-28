using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.RequirementSetReviewStoreV1;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class RequirementSetReviewOwnerV1(CP6Context db, OperationsOwnerV1 owner,
    ScopeAuthorizationFenceV1 scope, RequirementSetService requirementSets)
{
    private OperationsOwnerV1 Authority=>owner.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private static string Json(object value)=>RequirementSetReviewStoreV1.Json(value);
    private Guid Environment=>scope.EnvironmentId;
    private static DateTimeOffset Now()=>DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    private sealed record Context(OperationsAccessV1 Access,RequirementSetReviewStoreV1 Store,
        RequirementSetSnapshotV1 RequirementSet,RequirementSetReviewPolicyViewV1[] Policies,SubmissionRow[] Submissions)
    {
        internal string Etag=>"\"dr1-"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new {RequirementSet.Reference,
            Policies=Policies.Select(p=>p.Reference),Submissions=Submissions.Select(s=>s.Digest)}))+"\"";
    }
    private static RequirementSetReviewAuthorityV1[] Witnesses(OperationsAccessV1 a)=>a.Authorities
        .DistinctBy(x=>(x.Resource,x.Action,x.Actor.UserId)).OrderBy(x=>x.Resource,StringComparer.Ordinal).ThenBy(x=>x.Action,StringComparer.Ordinal)
        .Select(x=>new RequirementSetReviewAuthorityV1(x.Resource,x.Action,x.Actor.UserId,x.Source.SourcePermissionVersion,x.OrganizationVersion)).ToArray();
    private async Task<Context> Prepare(OperationsAccessV1 a,Guid id,bool write,CancellationToken ct)
    {
        await scope.Root(ct);
        if(write){var lease=await scope.AcquireAsync(ct);a.AtCommit(t=>scope.ValidateAsync(lease,t));}
        var originals=new RequirementSetStoreV1(db);await originals.Fence(false,ct);
        await a.RequireRequirementSet(id,ct);
        var current=await originals.Read(id,null,ct);
        var observed=await requirementSets.ReviewSource(a,current.Reference,false,ct);
        var store=new RequirementSetReviewStoreV1(db,Environment);await store.Fence(write,ct);
        return new(a,store,observed,await store.Policies(id,ct),await store.Submissions(id,ct));
    }
    public Task<RequirementSetReviewContextV1> ContextAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync(
        OperationsOwnerV1.RequirementSet,"query",false,async(a,t)=>{
            var c=await Prepare(a,id,false,t);
            var submissions=new List<RequirementSetReviewSubmissionV1>();
            foreach(var row in c.Submissions)
            {
                submissions.Add(await SubmissionView(c.Store,row,t));
            }
            return new RequirementSetReviewContextV1(db.CurrentTenantId,id,c.Etag,c.RequirementSet,c.Policies,submissions.ToArray());
        },ct);
    public sealed record RoleChoice(int RoleId,string RoleName);
    public Task<RoleChoice[]> RolesAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(
        OperationsOwnerV1.RequirementSet,"review-policy",false,async(a,t)=>{
            a.RequireRequirementSetFields(false);
            return await db.Sys_Roles.AsNoTracking().Where(x=>x.Enable).OrderBy(x=>x.RoleName).ThenBy(x=>x.RoleId)
                .Select(x=>new RoleChoice(x.RoleId,x.RoleName)).ToArrayAsync(t);
        },ct,readOnlyAuthorityFence:true);
    private static void Match(Context c,string? etag)
    {
        if(string.IsNullOrEmpty(etag))throw Error("IF_MATCH_REQUIRED",428);
        if(etag!=c.Etag)throw Error("REQUIREMENT_SET_REVIEW_CONTEXT_CHANGED",412);
    }
    private async Task RequireRoles(RequirementSetReviewRequirementV1[] requirements,CancellationToken ct)
    {
        var ids=requirements.Select(x=>x.RoleId).Distinct().ToArray();
        var found=await db.Sys_Roles.AsNoTracking().Where(x=>ids.Contains(x.RoleId)&&x.Enable).Select(x=>x.RoleId).ToArrayAsync(ct);
        if(!ids.Order().SequenceEqual(found.Order()))throw Error("REQUIREMENT_SET_REVIEW_ROLE_UNAVAILABLE",422);
    }
    private async Task<RequirementSetReviewPolicyViewV1> RequirePolicy(Context c,EngineeringSourceRefV1 reference,CancellationToken ct)
    {
        var p=c.Policies.LastOrDefault();var now=Now();
        if(p==null || p.Reference!=reference || p.Fact.Body.State!="ENABLED" || now<p.Fact.Body.EffectiveFrom || now>=p.Fact.Body.EffectiveTo)
            throw Error("REQUIREMENT_SET_REVIEW_POLICY_UNAVAILABLE",422);
        var initial=await Authority.RequirementSetReviewAuthorityAsync(OperationsOwnerV1.RequirementSet,"review-policy",p.Fact.ActorId,ct);
        Task<OperationsOwnerV1.Authority> Current(string r,string action,CancellationToken token)=>Authority.RequirementSetReviewAuthorityAsync(r,action,p.Fact.ActorId,token);
        var policyAccess=new OperationsAccessV1(db,owner,initial,storedSubjectSource:Current);
        await policyAccess.RequireRequirementSet(c.RequirementSet.Reference.Id,ct);policyAccess.RequireRequirementSetFields(false);
        await RequireRoles(p.Fact.Body.Requirements,ct);
        return p;
    }
    private Task<RequirementSetReviewResultV1> Write<T>(Guid id,string? etag,Guid operation,string action,T command,
        Func<Context,CancellationToken,Task<RequirementSetReviewResultV1>> change,CancellationToken ct)
    {
        if(id==Guid.Empty || operation==Guid.Empty)throw Error("REQUIREMENT_SET_REVIEW_COMMAND_INVALID",400);
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract=Contract,environment=Environment,id,etag,action,command});
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.RequirementSet,action,true,async(a,t)=>{
            var c=await Prepare(a,id,original==null,t);
            if(original!=null)return await Disclose(c,operation,original,bytes,t);
            a.RequireRequirementSetFields(false);
            var result=await change(c,t);
            await journal.Store(Journal,Contract,operation,a.ActorId,bytes,result,[id],new{action,a.ActorId,command,result.Reference},t);
            return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(Journal,operation,t);return original==null?action:"query";});
    }
    public Task<RequirementSetReviewResultV1> PolicyAsync(Guid id,string? etag,RequirementSetReviewPolicyCommandV1 input,CancellationToken ct=default)
    {
        var p=OperationsOwnerV1.Freeze(input);RequirementSetValuesV1.Text(p.Reason,true);
        if(!p.SeparateAuthorAndReviewer || p.State is not("ENABLED" or "REVOKED") || p.Requirements==null || p.Requirements.Length is <1 or >100
            || p.ExternalRequirements==null || p.ExternalRequirements.Length>0 || p.EffectiveTo<=p.EffectiveFrom
            || p.EffectiveFrom.Ticks%TimeSpan.TicksPerMillisecond!=0 || p.EffectiveTo.Ticks%TimeSpan.TicksPerMillisecond!=0)
            throw Error("REQUIREMENT_SET_REVIEW_POLICY_INVALID",400);
        foreach(var r in p.Requirements)
        {
            if(r==null || r.MinimumApprovals is <1 or >1000)throw Error("REQUIREMENT_SET_REVIEW_POLICY_INVALID",400);
            RequirementSetValuesV1.Text(r.Code,true);RequirementSetValuesV1.Text(r.Description,true);
        }
        if(p.Requirements.Select(r=>r.Code).Distinct(StringComparer.Ordinal).Count()!=p.Requirements.Length)throw Error("REQUIREMENT_SET_REVIEW_POLICY_INVALID",400);
        foreach(var r in p.ExternalRequirements)RequirementSetValuesV1.Text(r);
        return Write(id,etag,p.OperationId,"review-policy",p,async(c,t)=>{
            Match(c,etag);
            if(p.State=="ENABLED"){await RequireRoles(p.Requirements,t);c.Access.AtCommit(token=>RequireRoles(p.Requirements,token));}
            var fact=await c.Store.AddPolicy(new(Environment,db.CurrentTenantId,id,Guid.NewGuid(),c.Access.ActorId,Now(),Witnesses(c.Access),p),t);
            return new(p.OperationId,id,"POLICY",fact.Reference);
        },ct);
    }
    private async Task<RequirementSetReviewPackageV1> Package(Context c,RequirementSetReviewSubmitV1 input,CancellationToken ct)
    {
        var value=await requirementSets.ReviewSource(c.Access,input.RequirementSet,true,ct);
        if(!value.IsCurrent)throw Error("REQUIREMENT_SET_SUBMIT_VERSION_CHANGED",412);
        if(value.Missing!.Any(x=>x!="REQUIREMENT_SET_REVIEW_REQUIRED"))
            throw Error("REQUIREMENT_SET_SUBMISSION_INCOMPLETE",422);
        EngineeringSourceRefV1? previous=null;string? priorJson=null;
        if(value.ParentVersion!=null)
        {
            var originals=new RequirementSetStoreV1(db);await originals.Fence(false,ct);
            var old=await originals.Read(value.Reference.Id,Revision(value.ParentVersion),ct);
            previous=old.Reference;priorJson=old.OriginalJson;
        }
        // Full immutable originals remain in the requirement-set Owner; the fixed pair and its digest prove which difference was reviewed.
        var changes=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{previous=priorJson,current=value.OriginalJson}));
        return new(value.Reference,previous,changes,input.ReviewBasis,input.DeclaredChangeRelations,value.Body);
    }
    public Task<RequirementSetReviewResultV1> SubmitAsync(Guid id,string? etag,RequirementSetReviewSubmitV1 input,CancellationToken ct=default)
    {
        var command=OperationsOwnerV1.Freeze(input);RequirementSetValuesV1.Text(command.Reason,true);RequirementSetValuesV1.Text(command.ReviewBasis,true);
        RequirementSetValuesV1.Ref(command.RequirementSet);
        if(command.RequirementSet.Id!=id || command.Policy==null || command.Policy.Owner!=PolicyOwner || command.Policy.Version!="1"
            || command.Policy.Id==Guid.Empty || command.ReviewRoundId==Guid.Empty || command.ReplacesSubmissionId==Guid.Empty
            || command.DeclaredChangeRelations==null || command.DeclaredChangeRelations.Length>100)throw Error("REQUIREMENT_SET_REVIEW_COMMAND_INVALID",400);
        foreach(var relation in command.DeclaredChangeRelations)RequirementSetValuesV1.Text(relation);
        return Write(id,etag,command.OperationId,"review-submit",command,async(c,t)=>{
            var policy=await RequirePolicy(c,command.Policy,t);var package=await Package(c,command,t);
            var packageBytes=ApprovalCanonicalV2.Bytes(package);var packageDigest=ApprovalCanonicalV2.Hash(packageBytes);
            // Covers the bounded requirement set plus review declarations, including
            // supplementary Unicode escaping. The bounded FLOW snapshot carries references only.
            if(packageBytes.Length>16777216)throw Error("REQUIREMENT_SET_REVIEW_PACKAGE_TOO_LARGE",400);
            var authorization=Json(new{actorId=c.Access.ActorId,authorities=Witnesses(c.Access)});
            var subject=new ApprovalSubjectV2(RequirementSetReviewStoreV1.Owner,BizType,SubjectKind,id.ToString(),command.RequirementSet.Version,
                new("sha256","Main.Plm.RequirementSet.raw-json/1",command.RequirementSet.Digest));
            var selected=new ApprovalPolicyV2(new(PolicyOwner,command.Policy.Id.ToString(),"1"),"1",new("sha256",ApprovalCanonicalV2.Codec,command.Policy.Digest));
            var round=command.ReviewRoundId.ToString();
            var intent=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{scope=new ApprovalScopeV2(Environment.ToString(),db.CurrentTenantId),subject,policy=selected,roundId=round}));
            var prior=c.Submissions.SingleOrDefault(s=>s.Intent==intent);
            var submission=prior?.Id??Guid.NewGuid();
            var snapshot=ApprovalCanonicalV2.Bytes(new RequirementSetReviewSnapshotV1(package.RequirementSet,package.Previous,package.ChangeDigest,
                new(PackageOwner,submission,"1",packageDigest)));
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
            var separated=policy.Fact.Body.SeparateAuthorAndReviewer?new[]{c.RequirementSet.AuthorId,c.RequirementSet.ActorId,c.Access.ActorId}.Distinct().Order().ToArray():Array.Empty<Guid>();
            var e=new ApprovalSubmitCommandV2("oa.app/2.0",new(Environment.ToString(),db.CurrentTenantId),command.OperationId,submission,subject,selected,
                round,command.ReplacesSubmissionId,ApprovalBindingTimelineBuilderV2.Version(Revision(command.RequirementSet.Version)),c.Access.ActorId,
                new(AuthorizationOwner,submission.ToString(),"1"),separated,new(SnapshotSchema,Convert.ToBase64String(snapshot),ApprovalCanonicalV2.Digest(snapshot),
                    [new(RequirementSetReviewStoreV1.Owner,id.ToString(),command.RequirementSet.Version),selected.Ref]),null,command.Reason,outbox,Now());
            var payload=ApprovalCanonicalV2.Bytes(e);var row=new SubmissionRow(submission,id,Revision(command.RequirementSet.Version),command.Policy.Id,c.Access.ActorId,
                command.OperationId,intent,payload,ApprovalCanonicalV2.Hash(payload),authorization,packageBytes,packageDigest);
            await c.Store.Add(row,t);
            c.Access.AtCommit(async token=>{
                var originals=new RequirementSetStoreV1(db);await originals.Fence(false,token);
                var requirementSet=await originals.Read(id,null,token);
                var current=c with {RequirementSet=requirementSet,Policies=await c.Store.Policies(id,token)};
                await RequirePolicy(current,command.Policy,token);
                if(requirementSet.Reference!=command.RequirementSet)throw Error("REQUIREMENT_SET_REVIEW_CONTEXT_CHANGED",412);
            });
            return new(command.OperationId,id,"SUBMIT",new(SubmissionOwner,submission,"1",row.Digest),c.Store.View(row));
        },ct);
    }
    private async Task RequireNewRound(Context c,RequirementSetReviewSubmitV1 input,CancellationToken ct)
    {
        var same=c.Submissions.Where(s=>s.Revision==Revision(input.RequirementSet.Version)).ToArray();
        if(same.Length==0)
        {if(input.ReplacesSubmissionId!=null)throw Error("REQUIREMENT_SET_REVIEW_REPLACEMENT_INVALID",422);return;}
        var replaced=same.SingleOrDefault(s=>s.Id==input.ReplacesSubmissionId)
            ??throw Error("REQUIREMENT_SET_REVIEW_ALREADY_SUBMITTED",409);
        if(same.Any(s=>c.Store.Envelope(s).ReplacesSubmissionId==replaced.Id))throw Error("REQUIREMENT_SET_REVIEW_ALREADY_REPLACED",409);
        var applications=new RequirementSetReviewApplicationStoreV1(db,scope);await applications.Ready(ct);
        foreach(var row in same)
        {
            var actual=await applications.Application(row.Id,ct);
            if(actual==null || actual.Approved)throw Error("REQUIREMENT_SET_REVIEW_REJECTION_REQUIRED",409);
        }
    }
    private async Task<RequirementSetReviewResultV1> Disclose(Context c,Guid operation,MainOwnerOperationJournalV1.Original original,byte[]? input,CancellationToken ct)
    {
        if(original.Actor!=c.Access.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null && !MainOwnerOperationJournalV1.Matches(original,input))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var r=JsonSerializer.Deserialize<RequirementSetReviewResultV1>(original.Result);
        if(original.State!="COMMITTED" || r==null || r.OperationId!=operation || r.RequirementSetId!=c.RequirementSet.Reference.Id
            || !original.Objects.SequenceEqual(new[]{r.RequirementSetId}))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        if(r.Kind=="POLICY")
        {if(!c.Policies.Any(p=>p.Reference==r.Reference) || r.Submission!=null)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);}
        else if(r.Kind=="SUBMIT")
        {
            var row=c.Submissions.SingleOrDefault(s=>s.Id==r.Reference.Id);
            if(row==null || row.Digest!=r.Reference.Digest || Json(c.Store.View(row))!=Json(r.Submission!))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
            await requirementSets.ReviewSource(c.Access,c.Store.View(row).RequirementSet,false,ct);
        }
        else throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        return r;
    }
    public Task<RequirementSetReviewOperationV1> OperationAsync(Guid operation,bool close,CancellationToken ct=default)
    {
        if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.RequirementSet,"query",close,async(a,t)=>{
            a.RequireRequirementSetFields(false);var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(Journal,operation,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new RequirementSetReviewOperationV1(operation,"CLOSED");
                if(original.Objects.Length!=1)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
                return new RequirementSetReviewOperationV1(operation,"COMMITTED",await Disclose(await Prepare(a,original.Objects[0],false,t),operation,original,null,t));
            }
            if(!close)return new RequirementSetReviewOperationV1(operation,"UNKNOWN");
            var result=new RequirementSetReviewOperationV1(operation,"CLOSED");
            await journal.Store(Journal,Contract,operation,a.ActorId,null,result,[],new{action="close-uncommitted-requirement-set-review",a.ActorId},t);
            var closed=await journal.Read(Journal,operation,t);
            if(closed==null || closed.State!="CLOSED" || closed.Actor!=a.ActorId || closed.Contract!=Contract)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
            return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
