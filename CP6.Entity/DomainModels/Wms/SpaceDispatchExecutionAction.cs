using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// Idempotent, tenant-scoped receipt for an explicit retry or safe assignment
/// compensation requested against a Space dispatch approval.
/// </summary>
[Table("T_SpaceDispatchExecutionAction")]
public sealed class SpaceDispatchExecutionAction : BaseBizEntity
{
    public Guid ApprovalRequestId { get; set; }
    public Guid SiteId { get; set; }
    public Guid RecommendationId { get; set; }

    [Required, MaxLength(32)]
    public string ActionType { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string PayloadHash { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string Status { get; set; } = string.Empty;

    public Guid RequestedById { get; set; }
    public DateTime RequestedAtUtc { get; set; }

    [Required, MaxLength(100)]
    public string AdapterId { get; set; } = string.Empty;

    [Required]
    public string ReceiptJson { get; set; } = "[]";

    [MaxLength(100)]
    public string? FailureCode { get; set; }
}
