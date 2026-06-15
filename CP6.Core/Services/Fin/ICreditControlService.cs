namespace CP6.Core.Services.Fin;

/// <summary>
/// 信用控制（章04 §3，AR 独有风控）。出货/受注前校验"客户当前应收余额 + 本单金额 &gt; 信用额度"则超限。
/// 这是 ERP→财务的反向钩子：财务数据（应收余额）反过来约束业务动作（能否发货）。
/// 额度 0/null = 不控制。<see cref="CreditCheckResult.Exceeded"/> 由调用方决定硬拦截或仅警告。
/// </summary>
public interface ICreditControlService
{
    /// <summary>校验客户信用：算当前未结应收余额（本位币，红字反向），加本单是否超额度。</summary>
    Task<CreditCheckResult> CheckCreditAsync(string customerId, decimal orderAmount);
}

/// <summary>信用校验结果。</summary>
public class CreditCheckResult
{
    public string CustomerId { get; init; } = string.Empty;

    /// <summary>信用额度（本位币）。0=不控制</summary>
    public decimal CreditLimit { get; init; }

    /// <summary>当前未结应收余额（本位币，红字反向）</summary>
    public decimal OpenAr { get; init; }

    /// <summary>本单金额（本位币）</summary>
    public decimal OrderAmount { get; init; }

    /// <summary>是否启用控制（额度 &gt; 0）</summary>
    public bool Controlled => CreditLimit > 0m;

    /// <summary>是否超限（仅在受控时可能为 true）</summary>
    public bool Exceeded { get; init; }

    /// <summary>剩余可用额度（= 额度 − 已欠 − 本单）。未受控时为 0</summary>
    public decimal Available => Controlled ? CreditLimit - OpenAr - OrderAmount : 0m;
}
