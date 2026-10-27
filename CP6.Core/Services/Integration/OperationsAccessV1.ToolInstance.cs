using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal void RequireToolSourceAccess(CP6Context context)
    {
        if(!ReferenceEquals(db,context)||db.Database.CurrentTransaction==null||authority.Source.TenantId!=db.CurrentTenantId)
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
        if(authority.Resource!=OperationsOwnerV1.ToolInstance||authority.Action!="query")
            throw new OrderPriceCorrectionException("ACTION_FORBIDDEN",403);
    }
    internal IQueryable<PlateMoldStock> ToolInstances()
    {
        var rows=db.PlateMoldStocks.AsNoTracking().Where(x=>!x.IsDeleted);
        if(authority.Scope==1)rows=rows.Where(x=>x.Creator==Actor);
        else if(authority.Scope!=5)
        {var ids=StandaloneDepartmentScopeV1.Objects(db,authority,"tool-instance");rows=rows.Where(x=>ids.Contains(x.Id));}
        if(!authority.Actor.RoleIds.Contains(1))
        {
            var grants=db.WmsRoleScopes.Where(g=>authority.Actor.RoleIds.Contains(g.RoleId));
            // Missing location is unknown, not an invented stock/location binding. Only an all-area grant
            // can cover it; a missing warehouse additionally requires a wildcard warehouse grant.
            rows=rows.Where(x=>grants.Any(g=>(g.WarehouseCd=="*"||x.WarehouseCd!=null&&x.WarehouseCd!=""&&g.WarehouseCd==x.WarehouseCd)
                &&(g.AreaCd==null||g.AreaCd==""||g.AreaCd=="*"||x.WarehouseCd!=null&&x.WarehouseCd!=""&&x.LocationCd!=null&&x.LocationCd!=""
                    &&db.Warehouses.Any(w=>!w.IsDeleted&&w.WarehouseCd==x.WarehouseCd)
                    &&db.Locations.Any(l=>!l.IsDeleted&&l.WarehouseCd==x.WarehouseCd&&l.LocationCd==x.LocationCd&&l.AreaCd==g.AreaCd))));
        }
        return rows;
    }
    internal void RequireToolFieldRead(string field)
    {if(!authority.Fields.Fields[field].CanRead)throw new OrderPriceCorrectionException("FIELD_READ_FORBIDDEN",403);}
    internal async Task<PlateMoldStock> RequireTool(Guid id,CancellationToken ct)
        =>await ToolInstances().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new OrderPriceCorrectionException("TOOL_INSTANCE_NOT_FOUND",404);
    internal void RequireToolFieldWrite(string field)
    {if(!authority.Fields.Fields[field].CanWrite)throw new OrderPriceCorrectionException("FIELD_WRITE_FORBIDDEN",403);}
    internal async Task RequireToolInstance(CP6Context context,Guid id,CancellationToken ct)
    {
        if(!ReferenceEquals(db,context)||db.Database.CurrentTransaction==null||authority.Resource!=OperationsOwnerV1.ToolInstance
            ||authority.Source.TenantId!=db.CurrentTenantId)throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
        await RequireTool(id,ct);
    }
    internal async Task<System.Text.Json.JsonElement> MaskToolSource(System.Text.Json.JsonElement source,CancellationToken ct)
    {
        // An old location cannot become visible solely because the current object moved into scope.
        var warehouse=source.GetProperty("warehouseCd").GetString();var location=source.GetProperty("locationCd").GetString();
        if(!authority.Actor.RoleIds.Contains(1))
        {
            var area=await db.Locations.AsNoTracking().Where(x=>!x.IsDeleted&&x.WarehouseCd==warehouse&&x.LocationCd==location
                &&db.Warehouses.Any(w=>!w.IsDeleted&&w.WarehouseCd==warehouse)).Select(x=>x.AreaCd).SingleOrDefaultAsync(ct);
            var grants=await db.WmsRoleScopes.AsNoTracking().Where(g=>authority.Actor.RoleIds.Contains(g.RoleId)).ToArrayAsync(ct);
            if(!grants.Any(g=>(g.WarehouseCd=="*"||!string.IsNullOrEmpty(warehouse)&&g.WarehouseCd==warehouse)
                &&(string.IsNullOrEmpty(g.AreaCd)||g.AreaCd=="*"||area!=null&&g.AreaCd==area)))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
        }
        var copy=System.Text.Json.Nodes.JsonNode.Parse(source.GetRawText())!.AsObject();
        foreach(var field in authority.Fields.Fields)if(!field.Value.CanRead)copy[char.ToLowerInvariant(field.Key[0])+field.Key[1..]]=null;
        return System.Text.Json.JsonSerializer.SerializeToElement(copy);
    }
    internal string[] ToolReadableFields=>authority.Fields.Fields.Where(x=>x.Value.CanRead).Select(x=>x.Key).ToArray();
    internal string[] ToolWritableFields=>authority.Fields.Fields.Where(x=>x.Value.CanWrite).Select(x=>x.Key).ToArray();
}
