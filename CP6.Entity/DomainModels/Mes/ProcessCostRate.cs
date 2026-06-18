using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>工序费率（A2 · spec §3.3）。工作中心×生效区间 的 工/费双率（元/h）。</summary>
[Table("T_ProcessCostRate")]
public class ProcessCostRate : BaseBizEntity
{
    /// <summary>工作中心CD（业务键 → WorkCenter.WgCd）</summary>
    [Required, MaxLength(10)] public string WgCd { get; set; } = string.Empty;
    /// <summary>人工费率（元/h）</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal LaborRate { get; set; }
    /// <summary>制造费率（元/h）</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal OverheadRate { get; set; }
    /// <summary>生效日（Resolve 取 ≤ 基准日最新有效版本）</summary>
    public DateTime ValidFrom { get; set; }
    /// <summary>失效日（null = 长期）</summary>
    public DateTime? ValidTo { get; set; }
}
