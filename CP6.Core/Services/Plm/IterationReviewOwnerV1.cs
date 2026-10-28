using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.IterationReviewStoreV1;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;

public sealed partial class IterationReviewOwnerV1(CP6Context db,OperationsOwnerV1 owner,ScopeAuthorizationFenceV1 scope,
    EngineeringIterationService iterations,IterationCandidateService candidates)
{
    private OperationsOwnerV1 Authority=>owner.Owns(db) && iterations.Owns(db) && candidates.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private Guid Environment=>scope.EnvironmentId;
    private static DateTimeOffset Now()=>DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    private static string Json(object value)=>IterationReviewStoreV1.Json(value);
    private static void Text(string value,bool required=true,int max=8000)
    {EngineeringIterationValuesV1.Text(value,required,max);IterationReviewCanonicalV1.Text(value);}
    private sealed record Context(OperationsAccessV1 Access,IterationReviewStoreV1 Store,EngineeringIterationSnapshotV1 Iteration,
        IterationCandidateSnapshotV1[] Candidates,IterationReviewPolicyViewV1[] Policies,SubmissionRow[] Submissions)
    {
        internal string Etag=>"\"ir1-"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new{Iteration.Reference,
            candidates=Candidates.Select(c=>c.Reference),policies=Policies.Select(p=>p.Reference),submissions=Submissions.Select(s=>s.Digest)}))+"\"";
    }
    private static IterationReviewAuthorityV1[] Witnesses(OperationsAccessV1 a)=>a.Authorities.DistinctBy(x=>(x.Resource,x.Action,x.Actor.UserId))
        .OrderBy(x=>x.Resource,StringComparer.Ordinal).ThenBy(x=>x.Action,StringComparer.Ordinal)
        .Select(x=>new IterationReviewAuthorityV1(x.Resource,x.Action,x.Actor.UserId,x.Source.SourcePermissionVersion,x.OrganizationVersion)).ToArray();
    private async Task<Context> Prepare(OperationsAccessV1 a,Guid id,bool write,CancellationToken ct)
    {
        await scope.Root(ct);
        if(write)
        {
            var lease=await scope.AcquireAsync(ct);
            if(!ReferenceEquals(lease.Database,db) || lease.Transaction!=db.Database.CurrentTransaction?.TransactionId)throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
            a.AtCommit(t=>scope.ValidateAsync(lease,t));
        }
        var parent=new EngineeringIterationStoreV1(db);await parent.Fence(write,ct);
        var iteration=await iterations.ReviewSource(a,id,false,ct);
        var all=await candidates.ReviewCandidates(a,id,null,false,true,ct);
        var store=new IterationReviewStoreV1(db,Environment);await store.Fence(write,ct);
        return new(a,store,iteration,all,await store.Policies(id,ct),await store.Submissions(id,ct));
    }
    private static void Match(Context c,string? etag)
    {if(string.IsNullOrEmpty(etag))throw Error("IF_MATCH_REQUIRED",428);if(etag!=c.Etag)throw Error("ITERATION_REVIEW_CONTEXT_CHANGED",412);}
    public Task<IterationReviewContextV1> ContextAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",false,async(a,t)=>{
        var c=await Prepare(a,id,false,t);var submissions=new List<IterationReviewSubmissionV1>();
        foreach(var row in c.Submissions){await DisclosePackage(a,row,false,t);submissions.Add(await View(c.Store,row,t));}
        return new IterationReviewContextV1(id,c.Etag,c.Iteration,c.Candidates,c.Policies,submissions.ToArray());
    },ct);
    public sealed record RoleChoice(int RoleId,string RoleName);
    public Task<RoleChoice[]> RolesAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"review-policy",false,async(a,t)=>{
        a.RequireEngineeringIterationFields(false);return await db.Sys_Roles.AsNoTracking().Where(r=>r.Enable).OrderBy(r=>r.RoleName).ThenBy(r=>r.RoleId).Select(r=>new RoleChoice(r.RoleId,r.RoleName)).ToArrayAsync(t);
    },ct,readOnlyAuthorityFence:true);
    public Task<IterationMemberChoiceV1[]> ReviewersAsync(Guid id,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",false,async(a,t)=>{
        var i=await iterations.ReviewSource(a,id,false,t);var members=await iterations.ReviewMembers(a,i,t);
        return await db.Sys_Users.AsNoTracking().Where(u=>u.Enable && members.Contains(u.Id)).OrderBy(u=>u.UserName).ThenBy(u=>u.Id).Select(u=>new IterationMemberChoiceV1(u.Id,u.UserName)).ToArrayAsync(t);
    },ct);
    private async Task Roles(IterationReviewRequirementV1[] requirements,CancellationToken ct)
    {
        var ids=requirements.Select(r=>r.RoleId).Distinct().Order().ToArray();
        var found=await db.Sys_Roles.AsNoTracking().Where(r=>r.Enable && ids.Contains(r.RoleId)).Select(r=>r.RoleId).ToArrayAsync(ct);
        if(!ids.SequenceEqual(found.Order()))throw Error("ITERATION_REVIEW_ROLE_UNAVAILABLE",422);
    }
    private async Task<IterationReviewPolicyViewV1> Policy(Context c,EngineeringSourceRefV1 reference,CancellationToken ct)
    {
        var p=c.Policies.LastOrDefault();var now=Now();
        if(p==null || p.Reference!=reference || p.Fact.Body.State!="ENABLED" || now<p.Fact.Body.EffectiveFrom || now>=p.Fact.Body.EffectiveTo)throw Error("ITERATION_REVIEW_POLICY_UNAVAILABLE",422);
        Task<OperationsOwnerV1.Authority> Current(string r,string action,CancellationToken t)=>Authority.IterationReviewAuthorityAsync(r,action,p.Fact.ActorId,t);
        var source=await Current(OperationsOwnerV1.EngineeringIteration,"review-policy",ct);
        var access=new OperationsAccessV1(db,owner,source,storedSubjectSource:Current);
        await access.RequireEngineeringIteration(c.Iteration.Reference.Id,ct);access.RequireEngineeringIterationFields(false);
        await Roles(p.Fact.Body.Requirements,ct);await ValidateParticipant(access,p.Fact.ActorId,ct);return p;
    }
    private Task<IterationReviewResultV1> Write<T>(Guid id,string? etag,Guid operation,string action,T command,
        Func<Context,CancellationToken,Task<IterationReviewResultV1>> change,CancellationToken ct)
    {
        if(id==Guid.Empty || operation==Guid.Empty)throw Error("ITERATION_REVIEW_COMMAND_INVALID",400);
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract=Contract,environment=Environment,id,etag,action,command});
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,action,true,async(a,t)=>{
            var c=await Prepare(a,id,original==null,t);if(original!=null)return await Disclose(c,operation,original,bytes,t);
            var result=await change(c,t);await journal.Store(Journal,Contract,operation,a.ActorId,bytes,result,[id],new{action,a.ActorId,command,result.Reference},t);return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(Journal,operation,t);return original==null?action:"query";});
    }
    public Task<IterationReviewResultV1> PolicyAsync(Guid id,string? etag,IterationReviewPolicyCommandV1 input,CancellationToken ct=default)
    {
        if(input==null || input.Requirements==null)throw Error("ITERATION_REVIEW_POLICY_INVALID",400);
        Text(input.Reason);foreach(var r in input.Requirements){if(r==null)throw Error("ITERATION_REVIEW_POLICY_INVALID",400);Text(r.Code,true,100);Text(r.Description);}
        var p=OperationsOwnerV1.Freeze(input);Text(p.Reason);
        if(p.State is not("ENABLED" or "REVOKED") || p.Requirements==null || p.Requirements.Length is <1 or >100
            || !p.SeparateAuthorAndReviewer || p.RejectDisposition is not("RETURN" or "REJECT") || p.EffectiveTo<=p.EffectiveFrom
            || p.EffectiveFrom.Ticks%TimeSpan.TicksPerMillisecond!=0 || p.EffectiveTo.Ticks%TimeSpan.TicksPerMillisecond!=0)throw Error("ITERATION_REVIEW_POLICY_INVALID",400);
        foreach(var r in p.Requirements){if(r==null || r.MinimumApprovals is <1 or >1000)throw Error("ITERATION_REVIEW_POLICY_INVALID",400);Text(r.Code,true,100);Text(r.Description);}
        if(p.Requirements.Select(r=>r.Code).Distinct(StringComparer.Ordinal).Count()!=p.Requirements.Length)throw Error("ITERATION_REVIEW_POLICY_INVALID",400);
        return Write(id,etag,p.OperationId,"review-policy",p,async(c,t)=>{
            Match(c,etag);if(p.State=="ENABLED"){await Roles(p.Requirements,t);c.Access.AtCommit(token=>Roles(p.Requirements,token));}
            var fact=await c.Store.AddPolicy(new(Environment,db.CurrentTenantId,id,Guid.NewGuid(),c.Access.ActorId,Now(),Witnesses(c.Access),p),t);
            return new(p.OperationId,id,"POLICY",fact.Reference);
        },ct);
    }
    private async Task<IterationReviewRegistrationV1> Registration(CancellationToken ct)
    {
        var r=await db.Database.SqlQuery<IterationReviewRegistrationV1>($"""
            SELECT environment_id AS "EnvironmentId",tenant_id AS "TenantId",owner AS "Owner",biz_type AS "BizType",subject_kind AS "SubjectKind",
              revision AS "Revision",snapshot_schema AS "SnapshotSchema",service_principal AS "Principal",audience AS "Audience",fixed_route AS "Route",evidence_mode AS "EvidenceMode",enabled AS "Enabled"
            FROM cp6_approval_binding_v2.registrations WHERE environment_id={Environment} AND tenant_id={db.CurrentTenantId}
              AND owner={IterationReviewStoreV1.Owner} AND biz_type={BizType} AND subject_kind={SubjectKind} FOR SHARE
            """).SingleOrDefaultAsync(ct);
        if(r==null || !r.Enabled || r.Principal==Guid.Empty || r.EvidenceMode!=iterations.EvidenceMode || r.Audience!="CP6.Services"
            || r.SnapshotSchema!=IterationReviewCanonicalV1.SnapshotSchema || r.Route!="/internal/plm/iteration-review-results/v1")throw Error("ITERATION_REVIEW_REGISTRATION_UNAVAILABLE",422);
        return r;
    }
    private static Guid[] Separation(Context c)=>c.Candidates.SelectMany(x=>new[]{x.Original.CreatorId,x.Original.ActorId,x.Original.Definition.TechnicalSource.ActorId})
        .Concat([c.Iteration.AuthorId,c.Iteration.Body.Iteration!.Plan.OwnerId,c.Access.ActorId]).Distinct().Order().ToArray();
    private async Task Reviewers(Context c,Guid[] reviewers,IterationReviewPolicyViewV1 policy,CancellationToken ct)
    {
        var members=await iterations.ReviewMembers(c.Access,c.Iteration,ct);var separated=Separation(c);var roles=new Dictionary<Guid,int[]>();
        if(reviewers.Any(r=>!members.Contains(r) || separated.Contains(r)))throw Error("ITERATION_REVIEW_SEPARATION_REQUIRED",403);
        foreach(var id in reviewers)
        {
            Task<OperationsOwnerV1.Authority> Current(string r,string action,CancellationToken t)=>Authority.IterationReviewAuthorityAsync(r,action,id,t);
            var authority=await Current(OperationsOwnerV1.EngineeringIteration,"review-decide",ct);
            var access=new OperationsAccessV1(db,owner,authority,storedSubjectSource:Current);
            await iterations.ReviewSource(access,c.Iteration.Reference.Id,false,ct);
            await ValidateParticipant(access,id,ct);roles[id]=authority.Actor.RoleIds.ToArray();
        }
        foreach(var r in policy.Fact.Body.Requirements)if(roles.Count(x=>x.Value.Contains(r.RoleId))<r.MinimumApprovals)throw Error("ITERATION_REVIEW_REVIEWERS_INCOMPLETE",422);
    }
    public Task<IterationReviewResultV1> SubmitAsync(Guid id,string? etag,IterationReviewSubmitV1 input,CancellationToken ct=default)
    {
        if(input==null)throw Error("ITERATION_REVIEW_COMMAND_INVALID",400);Text(input.Reason,true,1000);Text(input.ReviewBasis);
        var command=OperationsOwnerV1.Freeze(input);Text(command.Reason,true,1000);Text(command.ReviewBasis);
        if(command.Reason.EnumerateRunes().Count()>500)throw Error("ITERATION_REVIEW_REASON_TOO_LONG",400);
        IterationCandidateValuesV1.Reference(command.Iteration,EngineeringIterationValuesV1.Owner);
        IterationCandidateValuesV1.Reference(command.Policy,PolicyOwner);
        if(command.Iteration.Id!=id || command.IterationEtag==Guid.Empty || command.Candidates==null || command.Candidates.Length==0
            || command.Reviewers==null || command.Reviewers.Length is <1 or >1000 || command.Reviewers.Contains(Guid.Empty)
            || command.Reviewers.Distinct().Count()!=command.Reviewers.Length)throw Error("ITERATION_REVIEW_COMMAND_INVALID",400);
        Revision(command.InputVersion);
        return Write(id,etag,command.OperationId,"review-submit",command,async(c,t)=>{
            Match(c,etag);
            if(c.Iteration.Reference!=command.Iteration || c.Iteration.Etag!=command.IterationEtag || c.Iteration.Body.Iteration!.InputVersion!=command.InputVersion)throw Error("ITERATION_VERSION_CHANGED",412);
            if(c.Iteration.Body.Iteration!.Stage!="IN_DESIGN" || c.Iteration.Body.Iteration.CurrentReview!=null || c.Submissions.Length>0)throw Error("ITERATION_REVIEW_ALREADY_SUBMITTED",409);
            var exact=await candidates.ReviewCandidates(c.Access,id,command.Candidates,true,true,t);c=c with{Candidates=exact};
            var policy=await Policy(c,command.Policy,t);var registration=await Registration(t);await Reviewers(c,command.Reviewers,policy,t);
            var round=Guid.NewGuid();var submission=Guid.NewGuid();var aggregate=Guid.NewGuid();var binding=Guid.NewGuid();var outbox=Guid.NewGuid();
            var package=new IterationReviewPackageV1(IterationReviewCanonicalV1.PackageSchema,new(Environment.ToString(),db.CurrentTenantId),round,
                c.Iteration.Reference,c.Iteration.OriginalJson,policy.Reference,command.Reviewers.Order().ToArray(),command.ReviewBasis,exact,[]);
            var packageBytes=IterationReviewCanonicalV1.Bytes(package);if(packageBytes.Length>16777216)throw Error("ITERATION_REVIEW_PACKAGE_TOO_LARGE",422);
            var snapshot=IterationReviewMappingV1.Snapshot(package,packageBytes,policy,registration,aggregate,binding,submission);
            var snapshotBytes=IterationReviewCanonicalV1.Bytes(snapshot);if(snapshotBytes.Length>262144)throw Error("ITERATION_REVIEW_OA_SNAPSHOT_TOO_LARGE",422);
            var separated=Separation(c);if(separated.Length>1000)throw Error("ITERATION_REVIEW_OA_ID_LIMIT",422);
            var digest=IterationReviewCanonicalV1.Digest(IterationReviewCanonicalV1.SnapshotSchema,snapshotBytes);
            var subject=new ApprovalSubjectV2(IterationReviewStoreV1.Owner,BizType,SubjectKind,round.ToString("D"),binding.ToString("D"),digest);
            var selected=new ApprovalPolicyV2(new(PolicyOwner,policy.Reference.Id.ToString("D"),"1"),"1",snapshot.Binding.PolicyDigest);
            var packageRef=new EngineeringSourceRefV1(PackageOwner,submission,"1",snapshot.Binding.ReviewPackageDigest.Value);
            var frozen=await iterations.StartReview(c.Access,c.Iteration,command.OperationId,new(round,binding,submission,packageRef),command.Reason,t);
            var envelope=new ApprovalSubmitCommandV2("oa.app/2.0",package.Scope,command.OperationId,submission,subject,selected,round.ToString("D"),null,
                ApprovalBindingTimelineBuilderV2.Version(Revision(frozen.Reference.Version)),c.Access.ActorId,new(AuthorizationOwner,submission.ToString("D"),"1"),separated,
                new(IterationReviewCanonicalV1.SnapshotSchema,Convert.ToBase64String(snapshotBytes),digest,[new(PackageOwner,submission.ToString("D"),"1"),selected.Ref]),
                IterationReviewMappingV1.Engineering(snapshot),command.Reason,outbox,Now());
            var parsed=ApprovalSubmissionWireV2.Parse(ApprovalCanonicalV2.Bytes(envelope));
            var row=new SubmissionRow(submission,id,Revision(c.Iteration.Reference.Version),Revision(frozen.Reference.Version),policy.Reference.Id,c.Access.ActorId,
                command.OperationId,round,aggregate,binding,parsed.Intent,parsed.Canonical,parsed.Full,Json(new{actorId=c.Access.ActorId,authorities=Witnesses(c.Access)}),packageBytes,packageRef.Digest,Json(registration));
            await c.Store.Add(row,t);
            c.Access.AtCommit(async token=>{
                await c.Access.EngineeringFinalObservation(async(observation,t)=>{
                    await candidates.ReviewCandidates(observation,id,command.Candidates,true,true,t);
                    var current=c with{Access=observation,Policies=await c.Store.Policies(id,t)};
                    await Policy(current,command.Policy,t);await Reviewers(current,command.Reviewers,policy,t);return true;
                },token);
                if(await Registration(token)!=registration)throw Error("ITERATION_REVIEW_REGISTRATION_CHANGED",412);
            });
            return new(command.OperationId,id,"SUBMIT",new(SubmissionOwner,submission,"1",row.Digest),await c.Store.View(row,t));
        },ct);
    }
    private async Task<IterationReviewSubmissionV1> View(IterationReviewStoreV1 store,SubmissionRow row,CancellationToken ct)
    {
        var value=await store.View(row,ct);var delivery=await ToolApprovalDeliveryV1.ReadAsync(db,Environment,row.Id,ct,IterationReviewStoreV1.Owner);
        return value with{DispatchState=delivery?.State??"PENDING"};
    }
    private async Task DisclosePackage(OperationsAccessV1 a,SubmissionRow row,bool hold,CancellationToken ct)
    {
        var package=ReadPackage(row);await iterations.CandidateHistory(a,package.Iteration,ct);
        var values=await candidates.ReviewCandidates(a,row.Iteration,package.Candidates.Select(x=>x.Reference).ToArray(),hold,hold,ct);
        if(!values.Select(x=>x.OriginalJson).SequenceEqual(package.Candidates.Select(x=>x.OriginalJson)))throw Error("ITERATION_REVIEW_PACKAGE_UNAVAILABLE",503);
    }
    public Task<IterationReviewDetailV1> SubmissionAsync(Guid id,Guid submission,CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",false,async(a,t)=>{
        var c=await Prepare(a,id,false,t);var row=c.Submissions.SingleOrDefault(s=>s.Id==submission)??throw Error("OBJECT_FORBIDDEN",403);
        await DisclosePackage(a,row,false,t);var e=await c.Store.Envelope(row,t);var bytes=Convert.FromBase64String(e.Snapshot.CanonicalBytesBase64);
        var snapshot=JsonSerializer.Deserialize<IterationReviewSnapshotV1>(bytes,ApprovalBindingOwnerV2.Json)!;
        return new IterationReviewDetailV1(await View(c.Store,row,t),ReadPackage(row),snapshot,Encoding.UTF8.GetString(bytes),Encoding.UTF8.GetString(row.Payload));
    },ct);
    private async Task<IterationReviewResultV1> Disclose(Context c,Guid operation,MainOwnerOperationJournalV1.Original original,byte[]? input,CancellationToken ct)
    {
        if(original.Actor!=c.Access.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null && !MainOwnerOperationJournalV1.Matches(original,input))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var r=JsonSerializer.Deserialize<IterationReviewResultV1>(original.Result);
        if(original.State!="COMMITTED" || r==null || r.OperationId!=operation || r.IterationId!=c.Iteration.Reference.Id || !original.Objects.SequenceEqual(new[]{r.IterationId}))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        if(r.Kind=="POLICY")
        {if(!c.Policies.Any(p=>p.Reference==r.Reference) || r.Submission!=null)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);}
        else if(r.Kind=="SUBMIT")
        {var row=c.Submissions.SingleOrDefault(s=>s.Id==r.Reference.Id);if(row==null || row.Digest!=r.Reference.Digest || Json(await c.Store.View(row,ct))!=Json(r.Submission!))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);await DisclosePackage(c.Access,row,false,ct);}
        else throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);return r;
    }
    public Task<IterationReviewOperationV1> OperationAsync(Guid id,bool close,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringIteration,"query",close,async(a,t)=>{
            await scope.Root(t);a.RequireEngineeringIterationFields(false);var j=new MainOwnerOperationJournalV1(db);var original=await j.Read(Journal,id,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new IterationReviewOperationV1(id,"CLOSED");
                if(original.Objects.Length!=1)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
                return new IterationReviewOperationV1(id,"COMMITTED",await Disclose(await Prepare(a,original.Objects[0],false,t),id,original,null,t));
            }
            if(!close)return new IterationReviewOperationV1(id,"UNKNOWN");var result=new IterationReviewOperationV1(id,"CLOSED");
            await j.Store(Journal,Contract,id,a.ActorId,null,result,[],new{action="close-uncommitted-iteration-review",a.ActorId},t);
            var saved=await j.Read(Journal,id,t);if(saved==null || saved.State!="CLOSED" || saved.Actor!=a.ActorId)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
