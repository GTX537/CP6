using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Crm;

[Table("Crm_Opportunity")]
public class CrmOpportunity : BaseBizEntity, IDataScoped, IAuditable
{
    [Required, MaxLength(30)] public string OpportunityNo { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public Guid LeadId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? PrimaryContactId { get; set; }
    public CrmOpportunityStage Stage { get; set; } = CrmOpportunityStage.Qualification;
    public decimal? ExpectedAmount { get; set; }
    [Required, MaxLength(3)] public string Currency { get; set; } = "USD";
    public DateTime? ExpectedCloseDate { get; set; }
    public Guid? OwnerUserId { get; set; }
    public Guid? DeptId { get; set; }
    [MaxLength(500)] public string? LostReason { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? WonAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    [MaxLength(20)] public string? AcceptedQuotationNo { get; set; }
    [MaxLength(30)] public string? WinningOrderNo { get; set; }
    [MaxLength(100)] public string? FirstSource { get; set; }
    [MaxLength(100)] public string? LastSource { get; set; }
}

[Table("Crm_StageHistory")]
public class CrmStageHistory : BaseTenantEntity
{
    [Required, MaxLength(30)] public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    [Required, MaxLength(40)] public string FromStage { get; set; } = string.Empty;
    [Required, MaxLength(40)] public string ToStage { get; set; } = string.Empty;
    [MaxLength(500)] public string? Reason { get; set; }
    public DateTime ChangedAt { get; set; }
    public Guid? ChangedByUserId { get; set; }
}

[Table("Crm_ErpLink")]
public class CrmErpLink : BaseTenantEntity
{
    public Guid OpportunityId { get; set; }
    [Required, MaxLength(30)] public string ErpEntityType { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string ErpEntityKey { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}

[Table("Crm_MergeRecord")]
public class CrmMergeRecord : BaseTenantEntity
{
    public Guid SourceLeadId { get; set; }
    public Guid TargetLeadId { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
    public Guid? MergedByUserId { get; set; }
    public DateTime MergedAt { get; set; }
}

[Table("Crm_IntakeConfig")]
public class CrmIntakeConfig : BaseTenantEntity, IAuditable
{
    [Required, MaxLength(100)] public string Name { get; set; } = "Default";
    public Guid? DefaultDeptId { get; set; }
    public Guid? DefaultOwnerUserId { get; set; }
    public int FirstResponseSlaMinutes { get; set; } = 240;
    public int WarningBeforeMinutes { get; set; } = 60;
    public bool EmailNotificationEnabled { get; set; } = true;
    public bool Enable { get; set; } = true;
}

[Table("Crm_IntakeMember")]
public class CrmIntakeMember : BaseTenantEntity
{
    public Guid IntakeConfigId { get; set; }
    public Guid UserId { get; set; }
}
