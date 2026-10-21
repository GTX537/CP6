using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class OutboundService
{
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true && _seq is WmsSequenceService seq && seq.Owns(_db)?_owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<List<OutboundOrder>> SearchOrdersAsync(OutboundOrderSearchQuery q)
    {
        if(!_db.Database.IsNpgsql())return SearchOrdersCoreAsync(q);
        var frozen=OperationsOwnerV1.Freeze(q);
        return Owner.ExecuteAsync(OperationsOwnerV1.Outbound,"query",false,(a,ct)=>SearchOrdersCoreAsync(frozen,a.Outbounds()));
    }
    public Task<OutboundOrderDto?> GetOrderAsync(string number)=>!_db.Database.IsNpgsql()?GetOrderCoreAsync(number):
        Owner.ExecuteAsync(OperationsOwnerV1.Outbound,"query",false,async (a,ct)=>
        {
            await a.RequireOutbound(number,false,ct);var dto=await GetOrderCoreAsync(number);
            if(dto!=null && !a.PriceReadable)foreach(var line in dto.Details)line.UnitPrice=null;
            return dto;
        });
    public Task<string> CreateOrderAsync(OutboundOrderDto dto,string? userName)
    {
        if(!_db.Database.IsNpgsql())return CreateOrderCoreAsync(dto,userName);
        var frozen=OperationsOwnerV1.Freeze(dto);ValidateOwned(frozen);
        return Owner.ExecuteAsync(OperationsOwnerV1.Outbound,"add",true,async (a,ct)=>
        {
            a.RequirePriceWrite();await a.RequireActiveAssociations(frozen.WebOrderNo,frozen.WorkOrderNo,ct);
            var no=await CreateOrderCoreAsync(frozen,a.Actor);a.AtCommit(t=>a.RequireOutbound(no,false,t));return no;
        });
    }
    public Task UpdateOrderAsync(string number,OutboundOrderDto dto,string? userName)
    {
        if(!_db.Database.IsNpgsql())return UpdateOrderCoreAsync(number,dto,userName);
        var frozen=OperationsOwnerV1.Freeze(dto);ValidateOwned(frozen);
        return Owner.ExecuteAsync(OperationsOwnerV1.Outbound,"edit",true,async (a,ct)=>
        {
            await a.RequireOutbound(number,false,ct);a.RequirePriceWrite();
            await a.RequireActiveAssociations(frozen.WebOrderNo,frozen.WorkOrderNo,ct);
            await UpdateOwnedGraph(number,frozen,a.Actor,ct);a.AtCommit(t=>a.RequireOutbound(number,false,t));return true;
        });
    }
    public Task DeleteOrderAsync(string number,string? userName)=>!_db.Database.IsNpgsql()?DeleteOrderCoreAsync(number,userName):
        Owner.ExecuteAsync(OperationsOwnerV1.Outbound,"del",true,async (a,ct)=>
        {
            await a.RequireOutbound(number,false,ct);await RequireNoAllocation(number,ct);
            await DeleteOrderCoreAsync(number,a.Actor);a.AtCommit(t=>a.RequireOutbound(number,true,t));return true;
        });
    public Task ConfirmOrderAsync(string number,string? userName)=>!_db.Database.IsNpgsql()?ConfirmOrderCoreAsync(number,userName):
        Owner.ExecuteAsync(OperationsOwnerV1.Outbound,"confirm",true,async (a,ct)=>
        {
            await a.RequireOutbound(number,false,ct);await RequireNoAllocation(number,ct);
            var row=await _db.OutboundOrders.AsNoTracking().SingleAsync(o=>o.OutboundNo==number,ct);
            await a.RequireActiveAssociations(row.WebOrderNo,row.WorkOrderNo,ct);
            await ConfirmOrderCoreAsync(number,a.Actor);a.AtCommit(t=>a.RequireOutbound(number,false,t));return true;
        });
    public async Task CancelOrderAsync(string number,string? userName)
    {
        if(!_db.Database.IsNpgsql()){await CancelCoreAsync(number,userName,false);return;}
        var transactions=new List<StockTransaction>();
        var result=await Owner.ExecuteAsync(OperationsOwnerV1.Outbound,"cancel",true,async (a,ct)=>
        {
            await a.RequireOutbound(number,false,ct);RequireStockContext();
            var row=await _db.OutboundOrders.AsNoTracking().SingleAsync(o=>o.OutboundNo==number,ct);
            if(row.Status==OutboundOrderStatus.Cancelled)return (Changed:false,Shipping:false,Actor:a.Actor);
            await RequireStoredReservation(row,false,ct);await CancelCoreAsync(number,a.Actor,true);
            transactions.AddRange(_db.ChangeTracker.Entries<StockTransaction>().Select(e=>e.Entity));
            a.AtCommit(t=>a.RequireOutbound(number,false,t));return (Changed:true,Shipping:row.OutboundType==OutboundType.Shipping,Actor:a.Actor);
        });
        if(result.Changed)await PublishOrderCancellationAsync(number,result.Shipping,result.Actor,transactions);
    }
    public Task StartPickingAsync(string number,string? userName)=>!_db.Database.IsNpgsql()?StartPickingCoreAsync(number,userName):
        Owner.ExecuteAsync(OperationsOwnerV1.Outbound,"pick",true,async (a,ct)=>
        {
            await a.RequireOutbound(number,false,ct);
            var row=await _db.OutboundOrders.AsNoTracking().SingleAsync(o=>o.OutboundNo==number,ct);
            await a.RequireActiveAssociations(row.WebOrderNo,row.WorkOrderNo,ct);await RequireStoredReservation(row,false,ct);
            await StartPickingCoreAsync(number,a.Actor);a.AtCommit(t=>a.RequireOutbound(number,false,t));return true;
        });
    public async Task AllocateAsync(string number,string? userName)
    {
        if(!_db.Database.IsNpgsql()){await AllocateCoreAsync(number,userName);return;}
        var effects=new List<Func<Task>>();
        await Owner.ExecuteAsync(OperationsOwnerV1.Outbound,"allocate",true,async (a,ct)=>
        {
            await a.RequireOutbound(number,false,ct);a.RequirePriceWrite();RequireStockContext();await RequireNoAllocation(number,ct);
            var row=await _db.OutboundOrders.AsNoTracking().SingleAsync(o=>o.OutboundNo==number,ct);
            await a.RequireActiveAssociations(row.WebOrderNo,row.WorkOrderNo,ct);
            if(_routing!=null && _routing.GetType()!=typeof(NoOpOutboundRoutingService)
                && (_routing is not OutboundRoutingService routing || !routing.Owns(_db)))
                throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
            if(_shortage!=null && (_shortage is not MaterialShortageService real || !real.Owns(_db)))
                throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
            await AllocateCoreAsync(number,a.Actor,a,effects);QueueStockEffects(effects);
            a.AtCommit(t=>a.RequireOutbound(number,false,t));return true;
        });
        await PublishEffects(effects);
    }
    public async Task<string?> ShipAsync(string number,ShipRequest req,string? userName)
    {
        if(!_db.Database.IsNpgsql())return await ShipCoreAsync(number,req,userName);
        var frozen=OperationsOwnerV1.Freeze(req);var effects=new List<Func<Task>>();
        var result=await Owner.ExecuteAsync(OperationsOwnerV1.Outbound,"ship",true,async (a,ct)=>
        {
            await a.RequireOutbound(number,false,ct);a.RequirePriceRead();RequireStockContext();
            var row=await _db.OutboundOrders.AsNoTracking().SingleAsync(o=>o.OutboundNo==number,ct);
            await a.RequireActiveAssociations(row.WebOrderNo,row.WorkOrderNo,ct);
            if(row.OutboundType==OutboundType.Shipping && !string.IsNullOrWhiteSpace(row.WebOrderNo))await a.RequireSourceOrder(row.WebOrderNo,true,ct);
            await RequireStoredReservation(row,true,ct);
            var package=await ShipCoreAsync(number,frozen,a.Actor,effects);QueueStockEffects(effects);
            a.AtCommit(t=>a.RequireOutbound(number,false,t));return package;
        });
        await PublishEffects(effects);return result;
    }
    public Task<string> CreateFromWorkOrderAsync(string number,string? userName)=>!_db.Database.IsNpgsql()?CreateFromWorkOrderCoreAsync(number,userName):
        Owner.ExecuteAsync(OperationsOwnerV1.Outbound,"add",true,async (a,ct)=>
        {
            a.RequirePriceWrite();await a.RequireSourceWorkOrder(number,ct);
            var no=await CreateFromWorkOrderCoreAsync(number,a.Actor,true);a.AtCommit(t=>a.RequireOutbound(no,false,t));return no;
        });
    public Task<string> CreateFromOrderAsync(string number,string? userName)=>!_db.Database.IsNpgsql()?CreateFromOrderCoreAsync(number,userName):
        Owner.ExecuteAsync(OperationsOwnerV1.Outbound,"add",true,async (a,ct)=>
        {
            a.RequirePriceWrite();await a.RequireSourceOrder(number,true,ct);
            var no=await CreateFromOrderCoreAsync(number,a.Actor,true);a.AtCommit(t=>a.RequireOutbound(no,false,t));return no;
        });
    internal Task<string> CreateDelegatedAsync(OperationsDelegationV1 ticket)=>
        Owner.ExecuteAsync(OperationsOwnerV1.Outbound,"add",true,async(a,ct)=>
        {
            var number=ticket.Resource=="order"?await CreateFromOrderCoreAsync(ticket.Number,a.Actor,true)
                :await CreateFromWorkOrderCoreAsync(ticket.Number,a.Actor,true);
            a.AtCommit(t=>a.RequireGenerated(OperationsOwnerV1.Outbound,number,ticket,t));return number;
        },delegation:ticket);
    private void RequireStockContext()
    {if(_stock is not StockMovementService stock || !stock.Owns(_db))throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);}
    private void QueueStockEffects(List<Func<Task>> effects)
    {
        foreach(var row in _db.ChangeTracker.Entries<StockTransaction>().Select(e=>e.Entity))
            effects.Add(()=>((StockMovementService)_stock).PublishCommittedCancellationAsync(row));
    }
    private static async Task PublishEffects(List<Func<Task>> effects)
    {foreach(var publish in effects)try{await publish();}catch{/* Existing best-effort after confirmed commit. */}}
    private static void ValidateOwned(OutboundOrderDto dto)
    {
        if(dto.Details==null || dto.Details.Any(d=>d==null))throw new OrderPriceCorrectionException("OUTBOUND_WRITE_INVALID",400);
        ValidateOrder(dto);
    }
    private async Task RequireNoAllocation(string number,CancellationToken ct)
    {
        if(await _db.OutboundOrderDetails.AnyAsync(d=>d.OutboundNo==number && (d.AllocatedQty!=0 || d.ShippedQty!=0),ct))
            throw new OrderPriceCorrectionException("OUTBOUND_ALREADY_PROCESSED",409);
    }
    private async Task RequireStoredReservation(OutboundOrder row,bool ship,CancellationToken ct)
    {
        var details=await _db.OutboundOrderDetails.AsNoTracking().Where(d=>d.OutboundNo==row.OutboundNo && !d.IsDeleted).ToArrayAsync(ct);
        var quantities=new Dictionary<Guid,decimal>();
        foreach(var d in details)
        {
            if(d.AllocatedQty<0 || d.ShippedQty<0 || d.AllocatedQty<d.ShippedQty || (!ship && d.ShippedQty!=0)
                || (row.Status is not (OutboundOrderStatus.Allocated or OutboundOrderStatus.Picking or OutboundOrderStatus.PartialAllocated) && d.AllocatedQty!=0))
                throw new OrderPriceCorrectionException("OUTBOUND_RESERVATION_INCONSISTENT",409);
            var qty=d.AllocatedQty-d.ShippedQty;if(qty==0)continue;
            var stock=await _db.Stocks.AsNoTracking().SingleOrDefaultAsync(s=>!s.IsDeleted && s.WarehouseCd==(d.WarehouseCd??row.WarehouseCd)
                && s.LocationCd==(d.LocationCd??"") && s.ProductCd==d.ProductCd && s.LotNo==(d.LotNo??""),ct);
            if(stock==null)throw new OrderPriceCorrectionException("OUTBOUND_RESERVATION_INCONSISTENT",409);
            quantities[stock.Id]=quantities.GetValueOrDefault(stock.Id)+qty;
        }
        foreach(var (id,qty) in quantities)
        {
            var stock=await _db.Stocks.AsNoTracking().SingleAsync(s=>s.Id==id,ct);
            if(stock.AllocatedQty<qty || ship && stock.PhysicalQty<qty)throw new OrderPriceCorrectionException("OUTBOUND_RESERVATION_INCONSISTENT",409);
        }
    }
    private async Task UpdateOwnedGraph(string number,OutboundOrderDto dto,string actor,CancellationToken ct)
    {
        var h=await _db.OutboundOrders.SingleAsync(o=>o.OutboundNo==number && !o.IsDeleted,ct);
        if(h.Status is not (OutboundOrderStatus.Draft or OutboundOrderStatus.Confirmed))throw new InvalidOperationException("WM-MSG-043");
        await RequireNoAllocation(number,ct);
        h.OutboundType=dto.OutboundType;h.WorkOrderNo=dto.WorkOrderNo;h.WebOrderNo=dto.WebOrderNo;h.CustomerCd=dto.CustomerCd;
        h.CustomerName=dto.CustomerName;h.WarehouseCd=dto.WarehouseCd;h.PlannedDate=dto.PlannedDate;h.Priority=dto.Priority;
        h.ShipToAddress=dto.ShipToAddress;h.CarrierCd=dto.CarrierCd;h.Remarks=dto.Remarks;h.Modifier=actor;h.ModifyDate=DateTime.Now;
        var old=await _db.OutboundOrderDetails.Where(d=>d.OutboundNo==number).ToArrayAsync(ct);
        for(var i=0;i<dto.Details.Count;i++)
        {
            var d=dto.Details[i];var row=old.SingleOrDefault(x=>x.LineNo==i+1);
            if(row==null){row=new(){OutboundNo=h.OutboundNo,LineNo=i+1,Creator=actor};_db.OutboundOrderDetails.Add(row);}
            row.ProductCd=d.ProductCd;row.ProductName=d.ProductName;row.RequiredQty=d.RequiredQty;
            row.UnitCd=d.UnitCd;row.UnitPrice=d.UnitPrice;row.Remarks=d.Remarks;row.Modifier=actor;row.ModifyDate=DateTime.Now;
        }
        _db.OutboundOrderDetails.RemoveRange(old.Where(d=>d.LineNo>dto.Details.Count));await _db.SaveChangesAsync(ct);
    }
}
