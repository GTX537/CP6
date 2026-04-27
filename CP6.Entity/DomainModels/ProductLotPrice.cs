using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 製品ロット別単価マスタ（MSBBPA050 第5页ロット別単価）
/// </summary>
/// <remarks>
/// 仕様書 §14.4 T_ProductLotPrice
/// 業務複合 PK：製品コード + 明細NO
/// </remarks>
[Table("T_ProductLotPrice")]
public class ProductLotPrice : BaseBizEntity
{
    /// <summary>製品コード（FK + PK1）</summary>
    [Required, MaxLength(15)]
    public string ProductCd { get; set; } = string.Empty;

    /// <summary>明細NO（PK2、行番号）</summary>
    public int DetailNo { get; set; }

    // ───── 品目CD+枝番（原価参照時使用） ─────
    [MaxLength(15)] public string? ItemCd { get; set; }
    [MaxLength(10)] public string? Branch1 { get; set; }
    [MaxLength(10)] public string? Branch2 { get; set; }
    [MaxLength(10)] public string? Branch3 { get; set; }

    // ───── 単価適用日 ─────
    public DateTime? CurrentPriceDate { get; set; }
    public DateTime? NewPriceDate { get; set; }
    public DateTime? CurrentBranchDate { get; set; }
    public DateTime? NewBranchDate { get; set; }

    /// <summary>ロット数</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal LotQty { get; set; }

    // ───── セット単価 ─────
    [Column(TypeName = "decimal(21,8)")] public decimal? CurrentSetPrice { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? NewSetPrice { get; set; }

    // ───── 個別単価（見積計算書の決定単価から引入） ─────
    [Column(TypeName = "decimal(21,8)")] public decimal? CurrentUnitPrice { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? NewUnitPrice { get; set; }

    // ───── 本支店単価 ─────
    [Column(TypeName = "decimal(21,8)")] public decimal? CurrentBranchPrice { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? NewBranchPrice { get; set; }

    /// <summary>仕入単価</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal? PurchasePrice { get; set; }

    /// <summary>単価適用基準：0=受注日基準 / 1=納入日基準</summary>
    [MaxLength(1)] public string? PriceApplyBasis { get; set; }
}
