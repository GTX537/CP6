using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 试算平衡表（章02 §2）。三栏：期初余额（开账至今、本期前累计，★含历史）+ 本期发生 + 期末余额。
/// 两层平衡都由借贷恒等保证；任一层不平 = 数据完整性 bug（非会计算错）。
/// </summary>
public class TrialBalance
{
    public Guid PeriodId { get; set; }
    public List<TrialBalanceRow> Rows { get; set; } = new();

    /// <summary>Σ本期借发生 == Σ本期贷发生。</summary>
    public bool MovementBalanced { get; set; }

    /// <summary>Σ借方余额 == Σ贷方余额（会计恒等式）。</summary>
    public bool ClosingBalanced { get; set; }

    /// <summary>两层都平。</summary>
    public bool IsBalanced => MovementBalanced && ClosingBalanced;
}

/// <summary>
/// 试算表一行。OpenBal/CloseBal 按科目正常方向带号（正=正常方向余额）。
/// </summary>
/// <param name="Code">科目编码</param>
/// <param name="Name">科目名称</param>
/// <param name="OpenBal">期初余额（正常方向）</param>
/// <param name="PeriodDebit">本期借方发生</param>
/// <param name="PeriodCredit">本期贷方发生</param>
/// <param name="CloseBal">期末余额（正常方向）= 期初 + 本期</param>
/// <param name="NormalSide">科目正常方向</param>
public record TrialBalanceRow(
    string Code,
    string Name,
    decimal OpenBal,
    decimal PeriodDebit,
    decimal PeriodCredit,
    decimal CloseBal,
    AccountSide NormalSide);
