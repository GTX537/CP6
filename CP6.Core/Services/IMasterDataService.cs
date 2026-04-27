using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;

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

    /// <summary>
    /// Master Popup 共通検索 — 得意先（取引先）一覧
    /// </summary>
    /// <remarks>
    /// 専用 取引先マスタが未配備のため、Quotation / ProductMaster 上の使用済 CustomerCd を集約して返却。
    /// Code/Name 部分一致 + ページング（既定 20 件）。
    /// </remarks>
    Task<MasterLookupResult<CustomerLookupDto>> SearchCustomersAsync(string? keyword, int page, int pageSize);

    /// <summary>
    /// Master Popup 共通検索 — 製品マスタ（セット品 / 既存製品検索）
    /// </summary>
    /// <param name="keyword">製品CD / セット品名 / 顧客品名 部分一致</param>
    /// <param name="customerCd">省略可 顧客CDで絞り込み</param>
    /// <param name="onlyApproved">true = 承認済(Status=9) のみ</param>
    Task<MasterLookupResult<ProductLookupDto>> SearchProductsAsync(string? keyword, string? customerCd, bool onlyApproved, int page, int pageSize);
}
