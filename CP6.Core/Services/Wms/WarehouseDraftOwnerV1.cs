using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Space;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public sealed class WarehouseDraftOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    internal const string JournalOwner = "Main.Wms.WarehouseDraft.v1", Contract = "WAREHOUSE-DRAFT/1";
    private const string TypeOwner = "Main.Wms.NativeWarehouseType.v1", TypeVersion = "1";
    private static readonly WarehouseDraftTypeV1[] Types = [new(WarehouseType.RawMaterial,"原材料"),
        new(WarehouseType.WorkInProcess,"半成品"),new(WarehouseType.Finished,"成品"),
        new(WarehouseType.Defective,"不良品"),new(WarehouseType.External,"外协")];
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private static OrderPriceCorrectionException Error(string code,int status=422)=>new(code,status);
    public Task<MasterOperationScopeV1> ScopeAsync(CancellationToken ct=default)
        => Authority.ExecuteAsync(OperationsOwnerV1.Warehouse,"query",false,
            (a,t)=>Task.FromResult(new MasterOperationScopeV1(db.CurrentTenantId,a.ActorId)),ct);
    public Task<WarehouseDraftContextV1> ContextAsync(CancellationToken ct=default)
        => Authority.ExecuteAsync(OperationsOwnerV1.Warehouse,"add",false,async(a,t)=>
        {
            var creation=await a.WarehouseDraftCreationContext(t);
            var source=await a.SourceAsync(OperationsOwnerV1.PhysicalSite,t);source.RequirePhysicalSiteSourceAccess(db);
            var ids=await db.Space_Sites.AsNoTracking().Where(s=>s.Enable&&!s.IsDeleted).OrderBy(s=>s.SiteCode).Select(s=>s.Id).ToArrayAsync(t);
            var sites=new List<EngineeringSiteChoiceV1>();
            foreach(var id in ids)sites.Add(await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,source,id,t));
            return new WarehouseDraftContextV1(creation,sites,TypeOwner,TypeVersion,Types,"OPTIONAL_BASE_MANAGER_SOURCE_UNAVAILABLE");
        },ct);
    private sealed class Metadata
    {
        public Guid Id {get;init;}
        public Guid Site {get;init;}
        public string SiteRef {get;init;}="";
        public long Revision {get;init;}
    }
    private IQueryable<Metadata> MetadataRows()=>db.Database.SqlQuery<Metadata>($"""
        SELECT id AS "Id", site_id AS "Site", site_ref_json AS "SiteRef", business_revision AS "Revision"
        FROM cp6_warehouse_draft_v1.warehouses WHERE tenant_id={db.CurrentTenantId}
        """);
    private async Task<WarehouseDraftViewV1> Read(OperationsAccessV1 a,Guid id,CancellationToken ct)
    {
        var row=(Warehouse)await a.RequireMaster("warehouses",id,ct);
        var m=await MetadataRows().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw Error("WAREHOUSE_DRAFT_NOT_FOUND",404);
        (await a.SourceAsync(OperationsOwnerV1.PhysicalSite,ct)).RequirePhysicalSiteSourceAccess(db);
        var reference=JsonSerializer.Deserialize<EngineeringSourceRefV1>(m.SiteRef)??throw Error("WAREHOUSE_DRAFT_UNAVAILABLE",503);
        if(reference.Owner!=EngineeringProductValuesV1.SiteOwner || reference.Id!=m.Site)throw Error("WAREHOUSE_DRAFT_UNAVAILABLE",503);
        a.AtCommit(async t=>{await a.RequireMaster("warehouses",id,t);});
        return new(a.ProjectWarehouse(row),reference,"DRAFT",m.Revision.ToString(CultureInfo.InvariantCulture),TypeOwner,TypeVersion);
    }
    public Task<WarehouseDraftViewV1> ReadAsync(Guid id,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.Warehouse,"query",false,(a,t)=>Read(a,id,t),ct);
    public Task<IReadOnlyList<WarehouseDraftViewV1>> ListAsync(int page=1,CancellationToken ct=default)
    {
        if(page<1 || page>int.MaxValue/50)throw Error("MASTER_QUERY_INVALID",400);
        return Authority.ExecuteAsync<IReadOnlyList<WarehouseDraftViewV1>>(OperationsOwnerV1.Warehouse,"query",false,async(a,t)=>
        {
            var ids=MetadataRows().Select(x=>x.Id);
            var selected=await a.MasterWarehouses().Where(x=>ids.Contains(x.Id)).OrderBy(x=>x.WarehouseCd).ThenBy(x=>x.Id)
                .Skip((page-1)*50).Take(50).Select(x=>x.Id).ToArrayAsync(t);
            var result=new List<WarehouseDraftViewV1>();foreach(var id in selected)result.Add(await Read(a,id,t));return result;
        },ct);
    }
    private static string Code(string value)
    {
        if(value==null || value.Any(char.IsControl))throw Error("WAREHOUSE_CODE_INVALID",400);
        string code;try{code=value.Normalize(NormalizationForm.FormC).Trim();}catch(ArgumentException){throw Error("WAREHOUSE_CODE_INVALID",400);}
        if(code.Length is <1 or >10 || Key(code).Length>100)throw Error("WAREHOUSE_CODE_INVALID",400);return code;
    }
    private static string Key(string value)=>value.Normalize(NormalizationForm.FormC).Trim().ToUpperInvariant();
    private static bool Text(string? value,int limit,bool multiline=false)=>value==null || value.Length<=limit
        && !value.Any(c=>char.IsControl(c)&&!(multiline&&c is '\r' or '\n' or '\t'));
    public Task<WarehouseDraftResultV1> CreateAsync(WarehouseDraftCommandV1 input,CancellationToken ct=default)
    {
        if(input==null || input.OperationId==Guid.Empty || input.SiteId==Guid.Empty || string.IsNullOrWhiteSpace(input.AuthorityVersion)
            || string.IsNullOrWhiteSpace(input.WarehouseName) || !Text(input.WarehouseName,100)
            || !Text(input.BaseCode,10) || !Text(input.ManagerCode,20) || !Text(input.Address,200) || !Text(input.Remarks,500,true))
            throw Error("WAREHOUSE_DRAFT_COMMAND_INVALID",400);
        var c=OperationsOwnerV1.Freeze(input) with{WarehouseCode=Code(input.WarehouseCode)};
        var bytes=MainOwnerOperationJournalV1.Encode(c);var journal=new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.Warehouse,"add",true,async(a,t)=>
        {
            if(original!=null)return await Disclose(a,original,c.OperationId,bytes,t);
            a.RequireWarehouseCreateFields(c);
            if(!Types.Any(x=>x.Code==c.WarehouseType))throw Error("WAREHOUSE_TYPE_UNAVAILABLE");
            if(c.BaseCode!=null || c.ManagerCode!=null)throw Error("WAREHOUSE_RELATION_SOURCE_UNAVAILABLE",503);
            var ticket=await a.BeginWarehouseDraftCreation(c.AuthorityVersion,c.DepartmentId,t);
            var source=await a.SourceAsync(OperationsOwnerV1.PhysicalSite,t);
            var site=await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,source,c.SiteId,t);
            if(!site.Enabled)throw Error("PHYSICAL_SITE_UNAVAILABLE");
            // OperationsOwner holds the actual native table write fence. Legacy and deleted anchors
            // participate without rewriting them or truncating Unicode normalization expansions.
            var key=Key(c.WarehouseCode);
            var codes=await db.Warehouses.IgnoreQueryFilters().Where(x=>x.TenantId==db.CurrentTenantId).Select(x=>x.WarehouseCd).ToArrayAsync(t);
            if(codes.Any(x=>Key(x)==key) || await db.Warehouses.IgnoreQueryFilters().AnyAsync(x=>x.TenantId==db.CurrentTenantId && x.WarehouseCd==c.WarehouseCode,t))throw Error("DUPLICATE_CODE",409);
            if (!await db.Database.SqlQuery<bool>($"SELECT cp6_warehouse_draft_v1.reserve_code({db.CurrentTenantId},{c.WarehouseCode}) AS \"Value\"").SingleAsync(t))
                throw Error("WAREHOUSE_CODE_HAS_LEGACY_REFERENCES",409);
            var w=new Warehouse{Id=ticket.Id,WarehouseCd=c.WarehouseCode,WarehouseName=c.WarehouseName,
                WarehouseType=c.WarehouseType,AddressText=c.Address,Remarks=c.Remarks,Creator=a.Actor};
            db.Warehouses.Add(w);await db.SaveChangesAsync(t);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_warehouse_draft_v1.warehouses
                (tenant_id,id,site_id,site_ref_json,code,code_key,lifecycle,business_revision,operation_id,actor_id)
                VALUES({db.CurrentTenantId},{w.Id},{c.SiteId},{JsonSerializer.Serialize(site.Reference)},
                  {w.WarehouseCd},{key},'DRAFT',1,{c.OperationId},{a.ActorId})
                """,t);
            await a.CompleteWarehouseDraftCreation(ticket,t);
            var result=new WarehouseDraftResultV1(c.OperationId,w.Id,c.SiteId,"1",OperationsAccessV1.MasterEtag(w));
            await journal.Store(JournalOwner,Contract,c.OperationId,a.ActorId,bytes,result,[w.Id],new{action="create-draft",site.Reference,TypeOwner,TypeVersion,result},t);
            a.AtCommit(async token=>
            {
                if((await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,source,c.SiteId,token)).Reference!=site.Reference)
                    throw Error("PHYSICAL_SITE_CHANGED",412);
                var current=await MetadataRows().SingleAsync(x=>x.Id==w.Id,token);
                var native=(Warehouse)await a.RequireMaster("warehouses",w.Id,token);
                if(current.Revision!=1 || OperationsAccessV1.MasterEtag(native)!=result.Etag)
                    throw Error("MASTER_VERSION_CHANGED",412);
            });
            return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(JournalOwner,c.OperationId,t);return original==null?"add":"query";});
    }
    private async Task<WarehouseDraftResultV1> Disclose(OperationsAccessV1 a,MainOwnerOperationJournalV1.Original o,Guid operation,byte[]? bytes,CancellationToken ct)
    {
        if(o.Actor!=a.ActorId || o.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(o.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        var r=JsonSerializer.Deserialize<WarehouseDraftResultV1>(o.Result);
        if(r==null || r.OperationId!=operation || !o.Objects.SequenceEqual(new[]{r.WarehouseId}))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        await Read(a,r.WarehouseId,ct);
        if(bytes!=null&&!MainOwnerOperationJournalV1.Matches(o,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        return r;
    }
    public Task<WarehouseDraftOperationV1> OperationAsync(Guid operation,bool close=false,CancellationToken ct=default)
    {
        if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.Warehouse,close?"abandon":"query",close,async(a,t)=>
        {
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                return original.State=="CLOSED"?new(operation,"CLOSED"):new WarehouseDraftOperationV1(operation,"COMMITTED",await Disclose(a,original,operation,null,t));
            }
            if(!close)return new WarehouseDraftOperationV1(operation,"UNKNOWN");
            var result=new WarehouseDraftOperationV1(operation,"CLOSED");
            await journal.Store(JournalOwner,Contract,operation,a.ActorId,null,result,[],new{action="close-uncommitted-warehouse-draft"},t);return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(JournalOwner,operation,t);return original!=null||!close?"query":"abandon";});
    }
}
