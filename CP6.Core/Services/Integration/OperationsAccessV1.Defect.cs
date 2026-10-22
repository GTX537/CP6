using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    private IQueryable<QualityInspection> DefectInspections()
    {
        var rows=db.QualityInspections.AsNoTracking().Where(q=>!q.IsDeleted);
        if(authority.Scope==1)rows=rows.Where(q=>q.Creator==Actor && !db.QualityInspectionItems.Any(i=>i.InspectionNo==q.InspectionNo && (i.Creator==null || i.Creator!=Actor)));
        return rows;
    }
    internal IQueryable<DefectRecord> Defects(OperationsAccessV1 source,bool includeDeleted=false)
    {
        var orders=WorkOrders();var sourceOrders=source.WorkOrders();var inspections=DefectInspections();
        var rows=db.DefectRecords.AsNoTracking().Where(d=>includeDeleted || !d.IsDeleted);
        if(authority.Scope==1)rows=rows.Where(d=>d.Creator==Actor);
        return rows.Where(d=>orders.Any(w=>w.WorkOrderNo==d.WorkOrderNo) && sourceOrders.Any(w=>w.WorkOrderNo==d.WorkOrderNo)
            && (d.ProcessCd==null || d.ProcessCd=="" || db.WorkOrderProcesses.Any(p=>p.WorkOrderNo==d.WorkOrderNo && p.ProcessCd==d.ProcessCd && !p.IsDeleted))
            && (d.InspectionNo==null || d.InspectionNo=="" || inspections.Any(q=>q.InspectionNo==d.InspectionNo && q.WorkOrderNo==d.WorkOrderNo
                && (q.ProcessCd==null || q.ProcessCd=="" || db.WorkOrderProcesses.Any(p=>p.WorkOrderNo==q.WorkOrderNo && p.ProcessCd==q.ProcessCd && !p.IsDeleted)))));
    }
    internal async Task RequireDefect(string number,OperationsAccessV1 source,CancellationToken ct,bool includeDeleted=false)
    {
        if(!await Defects(source,includeDeleted).AnyAsync(d=>d.DefectNo==number,ct))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
    }
    internal async Task RequireDefectLinks(string number,string? process,string? inspection,OperationsAccessV1 source,CancellationToken ct)
    {
        if(!await WorkOrders().AnyAsync(w=>w.WorkOrderNo==number,ct) || !await source.WorkOrders().AnyAsync(w=>w.WorkOrderNo==number,ct)
            || process!=null && process!="" && !await db.WorkOrderProcesses.AnyAsync(p=>p.WorkOrderNo==number && p.ProcessCd==process && !p.IsDeleted,ct)
            || inspection!=null && inspection!="" && !await DefectInspections().AnyAsync(q=>q.InspectionNo==inspection && q.WorkOrderNo==number
                && (q.ProcessCd==null || q.ProcessCd=="" || db.WorkOrderProcesses.Any(p=>p.WorkOrderNo==q.WorkOrderNo && p.ProcessCd==q.ProcessCd && !p.IsDeleted)),ct))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
    }
}
