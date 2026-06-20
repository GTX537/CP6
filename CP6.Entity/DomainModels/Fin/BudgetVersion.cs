using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>预算版本。方案下多版本；至多一个 IsActive，作控制+报表唯一基准。</summary>
[Table("Fin_BudgetVersion")]
public class BudgetVersion : BaseTenantEntity
{
    public Guid BudgetId { get; set; }
    public int VersionNo { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    public BudgetVersionStatus Status { get; set; } = BudgetVersionStatus.Draft;
    public bool IsActive { get; set; }
    public BudgetControlMode DefaultControlMode { get; set; } = BudgetControlMode.None;
    public BudgetControlBasis DefaultControlBasis { get; set; } = BudgetControlBasis.Ytd;
    public Guid? ApprovalInstanceId { get; set; }
    [MaxLength(50)] public string? ApprovalRef { get; set; }
    public DateTime? SubmittedAt { get; set; }
    [MaxLength(100)] public string? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    [MaxLength(100)] public string? ApprovedBy { get; set; }
    [MaxLength(500)] public string? RejectReason { get; set; }
    [Timestamp] public byte[]? RowVersion { get; set; }
}

public enum BudgetVersionStatus { Draft = 0, PendingApproval = 1, Approved = 2, Rejected = 3, Archived = 4 }
public enum BudgetControlMode { None = 0, Warn = 1, Block = 2 }
public enum BudgetControlBasis { Ytd = 0, Period = 1 }
