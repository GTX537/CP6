using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Space;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;
public sealed class LocationDraftOwnerV1(CP6Context db,OperationsOwnerV1 owner)
{
    internal const string JournalOwner="Main.Wms.LocationDraft.v1",Contract="LOCATION-DRAFT/1";
    internal const string Capability="Main.Wms.LocationInitialization.DraftWarehouse.v1";
    private OperationsOwnerV1 Authority=>owner.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private static OrderPriceCorrectionException Error(string code,int status=422)=>new(code,status);
    private static string Rev(long n)=>n.ToString(CultureInfo.InvariantCulture);
    private sealed class WarehouseData {public Guid Site {get;init;}public string SiteRef {get;init;}="";}
    private sealed class Data
    {
        public Guid Id {get;init;}public Guid Warehouse {get;init;}public Guid? Parent {get;init;}public long Revision {get;init;}
    }
    private IQueryable<Data> Rows()=>db.Database.SqlQuery<Data>($"""
        SELECT id AS "Id",warehouse_id AS "Warehouse",parent_id AS "Parent",business_revision AS "Revision"
        FROM cp6_location_draft_v1.locations WHERE tenant_id={db.CurrentTenantId}
        """);
    private async Task<(Warehouse Native,EngineeringSourceRefV1 Site)> Warehouse(OperationsAccessV1 a,Guid id,CancellationToken ct)
    {
        var source=await a.SourceAsync(OperationsOwnerV1.Warehouse,ct);var w=(Warehouse)await source.RequireMaster("warehouses",id,ct);
        var m=await db.Database.SqlQuery<WarehouseData>($"""
            SELECT site_id AS "Site",site_ref_json AS "SiteRef" FROM cp6_warehouse_draft_v1.warehouses
            WHERE tenant_id={db.CurrentTenantId} AND id={id} AND lifecycle='DRAFT'
            """).SingleOrDefaultAsync(ct)??throw Error("INITIALIZATION_FENCE_UNSUPPORTED");
        var reference=JsonSerializer.Deserialize<EngineeringSourceRefV1>(m.SiteRef);
        if(reference==null||reference.Id!=m.Site||reference.Owner!=EngineeringProductValuesV1.SiteOwner)throw Error("WAREHOUSE_DRAFT_UNAVAILABLE",503);
        (await a.SourceAsync(OperationsOwnerV1.PhysicalSite,ct)).RequirePhysicalSiteSourceAccess(db);
        a.AtCommit(async t=>{await source.RequireMaster("warehouses",id,t);});
        return(w,reference);
    }
    private async Task Fence(CancellationToken ct)
    {
        // This model is part of the capability: all ordinary legacy Location reads exclude quarantine.
        var filter=db.Model.FindEntityType(typeof(Location))?.GetQueryFilter();
        if(filter?.Body is not BinaryExpression {NodeType:ExpressionType.AndAlso,Right:UnaryExpression
            {NodeType:ExpressionType.Not,Operand:MemberExpression {Member.Name:var name}}}||name!=nameof(Location.IsInitializationQuarantined))
            throw Error("INITIALIZATION_FENCE_UNSUPPORTED");
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_warehouse_draft_v1.warehouses,cp6_location_draft_v1.locations IN SHARE MODE",ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_location_draft_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("INITIALIZATION_FENCE_UNSUPPORTED");
    }
    public Task<LocationDraftScopeV1> ScopeAsync(Guid warehouse,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.Location,"query",false,async(a,t)=>
        {await Warehouse(a,warehouse,t);return new LocationDraftScopeV1(db.CurrentTenantId,a.ActorId,warehouse);},ct);
    public Task<LocationDraftContextV1> ContextAsync(Guid warehouse,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.Location,"add",false,async(a,t)=>
        {
            await a.SourceAsync(OperationsOwnerV1.Location,t);
            var w=await Warehouse(a,warehouse,t);a.RequireWarehouseHierarchyControl(w.Native);
            var creation=await a.LocationDraftCreationContext(t);await Fence(t);
            var s=new LocationHierarchyStoreV1(db);await s.Fence(false,t);
            var p=await s.Read(warehouse,null,t)??throw Error("HIERARCHY_POLICY_UNAVAILABLE");
            if(!p.Original.Enabled)throw Error("HIERARCHY_POLICY_DISABLED");
            if(!await s.Compatible(w.Native,p.Original.RootLevels,p.Original.Edges,t))throw Error("HIERARCHY_CURRENT_TREE_INCOMPATIBLE",409);
            return new LocationDraftContextV1(new(db.CurrentTenantId,a.ActorId,warehouse),creation,w.Site,p,Rev((await s.HeadFor(warehouse,t))!.Hierarchy),Capability);
        },ct);
    private async Task<LocationDraftViewV1> Read(OperationsAccessV1 a,Guid warehouse,Guid id,CancellationToken ct)
    {
        var w=await Warehouse(a,warehouse,ct);var x=await a.RequireLocationDraft(id,ct);
        var m=await Rows().SingleOrDefaultAsync(x=>x.Id==id&&x.Warehouse==warehouse,ct)??throw Error("LOCATION_DRAFT_NOT_FOUND",404);
        Guid? parent=null;
        if(m.Parent is Guid p && await (await a.MasterLocations(ct,true)).AnyAsync(x=>x.Id==p,ct))parent=p;
        a.AtCommit(async t=>{await a.RequireLocationDraft(id,t);});
        return new(id,warehouse,w.Native.WarehouseCd,x.LocationCd,a.LocationNameReadable?x.LocationName:null,x.LocationLevel,
            parent,m.Parent==null?"ROOT_RECORD":parent!=null?"AUTHORIZED_ORIGINAL_REFERENCE":"SOURCE_DISCLOSURE_UNAVAILABLE",OperationsAccessV1.MasterEtag(x),Rev(m.Revision),w.Site,a.LocationNameReadable,a.LocationNameWritable);
    }
    public Task<LocationDraftViewV1> ReadAsync(Guid warehouse,Guid id,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.Location,"query",false,(a,t)=>Read(a,warehouse,id,t),ct);
    public Task<IReadOnlyList<LocationDraftViewV1>> ListAsync(Guid warehouse,int page=1,CancellationToken ct=default)
    {
        if(page<1||page>int.MaxValue/50)throw Error("MASTER_QUERY_INVALID",400);
        return Authority.ExecuteAsync<IReadOnlyList<LocationDraftViewV1>>(OperationsOwnerV1.Location,"query",false,async(a,t)=>
        {
            await Warehouse(a,warehouse,t);var ids=Rows().Where(x=>x.Warehouse==warehouse).Select(x=>x.Id);
            var selected=await (await a.MasterLocations(t,true)).Where(x=>ids.Contains(x.Id)).OrderBy(x=>x.LocationCd).ThenBy(x=>x.Id)
                .Skip((page-1)*50).Take(50).Select(x=>x.Id).ToArrayAsync(t);
            var result=new List<LocationDraftViewV1>();foreach(var id in selected)result.Add(await Read(a,warehouse,id,t));return result;
        },ct);
    }
    private static string Key(string value)=>value.Normalize(NormalizationForm.FormC).Trim().ToUpperInvariant();
    private static string Code(string value)
    {
        if(value==null||value.Any(char.IsControl))throw Error("LOCATION_CODE_INVALID",400);
        string code;try{code=value.Normalize(NormalizationForm.FormC).Trim();}catch(ArgumentException){throw Error("LOCATION_CODE_INVALID",400);}
        if(code.Length is <1 or >30||Key(code).Length>300)throw Error("LOCATION_CODE_INVALID",400);return code;
    }
    private static bool Text(string? text,int max)=>text==null||text.Length<=max&&!text.Any(char.IsControl);
    public Task<LocationDraftResultV1> CreateAsync(Guid warehouse,LocationDraftCommandV1 input,CancellationToken ct=default)
    {
        if(warehouse==Guid.Empty||input==null||input.OperationId==Guid.Empty||input.ParentLocationId==Guid.Empty
            ||string.IsNullOrWhiteSpace(input.AuthorityVersion)||input.LocationLevel is <1 or >5
            ||!Text(input.LocationName,100)||!Text(input.Barcode,50))throw Error("LOCATION_DRAFT_COMMAND_INVALID",400);
        EngineeringProductValuesV1.Revision(input.ExpectedPolicyVersion);EngineeringProductValuesV1.Revision(input.ExpectedHierarchyRevision);
        var c=OperationsOwnerV1.Freeze(input) with{LocationCode=Code(input.LocationCode)};
        var bytes=MainOwnerOperationJournalV1.Encode(new{warehouse,command=c});var j=new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.Location,"add",true,async(a,t)=>
        {
            if(original!=null)return await Disclose(a,warehouse,original,c.OperationId,bytes,t);
            var query=await a.SourceAsync(OperationsOwnerV1.Location,t);var w=await Warehouse(a,warehouse,t);
            a.RequireLocationDraftFields(c,w.Native);if(c.Barcode!=null)throw Error("LOCATION_BARCODE_SOURCE_UNAVAILABLE",503);
            var ticket=await a.BeginLocationDraftCreation(c.AuthorityVersion,c.DepartmentId,t);
            var siteSource=await a.SourceAsync(OperationsOwnerV1.PhysicalSite,t);
            var site=await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,siteSource,w.Site.Id,t);
            if(!site.Enabled)throw Error("PHYSICAL_SITE_UNAVAILABLE");
            await Fence(t);var s=new LocationHierarchyStoreV1(db);await s.Fence(true,t);
            var p=await s.Read(warehouse,null,t)??throw Error("HIERARCHY_POLICY_UNAVAILABLE");var h=(await s.HeadFor(warehouse,t))!;
            if(p.Reference.Version!=c.ExpectedPolicyVersion||Rev(h.Hierarchy)!=c.ExpectedHierarchyRevision)throw Error("HIERARCHY_POLICY_CHANGED",412);
            if(!p.Original.Enabled)throw Error("HIERARCHY_POLICY_DISABLED");
            if(!await s.Compatible(w.Native,p.Original.RootLevels,p.Original.Edges,t))throw Error("HIERARCHY_CURRENT_TREE_INCOMPATIBLE",409);
            Location? parent=null;
            if(c.ParentLocationId is Guid id)
            {
                parent=await query.RequireLocationDraft(id,t);
                if(!await Rows().AnyAsync(x=>x.Id==id&&x.Warehouse==warehouse,t)||!p.Original.Edges.Contains(new(parent.LocationLevel,c.LocationLevel)))throw Error("HIERARCHY_EDGE_FORBIDDEN");
                a.AtCommit(async token=>{await query.RequireLocationDraft(id,token);});
            }
            else if(!p.Original.RootLevels.Contains(c.LocationLevel))throw Error("HIERARCHY_ROOT_FORBIDDEN");
            var anchors=db.Locations.IgnoreQueryFilters().Where(x=>x.TenantId==db.CurrentTenantId&&x.WarehouseCd==w.Native.WarehouseCd);
            var key=Key(c.LocationCode);
            if((await anchors.Select(x=>x.LocationCd).ToArrayAsync(t)).Any(x=>Key(x)==key)||await anchors.AnyAsync(x=>x.LocationCd==c.LocationCode,t))throw Error("DUPLICATE_CODE",409);
            if(!await db.Database.SqlQuery<bool>($"SELECT cp6_location_draft_v1.reserve_code({db.CurrentTenantId},{c.LocationCode}) AS \"Value\"").SingleAsync(t))
                throw Error("LOCATION_CODE_HAS_LEGACY_REFERENCES");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_location_draft_v1.locations(tenant_id,id,warehouse_id,parent_id,code_key,lifecycle,source_kind,
                  initialization_state,provenance,legacy_provenance,business_revision,control_revision,policy_id,policy_revision,
                  hierarchy_before,operation_id,actor_id,fence_capability)
                VALUES({db.CurrentTenantId},{ticket.Id},{warehouse},{(parent==null?(Guid?)null:parent.Id)},{key},'DRAFT','MANUAL',
                  'QUARANTINED','MANUAL_CREATE','NEW_TECHNICAL_PLACEHOLDER',1,1,{p.Reference.Id},{h.Revision},
                  {h.Hierarchy},{c.OperationId},{a.ActorId},{Capability})
                """,t);
            var row=new Location{Id=ticket.Id,WarehouseCd=w.Native.WarehouseCd,LocationCd=c.LocationCode,LocationName=c.LocationName,
                ParentLocationCd=parent?.LocationCd,LocationLevel=c.LocationLevel,IsInitializationQuarantined=true,
                IsBlocked=true,IsPickable=false,CapacityQty=0,ReservedCapacityQty=0,Creator=a.Actor};
            db.Locations.Add(row);await db.SaveChangesAsync(t);await a.CompleteLocationDraftCreation(ticket,t);
            await query.RequireLocationDraft(row.Id,t);
            var revision=(await s.HeadFor(warehouse,t))!.Hierarchy;
            if(revision!=h.Hierarchy+1)throw Error("HIERARCHY_POLICY_CHANGED",412);
            var r=new LocationDraftResultV1(c.OperationId,warehouse,row.Id,w.Site.Id,"1",Rev(revision),OperationsAccessV1.MasterEtag(row));
            await j.Store(JournalOwner,Contract,c.OperationId,a.ActorId,bytes,r,[row.Id],new{action="create-quarantined-location",site.Reference,policy=p.Reference,Capability,result=r},t);
            a.AtCommit(async token=>
            {
                await Fence(token);await s.Fence(false,token);
                if((await s.Read(warehouse,null,token))?.Reference!=p.Reference||(await s.HeadFor(warehouse,token))?.Hierarchy!=revision
                    ||(await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db,siteSource,w.Site.Id,token)).Reference!=site.Reference)
                    throw Error("CREATION_PREDECESSOR_CHANGED",412);
                if(OperationsAccessV1.MasterEtag(await query.RequireLocationDraft(row.Id,token))!=r.Etag)throw Error("MASTER_VERSION_CHANGED",412);
            });return r;
        },ct,actionAtFence:async t=>{original=await j.Read(JournalOwner,c.OperationId,t);return original==null?"add":"query";});
    }
    private async Task<LocationDraftResultV1> Disclose(OperationsAccessV1 a,Guid warehouse,MainOwnerOperationJournalV1.Original o,Guid operation,byte[]? bytes,CancellationToken ct)
    {
        if(o.Actor!=a.ActorId||o.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(o.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        var r=JsonSerializer.Deserialize<LocationDraftResultV1>(o.Result);
        if(r==null||r.OperationId!=operation||r.WarehouseId!=warehouse||!o.Objects.SequenceEqual(new[]{r.LocationId}))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        await Read(a,warehouse,r.LocationId,ct);
        if(bytes!=null&&!MainOwnerOperationJournalV1.Matches(o,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);return r;
    }
    public Task<LocationDraftOperationV1> OperationAsync(Guid warehouse,Guid operation,bool close=false,CancellationToken ct=default)
    {
        if(warehouse==Guid.Empty||operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        var j=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.Location,close?"abandon":"query",close,async(a,t)=>
        {
            await Warehouse(a,warehouse,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId||original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                if(original.State!="CLOSED")return new LocationDraftOperationV1(operation,warehouse,"COMMITTED",await Disclose(a,warehouse,original,operation,null,t));
                var closed=JsonSerializer.Deserialize<LocationDraftOperationV1>(original.Result);
                if(closed?.WarehouseId!=warehouse||closed.OperationId!=operation)throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);return closed;
            }
            if(!close)return new LocationDraftOperationV1(operation,warehouse,"UNKNOWN");
            var r=new LocationDraftOperationV1(operation,warehouse,"CLOSED");await j.Store(JournalOwner,Contract,operation,a.ActorId,null,r,[],new{action="close-uncommitted-location-draft",warehouse},t);return r;
        },ct,actionAtFence:async t=>{original=await j.Read(JournalOwner,operation,t);return original!=null||!close?"query":"abandon";});
    }
}
