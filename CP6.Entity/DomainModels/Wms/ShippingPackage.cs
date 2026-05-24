using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 出荷梱包（MSBBWM080）
/// </summary>
/// <remarks>
/// 仕様書 §9 出荷実績・梱包確定。
/// 出庫指示（OutboundType=2 出荷）の確定時に併せて作成される。
/// 業務 PK：PackageNo（PKGYYYYMMDD-NNNN）
/// </remarks>
[Table("T_ShippingPackage")]
public class ShippingPackage : BaseBizEntity
{
    /// <summary>梱包NO（業務 PK）</summary>
    [Required, MaxLength(20)]
    public string PackageNo { get; set; } = string.Empty;

    /// <summary>出庫指示NO（FK → T_OutboundOrder.OutboundNo）</summary>
    [Required, MaxLength(20)]
    public string OutboundNo { get; set; } = string.Empty;

    /// <summary>ケース数</summary>
    public int CaseQty { get; set; } = 1;

    /// <summary>総重量(kg)</summary>
    [Column(TypeName = "decimal(10,3)")]
    public decimal? TotalWeightKg { get; set; }

    /// <summary>総容積(m3)</summary>
    [Column(TypeName = "decimal(10,4)")]
    public decimal? TotalVolumeM3 { get; set; }

    /// <summary>配送業者CD</summary>
    [MaxLength(20)] public string? CarrierCd { get; set; }

    /// <summary>送り状追跡番号</summary>
    [MaxLength(50)] public string? TrackingNo { get; set; }

    /// <summary>出発日時</summary>
    public DateTime? DepartureTime { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }
}
