using CP6.Entity.DTOs.Mes;

namespace CP6.Core.Services.Mes;

/// <summary>MSBBME010 — 生産計画ボード サービス契約</summary>
public interface IPlanningBoardService
{
    Task<List<PlanningBarDto>> GetBarsAsync(PlanningBoardQuery query);
    Task<PlanningKpiDto> GetKpiAsync(PlanningBoardQuery query);
    Task RescheduleAsync(RescheduleRequest req, string? userName);
    Task<int> AutoArrangeAsync(AutoArrangeRequest req, string? userName);
}
