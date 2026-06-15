namespace CP6.Core.Services.Fin;

/// <summary>损益表（章08 §3）。从收入/成本/费用类科目本期发生额算，复用 <see cref="ITrialBalanceService"/>。</summary>
public interface IIncomeStatementService
{
    /// <summary>构建指定期间的损益表（本期发生额区间数 + 毛利/净利润逐层）。</summary>
    Task<IncomeStatement> BuildAsync(Guid periodId);
}
