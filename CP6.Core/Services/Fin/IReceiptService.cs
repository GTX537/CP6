using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 收款服务（章04 §3，镜像 <see cref="IPaymentService"/>）。收款过账（借银行/贷应收；预收走借银行/贷预收账款）
/// + 撤销（顺序：先解核销还原发票欠款，再红冲收款凭证）。银行科目由银行账户的 GlAccountId 决定。
/// </summary>
public interface IReceiptService
{
    /// <summary>取收款单。</summary>
    Task<Receipt?> GetAsync(Guid id);

    /// <summary>列出（可按客户/状态过滤）。</summary>
    Task<List<Receipt>> ListAsync(string? customerId = null, ReceiptStatus? status = null);

    /// <summary>收款过账：采番 + 发 AR.Receipt/AR.Advance 事件→引擎生成凭证；回填 JournalEntryId + Status=Posted。</summary>
    Task<FinResult> ReceiveAsync(Receipt receipt, string user);

    /// <summary>撤销收款（§6.1 顺序）：①解核销还原发票 SettledAmount/Status + 反冲差额凭证 ②红冲收款凭证（系统直过）③Status=Reversed。</summary>
    Task<FinResult> ReverseReceiptAsync(Guid receiptId, string user, string reason);
}
