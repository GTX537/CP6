using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Crm;

[Table("Crm_Lead")]
public class CrmLead : BaseBizEntity, IDataScoped, IAuditable
{
    [Required, MaxLength(30)] public string LeadNo { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Subject { get; set; } = string.Empty;
    [MaxLength(4000), PiiField(Mode = PiiErase.Null)] public string? Description { get; set; }
    [MaxLength(200)] public string? ProductInterest { get; set; }

    [Required, MaxLength(200)] public string CompanyName { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string NormalizedCompanyName { get; set; } = string.Empty;
    [Required, MaxLength(160), PiiField(Mode = PiiErase.Placeholder)] public string ContactName { get; set; } = string.Empty;
    [MaxLength(200), PiiField(Mode = PiiErase.Null)] public string? Email { get; set; }
    [MaxLength(200), PiiField(Mode = PiiErase.Null)] public string? NormalizedEmail { get; set; }
    [MaxLength(50), PiiField(Mode = PiiErase.Null)] public string? Phone { get; set; }
    [MaxLength(50), PiiField(Mode = PiiErase.Null)] public string? NormalizedPhone { get; set; }

    public CrmLeadStatus Status { get; set; } = CrmLeadStatus.New;
    public CrmSourceChannel SourceChannel { get; set; }
    public Guid? OwnerUserId { get; set; }
    public Guid? DeptId { get; set; }
    public DateTime SlaDueAt { get; set; }
    public DateTime? FirstResponseAt { get; set; }
    public DateTime? QualifiedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    [MaxLength(500)] public string? DisqualificationReason { get; set; }

    public bool PrivacyConsent { get; set; }
    public DateTime? PrivacyConsentAt { get; set; }
    [MaxLength(50)] public string? PrivacyPolicyVersion { get; set; }
    public bool IsQuarantined { get; set; }
    [MaxLength(500)] public string? RiskReason { get; set; }

    public Guid? AccountId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? ConvertedOpportunityId { get; set; }
    public Guid? MergedIntoLeadId { get; set; }

    [MaxLength(100)] public string? FirstSource { get; set; }
    [MaxLength(100)] public string? LastSource { get; set; }
    [MaxLength(200)] public string? FirstLandingPage { get; set; }
    [MaxLength(200)] public string? LastLandingPage { get; set; }
    [MaxLength(100)] public string? FirstUtmCampaign { get; set; }
    [MaxLength(100)] public string? LastUtmCampaign { get; set; }
}

[Table("Crm_Collaborator")]
public class CrmCollaborator : BaseTenantEntity
{
    [Required, MaxLength(30)] public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(30)] public string? CollaborationRole { get; set; }
}

[Table("Crm_Activity")]
public class CrmActivity : BaseTenantEntity, IAuditable
{
    [Required, MaxLength(30)] public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public CrmActivityType ActivityType { get; set; }
    [Required, MaxLength(200)] public string Subject { get; set; } = string.Empty;
    [MaxLength(4000), PiiField(Mode = PiiErase.Null)] public string? Details { get; set; }
    public DateTime OccurredAt { get; set; }
    public bool IsCustomerFacing { get; set; }
    public DateTime? NextActionAt { get; set; }
}

[Table("Crm_SourceTouch")]
public class CrmSourceTouch : BaseTenantEntity
{
    public Guid LeadId { get; set; }
    [Required, MaxLength(100)] public string Source { get; set; } = string.Empty;
    [MaxLength(100)] public string? Medium { get; set; }
    [MaxLength(100)] public string? Campaign { get; set; }
    [MaxLength(100)] public string? Content { get; set; }
    [MaxLength(100)] public string? Term { get; set; }
    [MaxLength(500), PiiField(Mode = PiiErase.Null)] public string? LandingPage { get; set; }
    [MaxLength(500), PiiField(Mode = PiiErase.Null)] public string? Referrer { get; set; }
    public DateTime TouchedAt { get; set; }
}

[Table("Crm_PublicSubmission")]
public class CrmPublicSubmission : BaseTenantEntity
{
    public Guid FormId { get; set; }
    public Guid? LeadId { get; set; }
    [Required, MaxLength(64)] public string IdempotencyHash { get; set; } = string.Empty;
    [MaxLength(64), PiiField(Mode = PiiErase.Null)] public string? IpHash { get; set; }
    [MaxLength(300), PiiField(Mode = PiiErase.Null)] public string? UserAgent { get; set; }
    public CrmPublicSubmissionStatus Status { get; set; }
    [MaxLength(500)] public string? RiskReason { get; set; }
}
