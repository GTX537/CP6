using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1(CP6Context db,OperationsOwnerV1 owner,OperationsOwnerV1.Authority authority,List<OperationsOwnerV1.Authority>? participants=null)
{
    internal List<OperationsOwnerV1.Authority> Authorities {get;}=participants??[authority];
    private readonly List<Func<CancellationToken,Task>> finals=[];
    internal OperationsDelegationV1 Delegate(string number)=>new(db,db.CurrentTenantId,authority.Source.SubjectId,
        Actor,authority.Resource,authority.Action,number,authority.Source.SourcePermissionVersion);
    internal async Task RequireDelegationParent(OperationsDelegationV1 ticket,CancellationToken ct)
    {
        if(ticket.Resource=="order")
        {
            if(!authority.Fields.Fields["UnitPrice"].CanWrite || !authority.Fields.Fields["Amount"].CanWrite)
                throw new OrderPriceCorrectionException("FIELD_WRITE_FORBIDDEN",403);
            var details=OrderDepartmentScopeV1.Query(db,authority.Actor,authority.Scope,includeDeleted:true).Select(d=>d.Id);
            if(!await OrderDepartmentScopeV1.Headers(db,authority.Actor,authority.Scope,false).AnyAsync(o=>o.WebOrderNo==ticket.Number && o.CancelledAt==null,ct)
                || await db.OrderDetails.AnyAsync(d=>d.WebOrderNo==ticket.Number && !details.Contains(d.Id),ct))
                throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
        }
        else if(ticket.Resource==OperationsOwnerV1.Production)
        {
            await RequireProductionWorkOrder(ticket.Number,ct);
            if(!await db.WorkOrders.AnyAsync(w=>w.WorkOrderNo==ticket.Number && !w.IsDeleted
                && w.Status==WorkOrderStatus.Completed && w.CompletedQty==ticket.CompletedQuantity,ct)
                || !await db.ProductionResults.AnyAsync(r=>r.Id==ticket.CompletionResultId && r.WorkOrderNo==ticket.Number
                    && !r.IsDeleted && r.ResultType==4 && r.Creator==Actor,ct))
                throw new OrderPriceCorrectionException("DELEGATION_FORBIDDEN",403);
            var parent=await db.WorkOrders.AsNoTracking().SingleAsync(w=>w.WorkOrderNo==ticket.Number,ct);
            await RequireActiveAssociations(parent.WebOrderNo,null,ct);
        }
        else
        {
            if(!await WorkOrders(false).AnyAsync(w=>w.WorkOrderNo==ticket.Number && w.Status==WorkOrderStatus.Issued,ct))
                throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
            var parent=await db.WorkOrders.AsNoTracking().SingleAsync(w=>w.WorkOrderNo==ticket.Number,ct);
            await RequireActiveAssociations(parent.WebOrderNo,null,ct);
        }
    }
    internal async Task RequireGenerated(string resource,string number,OperationsDelegationV1 ticket,CancellationToken ct)
    {
        // Exact generated draft only. Parent authorization does not grant independent child actions or warehouse access.
        var valid=resource==OperationsOwnerV1.Mes
            ? await db.WorkOrders.AnyAsync(w=>w.WorkOrderNo==number && w.WebOrderNo==ticket.Number && !w.IsDeleted
                && w.Creator==Actor && w.Status==WorkOrderStatus.Confirmed,ct)
            : await db.OutboundOrders.AnyAsync(o=>o.OutboundNo==number && !o.IsDeleted && o.Creator==Actor
                && o.Status==OutboundOrderStatus.Draft && o.WarehouseCd=="W01"
                && (ticket.Resource=="order" ? o.WebOrderNo==ticket.Number && o.OutboundType==OutboundType.Shipping
                    : o.WorkOrderNo==ticket.Number && o.OutboundType==OutboundType.Material),ct);
        if(!valid)throw new OrderPriceCorrectionException("DELEGATION_FORBIDDEN",403);
    }
    internal string Actor=>authority.Actor.UserName!;
    internal Guid ActorId=>authority.Actor.UserId;
    internal bool PriceReadable=>authority.Fields.Fields.TryGetValue("UnitPrice",out var field) && field.CanRead;
    internal void RequirePriceWrite()
    {if(!authority.Fields.Fields.TryGetValue("UnitPrice",out var field) || !field.CanWrite)throw new OrderPriceCorrectionException("FIELD_WRITE_FORBIDDEN",403);}
    internal void RequirePriceRead()
    {if(!PriceReadable)throw new OrderPriceCorrectionException("FIELD_READ_FORBIDDEN",403);}
    internal void AtCommit(Func<CancellationToken,Task> validation)=>finals.Add(validation);
    internal async Task ValidateFinalAsync(CancellationToken ct)
    {foreach(var validate in finals)await validate(ct);}

    private static UserPermissionContext DepartmentActor(OperationsOwnerV1.Authority a)=>new()
    {
        UserId=a.Actor.UserId,UserName=a.Actor.UserName,DeptId=a.Actor.DeptId,DeptPath=a.Actor.DeptPath,
        CustomDeptIds=new(){["order"]=a.Actor.CustomDeptIds.GetValueOrDefault(a.Resource,[])}
    };
    private IQueryable<Order> DepartmentOrders(OperationsOwnerV1.Authority a)
        =>OrderDepartmentScopeV1.Headers(db,DepartmentActor(a),a.Scope,false).AsNoTracking();

    internal IQueryable<WorkOrder> WorkOrders(bool includeDeleted=false)
        =>WorkOrders(authority,includeDeleted);
    private IQueryable<WorkOrder> WorkOrders(OperationsOwnerV1.Authority a,bool includeDeleted)
    {
        var rows=db.WorkOrders.AsNoTracking().Where(w=>includeDeleted || !w.IsDeleted);
        if(a.Scope==1)rows=rows.Where(w=>w.Creator==a.Actor.UserName
            && !db.WorkOrderProcesses.Any(p=>p.WorkOrderNo==w.WorkOrderNo && (p.Creator==null || p.Creator!=a.Actor.UserName))
            && !db.WorkOrderMaterials.Any(m=>m.WorkOrderNo==w.WorkOrderNo && (m.Creator==null || m.Creator!=a.Actor.UserName)));
        else if(a.Scope!=5)
        {
            var numbers=DepartmentOrders(a).Select(o=>o.WebOrderNo);
            var standalone=StandaloneDepartmentScopeV1.Objects(db,a,"work-order");
            rows=rows.Where(w=>w.WebOrderNo!=null && w.WebOrderNo!="" && numbers.Contains(w.WebOrderNo)
                || (w.WebOrderNo==null || w.WebOrderNo=="") && standalone.Contains(w.Id));
        }
        // Native stored references must identify a live source if present; no inference from code casing.
        return rows.Where(w=>w.WebOrderNo==null || w.WebOrderNo=="" || db.Orders.Any(o=>o.WebOrderNo==w.WebOrderNo && !o.IsDeleted));
    }
    internal async Task RequireWorkOrder(string number,bool includeDeleted,CancellationToken ct)
    {
        if(!await WorkOrders(includeDeleted).AnyAsync(w=>w.WorkOrderNo==number,ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
    }
    internal IQueryable<OutboundOrder> Outbounds(bool includeDeleted=false)
    {
        var rows=db.OutboundOrders.AsNoTracking().Where(o=>includeDeleted || !o.IsDeleted);
        if(authority.Scope==1)rows=rows.Where(o=>o.Creator==Actor && !db.OutboundOrderDetails.Any(d=>d.OutboundNo==o.OutboundNo && (d.Creator==null || d.Creator!=Actor)));
        else if(authority.Scope!=5)
        {
            var orders=DepartmentOrders(authority).Select(o=>o.WebOrderNo);
            rows=rows.Where(o=>(o.WebOrderNo!=null && o.WebOrderNo!="" && orders.Contains(o.WebOrderNo))
                || ((o.WebOrderNo==null || o.WebOrderNo=="") && db.WorkOrders.Any(w=>w.WorkOrderNo==o.WorkOrderNo && !w.IsDeleted && w.WebOrderNo!=null && orders.Contains(w.WebOrderNo))));
        }
        rows=rows.Where(o=>(o.WebOrderNo==null || o.WebOrderNo=="" || db.Orders.Any(p=>p.WebOrderNo==o.WebOrderNo && !p.IsDeleted))
            && (o.WorkOrderNo==null || o.WorkOrderNo=="" || db.WorkOrders.Any(w=>w.WorkOrderNo==o.WorkOrderNo && !w.IsDeleted
                && (o.WebOrderNo==null || o.WebOrderNo=="" || w.WebOrderNo==o.WebOrderNo))));
        if(authority.Actor.RoleIds.Contains(1))return rows; // Existing warehouse-wide policy; never skips action/object/field checks.
        var grants=db.WmsRoleScopes.Where(g=>authority.Actor.RoleIds.Contains(g.RoleId));
        var details=db.OutboundOrderDetails.AsNoTracking();
        var allowedDetails=details.Where(d=>db.OutboundOrders.Any(o=>o.OutboundNo==d.OutboundNo && grants.Any(g=>
            (g.WarehouseCd=="*" || g.WarehouseCd==(d.WarehouseCd??o.WarehouseCd))
            && (g.AreaCd==null || g.AreaCd=="" || g.AreaCd=="*" || db.Locations.Any(l=>!l.IsDeleted
                && l.WarehouseCd==(d.WarehouseCd??o.WarehouseCd) && l.LocationCd==d.LocationCd && l.AreaCd==g.AreaCd))))).Select(d=>d.Id);
        return rows.Where(o=>grants.Any(g=>g.WarehouseCd=="*" || g.WarehouseCd==o.WarehouseCd)
            && !details.Any(d=>d.OutboundNo==o.OutboundNo && !allowedDetails.Contains(d.Id))
            && (details.Any(d=>d.OutboundNo==o.OutboundNo) || grants.Any(g=>(g.WarehouseCd=="*" || g.WarehouseCd==o.WarehouseCd)
                && (g.AreaCd==null || g.AreaCd=="" || g.AreaCd=="*"))));
    }
    internal async Task RequireOutbound(string number,bool includeDeleted,CancellationToken ct)
    {
        if(!await Outbounds(includeDeleted).AnyAsync(o=>o.OutboundNo==number,ct))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
    }
    internal IQueryable<Stock> Stocks(IQueryable<Stock> rows)
    {
        if(authority.Actor.RoleIds.Contains(1))return rows;
        var grants=db.WmsRoleScopes.Where(g=>authority.Actor.RoleIds.Contains(g.RoleId));
        return rows.Where(s=>grants.Any(g=>(g.WarehouseCd=="*" || g.WarehouseCd==s.WarehouseCd)
            && (g.AreaCd==null || g.AreaCd=="" || g.AreaCd=="*" || db.Locations.Any(l=>!l.IsDeleted
                && l.WarehouseCd==s.WarehouseCd && l.LocationCd==s.LocationCd && l.AreaCd==g.AreaCd))));
    }
    internal async Task RequireSourceOrder(string number,bool price,CancellationToken ct)
    {
        var source=await owner.AuthorityAsync("order","query",ct);Authorities.Add(source);
        var headers=OrderDepartmentScopeV1.Headers(db,source.Actor,source.Scope,false);
        var details=OrderDepartmentScopeV1.Query(db,source.Actor,source.Scope,includeDeleted:true).Select(d=>d.Id);
        if(!await headers.AnyAsync(o=>o.WebOrderNo==number && o.CancelledAt==null,ct)
            || await db.OrderDetails.AnyAsync(d=>d.WebOrderNo==number && !details.Contains(d.Id),ct))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
        if(price && !source.Fields.Fields["UnitPrice"].CanRead)throw new OrderPriceCorrectionException("FIELD_READ_FORBIDDEN",403);
    }
    internal async Task RequireSourceWorkOrder(string number,CancellationToken ct)
    {
        var source=await owner.AuthorityAsync(OperationsOwnerV1.Mes,"query",ct);Authorities.Add(source);
        if(!await WorkOrders(source,false).AnyAsync(w=>w.WorkOrderNo==number && w.Status!=WorkOrderStatus.Cancelled,ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
    }
    internal async Task RequireActiveAssociations(string? order,string? workOrder,CancellationToken ct)
    {
        if(!string.IsNullOrWhiteSpace(order) && !await db.Orders.AnyAsync(o=>o.WebOrderNo==order && !o.IsDeleted && o.CancelledAt==null,ct))
            throw new OrderPriceCorrectionException("SOURCE_NOT_ACTIVE",409);
        if(!string.IsNullOrWhiteSpace(workOrder) && !await db.WorkOrders.AnyAsync(w=>w.WorkOrderNo==workOrder && !w.IsDeleted && w.Status!=WorkOrderStatus.Cancelled
            && (order==null || order=="" || w.WebOrderNo==order)
            && (w.WebOrderNo==null || w.WebOrderNo=="" || db.Orders.Any(o=>o.WebOrderNo==w.WebOrderNo && !o.IsDeleted && o.CancelledAt==null)),ct))
            throw new OrderPriceCorrectionException("SOURCE_NOT_ACTIVE",409);
    }
}
