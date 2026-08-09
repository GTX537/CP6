using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>
/// Space 专用只追加审计事件。继承 <see cref="BaseTenantEntity"/> 以获得租户盖章和查询隔离。
/// </summary>
[Table("Space_AuditEvent")]
public sealed class Space_AuditEvent : BaseTenantEntity
{
    public DateTime OccurredAtUtc { get; set; }

    [Required, MaxLength(16)]
    public string ActorType { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ActorId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ActorName { get; set; }

    [MaxLength(100)]
    public string? OrganizationContextId { get; set; }

    [Required, MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string ResourceType { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? ResourceId { get; set; }

    public Guid? SiteId { get; set; }

    public Guid? VersionId { get; set; }

    public Guid? FloorId { get; set; }

    [Required, MaxLength(16)]
    public string Outcome { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ReasonCode { get; set; }

    public string? AuthorizationEvidenceJson { get; set; }

    [Column(TypeName = "char(64)")]
    public string? BeforeHash { get; set; }

    [Column(TypeName = "char(64)")]
    public string? AfterHash { get; set; }

    public Guid CorrelationId { get; set; }

    [Required, MaxLength(64), Column(TypeName = "varchar(64)")]
    public string TraceId { get; set; } = string.Empty;

    public Guid? JobId { get; set; }

    public Guid? RunId { get; set; }

    public Guid? PublishAttemptId { get; set; }

    public int? AttemptNo { get; set; }

    [MaxLength(32)]
    public string? ClientType { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [MaxLength(256)]
    public string? UserAgent { get; set; }
}
