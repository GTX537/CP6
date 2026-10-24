using System.Globalization;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class ReportCenterService
{
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true?_owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private static OrderPriceCorrectionException Error(string code,int status)=>new(code,status);
    private Task<T> LegacyRead<T>()=>Owner.ExecuteAsync<T>(OperationsOwnerV1.ReportCenter,"query",false,(_,_)=>throw Error("OWNER_READ_VERSION_REQUIRED",409));
    public Task<List<MonthlyStockReportRow>> MonthlyStockReportAsync(string yearMonth,string? warehouseCd=null)
        =>_db.Database.IsNpgsql()?LegacyRead<List<MonthlyStockReportRow>>():MonthlyStockReportCoreAsync(yearMonth,warehouseCd);
    public Task<List<AbcAnalysisRow>> AbcAnalysisAsync(int analysisDays=90,string? warehouseCd=null)
        =>_db.Database.IsNpgsql()?LegacyRead<List<AbcAnalysisRow>>():AbcAnalysisCoreAsync(analysisDays,warehouseCd);
    public Task<List<DeadStockRow>> DeadStockAsync(int idleDays=90,string? warehouseCd=null)
        =>_db.Database.IsNpgsql()?LegacyRead<List<DeadStockRow>>():DeadStockCoreAsync(idleDays,warehouseCd);
    public Task<List<InboundHistoryRow>> InboundHistoryAsync(DateTime fromDate,DateTime toDate,string? warehouseCd=null,string? productCd=null)
        =>_db.Database.IsNpgsql()?LegacyRead<List<InboundHistoryRow>>():InboundHistoryCoreAsync(fromDate,toDate,warehouseCd,productCd);
    public Task<List<OutboundHistoryRow>> OutboundHistoryAsync(DateTime fromDate,DateTime toDate,string? warehouseCd=null,string? productCd=null)
        =>_db.Database.IsNpgsql()?LegacyRead<List<OutboundHistoryRow>>():OutboundHistoryCoreAsync(fromDate,toDate,warehouseCd,productCd);
    public Task RequireExportOwnerAsync()=>!_db.Database.IsNpgsql()?Task.CompletedTask:
        Owner.ExecuteAsync<bool>(OperationsOwnerV1.ReportCenter,"export",false,(_,_)=>throw Error("REPORT_SNAPSHOT_ADOPTION_UNPROVEN",503));
    public byte[] ExportCsv<T>(IEnumerable<T> rows)
    {
        if(!_db.Database.IsNpgsql())return ExportCsvCore(rows);
        // A detached IEnumerable has no current row/field authority or immutable snapshot identity.
        // Never enumerate or emit it on PostgreSQL. HTTP export must use the async current action gate first.
        _=Owner;throw Error("REPORT_EXPORT_CONTEXT_REQUIRED",503);
    }
    private static (DateTime? From,DateTime? To) Validate(ReportQueryV1 q,bool transactions)
    {
        if(q.Page<1||q.PageSize<1||q.PageSize>200||(long)(q.Page-1)*q.PageSize>int.MaxValue
            ||q.WarehouseCd?.Length>10||q.ProductCd?.Length>20||q.LotNo?.Length>30||q.TxnType?.Length>10)
            throw Error("REPORT_QUERY_INVALID",400);
        DateTime? Parse(string? value)
        {
            if(string.IsNullOrEmpty(value))return null;
            if(!DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))throw Error("REPORT_QUERY_INVALID",400);
            return DateTime.SpecifyKind(date,DateTimeKind.Unspecified);
        }
        var from=Parse(q.FromDate);var to=Parse(q.ToDate);
        if(from.HasValue!=to.HasValue||from>to||!transactions&&(from.HasValue||!string.IsNullOrEmpty(q.TxnType)))throw Error("REPORT_QUERY_INVALID",400);
        // TxnType is an exact original code filter, including legacy unknown codes; it does not determine quantity sign.
        return(from,to);
    }
    public Task<ReportPageV1<ReportStockRowV1>> SearchStockOwnerAsync(ReportQueryV1 input)
    {
        var q=OperationsOwnerV1.Freeze(input);
        return Owner.ExecuteAsync(OperationsOwnerV1.ReportCenter,"query",false,async(a,ct)=>
        {
            Validate(q,false);var rows=await a.ReportStocks(ct);
            if(!string.IsNullOrEmpty(q.WarehouseCd))rows=rows.Where(x=>x.WarehouseCd==q.WarehouseCd);
            if(!string.IsNullOrEmpty(q.ProductCd))rows=rows.Where(x=>x.ProductCd==q.ProductCd);
            if(q.LotNo!=null)rows=rows.Where(x=>x.LotNo==q.LotNo);
            var total=await rows.CountAsync(ct);
            var items=await rows.OrderBy(x=>x.WarehouseCd).ThenBy(x=>x.LocationCd).ThenBy(x=>x.ProductCd).ThenBy(x=>x.LotNo).ThenBy(x=>x.Id)
                .Skip((q.Page-1)*q.PageSize).Take(q.PageSize).ToArrayAsync(ct);
            return new ReportPageV1<ReportStockRowV1>{Source="CURRENT_STOCK",Total=total,Page=q.Page,PageSize=q.PageSize,Items=items.Select(a.ReportStock).ToList()};
        });
    }
    public Task<ReportPageV1<ReportTransactionRowV1>> SearchTransactionsOwnerAsync(ReportQueryV1 input)
    {
        var q=OperationsOwnerV1.Freeze(input);
        return Owner.ExecuteAsync(OperationsOwnerV1.ReportCenter,"query",false,async(a,ct)=>
        {
            var dates=Validate(q,true);var rows=await a.ReportTransactions(ct);
            if(!string.IsNullOrEmpty(q.WarehouseCd))rows=rows.Where(x=>x.WarehouseCd==q.WarehouseCd);
            if(!string.IsNullOrEmpty(q.ProductCd))rows=rows.Where(x=>x.ProductCd==q.ProductCd);
            if(q.LotNo!=null)rows=rows.Where(x=>x.LotNo==q.LotNo);
            if(!string.IsNullOrEmpty(q.TxnType))rows=rows.Where(x=>x.TxnType==q.TxnType);
            if(dates.From.HasValue)
            {
                rows=rows.Where(x=>x.TxnDateTime>=dates.From.Value);
                // Date comparison preserves every subsecond in the last day, including 9999-12-31, without AddDays overflow.
                rows=rows.Where(x=>x.TxnDateTime.Date<=dates.To!.Value);
            }
            var total=await rows.CountAsync(ct);
            var items=await rows.OrderByDescending(x=>x.TxnDateTime).ThenBy(x=>x.Id).Skip((q.Page-1)*q.PageSize).Take(q.PageSize).ToArrayAsync(ct);
            return new ReportPageV1<ReportTransactionRowV1>{Source="CURRENT_TRANSACTION_RECORDS",Total=total,Page=q.Page,PageSize=q.PageSize,Items=items.Select(a.ReportTransaction).ToList()};
        });
    }
}
