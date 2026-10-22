using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class StockQcService
{
    internal bool Owns(CP6Context context)=>ReferenceEquals(_db,context);
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true?_owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);

    public Task<Stock> SetStockQcStatusAsync(Guid id,string status,string? reason,string? userName)
    {
        if(!_db.Database.IsNpgsql())return SetStockQcStatusCoreAsync(id,status,reason,userName);
        return Owner.ExecuteAsync(OperationsOwnerV1.StockQc,"set",true,async(a,ct)=>
        {
            ValidateStatus(status);var source=await a.SourceAsync(OperationsOwnerV1.Stock,ct);
            async Task Require(CancellationToken t)
            {
                if(!await a.InventoryStocks().AnyAsync(s=>s.Id==id,t) || !await source.InventoryStocks().AnyAsync(s=>s.Id==id,t))
                    throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
            }
            await Require(ct);var row=await _db.Stocks.SingleAsync(s=>s.Id==id,ct);
            row.QcStatus=status;row.Modifier=a.Actor;row.ModifyDate=DateTime.Now;await _db.SaveChangesAsync(ct);
            a.AtCommit(Require);
            // The independent QC response never discloses a hidden inventory price.
            var result=OperationsOwnerV1.Freeze(row);if(!source.PriceReadable)result.UnitPrice=null;return result;
        });
    }

    public Task<int> MarkLinkedStockByWorkOrderAsync(string number,string status,string? reason,string? userName)
    {
        if(!_db.Database.IsNpgsql())return MarkLinkedStockByWorkOrderCoreAsync(number,status,reason,userName);
        return Owner.ExecuteAsync(OperationsOwnerV1.StockQc,"set",true,async(a,ct)=>
        {
            ValidateStatus(status);var source=await a.SourceAsync(OperationsOwnerV1.Mes,ct);
            var inventory=await a.SourceAsync(OperationsOwnerV1.Stock,ct);
            return await MarkLinkedOwned(a,source,inventory,number,status,ct);
        });
    }

    internal async Task<int> MarkFailedFromQualityAsync(OperationsAccessV1 parent,string inspection,CancellationToken ct)
    {
        parent.RequireQualityParent(_db,OperationsOwnerV1.Quality,"add");
        var source=await parent.SourceAsync(OperationsOwnerV1.Mes,ct);
        await parent.RequireQualityInspection(inspection,source,ct);
        var row=await _db.QualityInspections.AsNoTracking().SingleAsync(q=>q.InspectionNo==inspection && !q.IsDeleted,ct);
        if(row.OverallResult!=2 || row.Creator!=parent.Actor)
            throw new OrderPriceCorrectionException("INTERNAL_QUALITY_CAPABILITY_REQUIRED",403);
        return await MarkLinkedOwned(parent,source,null,row.WorkOrderNo,StockQcStatus.Failed,ct);
    }

    private async Task<int> MarkLinkedOwned(OperationsAccessV1 access,OperationsAccessV1 source,OperationsAccessV1? inventory,string number,string status,CancellationToken ct)
    {
        var ids=await access.RequireLinkedQualityStocks(number,source,ct);
        async Task Require(CancellationToken t)
        {
            var current=await access.RequireLinkedQualityStocks(number,source,t);
            if(!ids.SequenceEqual(current))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
            if(inventory!=null)
            {
                var allowed=inventory.InventoryStocks();
                if(await _db.Stocks.AnyAsync(s=>ids.Contains(s.Id) && !allowed.Any(a=>a.Id==s.Id),t))
                    throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
            }
        }
        await Require(ct);
        var rows=await _db.Stocks.Where(s=>ids.Contains(s.Id)).ToArrayAsync(ct);
        foreach(var row in rows){row.QcStatus=status;row.Modifier=access.Actor;row.ModifyDate=DateTime.Now;}
        await _db.SaveChangesAsync(ct);access.AtCommit(Require);return rows.Length;
    }
}
