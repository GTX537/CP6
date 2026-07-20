using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>Space analytics settings. Exactly one active row is allowed per tenant.</summary>
[Table("Space_AnalyticsConfig")]
public class Space_AnalyticsConfig : BaseBizEntity, IAuditable
{
    public int WindowDays { get; set; } = 90;

    /// <summary>quantity or frequency.</summary>
    [Required, MaxLength(20)]
    public string Metric { get; set; } = "quantity";

    [Column(TypeName = "decimal(6,5)")]
    public decimal ThresholdA { get; set; } = 0.80m;

    [Column(TypeName = "decimal(6,5)")]
    public decimal ThresholdB { get; set; } = 0.95m;

    public int StaleAfterHours { get; set; } = 48;

    /// <summary>Tenant-local hour (0-23) after which the daily snapshot is due.</summary>
    public int ScheduledHourLocal { get; set; } = 2;

    public bool EnableScheduledSnapshot { get; set; } = true;
}
