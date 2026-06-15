namespace CP6.Core.Services.Fin;

/// <summary>报表一行（科目级）。Amount 按报表正向（资产/费用借方为正，负债/权益/收入贷方为正）。</summary>
public record FinReportLine(string Code, string Name, decimal Amount);

/// <summary>
/// 资产负债表（章08 §2）。某时点家底：取资产/负债/权益类科目【期末余额】重组。
/// 本年利润（收入−费用，年结前未转未分配利润）并入权益侧，故 资产 = 负债 + 权益 + 本年利润 恒平。
/// </summary>
public class BalanceSheet
{
    public Guid PeriodId { get; set; }
    public List<FinReportLine> Assets { get; set; } = new();
    public List<FinReportLine> Liabilities { get; set; } = new();
    public List<FinReportLine> Equity { get; set; } = new();
    /// <summary>本年利润（Σ收入期末 − Σ费用期末），并入权益侧</summary>
    public decimal CurrentProfit { get; set; }
    public decimal TotalAssets { get; set; }
    public decimal TotalLiabilities { get; set; }
    /// <summary>权益合计（含本年利润）</summary>
    public decimal TotalEquity { get; set; }
    public decimal TotalLiabEquity { get; set; }
    /// <summary>资产 == 负债+权益+本年利润（不平=数据完整性 bug）</summary>
    public bool IsBalanced { get; set; }
}

/// <summary>
/// 损益表（章08 §3）。一段时间赚了多少：取收入/成本/费用类科目【本期发生额】（区间数，非时点）。
/// 毛利 = 收入 − 主营成本(COGS Role)；净利润 = 毛利 − 营业费用（其余费用）。
/// </summary>
public class IncomeStatement
{
    public Guid PeriodId { get; set; }
    public List<FinReportLine> RevenueLines { get; set; } = new();
    public List<FinReportLine> CostLines { get; set; } = new();
    public List<FinReportLine> ExpenseLines { get; set; } = new();
    public decimal Revenue { get; set; }
    /// <summary>主营业务成本（Role=COGS）</summary>
    public decimal Cost { get; set; }
    public decimal GrossProfit { get; set; }
    /// <summary>营业费用（非 COGS 的费用类合计：销售/管理/财务/税金等）</summary>
    public decimal OperatingExpense { get; set; }
    public decimal NetProfit { get; set; }
}
