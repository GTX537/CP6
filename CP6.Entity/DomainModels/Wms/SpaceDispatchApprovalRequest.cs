using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// Durable OA business record for an immutable Space dispatch recommendation
/// selection and its all-or-nothing MobileTask assignment result.
/// </summary>
[Table("T_SpaceDispatchApprovalRequest")]
public sealed class SpaceDispatchApprovalRequest : BaseBizEntity
{
    public Guid SiteId { get; set; }
    public Guid RecommendationId { get; set; }
    public Guid PublishedVersionId { get; set; }

    [Required, MaxLength(10)]
    public string WarehouseCode { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string RecommendationDefinitionVersion { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string RecommendationRequestHash { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string PayloadHash { get; set; } = string.Empty;

    [Required]
    public string SelectionJson { get; set; } = "[]";

    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string Status { get; set; } = "PendingApproval";

    public Guid RequestedById { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public Guid FlowInstanceId { get; set; }
    public Guid? DecidedById { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public DateTime? AppliedAtUtc { get; set; }

    [Required, MaxLength(100)]
    public string AdapterId { get; set; } = string.Empty;

    [Required]
    public string ResultJson { get; set; } = "[]";

    [MaxLength(100)]
    public string? FailureCode { get; set; }
}
