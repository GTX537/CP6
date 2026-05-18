using System.Data;
using CP6.Entity.DTOs.Mes;
using Dapper;

namespace CP6.Core.Services.Mes;

/// <summary>
/// MES ダッシュボード Dapper + 存儲過程 (SP) 版実装
/// </summary>
/// <remarks>
/// JD「SQL Server 性能調優・存儲過程」要件のサンプル：
/// - 既存 EF Core 版（MesDashboardService）と並列実装
/// - 大量集計クエリは SP に寄せて DB 側でインデックス + プラン最適化
/// - Dapper でストロングタイプ マッピング + 単一往復
/// </remarks>
public class MesDashboardDapperService
{
    private readonly IDbConnection _conn;

    public MesDashboardDapperService(IDbConnection conn) => _conn = conn;

    /// <summary>本日サマリ — SP 経由</summary>
    public async Task<MesDashboardSummaryDto> GetSummaryAsync()
    {
        var row = await _conn.QueryFirstOrDefaultAsync<MesDashboardSummaryDto>(
            "usp_GetMesDashboardSummary",
            commandType: CommandType.StoredProcedure);
        return row ?? new MesDashboardSummaryDto();
    }

    /// <summary>日別推移 — SP 経由（既定 30 日）</summary>
    public async Task<List<DailyTrendDto>> GetDailyTrendAsync(int days = 30)
    {
        var rows = await _conn.QueryAsync<DailyTrendDto>(
            "usp_GetMesDailyTrend",
            new { Days = days },
            commandType: CommandType.StoredProcedure);
        return rows.AsList();
    }

    /// <summary>工程別進捗 — SP 経由</summary>
    public async Task<List<ProcessProgressDto>> GetProcessProgressAsync()
    {
        var rows = await _conn.QueryAsync<ProcessProgressDto>(
            "usp_GetMesProcessProgress",
            commandType: CommandType.StoredProcedure);
        return rows.AsList();
    }
}
