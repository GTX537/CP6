using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>折旧批次头（每期一批量批次；DisposalFinal 单资产 Run 不计入「每期单批」，spec §2.3）。</summary>
[Table("Fin_DepreciationRun")]
public class DepreciationRun : BaseTenantEntity
{
    [Required, MaxLength(30)] public string No { get; set; } = string.Empty;   // DEP-yyyy-MM-NNNNN
    public Guid FiscalPeriodId { get; set; }
    [MaxLength(7)] public string PeriodYearMonth { get; set; } = string.Empty;
    public DepreciationRunStatus Status { get; set; } = DepreciationRunStatus.Draft;
    public DepreciationRunMode RunMode { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalAmount { get; set; }
    public int AssetCount { get; set; }
    public Guid? JournalEntryId { get; set; }
    public DateTime RunAt { get; set; }
    [MaxLength(100)] public string RunBy { get; set; } = string.Empty;
    public DateTime? PostedAt { get; set; }
    [MaxLength(100)] public string? PostedBy { get; set; }
    public DateTime? ReversedAt { get; set; }
    [MaxLength(100)] public string? ReversedBy { get; set; }
    [Timestamp] public byte[]? RowVersion { get; set; }
}
