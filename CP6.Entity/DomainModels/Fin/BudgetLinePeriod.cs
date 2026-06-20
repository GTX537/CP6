using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>预算行按月分解（财年期号 1..12）。随 BudgetLine 整体保存。</summary>
[Table("Fin_BudgetLinePeriod")]
public class BudgetLinePeriod : BaseTenantEntity
{
    public Guid BudgetLineId { get; set; }
    public int PeriodNo { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
}
