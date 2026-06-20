using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>预算行=维度桶（科目×成本中心(可空)×成本对象(可空)）。一版一桶唯一。</summary>
[Table("Fin_BudgetLine")]
public class BudgetLine : BaseTenantEntity
{
    public Guid VersionId { get; set; }
    public Guid AccountId { get; set; }

    // 真实业务维度（可空，承载 FK/显示）
    public Guid? CostCenterId { get; set; }
    [MaxLength(20)] public string? CostObjectType { get; set; }
    [MaxLength(50)] public string? CostObjectId { get; set; }

    // 规范化键（not-null，仅供唯一索引；服务层派生，前端不可见）
    public Guid CostCenterKey { get; set; }
    [MaxLength(20)] public string CostObjectTypeKey { get; set; } = "";
    [MaxLength(50)] public string CostObjectIdKey { get; set; } = "";

    [Column(TypeName = "decimal(18,2)")] public decimal AnnualAmount { get; set; }
    public BudgetControlMode? ControlMode { get; set; }
    public BudgetControlBasis? ControlBasis { get; set; }
    [MaxLength(500)] public string? Memo { get; set; }
    [Timestamp] public byte[]? RowVersion { get; set; }

    /// <summary>由真实维度派生规范化键（保存前调）。</summary>
    public void NormalizeKeys()
    {
        CostCenterKey = CostCenterId ?? Guid.Empty;
        CostObjectTypeKey = CostObjectType ?? "";
        CostObjectIdKey = CostObjectId ?? "";
    }
}
