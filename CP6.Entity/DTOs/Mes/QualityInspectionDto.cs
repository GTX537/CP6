namespace CP6.Entity.DTOs.Mes;

/// <summary>品質検査ヘッダ DTO（ME060 / ME070）</summary>
public class QualityInspectionDto
{
    public Guid Id { get; set; }
    public string InspectionNo { get; set; } = string.Empty;
    public string WorkOrderNo { get; set; } = string.Empty;
    public string? ProductCd { get; set; }
    public string? ProductName { get; set; }
    public string? ProcessCd { get; set; }
    public string? ProcessName { get; set; }
    public DateTime InspectionDate { get; set; } = DateTime.Today;
    public string InspectorCd { get; set; } = string.Empty;
    public string? InspectorName { get; set; }
    public int InspectionType { get; set; } = 3;
    public string? TemplateCd { get; set; }
    public decimal? InspectionQty { get; set; }
    public decimal? SampleQty { get; set; }
    public int? OverallResult { get; set; }
    public int? DispositionAction { get; set; }
    public string? JudgmentReason { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreateDate { get; set; }

    /// <summary>合格件数 / 全件数 → 合格率（一覧表示用）</summary>
    public int ItemCount { get; set; }
    public int PassCount { get; set; }
    public decimal PassRate { get; set; }

    public List<QualityInspectionItemDto> Items { get; set; } = new();
}

/// <summary>品質検査項目明細 DTO</summary>
public class QualityInspectionItemDto
{
    public Guid Id { get; set; }
    public string InspectionNo { get; set; } = string.Empty;
    public int ItemSeqNo { get; set; }
    public string? ItemName { get; set; }
    public string? InspectionMethod { get; set; }
    public decimal? StandardValue { get; set; }
    public decimal? UpperLimit { get; set; }
    public decimal? LowerLimit { get; set; }
    public decimal? MeasuredValue { get; set; }
    public string? MeasuredText { get; set; }
    public int? Result { get; set; }
    public string? Unit { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>検査項目テンプレート DTO（M_InspectionTemplate 引入用）</summary>
public class InspectionTemplateDto
{
    public Guid Id { get; set; }
    public string TemplateCd { get; set; } = string.Empty;
    public int ItemSeqNo { get; set; }
    public string? TemplateName { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? InspectionMethod { get; set; }
    public decimal? StandardValue { get; set; }
    public decimal? UpperLimit { get; set; }
    public decimal? LowerLimit { get; set; }
    public string? Unit { get; set; }
    public string? ProcessCd { get; set; }
    public bool RequiredFlg { get; set; }
    public bool ActiveFlg { get; set; }
}

/// <summary>品質検査一覧 検索条件</summary>
public class QualityInspectionSearchQuery
{
    public string? InspectionNo { get; set; }
    public string? WorkOrderNo { get; set; }
    public string? ProcessCd { get; set; }
    public string? InspectorCd { get; set; }
    public int? InspectionType { get; set; }
    public int? OverallResult { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
