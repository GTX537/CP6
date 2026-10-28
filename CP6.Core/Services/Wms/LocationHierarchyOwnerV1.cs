using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.LocationHierarchyStoreV1;

namespace CP6.Core.Services.Wms;
public sealed class LocationHierarchyOwnerV1(CP6Context db,OperationsOwnerV1 owner)
{
    private OperationsOwnerV1 Authority=>owner.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private async Task<Warehouse> Warehouse(OperationsAccessV1 a,Guid id,bool edit,CancellationToken ct)
    {
        var w=(Warehouse)await a.RequireMaster("warehouses",id,ct);
        if(edit){await (await a.SourceAsync(OperationsOwnerV1.Warehouse,ct)).RequireMaster("warehouses",id,ct);a.RequireWarehouseHierarchyControl(w);}
        a.AtCommit(async t=>{var current=(Warehouse)await a.RequireMaster("warehouses",id,t);if(edit)a.RequireWarehouseHierarchyControl(current);});
        return w;
    }
    private static string Rev(long? n)=>n?.ToString(CultureInfo.InvariantCulture)??"0";
    public Task<LocationHierarchyContextV1> ContextAsync(Guid warehouse,CancellationToken ct=default)=>Authority.ExecuteAsync(
        OperationsOwnerV1.Warehouse,"query",false,async(a,t)=>
        {
            var w=await Warehouse(a,warehouse,false,t);var s=new LocationHierarchyStoreV1(db);await s.Fence(false,t);
            var h=await s.HeadFor(warehouse,t);var p=await s.Read(warehouse,null,t);
            return new LocationHierarchyContextV1(db.CurrentTenantId,a.ActorId,warehouse,w.WarehouseCd,p?.Reference.Version??"absent",Rev(h?.Hierarchy),p,
                p==null?"POLICY_UNAVAILABLE":!p.Original.Enabled?"DISABLED":await s.Compatible(w,p.Original.RootLevels,p.Original.Edges,t)?"COMPATIBLE":"REQUIRES_REPAIR");
        },ct);
    public Task<LocationHierarchyPolicyV1> HistoryAsync(Guid warehouse,string version,CancellationToken ct=default)
    {
        var revision=EngineeringProductValuesV1.Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.Warehouse,"query",false,async(a,t)=>
        {await Warehouse(a,warehouse,false,t);var s=new LocationHierarchyStoreV1(db);await s.Fence(false,t);return await s.Read(warehouse,revision,t)??throw Error("HIERARCHY_VERSION_NOT_FOUND",404);},ct);
    }
    public Task<LocationHierarchyResultV1> SaveAsync(Guid warehouse,LocationHierarchyCommandV1 input,CancellationToken ct=default)
    {
        if(warehouse==Guid.Empty||input==null||input.OperationId==Guid.Empty||!EngineeringProductValuesV1.ValidText(input.Reason,1000,true))throw Error("HIERARCHY_COMMAND_INVALID",400);
        var c=OperationsOwnerV1.Freeze(input);Validate(c.RootLevels,c.Edges);
        if(c.ExpectedVersion!="absent")EngineeringProductValuesV1.Revision(c.ExpectedVersion);
        if(c.ExpectedHierarchyRevision!="0")EngineeringProductValuesV1.Revision(c.ExpectedHierarchyRevision);
        c=c with{RootLevels=c.RootLevels.Order().ToArray(),Edges=c.Edges.OrderBy(x=>x.ParentLevel).ThenBy(x=>x.ChildLevel).ToArray()};
        var bytes=MainOwnerOperationJournalV1.Encode(new{warehouse,command=c});var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.Warehouse,"hierarchy-policy",true,async(a,t)=>
        {
            var w=await Warehouse(a,warehouse,original==null,t);var s=new LocationHierarchyStoreV1(db);await s.Fence(true,t);
            if(original!=null)return await Disclose(a,s,warehouse,original,c.OperationId,bytes,t);
            var h=await s.HeadFor(warehouse,t);var before=await s.Read(warehouse,null,t);
            if(c.ExpectedVersion!=(before?.Reference.Version??"absent")||c.ExpectedHierarchyRevision!=Rev(h?.Hierarchy))throw Error("HIERARCHY_POLICY_CHANGED",412);
            if(before==null&&!c.Enabled)throw Error("HIERARCHY_INITIAL_POLICY_REQUIRED");
            if(c.Enabled&&!await s.Compatible(w,c.RootLevels,c.Edges,t))throw Error("HIERARCHY_CURRENT_TREE_INCOMPATIBLE",409);
            var policy=await s.Append(warehouse,c,a.ActorId,before,t);var current=await s.HeadFor(warehouse,t);
            var result=new LocationHierarchyResultV1(c.OperationId,warehouse,policy.Reference,before?.Reference!=policy.Reference);
            await journal.Store(Owner,Contract,c.OperationId,a.ActorId,bytes,result,[warehouse],new{c.Reason,result},t);
            a.AtCommit(async token=>
            {
                await s.Fence(false,token);
                if((await s.Read(warehouse,null,token))?.Reference!=policy.Reference||(await s.HeadFor(warehouse,token))?.Hierarchy!=current?.Hierarchy)
                    throw Error("HIERARCHY_POLICY_CHANGED",412);
            });return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(Owner,c.OperationId,t);return original==null?"hierarchy-policy":"query";});
    }
    private async Task<LocationHierarchyResultV1> Disclose(OperationsAccessV1 a,LocationHierarchyStoreV1 s,Guid warehouse,
        MainOwnerOperationJournalV1.Original o,Guid operation,byte[]? bytes,CancellationToken ct)
    {
        if(o.Actor!=a.ActorId||o.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(o.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        var r=JsonSerializer.Deserialize<LocationHierarchyResultV1>(o.Result);
        if(r==null||r.OperationId!=operation||r.WarehouseId!=warehouse||!o.Objects.SequenceEqual(new[]{warehouse})
            ||(await s.Read(warehouse,EngineeringProductValuesV1.Revision(r.Reference.Version),ct))?.Reference!=r.Reference)
            throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        if(bytes!=null&&!MainOwnerOperationJournalV1.Matches(o,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        return r;
    }
    public Task<LocationHierarchyOperationV1> OperationAsync(Guid warehouse,Guid operation,bool close=false,CancellationToken ct=default)
    {
        if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        var j=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.Warehouse,close?"abandon":"query",close,async(a,t)=>
        {
            await Warehouse(a,warehouse,close&&original==null,t);var s=new LocationHierarchyStoreV1(db);await s.Fence(close,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId||original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                return original.State=="CLOSED"?new(operation,"CLOSED"):new LocationHierarchyOperationV1(operation,"COMMITTED",await Disclose(a,s,warehouse,original,operation,null,t));
            }
            if(!close)return new LocationHierarchyOperationV1(operation,"UNKNOWN");
            var r=new LocationHierarchyOperationV1(operation,"CLOSED");await j.Store(Owner,Contract,operation,a.ActorId,null,r,[],new{action="close-uncommitted-location-hierarchy",warehouse},t);return r;
        },ct,actionAtFence:async t=>{original=await j.Read(Owner,operation,t);return original!=null||!close?"query":"abandon";});
    }
    public Task<LocationHierarchyCheckV1> PrecheckAsync(Guid warehouse,LocationHierarchyPrecheckV1 c,CancellationToken ct=default)
    {
        if(c==null||c.LocationLevel is <1 or >5||c.ParentLocationId==Guid.Empty)throw Error("HIERARCHY_PRECHECK_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.Warehouse,"query",false,async(a,t)=>
        {
            var w=await Warehouse(a,warehouse,false,t);var s=new LocationHierarchyStoreV1(db);await s.Fence(false,t);
            var p=await s.Read(warehouse,null,t)??throw Error("HIERARCHY_POLICY_UNAVAILABLE");var h=(await s.HeadFor(warehouse,t))!;
            if(p.Reference.Version!=c.ExpectedVersion||Rev(h.Hierarchy)!=c.ExpectedHierarchyRevision)throw Error("HIERARCHY_POLICY_CHANGED",412);
            if(!p.Original.Enabled)throw Error("HIERARCHY_POLICY_DISABLED");
            if(!await s.Compatible(w,p.Original.RootLevels,p.Original.Edges,t))throw Error("HIERARCHY_CURRENT_TREE_INCOMPATIBLE",409);
            if(c.ParentLocationId is Guid id)
            {
                var location=await a.SourceAsync(OperationsOwnerV1.Location,t);var parent=(Location)await location.RequireMaster("locations",id,t);
                if(!await db.Locations.AsNoTracking().AnyAsync(x=>x.Id==parent.Id&&x.WarehouseCd==w.WarehouseCd,t)||!p.Original.Edges.Contains(new(parent.LocationLevel,c.LocationLevel)))throw Error("HIERARCHY_EDGE_FORBIDDEN");
                a.AtCommit(async token=>{await location.RequireMaster("locations",id,token);});
            }
            else if(!p.Original.RootLevels.Contains(c.LocationLevel))throw Error("HIERARCHY_ROOT_FORBIDDEN");
            a.AtCommit(async token=>{await s.Fence(false,token);if((await s.HeadFor(warehouse,token))?.Hierarchy!=h.Hierarchy
                ||(await s.Read(warehouse,null,token))?.Reference!=p.Reference)throw Error("HIERARCHY_POLICY_CHANGED",412);});
            return new LocationHierarchyCheckV1(warehouse,c.ParentLocationId,c.LocationLevel,p.Reference,Rev(h.Hierarchy));
        },ct);
    }
}
