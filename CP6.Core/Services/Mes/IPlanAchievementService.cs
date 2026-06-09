using CP6.Entity.DTOs;

namespace CP6.Core.Services.Mes;

/// <summary>
/// 生産計画達成率レポート（Gap 3.3）。
/// </summary>
/// <remarks>
/// T_WorkOrder の ProductionQty（計画）/ CompletedQty（良品累計）/ DefectQty（不良累計）を
/// 製品・月・得意先のいずれかで集計し、達成率・不良率を算出する。新規テーブルなし。
/// </remarks>
public interface IPlanAchievementService
{
    Task<PlanAchievementSummaryDto> GetSummaryAsync(PlanAchievementQuery query, CancellationToken ct = default);
    Task<byte[]> ExportCsvAsync(PlanAchievementQuery query, CancellationToken ct = default);
}
