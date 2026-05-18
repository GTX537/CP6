using CP6.Entity.DTOs.Mes;

namespace CP6.Core.Services.Mes;

/// <summary>
/// MSBBME080 — 不良品管理 業務サービス契約
/// </summary>
public interface IDefectRecordService
{
    Task<DefectRecordDto?> GetByNoAsync(string defectNo);
    Task<PagedResultDto<DefectRecordDto>> SearchAsync(DefectRecordSearchQuery query);
    Task<string> CreateAsync(DefectRecordDto dto, string? userName);
    Task UpdateAsync(string defectNo, DefectRecordDto dto, string? userName);
    Task DeleteAsync(string defectNo, string? userName);

    /// <summary>不良分類マスタ 一覧（前端 Select 用）</summary>
    Task<List<DefectCategoryDto>> GetAllCategoriesAsync();
}
