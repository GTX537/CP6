namespace CP6.Core.Services.Fin;

/// <summary>
/// 自动凭证引擎（章05 §3）。"一台引擎、规则千变"：把 <see cref="FinBizEvent"/> 据
/// <see cref="Entity.DomainModels.Fin.PostingRule"/> 拼成记账凭证并直过（复用 GL 的 AutoPostAsync，
/// 含借贷恒等 + 锁期双保险）。AP/AR 三段（发票/收付款/核销）均经本引擎生成凭证。
/// </summary>
public interface IAutoVoucherEngine
{
    /// <summary>
    /// 四步生成（章05 §3）：①幂等（同 Source+SourceDocNo 已过账则跳过）②按 EventType 找启用规则
    /// ③拼凭证（FixedRole 按 Role 锚点取科目；DocumentLines 按科目+成本中心炸开单据行；原币×汇率→本位币）
    /// ④AutoPostAsync 直过。幂等命中返回 <see cref="FinResult.Pass"/>。
    /// </summary>
    Task<FinResult> GenerateAsync(FinBizEvent evt);
}
