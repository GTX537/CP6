using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal IQueryable<Warehouse> MasterWarehouses()
    {
        var rows=db.Warehouses.AsNoTracking().Where(x=>x.TenantId==db.CurrentTenantId && !x.IsDeleted);
        if(authority.Scope==1)rows=rows.Where(x=>x.Creator==Actor);
        else if(authority.Scope!=5)
        {
            var ids=StandaloneDepartmentScopeV1.Objects(db,authority,"warehouse");rows=rows.Where(x=>ids.Contains(x.Id));
        }
        if(!authority.Actor.RoleIds.Contains(1))
        {
            var grants=db.WmsRoleScopes.Where(g=>g.TenantId==db.CurrentTenantId && authority.Actor.RoleIds.Contains(g.RoleId));
            // Warehouse identity metadata does not assert visibility of every child area or stock.
            rows=rows.Where(x=>grants.Any(g=>g.WarehouseCd=="*"||g.WarehouseCd==x.WarehouseCd));
        }
        return rows;
    }
    internal async Task<IQueryable<Location>> MasterLocations(CancellationToken ct,bool includeInitialization=false)
    {
        var source=await SourceAsync(OperationsOwnerV1.Warehouse,ct);
        var warehouses=source.MasterWarehouses();
        // IgnoreQueryFilters propagates into subqueries: tenant predicates on warehouses,
        // WMS grants and department mappings are deliberately explicit as well.
        var native=includeInitialization?db.Locations.IgnoreQueryFilters():db.Locations;
        var rows=native.AsNoTracking().Where(x=>x.TenantId==db.CurrentTenantId && !x.IsDeleted && warehouses.Any(w=>w.WarehouseCd==x.WarehouseCd));
        if(authority.Scope==1)rows=rows.Where(x=>x.Creator==Actor);
        else if(authority.Scope!=5)
        {
            var ids=StandaloneDepartmentScopeV1.Objects(db,authority,"location");rows=rows.Where(x=>ids.Contains(x.Id));
        }
        if(!authority.Actor.RoleIds.Contains(1))
        {
            var grants=db.WmsRoleScopes.Where(g=>g.TenantId==db.CurrentTenantId && authority.Actor.RoleIds.Contains(g.RoleId));
            rows=rows.Where(x=>grants.Any(g=>(g.WarehouseCd=="*"||g.WarehouseCd==x.WarehouseCd)
                &&(g.AreaCd==null||g.AreaCd==""||g.AreaCd=="*"||g.AreaCd==x.AreaCd)));
        }
        return rows;
    }
    internal async Task<BaseBizEntity> RequireMaster(string kind,Guid id,CancellationToken ct)
        =>kind=="warehouses"?(BaseBizEntity?)await MasterWarehouses().SingleOrDefaultAsync(x=>x.Id==id,ct)
            ??throw new OrderPriceCorrectionException("MASTER_NOT_FOUND",404)
            :await (await MasterLocations(ct)).SingleOrDefaultAsync(x=>x.Id==id,ct)
                ??throw new OrderPriceCorrectionException("MASTER_NOT_FOUND",404);
    internal async Task<Location> RequireLocationDraft(Guid id,CancellationToken ct)
        =>await (await MasterLocations(ct,true)).SingleOrDefaultAsync(x=>x.Id==id&&x.IsInitializationQuarantined,ct)
            ??throw new OrderPriceCorrectionException("LOCATION_DRAFT_NOT_FOUND",404);
    internal async Task<BaseBizEntity> RequireMasterNameTarget(string kind,Guid id,CancellationToken ct)
        =>kind=="warehouses"?await RequireMaster(kind,id,ct)
            :await (await MasterLocations(ct,true)).SingleOrDefaultAsync(x=>x.Id==id,ct)
                ??throw new OrderPriceCorrectionException("MASTER_NOT_FOUND",404);
    internal void RequireLocationDraftFields(LocationDraftCommandV1 c,Warehouse warehouse)
    {
        RequireWarehouseHierarchyControl(warehouse);
        foreach(var field in new[]{c.LocationName!=null?"LocationName":null,c.Barcode!=null?"Barcode":null}.OfType<string>())
            if(!authority.Fields.Fields[field].CanWrite)throw new OrderPriceCorrectionException("FIELD_WRITE_FORBIDDEN",403);
    }
    internal bool LocationNameReadable=>MasterRead("LocationName");
    internal bool LocationNameWritable=>authority.Fields.Fields["LocationName"].CanWrite;
    internal void RequireWarehouseCreateFields(WarehouseDraftCommandV1 c)
    {
        foreach (var field in new[] { "WarehouseName", c.Address != null ? "AddressText" : null,
            c.ManagerCode != null ? "ManagerCd" : null, c.Remarks != null ? "Remarks" : null }.OfType<string>())
            if (!authority.Fields.Fields[field].CanWrite) throw new OrderPriceCorrectionException("FIELD_WRITE_FORBIDDEN", 403);
        if (!authority.Actor.RoleIds.Contains(1) && !db.WmsRoleScopes.Any(g => authority.Actor.RoleIds.Contains(g.RoleId)
            && (g.WarehouseCd == "*" || g.WarehouseCd == c.WarehouseCode)))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
    }
    internal void RequireWarehouseHierarchyControl(Warehouse warehouse)
    {
        if(!authority.Actor.RoleIds.Contains(1) && !db.WmsRoleScopes.Any(g=>authority.Actor.RoleIds.Contains(g.RoleId)
            && (g.WarehouseCd=="*"||g.WarehouseCd==warehouse.WarehouseCd)
            && (g.AreaCd==null||g.AreaCd==""||g.AreaCd=="*")))
            throw new OrderPriceCorrectionException("HIERARCHY_WAREHOUSE_SCOPE_REQUIRED",403);
    }
    internal void RequireMasterNameWrite(string kind)
    {
        if(!authority.Fields.Fields[kind=="warehouses"?"WarehouseName":"LocationName"].CanWrite)
            throw new OrderPriceCorrectionException("FIELD_WRITE_FORBIDDEN",403);
    }
    internal static string MasterEtag(BaseBizEntity row)
        =>row.RowVersion is {Length:8}?"\""+Convert.ToBase64String(row.RowVersion)+"\"":throw new OrderPriceCorrectionException("MASTER_VERSION_UNAVAILABLE",503);
    private bool MasterRead(string field)=>authority.Fields.Fields[field].CanRead;
    internal WarehouseObservationV1 ProjectWarehouse(Warehouse x)=>new(x.Id,x.WarehouseCd,
        MasterRead("WarehouseName")?x.WarehouseName:null,x.WarehouseType,x.BaseCd,
        MasterRead("AddressText")?x.AddressText:null,MasterRead("ManagerCd")?x.ManagerCd:null,MasterRead("Remarks")?x.Remarks:null,
        x.AllowNegative,x.OutboundPriority,MasterEtag(x),MasterRead("WarehouseName"),authority.Fields.Fields["WarehouseName"].CanWrite,
        MasterRead("AddressText"),MasterRead("ManagerCd"),MasterRead("Remarks"));
    internal async Task<LocationObservationV1> ProjectLocation(Location x,CancellationToken ct)
    {
        var warehouses=await (await SourceAsync(OperationsOwnerV1.Warehouse,ct)).MasterWarehouses()
            .Where(w=>w.WarehouseCd==x.WarehouseCd).Select(w=>w.Id).ToArrayAsync(ct);
        if(warehouses.Length!=1)throw new OrderPriceCorrectionException("MASTER_SOURCE_UNAVAILABLE",503);
        Location? parent=null;var state=x.ParentLocationCd==null?"ROOT_RECORD":"SOURCE_DISCLOSURE_UNAVAILABLE";
        if(!string.IsNullOrEmpty(x.ParentLocationCd))
        {
            var candidates=await (await MasterLocations(ct)).Where(l=>l.WarehouseCd==x.WarehouseCd
                &&l.LocationCd==x.ParentLocationCd&&l.Id!=x.Id).Take(2).ToArrayAsync(ct);
            if(candidates.Length==1){parent=candidates[0];state="AUTHORIZED_ORIGINAL_REFERENCE";}
        }
        return new(x.Id,warehouses[0],x.WarehouseCd,x.LocationCd,MasterRead("LocationName")?x.LocationName:null,
            x.LocationLevel,parent?.Id,parent?.LocationCd,state,x.XCoord,x.YCoord,x.ZCoord,
            InventoryReportValuesV1.Exact(x.CapacityQty).ToString(),InventoryReportValuesV1.Exact(x.ReservedCapacityQty).ToString(),
            x.AreaCd,x.AllowedProductType,x.IsPickable,x.IsBlocked,MasterRead("Barcode")?x.Barcode:null,MasterEtag(x),
            MasterRead("LocationName"),authority.Fields.Fields["LocationName"].CanWrite,MasterRead("Barcode"));
    }
}
