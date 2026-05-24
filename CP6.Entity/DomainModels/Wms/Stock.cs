using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 在庫実況テーブル（MSBBWM020 + 全 WMS の中核）
/// </summary>
/// <remarks>
/// 仕様書 §12.2 / §13.3。
/// 業務 UK：(WarehouseCd, LocationCd, ProductCd, LotNo)。
/// 書込口は <c>StockMovementService</c> のみ。直接 UPDATE 禁止。
/// PhysicalQty - AllocatedQty = AvailableQty（DB に物化、Service で都度算出）。
/// </remarks>
[Table("T_Stock")]
public class Stock : BaseBizEntity
{
    /// <summary>倉庫CD（業務UK 構成 1/4）</summary>
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;

    /// <summary>ロケーションCD（業務UK 構成 2/4）</summary>
    [Required, MaxLength(30)]
    public string LocationCd { get; set; } = string.Empty;

    /// <summary>製品CD（業務UK 構成 3/4）</summary>
    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    /// <summary>ロットNO（業務UK 構成 4/4、ロット管理しない場合は空文字）</summary>
    [Required, MaxLength(30)]
    public string LotNo { get; set; } = string.Empty;

    /// <summary>物理在庫数</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal PhysicalQty { get; set; } = 0m;

    /// <summary>引当中数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal AllocatedQty { get; set; } = 0m;

    /// <summary>利用可能在庫（= PhysicalQty - AllocatedQty）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal AvailableQty { get; set; } = 0m;

    /// <summary>単位CD</summary>
    [MaxLength(10)] public string? UnitCd { get; set; }

    /// <summary>受入日</summary>
    public DateTime? ReceiveDate { get; set; }

    /// <summary>賞味期限（FEFO 引当用、§27）</summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>単価（在庫評価用）</summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal? UnitPrice { get; set; }

    /// <summary>リコール対象フラグ（§26.4、true の場合は出庫禁止）</summary>
    public bool RecallFlag { get; set; } = false;

    /// <summary>所有者区分：SELF/CUSTOMER（VMI 対応、§28.4）</summary>
    [MaxLength(10)] public string OwnerType { get; set; } = StockOwnerType.Self;

    /// <summary>所有者CD（VMI 客先CD、SELF の場合は null）</summary>
    [MaxLength(20)] public string? OwnerCd { get; set; }

    /// <summary>原紙ロールNO（§29 連携、null の場合は通常在庫）</summary>
    [MaxLength(20)] public string? PaperRollNo { get; set; }
}
