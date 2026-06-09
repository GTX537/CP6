namespace CP6.Entity.DTOs.Mes;

/// <summary>
/// 生産計画達成率レポート（Gap 3.3）の集計軸。
/// </summary>
public static class PlanAchievementGroupBy
{
    public const string Product = "product";
    public const string Month = "month";
    public const string Customer = "customer";
}

/// <summary>
/// 生産計画達成率レポートの照会条件。
/// </summary>
/// <remarks>
/// 達成率 = 完了数量(良品累計 CompletedQty) / 生産数量(計画 ProductionQty)。
/// 基準日 = ActualEndDate ?? PlanEndDate ?? PlanStartDate。
/// </remarks>
public class PlanAchievementQuery
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string GroupBy { get; set; } = PlanAchievementGroupBy.Product;
    public string? ProductCd { get; set; }
    public string? CustomerCd { get; set; }

    /// <summary>true（既定）= 完了/検査済の指図のみ。false = 発行済以降すべて（進行中も現時点進捗で集計）。</summary>
    public bool OnlyCompleted { get; set; } = true;
}

/// <summary>生産計画達成率レポート 全体サマリ。</summary>
public class PlanAchievementSummaryDto
{
    /// <summary>対象指図数</summary>
    public int TotalWorkOrders { get; set; }
    /// <summary>計画数量合計</summary>
    public decimal TotalPlannedQty { get; set; }
    /// <summary>良品数量合計</summary>
    public decimal TotalGoodQty { get; set; }
    /// <summary>不良数量合計</summary>
    public decimal TotalDefectQty { get; set; }
    /// <summary>全体達成率 = 良品合計 / 計画合計</summary>
    public decimal AchievementRate { get; set; }
    /// <summary>全体不良率 = 不良合計 / (良品合計 + 不良合計)</summary>
    public decimal DefectRate { get; set; }
    /// <summary>達成（達成率 >= 1.0）の指図数</summary>
    public int OnTargetCount { get; set; }
    public List<PlanAchievementRowDto> Rows { get; set; } = new();
}

/// <summary>生産計画達成率レポート 集計行。</summary>
public class PlanAchievementRowDto
{
    public string GroupKey { get; set; } = string.Empty;
    public string GroupLabel { get; set; } = string.Empty;
    public int WorkOrderCount { get; set; }
    public decimal PlannedQty { get; set; }
    public decimal GoodQty { get; set; }
    public decimal DefectQty { get; set; }
    /// <summary>達成率 = GoodQty / PlannedQty</summary>
    public decimal AchievementRate { get; set; }
    /// <summary>不良率 = DefectQty / (GoodQty + DefectQty)</summary>
    public decimal DefectRate { get; set; }
    /// <summary>達成（達成率 >= 1.0）の指図数</summary>
    public int OnTargetCount { get; set; }
}
