namespace CP6.Core.Services.Fin;

/// <summary>
/// 应收子账↔GL 勾稽（章04 §3，镜像 <see cref="IApReconcileService"/>）。AR 子账未收合计（本位币）必须等于
/// GL AR_CONTROL 控制科目余额——尾差/汇差写冲后仍恒等，否则账实不符。
/// </summary>
public interface IArReconcileService
{
    /// <summary>对账：子账未收合计 vs GL 应收控制科目余额（本位币）。</summary>
    Task<ArReconcileResult> ReconcileArAsync();
}

/// <summary>应收对账结果。</summary>
public class ArReconcileResult
{
    /// <summary>AR 子账未收合计（本位币）= Σ 未结发票 (Gross−Settled)×记账汇率（红字反向）</summary>
    public decimal SubLedger { get; init; }

    /// <summary>GL AR_CONTROL 余额（本位币）= Σ 已过账分录(借−贷)</summary>
    public decimal GlBalance { get; init; }

    /// <summary>是否相符（差 &lt; 0.01）</summary>
    public bool IsMatched => Math.Abs(SubLedger - GlBalance) < 0.01m;
}
