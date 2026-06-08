using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CP6.Entity;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// Material outbound shortage backflow record.
/// </summary>
[Table("T_MaterialShortage")]
public class MaterialShortage : BaseBizEntity
{
    [Required, MaxLength(20)]
    public string WorkOrderNo { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? RelatedOutboundNo { get; set; }

    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? LotNo { get; set; }

    [Column(TypeName = "decimal(21,8)")]
    public decimal RequiredQty { get; set; }

    [Column(TypeName = "decimal(21,8)")]
    public decimal AvailableQty { get; set; }

    public DateTime DetectedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = MaterialShortageStatus.Open;

    [MaxLength(500)]
    public string? Remark { get; set; }
}

public static class MaterialShortageStatus
{
    public const string Open = "OPEN";
    public const string Resolved = "RESOLVED";
    public const string Dismissed = "DISMISSED";
}
