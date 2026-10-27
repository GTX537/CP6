using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;

namespace CP6.Core.Services.Wms;

/// <summary>Actual physical-instance observations and bounded profile writes. No execution qualification.</summary>
public sealed class ToolInstanceOwnerV1(CP6Context db,OperationsOwnerV1 owner)
{
    private const string Contract="tool.profile.v1", JournalOwner="Main.Wms.ToolInstance.profile.v1";
    internal bool Owns(CP6Context context)=>ReferenceEquals(context,db)&&owner.Owns(db);
    private OperationsOwnerV1 Authority=>Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<MasterOperationScopeV1> ScopeAsync(CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,(a,t)=>Task.FromResult(new MasterOperationScopeV1(db.CurrentTenantId,a.ActorId)),ct,readOnlyAuthorityFence:true);
    public Task<ToolInstancePageV1> ListAsync(PlateMoldSearchQuery input,CancellationToken ct=default)
    {
        var q=OperationsOwnerV1.Freeze(input);
        if(q.Page<1||q.PageSize is <1 or >100||(long)(q.Page-1)*q.PageSize>int.MaxValue||q.PlateNo?.Length>100
            ||q.PlateType?.Length>10||q.CustomerCd?.Length>20||q.ProductCd?.Length>20)throw Error("TOOL_INSTANCE_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>
        {
            var store=new ToolInstanceStoreV1(db);await store.Fence(t);var rows=a.ToolInstances();
            if(q.PlateNo!=null)rows=rows.Where(x=>x.PlateNo.Contains(q.PlateNo));
            if(q.PlateType!=null)rows=rows.Where(x=>x.PlateType==q.PlateType);
            if(q.CustomerCd!=null){a.RequireToolFieldRead("CustomerCd");rows=rows.Where(x=>x.CustomerCd==q.CustomerCd);}
            if(q.ProductCd!=null){a.RequireToolFieldRead("ProductCd");rows=rows.Where(x=>x.ProductCd==q.ProductCd);}
            if(q.Status.HasValue)rows=rows.Where(x=>x.Status==q.Status);
            var total=await rows.CountAsync(t);var ids=await rows.OrderByDescending(x=>x.CreateDate).ThenBy(x=>x.Id)
                .Skip((q.Page-1)*q.PageSize).Take(q.PageSize).Select(x=>x.Id).ToArrayAsync(t);
            var result=new List<ToolInstanceViewV1>();foreach(var id in ids)result.Add(await store.Read(a,id,null,t));
            return new ToolInstancePageV1(result.ToArray(),total,q.Page,q.PageSize);
        },ct);
    }
    public Task<ToolInstanceViewV1> ReadAsync(Guid id,string? version=null,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("TOOL_INSTANCE_ID_INVALID",400);var exact=version==null?(long?)null:Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",false,async(a,t)=>
        {var store=new ToolInstanceStoreV1(db);await store.Fence(t);return await store.Read(a,id,exact,t);},ct);
    }
    internal Task RejectLegacyAsync(string action,string? number=null,CancellationToken ct=default)
        =>Authority.ExecuteAsync<object>(OperationsOwnerV1.ToolInstance,action,false,async(a,t)=>
        {
            if(number!=null&&!await a.ToolInstances().AnyAsync(x=>x.PlateNo==number,t))throw Error("TOOL_INSTANCE_NOT_FOUND",404);
            throw Error(action=="query"?"OWNER_READ_VERSION_REQUIRED":action=="edit"?"TOOL_PROFILE_VERSION_REQUIRED":"TOOL_LIFECYCLE_ADOPTION_UNPROVEN",action is "query" or "edit"?409:503);
        },ct);
    private static ToolInstanceCommandV1 Validate(ToolInstanceCommandV1 input)
    {
        if(input==null||input.OperationId==Guid.Empty||input.Expected==null||input.Expected.Owner!=Owner||input.Expected.Id==Guid.Empty
            ||input.Expected.Digest==null||!Regex.IsMatch(input.Expected.Digest,"\\A[0-9a-f]{64}\\z")||input.Patches==null
            ||input.Patches.Length is <1 or >6||input.Patches.Any(p=>p==null)||input.Patches.Select(p=>p.Field).Distinct(StringComparer.Ordinal).Count()!=input.Patches.Length)
            throw Error("TOOL_PROFILE_INVALID",400);
        Revision(input.Expected.Version);
        var patches=input.Patches.Select(p=>
        {
            switch(p.Field)
            {
                case "colorCount": if(p.Value!=null&&(Integer(p.Value)<1||Integer(p.Value)>12))throw Error("TOOL_PROFILE_INVALID",400);break;
                case "maxShots": if(p.Value!=null&&Integer(p.Value)<1)throw Error("TOOL_PROFILE_INVALID",400);break;
                case "sizeNote":if(p.Value?.Length>40)throw Error("TOOL_PROFILE_INVALID",400);break;
                case "remarks":if(p.Value?.Length>500)throw Error("TOOL_PROFILE_INVALID",400);break;
                case "madeDate":Date(p.Value);break;
                case "madeCost":return p with{Value=Cost(p.Value)?.ToString("0.00",CultureInfo.InvariantCulture)};
                default:throw Error("TOOL_PROFILE_FIELD_FORBIDDEN",400);
            }
            return p;
        }).OrderBy(p=>p.Field,StringComparer.Ordinal).ToArray();
        return input with{Patches=patches};
    }
    private static int Integer(string value)=>int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var n)&&n.ToString(CultureInfo.InvariantCulture)==value?n:throw Error("TOOL_PROFILE_INTEGER_INVALID",400);
    private static decimal? Cost(string? value)=>value==null?null:Regex.IsMatch(value,"\\A(?:0|[1-9][0-9]{0,15})(?:\\.[0-9]{1,2})?\\z")
        &&decimal.TryParse(value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var n)?n:throw Error("TOOL_PROFILE_COST_INVALID",400);
    private static DateTime? Date(string? value)=>value==null?null:DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)
        &&d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)==value?d:throw Error("TOOL_PROFILE_DATE_INVALID",400);
    public Task<ToolInstanceResultV1> SaveAsync(Guid id,string? expectedEtag,ToolInstanceCommandV1 input,CancellationToken ct=default)
    {
        var command=Validate(OperationsOwnerV1.Freeze(input));if(command.Expected.Id!=id)throw Error("TOOL_INSTANCE_REF_INVALID",400);
        var bytes=MainOwnerOperationJournalV1.Encode(new{contract=Contract,expectedEtag,command});var journal=new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"edit",true,async(a,t)=>
        {
            var query=await a.SourceAsync(OperationsOwnerV1.ToolInstance,t);var store=new ToolInstanceStoreV1(db);await store.Fence(t,true);
            if(original!=null)return await Disclose(query,store,command.OperationId,original,bytes,t);
            var before=await store.Read(query,id,null,t);await a.RequireToolInstance(db,id,t);
            if(string.IsNullOrEmpty(expectedEtag))throw Error("IF_MATCH_REQUIRED",428);
            if(expectedEtag!=before.Etag||command.Expected!=before.Reference)throw Error("TOOL_INSTANCE_CHANGED",412);
            if(before.Source.GetProperty("status").GetInt32()==3)throw Error("TOOL_INSTANCE_DISCARDED",409);
            var row=await db.PlateMoldStocks.SingleAsync(x=>x.Id==id,t);
            foreach(var patch in command.Patches)switch(patch.Field)
            {
                case "colorCount":row.ColorCount=patch.Value==null?null:Integer(patch.Value);break;
                case "maxShots":row.MaxShots=patch.Value==null?null:Integer(patch.Value);break;
                case "sizeNote":row.SizeNote=patch.Value;break;
                case "remarks":a.RequireToolFieldWrite("Remarks");row.Remarks=patch.Value;break;
                case "madeDate":row.MadeDate=Date(patch.Value);break;
                case "madeCost":a.RequireToolFieldWrite("MadeCost");row.MadeCost=Cost(patch.Value);break;
            }
            if(db.ChangeTracker.HasChanges()){row.Modifier=a.Actor;row.ModifyDate=DateTime.Now;await db.SaveChangesAsync(t);}
            var after=await store.Read(query,id,null,t);var result=new ToolInstanceResultV1(command.OperationId,"TOOL_PROFILE",id,after.Reference.Version,after.Reference.Digest,before.Reference!=after.Reference);
            await journal.Store(JournalOwner,Contract,command.OperationId,a.ActorId,bytes,result,[id],
                new{action="physical-tool-profile",actor=a.ActorId,before=before.Reference,after=after.Reference,fields=command.Patches.Select(p=>p.Field),result.Changed},t);
            a.AtCommit(async token=>{await query.RequireToolInstance(db,id,token);await a.RequireToolInstance(db,id,token);});
            return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(JournalOwner,command.OperationId,t);return original==null?"edit":"query";});
    }
    private async Task<ToolInstanceResultV1> Disclose(OperationsAccessV1 a,ToolInstanceStoreV1 store,Guid operation,
        MainOwnerOperationJournalV1.Original original,byte[]? input,CancellationToken ct)
    {
        if(original.Actor!=a.ActorId||original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null&&!MainOwnerOperationJournalV1.Matches(original,input))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<ToolInstanceResultV1>(original.Result);
        if(original.State!="COMMITTED"||result==null||result.OperationId!=operation||result.Kind!="TOOL_PROFILE"
            ||original.Objects.Length!=1||original.Objects[0]!=result.ObjectId)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        var view=await store.Read(a,result.ObjectId,Revision(result.Version),ct);
        if(view.Reference.Digest!=result.Digest)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        return result;
    }
    public Task<ToolInstanceOperationV1> OperationAsync(Guid id,bool close,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance,"query",close,async(a,t)=>
        {
            var store=new ToolInstanceStoreV1(db);await store.Fence(t,close);var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(JournalOwner,id,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId||original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                return original.State=="CLOSED"?new ToolInstanceOperationV1(id,"CLOSED"):new(id,"COMMITTED",await Disclose(a,store,id,original,null,t));
            }
            if(!close)return new ToolInstanceOperationV1(id,"UNKNOWN");
            var result=new ToolInstanceOperationV1(id,"CLOSED");await journal.Store(JournalOwner,Contract,id,a.ActorId,null,result,[],new{action="close-uncommitted-tool-profile",actor=a.ActorId},t);return result;
        },ct,readOnlyAuthorityFence:!close);
    }
}
