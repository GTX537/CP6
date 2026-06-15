using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Erp;

/// <summary>
/// Web取引先マスタ（MSBBPA110/120）— システム最複雑マスタ
/// </summary>
/// <remarks>
/// 仕様書 §9 T_WebBusinessPartner（170+ フィールド）
/// 業務 PK：BpCd（取引先CD）
/// 1 取引先 ⇄ 9 種類の属性 FLG（得意先/売掛先/請求先/入金先/納品先/発注先/買掛先/支払予定管理先/支払先）
/// </remarks>
[Table("T_WebBusinessPartner")]
public class BusinessPartner : BaseBizEntity
{
    // ───── 業務 PK ─────
    [Required, MaxLength(20)]
    public string BpCd { get; set; } = string.Empty;

    /// <summary>取引先名（mc: 取引先別名1）</summary>
    [Required, MaxLength(100)]
    public string BpName { get; set; } = string.Empty;

    /// <summary>取引先略称（mc: 取引先別名1）</summary>
    [MaxLength(10)]
    public string? BpAbbrev { get; set; }

    /// <summary>拠点 CD</summary>
    [Required, MaxLength(10)]
    public string BaseCd { get; set; } = string.Empty;

    /// <summary>取引先ステータス：0=事前登録 / 1=本登録 / 9=削除</summary>
    public int Status { get; set; } = 1;

    /// <summary>取引通貨CD（多通貨 Gap 4.3。null/JPY = 基軸通貨。受注時にこの通貨の当日レートを凍結）</summary>
    [MaxLength(3)] public string? CurrencyCd { get; set; }

    /// <summary>信用额度（本位币，AR 信用控制 F2 章04 §3。null/0=不控）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal? CreditLimit { get; set; }

    // ───── 法人 / 公的番号 ─────
    [MaxLength(20)] public string? StdCoCd { get; set; }
    [MaxLength(13)] public string? Ein { get; set; }
    [MaxLength(2)]  public string? EinType { get; set; }
    [MaxLength(5)]  public string? LocalPublicCd { get; set; }
    [MaxLength(9)]  public string? DenzaiNo { get; set; }

    // ───── 住所・連絡先 ─────
    [MaxLength(10)] public string? ZipCd { get; set; }
    [MaxLength(40)] public string? Addr1 { get; set; }
    [MaxLength(40)] public string? Addr2 { get; set; }
    [MaxLength(40)] public string? Addr3 { get; set; }
    [MaxLength(40)] public string? Addr4 { get; set; }
    [MaxLength(30)] public string? Tel { get; set; }
    [MaxLength(30)] public string? Fax { get; set; }

    [MaxLength(3)]  public string? AreaCd { get; set; }

    /// <summary>営業担当（得意先 FLG=ON 時必須）</summary>
    [MaxLength(20)] public string? SalesStaffCd { get; set; }
    /// <summary>業務担当（得意先 FLG=ON 時必須）</summary>
    [MaxLength(20)] public string? BusinessStaffCd { get; set; }

    // ───── 9 個の属性 FLG（タブ表示制御） ─────
    public bool CustomerFlg { get; set; }
    public bool AccountsReceivableFlg { get; set; }
    public bool BillingFlg { get; set; }
    public bool ReceiptFlg { get; set; }
    public bool DeliveryFlg { get; set; }
    public bool SupplierFlg { get; set; }
    public bool AccountsPayableFlg { get; set; }
    public bool PaymentScheduleFlg { get; set; }
    public bool PaymentFlg { get; set; }

    /// <summary>与信管理先 FLG（仕様書 §4 — 訂正時変更不可対象）</summary>
    public bool CreditMgmtFlg { get; set; }
    /// <summary>メーカー FLG</summary>
    public bool MakerFlg { get; set; }
    /// <summary>有償支給先 FLG（発注先内属性）</summary>
    public bool PaidSupplyFlg { get; set; }
    /// <summary>買戻義務 FLG</summary>
    public bool RebuyObligationFlg { get; set; }

