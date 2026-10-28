using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.LocationRuleValuesV1;
namespace CP6.Core.Services.Wms;
public sealed class LocationRuleOwnerV1(CP6Context db,OperationsOwnerV1 owner)
{
    private OperationsOwnerV1 Authority=>owner.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private async Task Target(OperationsAccessV1 a,Guid location,CancellationToken ct)
    {await a.RequireLocationRuleTarget(location,ct);a.AtCommit(async t=>{await a.RequireLocationRuleTarget(location,t);});}
    public Task<LocationRuleScopeV1> ScopeAsync(Guid location,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.LocationRule,"query",false,async(a,t)=>{await Target(a,location,t);return new LocationRuleScopeV1(db.CurrentTenantId,a.ActorId,location);},ct);
    public Task<LocationRuleContextV1> ContextAsync(Guid location,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.LocationRule,"query",false,async(a,t)=>
        {
            await Target(a,location,t);var native=await a.RequireLocationRuleTarget(location,t);var s=new LocationRuleStoreV1(db);await s.Fence(false,t);
            var fields=a.LocationRuleFields();return new LocationRuleContextV1(new(db.CurrentTenantId,a.ActorId,location),native.LocationCd,
                native.IsInitializationQuarantined?"QUARANTINED":"LEGACY_UNVERIFIED",fields,fields.Single(x=>x.Field=="Selector").CanRead?await s.Selectors(location,t):[]);
        },ct);
    private static async Task<LocationRuleViewV1> View(OperationsAccessV1 a,LocationRuleStoreV1 s,LocationRuleStoreV1.Head h,long? version,CancellationToken ct)
    {
        var r=await s.Read(h,version,ct);var d=r.Original.Draft;var fields=a.LocationRuleFields();
        var candidate=new Dictionary<string,object?>();
        foreach(var f in fields.Where(x=>x.CanRead))switch(f.Field)
        {
            case "Selector":candidate["selectorKey"]=d.SelectorKey;break;
            case "Capacity":candidate["capacity"]=d.Capacity;break;
            case "AllowedProductTypes":candidate["allowedProductTypes"]=d.AllowedProductTypes;break;
            case "AllowedOperations":candidate["allowedOperations"]=d.AllowedOperations;break;
            case "IsPickable":candidate["isPickable"]=d.IsPickable;break;
            case "Validity":candidate["validFrom"]=d.ValidFrom;candidate["validTo"]=d.ValidTo;break;
        }
        return new(r.Reference,h.Location,h.Selector,h.State,h.Etag,candidate,fields.Where(x=>!x.CanRead).Select(x=>x.Field).ToArray(),fields.All(x=>x.CanRead)?r.Json:null);
    }
    public Task<LocationRuleViewV1> ReadAsync(Guid location,Guid id,string? version=null,CancellationToken ct=default)
    {
        long? v=version==null?null:EngineeringProductValuesV1.Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.LocationRule,"query",false,async(a,t)=>
        {await Target(a,location,t);var s=new LocationRuleStoreV1(db);await s.Fence(false,t);return await View(a,s,await s.HeadFor(location,id,t),v,t);},ct);
    }
    public Task<IReadOnlyList<LocationRuleViewV1>> ListAsync(Guid location,int page=1,CancellationToken ct=default)
    {
        if(page<1||page>int.MaxValue/50)throw Error("RULE_QUERY_INVALID");
        return Authority.ExecuteAsync<IReadOnlyList<LocationRuleViewV1>>(OperationsOwnerV1.LocationRule,"query",false,async(a,t)=>
        {
            await Target(a,location,t);var s=new LocationRuleStoreV1(db);await s.Fence(false,t);
            var heads=await s.Heads().Where(x=>x.Location==location).OrderBy(x=>x.Id).Skip((page-1)*50).Take(50).ToArrayAsync(t);
            var result=new List<LocationRuleViewV1>();foreach(var h in heads)result.Add(await View(a,s,h,null,t));return result;
        },ct);
    }
    public Task<LocationRuleResultV1> CreateAsync(Guid location,LocationRuleCreateV1 c,CancellationToken ct=default)
    {
        if(c==null)throw Error("RULE_COMMAND_REQUIRED");var draft=Draft(c.Draft);var input=new LocationRuleCreateV1(c.OperationId,OperationsOwnerV1.Freeze(draft));
        return Command(location,Guid.Empty,c.OperationId,"draft",new{location,kind="CREATE",command=input},async(a,s,t)=>
        {a.RequireLocationRuleWrite(Fields);var h=await s.Create(location,input.OperationId,a.ActorId,input.Draft,t);return(h,"CREATED");},ct);
    }
    public Task<LocationRuleResultV1> PatchAsync(Guid location,Guid id,string? etag,LocationRuleUpdateV1 input,CancellationToken ct=default)
    {
        if(input==null)throw Error("RULE_COMMAND_REQUIRED");PatchFields(input.Patch);var c=OperationsOwnerV1.Freeze(input);
        return Command(location,id,c.OperationId,"edit",new{location,id,etag,kind="PATCH",command=c},async(a,s,t)=>
        {
            var h=await Editable(s,location,id,etag,t);a.RequireLocationRuleWrite(PatchFields(c.Patch));var old=(await s.Read(h,null,t)).Original.Draft;
            var updated=Patch(old,c.Patch);var changed=!EngineeringProductStoreV1.Encode(old).AsSpan().SequenceEqual(EngineeringProductStoreV1.Encode(updated));
            return(changed?await s.Change(h,c.OperationId,a.ActorId,updated,t):h,changed?"UPDATED":"NO_CHANGE");
        },ct);
    }
    public Task<LocationRuleResultV1> WithdrawAsync(Guid location,Guid id,string? etag,LocationRuleWithdrawV1 input,CancellationToken ct=default)
    {
        if(input==null||!Text(input.Reason,1000))throw Error("RULE_WITHDRAW_REASON_REQUIRED");var c=OperationsOwnerV1.Freeze(input);
        return Command(location,id,c.OperationId,"withdraw",new{location,id,etag,kind="WITHDRAW",command=c},async(a,s,t)=>
        {var h=await Editable(s,location,id,etag,t);return(await s.Change(h,c.OperationId,a.ActorId,null,t),"WITHDRAWN");},ct);
    }
    private static async Task<LocationRuleStoreV1.Head> Editable(LocationRuleStoreV1 s,Guid location,Guid id,string? etag,CancellationToken ct)
    {
        if(string.IsNullOrEmpty(etag))throw Error("RULE_ETAG_REQUIRED",428);var h=await s.HeadFor(location,id,ct);
        if(h.Etag!=etag)throw Error("RULE_PREDECESSOR_CHANGED",412);if(h.State!="DRAFT")throw Error("RULE_PROPOSAL_NOT_EDITABLE",409);return h;
    }
    private Task<LocationRuleResultV1> Command(Guid location,Guid id,Guid operation,string action,object command,
        Func<OperationsAccessV1,LocationRuleStoreV1,CancellationToken,Task<(LocationRuleStoreV1.Head Head,string Effect)>> body,CancellationToken ct)
    {
        if(location==Guid.Empty||operation==Guid.Empty||id==Guid.Empty&&action!="draft")throw Error("RULE_IDENTITY_REQUIRED");
        var bytes=MainOwnerOperationJournalV1.Encode(command);var j=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.LocationRule,action,true,async(a,t)=>
        {
            await Target(a,location,t);await a.SourceAsync(OperationsOwnerV1.LocationRule,t);
            if(original!=null)return await Disclose(a,location,operation,original,bytes,t);
            var s=new LocationRuleStoreV1(db);await s.Fence(true,t);var (h,effect)=await body(a,s,t);var v=await s.Read(h,null,t);
            var r=new LocationRuleResultV1(operation,location,v.Reference,h.Selector,h.Etag,h.State,effect);
            await j.Store(Owner,Contract,operation,a.ActorId,bytes,r,[location,h.Id],new{action,effect,result=r},t);
            a.AtCommit(async token=>{await s.Fence(false,token);if((await s.HeadFor(location,h.Id,token)).Etag!=r.Etag)throw Error("RULE_PREDECESSOR_CHANGED",412);});return r;
        },ct,actionAtFence:async t=>{original=await j.Read(Owner,operation,t);return original==null?action:"query";});
    }
    private async Task<LocationRuleResultV1> Disclose(OperationsAccessV1 a,Guid location,Guid operation,MainOwnerOperationJournalV1.Original o,byte[]? bytes,CancellationToken ct)
    {
        if(o.Actor!=a.ActorId||o.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(o.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        var r=JsonSerializer.Deserialize<LocationRuleResultV1>(o.Result);
        if(r==null||r.OperationId!=operation||r.LocationId!=location||r.Reference.Owner!=Owner||!o.Objects.SequenceEqual(new[]{location,r.Reference.Id}))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        await a.RequireLocationRuleTarget(location,ct);if(bytes!=null&&!MainOwnerOperationJournalV1.Matches(o,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);return r;
    }
    public Task<LocationRuleOperationV1> OperationAsync(Guid location,Guid operation,bool close=false,CancellationToken ct=default)
    {
        if(location==Guid.Empty||operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED");var j=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.LocationRule,close?"abandon":"query",close,async(a,t)=>
        {
            await Target(a,location,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId||original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State!="CLOSED")return new LocationRuleOperationV1(operation,location,"COMMITTED",await Disclose(a,location,operation,original,null,t));
                var closed=JsonSerializer.Deserialize<LocationRuleOperationV1>(original.Result);if(closed?.LocationId!=location||closed.OperationId!=operation)throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);return closed;
            }
            if(!close)return new LocationRuleOperationV1(operation,location,"UNKNOWN");
            var r=new LocationRuleOperationV1(operation,location,"CLOSED");await j.Store(Owner,Contract,operation,a.ActorId,null,r,[location],new{action="close-uncommitted-rule-proposal",location},t);return r;
        },ct,actionAtFence:async t=>{original=await j.Read(Owner,operation,t);return original!=null||!close?"query":"abandon";});
    }
}
