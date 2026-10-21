using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal IQueryable<WorkOrder> ProductionWorkOrders()
    {
        var rows=WorkOrders();
        if(authority.Scope==1)rows=rows.Where(w=>!db.ProductionResults.Any(r=>r.WorkOrderNo==w.WorkOrderNo && (r.Creator==null || r.Creator!=Actor)));
        return rows;
    }
    internal IQueryable<ProductionResult> ProductionResults()
    {
        var orders=ProductionWorkOrders();
        return db.ProductionResults.AsNoTracking().Where(r=>!r.IsDeleted && orders.Any(w=>w.WorkOrderNo==r.WorkOrderNo));
    }
    internal async Task RequireProductionWorkOrder(string number,CancellationToken ct)
    {
        if(!await ProductionWorkOrders().AnyAsync(w=>w.WorkOrderNo==number,ct))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
    }
    internal IQueryable<InboundOrder> Inbounds(bool includeDeleted=false)
        =>Inbounds(authority,includeDeleted);
    private IQueryable<InboundOrder> Inbounds(OperationsOwnerV1.Authority a,bool includeDeleted)
    {
        var rows=db.InboundOrders.AsNoTracking().Where(o=>includeDeleted || !o.IsDeleted);
        if(a.Scope==1)rows=rows.Where(o=>o.Creator==a.Actor.UserName && !db.InboundOrderDetails.Any(d=>d.InboundNo==o.InboundNo && (d.Creator==null || d.Creator!=a.Actor.UserName)));
        else if(a.Scope!=5)rows=rows.Where(o=>false); // PoNo is not an accepted order/department binding.
        if(a.Actor.RoleIds.Contains(1))return rows;
        var grants=db.WmsRoleScopes.Where(g=>a.Actor.RoleIds.Contains(g.RoleId));
        var details=db.InboundOrderDetails.AsNoTracking();
        var allowed=details.Where(d=>db.InboundOrders.Any(o=>o.InboundNo==d.InboundNo && grants.Any(g=>
            (g.WarehouseCd=="*" || g.WarehouseCd==o.WarehouseCd) && (g.AreaCd==null || g.AreaCd=="" || g.AreaCd=="*"
                || db.Locations.Any(l=>!l.IsDeleted && l.WarehouseCd==o.WarehouseCd && l.LocationCd==d.ExpectedLocationCd && l.AreaCd==g.AreaCd))))).Select(d=>d.Id);
        return rows.Where(o=>grants.Any(g=>g.WarehouseCd=="*" || g.WarehouseCd==o.WarehouseCd)
            && !details.Any(d=>d.InboundNo==o.InboundNo && !allowed.Contains(d.Id))
            && (details.Any(d=>d.InboundNo==o.InboundNo) || grants.Any(g=>(g.WarehouseCd=="*" || g.WarehouseCd==o.WarehouseCd) && (g.AreaCd==null || g.AreaCd=="" || g.AreaCd=="*"))));
    }
    internal async Task RequireInbound(string number,bool includeDeleted,CancellationToken ct)
    {
        if(!await Inbounds(includeDeleted).AnyAsync(o=>o.InboundNo==number,ct))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
    }
    internal IQueryable<InboundReceipt> Receipts()
    {
        var rows=db.InboundReceipts.AsNoTracking().Where(r=>!r.IsDeleted);
        if(authority.Scope==1)rows=rows.Where(r=>r.Creator==Actor && !db.InboundReceiptDetails.Any(d=>d.ReceiptNo==r.ReceiptNo && (d.Creator==null || d.Creator!=Actor)));
        else if(authority.Scope!=5)
        {
            var orders=DepartmentOrders(authority).Select(o=>o.WebOrderNo);
            rows=rows.Where(r=>db.WorkOrders.Any(w=>w.WorkOrderNo==r.WorkOrderNo && !w.IsDeleted && w.WebOrderNo!=null && orders.Contains(w.WebOrderNo)));
        }
        rows=rows.Where(r=>(r.InboundNo==null || r.InboundNo=="" || db.InboundOrders.Any(o=>o.InboundNo==r.InboundNo && !o.IsDeleted))
            && (r.WorkOrderNo==null || r.WorkOrderNo=="" || db.WorkOrders.Any(w=>w.WorkOrderNo==r.WorkOrderNo && !w.IsDeleted)));
        if(authority.Actor.RoleIds.Contains(1))return rows;
        var grants=db.WmsRoleScopes.Where(g=>authority.Actor.RoleIds.Contains(g.RoleId));
        var details=db.InboundReceiptDetails.AsNoTracking();
        var allowed=details.Where(d=>db.InboundReceipts.Any(r=>r.ReceiptNo==d.ReceiptNo && grants.Any(g=>
            (g.WarehouseCd=="*" || g.WarehouseCd==r.WarehouseCd) && (g.AreaCd==null || g.AreaCd=="" || g.AreaCd=="*"
                || db.Locations.Any(l=>!l.IsDeleted && l.WarehouseCd==r.WarehouseCd && l.LocationCd==d.LocationCd && l.AreaCd==g.AreaCd))))).Select(d=>d.Id);
        return rows.Where(r=>grants.Any(g=>g.WarehouseCd=="*" || g.WarehouseCd==r.WarehouseCd)
            && !details.Any(d=>d.ReceiptNo==r.ReceiptNo && !allowed.Contains(d.Id))
            && (details.Any(d=>d.ReceiptNo==r.ReceiptNo) || grants.Any(g=>(g.WarehouseCd=="*" || g.WarehouseCd==r.WarehouseCd) && (g.AreaCd==null || g.AreaCd=="" || g.AreaCd=="*"))));
    }
    internal async Task RequireReceipt(string number,CancellationToken ct)
    {
        if(!await Receipts().AnyAsync(r=>r.ReceiptNo==number,ct))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
    }
    internal async Task RequireSourceInbound(string number,CancellationToken ct)
    {
        var source=await owner.AuthorityAsync(OperationsOwnerV1.Inbound,"query",ct);Authorities.Add(source);
        if(!await Inbounds(source,false).AnyAsync(o=>o.InboundNo==number,ct))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
        // Receipt confirmation consumes source identity/quantities, never the plan's UnitPrice.
    }
    internal async Task RequireLocation(string warehouse,string? location,CancellationToken ct)
    {
        if(authority.Actor.RoleIds.Contains(1))return;
        if(!await db.WmsRoleScopes.AnyAsync(g=>authority.Actor.RoleIds.Contains(g.RoleId) && (g.WarehouseCd=="*" || g.WarehouseCd==warehouse)
            && (g.AreaCd==null || g.AreaCd=="" || g.AreaCd=="*" || db.Locations.Any(l=>!l.IsDeleted && l.WarehouseCd==warehouse && l.LocationCd==location && l.AreaCd==g.AreaCd)),ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
    }
}
