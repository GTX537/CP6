using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using CP6.Core.Services.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal async Task<IQueryable<Stock>> ReportStocks(CancellationToken ct)
    {
        var source=await SourceAsync(OperationsOwnerV1.Stock,ct);var ids=source.InventoryStocks().Select(x=>x.Id);
        return InventoryStocks().Where(x=>ids.Contains(x.Id)
            && db.Warehouses.Any(w=>!w.IsDeleted && w.WarehouseCd==x.WarehouseCd)
            && db.Locations.Any(l=>!l.IsDeleted && l.WarehouseCd==x.WarehouseCd && l.LocationCd==x.LocationCd));
    }
    internal async Task<IQueryable<StockTransaction>> ReportTransactions(CancellationToken ct)
    {
        var source=await SourceAsync(OperationsOwnerV1.Stock,ct);var ids=source.InventoryTransactions().Select(x=>x.Id);
        return InventoryTransactions().Where(x=>ids.Contains(x.Id)
            && db.Warehouses.Any(w=>!w.IsDeleted && w.WarehouseCd==x.WarehouseCd)
            && db.Locations.Any(l=>!l.IsDeleted && l.WarehouseCd==x.WarehouseCd && l.LocationCd==x.LocationCd));
    }
    private bool ReportRead(string field)=>authority.Fields.Fields[field].CanRead;
    internal ReportStockRowV1 ReportStock(Stock x)=>new(){Id=x.Id,WarehouseCd=x.WarehouseCd,LocationCd=x.LocationCd,
        ProductCd=x.ProductCd,LotNo=x.LotNo,PhysicalQty=InventoryReportValuesV1.Exact(x.PhysicalQty).ToString(),
        LegacyAllocatedQty=InventoryReportValuesV1.Exact(x.AllocatedQty).ToString(),LegacyAvailableQty=InventoryReportValuesV1.Exact(x.AvailableQty).ToString(),
        UnitCd=x.UnitCd,OwnerType=x.OwnerType,OwnerCdReadable=ReportRead("OwnerCd"),OwnerCd=ReportRead("OwnerCd")?x.OwnerCd:null,
        ReceiveDate=x.ReceiveDate,ExpiryDate=x.ExpiryDate};
    internal ReportTransactionRowV1 ReportTransaction(StockTransaction x)=>new(){Id=x.Id,TxnNo=x.TxnNo,TxnType=x.TxnType,TxnDateTime=x.TxnDateTime,
        WarehouseCd=x.WarehouseCd,LocationCd=x.LocationCd,ProductCd=x.ProductCd,LotNo=x.LotNo,Qty=InventoryReportValuesV1.Exact(x.Qty).ToString(),
        UnitCd=x.UnitCd,OwnerType=x.OwnerType,OwnerCdReadable=ReportRead("OwnerCd"),OwnerCd=ReportRead("OwnerCd")?x.OwnerCd:null,
        OperatorCdReadable=ReportRead("OperatorCd"),OperatorCd=ReportRead("OperatorCd")?x.OperatorCd:null,
        RemarkReadable=ReportRead("Remark"),Remark=ReportRead("Remark")?x.Remark:null};
}
