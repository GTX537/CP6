using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Erp;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;
public sealed partial class RequirementSetService(CP6Context db,OperationsOwnerV1 owner,RequirementEvaluationService evaluations,CP6.Core.Services.Sys.ScopeAuthorizationFenceV1 scope)
{
    internal string EvidenceMode=>evaluations.EvidenceMode;
    private OperationsOwnerV1 Authority=>owner.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<RequirementSetScopeV1> ScopeAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.RequirementSet,"query",false,
        async(a,t)=>{await scope.Root(t);if(evaluations.EvidenceMode is not("REAL" or "DEMO"))throw Error("REQUIREMENT_SET_ENVIRONMENT_UNAVAILABLE",503);return new RequirementSetScopeV1(scope.EnvironmentId,db.CurrentTenantId,a.ActorId,evaluations.EvidenceMode);},ct,readOnlyAuthorityFence:true);
    public Task<EngineeringRevisionCreationContextV1> CreationContextAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.RequirementSet,"add",false,
        async(a,t)=>{await new RequirementSetStoreV1(db).Fence(false,t);a.RequireRequirementSetFields(true);return await a.RequirementSetCreationContext(t);},ct,readOnlyAuthorityFence:true);
    public Task<RequirementSetSnapshotV1[]> ListAsync(string? kind=null,string? search=null,int page=1,CancellationToken ct=default)
    {
        if(kind!=null && kind!="SET" || search?.Length>256 || page is <1 or >100000)throw Error("REQUIREMENT_SET_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.RequirementSet,"query",false,async(a,t)=>
        {
            var s=new RequirementSetStoreV1(db);await s.Fence(false,t);var rows=a.RequirementSets();
            if(kind!=null)rows=rows.Where(x=>x.Kind==kind);if(!string.IsNullOrEmpty(search))rows=rows.Where(x=>x.Name.Contains(search));
            var ids=await rows.OrderBy(x=>x.Name).ThenBy(x=>x.Id).Skip((page-1)*20).Take(20).Select(x=>x.Id).ToArrayAsync(t);
            var result=new List<RequirementSetSnapshotV1>();
            foreach(var id in ids)result.Add(await Observe(a,s,await s.Read(id,null,t),t));return result.ToArray();
        },ct);
    }
    public Task<RequirementSetSnapshotV1> ReadAsync(Guid id,string? version=null,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("REQUIREMENT_SET_IDENTITY_INVALID",400);var v=version==null?(long?)null:Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.RequirementSet,"query",false,async(a,t)=>
        {var s=new RequirementSetStoreV1(db);await s.Fence(false,t);await a.RequireRequirementSet(id,t);return await Observe(a,s,await s.Read(id,v,t),t);},ct);
    }
    private async Task<RequirementSetSnapshotV1> Observe(OperationsAccessV1 a,RequirementSetStoreV1 s,RequirementSetSnapshotV1 value,CancellationToken ct,bool hold=false)
    {
        await a.RequireRequirementSet(value.Reference.Id,ct);a.AtCommit(t=>a.RequireRequirementSet(value.Reference.Id,t));
        var missing=new List<string>();var members=new List<RequirementSetMemberObservationV1>();
        if(string.IsNullOrWhiteSpace(value.Body.Scope))missing.Add("SCOPE_REQUIRED");
        if(string.IsNullOrWhiteSpace(value.Body.CompletenessBasis))missing.Add("COMPLETENESS_BASIS_REQUIRED");
        if(value.Body.Members.Length==0)missing.Add("MEMBERS_REQUIRED");
        var requirementAccess=await a.SourceAsync(OperationsOwnerV1.EngineeringRequirement,ct);
        var originals=new EngineeringRequirementStoreV1(db);await originals.Fence(false,ct);
        foreach(var member in value.Body.Members)
        {
            await requirementAccess.RequireEngineeringRequirement(member.Requirement.Id,ct);
            requirementAccess.AtCommit(t=>requirementAccess.RequireEngineeringRequirement(member.Requirement.Id,t));
            var original=await originals.Read(member.Requirement.Id,Revision(member.Requirement.Version),ct);
            if(original.Reference!=member.Requirement || original.Body.Requirement==null)throw Error("REQUIREMENT_SET_MEMBER_CHANGED",412);
            var proof=await evaluations.Source(a,member.Requirement,hold,ct,tolerateInputChange:!hold);
            if(proof.EnvironmentId!=scope.EnvironmentId)throw Error("REQUIREMENT_SET_ENVIRONMENT_CHANGED",503);
            if(proof.Availability!="AVAILABLE")missing.Add("MEMBER_UNAVAILABLE:"+member.Requirement.Id);
            if(proof.Control!=member.EvaluationControl || proof.Approval!=member.Approval || member.Approval==null)missing.Add("MEMBER_APPROVAL_CHANGED:"+member.Requirement.Id);
            // Exclusions are proposals until an authoritative source-scope decision is available.
            // A priority or arbitrary free-text necessity can never grant permission to omit a requirement.
            if(member.Disposition!="INCLUDED")missing.Add("EXCLUSION_SOURCE_DECISION_REQUIRED:"+member.Requirement.Id);
            foreach(var conflict in original.Body.Requirement.Relations.Where(x=>x.Kind=="CONFLICT"))
                missing.Add("SOURCE_CONFLICT_UNRESOLVED:"+member.Requirement.Id+":"+conflict.Target.Id);
            var sourceRef=original.Body.Requirement.Source;
            await requirementAccess.RequireEngineeringRequirement(sourceRef.Id,ct);
            requirementAccess.AtCommit(t=>requirementAccess.RequireEngineeringRequirement(sourceRef.Id,t));
            var source=await originals.Read(sourceRef.Id,Revision(sourceRef.Version),ct);
            if(source.Reference!=sourceRef || source.Body.Source==null)throw Error("REQUIREMENT_SET_MEMBER_SOURCE_UNAVAILABLE",503);
            members.Add(new(original,proof,source));
        }
        if(value.Body.Conflicts.Length>0)missing.Add("CONFLICT_SOURCE_DECISION_REQUIRED");
        var changes=new List<RequirementSetDifferenceV1>();
        if(value.ParentVersion!=null)
        {
            var previous=await s.Read(value.Reference.Id,Revision(value.ParentVersion),ct);
            foreach(var old in previous.Body.Members)
            {
                // Earlier originals remain protected even when a draft proposes an exclusion.
                await evaluations.Source(a,old.Requirement,false,ct,tolerateInputChange:true);
                var next=value.Body.Members.SingleOrDefault(x=>x.Requirement.Id==old.Requirement.Id);
                if(next==null)missing.Add("PREVIOUS_MEMBER_OMITTED:"+old.Requirement.Id);
                if(next!=old)changes.Add(new(old.Requirement.Id,next==null?"REMOVED":"CHANGED",old,next));
            }
            foreach(var m in value.Body.Members.Where(x=>!previous.Body.Members.Any(o=>o.Requirement.Id==x.Requirement.Id)))changes.Add(new(m.Requirement.Id,"ADDED",null,m));
        }
        else changes.AddRange(value.Body.Members.Select(m=>new RequirementSetDifferenceV1(m.Requirement.Id,"ADDED",null,m)));
        // Carry obligations across the entire predecessor chain; an intermediate draft cannot erase one.
        var ancestor=value.ParentVersion;var visited=new HashSet<EngineeringSourceRefV1>();var depth=0;
        while(ancestor!=null)
        {
            if(++depth>4096)throw Error("REQUIREMENT_SET_HISTORY_LIMIT",422);
            var prior=await s.Read(value.Reference.Id,Revision(ancestor),ct);
            foreach(var old in prior.Body.Members)
            {
                if(visited.Add(old.Requirement))await evaluations.Source(a,old.Requirement,false,ct,tolerateInputChange:true);
                if(!value.Body.Members.Any(m=>m.Requirement.Id==old.Requirement.Id))missing.Add("PREVIOUS_MEMBER_OMITTED:"+old.Requirement.Id);
            }
            ancestor=prior.ParentVersion;
        }
        if(hold && missing.Count>0)throw Error("REQUIREMENT_SET_SOURCE_INCOMPLETE",422);
        var applications=new RequirementSetReviewApplicationStoreV1(db,scope);await applications.Ready(ct);
        var approved=await applications.Approved(value.Reference,ct);
        if(approved==null)missing.Add("REQUIREMENT_SET_REVIEW_REQUIRED");
        return value with {MemberStatus=members.ToArray(),Differences=changes.ToArray(),Missing=missing.Distinct().ToArray(),State=approved==null?"DRAFT":"APPROVED"};
    }
    internal async Task<RequirementSetSnapshotV1> ReviewSource(OperationsAccessV1 a,EngineeringSourceRefV1 reference,bool requireAvailable,CancellationToken ct)
    {
        RequirementSetValuesV1.Ref(reference);var s=new RequirementSetStoreV1(db);await s.Fence(false,ct);
        var v=await s.Read(reference.Id,Revision(reference.Version),ct);
        if(v.Reference!=reference)throw Error("REQUIREMENT_SET_REFERENCE_CHANGED",412);
        return await Observe(a,s,v,ct,requireAvailable);
    }
    public Task<RequirementSetResultV1> CommandAsync(RequirementSetCommandV1 input,CancellationToken ct=default)
    {
        if(input==null || input.OperationId==Guid.Empty || input.ObjectId==Guid.Empty || input.Action is not("CREATE" or "REVISE"))throw Error("REQUIREMENT_SET_COMMAND_INVALID",400);
        var c=OperationsOwnerV1.Freeze(input);RequirementSetValuesV1.Text(c.Reason,true);RequirementSetValuesV1.Body(c.Body);
        if(c.Action=="CREATE") {if(c.ExpectedVersion!=null || c.ExpectedEtag!=null)throw Error("REQUIREMENT_SET_COMMAND_INVALID",400);}
        else {Revision(c.ExpectedVersion);if(c.ExpectedEtag==null || c.ExpectedEtag==Guid.Empty || c.DepartmentId!=null || c.ExpectedAuthorityVersion!=null)throw Error("REQUIREMENT_SET_COMMAND_INVALID",400);}
        var bytes=MainOwnerOperationJournalV1.Encode(c);if(bytes.Length>2097152)throw Error("REQUIREMENT_SET_SIZE_LIMIT",400);
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;var action=c.Action=="CREATE"?"add":"edit";
        return Authority.ExecuteAsync(OperationsOwnerV1.RequirementSet,action,true,async(a,t)=>
        {
            var lease=await scope.AcquireAsync(t);a.AtCommit(token=>scope.ValidateAsync(lease,token));
            var s=new RequirementSetStoreV1(db);await s.Fence(true,t);
            if(original!=null)return await Disclose(a,s,original,c.OperationId,bytes,t);
            a.RequireRequirementSetFields(true);OperationsAccessV1.NewRequirementSetBinding? ticket=null;
            if(c.Action=="CREATE")ticket=await a.BeginRequirementSetCreation(c.ObjectId,c.ExpectedAuthorityVersion,c.DepartmentId,t);
            else
            {
                await a.RequireRequirementSet(c.ObjectId,t);var old=await s.Read(c.ObjectId,null,t);
                if(old.Reference.Version!=c.ExpectedVersion || old.Etag!=c.ExpectedEtag)throw Error("REQUIREMENT_SET_VERSION_CHANGED",412);

                // Editing cannot remove a revoked dependency to disclose or launder its former original.
                await Observe(a,s,old,t);
            }
            var saved=await s.Append(c,a.ActorId,t);
            if(ticket!=null)await a.CompleteRequirementSetCreation(ticket,t);
            await Observe(a,s,saved,t);
            var result=new RequirementSetResultV1(c.OperationId,saved.Body.Kind,c.ObjectId,saved.Reference.Version,saved.Reference.Digest);
            await journal.Store(RequirementSetValuesV1.Owner,RequirementSetValuesV1.Contract,c.OperationId,a.ActorId,bytes,result,[c.ObjectId],
                new{c.Action,c.Reason,actor=a.ActorId,result},t);return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(RequirementSetValuesV1.Owner,c.OperationId,t);return original==null?action:"query";});
    }
    private async Task<RequirementSetResultV1> Disclose(OperationsAccessV1 a,RequirementSetStoreV1 s,MainOwnerOperationJournalV1.Original original,Guid id,byte[]? bytes,CancellationToken ct)
    {
        if(original.Actor!=a.ActorId || original.Contract!=RequirementSetValuesV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null && !MainOwnerOperationJournalV1.Matches(original,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<RequirementSetResultV1>(original.Result);
        if(original.State!="COMMITTED" || result==null || result.OperationId!=id || !original.Objects.SequenceEqual(new[]{result.ObjectId}))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        var v=await Observe(a,s,await s.Read(result.ObjectId,Revision(result.Version),ct),ct);
        if(v.Reference.Digest!=result.Digest || v.Body.Kind!=result.Kind)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);return result;
    }
    public Task<RequirementSetOperationV1> OperationAsync(Guid id,bool close,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.RequirementSet,"query",close,async(a,t)=>
        {
            a.RequireRequirementSetFields(false);var s=new RequirementSetStoreV1(db);await s.Fence(false,t);
            var j=new MainOwnerOperationJournalV1(db);var o=await j.Read(RequirementSetValuesV1.Owner,id,t);
            if(o!=null)
            {
                if(o.Actor!=a.ActorId || o.Contract!=RequirementSetValuesV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                return o.State=="CLOSED"?new(id,"CLOSED"):new RequirementSetOperationV1(id,"COMMITTED",await Disclose(a,s,o,id,null,t));
            }
            if(!close)return new RequirementSetOperationV1(id,"UNKNOWN");
            var result=new RequirementSetOperationV1(id,"CLOSED");await j.Store(RequirementSetValuesV1.Owner,RequirementSetValuesV1.Contract,id,a.ActorId,null,result,[],new{action="close-uncommitted-requirement-set",actor=a.ActorId},t);
            var closed=await j.Read(RequirementSetValuesV1.Owner,id,t);
            if(closed==null || closed.State!="CLOSED" || closed.Actor!=a.ActorId || closed.Contract!=RequirementSetValuesV1.Contract)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
            return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
