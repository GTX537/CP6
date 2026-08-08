using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// Auditable, dual-person approval request for warehouse production feature flags.
/// The row is the durable business audit record for both the request and its application.
/// </summary>
[Table("T_WmsFeatureFlagChange")]
public sealed class WmsFeatureFlagChange : BaseBizEntity
{
    public Guid OperationId { get; set; }

    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    public bool BaseProductionMoveEnabled { get; set; }
    public bool BaseSerialLpnEnabled { get; set; }
    public int BaseScanRetentionDays { get; set; }

    [MaxLength(128)]
    public string BaseFeatureRowVersion { get; set; } = string.Empty;

    public bool TargetProductionMoveEnabled { get; set; }
    public bool TargetSerialLpnEnabled { get; set; }
    public int TargetScanRetentionDays { get; set; }

    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ChangeTicket { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? EvidenceUri { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = WmsFeatureFlagChangeStatus.Pending;

    public Guid RequestedById { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public Guid FlowInstanceId { get; set; }
    public Guid? DecidedById { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public DateTime? AppliedAtUtc { get; set; }

    [MaxLength(64)]
    public string? FailureCode { get; set; }
}

public static class WmsFeatureFlagChangeStatus
{
    public const string Pending = "PENDING";
    public const string Applied = "APPLIED";
    public const string Rejected = "REJECTED";
    public const string Stale = "STALE";
    public const string Cancelled = "CANCELLED";
    public const string Failed = "FAILED";
}
