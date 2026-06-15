using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 税码（章03 §2）。发票行算税用：<see cref="Rate"/> 税率，<see cref="Direction"/> 进项/销项，
/// <see cref="Recoverable"/> 可抵扣性——不可抵扣（false）时税额并入费用科目不单列进项税行。
/// </summary>
[Table("Fin_TaxCode")]
public class TaxCode : BaseEntity
{
    /// <summary>税码（唯一，如 "IN10"/"OUT10"）</summary>
    [Required, MaxLength(20)] public string Code { get; set; } = string.Empty;

    /// <summary>名称</summary>
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;

    /// <summary>税率（如 0.10 表 10%）</summary>
    [Column(TypeName = "decimal(9,6)")] public decimal Rate { get; set; }

    /// <summary>方向：进项 / 销项</summary>
    public TaxDirection Direction { get; set; }

    /// <summary>可抵扣？进项税 false 时并入成本，不生成独立进项税行</summary>
    public bool Recoverable { get; set; } = true;

    /// <summary>停用</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>税方向（章03）。</summary>
public enum TaxDirection
{
    /// <summary>进项（采购/应付）</summary>
    Input = 1,
    /// <summary>销项（销售/应收）</summary>
    Output = 2,
}
