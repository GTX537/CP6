namespace CP6.Entity.DTOs.Mes;

/// <summary>製造実績 DTO（ME040/050）</summary>
public class ProductionResultDto
{
    public Guid Id { get; set; }
    public string ResultNo { get; set; } = string.Empty;
    public string WorkOrderNo { get; set; } = string.Empty;
    /// <summary>関連製品名（一覧表示用）</summary>
    public string? ProductName { get; set; }
    public string ProcessCd { get; set; } = string.Empty;
    public string? ProcessName { get; set; }
    public string? TaskCd { get; set; }
    public int ResultType { get; set; }
    public string OperatorCd { get; set; } = string.Empty;
    public string? OperatorName { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }
    public decimal GoodQty { get; set; }
    public decimal DefectQty { get; set; }
    public decimal? ActualLossRate { get; set; }
    public string? DefectReasonCd { get; set; }
    public string? SuspendReasonCd { get; set; }
    public string? MachineCd { get; set; }
    public string? ResultNote { get; set; }
    public DateTime CreateDate { get; set; }
}

/// <summary>実績入力 リクエスト（ME040）</summary>
public class ProductionResultRequest
{
    public string WorkOrderNo { get; set; } = string.Empty;
    public string ProcessCd { get; set; } = string.Empty;
    public string? TaskCd { get; set; }
    public string OperatorCd { get; set; } = string.Empty;
    public string? OperatorName { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }
    public decimal GoodQty { get; set; }
    public decimal DefectQty { get; set; }
    public string? DefectReasonCd { get; set; }
    public string? SuspendReasonCd { get; set; }
    public string? MachineCd { get; set; }
    public string? ResultNote { get; set; }
}

/// <summary>実績一覧 検索条件</summary>
public class ProductionResultSearchQuery
{
    public string? WorkOrderNo { get; set; }
    public string? ProcessCd { get; set; }
    public string? OperatorCd { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? ResultType { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>受注 → 指図 自動展開 リクエスト</summary>
public class ExpandFromOrderRequest
{
    /// <summary>受注Web NO（PA070）</summary>
    public string WebOrderNo { get; set; } = string.Empty;
    /// <summary>受注明細NO（複数指定可）</summary>
    public List<int>? WebOrderDetailNos { get; set; }
    public string? BaseCd { get; set; }
    public int Priority { get; set; } = 1;
}
