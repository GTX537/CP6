using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Erp;

/// <summary>
/// 製品基本マスタ（MSBBPA050 第2页基本情報）— Web 製品マスタ主表
/// </summary>
/// <remarks>
/// 仕様書 §14.1 T_ProductMaster
/// 業務 PK：製品コード（VARCHAR 15）= 品目CD(連番) + 枝番1(行番) + 枝番2(MCNULLVAL) + 枝番3(MCNULLVAL)
/// 採番ルール（Rev.5）：登録時 BRANCH1 = "00" + 行番、BRANCH2/3 = "MCNULLVAL"
/// </remarks>
[Table("T_ProductMaster")]
public class ProductMaster : BaseBizEntity, IAuditable
{
    // ───── 業務主キー ─────
    /// <summary>製品コード（機能コード+年月+自増13桁 + 枝番4桁=17桁、業務 PK、自動採番）</summary>
    [Required, MaxLength(20)]
    public string ProductCd { get; set; } = string.Empty;

    /// <summary>品目コード（mcframe7 連携用：=製品コード連番部分）</summary>
    [Required, MaxLength(15)]
    public string ItemCd { get; set; } = string.Empty;

    /// <summary>枝番1（部材行番、登録="00"+行番）</summary>
    [Required, MaxLength(10)]
    public string Branch1 { get; set; } = "00";

    /// <summary>枝番2（MCNULLVAL 固定）</summary>
    [Required, MaxLength(10)]
    public string Branch2 { get; set; } = "MCNULLVAL";

    /// <summary>枝番3（MCNULLVAL 固定）</summary>
    [Required, MaxLength(10)]
    public string Branch3 { get; set; } = "MCNULLVAL";

    // ───── 案件・参照情報 ─────
    [MaxLength(15)] public string? ProjectNoParent { get; set; }
    [MaxLength(15)] public string? ProjectNoChild { get; set; }
    [MaxLength(15)] public string? ProjectNoMaterial { get; set; }

    /// <summary>御見積書NO</summary>
    [MaxLength(20)] public string? QuotationNo { get; set; }

    /// <summary>見積計算書NO</summary>
    [MaxLength(20)] public string? EstimateCalcNo { get; set; }

    /// <summary>参照元_見積計算書NO（コピー元）</summary>
    [MaxLength(20)] public string? RefEstimateCalcNo { get; set; }

    // ───── 顧客・セット情報 ─────
    [Required, MaxLength(20)]
    public string CustomerCd { get; set; } = string.Empty;

    /// <summary>セット製品CD（部材全行共通のセットCD）</summary>
    [Required, MaxLength(20)]
    public string SetProductCd { get; set; } = string.Empty;

    /// <summary>セット品名</summary>
    [MaxLength(100)] public string? SetProductName { get; set; }

    /// <summary>親子区分：0=親 / 1=子</summary>
    [Required, MaxLength(1)]
    public string ParentChildDiv { get; set; } = "0";

    // ───── 品名・品番 ─────
    [MaxLength(100)] public string? CustomerItemName1 { get; set; }
    [MaxLength(100)] public string? CustomerItemName2 { get; set; }
    [MaxLength(20)] public string? CustomerPartNo { get; set; }
    [MaxLength(100)] public string? CpItemName1 { get; set; }
    [MaxLength(100)] public string? CpItemName2 { get; set; }
    [MaxLength(13)] public string? JanCode { get; set; }

    /// <summary>セット比率（0 不可）</summary>
    [Column(TypeName = "decimal(18,8)")]
    public decimal SetRatio { get; set; } = 1m;

    // ───── 用途・物流・機密 ─────
    [MaxLength(4)] public string? ProductUsage { get; set; }
    [MaxLength(4)] public string? DistributionDiv { get; set; }
    [MaxLength(4)] public string? ConfidentialInfo { get; set; }
    [MaxLength(4)] public string? SeizureDiv { get; set; }
    [MaxLength(4)] public string? ImportanceDiv { get; set; }
    [MaxLength(4)] public string? MChange { get; set; }
    [MaxLength(4)] public string? QualityDiv { get; set; }
    [MaxLength(4)] public string? ShipInspection { get; set; }
    [MaxLength(4)] public string? ProductShape { get; set; }
    [MaxLength(4)] public string? UnescoMark { get; set; }
    [MaxLength(4)] public string? OrigamiMark { get; set; }
    [MaxLength(4)] public string? FourMContract { get; set; }
    [MaxLength(4)] public string? TkpWrinkleStd { get; set; }
    [MaxLength(4)] public string? FoodSafety { get; set; }

    /// <summary>AD（形状）— Rev.5 追加</summary>
    [MaxLength(4)] public string? AdShape { get; set; }

    // ───── 戦略商品区分 01〜10（Rev.5 追加） ─────
    [MaxLength(1)] public string? StrategicDiv01 { get; set; }
    [MaxLength(1)] public string? StrategicDiv02 { get; set; }
    [MaxLength(1)] public string? StrategicDiv03 { get; set; }
    [MaxLength(1)] public string? StrategicDiv04 { get; set; }
    [MaxLength(1)] public string? StrategicDiv05 { get; set; }
    [MaxLength(1)] public string? StrategicDiv06 { get; set; }
    [MaxLength(1)] public string? StrategicDiv07 { get; set; }
    [MaxLength(1)] public string? StrategicDiv08 { get; set; }
    [MaxLength(1)] public string? StrategicDiv09 { get; set; }
    [MaxLength(1)] public string? StrategicDiv10 { get; set; }

