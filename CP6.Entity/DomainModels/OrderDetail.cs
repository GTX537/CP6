using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 受注明細（MSBBPA070 第2页 基本情報・構成情報・仕入情報・備考）
/// </summary>
/// <remarks>
/// 仕様書 §11.2 T_OrderDetail（約125項目）
/// 業務 PK: (WebOrderNo, WebOrderDetailNo)
/// 受注時に製品基本マスタから大量フィールドをスナップショットコピーする設計
/// </remarks>
[Table("T_OrderDetail")]
public class OrderDetail : BaseBizEntity
{
    // ───── 業務主キー ─────
    /// <summary>Web受注NO (FK→T_Order)</summary>
    [Required, MaxLength(20)]
    public string WebOrderNo { get; set; } = string.Empty;

    /// <summary>Web受注明細NO（明細行連番）</summary>
    public int WebOrderDetailNo { get; set; }

    // ───── 手配NO 1〜4（Rev2.0 仕様） ─────
    /// <summary>手配NO1（シーケンス自動採番）</summary>
    [MaxLength(20)] public string? HaibaiNo1 { get; set; }
    /// <summary>手配NO2（=得意先CD）</summary>
    [MaxLength(20)] public string? HaibaiNo2 { get; set; }
    /// <summary>手配NO3（=案件NO親）</summary>
    [MaxLength(20)] public string? HaibaiNo3 { get; set; }
    /// <summary>手配NO4（NULL固定）</summary>
    [MaxLength(20)] public string? HaibaiNo4 { get; set; }

    // ───── 製品キー（mcframe7 連携） ─────
    /// <summary>製品コード（15桁；製品マスタ参照）</summary>
    [Required, MaxLength(15)]
    public string ProductCd { get; set; } = string.Empty;

    /// <summary>品目コード（mcframe7 連携用、システム自動）</summary>
    [MaxLength(15)] public string? ItemCd { get; set; }
    [MaxLength(10)] public string? Branch1 { get; set; }
    [MaxLength(10)] public string? Branch2 { get; set; }
    [MaxLength(10)] public string? Branch3 { get; set; }

    // ───── 製品区分（自動判定） ─────
    [MaxLength(4)] public string? ProductCatBig { get; set; }
    [MaxLength(4)] public string? ProductCatMid { get; set; }
    [MaxLength(4)] public string? ProductCatSml { get; set; }

    // ───── 顧客・品名 ─────
    [MaxLength(100)] public string? CustomerItemName1 { get; set; }
    [MaxLength(100)] public string? CustomerItemName2 { get; set; }
    [MaxLength(20)] public string? CustomerPartNo { get; set; }
    [MaxLength(100)] public string? CpItemName1 { get; set; }
    [MaxLength(100)] public string? CpItemName2 { get; set; }
    [MaxLength(13)] public string? JanCode { get; set; }

