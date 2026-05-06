using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 受注加工材料（MSBBPA070 材料設定）
/// </summary>
/// <remarks>
/// 仕様書 §11.4 T_OrderMaterial
/// 業務 PK: (WebOrderNo, WebOrderDetailNo, ProductCd, ProcessCd, MaterialCd)
/// </remarks>
[Table("T_OrderMaterial")]
public class OrderMaterial : BaseBizEntity
{
    [Required, MaxLength(20)]
    public string WebOrderNo { get; set; } = string.Empty;

    public int WebOrderDetailNo { get; set; }

    [Required, MaxLength(15)]
    public string ProductCd { get; set; } = string.Empty;

    [Required, MaxLength(15)]
    public string ProcessCd { get; set; } = string.Empty;

    [Required, MaxLength(15)]
    public string MaterialCd { get; set; } = string.Empty;

    /// <summary>工程材料区分：1=仕掛品 / 2=連産品 / 3=原料 / 4=印刷原紙</summary>
    [Required, MaxLength(1)]
    public string MaterialTypeDiv { get; set; } = "3";

    [MaxLength(15)] public string? ItemCd { get; set; }
    [MaxLength(10)] public string? Branch1 { get; set; }
    [MaxLength(10)] public string? Branch2 { get; set; }
    [MaxLength(10)] public string? Branch3 { get; set; }

    /// <summary>受給区分：1=無償支給 / 2=有償支給</summary>
    [Required, MaxLength(1)]
    public string SupplyDiv { get; set; } = "1";

    [Column(TypeName = "decimal(21,8)")]
    public decimal SupplyUnitPrice { get; set; }

    public int SortOrder { get; set; }
}
