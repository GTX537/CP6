using CP6.Core.Services.Plm;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.LocationRuleReviewStoreV1;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Wms;

public sealed partial class LocationRuleReviewOwnerV1(CP6Context db, OperationsOwnerV1 owner,
    ScopeAuthorizationFenceV1 scope, LocationRuleReviewSourceV1 locationRules)
{
    private OperationsOwnerV1 Authority=>owner.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private static string Json(object value)=>LocationRuleReviewStoreV1.Json(value);
    private Guid Environment=>scope.EnvironmentId;
    public sealed record ReviewScope(Guid EnvironmentId,Guid TenantId,Guid ActorId,string EvidenceMode);
    public Task<ReviewScope> ScopeAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.LocationRule,"query",false,
        async(a,t)=>{await scope.Root(t);if(locationRules.EvidenceMode is not("REAL" or "DEMO"))throw Error("LOCATION_RULE_REVIEW_ENVIRONMENT_UNAVAILABLE",503);
            return new ReviewScope(Environment,db.CurrentTenantId,a.ActorId,locationRules.EvidenceMode);},ct,readOnlyAuthorityFence:true);
    private static DateTimeOffset Now()=>DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    private sealed record Context(OperationsAccessV1 Access,LocationRuleReviewStoreV1 Store,
        LocationRuleReviewSourceViewV1 LocationRule,LocationRuleReviewPolicyViewV1[] Policies,SubmissionRow[] Submissions)
    {
        internal string Etag=>"\"dr1-"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new {LocationRule.Reference,LocationRule.Etag,
            Policies=Policies.Select(p=>p.Reference),Submissions=Submissions.Select(s=>s.Digest)}))+"\"";
    }
    private static LocationRuleReviewAuthorityV1[] Witnesses(OperationsAccessV1 a)=>a.Authorities
        .DistinctBy(x=>(x.Resource,x.Action,x.Actor.UserId)).OrderBy(x=>x.Resource,StringComparer.Ordinal).ThenBy(x=>x.Action,StringComparer.Ordinal)
        .Select(x=>new LocationRuleReviewAuthorityV1(x.Resource,x.Action,x.Actor.UserId,x.Source.SourcePermissionVersion,x.OrganizationVersion)).ToArray();
    private async Task<Context> Prepare(OperationsAccessV1 a,Guid id,bool write,CancellationToken ct)
    {
        await scope.Root(ct);
        if(write){var lease=await scope.AcquireAsync(ct);a.AtCommit(t=>scope.ValidateAsync(lease,t));}
        var originals=new LocationRuleReviewOriginalsV1(db);await originals.Fence(false,ct);
        await a.RequireLocationRule(id,ct);
        var current=await originals.Read(id,null,ct);
        var observed=await locationRules.ReviewSource(a,current.Reference,false,ct);
        var store=new LocationRuleReviewStoreV1(db,Environment);await store.Fence(write,ct);
        return new(a,store,observed,await store.Policies(id,ct),await store.Submissions(id,ct));
    }
    public Task<LocationRuleReviewContextV1> ContextAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync(
        OperationsOwnerV1.LocationRule,"query",false,async(a,t)=>{
            var c=await Prepare(a,id,false,t);
            var submissions=new List<LocationRuleReviewSubmissionV1>();
            foreach(var row in c.Submissions)
            {
                submissions.Add(await SubmissionView(c.Store,row,t));
            }
            return new LocationRuleReviewContextV1(db.CurrentTenantId,id,c.Etag,c.LocationRule,c.Policies,submissions.ToArray());
        },ct);
    public sealed record RoleChoice(int RoleId,string RoleName);
    public Task<RoleChoice[]> RolesAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(
        OperationsOwnerV1.LocationRule,"review-policy",false,async(a,t)=>{
            a.RequireLocationRuleFields(false);
            return await db.Sys_Roles.AsNoTracking().Where(x=>x.Enable).OrderBy(x=>x.RoleName).ThenBy(x=>x.RoleId)
                .Select(x=>new RoleChoice(x.RoleId,x.RoleName)).ToArrayAsync(t);
        },ct,readOnlyAuthorityFence:true);
    private static void Match(Context c,string? etag)
    {
        if(string.IsNullOrEmpty(etag))throw Error("IF_MATCH_REQUIRED",428);
        if(etag!=c.Etag)throw Error("LOCATION_RULE_REVIEW_CONTEXT_CHANGED",412);
    }
    private async Task RequireRoles(LocationRuleReviewRequirementV1[] requirements,CancellationToken ct)
    {
        var ids=requirements.Select(x=>x.RoleId).Distinct().ToArray();
        var found=await db.Sys_Roles.AsNoTracking().Where(x=>ids.Contains(x.RoleId)&&x.Enable).Select(x=>x.RoleId).ToArrayAsync(ct);
        if(!ids.Order().SequenceEqual(found.Order()))throw Error("LOCATION_RULE_REVIEW_ROLE_UNAVAILABLE",422);
    }
    private async Task<LocationRuleReviewPolicyViewV1> RequirePolicy(Context c,EngineeringSourceRefV1 reference,CancellationToken ct)
    {
        var p=c.Policies.LastOrDefault();var now=Now();
        if(p==null || p.Reference!=reference || p.Fact.Body.State!="ENABLED" || now<p.Fact.Body.EffectiveFrom || now>=p.Fact.Body.EffectiveTo)
            throw Error("LOCATION_RULE_REVIEW_POLICY_UNAVAILABLE",422);
        var initial=await Authority.LocationRuleReviewAuthorityAsync(OperationsOwnerV1.LocationRule,"review-policy",p.Fact.ActorId,ct);
        Task<OperationsOwnerV1.Authority> Current(string r,string action,CancellationToken token)=>Authority.LocationRuleReviewAuthorityAsync(r,action,p.Fact.ActorId,token);
        var policyAccess=new OperationsAccessV1(db,owner,initial,storedSubjectSource:Current);
        await policyAccess.RequireLocationRule(c.LocationRule.Reference.Id,ct);policyAccess.RequireLocationRuleFields(false);
        await RequireRoles(p.Fact.Body.Requirements,ct);
        return p;
    }
    private Task<LocationRuleReviewResultV1> Write<T>(Guid id,string? etag,Guid operation,string action,T command,
        Func<Context,CancellationToken,Task<LocationRuleReviewResultV1>> change,CancellationToken ct)
    {
        if(id==Guid.Empty || operation==Guid.Empty)throw Error("LOCATION_RULE_REVIEW_COMMAND_INVALID",400);
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract=Contract,environment=Environment,id,etag,action,command});
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.LocationRule,action,true,async(a,t)=>{
            var c=await Prepare(a,id,original==null,t);
            if(original!=null)return await Disclose(c,operation,original,bytes,t);
            a.RequireLocationRuleFields(false);
            var result=await change(c,t);
            await journal.Store(Journal,Contract,operation,a.ActorId,bytes,result,[id],new{action,a.ActorId,command,result.Reference},t);
            return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(Journal,operation,t);return original==null?action:"query";});
    }
    public Task<LocationRuleReviewResultV1> PolicyAsync(Guid id,string? etag,LocationRuleReviewPolicyCommandV1 input,CancellationToken ct=default)
    {
        var p=OperationsOwnerV1.Freeze(input);LocationRuleReviewValuesV1.Text(p.Reason,true);
        if(p.RejectionDisposition is not("RETURNED" or "REJECTED") || p.State is not("ENABLED" or "REVOKED") || p.Requirements==null || p.Requirements.Length is <1 or >100
            || p.ExternalRequirements==null || p.ExternalRequirements.Length>0 || p.EffectiveTo<=p.EffectiveFrom
            || p.EffectiveFrom.Ticks%TimeSpan.TicksPerMillisecond!=0 || p.EffectiveTo.Ticks%TimeSpan.TicksPerMillisecond!=0)
            throw Error("LOCATION_RULE_REVIEW_POLICY_INVALID",400);
        foreach(var r in p.Requirements)
        {
            if(r==null || r.MinimumApprovals is <1 or >1000)throw Error("LOCATION_RULE_REVIEW_POLICY_INVALID",400);
            LocationRuleReviewValuesV1.Text(r.Code,true);LocationRuleReviewValuesV1.Text(r.Description,true);
        }
        if(p.Requirements.Select(r=>r.Code).Distinct(StringComparer.Ordinal).Count()!=p.Requirements.Length)throw Error("LOCATION_RULE_REVIEW_POLICY_INVALID",400);
        foreach(var r in p.ExternalRequirements)LocationRuleReviewValuesV1.Text(r);
        return Write(id,etag,p.OperationId,"review-policy",p,async(c,t)=>{
            Match(c,etag);
            if(p.State=="ENABLED"){await RequireRoles(p.Requirements,t);c.Access.AtCommit(token=>RequireRoles(p.Requirements,token));}
            var fact=await c.Store.AddPolicy(new(Environment,db.CurrentTenantId,id,Guid.NewGuid(),c.Access.ActorId,Now(),Witnesses(c.Access),p),t);
            return new(p.OperationId,id,"POLICY",fact.Reference);
        },ct);
    }
    private async Task<LocationRuleReviewPackageV1> Package(Context c,LocationRuleReviewSubmitV1 input,CancellationToken ct)
    {
        var value=await locationRules.ReviewSource(c.Access,input.LocationRule,true,ct);
        if(!value.IsCurrent)throw Error("LOCATION_RULE_SUBMIT_VERSION_CHANGED",412);
        if(value.Missing!.Any(x=>x!="LOCATION_RULE_REVIEW_REQUIRED"))
            throw Error("LOCATION_RULE_SUBMISSION_INCOMPLETE",422);
        EngineeringSourceRefV1? previous=null;string? priorJson=null;
        if(value.ParentVersion!=null)
        {
            var originals=new LocationRuleReviewOriginalsV1(db);await originals.Fence(false,ct);
            var old=await originals.Read(value.Reference.Id,Revision(value.ParentVersion),ct);
            previous=old.Reference;priorJson=old.OriginalJson;
        }
        // Full immutable originals remain in the location-rule Owner; the fixed pair and its digest prove which difference was reviewed.
        var changes=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{previous=priorJson,current=value.OriginalJson}));
        // Preserve the native original's strings and timestamps across the OA wire codec.
        return new(value.Reference,previous,changes,input.ReviewBasis,input.DeclaredChangeRelations,JsonSerializer.Deserialize<JsonElement>(value.OriginalJson));
    }
    public Task<LocationRuleReviewResultV1> SubmitAsync(Guid id,string? etag,LocationRuleReviewSubmitV1 input,CancellationToken ct=default)
    {
        var command=OperationsOwnerV1.Freeze(input);LocationRuleReviewValuesV1.Text(command.Reason,true);LocationRuleReviewValuesV1.Text(command.ReviewBasis,true);
        LocationRuleReviewValuesV1.Ref(command.LocationRule);
        if(command.LocationRule.Id!=id || command.Policy==null || command.Policy.Owner!=PolicyOwner || command.Policy.Version!="1"
            || command.Policy.Id==Guid.Empty || command.ReviewRoundId==Guid.Empty || command.ReplacesSubmissionId==Guid.Empty
            || command.DeclaredChangeRelations==null || command.DeclaredChangeRelations.Length>100)throw Error("LOCATION_RULE_REVIEW_COMMAND_INVALID",400);
        foreach(var relation in command.DeclaredChangeRelations)LocationRuleReviewValuesV1.Text(relation);
        return Write(id,etag,command.OperationId,"review-submit",command,async(c,t)=>{
            var policy=await RequirePolicy(c,command.Policy,t);var package=await Package(c,command,t);
            var packageBytes=ApprovalCanonicalV2.Bytes(package);var packageDigest=ApprovalCanonicalV2.Hash(packageBytes);
            // Covers the bounded requirement set plus review declarations, including
            // supplementary Unicode escaping. The bounded FLOW snapshot carries references only.
            if(packageBytes.Length>16777216)throw Error("LOCATION_RULE_REVIEW_PACKAGE_TOO_LARGE",400);
            var authorization=Json(new{actorId=c.Access.ActorId,authorities=Witnesses(c.Access)});
            var subject=new ApprovalSubjectV2(LocationRuleReviewStoreV1.Owner,BizType,SubjectKind,id.ToString(),command.LocationRule.Version,
                new("sha256","Main.Wms.LocationRule.raw-json/1",command.LocationRule.Digest));
            var selected=new ApprovalPolicyV2(new(PolicyOwner,command.Policy.Id.ToString(),"1"),"1",new("sha256",ApprovalCanonicalV2.Codec,command.Policy.Digest));
            var round=command.ReviewRoundId.ToString();
            var intent=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{scope=new ApprovalScopeV2(Environment.ToString(),db.CurrentTenantId),subject,policy=selected,roundId=round}));
            var prior=c.Submissions.SingleOrDefault(s=>s.Intent==intent);
            var submission=prior?.Id??Guid.NewGuid();
            var snapshot=ApprovalCanonicalV2.Bytes(new LocationRuleReviewSnapshotV1(package.LocationRule,package.Previous,package.ChangeDigest,
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
            var separated=policy.Fact.Body.SeparateAuthorAndReviewer?new[]{c.LocationRule.AuthorId,c.LocationRule.ActorId,c.Access.ActorId}.Distinct().Order().ToArray():Array.Empty<Guid>();
            var e=new ApprovalSubmitCommandV2("oa.app/2.0",new(Environment.ToString(),db.CurrentTenantId),command.OperationId,submission,subject,selected,
                round,command.ReplacesSubmissionId,ApprovalBindingTimelineBuilderV2.Version(Revision(command.LocationRule.Version)),c.Access.ActorId,
                new(AuthorizationOwner,submission.ToString(),"1"),separated,new(SnapshotSchema,Convert.ToBase64String(snapshot),ApprovalCanonicalV2.Digest(snapshot),
                    [new(LocationRuleReviewStoreV1.Owner,id.ToString(),command.LocationRule.Version),selected.Ref]),null,command.Reason,outbox,Now());
            var payload=ApprovalCanonicalV2.Bytes(e);var row=new SubmissionRow(submission,id,Revision(command.LocationRule.Version),command.Policy.Id,c.Access.ActorId,
                command.OperationId,intent,payload,ApprovalCanonicalV2.Hash(payload),authorization,packageBytes,packageDigest);
            await c.Store.Add(row,t);
            await Transition(c.LocationRule,command.OperationId,c.Access.ActorId,"SUBMITTED",row.Id,new{command,etag},t);
            c.Access.AtCommit(async token=>{
                var originals=new LocationRuleReviewOriginalsV1(db);await originals.Fence(false,token);
                var locationRule=await originals.Read(id,null,token);
                var current=c with {LocationRule=locationRule,Policies=await c.Store.Policies(id,token)};
                await RequirePolicy(current,command.Policy,token);
                if(locationRule.Reference!=command.LocationRule)throw Error("LOCATION_RULE_REVIEW_CONTEXT_CHANGED",412);
            });
            return new(command.OperationId,id,"SUBMIT",new(SubmissionOwner,submission,"1",row.Digest),c.Store.View(row));
        },ct);
    }
    private async Task RequireNewRound(Context c,LocationRuleReviewSubmitV1 input,CancellationToken ct)
    {
        if(c.LocationRule.State is not("DRAFT" or "RETURNED"))throw Error("RULE_PROPOSAL_NOT_SUBMITTABLE",409);
        var same=c.Submissions.Where(s=>s.Revision==Revision(input.LocationRule.Version)).ToArray();
        if(same.Length==0)
        {if(input.ReplacesSubmissionId!=null)throw Error("LOCATION_RULE_REVIEW_REPLACEMENT_INVALID",422);return;}
        var replaced=same.SingleOrDefault(s=>s.Id==input.ReplacesSubmissionId)
            ??throw Error("LOCATION_RULE_REVIEW_ALREADY_SUBMITTED",409);
        if(same.Any(s=>c.Store.Envelope(s).ReplacesSubmissionId==replaced.Id))throw Error("LOCATION_RULE_REVIEW_ALREADY_REPLACED",409);
        var applications=new LocationRuleReviewApplicationStoreV1(db,scope);await applications.Ready(ct);
        foreach(var row in same)
        {
            var actual=await applications.Application(row.Id,ct);
            if(actual==null || actual.Approved)throw Error("LOCATION_RULE_REVIEW_REJECTION_REQUIRED",409);
        }
    }
    private async Task<LocationRuleReviewResultV1> Disclose(Context c,Guid operation,MainOwnerOperationJournalV1.Original original,byte[]? input,CancellationToken ct)
    {
        if(original.Actor!=c.Access.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null && !MainOwnerOperationJournalV1.Matches(original,input))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var r=JsonSerializer.Deserialize<LocationRuleReviewResultV1>(original.Result);
        if(original.State!="COMMITTED" || r==null || r.OperationId!=operation || r.LocationRuleId!=c.LocationRule.Reference.Id
            || !original.Objects.SequenceEqual(new[]{r.LocationRuleId}))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        if(r.Kind=="POLICY")
        {if(!c.Policies.Any(p=>p.Reference==r.Reference) || r.Submission!=null)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);}
        else if(r.Kind=="SUBMIT")
        {
            var row=c.Submissions.SingleOrDefault(s=>s.Id==r.Reference.Id);
            if(row==null || row.Digest!=r.Reference.Digest || Json(c.Store.View(row))!=Json(r.Submission!))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
            await locationRules.ReviewSource(c.Access,c.Store.View(row).LocationRule,false,ct);
        }
        else throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        return r;
    }
    public Task<LocationRuleReviewOperationV1> OperationAsync(Guid operation,bool close,CancellationToken ct=default)
    {
        if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.LocationRule,"query",close,async(a,t)=>{
            a.RequireLocationRuleFields(false);var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(Journal,operation,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new LocationRuleReviewOperationV1(operation,"CLOSED");
                if(original.Objects.Length!=1)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
                return new LocationRuleReviewOperationV1(operation,"COMMITTED",await Disclose(await Prepare(a,original.Objects[0],false,t),operation,original,null,t));
            }
            if(!close)return new LocationRuleReviewOperationV1(operation,"UNKNOWN");
            var result=new LocationRuleReviewOperationV1(operation,"CLOSED");
            await journal.Store(Journal,Contract,operation,a.ActorId,null,result,[],new{action="close-uncommitted-location-rule-review",a.ActorId},t);
            var closed=await journal.Read(Journal,operation,t);
            if(closed==null || closed.State!="CLOSED" || closed.Actor!=a.ActorId || closed.Contract!=Contract)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
            return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
