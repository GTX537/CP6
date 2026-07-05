using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// WMS 侧库位消费表（Space ch04 §5.3 v1.1）。接收 Space LocationPublished 发布的库位目录，
/// 是发布的物理落点与幂等判据 lastVersion 的存放处。
/// Id = Space LocationId（稳定主键，跨系统同一身份，由发布方给定、非自动生成）。
/// 与 T_Stock 靠 (WarehouseCd, LocationCode) 逻辑关联，不加物理 FK（库位目录与库存事务解耦演进）。
/// </summary>
[Table("T_WmsBin")]
public class WmsBin : BaseBizEntity
{
    /// <summary>冻结的 join key（发布后不变，ch04 §3.1）</summary>
    [Required, MaxLength(100)]
    public string LocationCode { get; set; } = string.Empty;

    /// <summary>仓库维度（SiteCode↔WarehouseCd 映射得来，ch04 §3.4，多仓防串仓）</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>已消费的最新发布版本（= lastVersion，幂等判据，ch04 §3.3）</summary>
    public long Version { get; set; }

    /// <summary>变长层级路径 JSON（区/巷/架…，不含坐标几何，ch04 §3.2）</summary>
    public string PathJson { get; set; } = "{}";

    /// <summary>业务属性 JSON（格口尺寸等，ch04 §3.1）</summary>
    public string AttrsJson { get; set; } = "{}";

    /// <summary>是否启用（DEACTIVATE 置 false，ch04 §6）</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>最近一次成功消费时间</summary>
    public DateTime? LastPublishedAt { get; set; }

    /// <summary>最近一次发布人（payload publishedBy，溯源，ch04 §2.1）</summary>
    [MaxLength(100)]
    public string? LastPublishedBy { get; set; }
}
