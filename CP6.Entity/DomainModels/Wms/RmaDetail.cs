using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 返品（RMA）明細
/// </summary>
/// <remarks>
/// 業務 UK：(RmaNo, LineNo)
/// Judgement で行ごとに振分先（再販/修理/廃棄/仕入先返品）を決定する。
/// </remarks>
[Table("T_RmaDetail")]
public class RmaDetail : BaseBizEntity
{
    [Required, MaxLength(20)]
    public string RmaNo { get; set; } = string.Empty;

    public int LineNo { get; set; }

    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    [MaxLength(100)] public string? ProductName { get; set; }

    /// <summary>返品ロットNO（任意、不明な場合は空）</summary>
    [MaxLength(30)] public string LotNo { get; set; } = string.Empty;

    /// <summary>返品数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal Qty { get; set; }

    /// <summary>単位</summary>
    [MaxLength(10)] public string? UnitCd { get; set; }

    /// <summary>商品状態：NEW=新品未使用 / OPEN=開封済 / DAMAGED=破損</summary>
    [MaxLength(20)] public string ConditionLevel { get; set; } = "OPEN";

    /// <summary>判定：RESELL=再販可 / REPAIR=要修理 / SCRAP=廃棄 / SUPPLIER_RETURN=仕入先返品</summary>
    [MaxLength(30)] public string? Judgement { get; set; }

    /// <summary>振分先ロケーションCD（判定時にセット）</summary>
    [MaxLength(30)] public string? DestLocationCd { get; set; }

    /// <summary>返品入庫時の在庫 IN トランザクションNO</summary>
    [MaxLength(25)] public string? InboundTxnNo { get; set; }

    /// <summary>振分時の MOVE/ADJ トランザクションNO</summary>
    [MaxLength(25)] public string? DispositionTxnNo { get; set; }

    /// <summary>備考</summary>
    [MaxLength(500)] public string? Remarks { get; set; }
}
