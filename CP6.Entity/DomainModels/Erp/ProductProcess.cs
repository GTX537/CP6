using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Erp;

/// <summary>
/// 製品加工工程マスタ（MSBBPA050 第3页工程情報）
/// </summary>
/// <remarks>
/// 仕様書 §14.2 T_ProductProcess
/// 業務複合 PK：製品コード + 作業コード（TaskCd）
/// </remarks>
[Table("T_ProductProcess")]
public class ProductProcess : BaseBizEntity, IAuditable
{
    /// <summary>製品コード（FK → T_ProductMaster.ProductCd）</summary>
    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    /// <summary>作業コード（業務 PK2、mcframe7 の工程コードに相当）</summary>
    [Required, MaxLength(10)]
    public string TaskCd { get; set; } = string.Empty;

    /// <summary>工程コード（mcframe7 の品目コードに相当）</summary>
    [Required, MaxLength(10)]
    public string ProcessCd { get; set; } = string.Empty;

    // ───── mcframe7 連携用 TOP 品目 ─────
    [MaxLength(15)] public string? TopItemCd { get; set; }
    [MaxLength(10)] public string? TopBranch1 { get; set; }
    [MaxLength(10)] public string? TopBranch2 { get; set; }
    [MaxLength(10)] public string? TopBranch3 { get; set; }

    // ───── 工程の品目 ─────
    [MaxLength(15)] public string? ItemCd { get; set; }
    [MaxLength(10)] public string? Branch1 { get; set; }
    [MaxLength(10)] public string? Branch2 { get; set; }
    [MaxLength(10)] public string? Branch3 { get; set; }

    /// <summary>ワーキンググループCD</summary>
    [MaxLength(10)] public string? WgCd { get; set; }

    /// <summary>号機・発注先（作業場所）</summary>
    [MaxLength(20)] public string? MachineOrVendor { get; set; }

    /// <summary>号機指定FLG</summary>
    public bool MachineFixedFlg { get; set; } = false;

    /// <summary>CP配送区分（引取出荷/移送/売上出荷/WG間依頼）</summary>
    [MaxLength(4)] public string? CpDeliveryDiv { get; set; }

    // ───── 工程仕様 01〜10 ─────
    [MaxLength(20)] public string? Spec01 { get; set; }
    [MaxLength(20)] public string? Spec02 { get; set; }
    [MaxLength(20)] public string? Spec03 { get; set; }
    [MaxLength(20)] public string? Spec04 { get; set; }
    [MaxLength(20)] public string? Spec05 { get; set; }
    [MaxLength(20)] public string? Spec06 { get; set; }
    [MaxLength(20)] public string? Spec07 { get; set; }
    [MaxLength(20)] public string? Spec08 { get; set; }
    [MaxLength(20)] public string? Spec09 { get; set; }
    [MaxLength(20)] public string? Spec10 { get; set; }

    // ───── 版型・消耗品 ─────
    [MaxLength(20)] public string? PlateNo1 { get; set; }
    [MaxLength(20)] public string? PlateNo2 { get; set; }
    [MaxLength(20)] public string? PlateNo3 { get; set; }
    [MaxLength(32)] public string? Consumable1 { get; set; }
    [MaxLength(32)] public string? Consumable2 { get; set; }
    [MaxLength(32)] public string? Consumable3 { get; set; }

    // ───── 単価・LT ─────
    /// <summary>仕入単価（外注・購買工程）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? PurchasePrice { get; set; }

    /// <summary>指値（本支店売買）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? FixedPrice { get; set; }

    /// <summary>ロス率</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? LossRate { get; set; }

    /// <summary>台数（固定使用数量）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? MachineCount { get; set; }

    /// <summary>製造リードタイム（日）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? LeadTime { get; set; }

    // ───── A2 标准工时（spec §3.1）─────
    /// <summary>段取工时（h，固定/批；与数量无关）。标准机时 = SetupHour + 数量 × CycleTime。</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal? SetupHour { get; set; }
    /// <summary>单件加工工时（h/件）。</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal? CycleTime { get; set; }
    /// <summary>标准作业人数（标准人工工时 = 标准机时 × 人数；空按 1）。</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal? StandardCrewSize { get; set; }

    [MaxLength(100)] public string? ProcessNote1 { get; set; }
    [MaxLength(100)] public string? ProcessNote2 { get; set; }

    /// <summary>入庫先</summary>
    [MaxLength(20)] public string? StorageDest { get; set; }

    /// <summary>並び順（画面表示順、システム自動採番）</summary>
    public int SortOrder { get; set; }

    // ───── 製造順優先項目 1〜8（生産並び順計算結果、第11章参照） ─────
    [MaxLength(20)] public string? ManufOrderPrio1 { get; set; }
    [MaxLength(20)] public string? ManufOrderPrio2 { get; set; }
    [MaxLength(20)] public string? ManufOrderPrio3 { get; set; }
    [MaxLength(20)] public string? ManufOrderPrio4 { get; set; }
    [MaxLength(20)] public string? ManufOrderPrio5 { get; set; }
    [MaxLength(20)] public string? ManufOrderPrio6 { get; set; }
    [MaxLength(20)] public string? ManufOrderPrio7 { get; set; }
    [MaxLength(20)] public string? ManufOrderPrio8 { get; set; }
}
