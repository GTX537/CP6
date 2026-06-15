namespace CP6.Core.Services.Fin;

/// <summary>
/// 应付子账↔GL 勾稽（章03 §4，月结对账第一刀）。AP 子账未付合计（本位币）必须等于
/// GL AP_CONTROL 控制科目余额——尾差/汇差写冲后仍恒等，否则账实不符。
/// </summary>
public interface IApReconcileService
{
    /// <summary>对账：子账未付合计 vs GL 应付控制科目余额（本位币）。</summary>
    Task<ApReconcileResult> ReconcileApAsync();
}

/// <summary>应付对账结果。</summary>
public class ApReconcileResult
{
    /// <summary>AP 子账未付合计（本位币）= Σ 未结发票 (Gross−Settled)×记账汇率</summary>
    public decimal SubLedger { get; init; }

    /// <summary>GL AP_CONTROL 余额（本位币）= Σ 已过账分录(贷−借)</summary>
    public decimal GlBalance { get; init; }

    /// <summary>是否相符（差 &lt; 0.01）</summary>
    public bool IsMatched => Math.Abs(SubLedger - GlBalance) < 0.01m;
}
