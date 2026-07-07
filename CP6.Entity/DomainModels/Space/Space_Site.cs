using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>
/// 站点（Space 章00 §4，6 层数据模型顶层）。一个物理园区/仓库地址。
/// 继承 BaseBizEntity → 自带 TenantId（行级隔离）+ IsDeleted + RowVersion（乐观锁）。
/// </summary>
[Table("Space_Site")]
public class Space_Site : BaseBizEntity, IAuditable
{
    /// <summary>站点编码（租户内唯一）</summary>
    [Required, MaxLength(50)]
    public string SiteCode { get; set; } = string.Empty;

    /// <summary>站点名称</summary>
    [Required, MaxLength(100)]
    public string SiteName { get; set; } = string.Empty;

    /// <summary>地址</summary>
    [MaxLength(500)]
    public string? Address { get; set; }

    /// <summary>经度（可选，地图定位）</summary>
    public double? Lng { get; set; }

    /// <summary>纬度（可选，地图定位）</summary>
    public double? Lat { get; set; }

    /// <summary>是否启用</summary>
    public bool Enable { get; set; } = true;

    /// <summary>WMS 仓库编码映射（ch04 §3.4 SiteCode↔WarehouseCd；空=默认规则 WarehouseCd=SiteCode）</summary>
    [MaxLength(10)]
    public string? WarehouseCd { get; set; }
}
