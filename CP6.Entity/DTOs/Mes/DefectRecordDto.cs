namespace CP6.Entity.DTOs.Mes;

/// <summary>不良品記録 DTO（ME080）</summary>
public class DefectRecordDto
{
    public Guid Id { get; set; }
    public string DefectNo { get; set; } = string.Empty;
    public string WorkOrderNo { get; set; } = string.Empty;
    public string? ProductCd { get; set; }
    public string? ProductName { get; set; }
    public string? ProcessCd { get; set; }
    public string? ProcessName { get; set; }
    public string? InspectionNo { get; set; }
    public DateTime OccurDate { get; set; } = DateTime.Today;
    public string? ReporterCd { get; set; }
    public string? ReporterName { get; set; }
    public string CategoryCd { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string? DetailCd { get; set; }
    public string? DetailName { get; set; }
    public decimal DefectQty { get; set; }
    public string DefectDescription { get; set; } = string.Empty;
    public string? CauseAnalysis { get; set; }
    public string? CorrectiveAction { get; set; }
    public string? AssigneeCd { get; set; }
    public string? AssigneeName { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    /// <summary>0=起票済 1=分析中 2=是正中 3=完了</summary>
    public int Status { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreateDate { get; set; }
}

/// <summary>不良品 検索条件</summary>
public class DefectRecordSearchQuery
{
    public string? DefectNo { get; set; }
    public string? WorkOrderNo { get; set; }
    public string? ProcessCd { get; set; }
    public string? CategoryCd { get; set; }
    public string? DetailCd { get; set; }
    public List<int>? Statuses { get; set; }
    public string? AssigneeCd { get; set; }
    public DateTime? OccurDateFrom { get; set; }
    public DateTime? OccurDateTo { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>不良分類マスタ DTO（M_DefectCategory）</summary>
public class DefectCategoryDto
{
    public Guid Id { get; set; }
    public string CategoryCd { get; set; } = string.Empty;
    public string DetailCd { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string? DetailName { get; set; }
    public int SortOrder { get; set; }
    public bool ActiveFlg { get; set; }
}
