using CP6.Entity.DTOs.Mes;

namespace CP6.Core.Services.Mes;

/// <summary>MSBBME090 — MES ダッシュボード サービス契約</summary>
public interface IMesDashboardService
{
    Task<MesDashboardSummaryDto> GetSummaryAsync();
    Task<List<ProcessProgressDto>> GetProcessProgressAsync();
    Task<List<DelayAlertDto>> GetDelayAlertsAsync(int top = 50);
    Task<List<DailyTrendDto>> GetDailyTrendAsync(int days = 30);
    Task<List<DefectTop5Dto>> GetDefectTop5Async();
    Task<List<RecentCompletedDto>> GetRecentCompletedAsync(int top = 10);
    Task<List<MachineHeatmapDto>> GetMachineHeatmapAsync(int days = 7);
}
