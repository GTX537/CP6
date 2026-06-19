using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>资产卡片（核心，spec §2.2）。NetBookValue 不物化（计算属性，避免多处同步漂移）。</summary>
[Table("Fin_AssetCard")]
public class AssetCard : BaseTenantEntity
{
    [Required, MaxLength(30)] public string AssetNo { get; set; } = string.Empty;   // FA-yyyy-MM-NNNNN
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(100)] public string? SpecModel { get; set; }
    public Guid CategoryId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal OriginalValue { get; set; }
    [Column(TypeName = "decimal(7,4)")] public decimal SalvageRate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal SalvageValue { get; set; }
    public DepreciationMethod Method { get; set; } = DepreciationMethod.StraightLine;
    public int UsefulLifeMonths { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? TotalWorkload { get; set; }
    [MaxLength(20)] public string? WorkloadUnit { get; set; }
    public DateTime AcquisitionDate { get; set; }
    /// <summary>起折期间 yyyy-MM（= 购置日次月，D2；启用时定格）</summary>
    [MaxLength(7)] public string? DepreciationStartPeriod { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal AccumulatedDepreciation { get; set; }
    public int DepreciatedPeriods { get; set; }
    /// <summary>净值（不物化）：OriginalValue − AccumulatedDepreciation</summary>
    [NotMapped] public decimal NetBookValue => OriginalValue - AccumulatedDepreciation;
    /// <summary>折旧费用科目卡片覆盖（null=取分类默认）</summary>
    public Guid? DeprecExpenseAccountId { get; set; }
    public Guid? CostCenterId { get; set; }
    public Guid? MachineId { get; set; }
    public Guid? DeptId { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.Draft;
    [MaxLength(200)] public string? Location { get; set; }
    [MaxLength(100)] public string? Custodian { get; set; }
    /// <summary>期初建卡标记（true=不生成取得凭证、允许录初始累计）</summary>
    public bool IsOpeningImport { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
    [Timestamp] public byte[]? RowVersion { get; set; }
}
