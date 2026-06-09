using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Erp;

/// <summary>
/// 御見積書明細(印字用) - MSBBPA030 §2.9.3
/// </summary>
/// <remarks>
/// 打印到正式报价书 PDF 上的明细数据。
/// 每行可关联一个 見積計算書NO（来自「使用」チェックの自動追加），也可手工追加。
/// </remarks>
[Table("T_QuotationDetail")]
public class QuotationDetail : BaseBizEntity
{
    /// <summary>御見積書NO（复合主键一部分；FK → T_Quotation）</summary>
    [Required]
    [MaxLength(20)]
    public string QtnNo { get; set; } = string.Empty;

    /// <summary>明細NO（行序号；复合主键一部分）</summary>
    public int DetailNo { get; set; }

    /// <summary>商品名1（品名1）</summary>
    [MaxLength(100)]
    public string? ItemName1 { get; set; }

    /// <summary>商品名2（品名2）</summary>
    [MaxLength(100)]
    public string? ItemName2 { get; set; }

    /// <summary>数量</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal? Quantity { get; set; }

    /// <summary>単価</summary>
    [Column(TypeName = "decimal(15,4)")]
    public decimal? UnitPrice { get; set; }

    /// <summary>単価単位</summary>
    [MaxLength(11)]
    public string? Unit { get; set; }

    /// <summary>金額 = 数量 × 単価</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? Amount { get; set; }

    /// <summary>合計金額印字（行级 CheckBox）</summary>
    public bool PrintTotalFlg { get; set; } = true;

    /// <summary>关联の見積計算書NO（「使用」チェック自動設定、手工追加时为空）</summary>
    [MaxLength(20)]
    public string? QtnCalcNo { get; set; }
}
