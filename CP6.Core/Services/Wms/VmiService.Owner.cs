using System.Text.RegularExpressions;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class VmiService
{
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true?_owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private static OrderPriceCorrectionException Error(string code,int status)=>new(code,status);
    private Task<T> LegacyRead<T>()=>Owner.ExecuteAsync<T>(OperationsOwnerV1.Vmi,"query",false,(_,_)=>throw Error("OWNER_READ_VERSION_REQUIRED",409));
    public Task<List<VmiCustomerSummary>> SearchByCustomerAsync(string? customerCd)=>!_db.Database.IsNpgsql()?SearchByCustomerCoreAsync(customerCd):LegacyRead<List<VmiCustomerSummary>>();
    public Task<List<VmiStockDetail>> GetDetailsAsync(string customerCd)=>!_db.Database.IsNpgsql()?GetDetailsCoreAsync(customerCd):LegacyRead<List<VmiStockDetail>>();
    public Task<List<VmiBilling>> SearchBillingsAsync(string? customerCd,string? yearMonth,bool? confirmed)=>!_db.Database.IsNpgsql()?SearchBillingsCoreAsync(customerCd,yearMonth,confirmed):LegacyRead<List<VmiBilling>>();
    private Task<T> RejectUnadopted<T>(string action,string? no=null)=>Owner.ExecuteAsync<T>(OperationsOwnerV1.Vmi,action,false,async(a,ct)=>
    {
        if(no!=null&&!await a.VmiBillings().AnyAsync(x=>x.BillingNo==no,ct))throw Error("OBJECT_NOT_FOUND",404);
        throw Error("VMI_OWNER_ADOPTION_UNPROVEN",503);
    });
    public Task<int> CalculateMonthlyBillingAsync(string yearMonth,decimal dailyStorageRate,string? userName)=>!_db.Database.IsNpgsql()?CalculateMonthlyBillingCoreAsync(yearMonth,dailyStorageRate,userName):RejectUnadopted<int>("calculate");
    public Task ConfirmBillingAsync(string no,string? userName)=>!_db.Database.IsNpgsql()?ConfirmBillingCoreAsync(no,userName):RejectUnadopted<bool>("confirm",no);
    private static void Validate(VmiQueryV1 q,bool billing)
    {
        if(q.Page<1||q.PageSize<1||q.PageSize>100||(long)(q.Page-1)*q.PageSize>int.MaxValue||q.CustomerCd?.Length>20
            ||(!billing&&(q.YearMonth!=null||q.Confirmed!=null)))throw Error("VMI_QUERY_INVALID",400);
        if(!string.IsNullOrEmpty(q.YearMonth)&&(!Regex.IsMatch(q.YearMonth,@"\A[0-9]{6}\z")||int.Parse(q.YearMonth[..4]) is <1 or >9999
            ||int.Parse(q.YearMonth[4..]) is <1 or >12))throw Error("INVALID_YEAR_MONTH",400);
    }
    public Task<VmiPageV1<VmiCustomerObservationV1>> SearchCustomersOwnerAsync(VmiQueryV1 input)
    {
        var q=OperationsOwnerV1.Freeze(input);
        return Owner.ExecuteAsync(OperationsOwnerV1.Vmi,"query",false,async(a,ct)=>
        {
            Validate(q,false);var rows=await a.VmiStocks(ct);
            if(!string.IsNullOrWhiteSpace(q.CustomerCd))rows=rows.Where(x=>x.OwnerCd==q.CustomerCd);
            // SUM remains PostgreSQL numeric, then becomes text BEFORE materialization. No decimal/float aggregate boundary.
            var groups=rows.GroupBy(x=>new{x.OwnerCd,x.UnitCd}).Select(g=>new VmiCustomerObservationV1
            {
                CustomerCd=g.Key.OwnerCd!,UnitCd=g.Key.UnitCd,StockRowCount=g.Count(),SkuCount=g.Select(x=>x.ProductCd).Distinct().Count(),
                PhysicalQty=g.Key.UnitCd==null||g.Key.UnitCd.Trim()==""?null:g.Sum(x=>x.PhysicalQty).ToString(),
                LegacyAllocatedQty=g.Key.UnitCd==null||g.Key.UnitCd.Trim()==""?null:g.Sum(x=>x.AllocatedQty).ToString(),
                LegacyAvailableQty=g.Key.UnitCd==null||g.Key.UnitCd.Trim()==""?null:g.Sum(x=>x.AvailableQty).ToString()
            });
            var total=await groups.CountAsync(ct);
            var items=await groups.OrderBy(x=>x.CustomerCd).ThenBy(x=>x.UnitCd).Skip((q.Page-1)*q.PageSize).Take(q.PageSize).ToListAsync(ct);
            return new VmiPageV1<VmiCustomerObservationV1>{Source="CURRENT_STOCK_OBSERVATION",Total=total,Page=q.Page,PageSize=q.PageSize,Items=items};
        });
    }
    public Task<VmiPageV1<VmiStockObservationV1>> SearchStockOwnerAsync(string customerCd,VmiQueryV1 input)
    {
        var q=OperationsOwnerV1.Freeze(input);
        return Owner.ExecuteAsync(OperationsOwnerV1.Vmi,"query",false,async(a,ct)=>
        {
            Validate(q,false);
            if(string.IsNullOrWhiteSpace(customerCd)||customerCd.Length>20||q.CustomerCd!=null&&q.CustomerCd!=customerCd)throw Error("VMI_QUERY_INVALID",400);
            var rows=(await a.VmiStocks(ct)).Where(x=>x.OwnerCd==customerCd);var total=await rows.CountAsync(ct);
            var items=await rows.OrderBy(x=>x.ProductCd).ThenBy(x=>x.LotNo).ThenBy(x=>x.Id).Skip((q.Page-1)*q.PageSize).Take(q.PageSize).ToArrayAsync(ct);
            return new VmiPageV1<VmiStockObservationV1>{Source="CURRENT_STOCK_OBSERVATION",Total=total,Page=q.Page,PageSize=q.PageSize,
                Items=items.Select(x=>new VmiStockObservationV1{Id=x.Id,CustomerCd=x.OwnerCd!,ProductCd=x.ProductCd,LotNo=x.LotNo,WarehouseCd=x.WarehouseCd,LocationCd=x.LocationCd,UnitCd=x.UnitCd,
                    PhysicalQty=InventoryReportValuesV1.Exact(x.PhysicalQty).ToString(),LegacyAllocatedQty=InventoryReportValuesV1.Exact(x.AllocatedQty).ToString(),LegacyAvailableQty=InventoryReportValuesV1.Exact(x.AvailableQty).ToString(),ReceiveDate=x.ReceiveDate,ExpiryDate=x.ExpiryDate}).ToList()};
        });
    }
    public Task<VmiPageV1<VmiBillingObservationV1>> SearchBillingsOwnerAsync(VmiQueryV1 input)
    {
        var q=OperationsOwnerV1.Freeze(input);
        return Owner.ExecuteAsync(OperationsOwnerV1.Vmi,"query",false,async(a,ct)=>
        {
            Validate(q,true);var rows=a.VmiBillings();
            if(!string.IsNullOrWhiteSpace(q.CustomerCd))rows=rows.Where(x=>x.CustomerCd==q.CustomerCd);
            if(!string.IsNullOrEmpty(q.YearMonth))rows=rows.Where(x=>x.YearMonth==q.YearMonth);
            if(q.Confirmed.HasValue)rows=rows.Where(x=>x.Confirmed==q.Confirmed);
            var total=await rows.CountAsync(ct);var items=await rows.OrderByDescending(x=>x.YearMonth).ThenBy(x=>x.CustomerCd).ThenBy(x=>x.Id).Skip((q.Page-1)*q.PageSize).Take(q.PageSize).ToArrayAsync(ct);
            var reads=await a.VmiSensitiveRead(ct);
            return new VmiPageV1<VmiBillingObservationV1>{Source="HISTORICAL_BILLING_ESTIMATE",Total=total,Page=q.Page,PageSize=q.PageSize,Items=items.Select(x=>a.ProjectVmiBilling(x,reads)).ToList()};
        });
    }
}