    // ───── 数量・単価 ─────
    [MaxLength(4)] public string? QtyUnit { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? Quantity { get; set; }
    /// <summary>特値FLG：0=通常 / 1=特値</summary>
    [MaxLength(1)] public string? SpecialPriceFlg { get; set; }
    [MaxLength(4)] public string? UnitPriceUnit { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? SetUnitPrice { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? IndividualUnitPrice { get; set; }
    /// <summary>金額 = 数量 × 単価</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal? Amount { get; set; }

    // ───── 納品・物流 ─────
    [MaxLength(20)] public string? DeliveryCd { get; set; }
    [MaxLength(100)] public string? DeliveryName { get; set; }
    public DateTime? CustomerDeliveryDate { get; set; }
    [MaxLength(10)] public string? LogisticsGroup { get; set; }
    /// <summary>手配区分</summary>
    [MaxLength(4)] public string? HaibaiKbn { get; set; }

    // ───── 預り売上（Rev4.0） ─────
    /// <summary>預り売上FLG：0=通常 / 1=預り売上</summary>
    [MaxLength(1)] public string? ConsignedSalesFlg { get; set; }
    [MaxLength(100)] public string? SalesReason { get; set; }
    /// <summary>預り売上数（Rev4 追加）</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal? ConsignedSalesQty { get; set; }

    // ───── FSC・食品安全 ─────
    [MaxLength(4)] public string? FscOrderType { get; set; }
    [MaxLength(4)] public string? FscProductDiv { get; set; }
    [MaxLength(4)] public string? FscMaterialDiv { get; set; }
    [MaxLength(20)] public string? FscManagementNo { get; set; }
    [MaxLength(4)] public string? FoodSafety { get; set; }

    // ───── 数量関連属性 ─────
    [MaxLength(4)] public string? ShipInspection { get; set; }
    [MaxLength(4)] public string? FixedShipment { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? DeliveryReserve { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? SalesSample { get; set; }
    [MaxLength(4)] public string? SalesAvailable { get; set; }

    // ───── 構成情報スナップショット（13フィールド） ─────
    [MaxLength(4)] public string? SheetFlute { get; set; }
    [MaxLength(20)] public string? PaperCdF { get; set; }
    [MaxLength(20)] public string? PaperCdC { get; set; }
    [MaxLength(20)] public string? PaperCdB { get; set; }
    [MaxLength(20)] public string? PrintCdF { get; set; }
    [MaxLength(20)] public string? PrintCdC { get; set; }
    [MaxLength(20)] public string? PrintCdB { get; set; }
    [MaxLength(20)] public string? EmbossCdF { get; set; }
    [MaxLength(20)] public string? EmbossCdC { get; set; }
    [MaxLength(20)] public string? EmbossCdB { get; set; }
    [MaxLength(20)] public string? MakerCdF { get; set; }
    [MaxLength(20)] public string? MakerCdC { get; set; }
    [MaxLength(20)] public string? MakerCdB { get; set; }

    // ───── 寸法スナップショット ─────
    [MaxLength(20)] public string? SheetPrint { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? BladeWidth { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? BladeFlow { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? GutterFb { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? GutterLr { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? SheetDimW { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? SheetDimF { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? SalesWidth { get; set; }
    [MaxLength(20)] public string? FinalMachineProcess { get; set; }

    // ───── 備考（7種） ─────
    [MaxLength(100)] public string? PrintNote { get; set; }
    [MaxLength(100)] public string? MfgNote { get; set; }
    [MaxLength(100)] public string? RemfgNote { get; set; }
    [MaxLength(100)] public string? SlipNote { get; set; }
    [MaxLength(100)] public string? DeliveryNote { get; set; }
    [MaxLength(100)] public string? ShipNote1 { get; set; }
    [MaxLength(100)] public string? ShipNote2 { get; set; }

    // ───── 仕入情報 ─────
    [MaxLength(20)] public string? DefectiveHaibaiNo { get; set; }
    [MaxLength(20)] public string? PurchaseVendor { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? RollMeter { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? PurchaseUnitPrice { get; set; }
    [MaxLength(4)] public string? PurchaseUnit { get; set; }

    // ───── 案件・見積連携 ─────
    [MaxLength(15)] public string? ProjectNoParent { get; set; }
    [MaxLength(15)] public string? ProjectNoChild { get; set; }
    [MaxLength(15)] public string? ProjectNoMaterial { get; set; }
    [MaxLength(11)] public string? QuotationNo { get; set; }
    [MaxLength(11)] public string? EstimateCalcNo { get; set; }
    [MaxLength(11)] public string? RefEstimateCalcNo { get; set; }

    // ───── セット品管理 ─────
    [MaxLength(15)] public string? SetProductCd { get; set; }
    [MaxLength(100)] public string? SetProductName { get; set; }
    /// <summary>親子区分：0=親 / 1=子</summary>
    [MaxLength(1)] public string? ParentChildDiv { get; set; }
    [Column(TypeName = "decimal(18,8)")] public decimal? SetRatio { get; set; }

    /// <summary>受注区分（明細レベル；ヘッダーから引継ぎ）</summary>
    [MaxLength(4)] public string? OrderType { get; set; }

    // ───── 製品属性スナップショット ─────
    [MaxLength(4)] public string? ProductUsage { get; set; }
    [MaxLength(4)] public string? DistributionDiv { get; set; }
    [MaxLength(4)] public string? ConfidentialInfo { get; set; }
    [MaxLength(4)] public string? SeizureDiv { get; set; }
    [MaxLength(4)] public string? ImportanceDiv { get; set; }
    [MaxLength(4)] public string? MChange { get; set; }
    [MaxLength(4)] public string? QualityDiv { get; set; }
    [MaxLength(4)] public string? ProductShape { get; set; }
    [MaxLength(4)] public string? UnescoMark { get; set; }
    [MaxLength(4)] public string? OrigamiMark { get; set; }
    [MaxLength(4)] public string? FourMContract { get; set; }
    [MaxLength(4)] public string? TkpWrinkleStd { get; set; }
    [MaxLength(2)] public string? RecyclingPayment { get; set; }

    // ───── 容リ法 使用量（g） ─────
    [Column(TypeName = "decimal(21,8)")] public decimal? PaperUsageG { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? PlasticUsageG { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? GlassUsageG { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? PetUsageG { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? PackPaperUsageG { get; set; }
    [Column(TypeName = "decimal(21,8)")] public decimal? PackPlasticUsageG { get; set; }

    [MaxLength(11)] public string? DesignProposalNo { get; set; }
    [MaxLength(1)] public string? SalesPriceDiv { get; set; }
    [MaxLength(1)] public string? FreightBilling { get; set; }

    // ───── mcframe7 連携キー ─────
    /// <summary>受注NO（mc側キー；新規=NULL）</summary>
    [MaxLength(20)] public string? McOrderNo { get; set; }
    /// <summary>受注明細NO（mc側キー；新規=NULL）</summary>
    [MaxLength(20)] public string? McOrderDetailNo { get; set; }

    // ───── ステータス・連携FLG ─────
    /// <summary>ステータス：0=未転送 / 9=mc転送済</summary>
    public int Status { get; set; } = 0;
    /// <summary>WF承認FLG（訂正で単価変更時=0にリセット）</summary>
    public bool WfApprovalFlg { get; set; } = false;
    public bool McTransferFlg { get; set; } = false;

    // ───── PA090 単価訂正用 ─────
    /// <summary>仮単価FLG（true=仮単価/false=本単価）</summary>
    public bool ProvisionalPriceFlg { get; set; } = false;
    /// <summary>単価変更理由</summary>
    [MaxLength(200)] public string? PriceChangeReason { get; set; }
    /// <summary>承認状況（PA090 単価訂正用：0=未/1=承認依頼中/9=承認済）</summary>
    public int? ApprovalStatus { get; set; }

    // ───── ナビゲーション ─────
    public Order? Order { get; set; }
    public List<OrderProcess> Processes { get; set; } = new();
    public List<OrderProcessNote> ProcessNotes { get; set; } = new();
    public List<OrderMaterial> Materials { get; set; } = new();
}
