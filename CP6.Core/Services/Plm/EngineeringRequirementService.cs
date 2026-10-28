using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;
public sealed class EngineeringRequirementService(CP6Context db,OperationsOwnerV1 owner)
{
    private OperationsOwnerV1 Authority=>owner.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<EngineeringProductScopeV1> ScopeAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,"query",false,
        (a,_)=>Task.FromResult(new EngineeringProductScopeV1(db.CurrentTenantId,a.ActorId)),ct,readOnlyAuthorityFence:true);
    public Task<EngineeringRevisionCreationContextV1> CreationContextAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,"add",false,
        async(a,t)=>{await new EngineeringRequirementStoreV1(db).Fence(false,t);a.RequireRequirementFields(true);return await a.EngineeringRequirementCreationContext(t);},ct,readOnlyAuthorityFence:true);
    public Task<EngineeringRequirementSnapshotV1[]> ListAsync(string? kind=null,string? search=null,int page=1,CancellationToken ct=default)
    {
        if(kind!=null && kind is not("SOURCE" or "REQUIREMENT") || search?.Length>256 || page is <1 or >100000)throw Error("REQUIREMENT_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,"query",false,async(a,t)=>
        {
            var s=new EngineeringRequirementStoreV1(db);await s.Fence(false,t);var rows=a.EngineeringRequirements();
            if(kind!=null)rows=rows.Where(x=>x.Kind==kind);if(!string.IsNullOrEmpty(search))rows=rows.Where(x=>x.Name.Contains(search));
            var ids=await rows.OrderBy(x=>x.Name).ThenBy(x=>x.Id).Skip((page-1)*20).Take(20).Select(x=>x.Id).ToArrayAsync(t);
            var result=new List<EngineeringRequirementSnapshotV1>();
            foreach(var id in ids)result.Add(await Observe(a,s,await s.Read(id,null,t),t));return result.ToArray();
        },ct);
    }
    public Task<EngineeringRequirementSnapshotV1> ReadAsync(Guid id,string? version=null,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("REQUIREMENT_IDENTITY_INVALID",400);var v=version==null?(long?)null:Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,"query",false,async(a,t)=>
        {var s=new EngineeringRequirementStoreV1(db);await s.Fence(false,t);await a.RequireEngineeringRequirement(id,t);return await Observe(a,s,await s.Read(id,v,t),t);},ct);
    }
    // Traverse exact referenced originals. Every disclosed dependency retains current object and field authorization.
    private static async Task<EngineeringRequirementSnapshotV1> Observe(OperationsAccessV1 a,EngineeringRequirementStoreV1 s,EngineeringRequirementSnapshotV1 value,CancellationToken ct)
    {
        var seen=new HashSet<EngineeringSourceRefV1>();var missing=EngineeringRequirementValuesV1.Missing(value.Body);
        async Task Visit(EngineeringRequirementSnapshotV1 v,int depth)
        {
            if(!seen.Add(v.Reference))return;if(depth>128 || seen.Count>4096)throw Error("REQUIREMENT_GRAPH_LIMIT",422);
            await a.RequireEngineeringRequirement(v.Reference.Id,ct);a.AtCommit(t=>a.RequireEngineeringRequirement(v.Reference.Id,t));
            if(v.Body.Requirement is not {} r)return;
            var source=await Exact(a,s,r.Source,"SOURCE",ct);
            if(!source.IsCurrent)missing.Add("SOURCE_VERSION_CHANGED:"+v.Reference.Id);
            if(source.Body.Source!.Kind=="UNRESOLVED")missing.Add("SOURCE_UNRESOLVED:"+v.Reference.Id);
            foreach(var link in r.Relations)
            {
                var target=await Exact(a,s,link.Target,"REQUIREMENT",ct);
                if(!target.IsCurrent)missing.Add("RELATION_VERSION_CHANGED:"+link.Id);
                await Visit(target,depth+1);
            }
        }
        await Visit(value,0);return value with{Missing=missing.Distinct(StringComparer.Ordinal).ToArray()};
    }
    private static async Task<EngineeringRequirementSnapshotV1> Exact(OperationsAccessV1 a,EngineeringRequirementStoreV1 s,EngineeringSourceRefV1 r,string kind,CancellationToken ct)
    {
        EngineeringRequirementValuesV1.Ref(r);await a.RequireEngineeringRequirement(r.Id,ct);a.AtCommit(t=>a.RequireEngineeringRequirement(r.Id,t));
        var value=await s.Read(r.Id,Revision(r.Version),ct);
        if(value.Reference!=r || value.Body.Kind!=kind)throw Error("REQUIREMENT_REFERENCE_CHANGED",412);return value;
    }
    private static async Task ValidateSources(OperationsAccessV1 a,EngineeringRequirementStoreV1 s,EngineeringRequirementCommandV1 c,CancellationToken ct)
    {
        if(c.Body.Requirement is not {} r)return;
        var source=await Exact(a,s,r.Source,"SOURCE",ct);
        if(r.ClauseIds.Any(id=>!source.Body.Source!.Clauses.Any(x=>x.Id==id)))throw Error("REQUIREMENT_CLAUSE_NOT_FOUND",422);
        // Backwards exact-version edges must not prove the new version through an older version of its own logical identity.
        var seen=new HashSet<EngineeringSourceRefV1>();
        async Task Walk(EngineeringSourceRefV1 target,int depth)
        {
            if(target.Id==c.ObjectId)throw Error("REQUIREMENT_RELATION_CYCLE",422);
            if(!seen.Add(target))return;if(depth>128 || seen.Count>4096)throw Error("REQUIREMENT_GRAPH_LIMIT",422);
            var v=await Exact(a,s,target,"REQUIREMENT",ct);
            foreach(var l in v.Body.Requirement!.Relations.Where(x=>x.Kind!="CONFLICT"))await Walk(l.Target,depth+1);
        }
        foreach(var link in r.Relations)
        {
            if(link.Target.Id==c.ObjectId)throw Error("REQUIREMENT_RELATION_CYCLE",422);
            await Exact(a,s,link.Target,"REQUIREMENT",ct);
            if(link.Kind!="CONFLICT")await Walk(link.Target,0);
        }
    }
    public Task<EngineeringRequirementResultV1> CommandAsync(EngineeringRequirementCommandV1 input,CancellationToken ct=default)
    {
        if(input==null || input.OperationId==Guid.Empty || input.ObjectId==Guid.Empty || input.Action is not("CREATE" or "REVISE"))throw Error("REQUIREMENT_COMMAND_INVALID",400);
        var c=OperationsOwnerV1.Freeze(input);EngineeringRequirementValuesV1.Text(c.Reason,true);EngineeringRequirementValuesV1.Body(c.Body);
        if(c.Action=="CREATE") {if(c.ExpectedVersion!=null || c.ExpectedEtag!=null)throw Error("REQUIREMENT_COMMAND_INVALID",400);}
        else {Revision(c.ExpectedVersion);if(c.ExpectedEtag==null || c.ExpectedEtag==Guid.Empty || c.DepartmentId!=null || c.ExpectedAuthorityVersion!=null)throw Error("REQUIREMENT_COMMAND_INVALID",400);}
        var bytes=MainOwnerOperationJournalV1.Encode(c);if(bytes.Length>2097152)throw Error("REQUIREMENT_SIZE_LIMIT",400);
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;var action=c.Action=="CREATE"?"add":"edit";
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,action,true,async(a,t)=>
        {
            var s=new EngineeringRequirementStoreV1(db);await s.Fence(true,t);
            if(original!=null)return await Disclose(a,s,original,c.OperationId,bytes,t);
            a.RequireRequirementFields(true);OperationsAccessV1.NewEngineeringRequirementBinding? ticket=null;
            if(c.Action=="CREATE")ticket=await a.BeginEngineeringRequirementCreation(c.ObjectId,c.ExpectedAuthorityVersion,c.DepartmentId,t);
            else
            {
                await a.RequireEngineeringRequirement(c.ObjectId,t);var old=await s.Read(c.ObjectId,null,t);
                if(old.Reference.Version!=c.ExpectedVersion || old.Etag!=c.ExpectedEtag)throw Error("REQUIREMENT_VERSION_CHANGED",412);
                // Editing cannot remove a revoked dependency to disclose or launder its former original.
                await Observe(a,s,old,t);
            }
            await ValidateSources(a,s,c,t);var saved=await s.Append(c,a.ActorId,t);
            if(ticket!=null)await a.CompleteEngineeringRequirementCreation(ticket,t);
            await Observe(a,s,saved,t);
            var result=new EngineeringRequirementResultV1(c.OperationId,saved.Body.Kind,c.ObjectId,saved.Reference.Version,saved.Reference.Digest);
            await journal.Store(EngineeringRequirementValuesV1.Owner,EngineeringRequirementValuesV1.Contract,c.OperationId,a.ActorId,bytes,result,[c.ObjectId],
                new{c.Action,c.Reason,actor=a.ActorId,result},t);return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(EngineeringRequirementValuesV1.Owner,c.OperationId,t);return original==null?action:"query";});
    }
    private static async Task<EngineeringRequirementResultV1> Disclose(OperationsAccessV1 a,EngineeringRequirementStoreV1 s,MainOwnerOperationJournalV1.Original original,Guid id,byte[]? bytes,CancellationToken ct)
    {
        if(original.Actor!=a.ActorId || original.Contract!=EngineeringRequirementValuesV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null && !MainOwnerOperationJournalV1.Matches(original,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<EngineeringRequirementResultV1>(original.Result);
        if(result==null || result.OperationId!=id || !original.Objects.SequenceEqual(new[]{result.ObjectId}))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        var v=await Observe(a,s,await s.Read(result.ObjectId,Revision(result.Version),ct),ct);
        if(v.Reference.Digest!=result.Digest || v.Body.Kind!=result.Kind)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);return result;
    }
    public Task<EngineeringRequirementOperationV1> OperationAsync(Guid id,bool close,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,"query",close,async(a,t)=>
        {
            a.RequireRequirementFields(false);var s=new EngineeringRequirementStoreV1(db);await s.Fence(false,t);
            var j=new MainOwnerOperationJournalV1(db);var o=await j.Read(EngineeringRequirementValuesV1.Owner,id,t);
            if(o!=null)
            {
                if(o.Actor!=a.ActorId || o.Contract!=EngineeringRequirementValuesV1.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                return o.State=="CLOSED"?new(id,"CLOSED"):new EngineeringRequirementOperationV1(id,"COMMITTED",await Disclose(a,s,o,id,null,t));
            }
            if(!close)return new EngineeringRequirementOperationV1(id,"UNKNOWN");
            var result=new EngineeringRequirementOperationV1(id,"CLOSED");await j.Store(EngineeringRequirementValuesV1.Owner,EngineeringRequirementValuesV1.Contract,id,a.ActorId,null,result,[],new{action="close-uncommitted-requirement",actor=a.ActorId},t);return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
