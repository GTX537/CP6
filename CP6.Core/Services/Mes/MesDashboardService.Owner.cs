using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

public partial class MesDashboardService
{
    private sealed record MesDashboardData(IQueryable<WorkOrder> WorkOrders,IQueryable<WorkOrderProcess> WorkOrderProcesses,
        IQueryable<ProductionResult> ProductionResults,IQueryable<DefectRecord> DefectRecords);
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true?_owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task AuthorizeUpdatesAsync(CancellationToken ct=default)
        =>Owner.ExecuteAsync(OperationsOwnerV1.MesDashboard,"query",false,(_,_)=>Task.FromResult(true),ct,readOnlyAuthorityFence:true);
    private async Task<MesDashboardData> Data(OperationsAccessV1 a,bool work,bool production,bool defect,CancellationToken ct)
    {
        var orders=a.DashboardWorkOrders();
        if(work)
        {
            var source=await a.SourceAsync(OperationsOwnerV1.Mes,ct);var ids=source.WorkOrders().Select(x=>x.Id);
            orders=orders.Where(w=>ids.Contains(w.Id));
        }
        var processes=_db.WorkOrderProcesses.AsNoTracking().Where(p=>!p.IsDeleted && orders.Any(w=>w.WorkOrderNo==p.WorkOrderNo));
        var results=_db.ProductionResults.AsNoTracking().Where(r=>false);
        var defects=_db.DefectRecords.AsNoTracking().Where(r=>false);
        if(production)
        {
            var source=await a.SourceAsync(OperationsOwnerV1.Production,ct);
            results=source.ProductionResults().Where(r=>orders.Any(w=>w.WorkOrderNo==r.WorkOrderNo));
        }
        if(defect)
        {
            var source=await a.SourceAsync(OperationsOwnerV1.Defect,ct);
            defects=source.DashboardDefects().Where(r=>orders.Any(w=>w.WorkOrderNo==r.WorkOrderNo));
        }
        return new(orders,processes,results,defects);
    }
    public Task<MesDashboardSummaryDto> GetSummaryAsync()=>!_db.Database.IsNpgsql()?GetSummaryCoreAsync():
        Owner.ExecuteAsync(OperationsOwnerV1.MesDashboard,"query",false,async(a,ct)=>await GetSummaryCoreAsync(await Data(a,true,true,false,ct)));
    public Task<List<ProcessProgressDto>> GetProcessProgressAsync()=>!_db.Database.IsNpgsql()?GetProcessProgressCoreAsync():
        Owner.ExecuteAsync(OperationsOwnerV1.MesDashboard,"query",false,async(a,ct)=>await GetProcessProgressCoreAsync(await Data(a,true,false,false,ct)));
    public Task<List<DelayAlertDto>> GetDelayAlertsAsync(int top=50)=>!_db.Database.IsNpgsql()?GetDelayAlertsCoreAsync(top):
        Owner.ExecuteAsync(OperationsOwnerV1.MesDashboard,"query",false,async(a,ct)=>await GetDelayAlertsCoreAsync(top,await Data(a,true,false,false,ct)));
    public Task<List<DailyTrendDto>> GetDailyTrendAsync(int days=30)=>!_db.Database.IsNpgsql()?GetDailyTrendCoreAsync(days):
        Owner.ExecuteAsync(OperationsOwnerV1.MesDashboard,"query",false,async(a,ct)=>await GetDailyTrendCoreAsync(days,await Data(a,false,true,false,ct)));
    public Task<List<DefectTop5Dto>> GetDefectTop5Async()=>!_db.Database.IsNpgsql()?GetDefectTop5CoreAsync():
        Owner.ExecuteAsync(OperationsOwnerV1.MesDashboard,"query",false,async(a,ct)=>await GetDefectTop5CoreAsync(await Data(a,false,false,true,ct)));
    public Task<List<RecentCompletedDto>> GetRecentCompletedAsync(int top=10)=>!_db.Database.IsNpgsql()?GetRecentCompletedCoreAsync(top):
        Owner.ExecuteAsync(OperationsOwnerV1.MesDashboard,"query",false,async(a,ct)=>await GetRecentCompletedCoreAsync(top,await Data(a,true,false,false,ct)));
    public Task<List<MachineHeatmapDto>> GetMachineHeatmapAsync(int days=7)=>!_db.Database.IsNpgsql()?GetMachineHeatmapCoreAsync(days):
        Owner.ExecuteAsync(OperationsOwnerV1.MesDashboard,"query",false,async(a,ct)=>await GetMachineHeatmapCoreAsync(days,await Data(a,false,true,false,ct)));
    // Existing /sp routes retain the database calendar and zero/negative-days anchor, on the Owner's connection.
    private Task<DateTime> DatabaseToday(CancellationToken ct)=>_db.Database.SqlQueryRaw<DateTime>("SELECT clock_timestamp()::date::timestamp AS \"Value\"").SingleAsync(ct);
    internal Task<MesDashboardSummaryDto> GetSummaryPostgreSqlAsync()=>Owner.ExecuteAsync(OperationsOwnerV1.MesDashboard,"query",false,async(a,ct)=>
    {
        var result=await GetSummaryCoreAsync(await Data(a,true,true,false,ct),await DatabaseToday(ct));
        // Preserve the accepted SP numeric expression after the scoped aggregation, including PostgreSQL tie rounding.
        result.DefectRate=await _db.Database.SqlQuery<decimal>($"""
            WITH quantities AS (SELECT {result.TotalGoodQty}::numeric(21,8) AS good,{result.TotalDefectQty}::numeric(21,8) AS defect),
            totals AS (SELECT good,defect,(good+defect)::numeric(22,8) AS total FROM quantities)
            SELECT CASE WHEN total>0 THEN (((defect/total)::numeric(38,17)*100)::numeric(38,13))::numeric(8,2)
                ELSE 0::numeric(8,2) END AS "Value" FROM totals
            """).SingleAsync(ct);
        return result;
    });
    internal Task<List<DailyTrendDto>> GetDailyTrendPostgreSqlAsync(int days)=>Owner.ExecuteAsync(OperationsOwnerV1.MesDashboard,"query",false,async(a,ct)=>
    {
        var data=await Data(a,false,true,false,ct);var today=await DatabaseToday(ct);
        if(days>367)throw new InvalidOperationException("MES daily trend exceeds the stored-procedure recursion limit.");
        if(days<=0)
        {
            try{return new List<DailyTrendDto>{new(){Date=today.AddDays(checked(-days+1)).ToString("yyyy-MM-dd")}};}
            catch(Exception e) when(e is OverflowException or ArgumentOutOfRangeException){throw new InvalidOperationException("MES daily trend exceeds the stored-procedure date range.",e);}
        }
        return await GetDailyTrendCoreAsync(days,data,today);
    });
}
