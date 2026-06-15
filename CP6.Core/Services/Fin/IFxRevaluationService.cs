namespace CP6.Core.Services.Fin;

/// <summary>
/// 期末未实现汇兑重估（章07 §4）。月结时对未结清的外币 AP/AR 余额按期末汇率重估，
/// 把账面本位币调到"若此刻结算会是多少"，差额计未实现汇兑损益（unrealized），
/// 下期初冲回（reversing 法）——避免与结算时点的已实现汇差（<see cref="ApSettlementService"/>/<see cref="ArSettlementService"/>）重复计。
/// </summary>
public interface IFxRevaluationService
{
    /// <summary>
    /// 对指定期间做期末未实现汇兑重估：
    /// 生成①重估凭证（VoucherDate=期末，Source=FxReval）+ ②冲回凭证（VoucherDate=下期初，借贷对调）。
    /// 仅重估未结（已过账/部分核销）的外币发票；本位币(JPY/空)跳过；期末汇率未登记的币种跳过并告警。
    /// 幂等：本期已重估（存在 FXREVAL-{yyyyMM} 凭证）则直接返回成功；无外币敞口时不生成任何凭证。
    /// </summary>
    Task<FinResult> RevalueAsync(Guid periodId, string user);
}
