using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>资产级折旧明细（每资产每批，追溯，spec §2.4）。</summary>
[Table("Fin_DepreciationEntry")]
public class DepreciationEntry : BaseTenantEntity
{
    public Guid RunId { get; set; }
    public Guid AssetCardId { get; set; }
    public Guid FiscalPeriodId { get; set; }
    public DepreciationMethod Method { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DepreciationAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal OpeningAccumulated { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ClosingAccumulated { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal OpeningNetValue { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ClosingNetValue { get; set; }
    public Guid DeprecExpenseAccountId { get; set; }
    public Guid AccumDeprecAccountId { get; set; }
    public Guid? CostCenterId { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? WorkloadThisPeriod { get; set; }
    [Timestamp] public byte[]? RowVersion { get; set; }
}
