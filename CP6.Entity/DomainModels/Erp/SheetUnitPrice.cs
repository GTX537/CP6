using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Erp;

/// <summary>
/// Web シート単価マスタ（PA130）
/// </summary>
/// <remarks>
/// 仕様書 §7 — 13 項目複合 PK
/// 取込区分=シート単価 → 本テーブル / 取込区分=見積用 → SheetUnitPriceEstimate（同一構造）
/// </remarks>
[Table("T_SheetUnitPrice")]
public class SheetUnitPrice : BaseBizEntity, IAuditable
{
    /// <summary>改定日（適用基準日；PK1）</summary>
    public DateTime RevisionDate { get; set; }

    /// <summary>拠点（PK2）</summary>
    [Required, MaxLength(10)] public string BaseCd { get; set; } = string.Empty;

    /// <summary>得意先 CD（PK3）— 取引先マスタ(得意先 FLG=1)</summary>
    [Required, MaxLength(20)] public string CustomerCd { get; set; } = string.Empty;

    /// <summary>シート段（PK4）</summary>
    [Required, MaxLength(4)] public string SheetFlute { get; set; } = string.Empty;

    /// <summary>原紙(表) PK5</summary>
    [Required, MaxLength(20)] public string PaperCdF { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string PrintCdF { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string EmbossCdF { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string PaperCdC { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string PrintCdC { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string EmbossCdC { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string PaperCdB { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string PrintCdB { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string EmbossCdB { get; set; } = string.Empty;

    /// <summary>シート単価</summary>
    [Column(TypeName = "decimal(15,4)")]
    public decimal UnitPrice { get; set; }

    /// <summary>営業担当（参照表示用：得意先担当マスタから引入）</summary>
    [MaxLength(20)] public string? SalesStaffCd { get; set; }
}

/// <summary>
/// Web シート単価（見積用）マスタ — PA130 取込区分=見積用 の書込み先
/// </summary>
/// <remarks>SheetUnitPrice と同一構造の別テーブル（仕様書 §6 §7）。</remarks>
[Table("T_SheetUnitPriceEstimate")]
public class SheetUnitPriceEstimate : BaseBizEntity, IAuditable
{
    public DateTime RevisionDate { get; set; }
    [Required, MaxLength(10)] public string BaseCd { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string CustomerCd { get; set; } = string.Empty;
    [Required, MaxLength(4)]  public string SheetFlute { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string PaperCdF { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string PrintCdF { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string EmbossCdF { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string PaperCdC { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string PrintCdC { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string EmbossCdC { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string PaperCdB { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string PrintCdB { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string EmbossCdB { get; set; } = string.Empty;
    [Column(TypeName = "decimal(15,4)")] public decimal UnitPrice { get; set; }
    [MaxLength(20)] public string? SalesStaffCd { get; set; }
}
