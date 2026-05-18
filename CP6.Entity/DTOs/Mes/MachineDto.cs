namespace CP6.Entity.DTOs.Mes;

/// <summary>設備マスタ DTO</summary>
public class MachineDto
{
    public Guid Id { get; set; }
    public string MachineCd { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string? MachineType { get; set; }
    public string? ProcessCd { get; set; }
    public string? WgCd { get; set; }
    public string? BaseCd { get; set; }
    public int Status { get; set; }
    public int PlannedRunMinutesPerDay { get; set; }
    public decimal? StandardCycleSec { get; set; }
    public decimal? CapacityPerHour { get; set; }
    public DateTime? InstallDate { get; set; }
    public DateTime? LastMaintenanceDate { get; set; }
    public DateTime? NextMaintenanceDate { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Remarks { get; set; }
    public bool ActiveFlg { get; set; }

    /// <summary>当前活动の指図/工程（リアルタイム稼働情報）</summary>
    public string? CurrentWorkOrderNo { get; set; }
    public string? CurrentProcessCd { get; set; }

    /// <summary>本日 OEE（読み取り専用）</summary>
    public decimal? TodayOee { get; set; }
}

/// <summary>設備停止記録 DTO</summary>
public class MachineDowntimeDto
{
    public Guid Id { get; set; }
    public string DowntimeNo { get; set; } = string.Empty;
    public string MachineCd { get; set; } = string.Empty;
    public string? MachineName { get; set; }
    public string? WorkOrderNo { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int? DowntimeMinutes { get; set; }
    public int DowntimeType { get; set; }
    public string? ReasonCd { get; set; }
    public string? Description { get; set; }
    public string? OperatorCd { get; set; }
    public string? RecoveryOperatorCd { get; set; }
    public string? Remarks { get; set; }
    public DateTime CreateDate { get; set; }
}

/// <summary>設備検索クエリ</summary>
public class MachineSearchQuery
{
    public string? MachineCd { get; set; }
    public string? ProcessCd { get; set; }
    public string? WgCd { get; set; }
    public string? BaseCd { get; set; }
    public List<int>? Statuses { get; set; }
    public bool? ActiveOnly { get; set; }
}

/// <summary>停止記録 検索クエリ</summary>
public class DowntimeSearchQuery
{
    public string? MachineCd { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? DowntimeType { get; set; }
    public bool? OnlyOpen { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>OEE 日次 DTO</summary>
public class OeeDailyDto
{
    public Guid Id { get; set; }
    public DateTime OeeDate { get; set; }
    public string MachineCd { get; set; } = string.Empty;
    public string? MachineName { get; set; }
    public int PlannedRunMinutes { get; set; }
    public int ActualRunMinutes { get; set; }
    public int DowntimeMinutes { get; set; }
    public decimal GoodQty { get; set; }
    public decimal DefectQty { get; set; }
    public decimal Availability { get; set; }
    public decimal Performance { get; set; }
    public decimal Quality { get; set; }
    public decimal Oee { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>OEE 検索クエリ</summary>
public class OeeSearchQuery
{
    public string? MachineCd { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}

/// <summary>OEE 再計算リクエスト</summary>
public class OeeRecalcRequest
{
    public DateTime TargetDate { get; set; } = DateTime.Today;
    public string? MachineCd { get; set; }
}