    // ───── 取引先分類 1〜10 + 販売分析 1〜3 ─────
    [MaxLength(10)] public string? BpClass01 { get; set; }
    [MaxLength(10)] public string? BpClass02 { get; set; }
    [MaxLength(10)] public string? BpClass03 { get; set; }
    [MaxLength(10)] public string? BpClass04 { get; set; }
    [MaxLength(10)] public string? BpClass05 { get; set; }
    [MaxLength(10)] public string? BpClass06 { get; set; }
    [MaxLength(10)] public string? BpClass07 { get; set; }
    [MaxLength(10)] public string? BpClass08 { get; set; }
    [MaxLength(10)] public string? BpClass09 { get; set; }
    [MaxLength(10)] public string? BpClass10 { get; set; }
    [MaxLength(10)] public string? SalesAnalysis1 { get; set; }
    [MaxLength(10)] public string? SalesAnalysis2 { get; set; }
    [MaxLength(10)] public string? SalesAnalysis3 { get; set; }

    // ═══════════════════════════════════════════════════════════
    //  得意先 Tab（CustomerFlg=ON 時表示） — §6.1
    // ═══════════════════════════════════════════════════════════
    [MaxLength(20)] public string? ParentCustomerCd { get; set; }
    [MaxLength(4)]  public string? UserConverterDiv { get; set; }
    [MaxLength(20)] public string? AccountsReceivableCd { get; set; }
    [MaxLength(20)] public string? CreditMgmtCd { get; set; }
    [MaxLength(50)] public string? CustomerDept { get; set; }
    [MaxLength(50)] public string? CustomerContact { get; set; }
    [MaxLength(20)] public string? CustomerContactTitle { get; set; }
    [MaxLength(4)]  public string? RecyclingTarget { get; set; }

