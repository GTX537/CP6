using CP6.Entity.DomainModels;

namespace CP6.Core.Services;

/// <summary>
/// MSBBPA010 第 1 页依赖的各种下拉/联动用主数据查询
/// </summary>
public interface IMasterDataService
{
    /// <summary>所有据点（下拉）</summary>
    Task<List<MasterBase>> GetBasesAsync();

    /// <summary>按据点过滤担当者（受注拠点变更联动）</summary>
    Task<List<MasterStaff>> GetStaffsAsync(string? baseCd);

    /// <summary>按组查汎用マスタ（通用下拉，含 シート段/受注区分/親子区分 等）</summary>
    Task<List<MasterGenericCode>> GetGenericCodesAsync(string groupCode);
}
