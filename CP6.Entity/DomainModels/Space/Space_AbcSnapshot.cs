using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>
/// One immutable, atomic ABC calculation for a site. ResultJson contains the classified
/// product rows so readers never observe a partially-written batch.
/// </summary>
[Table("Space_AbcSnapshot")]
public class Space_AbcSnapshot : BaseBizEntity, IAuditable
{
    public Guid SiteId { get; set; }

    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    public DateTime WindowFrom { get; set; }
    public DateTime WindowTo { get; set; }
    public DateTime CalculatedAt { get; set; }
    /// <summary>Tenant-local calendar date for scheduled runs; null for manual snapshots.</summary>
    [Column(TypeName = "date")]
    public DateOnly? ScheduledDate { get; set; }
    public int WindowDays { get; set; }

    [Required, MaxLength(20)]
    public string Metric { get; set; } = "quantity";

    [Column(TypeName = "decimal(6,5)")]
    public decimal ThresholdA { get; set; }

    [Column(TypeName = "decimal(6,5)")]
    public decimal ThresholdB { get; set; }

    public int ItemCount { get; set; }

    [Required, MaxLength(20)]
    public string Trigger { get; set; } = "scheduled";

    [Column(TypeName = "nvarchar(max)")]
    public string ResultJson { get; set; } = "[]";
}
