using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>预算方案（按财年，每财年唯一）。</summary>
[Table("Fin_Budget")]
public class Budget : BaseTenantEntity
{
    [MaxLength(30)] public string No { get; set; } = "";
    [MaxLength(100)] public string Name { get; set; } = "";
    public int FiscalYear { get; set; }
    public BudgetScope Scope { get; set; } = BudgetScope.PnL;
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum BudgetScope { PnL = 1 }
