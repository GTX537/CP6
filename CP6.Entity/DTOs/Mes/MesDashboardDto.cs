namespace CP6.Entity.DTOs.Mes;

/// <summary>ME090 ダッシュボード 本日 KPI</summary>
public class MesDashboardSummaryDto
{
    /// <summary>本日 着手中 指図件数</summary>
    public int InProgressCount { get; set; }
    /// <summary>本日 完了 指図件数</summary>
    public int CompletedCount { get; set; }
    /// <summary>本日 良品数合計</summary>
    public decimal TotalGoodQty { get; set; }
    /// <summary>本日 不良数合計</summary>
    public decimal TotalDefectQty { get; set; }
    /// <summary>本日 不良率(%) = defect / (good+defect) * 100</summary>
    public decimal DefectRate { get; set; }
    /// <summary>遅延件数（計画完了日 &lt; 今日 AND ステータス ≠ 完了/検査済/取消）</summary>
    public int DelayedCount { get; set; }
}

/// <summary>ME090 — 工程別進捗（横棒積み上げ）</summary>
public class ProcessProgressDto
{
    public string ProcessCd { get; set; } = string.Empty;
    public string? ProcessName { get; set; }
    public int NotStarted { get; set; }
    public int InProgress { get; set; }
    public int Completed { get; set; }
}

/// <summary>ME090 — 納期遅延アラート行</summary>
public class DelayAlertDto
{
    public string WorkOrderNo { get; set; } = string.Empty;
    public string? ProductCd { get; set; }
    public string? ProductName { get; set; }
    public string? CustomerCd { get; set; }
    public DateTime? PlanEndDate { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public int DelayDays { get; set; }
    public int Status { get; set; }
    public decimal ProgressRate { get; set; }
}

/// <summary>ME090 — 日別生産推移（折線）</summary>
public class DailyTrendDto
{
    /// <summary>日付（yyyy-MM-dd）</summary>
    public string Date { get; set; } = string.Empty;
    public decimal GoodQty { get; set; }
    public decimal DefectQty { get; set; }
}

/// <summary>ME090 — 不良 TOP5（円グラフ）</summary>
public class DefectTop5Dto
{
    public string CategoryCd { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public int Count { get; set; }
    public decimal Qty { get; set; }
}

/// <summary>ME090 — 直近完了指図</summary>
public class RecentCompletedDto
{
    public string WorkOrderNo { get; set; } = string.Empty;
    public string? ProductCd { get; set; }
    public string? ProductName { get; set; }
    public decimal ProductionQty { get; set; }
    public decimal CompletedQty { get; set; }
    public DateTime? ActualEndDate { get; set; }
}

/// <summary>ME090 — 工程稼働ヒートマップ（号機×時間帯）</summary>
public class MachineHeatmapDto
{
    public string MachineCd { get; set; } = string.Empty;
    /// <summary>時間帯（0〜23）</summary>
    public int Hour { get; set; }
    /// <summary>稼働件数</summary>
    public int Count { get; set; }
}
