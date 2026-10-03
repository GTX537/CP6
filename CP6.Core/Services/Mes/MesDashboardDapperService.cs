using System.Data;
using System.Globalization;
using CP6.Entity.DTOs.Mes;
using Dapper;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace CP6.Core.Services.Mes;

/// <summary>
/// MES ダッシュボード Dapper + 存儲過程 (SP) 版実装
/// </summary>
/// <remarks>
/// JD「SQL Server 性能調優・存儲過程」要件のサンプル：
/// - 既存 EF Core 版（MesDashboardService）と並列実装
/// - SQL Server は SP、PostgreSQL は同じ集計契約の SQL を使用
/// - Dapper で型付きマッピング。PostgreSQL の日別推移は DB 日付取得後に集計する 2 往復
/// </remarks>
public class MesDashboardDapperService
{
    private readonly IDbConnection _conn;

    public MesDashboardDapperService(IDbConnection conn) => _conn = conn;

    private bool IsPostgreSql => _conn switch
    {
        NpgsqlConnection => true,
        SqlConnection => false,
        _ => throw new InvalidOperationException("MES reports require a configured SQL Server or PostgreSQL connection.")
    };

    /// <summary>本日サマリ — SP 経由</summary>
    public async Task<MesDashboardSummaryDto> GetSummaryAsync()
    {
        if (IsPostgreSql)
            return await _conn.QuerySingleAsync<MesDashboardSummaryDto>(PostgreSqlSummary);
        var row = await _conn.QueryFirstOrDefaultAsync<MesDashboardSummaryDto>(
            "usp_GetMesDashboardSummary",
            commandType: CommandType.StoredProcedure);
        return row ?? new MesDashboardSummaryDto();
    }

    /// <summary>日別推移 — SP 経由（既定 30 日）</summary>
    public async Task<List<DailyTrendDto>> GetDailyTrendAsync(int days = 30)
    {
        if (IsPostgreSql) return await GetPostgreSqlDailyTrendAsync(days);
        var rows = await _conn.QueryAsync<DailyTrendDto>(
            "usp_GetMesDailyTrend",
            new { Days = days },
            commandType: CommandType.StoredProcedure);
        return rows.AsList();
    }

    /// <summary>工程別進捗 — SP 経由</summary>
    public async Task<List<ProcessProgressDto>> GetProcessProgressAsync()
    {
        if (IsPostgreSql)
            return (await _conn.QueryAsync<ProcessProgressDto>("""
                SELECT "ProcessCd", "ProcessName",
                    SUM(CASE WHEN "ProcessStatus"=0 THEN 1 ELSE 0 END)::integer AS "NotStarted",
                    SUM(CASE WHEN "ProcessStatus" IN (1,3) THEN 1 ELSE 0 END)::integer AS "InProgress",
                    SUM(CASE WHEN "ProcessStatus"=2 THEN 1 ELSE 0 END)::integer AS "Completed"
                FROM public."T_WorkOrderProcess"
                WHERE "IsDeleted"=FALSE
                GROUP BY "ProcessCd", "ProcessName"
                ORDER BY "ProcessCd"
                """)).AsList();
        var rows = await _conn.QueryAsync<ProcessProgressDto>(
            "usp_GetMesProcessProgress",
            commandType: CommandType.StoredProcedure);
        return rows.AsList();
    }

    private async Task<List<DailyTrendDto>> GetPostgreSqlDailyTrendAsync(int days)
    {
        // The SQL procedure permits its anchor plus 366 recursive rows. Failure remains an unhandled report error.
        if (days > 367) throw new InvalidOperationException("MES daily trend exceeds the stored-procedure recursion limit.");
        var calendar = await _conn.QuerySingleAsync<string>("SELECT to_char(clock_timestamp()::date, 'YYYY-MM-DD')");
        DateTime fromDate;
        DateTime toDate;
        try
        {
            var today = DateOnly.ParseExact(calendar, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            // Preserve SQL int arithmetic/date bounds and the single future anchor for zero/negative days.
            fromDate = today.AddDays(checked(-days + 1)).ToDateTime(TimeOnly.MinValue);
            toDate = today.AddDays(1).ToDateTime(TimeOnly.MinValue);
        }
        catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException or FormatException)
        {
            throw new InvalidOperationException("MES daily trend exceeds the stored-procedure date range.", exception);
        }
        return (await _conn.QueryAsync<DailyTrendDto>("""
            WITH dates AS (
                SELECT CAST(@fromDate AS date)+offsets.n AS d
                FROM generate_series(0, @rowCount-1) AS offsets(n)
            ), quantities AS (
                SELECT "CreateDate"::date AS d,
                    SUM("GoodQty")::numeric(38,8) AS good,
                    SUM("DefectQty")::numeric(38,8) AS defect
                FROM public."T_ProductionResult"
                WHERE "IsDeleted"=FALSE AND "CreateDate">=@fromDate AND "CreateDate"<@toDate
                GROUP BY "CreateDate"::date
            )
            SELECT to_char(dates.d, 'YYYY-MM-DD') AS "Date",
                COALESCE(quantities.good, 0)::numeric(38,8) AS "GoodQty",
                COALESCE(quantities.defect, 0)::numeric(38,8) AS "DefectQty"
            FROM dates LEFT JOIN quantities ON dates.d=quantities.d
            ORDER BY dates.d
            """, new { fromDate, toDate, rowCount = Math.Max(days, 1) })).AsList();
    }

    // These endpoints intentionally retain the original procedures' global scope and database calendar.
    // The casts mirror the native SQL expression metadata, including multiplication by integer literal 100.
    private const string PostgreSqlSummary = """
        WITH calendar AS MATERIALIZED (
            SELECT clock_timestamp()::date AS today
        ), quantities AS (
            SELECT COALESCE(SUM(p."GoodQty"), 0)::numeric(21,8) AS good,
                COALESCE(SUM(p."DefectQty"), 0)::numeric(21,8) AS defect
            FROM public."T_ProductionResult" p CROSS JOIN calendar c
            WHERE p."IsDeleted"=FALSE AND p."CreateDate">=c.today AND p."CreateDate"<c.today+1
        ), totals AS (
            SELECT good, defect, (good+defect)::numeric(22,8) AS total FROM quantities
        )
        SELECT
            (SELECT COUNT(*)::integer FROM public."T_WorkOrder" WHERE "IsDeleted"=FALSE AND "Status"=3) AS "InProgressCount",
            (SELECT COUNT(*)::integer FROM public."T_WorkOrder" WHERE "IsDeleted"=FALSE AND "Status" IN (4,6)
                AND "ActualEndDate">=c.today AND "ActualEndDate"<c.today+1) AS "CompletedCount",
            good AS "TotalGoodQty", defect AS "TotalDefectQty",
            CASE WHEN total>0 THEN (((defect/total)::numeric(38,17)*100)::numeric(38,13))::numeric(8,2)
                ELSE 0::numeric(8,2) END AS "DefectRate",
            (SELECT COUNT(*)::integer FROM public."T_WorkOrder" WHERE "IsDeleted"=FALSE
                AND "PlanEndDate"<c.today AND "Status" NOT IN (4,6,9)) AS "DelayedCount"
        FROM totals CROSS JOIN calendar c
        """;
}
