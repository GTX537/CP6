using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>Current authority for existing warehouse/location records and independent name maintenance.</summary>
public sealed class WarehouseOwnerServiceV1(CP6Context db,OperationsOwnerV1 owner)
{
    private const string Contract="MASTER-NAME/1";
    private OperationsOwnerV1 Owner=>owner.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private static OrderPriceCorrectionException Error(string message,int status)=>new(message,status);
    private static string Resource(string kind)=>kind switch
    {"warehouses"=>OperationsOwnerV1.Warehouse,"locations"=>OperationsOwnerV1.Location,_=>throw Error("MASTER_KIND_INVALID",400)};
    private static string JournalOwner(string kind)=>"Main.Wms.MasterName."+Resource(kind);
    private static WarehouseQueryV1 Query(WarehouseQueryV1 input,bool location)
    {
        var q=OperationsOwnerV1.Freeze(input);
        if(q.Page<1||q.PageSize is <1 or >200||(long)(q.Page-1)*q.PageSize>int.MaxValue
            ||q.WarehouseCd?.Length>10||q.LocationCd?.Length>30||q.BaseCd?.Length>10
            ||location&&(q.WarehouseType!=null||q.BaseCd!=null)||!location&&q.LocationCd!=null)
            throw Error("MASTER_QUERY_INVALID",400);
        return q;
    }
    public Task<MasterPageV1<WarehouseObservationV1>> WarehousesAsync(WarehouseQueryV1 input,CancellationToken ct=default)
    {
        var q=Query(input,false);
        return Owner.ExecuteAsync(OperationsOwnerV1.Warehouse,"query",false,async(a,t)=>
        {
            var rows=a.MasterWarehouses();
            if(!string.IsNullOrEmpty(q.WarehouseCd))rows=rows.Where(x=>x.WarehouseCd.Contains(q.WarehouseCd));
            if(q.WarehouseType.HasValue)rows=rows.Where(x=>x.WarehouseType==q.WarehouseType);
            if(q.BaseCd!=null)rows=rows.Where(x=>x.BaseCd==q.BaseCd);
            var total=await rows.CountAsync(t);var page=await rows.OrderBy(x=>x.WarehouseCd).ThenBy(x=>x.Id)
                .Skip((q.Page-1)*q.PageSize).Take(q.PageSize).ToArrayAsync(t);
            return new MasterPageV1<WarehouseObservationV1>(page.Select(a.ProjectWarehouse).ToArray(),total,q.Page,q.PageSize);
        },ct);
    }
    public Task<WarehouseObservationV1> WarehouseAsync(Guid id,CancellationToken ct=default)
        =>Owner.ExecuteAsync(OperationsOwnerV1.Warehouse,"query",false,async(a,t)=>a.ProjectWarehouse((Warehouse)await a.RequireMaster("warehouses",id,t)),ct);
    public Task<MasterPageV1<LocationObservationV1>> LocationsAsync(Guid warehouseId,WarehouseQueryV1 input,CancellationToken ct=default)
    {
        var q=Query(input,true);
        return Owner.ExecuteAsync(OperationsOwnerV1.Location,"query",false,async(a,t)=>
        {
            var source=await a.SourceAsync(OperationsOwnerV1.Warehouse,t);
            var wh=(Warehouse)await source.RequireMaster("warehouses",warehouseId,t);
            var rows=(await a.MasterLocations(t)).Where(x=>x.WarehouseCd==wh.WarehouseCd);
            if(q.WarehouseCd!=null&&q.WarehouseCd!=wh.WarehouseCd)throw Error("MASTER_QUERY_INVALID",400);
            if(!string.IsNullOrEmpty(q.LocationCd))rows=rows.Where(x=>x.LocationCd.Contains(q.LocationCd));
            var total=await rows.CountAsync(t);var page=await rows.OrderBy(x=>x.LocationCd).ThenBy(x=>x.Id)
                .Skip((q.Page-1)*q.PageSize).Take(q.PageSize).ToArrayAsync(t);
            var result=new List<LocationObservationV1>();foreach(var row in page)result.Add(await a.ProjectLocation(row,t));
            return new MasterPageV1<LocationObservationV1>(result,total,q.Page,q.PageSize);
        },ct);
    }
    public Task<LocationObservationV1> LocationAsync(Guid id,CancellationToken ct=default)
        =>Owner.ExecuteAsync(OperationsOwnerV1.Location,"query",false,async(a,t)=>await a.ProjectLocation((Location)await a.RequireMaster("locations",id,t),t),ct);