    // 得意先(売上計算) — 19 項目
    [MaxLength(4)]  public string? SalesPostingDiv { get; set; }
    [MaxLength(4)]  public string? SheetSalesCalcMethod { get; set; }
    [MaxLength(4)]  public string? SalesCalcDivM2 { get; set; }
    [MaxLength(4)]  public string? SalesCalcDivPiece { get; set; }
    [MaxLength(4)]  public string? FractionCalcDiv { get; set; }
    [MaxLength(4)]  public string? FullSheetSalesDiv { get; set; }
    [MaxLength(4)]  public string? SlitterBillingDiv { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? SlitterBillingUnitPrice { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? SlitterMaxFlow { get; set; }
    [MaxLength(4)]  public string? PrintMinBilling { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? PrintMinBillingBelow { get; set; }
    [MaxLength(4)]  public string? PrintMinBillingUnit { get; set; }   // 1=㎡ / 8=m
    [MaxLength(4)]  public string? LaminateMinBilling { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? LaminateMinBillingBelow { get; set; }
    [MaxLength(4)]  public string? LaminateMinBillingUnit { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal? LaminateAddRate { get; set; }
    [MaxLength(4)]  public string? LaminateAddDisplay { get; set; }
    [MaxLength(4)]  public string? ProcessingMinEstimate { get; set; }
    [MaxLength(4)]  public string? NewSheetUnitPriceBase { get; set; }

    // 得意先(納品書/見積計算書) — 5 項目
    [MaxLength(4)]  public string? DeliverySlipOutDiv { get; set; }
    [MaxLength(4)]  public string? DeliverySlipIssueDiv { get; set; }
    [MaxLength(4)]  public string? SpecialSlipDiv { get; set; }
    [MaxLength(4)]  public string? NightLoadDiv { get; set; }
    [MaxLength(4)]  public string? SizePrintDiv { get; set; }

    // 得意先(納品計算書) — 10 項目
    [MaxLength(4)]  public string? DeliveryCalcOutDiv { get; set; }
    [MaxLength(4)]  public string? DeliveryCalcIssueDiv { get; set; }
    [MaxLength(4)]  public string? DeliveryCalcOutDiv2 { get; set; }
    [MaxLength(100)] public string? DeliveryCalcAddressee { get; set; }
    [MaxLength(50)] public string? DeliveryCalcSender { get; set; }
    [MaxLength(10)] public string? DeliveryCalcZipCd { get; set; }
    [MaxLength(40)] public string? DeliveryCalcAddr1 { get; set; }
    [MaxLength(40)] public string? DeliveryCalcAddr2 { get; set; }
    [MaxLength(40)] public string? DeliveryCalcAddr3 { get; set; }
    [MaxLength(40)] public string? DeliveryCalcAddr4 { get; set; }

    // ═══════════════════════════════════════════════════════════
    //  売掛先 Tab — §6.2
    // ═══════════════════════════════════════════════════════════
    [MaxLength(20)] public string? BillingCd { get; set; }
    [MaxLength(100)] public string? BillingName { get; set; }

    // ═══════════════════════════════════════════════════════════
    //  請求先 Tab — §6.3
    // ═══════════════════════════════════════════════════════════
    [MaxLength(20)] public string? ReceiptCd { get; set; }
    [MaxLength(100)] public string? ReceiptName { get; set; }
    [MaxLength(20)] public string? CreditMgmtArCd { get; set; }
    public int? BillingClosingDay1 { get; set; }   // 1〜31, 99
    [MaxLength(4)]  public string? BillingPrintDiv { get; set; }
    [MaxLength(4)]  public string? BillingSealDiv { get; set; }
    [MaxLength(4)]  public string? ElectronicBilling { get; set; }
    [MaxLength(100)] public string? BillingAddressee { get; set; }
    [MaxLength(50)] public string? BillingSender { get; set; }
    [MaxLength(10)] public string? BillingZipCd { get; set; }
    [MaxLength(40)] public string? BillingAddr1 { get; set; }
    [MaxLength(40)] public string? BillingAddr2 { get; set; }
    [MaxLength(40)] public string? BillingAddr3 { get; set; }
    [MaxLength(40)] public string? BillingAddr4 { get; set; }

    // ═══════════════════════════════════════════════════════════
    //  入金先 Tab — §6.4
    // ═══════════════════════════════════════════════════════════
    [MaxLength(100)] public string? RemittanceName { get; set; }
    [MaxLength(20)] public string? BankCd { get; set; }
    [MaxLength(20)] public string? BankBranchCd { get; set; }
    [MaxLength(50)] public string? RemittanceAccount { get; set; }
    [MaxLength(4)]  public string? TempAccountDiv { get; set; }
    public DateTime? MainAccountRegDate { get; set; }
    [MaxLength(50)] public string? Drawer { get; set; }
    [MaxLength(4)]  public string? CollectionDiv { get; set; }
    public int? CollectionPlannedDay { get; set; } // 1〜31, 99
    [MaxLength(4)]  public string? BillRotationDiv { get; set; }
    [MaxLength(100)] public string? ReceiptAddressee { get; set; }
    [MaxLength(50)] public string? ReceiptSender { get; set; }
    [MaxLength(10)] public string? ReceiptZipCd { get; set; }
    [MaxLength(40)] public string? ReceiptAddr1 { get; set; }
    [MaxLength(40)] public string? ReceiptAddr2 { get; set; }
    [MaxLength(40)] public string? ReceiptAddr3 { get; set; }
    [MaxLength(40)] public string? ReceiptAddr4 { get; set; }
    [MaxLength(200)] public string? CollectionNote { get; set; }

    // ═══════════════════════════════════════════════════════════
    //  納品先 Tab — §6.5
    // ═══════════════════════════════════════════════════════════
    [MaxLength(10)] public string? LogisticsGroupCd { get; set; }   // R 必須
    [MaxLength(100)] public string? LogisticsGroupName { get; set; }
    [MaxLength(50)] public string? DeliveryDept { get; set; }
    [MaxLength(50)] public string? DeliveryContact { get; set; }
    [MaxLength(20)] public string? DeliveryContactTitle { get; set; }
    [Column(TypeName = "decimal(10,2)")] public decimal? TruckLengthLimit { get; set; }
    [MaxLength(8)]  public string? DeliveryTimeFrom { get; set; }
    [MaxLength(8)]  public string? DeliveryTimeTo { get; set; }
    [MaxLength(8)]  public string? PlannedShipTime { get; set; }

    // ═══════════════════════════════════════════════════════════
    //  発注先 Tab — §6.6（最重要ロジック）
    // ═══════════════════════════════════════════════════════════
    /// <summary>発注先パターン：1=原材料 / 2=資材 / 3=副資材 / 4=購買品 / 5=外注品 / 9=他</summary>
    [MaxLength(4)]  public string? SupplierPattern { get; set; }

    /// <summary>下請対象 FLG（パターン=外注品(5) 以外は固定 OFF）</summary>
    public bool SubcontractTargetFlg { get; set; }
    /// <summary>外注単価区分（パターン=外注品(5)時のみ：1=加工単価+有償支給単価）</summary>
    [MaxLength(4)]  public string? SubcontractPriceDiv { get; set; }
    /// <summary>支給計上区分（パターン=外注品(5)時：2=着荷基準）</summary>
    [MaxLength(4)]  public string? SupplyPostingDiv { get; set; }
    /// <summary>支給単価変更許可 FLG</summary>
    public bool SupplyPriceChangeAllowFlg { get; set; }

    // 共通初期値
    public bool DeliveryConfirmFlg { get; set; } = true;        // ☑ 固定
    [MaxLength(4)]  public string? PurchaseFractionDiv { get; set; } = "3"; // 四捨五入
    [MaxLength(4)]  public string? PurchaseTaxFractionDiv { get; set; } = "3";
    [MaxLength(10)] public string? PurchaseTaxCd { get; set; } = "P010";
    public bool PurchaseTaxPriorityFlg { get; set; }
    [MaxLength(10)] public string? SupplierCalendarCd { get; set; } = "CAL01";
    [MaxLength(4)]  public string? SupplyConsignDiv { get; set; } = "1"; // 着荷登録不要
    [MaxLength(4)]  public string? PurchaseLotSplitDiv { get; set; } = "1"; // 分割しない
    [MaxLength(4)]  public string? PurchasePostingDiv { get; set; } = "2"; // 検収基準

    // 有償支給連動
    [MaxLength(4)]  public string? PaidSupplyFractionDiv { get; set; }
    [MaxLength(4)]  public string? PaidSupplyTaxCd { get; set; }
    public bool PaidSupplyTaxPriorityFlg { get; set; }
    [MaxLength(4)]  public string? PaidSupplyAmountFractionDiv { get; set; }
    [MaxLength(4)]  public string? PaidSupplyTaxFractionDiv { get; set; }
    [MaxLength(4)]  public string? PaidSupplyTaxCalcDiv { get; set; }

    // メーカ連動
    [MaxLength(4)]  public string? FscCertificationDiv { get; set; }

    // ═══════════════════════════════════════════════════════════
    //  買掛先 / 支払予定管理先 / 支払先 Tab — §6.7〜6.9
    // ═══════════════════════════════════════════════════════════
    [MaxLength(20)] public string? PaymentScheduleCd { get; set; }
    [MaxLength(20)] public string? PaymentCd { get; set; }
    [MaxLength(20)] public string? PaymentScheduleDeptCd { get; set; }
    public bool PaidEachTimeFlg { get; set; }
    public int? PaymentClosingDay1 { get; set; }   // 1〜31, 99
    public bool PaymentTaxCalcFlg { get; set; }
    [MaxLength(4)]  public string? PaymentTaxFractionDiv { get; set; }
    public bool BatchPaymentScheduleFlg { get; set; }
    public int? PaymentScheduleDelayDays { get; set; }

    // ───── 監査 / 連携 FLG ─────
    /// <summary>岐阜 IF 連携 FLG（仕様書 §10 NO.2）</summary>
    public bool GifuInterfaceFlg { get; set; }
    /// <summary>mcframe7 転送 FLG</summary>
    public bool McTransferFlg { get; set; }
}
