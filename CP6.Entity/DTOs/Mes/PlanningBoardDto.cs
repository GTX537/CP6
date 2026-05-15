namespace CP6.Entity.DTOs.Mes;

/// <summary>生産計画ボード ガントチャート 1 本の Bar</summary>
public class PlanningBarDto
{
    public Guid Id { get; set; }
    public string WorkOrderNo { get; set; } = string.Empty;
    public string ProductCd { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string? CustomerCd { get; set; }
    public string ProcessCd { get; set; } = string.Empty;
    public string TaskCd { get; set; } = string.Empty;
    public string? ProcessName { get; set; }
    public string? MachineCd { get; set; }
    public string? WgCd { get; set; }
    public int SortOrder { get; set; }
    /// <summary>工程ステータス：0=未着手 1=着手中 2=完了 3=中断 9=取消</summary>
    public int ProcessStatus { get; set; }
    /// <summary>指図ステータス（バー色判定の補助）</summary>
    public int WorkOrderStatus { get; set; }
    public int Priority { get; set; }
    public DateTime? PlanStartTime { get; set; }
    public DateTime? PlanEndTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public decimal? PlanQty { get; set; }
    public decimal GoodQty { get; set; }
    public decimal DefectQty { get; set; }
}

/// <summary>ガント チャート 検索条件</summary>
public class PlanningBoardQuery
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? ProcessCd { get; set; }
    public string? WgCd { get; set; }
    public string? MachineCd { get; set; }
    public List<int>? Statuses { get; set; }
    public string? BaseCd { get; set; }
}

/// <summary>ガント KPI サマリ</summary>
public class PlanningKpiDto
{
    public int TotalOrders { get; set; }
    public int InProgressOrders { get; set; }
    public int CompletedOrders { get; set; }
    public int DelayedOrders { get; set; }
    /// <summary>計画消化率 = 完了工程 / 全工程 * 100</summary>
    public decimal CompletionRate { get; set; }
    /// <summary>平均遅延日数</summary>
    public decimal AvgDelayDays { get; set; }
}

/// <summary>ME010 — バー ドラッグ後の日時更新リクエスト</summary>
public class RescheduleRequest
{
    public Guid Id { get; set; }
    public DateTime PlanStartTime { get; set; }
    public DateTime PlanEndTime { get; set; }
    public string? MachineCd { get; set; }
}

/// <summary>ME010 — 自動配置 リクエスト（PA050 ManufOrderPrio1-8 で並び替え）</summary>
public class AutoArrangeRequest
{
    public DateTime BaseDate { get; set; } = DateTime.Today;
    public string? WgCd { get; set; }
    public string? ProcessCd { get; set; }
    /// <summary>工程1件あたり既定リードタイム（時間）— PlanQty * Hours で計算</summary>
    public int DefaultHoursPerJob { get; set; } = 2;
}
