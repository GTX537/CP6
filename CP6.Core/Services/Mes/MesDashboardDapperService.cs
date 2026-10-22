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

    private readonly MesDashboardService? _owned;
    public MesDashboardDapperService(IDbConnection conn, MesDashboardService? owned=null) { _conn=conn; _owned=owned; }
    private MesDashboardService Owned=>_owned??throw new CP6.Core.Services.Erp.OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);

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
            return await Owned.GetSummaryPostgreSqlAsync();
        var row = await _conn.QueryFirstOrDefaultAsync<MesDashboardSummaryDto>(
            "usp_GetMesDashboardSummary",
            commandType: CommandType.StoredProcedure);
        return row ?? new MesDashboardSummaryDto();
    }

    /// <summary>日別推移 — SP 経由（既定 30 日）</summary>
    public async Task<List<DailyTrendDto>> GetDailyTrendAsync(int days = 30)
    {
        if (IsPostgreSql) return await Owned.GetDailyTrendPostgreSqlAsync(days);
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
            return await Owned.GetProcessProgressAsync();
        var rows = await _conn.QueryAsync<ProcessProgressDto>(
            "usp_GetMesProcessProgress",
            commandType: CommandType.StoredProcedure);
        return rows.AsList();
    }

}
