using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Mes;

/// <summary>
/// 製造指図材料明細（MSBBME020 第3页）
/// </summary>
/// <remarks>
/// 仕様書 §9 No.3 T_WorkOrderMaterial
/// 業務複合 PK：WORK_ORDER_NO + PROCESS_CD + MATERIAL_CD
/// </remarks>
[Table("T_WorkOrderMaterial")]
public class WorkOrderMaterial : BaseBizEntity, IAuditable
{
    /// <summary>指図NO（FK）</summary>
    [Required, MaxLength(20)]
    public string WorkOrderNo { get; set; } = string.Empty;

    /// <summary>工程CD（PK2）</summary>
    [Required, MaxLength(10)]
    public string ProcessCd { get; set; } = string.Empty;

    /// <summary>材料CD（PK3）</summary>
    [Required, MaxLength(20)]
    public string MaterialCd { get; set; } = string.Empty;

    /// <summary>材料名</summary>
    [MaxLength(100)] public string? MaterialName { get; set; }

    /// <summary>工程材料区分：1=仕掛品 2=連産品 3=原料 4=印刷原紙</summary>
    [MaxLength(1)] public string? MaterialTypeDiv { get; set; }

    /// <summary>計画必要数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? PlanQty { get; set; }

    /// <summary>実績消費数量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal ActualQty { get; set; } = 0;

    /// <summary>単位</summary>
    [MaxLength(10)] public string? Unit { get; set; }

    /// <summary>手配状況：0=未手配 1=手配中 2=入荷済 3=不足</summary>
    public int SupplyStatus { get; set; } = 0;

    /// <summary>並び順</summary>
    public int SortOrder { get; set; }

    /// <summary>備考</summary>
    [MaxLength(200)] public string? Remarks { get; set; }
}
