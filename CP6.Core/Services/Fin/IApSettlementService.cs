namespace CP6.Core.Services.Fin;

/// <summary>
/// 应付核销服务（章03 §3③/§4）。一笔付款核销一/多张应付发票：按原币应用金额冲减发票欠款，
/// 尾差（现金折扣）与已实现汇兑损益（发票记账汇率 vs 付款汇率）写一张差额冲销凭证。
/// 核销本身不产凭证（勾稽关系），仅尾差/汇差产凭证。
/// </summary>
public interface IApSettlementService
{
    /// <summary>
    /// 核销：把付款应用到若干发票。校验付款余额/供应商一致/发票不超额/有折扣须折扣科目；
    /// 逐发票算已实现汇兑损益 + 折扣写冲；更新发票 SettledAmount/Status + 付款 SettledAmount。
    /// </summary>
    Task<FinResult> SettleAsync(Guid paymentId, IReadOnlyList<SettlementApply> applies, string user);
}

/// <summary>一条核销应用：把付款的 <see cref="AppliedAmount"/>（原币）应用到某发票，可带现金折扣。</summary>
public class SettlementApply
{
    /// <summary>应付发票 Id</summary>
    public Guid InvoiceId { get; set; }

    /// <summary>本次实付应用金额（原币，冲减发票欠款）</summary>
    public decimal AppliedAmount { get; set; }

    /// <summary>现金折扣金额（原币，与实付共同清账；&gt;0 时须给折扣科目）</summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>折扣冲销科目 Id（财务费用等）</summary>
    public Guid? DiscountAccountId { get; set; }
}
