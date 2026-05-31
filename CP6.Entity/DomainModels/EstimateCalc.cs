using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 見積計算書（报价计算书）主表 - MSBBPA010
/// </summary>
/// <remarks>
/// 对应需求 §7.1 T_EstimateCalc
/// Phase 1 先实现 3 页 MVP 所必需的核心字段（~55 个）
/// 战略商品区分 03-10、容利法判定明细、決定見積補足等次要字段后续 Phase 再补
/// </remarks>
[Table("T_EstimateCalc")]
public class EstimateCalc : BaseBizEntity
{
    // ───── 业务主键 ─────
    /// <summary>見積計算書NO（XXXXXXXX-NN，11 字符：8 位主番 + '-' + 2 位枝番）</summary>
    [Required]
    [MaxLength(11)]
    public string QtnCalcNo { get; set; } = string.Empty;

    /// <summary>主番（NUMERIC 8）</summary>
    public int QtnCalcNoMain { get; set; }

    /// <summary>枝番（NUMERIC 2）</summary>
    public int QtnCalcNoBranch { get; set; }

    /// <summary>コピー来源 NO</summary>
    [MaxLength(11)]
    public string? RefQtnCalcNo { get; set; }

    /// <summary>製品 CD（Rev4 扩展至 15 位）</summary>
    [MaxLength(15)]
    public string? ProCd { get; set; }

    // ───── 基本信息区 ─────
    /// <summary>見積日</summary>
    public DateTime QtnDate { get; set; } = DateTime.Today;

    /// <summary>見積拠点 CD</summary>
    [Required, MaxLength(10)]
    public string QtnBaseCd { get; set; } = string.Empty;

    /// <summary>受注拠点 CD</summary>
    [Required, MaxLength(10)]
    public string OrderBaseCd { get; set; } = string.Empty;

    /// <summary>担当者 CD</summary>
    [Required, MaxLength(10)]
    public string StaffCd { get; set; } = string.Empty;

    /// <summary>得意先 CD</summary>
    [MaxLength(20)]
    public string? CustomerCd { get; set; }

    /// <summary>案件 NO（親）</summary>
    [MaxLength(15)]
    public string? ProjectNoParent { get; set; }

    /// <summary>案件 NO（子）</summary>
    [MaxLength(15)]
    public string? ProjectNoChild { get; set; }

    /// <summary>案件 NO（部材）</summary>
    [MaxLength(15)]
    public string? ProjectNoMaterial { get; set; }

    /// <summary>受注区分（汎用マスタ）</summary>
    [MaxLength(4)]
    public string? OrderType { get; set; }

    /// <summary>製品区分（大）</summary>
    [MaxLength(4)]
    public string? ProductCategoryBig { get; set; }

    /// <summary>製品区分（中）- 第 3 页填</summary>
    [MaxLength(4)]
    public string? ProductCategoryMid { get; set; }

    /// <summary>製品区分（小）- 第 3 页填（小分類码为 5 字符如 A0101）</summary>
    [MaxLength(6)]
    public string? ProductCategorySml { get; set; }

    /// <summary>顧客品名1（画面 32、DB 100）</summary>
    [MaxLength(100)]
    public string? CustomerProductName1 { get; set; }

    /// <summary>顧客品名2</summary>
    [MaxLength(100)]
    public string? CustomerProductName2 { get; set; }

    /// <summary>受注総数</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? OrderQty { get; set; }

    /// <summary>受注年月（YYYYMM）</summary>
    [MaxLength(6)]
    public string? OrderYm { get; set; }

    /// <summary>親子区分</summary>
    [MaxLength(2)]
    public string? ParentChildDiv { get; set; }

    /// <summary>FSC 製品区分（≥1 时触发原紙 FSC 校验）</summary>
    [MaxLength(4)]
    public string? FscProductDiv { get; set; }

    /// <summary>FSC 材質区分</summary>
    [MaxLength(4)]
    public string? FscMaterialDiv { get; set; }

    // ───── 构成信息区（纸板 3 层） ─────
    /// <summary>シート段（E/B/C/BC/EB/BE）</summary>
    [MaxLength(4)]
    public string? SheetFlute { get; set; }