    // Legacy entity PUT conflates multiple Owners. Only the new name command can perform a name write.
    public Task RejectLegacyAsync(string kind,string action,string? code=null,bool warehouseReference=false,CancellationToken ct=default)
        =>Owner.ExecuteAsync<object>(Resource(kind),action,false,async(a,t)=>
        {
            if(code!=null)
            {
                var visible=kind=="warehouses"?await a.MasterWarehouses().AnyAsync(x=>x.WarehouseCd==code,t)
                    :warehouseReference?await (await a.SourceAsync(OperationsOwnerV1.Warehouse,t)).MasterWarehouses().AnyAsync(x=>x.WarehouseCd==code,t)
                    :await (await a.MasterLocations(t)).AnyAsync(x=>x.LocationCd==code,t);
                if(!visible)throw Error("MASTER_NOT_FOUND",404);
            }
            throw action switch
            {
                "query"=>Error("OWNER_READ_VERSION_REQUIRED",409),
                "edit"=>Error("MASTER_WRITE_VERSION_REQUIRED",409),
                "del"=>Error("MASTER_LIFECYCLE_ADOPTION_UNPROVEN",503),
                _ when kind=="locations"=>Error("INITIALIZATION_FENCE_UNSUPPORTED",422),
                _=>Error("MASTER_SITE_DICTIONARY_ADOPTION_UNPROVEN",503)
            };
        },ct);

