using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class WmsDashboardService
{
    private sealed record WmsDashboardData(IQueryable<Stock> Stocks,IQueryable<StockTransaction> StockTransactions,
        IQueryable<InboundOrder> InboundOrders,IQueryable<OutboundOrder> OutboundOrders,IQueryable<StockTake> StockTakes);
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true?_owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    private async Task<WmsDashboardData> Data(OperationsAccessV1 a,bool price,bool plans,bool outbound,CancellationToken ct)
    {
        var stocks=await a.SourceAsync(OperationsOwnerV1.Stock,ct);
        if(price){a.RequireAmountRead();stocks.RequirePriceRead();}
        var inboundRows=_db.InboundOrders.Where(x=>false).AsNoTracking();
        var outboundRows=_db.OutboundOrders.Where(x=>false).AsNoTracking();
        var takeRows=_db.StockTakes.Where(x=>false).AsNoTracking();
        if(plans)
        {
            var inbound=await a.SourceAsync(OperationsOwnerV1.Inbound,ct);var takes=await a.SourceAsync(OperationsOwnerV1.StockTake,ct);
            var allowedInbound=inbound.Inbounds().Select(x=>x.Id);var allowedTakes=takes.StockTakes().Select(x=>x.Id);
            inboundRows=a.Inbounds().Where(x=>allowedInbound.Contains(x.Id));takeRows=a.StockTakes().Where(x=>allowedTakes.Contains(x.Id));
        }
        if(outbound)
        {
            var source=await a.SourceAsync(OperationsOwnerV1.Outbound,ct);var ids=source.Outbounds().Select(x=>x.Id);
            outboundRows=a.Outbounds().Where(x=>ids.Contains(x.Id));
        }
        return new(a.InventoryStocks(stocks.InventoryStocks()),a.InventoryTransactions(stocks.InventoryTransactions()),inboundRows,outboundRows,takeRows);
    }
    public Task<WmsKpiDto> GetKpiAsync()=>!_db.Database.IsNpgsql()?GetKpiCoreAsync():
        Owner.ExecuteAsync(OperationsOwnerV1.WmsDashboard,"query",false,async(a,ct)=>await GetKpiCoreAsync(await Data(a,true,true,true,ct)));
    public Task<List<WmsTrendPoint>> GetTransactionTrendAsync(int days=30)=>!_db.Database.IsNpgsql()?GetTransactionTrendCoreAsync(days):
        Owner.ExecuteAsync(OperationsOwnerV1.WmsDashboard,"query",false,async(a,ct)=>await GetTransactionTrendCoreAsync(days,await Data(a,false,false,false,ct)));
    public Task<List<WmsWarehouseValueDto>> GetWarehouseValueAsync()=>!_db.Database.IsNpgsql()?GetWarehouseValueCoreAsync():
        Owner.ExecuteAsync(OperationsOwnerV1.WmsDashboard,"query",false,async(a,ct)=>await GetWarehouseValueCoreAsync(await Data(a,true,false,false,ct)));
    public Task<WmsAlertSummaryDto> GetAlertsAsync()=>!_db.Database.IsNpgsql()?GetAlertsCoreAsync():
        Owner.ExecuteAsync(OperationsOwnerV1.WmsDashboard,"query",false,async(a,ct)=>await GetAlertsCoreAsync(await Data(a,false,true,false,ct)));
}
