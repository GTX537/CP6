using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 受注工程備考（MSBBPA070 第3页 工程情報の備考1/2）
/// </summary>
/// <remarks>
/// 仕様書 §11.x T_OrderProcessNote
/// 業務 PK: (WebOrderNo, WebOrderDetailNo, ProductCd, OperationCd)
/// </remarks>
[Table("T_OrderProcessNote")]
public class OrderProcessNote : BaseBizEntity
{
    [Required, MaxLength(20)]
    public string WebOrderNo { get; set; } = string.Empty;

    public int WebOrderDetailNo { get; set; }

    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    [Required, MaxLength(15)]
    public string OperationCd { get; set; } = string.Empty;

    [MaxLength(200)] public string? Note1 { get; set; }
    [MaxLength(200)] public string? Note2 { get; set; }
}
