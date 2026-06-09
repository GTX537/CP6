using CP6.Entity.DTOs;

namespace CP6.Core.Services.Erp;

/// <summary>
/// MSBBPA050/060 - Web 製品マスタ業務服務契約
/// </summary>
/// <remarks>
/// 5 表 1 次提交（製品基本/工程/材料/ロット単価/連産品）
/// 業務 PK：ProductCd（15 桁、ItemCd(11) + Branch1(4)）
/// </remarks>
public interface IProductService
{
    /// <summary>分页一覧（MSBBPA060 调用；自动过滤 IsDeleted=false）</summary>
    Task<(List<ProductListItemDto> Rows, int Total)> GetPageListAsync(ProductQuery query);

    /// <summary>按製品 CD 获取详情（含全部 5 表）</summary>
    Task<ProductDto?> GetByCdAsync(string productCd, bool includeDeleted = false);

    /// <summary>登録（POST）— 採番後 5 表一括書き込み。返回新採番された ProductCd</summary>
    Task<string> CreateAsync(ProductDto dto, string? userName);

    /// <summary>訂正（PUT）— 5 表 diff 同期。RowVersion 楽観锁</summary>
    Task UpdateAsync(string productCd, ProductDto dto, string? userName);

    /// <summary>削除（DELETE）— 主表+全子表 软删除</summary>
    Task DeleteAsync(string productCd, byte[]? rowVersion, string? userName);

    /// <summary>コピー（POST）— 来源を複製、新採番、ステータス=0</summary>
    Task<string> CopyAsync(string sourceProductCd, string? userName);

    /// <summary>採番 — 次の 11 桁連番を発行（重複なし）</summary>
    Task<string> NextSequenceAsync();

    /// <summary>御見積書NO による部材一覧引入（MSBBPA050 第1页）</summary>
    Task<List<ProductMemberDto>> GetMembersByQuotationAsync(string quotationNo);

    /// <summary>見積計算書NO による基本情報引入（MSBBPA050 §3.x）</summary>
    Task<ProductBasicInfoDto?> GetBasicInfoByEstimateCalcAsync(string estimateCalcNo);

    /// <summary>CSV 出力 — MSBBPA060 一覧の現在検索条件をすべて出力（ページング無視）</summary>
    Task<byte[]> ExportCsvAsync(ProductQuery query);
}
