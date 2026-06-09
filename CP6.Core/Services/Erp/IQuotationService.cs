using CP6.Entity.DTOs;

namespace CP6.Core.Services.Erp;

/// <summary>
/// 御見積書（MSBBPA030/040）業務服務契約
/// </summary>
public interface IQuotationService
{
    /// <summary>分页列表（MSBBPA040 调用；自动过滤 IsDeleted=false）</summary>
    Task<(List<QuotationListItem> Rows, int Total)> GetPageListAsync(QuotationQuery query);

    /// <summary>按 NO 获取详情（含関連計算書+明细）。削除/参照 模式可读软删数据</summary>
    Task<QuotationDto?> GetByNoAsync(string qtnNo, bool includeDeleted = false);

    /// <summary>
    /// 新建（登録）。自动采番 XXXXXXXX-01。
    /// 写入主表 + 関連見積計算書 + 打印用明细，合計金額 = 各明细金額之和。
    /// </summary>
    Task<string> CreateAsync(QuotationDto dto, string? userName);

    /// <summary>
    /// 修改（訂正）。乐观锁控制（RowVersion）。
    /// 明细按 DetailNo diff：新增/修改/删除；関連計算書按 QtnCalcNo diff。
    /// 若 MasterConfirmFlg != 0 則拒绝编辑并抛出 InvalidOperationException（MSG-004）。
    /// </summary>
    Task UpdateAsync(string qtnNo, QuotationDto dto, string? userName);

    /// <summary>逻辑删除（削除）。若已確定则抛出 InvalidOperationException（MSG-004）。</summary>
    Task DeleteAsync(string qtnNo, byte[]? rowVersion, string? userName);

    /// <summary>
    /// 复制新建（コピー）。读取来源，清空 NO/RowVersion/発行日，重新采番。
    /// 発行日清空，状态重置为 0（未承認）。
    /// </summary>
    Task<string> CopyAsync(string sourceQtnNo, string? userName);

    /// <summary>
    /// 確定登録（§2.6.5）。前提：
    ///   ① 至少 1 行「確定」☑
    ///   ② 所有「確定」☑ 行对应的見積計算書.QtnDiv == "20"（決定）
    /// 更新 Quotation.MasterConfirmFlg=9 与选中行的 QuotationCalc.MasterConfirmFlg=9
    /// </summary>
    Task ConfirmAsync(string qtnNo, ConfirmRequest req, string? userName);

    /// <summary>確定取消（§2.6.6）。将所有关联回退到 MasterConfirmFlg=0</summary>
    Task CancelConfirmAsync(string qtnNo, byte[]? rowVersion, string? userName);

    /// <summary>
    /// 発行帳票（§2.6.4 Step6）。更新 QtnIssueDate/CalcIssueDate。
    /// 返回真正生成的 PDF 字节流由 Phase 5 实现；当前阶段仅更新状态并返回文件名列表。
    /// </summary>
    Task<List<string>> IssueAsync(string qtnNo, IssueRequest req, string? userName);

    /// <summary>
    /// 按 得意先CD + 案件NO 查询可关联的見積計算書候选（§2.6.1 联动）。
    /// 若 quotationNo 已存在，会返回 IsLinked=true（已勾选使用）。
    /// </summary>
    Task<List<QuotationCalcCandidate>> GetCalcCandidatesAsync(
        string customerCd, string? projectNoParent, string? projectNoChild, string? projectNoMaterial,
        string? currentQuotationNo);
}
