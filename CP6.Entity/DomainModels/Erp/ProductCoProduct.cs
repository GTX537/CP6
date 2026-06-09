using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Erp;

/// <summary>
/// 製品連産品マスタ（MSBBPA050 第3页トムソン工程 Popup の連産品リスト）
/// </summary>
/// <remarks>
/// 仕様書 §14.5 T_ProductCoProduct
/// 業務複合 PK：製品コード + 工程コード + 行番
/// 用途：工程CD = 0600/0601/0602（トムソン）の連産品行
/// </remarks>
[Table("T_ProductCoProduct")]
public class ProductCoProduct : BaseBizEntity
{
    /// <summary>製品コード（FK + PK1）</summary>
    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    /// <summary>工程コード（PK2、トムソン工程のCD）</summary>
    [Required, MaxLength(10)]
    public string ProcessCd { get; set; } = string.Empty;

    /// <summary>行番（PK3、連産品の行番号）</summary>
    public int RowNo { get; set; }

    /// <summary>連産品名</summary>
    [MaxLength(100)] public string? CoProductName { get; set; }

    /// <summary>数量比率（各連産品の産出比率；総計=トムソン工程の個数）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal QtyRatio { get; set; }

    /// <summary>次工程CD</summary>
    [MaxLength(10)] public string? NextProcessCd { get; set; }
}
