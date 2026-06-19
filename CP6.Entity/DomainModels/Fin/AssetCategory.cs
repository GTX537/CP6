using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>资产分类（主数据，树·驱动默认值 + 三科目路由，spec §2.1）。</summary>
[Table("Fin_AssetCategory")]
public class AssetCategory : BaseTenantEntity
{
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public int Level { get; set; } = 1;
    public DepreciationMethod DefaultMethod { get; set; } = DepreciationMethod.StraightLine;
    public int DefaultUsefulLifeMonths { get; set; }
    [Column(TypeName = "decimal(7,4)")] public decimal DefaultSalvageRate { get; set; }
    /// <summary>固定资产科目（默认 1601）</summary>
    public Guid AssetAccountId { get; set; }
    /// <summary>累计折旧科目（默认 1602）</summary>
    public Guid AccumDeprecAccountId { get; set; }
    /// <summary>折旧费用科目（机器→5101.01 / 管理→6002 / 销售→6001，D8 路由核心，叶子科目）</summary>
    public Guid DeprecExpenseAccountId { get; set; }
    public bool IsActive { get; set; } = true;
    [Timestamp] public byte[]? RowVersion { get; set; }
}
