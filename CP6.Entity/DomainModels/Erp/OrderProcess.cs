using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Erp;

/// <summary>
/// 受注加工工程（MSBBPA070 第3页 工程情報）
/// </summary>
/// <remarks>
/// 仕様書 §11.3 T_OrderProcess
/// 業務 PK: (WebOrderNo, WebOrderDetailNo, ProductCd, OperationCd)
/// </remarks>
[Table("T_OrderProcess")]
public class OrderProcess : BaseBizEntity
{
    [Required, MaxLength(20)]
    public string WebOrderNo { get; set; } = string.Empty;

    public int WebOrderDetailNo { get; set; }

    /// <summary>製品コード</summary>
    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    /// <summary>作業コード（mc 工程コード；PK4）</summary>
    [Required, MaxLength(15)]
    public string OperationCd { get; set; } = string.Empty;

    /// <summary>工程コード（mc 品目コード）</summary>
    [Required, MaxLength(15)]
    public string ProcessCd { get; set; } = string.Empty;

    // ───── TOP 品目 / 工程品目 ─────
    [MaxLength(15)] public string? TopItemCd { get; set; }
    [MaxLength(10)] public string? TopBranch1 { get; set; }
    [MaxLength(10)] public string? TopBranch2 { get; set; }
    [MaxLength(10)] public string? TopBranch3 { get; set; }
    [MaxLength(15)] public string? ItemCd { get; set; }
    [MaxLength(10)] public string? Branch1 { get; set; }
    [MaxLength(10)] public string? Branch2 { get; set; }
    [MaxLength(10)] public string? Branch3 { get; set; }

    // ───── WG / 号機 / 配送 ─────
    [MaxLength(10)] public string? WorkingGroupCd { get; set; }
    [MaxLength(20)] public string? MachineOrVendor { get; set; }
    public bool MachineFixedFlg { get; set; } = false;
    [MaxLength(4)] public string? CpDeliveryDiv { get; set; }

    // ───── 工程仕様 01〜10 ─────
    [MaxLength(50)] public string? Spec01 { get; set; }
    [MaxLength(50)] public string? Spec02 { get; set; }
    [MaxLength(50)] public string? Spec03 { get; set; }
    [MaxLength(50)] public string? Spec04 { get; set; }
    [MaxLength(50)] public string? Spec05 { get; set; }
    [MaxLength(50)] public string? Spec06 { get; set; }
    [MaxLength(50)] public string? Spec07 { get; set; }
    [MaxLength(50)] public string? Spec08 { get; set; }
    [MaxLength(50)] public string? Spec09 { get; set; }
    [MaxLength(50)] public string? Spec10 { get; set; }

    [MaxLength(4)] public string? QtyUnit { get; set; }

    // ───── 版型・消耗品 ─────
    [MaxLength(20)] public string? PlateNo1 { get; set; }
    [MaxLength(20)] public string? PlateNo2 { get; set; }
    [MaxLength(20)] public string? PlateNo3 { get; set; }
    [MaxLength(20)] public string? Consumable1 { get; set; }
    [MaxLength(20)] public string? Consumable2 { get; set; }
    [MaxLength(20)] public string? Consumable3 { get; set; }

    // ───── 単価・ロス・LT ─────
    [Column(TypeName = "decimal(21,8)")] public decimal? PurchaseUnitPrice { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? FixedPrice { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal LossRate { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal MachineCount { get; set; }
    public int LeadTimeDays { get; set; }
    [MaxLength(20)] public string? StorageLocation { get; set; }

    /// <summary>並び順（システム自動採番）</summary>
    public int SortOrder { get; set; }

    // ───── 製造順優先項目 1〜8 ─────
    [MaxLength(20)] public string? PriorityItem1 { get; set; }
    [MaxLength(20)] public string? PriorityItem2 { get; set; }
    [MaxLength(20)] public string? PriorityItem3 { get; set; }
    [MaxLength(20)] public string? PriorityItem4 { get; set; }
    [MaxLength(20)] public string? PriorityItem5 { get; set; }
    [MaxLength(20)] public string? PriorityItem6 { get; set; }
    [MaxLength(20)] public string? PriorityItem7 { get; set; }
    [MaxLength(20)] public string? PriorityItem8 { get; set; }

    /// <summary>加工予定日（出荷日時から営業日逆算）</summary>
    public DateTime? ScheduledDate { get; set; }
}
