using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal void RequireQualityParent(CP6Context context,string resource,string action)
    {
        if(!ReferenceEquals(context,db) || !db.Database.IsNpgsql() || db.Database.CurrentTransaction==null
            || authority.Resource!=resource || authority.Action!=action)
            throw new OrderPriceCorrectionException("INTERNAL_QUALITY_CAPABILITY_REQUIRED",403);
    }

    internal async Task<OperationsAccessV1?> OptionalInboundSource(CancellationToken ct)
    {
        try{return await SourceAsync(OperationsOwnerV1.Inbound,ct);}
        catch(OrderPriceCorrectionException e) when(e.Message=="ACTION_FORBIDDEN"){return null;}
    }

    internal IQueryable<QcInspection> InboundInspections(OperationsAccessV1? source)
    {
        var rows=db.QcInspections.AsNoTracking().Where(q=>!q.IsDeleted);
        if(authority.Scope==1)rows=rows.Where(q=>q.Creator==Actor && !db.QcInspectionItems.Any(i=>i.InspectionNo==q.InspectionNo && (i.Creator==null || i.Creator!=Actor)));
        else if(authority.Scope!=5)rows=rows.Where(q=>false); // No accepted department binding on direct/purchase QC.
        var inbounds=Inbounds();var receipts=Receipts();
        if(source==null)rows=rows.Where(q=>q.InboundNo==null || q.InboundNo=="");
        else
        {
            var sourceRows=source.Inbounds();
            rows=rows.Where(q=>q.InboundNo==null || q.InboundNo=="" || sourceRows.Any(i=>i.InboundNo==q.InboundNo));
        }
        rows=rows.Where(q=>(q.InboundNo==null || q.InboundNo=="" || inbounds.Any(i=>i.InboundNo==q.InboundNo))
            && (q.GeneratedReceiptNo==null || q.GeneratedReceiptNo=="" || receipts.Any(r=>r.ReceiptNo==q.GeneratedReceiptNo)));
        return rows;
    }
    internal async Task RequireInboundInspection(string number,OperationsAccessV1? source,CancellationToken ct)
    {
        if(!await InboundInspections(source).AnyAsync(q=>q.InspectionNo==number,ct))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
    }

    internal IQueryable<QualityInspection> QualityInspections(OperationsAccessV1 source)
    {
        var orders=WorkOrders();var sourceOrders=source.WorkOrders();
        var rows=DefectInspections();
        if(authority.Scope==1)rows=rows.Where(q=>!db.DefectRecords.Any(d=>d.InspectionNo==q.InspectionNo && (d.Creator==null || d.Creator!=Actor)));
        return rows.Where(q=>orders.Any(w=>w.WorkOrderNo==q.WorkOrderNo) && sourceOrders.Any(w=>w.WorkOrderNo==q.WorkOrderNo)
            && (q.ProcessCd==null || q.ProcessCd=="" || db.WorkOrderProcesses.Any(p=>p.WorkOrderNo==q.WorkOrderNo && p.ProcessCd==q.ProcessCd && !p.IsDeleted))
            && !db.DefectRecords.Any(d=>d.InspectionNo==q.InspectionNo && (d.WorkOrderNo!=q.WorkOrderNo
                || d.ProcessCd!=null && d.ProcessCd!="" && !db.WorkOrderProcesses.Any(p=>p.WorkOrderNo==d.WorkOrderNo && p.ProcessCd==d.ProcessCd && !p.IsDeleted))));
    }
    internal async Task RequireQualityInspection(string number,OperationsAccessV1 source,CancellationToken ct)
    {
        if(!await QualityInspections(source).AnyAsync(q=>q.InspectionNo==number,ct))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
    }

    internal IQueryable<Stock> LinkedQualityStocks(string workOrder)
        =>db.Stocks.AsNoTracking().Where(s=>!s.IsDeleted && (from r in db.InboundReceipts
            join d in db.InboundReceiptDetails on r.ReceiptNo equals d.ReceiptNo
            where !r.IsDeleted && !d.IsDeleted && r.WorkOrderNo==workOrder
                && r.WarehouseCd==s.WarehouseCd && d.LocationCd==s.LocationCd && d.ProductCd==s.ProductCd && d.LotNo==s.LotNo
            select d.Id).Any());

    internal async Task<Guid[]> RequireLinkedQualityStocks(string workOrder,OperationsAccessV1 source,CancellationToken ct)
    {
        await RequireWorkOrder(workOrder,false,ct);await source.RequireWorkOrder(workOrder,false,ct);
        await RequireActiveAssociations(null,workOrder,ct);
        var receipts=Receipts();
        if(await db.InboundReceipts.AnyAsync(r=>!r.IsDeleted && r.WorkOrderNo==workOrder && !receipts.Any(a=>a.Id==r.Id),ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
        var targets=LinkedQualityStocks(workOrder);var permitted=Stocks(targets);
        if(authority.Scope==1)permitted=permitted.Where(s=>s.Creator==Actor);
        if(await targets.AnyAsync(s=>!permitted.Any(a=>a.Id==s.Id),ct))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
        return await targets.Select(s=>s.Id).OrderBy(id=>id).ToArrayAsync(ct);
    }
}
