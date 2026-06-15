namespace CP6.Core.Services.Fin;

/// <summary>
/// 应收核销服务（章04 §3，镜像 <see cref="IApSettlementService"/>）。一笔收款核销一/多张应收发票：
/// 按原币应用金额冲减发票欠款，尾差（销售折扣）与已实现汇兑损益（收款汇率 vs 发票记账汇率）写一张差额冲销凭证。
/// 核销本身不产凭证（勾稽关系），仅尾差/汇差产凭证。复用 <see cref="SettlementApply"/>。
/// </summary>
public interface IArSettlementService
{
    /// <summary>
    /// 核销：把收款应用到若干发票。校验收款余额/客户一致/发票不超额/有折扣须折扣科目；
    /// 逐发票算已实现汇兑损益 + 销售折扣写冲；更新发票 SettledAmount/Status + 收款 SettledAmount。
    /// </summary>
    Task<FinResult> SettleAsync(Guid receiptId, IReadOnlyList<SettlementApply> applies, string user);
}
