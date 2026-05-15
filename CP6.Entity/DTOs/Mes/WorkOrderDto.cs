namespace CP6.Entity.DTOs.Mes;

/// <summary>製造指図 一覧/詳細 DTO（ME020 第1页 + ME030 一覧）</summary>
public class WorkOrderDto
{
    public Guid Id { get; set; }
    public string WorkOrderNo { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? OrderNo1 { get; set; }
    public string? OrderNo2 { get; set; }
    public string? OrderNo3 { get; set; }
    public string? WebOrderNo { get; set; }
    public string? CustomerCd { get; set; }
    public string? CustomerName { get; set; }
    public string ProductCd { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public decimal ProductionQty { get; set; }
    public decimal CompletedQty { get; set; }
    public decimal DefectQty { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public DateTime? PlanStartDate { get; set; }
    public DateTime? PlanEndDate { get; set; }
    public DateTime? ActualStartDate { get; set; }
    public DateTime? ActualEndDate { get; set; }
    public int Priority { get; set; }
    public string? LotNo { get; set; }
    public string? BaseCd { get; set; }
    public string? Remarks { get; set; }
    public int ProcessCount { get; set; }
    public int CompletedProcessCount { get; set; }
    /// <summary>進捗率 = CompletedQty / ProductionQty * 100</summary>
    public decimal ProgressRate { get; set; }
    /// <summary>遅延日数（= 今日 - 計画完了日、計画完了日 < 今日 且 未完了の時）</summary>
    public int DelayDays { get; set; }
    public DateTime CreateDate { get; set; }

    public List<WorkOrderProcessDto> Processes { get; set; } = new();
    public List<WorkOrderMaterialDto> Materials { get; set; } = new();
}

/// <summary>指図工程明細 DTO</summary>
public class WorkOrderProcessDto
{
    public Guid Id { get; set; }
    public string WorkOrderNo { get; set; } = string.Empty;
    public string ProcessCd { get; set; } = string.Empty;
    public string TaskCd { get; set; } = string.Empty;
    public string? ProcessName { get; set; }
    public int SortOrder { get; set; }
    public int ProcessStatus { get; set; }
    public string? MachineCd { get; set; }
    public string? WgCd { get; set; }
    public DateTime? PlanStartTime { get; set; }
    public DateTime? PlanEndTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }
    public decimal? PlanQty { get; set; }
    public decimal GoodQty { get; set; }
    public decimal DefectQty { get; set; }
    public decimal? StdLossRate { get; set; }
    public decimal? LeadTime { get; set; }
    public string? PrevProcessCd { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>指図材料明細 DTO</summary>
public class WorkOrderMaterialDto
{
    public Guid Id { get; set; }
    public string WorkOrderNo { get; set; } = string.Empty;
    public string ProcessCd { get; set; } = string.Empty;
    public string MaterialCd { get; set; } = string.Empty;
    public string? MaterialName { get; set; }
    public string? MaterialTypeDiv { get; set; }
    public decimal? PlanQty { get; set; }
    public decimal ActualQty { get; set; }
    public string? Unit { get; set; }
    public int SupplyStatus { get; set; }
    public int SortOrder { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>指図 検索条件（ME030 用）</summary>
public class WorkOrderSearchQuery
{
    public string? BaseCd { get; set; }
    public string? WorkOrderNo { get; set; }
    public string? OrderNo { get; set; }
    public string? ProductCd { get; set; }
    public string? CustomerCd { get; set; }
    public DateTime? DeliveryDateFrom { get; set; }
    public DateTime? DeliveryDateTo { get; set; }
    public DateTime? PlanStartDateFrom { get; set; }
    public DateTime? PlanStartDateTo { get; set; }
    public List<int>? Statuses { get; set; }
    public int? Priority { get; set; }
    public string? ProcessCd { get; set; }
    public string? WgCd { get; set; }
    /// <summary>遅延のみ</summary>
    public bool DelayedOnly { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