    private sealed record NameChange(string Op,string? Value);
    private static NameChange ParseChange(string kind,JsonElement patch)
    {
        if(patch.ValueKind!=JsonValueKind.Object)throw Error("MASTER_PATCH_INVALID",400);
        var fields=patch.EnumerateObject().ToArray();
        if(fields.Any(x=>x.Name is "tenantId" or "siteId"))throw Error("TENANT_OVERRIDE_FORBIDDEN",400);
        if(fields.Any(x=>x.Name!="name")||fields.Length>1)throw Error("UNEXPECTED_FIELD",400);
        if(fields.Length==0)return new("KEEP",null);
        var name=fields[0].Value;if(name.ValueKind!=JsonValueKind.Object)throw Error("MASTER_PATCH_INVALID",400);
        var members=name.EnumerateObject().ToArray();
        if(members.Any(x=>x.Name is not("op" or "value"))||members.Select(x=>x.Name).Distinct().Count()!=members.Length
            ||!name.TryGetProperty("op",out var operation)||operation.ValueKind!=JsonValueKind.String)throw Error("MASTER_PATCH_INVALID",400);
        var op=operation.GetString();
        if(op=="CLEAR"&&kind=="locations"&&members.Length==1)return new("CLEAR",null);
        if(op!="SET"||members.Length!=2||!name.TryGetProperty("value",out var value)||value.ValueKind!=JsonValueKind.String)throw Error("MASTER_PATCH_INVALID",400);
        var text=value.GetString()!;
        if(text.Length>100||text.Any(char.IsControl)||kind=="warehouses"&&string.IsNullOrWhiteSpace(text))throw Error("MASTER_PATCH_INVALID",400);
        return new("SET",text);
    }
    private static void Locator(Guid id,Guid operation)
    {if(id==Guid.Empty||operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);}
    private static void CheckEtag(string? expected,BaseBizEntity row)
    {
        if(string.IsNullOrEmpty(expected))throw Error("IF_MATCH_REQUIRED",428);
        // Exact native version, no wildcard, weak tag or guessed integer comparison.
        if(expected!=OperationsAccessV1.MasterEtag(row))throw Error("MASTER_VERSION_CHANGED",412);
    }
    public Task<MasterNameResultV1> RenameAsync(string kind,Guid id,Guid operation,string? expected,JsonElement patch,CancellationToken ct=default)
    {
        var resource=Resource(kind);Locator(id,operation);var change=ParseChange(kind,patch.Clone());
        var input=MainOwnerOperationJournalV1.Encode(new {contract=Contract,kind,id,expected,change});
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Owner.ExecuteAsync(resource,"edit",true,async(a,t)=>
        {
            if(original!=null)
            {
                var disclosed=await Disclose(a,original,kind,id,operation,t);
                if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
                if(!MainOwnerOperationJournalV1.Matches(original,input))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
                return disclosed.Result!;
            }
            var row=await a.RequireMaster(kind,id,t);a.RequireMasterNameWrite(kind);CheckEtag(expected,row);
            var before=row is Warehouse w?w.WarehouseName:((Location)row).LocationName;
            var after=change.Op=="KEEP"?before:change.Value;
            var changed=!string.Equals(before,after,StringComparison.Ordinal);
            if(changed)
            {
                db.Attach(row);
                if(row is Warehouse wh)wh.WarehouseName=after!;else ((Location)row).LocationName=after;
                row.Modifier=a.Actor;row.ModifyDate=DateTime.Now;
                await db.SaveChangesAsync(t);
            }
            var result=new MasterNameResultV1(operation,kind,id,changed?"CHANGED":"NO_CHANGE",OperationsAccessV1.MasterEtag(row));
            await journal.Store(JournalOwner(kind),Contract,operation,a.ActorId,input,result,[id],
                new {action="name",kind,id,actor=a.ActorId,before,after,effect=result.Effect},t);
            a.AtCommit(async token=>{await a.RequireMaster(kind,id,token);});return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(JournalOwner(kind),operation,t);return original==null?"edit":"query";});
    }
    private async Task<MasterOperationV1> Disclose(OperationsAccessV1 a,MainOwnerOperationJournalV1.Original original,string kind,Guid id,Guid operation,CancellationToken ct)
    {
        if(original.Actor!=a.ActorId||original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(!original.Objects.SequenceEqual(new[]{id}))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        await a.RequireMaster(kind,id,ct);a.AtCommit(async t=>{await a.RequireMaster(kind,id,t);});
        if(original.State=="CLOSED")return new(operation,kind,id,"CLOSED");
        var result=JsonSerializer.Deserialize<MasterNameResultV1>(original.Result);
        if(original.State!="COMMITTED"||result==null||result.OperationId!=operation||result.ObjectId!=id||result.Kind!=kind
            ||result.Effect is not("CHANGED" or "NO_CHANGE"))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        // Return immutable locator/effect/version only. Private old/new names stay inside the audit.
        return new(operation,kind,id,"COMMITTED",result);
    }
    public Task<MasterOperationScopeV1> ScopeAsync(string kind,CancellationToken ct=default)
        =>Owner.ExecuteAsync(Resource(kind),"query",false,(a,t)=>Task.FromResult(new MasterOperationScopeV1(db.CurrentTenantId,a.ActorId)),ct,readOnlyAuthorityFence:true);
    public Task<MasterOperationV1> OperationAsync(string kind,Guid id,Guid operation,bool close=false,CancellationToken ct=default)
    {
        var resource=Resource(kind);Locator(id,operation);var journal=new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original=null;
        return Owner.ExecuteAsync(resource,close?"abandon":"query",close,async(a,t)=>
        {
            if(original!=null)return await Disclose(a,original,kind,id,operation,t);
            await a.RequireMaster(kind,id,t);a.AtCommit(async token=>{await a.RequireMaster(kind,id,token);});
            if(!close)return new MasterOperationV1(operation,kind,id,"UNKNOWN");
            var result=new MasterOperationV1(operation,kind,id,"CLOSED");
            await journal.Store(JournalOwner(kind),Contract,operation,a.ActorId,null,result,[id],new {action="abandon-uncommitted-name",kind,id,actor=a.ActorId},t);
            return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(JournalOwner(kind),operation,t);return original==null&&close?"abandon":"query";});
    }
}
