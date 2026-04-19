using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 拠点マスタ（据点主数据）- MSBBPA010 依赖
/// </summary>
[Table("M_Base")]
public class MasterBase : BaseBizEntity
{
    /// <summary>据点 CD（业务键）</summary>
    [Required, MaxLength(10)]
    public string BaseCd { get; set; } = string.Empty;

    /// <summary>据点名称</summary>
    [Required, MaxLength(100)]
    public string BaseName { get; set; } = string.Empty;

    /// <summary>是否为管理者/岐阜据点（管理者/岐阜事業所可修改見積拠点、其他只读）</summary>
    public bool IsAdminBase { get; set; } = false;

    /// <summary>据点单价阈值（汎用マスタ M044.数値1，折扣率超此值红色加粗）</summary>
    [Column(TypeName = "decimal(10,4)")]
    public decimal? DiscountThreshold { get; set; }

    /// <summary>显示顺序</summary>
    public int SortOrder { get; set; }
}
