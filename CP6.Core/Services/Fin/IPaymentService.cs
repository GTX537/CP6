using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 付款服务（章03 §3②/§6.1）。付款过账（借应付/贷银行；预付走借预付账款）+ 撤销
/// （顺序：先解核销还原发票欠款，再红冲付款凭证）。银行科目由银行账户的 GlAccountId 决定。
/// </summary>
public interface IPaymentService
{
    /// <summary>取付款单。</summary>
    Task<Payment?> GetAsync(Guid id);

    /// <summary>列出（可按供应商/状态过滤）。</summary>
    Task<List<Payment>> ListAsync(string? supplierId = null, PaymentStatus? status = null);

    /// <summary>付款过账：采番 + 发 AP.Payment/AP.Prepayment 事件→引擎生成凭证；回填 JournalEntryId + Status=Posted。</summary>
    Task<FinResult> PayAsync(Payment payment, string user);

    /// <summary>撤销付款（§6.1 顺序）：①解核销还原发票 SettledAmount/Status + 反冲差额凭证 ②红冲付款凭证（系统直过）③Status=Reversed。</summary>
    Task<FinResult> ReversePaymentAsync(Guid paymentId, string user, string reason);
}
