using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

/// <summary>Independent inventory reads; every query is scoped before paging or returning history.</summary>
public sealed class StockOperationsService(CP6Context db,OperationsOwnerV1? owner=null)
{
    private OperationsOwnerV1 Owner=>owner?.Owns(db)==true?owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task AuthorizeUpdatesAsync(CancellationToken ct=default)
        =>Owner.ExecuteAsync(OperationsOwnerV1.Stock,"query",false,(a,t)=>Task.FromResult(true),ct);
    public Task<StockPage<Stock>> SearchAsync(StockSearchRequest request,CancellationToken ct=default)
    {
        var q=OperationsOwnerV1.Freeze(request);
        return Owner.ExecuteAsync(OperationsOwnerV1.Stock,"query",false,async(a,t)=>
        {
            var rows=a.InventoryStocks();
            if(!string.IsNullOrWhiteSpace(q.WarehouseCd))rows=rows.Where(s=>s.WarehouseCd==q.WarehouseCd);
            if(!string.IsNullOrWhiteSpace(q.LocationCd))rows=rows.Where(s=>s.LocationCd==q.LocationCd);
            if(!string.IsNullOrWhiteSpace(q.ProductCd))rows=rows.Where(s=>s.ProductCd.Contains(q.ProductCd));
            if(!string.IsNullOrWhiteSpace(q.LotNo))rows=rows.Where(s=>s.LotNo==q.LotNo);
            if(!string.IsNullOrWhiteSpace(q.OwnerType))rows=rows.Where(s=>s.OwnerType==q.OwnerType);
            if(!string.IsNullOrWhiteSpace(q.OwnerCd))rows=rows.Where(s=>s.OwnerCd==q.OwnerCd);
            if(q.HasStockOnly==true)rows=rows.Where(s=>s.PhysicalQty!=0);
            var total=await rows.CountAsync(t);var items=await rows.OrderBy(s=>s.WarehouseCd).ThenBy(s=>s.LocationCd).ThenBy(s=>s.ProductCd).ThenBy(s=>s.LotNo)
                .Skip((q.Page-1)*q.PageSize).Take(q.PageSize).ToListAsync(t);
            if(!a.PriceReadable)foreach(var item in items)item.UnitPrice=null;
            return new StockPage<Stock>(total,q.Page,q.PageSize,items);
        },ct);
    }
    public Task<StockHistory> HistoryAsync(Guid id,int days=90,CancellationToken ct=default)
        =>Owner.ExecuteAsync(OperationsOwnerV1.Stock,"query",false,async(a,t)=>
        {
            var stock=await a.InventoryStocks().SingleOrDefaultAsync(s=>s.Id==id,t)??throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
            var since=DateTime.Today.AddDays(-days);
            var transactions=await a.InventoryTransactions().Where(s=>s.WarehouseCd==stock.WarehouseCd && s.LocationCd==stock.LocationCd
                && s.ProductCd==stock.ProductCd && s.LotNo==stock.LotNo && s.TxnDateTime>=since).OrderByDescending(s=>s.TxnDateTime).ToListAsync(t);
            if(!a.PriceReadable){stock.UnitPrice=null;foreach(var row in transactions)row.UnitPrice=null;}
            return new StockHistory(stock,transactions);
        },ct);
    public Task<StockPage<StockTransaction>> TransactionsAsync(StockTransactionSearchRequest request,CancellationToken ct=default)
    {
        var q=OperationsOwnerV1.Freeze(request);
        return Owner.ExecuteAsync(OperationsOwnerV1.Stock,"query",false,async(a,t)=>
        {
            var rows=a.InventoryTransactions();
            if(!string.IsNullOrWhiteSpace(q.ProductCd))rows=rows.Where(s=>s.ProductCd==q.ProductCd);
            if(!string.IsNullOrWhiteSpace(q.LotNo))rows=rows.Where(s=>s.LotNo==q.LotNo);
            if(!string.IsNullOrWhiteSpace(q.TxnType))rows=rows.Where(s=>s.TxnType==q.TxnType);
            if(q.From.HasValue)rows=rows.Where(s=>s.TxnDateTime>=q.From.Value);
            if(q.To.HasValue)rows=rows.Where(s=>s.TxnDateTime<=q.To.Value);
            var total=await rows.CountAsync(t);var items=await rows.OrderByDescending(s=>s.TxnDateTime).Skip((q.Page-1)*q.PageSize).Take(q.PageSize).ToListAsync(t);
            if(!a.PriceReadable)foreach(var row in items)row.UnitPrice=null;
            return new StockPage<StockTransaction>(total,q.Page,q.PageSize,items);
        },ct);
    }
}
public sealed record StockPage<T>(int Total,int Page,int PageSize,List<T> Items);
public sealed record StockHistory(Stock Stock,List<StockTransaction> Transactions);
public sealed class StockSearchRequest
{
    public string? WarehouseCd{get;set;} public string? LocationCd{get;set;} public string? ProductCd{get;set;} public string? LotNo{get;set;}
    public string? OwnerType{get;set;} public string? OwnerCd{get;set;} public bool? HasStockOnly{get;set;}
    public int Page{get;set;}=1; public int PageSize{get;set;}=50;
}
public sealed class StockTransactionSearchRequest
{
    public string? ProductCd{get;set;} public string? LotNo{get;set;} public string? TxnType{get;set;}
    public DateTime? From{get;set;} public DateTime? To{get;set;}
    public int Page{get;set;}=1; public int PageSize{get;set;}=100;
}
