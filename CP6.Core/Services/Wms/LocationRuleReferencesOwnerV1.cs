using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Entity.DomainModels.Wms;
using static CP6.Core.Services.Wms.LocationRuleReferencesValuesV1;
namespace CP6.Core.Services.Wms;
public sealed class LocationRuleReferencesOwnerV1(CP6Context db,OperationsOwnerV1 owner)
{
    private OperationsOwnerV1 Authority=>owner.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private static async Task<Warehouse> Target(OperationsAccessV1 a,Guid id,bool write,CancellationToken ct)
    {
        var w=(Warehouse)await a.RequireMaster("warehouses",id,ct);
        if(write){await (await a.SourceAsync(OperationsOwnerV1.Warehouse,ct)).RequireMaster("warehouses",id,ct);a.RequireWarehouseHierarchyControl(w);}
        a.AtCommit(async t=>{var n=(Warehouse)await a.RequireMaster("warehouses",id,t);if(write)a.RequireWarehouseHierarchyControl(n);});return w;
    }
    public Task<RuleReferenceContextV1> ContextAsync(Guid warehouse,CancellationToken ct=default)=>Authority.ExecuteAsync(
        OperationsOwnerV1.Warehouse,"query",false,async(a,t)=>
        {var w=await Target(a,warehouse,false,t);var s=new LocationRuleReferencesStoreV1(db);await s.Fence(false,t);var p=await s.Read(warehouse,null,t);
            return new RuleReferenceContextV1(db.CurrentTenantId,a.ActorId,warehouse,w.WarehouseCd,p?.Reference.Version??"absent",p);},ct);
    public Task<RuleReferencePolicyV1> HistoryAsync(Guid warehouse,string version,CancellationToken ct=default)
    {
        var n=EngineeringProductValuesV1.Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.Warehouse,"query",false,async(a,t)=>
        {await Target(a,warehouse,false,t);var s=new LocationRuleReferencesStoreV1(db);await s.Fence(false,t);return await s.Read(warehouse,n,t)??throw Error("RULE_DICTIONARY_VERSION_NOT_FOUND",404);},ct);
    }
    public Task<RuleReferenceResultV1> SaveAsync(Guid warehouse,RuleReferenceCommandV1 input,CancellationToken ct=default)
    {
        if(warehouse==Guid.Empty||input==null||input.OperationId==Guid.Empty||!LocationRuleValuesV1.Text(input.Reason,1000))throw Error("RULE_DICTIONARY_COMMAND_INVALID",400);
        var c=OperationsOwnerV1.Freeze(input with{Definition=Validate(input.Definition)});
        if(c.ExpectedVersion!="absent")EngineeringProductValuesV1.Revision(c.ExpectedVersion);
        var bytes=MainOwnerOperationJournalV1.Encode(new{warehouse,command=c});var j=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.Warehouse,"rule-policy",true,async(a,t)=>
        {
            await Target(a,warehouse,original==null,t);var s=new LocationRuleReferencesStoreV1(db);await s.Fence(true,t);
            if(original!=null)return await Disclose(s,warehouse,a.ActorId,c.OperationId,original,bytes,t);
            var before=await s.Read(warehouse,null,t);
            if(c.ExpectedVersion!=(before?.Reference.Version??"absent"))throw Error("RULE_DICTIONARY_CHANGED",412);
            RuleReferenceUnitV1[] units;
            if(c.Definition.Enabled)units=await LocationRuleReferenceResolverV1.Units(db,a,c.Definition,t);
            else
            {
                if(before==null||!EngineeringProductStoreV1.Encode(before.Original.Definition with{Enabled=false}).AsSpan().SequenceEqual(EngineeringProductStoreV1.Encode(c.Definition)))
                    throw Error("RULE_DICTIONARY_DISABLE_ONLY",422);
                units=before.Original.Units;
            }
            var p=await s.Append(warehouse,c,a.ActorId,before,units,t);var r=new RuleReferenceResultV1(c.OperationId,warehouse,p.Reference,before?.Reference!=p.Reference);
            await j.Store(Owner,Contract,c.OperationId,a.ActorId,bytes,r,[warehouse],new{c.Reason,result=r},t);
            a.AtCommit(async last=>{await s.Fence(false,last);if((await s.Read(warehouse,null,last))?.Reference!=p.Reference)throw Error("RULE_DICTIONARY_CHANGED",412);});return r;
        },ct,actionAtFence:async t=>{original=await j.Read(Owner,c.OperationId,t);return original==null?"rule-policy":"query";});
    }
    private static async Task<RuleReferenceResultV1> Disclose(LocationRuleReferencesStoreV1 s,Guid warehouse,Guid actor,Guid operation,
        MainOwnerOperationJournalV1.Original o,byte[]? bytes,CancellationToken ct)
    {
        if(o.Actor!=actor||o.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(o.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        var r=JsonSerializer.Deserialize<RuleReferenceResultV1>(o.Result);
        if(r==null||r.OperationId!=operation||r.WarehouseId!=warehouse||!o.Objects.SequenceEqual(new[]{warehouse})
            ||(await s.Read(warehouse,EngineeringProductValuesV1.Revision(r.Reference.Version),ct))?.Reference!=r.Reference)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        if(bytes!=null&&!MainOwnerOperationJournalV1.Matches(o,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);return r;
    }
    public Task<RuleReferenceOperationV1> OperationAsync(Guid warehouse,Guid operation,bool close=false,CancellationToken ct=default)
    {
        if(warehouse==Guid.Empty||operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        var j=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.Warehouse,close?"abandon":"query",close,async(a,t)=>
        {
            await Target(a,warehouse,close&&original==null,t);var s=new LocationRuleReferencesStoreV1(db);await s.Fence(false,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId||original.Contract!=Contract||!original.Objects.SequenceEqual(new[]{warehouse}))throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                return original.State=="CLOSED"?new(operation,warehouse,"CLOSED"):new RuleReferenceOperationV1(operation,warehouse,"COMMITTED",await Disclose(s,warehouse,a.ActorId,operation,original,null,t));
            }
            if(!close)return new RuleReferenceOperationV1(operation,warehouse,"UNKNOWN");
            var r=new RuleReferenceOperationV1(operation,warehouse,"CLOSED");await j.Store(Owner,Contract,operation,a.ActorId,null,r,[warehouse],new{action="close-uncommitted-rule-dictionary",warehouse},t);return r;
        },ct,actionAtFence:async t=>{original=await j.Read(Owner,operation,t);return original!=null||!close?"query":"abandon";});
    }
}