    // ───── FSC 関連 ─────
    [MaxLength(4)] public string? FscProductDiv { get; set; }
    [MaxLength(4)] public string? FscMaterialDiv { get; set; }
    [MaxLength(20)] public string? FscManagementNo { get; set; }

    // ───── 容リ法・識別表示 ─────
    [MaxLength(2)] public string? RecyclingPayment { get; set; }
    [MaxLength(2)] public string? IdMark { get; set; }

    // ───── 容リ法 使用量（g） ─────
    [Column(TypeName = "decimal(21,8)")] public decimal? PaperUsageG { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? PlasticUsageG { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? GlassUsageG { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? PetUsageG { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? PackPaperUsageG { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? PackPlasticUsageG { get; set; }

    // ───── デザイン・受注 ─────
    [MaxLength(11)] public string? DesignProposalNo { get; set; }
    [MaxLength(4)] public string? OrderType { get; set; }

    // ───── 段ボール・原紙構成 ─────
    [MaxLength(4)] public string? SheetFlute { get; set; }

    [MaxLength(20)] public string? PaperCdF { get; set; }
    [MaxLength(20)] public string? PrintCdF { get; set; }
    [MaxLength(20)] public string? EmbossCdF { get; set; }
    [MaxLength(20)] public string? MakerCdF { get; set; }

    [MaxLength(20)] public string? PaperCdC { get; set; }
    [MaxLength(20)] public string? PrintCdC { get; set; }
    [MaxLength(20)] public string? EmbossCdC { get; set; }

    [MaxLength(20)] public string? PaperCdB { get; set; }
    [MaxLength(20)] public string? PrintCdB { get; set; }
    [MaxLength(20)] public string? EmbossCdB { get; set; }
    [MaxLength(20)] public string? MakerCdB { get; set; }

    /// <summary>枚葉印刷（製品区分=31時 自動="オフセット"）</summary>
    [MaxLength(20)] public string? SheetPrint { get; set; }

    // ───── 寸法（mm） ─────
    [Column(TypeName = "decimal(21,8)")] public decimal? BladeWidth { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? BladeFlow { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? GutterFb { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? GutterLr { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? SheetDimW { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? SheetDimF { get; set; }

    /// <summary>最終機械工程</summary>
    [MaxLength(20)] public string? FinalMachineProcess { get; set; }

    // ───── 備考 ─────
    [MaxLength(100)] public string? PrintNote { get; set; }
    [MaxLength(100)] public string? MfgNote { get; set; }
    [MaxLength(100)] public string? SlipNote { get; set; }
    [MaxLength(100)] public string? DeliveryNote { get; set; }
    [MaxLength(100)] public string? ShipNote1 { get; set; }
    [MaxLength(100)] public string? ShipNote2 { get; set; }

    // ───── 製品区分・売価 ─────
    /// <summary>製品区分_大（工程により自動判定）</summary>
    [MaxLength(4)] public string? ProductCatBig { get; set; }
    [MaxLength(4)] public string? ProductCatMid { get; set; }
    [MaxLength(6)] public string? ProductCatSml { get; set; }

    /// <summary>売価区分：1=ｾｯﾄｳﾘ / 2=ﾀﾝﾋﾟﾝｳﾘ / 3=ｱｿｰﾄ</summary>
    [Required, MaxLength(1)]
    public string SalesPriceDiv { get; set; } = "2";

    /// <summary>数量単位（受注後変更不可）</summary>
    [MaxLength(4)] public string? QtyUnit { get; set; }

    /// <summary>単価単位（受注後変更不可）</summary>
    [MaxLength(4)] public string? UnitPriceUnit { get; set; }

    /// <summary>購買品の発注先（商品のみ）</summary>
    [MaxLength(20)] public string? PurchaseVendor { get; set; }

    /// <summary>運賃請求：0=込み / 1=別途</summary>
    [Required, MaxLength(1)]
    public string FreightBilling { get; set; } = "0";

    [MaxLength(4)] public string? FixedShipment { get; set; }

    [Column(TypeName = "decimal(21,8)")] public decimal? DeliveryReserve { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? SalesSample { get; set; }

    // ───── ステータス・連携FLG ─────
    /// <summary>ステータス：0=承認待ち / 1=承認済み / 9=mcframe7 転送済</summary>
    public int Status { get; set; } = 0;

    /// <summary>ワークフロー承認FLG</summary>
    public bool WfApprovalFlg { get; set; } = false;

    /// <summary>mcframe7 転送FLG（mcframe7 が無い環境では常に false）</summary>
    public bool McTransferFlg { get; set; } = false;

    // ───── ナビゲーションプロパティ ─────
    public List<ProductProcess> Processes { get; set; } = new();
    public List<ProductMaterial> Materials { get; set; } = new();
    public List<ProductLotPrice> LotPrices { get; set; } = new();
    public List<ProductCoProduct> CoProducts { get; set; } = new();
}
