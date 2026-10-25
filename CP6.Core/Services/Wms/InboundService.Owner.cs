using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class InboundService
{
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true && _seq is WmsSequenceService seq && seq.Owns(_db)
        ?_owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    private StockMovementService StockOwner=>_stock is StockMovementService stock && stock.Owns(_db)
        ?stock:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<List<InboundOrder>> SearchOrdersAsync(InboundOrderSearchQuery q)
    {
        if(!_db.Database.IsNpgsql())return SearchOrdersCoreAsync(q);
        var frozen=OperationsOwnerV1.Freeze(q);
        return Owner.ExecuteAsync(OperationsOwnerV1.Inbound,"query",false,(a,ct)=>SearchOrdersCoreAsync(frozen,a.Inbounds()));
    }
    public Task<InboundOrderDto?> GetOrderAsync(string no)=>!_db.Database.IsNpgsql()?GetOrderCoreAsync(no):
        Owner.ExecuteAsync(OperationsOwnerV1.Inbound,"query",false,async(a,ct)=>
        {
            await a.RequireInbound(no,false,ct);var dto=await GetOrderCoreAsync(no);
            if(dto!=null && !a.PriceReadable)foreach(var d in dto.Details)d.UnitPrice=null;
            return dto;
        });
    public Task<string> CreateOrderAsync(InboundOrderDto dto,string? userName)
    {
        if(!_db.Database.IsNpgsql())return CreateOrderCoreAsync(dto,userName);
        var frozen=OperationsOwnerV1.Freeze(dto);ValidateOwnedOrder(frozen);
        return Owner.ExecuteAsync(OperationsOwnerV1.Inbound,"add",true,async(a,ct)=>
        {
            a.RequirePriceWrite();var no=await CreateOrderCoreAsync(frozen,a.Actor);
            a.AtCommit(t=>a.RequireInbound(no,false,t));return no;
        });
    }
    public Task UpdateOrderAsync(string no,InboundOrderDto dto,string? userName)
    {
        if(!_db.Database.IsNpgsql())return UpdateOrderCoreAsync(no,dto,userName);
        var frozen=OperationsOwnerV1.Freeze(dto);ValidateOwnedOrder(frozen);
        return Owner.ExecuteAsync(OperationsOwnerV1.Inbound,"edit",true,async(a,ct)=>
        {
            await a.RequireInbound(no,false,ct);a.RequirePriceWrite();await RequireUnreceived(no,ct);
            await UpdateOwnedOrder(no,frozen,a.Actor,ct);a.AtCommit(t=>a.RequireInbound(no,false,t));return true;
        });
    }
    public Task DeleteOrderAsync(string no,string? userName)=>!_db.Database.IsNpgsql()?DeleteOrderCoreAsync(no,userName):
        Owner.ExecuteAsync(OperationsOwnerV1.Inbound,"del",true,async(a,ct)=>
        {
            await a.RequireInbound(no,false,ct);await RequireUnreceived(no,ct);await DeleteOrderCoreAsync(no,a.Actor);
            a.AtCommit(t=>a.RequireInbound(no,true,t));return true;
        });
    public Task ConfirmOrderAsync(string no,string? userName)=>!_db.Database.IsNpgsql()?ConfirmOrderCoreAsync(no,userName):
        Owner.ExecuteAsync(OperationsOwnerV1.Inbound,"confirm",true,async(a,ct)=>
        {
            await a.RequireInbound(no,false,ct);await RequireUnreceived(no,ct);await ConfirmOrderCoreAsync(no,a.Actor);
            a.AtCommit(t=>a.RequireInbound(no,false,t));return true;
        });
    public Task CancelOrderAsync(string no,string? userName)=>!_db.Database.IsNpgsql()?CancelOrderCoreAsync(no,userName):
        Owner.ExecuteAsync(OperationsOwnerV1.Inbound,"cancel",true,async(a,ct)=>
        {
            await a.RequireInbound(no,false,ct);await CancelOrderCoreAsync(no,a.Actor);
            a.AtCommit(t=>a.RequireInbound(no,false,t));return true;
        });
    public Task<List<InboundReceipt>> SearchReceiptsAsync(InboundReceiptSearchQuery q)
    {
        if(!_db.Database.IsNpgsql())return SearchReceiptsCoreAsync(q);
        var frozen=OperationsOwnerV1.Freeze(q);
        return Owner.ExecuteAsync(OperationsOwnerV1.Receipt,"query",false,(a,ct)=>SearchReceiptsCoreAsync(frozen,a.Receipts()));
    }
    public Task<InboundReceiptDto?> GetReceiptAsync(string no)=>!_db.Database.IsNpgsql()?GetReceiptCoreAsync(no):
        Owner.ExecuteAsync(OperationsOwnerV1.Receipt,"query",false,async(a,ct)=>
        {
            await a.RequireReceipt(no,ct);var dto=await GetReceiptCoreAsync(no);
            if(dto!=null && !a.PriceReadable)foreach(var d in dto.Details)d.UnitPrice=null;
            return dto;
        });
    public async Task<string> ConfirmReceiptAsync(InboundReceiptDto dto,string? userName)
    {
        if(!_db.Database.IsNpgsql())return await ConfirmReceiptCoreAsync(dto,userName);
        var frozen=OperationsOwnerV1.Freeze(dto);ValidateOwnedReceipt(frozen);var effects=new List<StockTransaction>();
        var no=await Owner.ExecuteAsync(OperationsOwnerV1.Receipt,"post",true,async(a,ct)=>
        {
            _=StockOwner;a.RequirePriceWrite();frozen.OperatorCd=a.Actor;
            foreach(var line in frozen.Details)await a.RequireLocation(frozen.WarehouseCd,line.LocationCd,ct);
            await RequireReceiptSources(frozen,a,ct);
            using var inheritance=StockOwner.InheritTransactions(a);
            var number=await ConfirmReceiptCoreAsync(frozen,a.Actor,true);CaptureStock(effects);
            a.AtCommit(t=>a.RequireReceipt(number,t));return number;
        });
        await PublishReceipt(no,frozen.WarehouseCd,effects);return no;
    }
    public Task<string> CreateFinishedGoodsFromWorkOrderAsync(string no,decimal goodQty,string? userName)
        =>!_db.Database.IsNpgsql()?CreateFinishedGoodsFromWorkOrderCoreAsync(no,goodQty,userName):ReceiveFinished(no,goodQty,null);
    internal Task<string> ReceiveCompletedAsync(OperationsDelegationV1 ticket)=>ReceiveFinished(ticket.Number,ticket.CompletedQuantity??0,ticket);
    private async Task<string> ReceiveFinished(string workOrder,decimal quantity,OperationsDelegationV1? ticket)
    {
        var effects=new List<StockTransaction>();
        var no=await Owner.ExecuteAsync(OperationsOwnerV1.Receipt,"post",true,async(a,ct)=>
        {
            _=StockOwner;
            if(ticket==null){a.RequirePriceWrite();await a.RequireSourceWorkOrder(workOrder,ct);await a.RequireLocation(FinishedGoodsWarehouse,FinishedGoodsLocation,ct);}
            if(quantity<=0 || !await _db.WorkOrders.AnyAsync(w=>w.WorkOrderNo==workOrder && !w.IsDeleted && w.Status==WorkOrderStatus.Completed && w.CompletedQty==quantity,ct))
                throw new OrderPriceCorrectionException("PRODUCTION_RECEIPT_INVALID",409);
            using var inheritance=StockOwner.InheritTransactions(a);
            var number=await CreateFinishedGoodsFromWorkOrderCoreAsync(workOrder,quantity,a.Actor,true);CaptureStock(effects);
            if(ticket==null)a.AtCommit(t=>a.RequireReceipt(number,t));
            else a.AtCommit(async t=>
            {
                if(!await _db.InboundReceipts.AnyAsync(r=>r.ReceiptNo==number && r.WorkOrderNo==ticket.Number && r.SourceType==InboundSourceType.Production
                    && r.InboundNo==null && r.Creator==a.Actor && r.WarehouseCd==FinishedGoodsWarehouse && r.Status==InboundReceiptStatus.Confirmed && !r.IsDeleted,t)
                    || await _db.InboundReceiptDetails.CountAsync(d=>d.ReceiptNo==number,t)!=1
                    || !await _db.InboundReceiptDetails.AnyAsync(d=>d.ReceiptNo==number && !d.IsDeleted && d.Creator==a.Actor && d.ReceivedQty==ticket.CompletedQuantity
                        && d.LocationCd==FinishedGoodsLocation && d.UnitPrice==null && _db.WorkOrders.Any(w=>w.WorkOrderNo==ticket.Number && w.ProductCd==d.ProductCd),t))
                    throw new OrderPriceCorrectionException("DELEGATION_FORBIDDEN",403);
            });
            return number;
        },delegation:ticket);
        await PublishReceipt(no,FinishedGoodsWarehouse,effects);return no;
    }
    private void CaptureStock(List<StockTransaction> target)=>target.AddRange(_db.ChangeTracker.Entries<StockTransaction>().Select(e=>e.Entity));
    internal async Task<string> ReceiveInspectionWithinOwnerAsync(OperationsAccessV1 parent,string inspection,InboundReceiptDto dto,List<StockTransaction> effects,CancellationToken ct)
    {
        parent.RequireQualityParent(_db,OperationsOwnerV1.InboundQc,"judge");
        _=Owner;_=StockOwner;ValidateOwnedReceipt(dto);
        var header=await _db.QcInspections.SingleAsync(q=>q.InspectionNo==inspection && !q.IsDeleted,ct);
        var lines=await _db.QcInspectionItems.AsNoTracking().Where(d=>d.InspectionNo==inspection && !d.IsDeleted && d.AcceptedQty>0).OrderBy(d=>d.LineNo).ToArrayAsync(ct);
        if(header.Status!=QcInspectionStatus.Judged || header.FinalJudgement!=QcInspectionJudgement.Pass || header.GeneratedReceiptNo!=null
            || dto.InboundNo!=header.InboundNo || dto.WorkOrderNo!=null || dto.SourceType!=InboundSourceType.Purchase || lines.Length!=dto.Details.Count
            || lines.Where((d,i)=>dto.Details[i].ProductCd!=d.ProductCd || dto.Details[i].ReceivedQty!=d.AcceptedQty
                || dto.Details[i].UnitPrice!=null || dto.Details[i].RefOrderLineNo!=null).Any())
            throw new OrderPriceCorrectionException("INTERNAL_QUALITY_CAPABILITY_REQUIRED",403);
        var access=await parent.ActionAsync(OperationsOwnerV1.Receipt,"post",ct);access.RequirePriceWrite();dto.OperatorCd=access.Actor;
        foreach(var line in dto.Details)await access.RequireLocation(dto.WarehouseCd,line.LocationCd,ct);
        await RequireReceiptSources(dto,access,ct);
        using var inheritance=StockOwner.InheritTransactions(parent);
        var number=await ConfirmReceiptCoreAsync(dto,access.Actor,true);CaptureStock(effects);
        parent.AtCommit(async t=>
        {
            await RequireReceiptSources(dto,access,t);await access.RequireReceipt(number,t);
        });
        return number;
    }
    internal Task PublishInspectionReceiptAsync(string number,string warehouse,List<StockTransaction> effects)=>PublishReceipt(number,warehouse,effects);
    private async Task PublishReceipt(string no,string warehouse,List<StockTransaction> effects)
    {
        foreach(var effect in effects)await StockOwner.PublishCommittedCancellationAsync(effect);
        try{await _notifier.NotifyInboundReceivedAsync(no,warehouse);}catch{/* Existing best-effort after confirmed commit. */}
    }
    private static void ValidateOwnedOrder(InboundOrderDto dto)
    {
        if(dto.Details==null || dto.Details.Any(d=>d==null || d.ReceivedQty!=0))throw new OrderPriceCorrectionException("INBOUND_WRITE_INVALID",400);
        ValidateOrder(dto);
    }
    private static void ValidateOwnedReceipt(InboundReceiptDto dto)
    {
        if(dto.Details==null || dto.Details.Any(d=>d==null))throw new OrderPriceCorrectionException("RECEIPT_WRITE_INVALID",400);
        ValidateReceipt(dto);
    }
    private async Task RequireUnreceived(string no,CancellationToken ct)
    {
        if(await _db.InboundOrderDetails.AnyAsync(d=>d.InboundNo==no && d.ReceivedQty!=0,ct)
            || await _db.InboundReceipts.AnyAsync(r=>r.InboundNo==no,ct))throw new OrderPriceCorrectionException("INBOUND_ALREADY_RECEIVED",409);
    }
    private async Task RequireReceiptSources(InboundReceiptDto dto,OperationsAccessV1 access,CancellationToken ct)
    {
        if(!string.IsNullOrWhiteSpace(dto.InboundNo))
        {
            await access.RequireSourceInbound(dto.InboundNo,ct);
            // Receipt authority covers the plan being changed as well as its own new object.
            await access.RequireInbound(dto.InboundNo,false,ct);
            if(!await _db.InboundOrders.AnyAsync(o=>o.InboundNo==dto.InboundNo && o.WarehouseCd==dto.WarehouseCd,ct))throw new OrderPriceCorrectionException("RECEIPT_SOURCE_MISMATCH",409);
            foreach(var d in dto.Details)
                if(d.RefOrderLineNo!=null && !await _db.InboundOrderDetails.AnyAsync(o=>o.InboundNo==dto.InboundNo && !o.IsDeleted && o.LineNo==d.RefOrderLineNo
                    && o.ProductCd==d.ProductCd,ct))
                    throw new OrderPriceCorrectionException("RECEIPT_SOURCE_MISMATCH",409);
        }
        else if(dto.Details.Any(d=>d.RefOrderLineNo!=null))throw new OrderPriceCorrectionException("RECEIPT_SOURCE_MISMATCH",409);
        // The existing manual WM060 screen supports independent/partial receipts.
        // Only automatic completion generation owns the one-receipt/all-good-quantity rule.
        if(!string.IsNullOrWhiteSpace(dto.WorkOrderNo))
        {
            await access.RequireSourceWorkOrder(dto.WorkOrderNo,ct);
        }
    }
    private async Task UpdateOwnedOrder(string no,InboundOrderDto dto,string actor,CancellationToken ct)
    {
        var h=await _db.InboundOrders.SingleAsync(o=>o.InboundNo==no && !o.IsDeleted,ct);
        if(h.Status is not (InboundOrderStatus.Draft or InboundOrderStatus.Confirmed))throw new InvalidOperationException("WM-MSG-043");
        h.InboundType=dto.InboundType;h.SupplierCd=dto.SupplierCd;h.SupplierName=dto.SupplierName;h.PoNo=dto.PoNo;
        h.ExpectedArrivalDate=dto.ExpectedArrivalDate;h.WarehouseCd=dto.WarehouseCd;h.Remarks=dto.Remarks;h.Modifier=actor;h.ModifyDate=DateTime.Now;
        var old=await _db.InboundOrderDetails.Where(d=>d.InboundNo==no).ToArrayAsync(ct);
        for(var i=0;i<dto.Details.Count;i++)
        {
            var d=dto.Details[i];var row=old.SingleOrDefault(x=>x.LineNo==i+1);
            if(row==null){row=new(){InboundNo=h.InboundNo,LineNo=i+1,Creator=actor};_db.InboundOrderDetails.Add(row);}
            if(row.IsDeleted)throw new OrderPriceCorrectionException("INBOUND_WRITE_INVALID",409);
            row.ProductCd=d.ProductCd;row.ProductName=d.ProductName;row.LotNo=d.LotNo;row.ExpectedQty=d.ExpectedQty;
            row.UnitCd=d.UnitCd;row.ExpectedLocationCd=d.ExpectedLocationCd;row.UnitPrice=d.UnitPrice;row.Remarks=d.Remarks;row.Modifier=actor;row.ModifyDate=DateTime.Now;
        }
        _db.InboundOrderDetails.RemoveRange(old.Where(d=>d.LineNo>dto.Details.Count));await _db.SaveChangesAsync(ct);
    }
}
