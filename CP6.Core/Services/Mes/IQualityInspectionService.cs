using CP6.Entity.DTOs.Mes;

namespace CP6.Core.Services.Mes;

/// <summary>
/// MSBBME060 / 070 — 品質検査 業務サービス契約
/// </summary>
public interface IQualityInspectionService
{
    /// <summary>ME060 — 単条検査取得（含全項目）</summary>
    Task<QualityInspectionDto?> GetByNoAsync(string inspectionNo);

    /// <summary>ME070 — 多条件検索 + 分页</summary>
    Task<PagedResultDto<QualityInspectionDto>> SearchAsync(QualityInspectionSearchQuery query);

    /// <summary>ME060 — 新規登録（自動採番 + 自動合否判定）</summary>
    Task<string> CreateAsync(QualityInspectionDto dto, string? userName);

    /// <summary>ME060 — 訂正</summary>
    Task UpdateAsync(string inspectionNo, QualityInspectionDto dto, string? userName);

    /// <summary>ME060 — 検査テンプレート取得（項目一覧引入用）</summary>
    Task<List<InspectionTemplateDto>> GetTemplateAsync(string templateCd);

    /// <summary>ME060 — 全テンプレートCD一覧</summary>
    Task<List<string>> GetAllTemplateCodesAsync();
}
