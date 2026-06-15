namespace CP6.Core.Services.Fin;

/// <summary>资产负债表（章08 §2）。从科目期末余额重组，复用 <see cref="ITrialBalanceService"/>，永远与总账一致。</summary>
public interface IBalanceSheetService
{
    /// <summary>构建指定期间的资产负债表（期末余额时点数 + 本年利润并入权益 + 必平校验）。</summary>
    Task<BalanceSheet> BuildAsync(Guid periodId);
}
