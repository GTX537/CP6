using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 印版・木型 管理（MSBBWM220）
/// </summary>
/// <remarks>
/// 仕様書 §31 印刷版・打抜木型のマスタ＋使用履歴。
/// 業務 PK：PlateNo（PLT-NNNNNN）
/// 顧客×製品×版/型 で 1 レコード。寿命（最大ショット数）管理 + メンテ要請。
/// 状态：0=使用可 1=メンテ中 2=寿命到達 3=廃版
/// </remarks>
[Table("T_PlateMoldStock")]
public class PlateMoldStock : BaseBizEntity, IAuditable
{
    [Required, MaxLength(25)]
    public string PlateNo { get; set; } = string.Empty;

    /// <summary>区分：PLATE=印版 / MOLD=木型 / CYL=シリンダ / OTHER</summary>
    [Required, MaxLength(10)]
    public string PlateType { get; set; } = "PLATE";

    /// <summary>顧客CD</summary>
    [MaxLength(20)] public string? CustomerCd { get; set; }

    /// <summary>顧客名</summary>
    [MaxLength(100)] public string? CustomerName { get; set; }

    /// <summary>製品CD（紐付け、任意）</summary>
    [MaxLength(20)] public string? ProductCd { get; set; }

    /// <summary>製品名・絵柄名</summary>
    [MaxLength(100)] public string? ProductName { get; set; }

    /// <summary>色数（4色 / 6色 等、版の場合）</summary>
    public int? ColorCount { get; set; }

    /// <summary>サイズ表記（自由文字、例: 540×750）</summary>
    [MaxLength(40)] public string? SizeNote { get; set; }

    /// <summary>製作日</summary>
    public DateTime? MadeDate { get; set; }

    /// <summary>製作費（任意）</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? MadeCost { get; set; }

    /// <summary>最大寿命ショット数（メーカー推奨）</summary>
    public int? MaxShots { get; set; }

    /// <summary>累計使用ショット数</summary>
    public int UsedShots { get; set; } = 0;

    /// <summary>最終使用日</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>次回メンテ予定（任意）</summary>
    public DateTime? NextMaintenanceDate { get; set; }

    /// <summary>保管 倉庫CD</summary>
    [MaxLength(10)] public string? WarehouseCd { get; set; }

    /// <summary>保管 ロケーションCD</summary>
    [MaxLength(30)] public string? LocationCd { get; set; }

    /// <summary>状态：0=使用可 1=メンテ中 2=寿命到達 3=廃版</summary>
    public int Status { get; set; } = 0;

    [MaxLength(500)] public string? Remarks { get; set; }
}