    [MaxLength(10)] public string? PaperCdF { get; set; }
    [MaxLength(10)] public string? PrintCdF { get; set; }
    [MaxLength(10)] public string? EmbossCdF { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? PatternCntF { get; set; }

    [MaxLength(10)] public string? PaperCdC { get; set; }
    [MaxLength(10)] public string? PrintCdC { get; set; }
    [MaxLength(10)] public string? EmbossCdC { get; set; }

    [MaxLength(10)] public string? PaperCdB { get; set; }
    [MaxLength(10)] public string? PrintCdB { get; set; }
    [MaxLength(10)] public string? EmbossCdB { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? PatternCntB { get; set; }

    /// <summary>枚葉印刷</summary>
    [MaxLength(4)]
    public string? SheetPrint { get; set; }

    [Column(TypeName = "decimal(10,2)")] public decimal? BladeWidth { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? BladeFlow { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? GutterFb { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? GutterLr { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? SheetDimW { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? SheetDimF { get; set; }

    /// <summary>最終機械工程</summary>
    [MaxLength(4)]
    public string? FinalMachineProc { get; set; }

    /// <summary>製品形状1（計上分類）</summary>
    [MaxLength(4)]
    public string? ProductShape1 { get; set; }

    /// <summary>製品形状2（計上区分）</summary>
    [MaxLength(4)]
    public string? ProductShape2 { get; set; }

    /// <summary>物流区分</summary>
    [MaxLength(4)]
    public string? DistDiv { get; set; }

    /// <summary>容リ支払（0=非该当，1=该当，自动设置）</summary>
    [MaxLength(1)]
    public string? RecyclePayment { get; set; } = "0";

    /// <summary>識別表示（容利法弹窗自动设置）</summary>
    [MaxLength(4)]
    public string? IdMark { get; set; }

    /// <summary>AD（形状）</summary>
    [MaxLength(4)]
    public string? AdShape { get; set; }

    // ───── 战略商品区分（10 个 bit） ─────
    public bool StrategicDiv01 { get; set; } // FSC
    public bool StrategicDiv02 { get; set; } // TKP
    public bool StrategicDiv03 { get; set; } // スマパピ
    public bool StrategicDiv04 { get; set; } // バリット
    public bool StrategicDiv05 { get; set; } // マイクロ
    public bool StrategicDiv06 { get; set; } // カラフルウィッシュ
    public bool StrategicDiv07 { get; set; }
    public bool StrategicDiv08 { get; set; }
    public bool StrategicDiv09 { get; set; }
    public bool StrategicDiv10 { get; set; }

    // ───── 备注区（各 32 字符） ─────
    [MaxLength(32)] public string? PrintNote { get; set; }
    [MaxLength(32)] public string? MfgNote { get; set; }
    [MaxLength(32)] public string? SlipNote { get; set; }
    [MaxLength(32)] public string? DeliveryNote { get; set; }
    [MaxLength(32)] public string? ShipNote1 { get; set; }
    [MaxLength(32)] public string? ShipNote2 { get; set; }

    // ───── 报价数量 ─────
    [Column(TypeName = "decimal(10,2)")] public decimal? EstimateQty01 { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? EstimateQty02 { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? EstimateQty03 { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? EstimateQty04 { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? EstimateQty05 { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? EstimateQty06 { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? EstimateQty07 { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? EstimateQty08 { get; set; }

    [Column(TypeName = "decimal(10,2)")] public decimal? ProposalLot1 { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? ProposalLot2 { get; set; }

    [MaxLength(4)] public string? Unit { get; set; }

    /// <summary>決定予想見積り数（选中的是哪一个 01-08）</summary>
    [Column(TypeName = "decimal(10,2)")] public decimal? DecidedQty { get; set; }

    [Column(TypeName = "decimal(10,2)")] public decimal? PalletCnt01 { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? PalletCnt02 { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? PalletCnt03 { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? PalletCnt04 { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? PalletCnt05 { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? PalletCnt06 { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? PalletCnt07 { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? PalletCnt08 { get; set; }

    // ───── 計算結果（第 3 页） ─────
    /// <summary>見積区分：10=通常見積，20=決定見積</summary>
    [MaxLength(2)]
    public string? QtnDiv { get; set; }

    [Column(TypeName = "decimal(12,4)")] public decimal? EstimateSqm { get; set; }
    [Column(TypeName = "decimal(15,4)")] public decimal? StandardUnitPrice { get; set; }
    [Column(TypeName = "decimal(15,4)")] public decimal? EstimateUnitPrice { get; set; }
    [Column(TypeName = "decimal(15,4)")] public decimal? ConfirmedUnitPrice { get; set; }

    // ───── PA100 FSC 関連（仕様書 §5） ─────
    /// <summary>FSC 管理 NO（PA100 で発行時に採番）</summary>
    [MaxLength(20)] public string? FscManagementNo { get; set; }

    /// <summary>合計金額（見積金額の合計）</summary>
    [Column(TypeName = "decimal(15,4)")] public decimal? TotalAmount { get; set; }

    /// <summary>ステータス：0=未確定 / 1=確定 / 9=削除</summary>
    public int Status { get; set; } = 0;

    // ───── 导航属性 ─────
    /// <summary>工程明细（级联）</summary>
    public List<EstimateCalcProcess> Processes { get; set; } = new();
}
