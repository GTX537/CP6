using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 应付发票服务（章03 §3①）。录入（防重 + 行级算税 + 采番落草稿）+ 过账（发 AP.InvoicePosted 事件，
/// 经自动凭证引擎生成"借费用/原材料+进项税 / 贷应付"凭证，回填凭证 Id + 状态）。
/// 不可抵扣税并入成本行，无独立进项税行。
/// </summary>
public interface IApInvoiceService
{
    /// <summary>取发票（含行）。</summary>
    Task<ApInvoice?> GetAsync(Guid id);

    /// <summary>列出（可按供应商/状态过滤）。</summary>
    Task<List<ApInvoice>> ListAsync(string? supplierId = null, ApInvoiceStatus? status = null);

    /// <summary>录入草稿：防重 (SupplierId,SupplierInvoiceNo) + 行级算税 + 采番。回填 invoice 的 Id/No/Net/Tax/Gross。</summary>
    Task<FinResult> CreateAsync(ApInvoice invoice, string user);

    /// <summary>过账：发 AP.InvoicePosted 事件→引擎生成凭证；回填 JournalEntryId + Status=Posted。仅 Draft 可过账。</summary>
    Task<FinResult> PostAsync(Guid invoiceId, string user);
}
