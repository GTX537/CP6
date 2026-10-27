using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Space;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.ToolDispositionApprovalStoreV1;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;
namespace CP6.Core.Services.Wms;

/// <summary>Business Owner local freeze transaction. Durable acceptance means DISPATCH_PENDING, never engine or business success.</summary>
public sealed partial class ToolDispositionApprovalOwnerV1(CP6Context db, OperationsOwnerV1 owner, ScopeAuthorizationFenceV1 scope)
{
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private Guid Environment { get { try { return scope.EnvironmentId; } catch(ScopeAuthorizationExceptionV1 e) { throw Error(e.Message,e.Status); } } }
    private static DateTimeOffset Now()=>DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    private sealed record Context(OperationsAccessV1 Access, ToolDispositionApprovalStoreV1 Store,
        ToolDispositionContextV1 Disposition, ToolDispositionPolicyViewV1[] Policies, SubmissionRow[] Submissions)
    {
        internal string Etag=>"\"ta1-"+ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new {
            Disposition.Etag,Policy=Policies.LastOrDefault()?.Reference,Submissions=Submissions.Select(x=>x.Id).Order().ToArray() }))+"\"";
    }
    private async Task<Context> Prepare(OperationsAccessV1 a,Guid id,bool write,CancellationToken ct)
    {
        try
        {
            await scope.Root(ct);
            if(write){var lease=await scope.AcquireAsync(ct);a.AtCommit(async t=> {
                try { await scope.ValidateAsync(lease,t); } catch(ScopeAuthorizationExceptionV1 e) { throw Error(e.Message,e.Status); }
            });}
        }
        catch(ScopeAuthorizationExceptionV1 e){throw Error(e.Message,e.Status);}
        var d=await new ToolDispositionOwnerV1(db,owner).ReadOwnedAsync(a,id,write,ct);
        var store=new ToolDispositionApprovalStoreV1(db,Environment);await store.Fence(write,ct);
        return new(a,store,d,await store.Policies(id,ct),await store.Submissions(id,ct));
    }
    private static void Text(string? value,int max)
    {
        if(string.IsNullOrWhiteSpace(value))throw Error("TOOL_APPROVAL_INPUT_INVALID",400);
        try { new UTF8Encoding(false,true).GetByteCount(value); } catch(EncoderFallbackException){throw Error("TOOL_APPROVAL_INPUT_INVALID",400);}
        if(value.EnumerateRunes().Count()>max)throw Error("TOOL_APPROVAL_INPUT_INVALID",400);
    }
    private static ToolMaintenanceAuthorityV1[] Witnesses(OperationsAccessV1 a)=>a.Authorities.DistinctBy(x=>(x.Resource,x.Action))
        .OrderBy(x=>x.Resource,StringComparer.Ordinal).ThenBy(x=>x.Action,StringComparer.Ordinal)
        .Select(x=>new ToolMaintenanceAuthorityV1(x.Resource,x.Action,x.Source.SourcePermissionVersion,x.OrganizationVersion)).ToArray();
    private async Task<ToolDispositionPolicyCommandV1> RequirePolicy(Context c,ToolDispositionRefV1 reference,CancellationToken ct,bool storedSources=false)
    {
        var policy=(await c.Store.Policies(c.Disposition.ToolId,ct)).LastOrDefault();
        if(policy==null || policy.Reference!=reference)throw Error("TOOL_APPROVAL_POLICY_CHANGED",412);
        var p=policy.Fact.Body;var now=DateTimeOffset.UtcNow;
        if(p.State!="ENABLED" || p.EffectiveFrom>now || p.EffectiveTo<=now)throw Error("POLICY_UNRESOLVED",422);
        if(p.Site!=c.Disposition.Site)throw Error("TOOL_SITE_CHANGED",412);
        if(!policy.Fact.Authorities.Any(x=>x.Resource==OperationsOwnerV1.ToolInstance&&x.Action=="dispose-policy")
            || !policy.Fact.Authorities.Any(x=>x.Resource==OperationsOwnerV1.PhysicalSite&&x.Action=="query"))throw Error("TOOL_APPROVAL_UNAVAILABLE",503);
        foreach(var w in policy.Fact.Authorities)
        {
            var authority=await Authority.ToolDispositionPolicyAuthorityAsync(w.Resource,w.Action,policy.Fact.ActorId,ct);
            if(authority.Source.SourcePermissionVersion!=w.PermissionVersion || authority.OrganizationVersion!=w.OrganizationVersion)throw Error("TOOL_APPROVAL_POLICY_ISSUER_CHANGED",412);
            var access=new OperationsAccessV1(db,owner,authority,storedSubjectSource:storedSources
                ? (resource,action,t)=>Authority.ToolDispositionPolicyAuthorityAsync(resource,action,policy.Fact.ActorId,t) : null);
            if(w.Resource==OperationsOwnerV1.ToolInstance)await access.RequireToolInstance(db,c.Disposition.ToolId,ct);
            if(w.Resource==OperationsOwnerV1.PhysicalSite)
            {
                var site=await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,access,p.Site.Id,ct);
                if(!site.Enabled || site.Reference!=p.Site)throw Error("TOOL_SITE_CHANGED",412);
            }
            await access.ValidateFinalAsync(ct);
        }
        return p;
    }
    public Task<ToolDispositionApprovalContextV1> ContextAsync(Guid id,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>{
            var c=await Prepare(a,id,false,t);var policy=c.Policies.LastOrDefault();var knowledge="MISSING";
            if(policy!=null)try{await RequirePolicy(c,policy.Reference,t);knowledge="CURRENT";}
                catch(OrderPriceCorrectionException e)when(e.Status is 403 or 404 or 412 or 422){knowledge="UNAVAILABLE";}
                catch(CurrentPermissionUnavailableException){knowledge="UNAVAILABLE";}
            return new ToolDispositionApprovalContextV1(db.CurrentTenantId,id,c.Etag,c.Disposition,policy,knowledge,
                c.Submissions.SingleOrDefault() is {} s?c.Store.View(s):null,
                c.Submissions.SingleOrDefault() is {} delivered?await ToolApprovalDeliveryV1.ReadAsync(db,Environment,delivered.Id,t):null);
        },ct);
    private Task<ToolDispositionApprovalResultV1> Write<T>(Guid id,string? etag,Guid operation,string action,T command,
        Func<Context,CancellationToken,Task<ToolDispositionApprovalResultV1>> change,CancellationToken ct)
    {
        if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract=Contract,environment=Environment,id,etag,action,command});
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,action,true,async(a,t)=>{
            var c=await Prepare(a,id,original==null,t);
            if(original!=null)return await Disclose(c,operation,original,bytes,t);
            await a.RequireToolInstance(db,id,t);
            var result=await change(c,t);
            await journal.Store(Journal,Contract,operation,a.ActorId,bytes,result,[id],new{action,a.ActorId,result.Fact,command},t);
            return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(Journal,operation,t);return original==null?action:"query";});
    }
    private static void Match(Context c,string? etag)
    {
        if(string.IsNullOrEmpty(etag))throw Error("IF_MATCH_REQUIRED",428);
        if(etag!=c.Etag)throw Error("TOOL_APPROVAL_CONTEXT_CHANGED",412);
        if(c.Disposition.IsDiscarded)throw Error("TOOL_INSTANCE_DISCARDED",409);
    }
    private async Task Hold(Context c,CancellationToken ct,ToolDispositionRefV1? policy=null)
    {
        var q=await c.Access.SourceAsync(OperationsOwnerV1.ToolInstance,ct);
        await new ToolInstanceStoreV1(db).HoldExactAsync(q,c.Disposition.Native,ct);
        c.Access.AtCommit(async t=>{
            if(policy!=null)await RequirePolicy(c,policy,t);
            var final=await new ToolDispositionOwnerV1(db,owner).ReadOwnedAsync(c.Access,c.Disposition.ToolId,true,t);
            if(final.Etag!=c.Disposition.Etag)throw Error("TOOL_APPROVAL_CONTEXT_CHANGED",412);
            var sites=await q.SourceAsync(OperationsOwnerV1.PhysicalSite,t);
            var site=await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,sites,c.Disposition.Site.Id,t);
            if(!site.Enabled || site.Reference!=c.Disposition.Site)throw Error("TOOL_SITE_CHANGED",412);
            await c.Access.RequireToolInstance(db,c.Disposition.ToolId,t);
            // Nested context/policy readers run after source participant callbacks. Recheck the
            // native head after them, so a same-UoW change cannot hide behind an earlier snapshot.
            var tools=new ToolInstanceStoreV1(db);await tools.Fence(t);
            if((await tools.Read(q,c.Disposition.ToolId,null,t)).Reference!=c.Disposition.Native)throw Error("TOOL_INSTANCE_CHANGED",412);
        });
    }
    public Task<ToolDispositionApprovalResultV1> PolicyAsync(Guid id,string? etag,ToolDispositionPolicyCommandV1 input,CancellationToken ct=default)
    {
        Text(input.Reason,500);
        var p=OperationsOwnerV1.Freeze(input);
        if(p.State is not("ENABLED" or "REVOKED") || p.Site==null || p.ReasonCodes==null || p.ReasonCodes.Length is <1 or >3
            || p.ReasonCodes.Distinct(StringComparer.Ordinal).Count()!=p.ReasonCodes.Length || p.ReasonCodes.Any(x=>x is not("WEAR" or "DAMAGE" or "OBSOLETE"))
            || p.RequiredHumanApprovals is <1 or >1000 || p.EffectiveTo<=p.EffectiveFrom
            || p.EffectiveFrom.Ticks%TimeSpan.TicksPerMillisecond!=0 || p.EffectiveTo.Ticks%TimeSpan.TicksPerMillisecond!=0)throw Error("TOOL_APPROVAL_POLICY_INVALID",400);
        return Write(id,etag,p.OperationId,"dispose-policy",p,async(c,t)=>{
            Match(c,etag);if(p.Site!=c.Disposition.Site)throw Error("TOOL_SITE_CHANGED",412);
            var fact=await c.Store.AddPolicy(new(Environment,db.CurrentTenantId,id,Guid.NewGuid(),c.Access.ActorId,Now(),Witnesses(c.Access),p),t);
            await Hold(c,t);
            return new(p.OperationId,id,"POLICY",fact.Reference);
        },ct);
    }
    private static byte[] Snapshot(Context c)=>ApprovalCanonicalV2.Bytes(new {
        schema=SnapshotSchema,toolId=c.Disposition.ToolId,request=c.Disposition.Request,
        native=c.Disposition.Native,site=c.Disposition.Site,obligations=c.Disposition.Obligations,
        purpose="TOOL_DISCARD",reasonCode=c.Disposition.Request!.Fact.ReasonCode
    });
    public Task<ToolDispositionApprovalResultV1> SubmitAsync(Guid id,string? etag,ToolDispositionSubmitV1 input,CancellationToken ct=default)
    {
        Text(input.Reason,500);var cmd=OperationsOwnerV1.Freeze(input);
        if(cmd.Request==null || cmd.Policy==null || string.IsNullOrEmpty(cmd.ExpectedObligationVersion))throw Error("TOOL_APPROVAL_INPUT_INVALID",400);
        return Write(id,etag,cmd.OperationId,"dispose-submit",cmd,async(c,t)=>{
            var request=c.Disposition.Request;
            if(request==null || request.Reference!=cmd.Request)throw Error("TOOL_APPROVAL_REQUEST_CHANGED",412);
            var policy=await RequirePolicy(c,cmd.Policy,t);
            if(!policy.ReasonCodes.Contains(request.Fact.ReasonCode,StringComparer.Ordinal))throw Error("POLICY_UNRESOLVED",422);
            var snapshot=Snapshot(c);if(snapshot.Length>262144)throw Error("SNAPSHOT_SCHEMA_INVALID",422);
            var authorization=Encoding.UTF8.GetString(ApprovalCanonicalV2.Bytes(new{actorId=c.Access.ActorId,authorities=Witnesses(c.Access)}));
            var subject=new ApprovalSubjectV2(ToolDispositionStoreV1.DispositionOwner,"WMS_TOOL_DISCARD","TOOL_DISPOSITION_REQUEST",request.Reference.Id.ToString(),request.Reference.Version,
                new("sha256",FactCodec,request.Reference.Digest));
            var selectedPolicy=new ApprovalPolicyV2(new(cmd.Policy.Owner,cmd.Policy.Id.ToString(),cmd.Policy.Version),cmd.Policy.Version,new("sha256",ApprovalCanonicalV2.Codec,cmd.Policy.Digest));
            var intent=ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new {scope=new ApprovalScopeV2(Environment.ToString(),db.CurrentTenantId),subject,policy=selectedPolicy,roundId=(string?)null}));
            var prior=c.Submissions.SingleOrDefault();
            if(prior!=null)
            {
                // A fresh HTTP key cannot start a second review while this original Owner intent awaits transport.
                var old=c.Store.Envelope(prior);
                if(prior.Intent!=intent)throw Error("SUBJECT_ALREADY_SUBMITTED",409);
                if(prior.Actor!=c.Access.ActorId || prior.Authorization!=authorization || old.Snapshot.CanonicalBytesBase64!=Convert.ToBase64String(snapshot)
                    || cmd.ExpectedObligationVersion!=c.Disposition.Obligations.Version)throw Error("INTENT_PAYLOAD_CONFLICT",409);
                await Hold(c,t,cmd.Policy);
                return new(cmd.OperationId,id,"SUBMIT",new("Main.Wms.ToolDispositionSubmission.v1",prior.Id,"1",prior.Digest),c.Store.View(prior));
            }
            Match(c,etag);
            if(cmd.ExpectedObligationVersion!=c.Disposition.Obligations.Version)throw Error("TOOL_APPROVAL_CONTEXT_CHANGED",412);
            var sequence=await db.Database.SqlQuery<long>($"""
                SELECT sequence AS "Value" FROM cp6_tool_disposition_v1.facts WHERE tenant_id={db.CurrentTenantId} AND record_id={request.Reference.Id}
                """).SingleAsync(t);
            var submission=Guid.NewGuid();var outbox=Guid.NewGuid();
            var e=new ApprovalSubmitCommandV2("oa.app/2.0",new(Environment.ToString(),db.CurrentTenantId),cmd.OperationId,submission,subject,selectedPolicy,
                null,null,ApprovalBindingTimelineBuilderV2.Version(sequence),c.Access.ActorId,new(AuthorizationOwner,submission.ToString(),"1"),
                new[]{request.Fact.ActorId,c.Access.ActorId}.Distinct().Order().ToArray(),
                new(SnapshotSchema,Convert.ToBase64String(snapshot),ApprovalCanonicalV2.Digest(snapshot),[
                    new(cmd.Request.Owner,cmd.Request.Id.ToString(),cmd.Request.Version),selectedPolicy.Ref]),null,cmd.Reason,outbox,Now());
            var payload=ApprovalCanonicalV2.Bytes(e);
            var row=new SubmissionRow(submission,id,request.Reference.Id,cmd.Policy.Id,c.Access.ActorId,cmd.OperationId,intent,payload,ApprovalCanonicalV2.Hash(payload),authorization);
            await c.Store.Add(row,t);await Hold(c,t,cmd.Policy);
            return new(cmd.OperationId,id,"SUBMIT",new("Main.Wms.ToolDispositionSubmission.v1",submission,"1",row.Digest),c.Store.View(row));
        },ct);
    }
    private async Task<ToolDispositionApprovalResultV1> Disclose(Context c,Guid operation,MainOwnerOperationJournalV1.Original original,byte[]? input,CancellationToken ct)
    {
        if(original.Actor!=c.Access.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null&&!MainOwnerOperationJournalV1.Matches(original,input))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var r=JsonSerializer.Deserialize<ToolDispositionApprovalResultV1>(original.Result);
        if(original.State!="COMMITTED"||r==null||r.OperationId!=operation||r.ObjectId!=c.Disposition.ToolId||original.Objects.Length!=1||original.Objects[0]!=r.ObjectId
            ||r.IsDiscarded||r.IsUsePermit)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        if(r.Kind=="POLICY")
        { if(!c.Policies.Any(x=>x.Reference==r.Fact)||r.Submission!=null)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503); }
        else if(r.Kind=="SUBMIT")
        {
            var s=c.Submissions.SingleOrDefault(x=>x.Id==r.Fact.Id);
            if(s==null||r.Fact!=new ToolDispositionRefV1("Main.Wms.ToolDispositionSubmission.v1",s.Id,"1",s.Digest)
                ||JsonSerializer.Serialize(r.Submission)!=JsonSerializer.Serialize(c.Store.View(s)))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        }
        else throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        var q=await c.Access.SourceAsync(OperationsOwnerV1.ToolInstance,ct);await q.RequireToolInstance(db,r.ObjectId,ct);return r;
    }
    public Task<ToolDispositionApprovalOperationV1> OperationAsync(Guid operation,bool close,CancellationToken ct=default)
    {
        if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",close,async(a,t)=>{
            var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(Journal,operation,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId||original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State=="CLOSED")return new ToolDispositionApprovalOperationV1(operation,"CLOSED");
                if(original.Objects.Length!=1)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
                var c=await Prepare(a,original.Objects[0],false,t);return new(operation,"COMMITTED",await Disclose(c,operation,original,null,t));
            }
            if(!close)return new(operation,"UNKNOWN");var result=new ToolDispositionApprovalOperationV1(operation,"CLOSED");
            await journal.Store(Journal,Contract,operation,a.ActorId,null,result,[],new{action="close-tool-approval-operation"},t);return result;
        },ct);
    }
}
