using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    private readonly Dictionary<string,OperationsAccessV1> sources=[];
    internal async Task<OperationsAccessV1> SourceAsync(string resource,CancellationToken ct)
        =>await ActionAsync(resource,"query",ct);
    internal async Task<OperationsAccessV1> ActionAsync(string resource,string action,CancellationToken ct)
    {
        var key=resource+":"+action;
        if(sources.TryGetValue(key,out var found))return found;
        var source=await owner.AuthorityAsync(resource,action,ct);Authorities.Add(source);
        return sources[key]=new(db,owner,source,Authorities);
    }
    internal bool AmountReadable=>authority.Fields.Fields.TryGetValue("Amount",out var field) && field.CanRead;
    internal void RequireAmountRead()
    {if(!AmountReadable)throw new OrderPriceCorrectionException("FIELD_READ_FORBIDDEN",403);}
    internal void RequireAmountWrite()
    {if(!authority.Fields.Fields.TryGetValue("Amount",out var f) || !f.CanWrite)throw new OrderPriceCorrectionException("FIELD_WRITE_FORBIDDEN",403);}
    internal IQueryable<Stock> InventoryStocks(IQueryable<Stock>? rows=null)
    {
        rows??=db.Stocks.AsNoTracking().Where(s=>!s.IsDeleted);
        if(authority.Scope==1)rows=rows.Where(s=>s.Creator==Actor);
        else if(authority.Scope!=5) { var ids=StandaloneDepartmentScopeV1.Objects(db,authority,"stock"); rows=rows.Where(s=>ids.Contains(s.Id)); }
        return Stocks(rows);
    }
    internal IQueryable<StockTransaction> InventoryTransactions(IQueryable<StockTransaction>? rows=null)
    {
        rows??=db.StockTransactions.AsNoTracking().Where(s=>!s.IsDeleted);
        if(authority.Scope==1)rows=rows.Where(s=>s.Creator==Actor);
        else if(authority.Scope!=5) { var ids=StandaloneDepartmentScopeV1.Objects(db,authority,"stock-transaction"); rows=rows.Where(s=>ids.Contains(s.Id)); }
        if(authority.Actor.RoleIds.Contains(1))return rows;
        var grants=db.WmsRoleScopes.Where(g=>authority.Actor.RoleIds.Contains(g.RoleId));
        return rows.Where(s=>grants.Any(g=>(g.WarehouseCd=="*" || g.WarehouseCd==s.WarehouseCd)
            && (g.AreaCd==null || g.AreaCd=="" || g.AreaCd=="*" || db.Locations.Any(l=>!l.IsDeleted
                && l.WarehouseCd==s.WarehouseCd && l.LocationCd==s.LocationCd && l.AreaCd==g.AreaCd))));
    }
    internal async Task RequireStockKey(string warehouse,string location,string product,string lot,bool allowNew,CancellationToken ct)
    {
        await RequireLocation(warehouse,location,ct);
        var rows=db.Stocks.AsNoTracking().Where(s=>s.WarehouseCd==warehouse && s.LocationCd==location && s.ProductCd==product && s.LotNo==lot);
        if(await rows.AnyAsync(ct))
        {
            if(!await InventoryStocks(rows).AnyAsync(ct))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
        }
        else if(!allowNew || authority.Scope is not (1 or 5))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
    }
    internal IQueryable<StockTake> StockTakes()
    {
        var rows=db.StockTakes.AsNoTracking().Where(s=>!s.IsDeleted);
        var details=db.StockTakeDetails.AsNoTracking();
        if(authority.Scope==1)rows=rows.Where(s=>s.Creator==Actor && !details.Any(d=>d.StockTakeNo==s.StockTakeNo && (d.Creator==null || d.Creator!=Actor)));
        else if(authority.Scope!=5)
        {
            var ids=StandaloneDepartmentScopeV1.Objects(db,authority,"stocktake");
            var stocks=InventoryStocks();
            // Header membership alone cannot expose or adjust a stock outside the same current department scope.
            rows=rows.Where(s=>ids.Contains(s.Id) && !details.Any(d=>d.StockTakeNo==s.StockTakeNo
                && !stocks.Any(t=>t.Id==d.StockId && t.WarehouseCd==d.WarehouseCd && t.LocationCd==d.LocationCd
                    && t.ProductCd==d.ProductCd && t.LotNo==d.LotNo)));
        }
        if(authority.Actor.RoleIds.Contains(1))return rows;
        var grants=db.WmsRoleScopes.Where(g=>authority.Actor.RoleIds.Contains(g.RoleId));
        var allowed=details.Where(d=>grants.Any(g=>(g.WarehouseCd=="*" || g.WarehouseCd==d.WarehouseCd)
            && (g.AreaCd==null || g.AreaCd=="" || g.AreaCd=="*" || db.Locations.Any(l=>!l.IsDeleted
                && l.WarehouseCd==d.WarehouseCd && l.LocationCd==d.LocationCd && l.AreaCd==g.AreaCd)))).Select(d=>d.Id);
        return rows.Where(s=>grants.Any(g=>g.WarehouseCd=="*" || g.WarehouseCd==s.TargetWarehouseCd)
            && !details.Any(d=>d.StockTakeNo==s.StockTakeNo && !allowed.Contains(d.Id))
            && (details.Any(d=>d.StockTakeNo==s.StockTakeNo) || grants.Any(g=>(g.WarehouseCd=="*" || g.WarehouseCd==s.TargetWarehouseCd)
                && (g.AreaCd==null || g.AreaCd=="" || g.AreaCd=="*"))));
    }
    internal async Task RequireStockTake(string no,CancellationToken ct)
    {
        if(!await StockTakes().AnyAsync(s=>s.StockTakeNo==no,ct))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
    }
    internal IQueryable<WorkOrder> DashboardWorkOrders()
    {
        var rows=ProductionWorkOrders();
        if(authority.Scope==1)rows=rows.Where(w=>!db.DefectRecords.Any(d=>d.WorkOrderNo==w.WorkOrderNo && (d.Creator==null || d.Creator!=Actor)));
        return rows;
    }
    internal IQueryable<DefectRecord> DashboardDefects()
    {
        var orders=DashboardWorkOrders();
        // The aggregate source must honor the same optional inspection graph as the independent Owner.
        return Defects(this).Where(d=>orders.Any(w=>w.WorkOrderNo==d.WorkOrderNo));
    }
}
