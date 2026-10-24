using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal async Task<IQueryable<Stock>> VmiStocks(CancellationToken ct)
    {
        var source=await SourceAsync(OperationsOwnerV1.Stock,ct);
        var sourceIds=source.InventoryStocks().Select(x=>x.Id);
        return InventoryStocks().Where(s=>s.OwnerType==StockOwnerType.Customer && s.OwnerCd!=null && s.OwnerCd!=""
            && sourceIds.Contains(s.Id) && db.Warehouses.Any(w=>!w.IsDeleted && w.WarehouseCd==s.WarehouseCd)
            && db.Locations.Any(l=>!l.IsDeleted && l.WarehouseCd==s.WarehouseCd && l.LocationCd==s.LocationCd));
    }
    internal IQueryable<VmiBilling> VmiBillings()
    {
        var rows=db.VmiBillings.AsNoTracking().Where(x=>!x.IsDeleted);
        if(authority.Scope==1)rows=rows.Where(x=>x.Creator==Actor);
        else if(authority.Scope!=5)
        {var ids=StandaloneDepartmentScopeV1.Objects(db,authority,"vmi-billing");rows=rows.Where(x=>ids.Contains(x.Id));}
        return rows;
    }
    internal async Task<(bool Amount,bool Rate)> VmiSensitiveRead(CancellationToken ct)
    {
        async Task<bool> Allowed(string action,string field)
        {
            if(!authority.Fields.Fields[field].CanRead)return false;
            try{await ActionAsync(OperationsOwnerV1.Vmi,action,ct);return true;}
            catch(OrderPriceCorrectionException e)when(e.Message=="ACTION_FORBIDDEN"){return false;}
        }
        return(await Allowed("amount-read","BillingAmount"),await Allowed("rate-read","DailyStorageRate"));
    }
    internal VmiBillingObservationV1 ProjectVmiBilling(VmiBilling row,(bool Amount,bool Rate) reads)
    {
        bool Read(string field)=>authority.Fields.Fields[field].CanRead;
        return new(){Id=row.Id,BillingNo=row.BillingNo,CustomerCd=row.CustomerCd,YearMonth=row.YearMonth,LegacySkuCount=row.SkuCount,
            BeginQty=InventoryReportValuesV1.Exact(row.BeginQty).ToString(),EndQty=InventoryReportValuesV1.Exact(row.EndQty).ToString(),AvgQty=InventoryReportValuesV1.Exact(row.AvgQty).ToString(),
            AmountReadable=reads.Amount,RateReadable=reads.Rate,BillingAmount=reads.Amount?InventoryReportValuesV1.Exact(row.BillingAmount).ToString():null,
            DailyStorageRate=reads.Rate?InventoryReportValuesV1.Exact(row.DailyStorageRate).ToString():null,LegacyConfirmedFlag=row.Confirmed,CalculatedAt=row.CalculatedAt,
            CustomerNameReadable=Read("CustomerName"),CustomerName=Read("CustomerName")?row.CustomerName:null,RemarksReadable=Read("Remarks"),Remarks=Read("Remarks")?row.Remarks:null};
    }
}
