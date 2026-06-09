using CP6.Entity.DomainModels;

namespace CP6.Entity.DTOs.Erp;

/// <summary>受注操作種別（MSBBPA070 §2.1 操作種別切替処理）</summary>
public enum OrderOperationType
{
    /// <summary>登録（新規）</summary>
    New = 10,
    /// <summary>訂正</summary>
    Edit = 20,
    /// <summary>削除（論理削除）</summary>
    Delete = 30,
    /// <summary>参照</summary>
    View = 40,
}

/// <summary>
/// 受注ヘッダー＋全明細＋全工程＋全材料 を 1 個の DTO に集約
/// （3 ページ Wizard で 1-shot POST/PUT）
/// </summary>
public class OrderDto
{
    /// <summary>Web受注NO（PK；新規時は空）</summary>
    public string? WebOrderNo { get; set; }

    // ───── ヘッダー（第1画面 基本情報） ─────
    public string CustomerCd { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public string? OrderDepartment { get; set; }
    public DateTime? OrderDate { get; set; }
    public DateTime? CustomerDeliveryDate { get; set; }
    public decimal? Quantity { get; set; }
    public string? OrderSheetNo { get; set; }
    public string? CustomerContact { get; set; }
    public string? Addressee { get; set; }
    public string? Carrier { get; set; }
    public string? ShipDateTime { get; set; }
    public string? ShipCondition { get; set; }
    public string? SalesPriceDiv { get; set; }
    public string? Memo1 { get; set; }
    public string? Memo2 { get; set; }
    public string? Memo3 { get; set; }

    /// <summary>第1画面 受注明細（部材一覧）— 多行</summary>
    public List<OrderDetailDto> Details { get; set; } = new();

    // ───── サーバ側状態 ─────
    public int Status { get; set; }
    public bool McTransferFlg { get; set; }
    public string? McOrderNo { get; set; }
    public byte[]? RowVersion { get; set; }

    // ───── 出荷実績（WMS出荷確定 → ERP回写 / Phase4）─────
    /// <summary>出荷ステータス：0=未出荷 / 5=一部出荷 / 9=出荷済</summary>
    public int ShipStatus { get; set; }
    /// <summary>実出荷日時（WMS出荷確定で記録）</summary>
    public DateTime? ActualShipDate { get; set; }

    // ───── 多通貨（Gap 4.3、サーバ側で凍結。読取専用）─────
    /// <summary>受注通貨CD（既定 JPY）</summary>
    public string CurrencyCd { get; set; } = FxConstants.BaseCurrency;
    /// <summary>凍結為替レート（外貨1単位あたり基軸通貨額）</summary>
    public decimal FxRate { get; set; } = 1m;
}

/// <summary>受注明細 1 行（製品単位）</summary>
public class OrderDetailDto
{
    public string? WebOrderNo { get; set; }
    public int WebOrderDetailNo { get; set; }

    // ───── 手配NO 1〜4 ─────
    public string? HaibaiNo1 { get; set; }
    public string? HaibaiNo2 { get; set; }
    public string? HaibaiNo3 { get; set; }
    public string? HaibaiNo4 { get; set; }

    // ───── 製品キー ─────
    public string ProductCd { get; set; } = string.Empty;
    public string? ItemCd { get; set; }
    public string? Branch1 { get; set; }
    public string? Branch2 { get; set; }
    public string? Branch3 { get; set; }

    // ───── 製品区分 ─────
    public string? ProductCatBig { get; set; }
    public string? ProductCatMid { get; set; }
    public string? ProductCatSml { get; set; }

    // ───── 顧客・品名 ─────
    public string? CustomerItemName1 { get; set; }
    public string? CustomerItemName2 { get; set; }
    public string? CustomerPartNo { get; set; }
    public string? CpItemName1 { get; set; }
    public string? CpItemName2 { get; set; }
    public string? JanCode { get; set; }

    // ───── 数量・単価 ─────
    public string? QtyUnit { get; set; }
    public decimal? Quantity { get; set; }
    public string? SpecialPriceFlg { get; set; }
    public string? UnitPriceUnit { get; set; }
    public decimal? SetUnitPrice { get; set; }
    public decimal? IndividualUnitPrice { get; set; }
    public decimal? Amount { get; set; }

    // ───── 納品・物流 ─────
    public string? DeliveryCd { get; set; }
    public string? DeliveryName { get; set; }
    public DateTime? CustomerDeliveryDate { get; set; }
    public string? LogisticsGroup { get; set; }
    public string? HaibaiKbn { get; set; }

    // ───── 預り売上（Rev4） ─────
    public string? ConsignedSalesFlg { get; set; }
    public string? SalesReason { get; set; }
    public decimal? ConsignedSalesQty { get; set; }

    // ───── FSC・食品安全 ─────
    public string? FscOrderType { get; set; }
    public string? FscProductDiv { get; set; }
    public string? FscMaterialDiv { get; set; }
    public string? FscManagementNo { get; set; }
    public string? FoodSafety { get; set; }

    // ───── 数量関連属性 ─────
    public string? ShipInspection { get; set; }
    public string? FixedShipment { get; set; }
    public decimal? DeliveryReserve { get; set; }
    public decimal? SalesSample { get; set; }
    public string? SalesAvailable { get; set; }

    // ───── 構成情報 (13フィールド) ─────
    public string? SheetFlute { get; set; }
    public string? PaperCdF { get; set; }
    public string? PaperCdC { get; set; }
    public string? PaperCdB { get; set; }
    public string? PrintCdF { get; set; }
    public string? PrintCdC { get; set; }
    public string? PrintCdB { get; set; }
    public string? EmbossCdF { get; set; }
    public string? EmbossCdC { get; set; }
    public string? EmbossCdB { get; set; }
    public string? MakerCdF { get; set; }
    public string? MakerCdC { get; set; }
    public string? MakerCdB { get; set; }

    // ───── 寸法 ─────
    public string? SheetPrint { get; set; }
    public decimal? BladeWidth { get; set; }
    public decimal? BladeFlow { get; set; }
    public decimal? GutterFb { get; set; }
    public decimal? GutterLr { get; set; }
    public decimal? SheetDimW { get; set; }
    public decimal? SheetDimF { get; set; }
    public decimal? SalesWidth { get; set; }
    public string? FinalMachineProcess { get; set; }

    // ───── 備考 ─────
    public string? PrintNote { get; set; }
    public string? MfgNote { get; set; }
    public string? RemfgNote { get; set; }
    public string? SlipNote { get; set; }
    public string? DeliveryNote { get; set; }
    public string? ShipNote1 { get; set; }
    public string? ShipNote2 { get; set; }

    // ───── 仕入情報 ─────
    public string? DefectiveHaibaiNo { get; set; }
    public string? PurchaseVendor { get; set; }
    public decimal? RollMeter { get; set; }
    public decimal? PurchaseUnitPrice { get; set; }
    public string? PurchaseUnit { get; set; }

    // ───── 案件・見積連携 ─────
    public string? ProjectNoParent { get; set; }
    public string? ProjectNoChild { get; set; }
    public string? ProjectNoMaterial { get; set; }
    public string? QuotationNo { get; set; }
    public string? EstimateCalcNo { get; set; }
    public string? RefEstimateCalcNo { get; set; }

    // ───── セット品 ─────
    public string? SetProductCd { get; set; }
    public string? SetProductName { get; set; }
    public string? ParentChildDiv { get; set; }
    public decimal? SetRatio { get; set; }
    public string? OrderType { get; set; }

    // ───── 製品属性スナップショット ─────
    public string? ProductUsage { get; set; }
    public string? DistributionDiv { get; set; }
    public string? ConfidentialInfo { get; set; }
    public string? SeizureDiv { get; set; }
    public string? ImportanceDiv { get; set; }
    public string? MChange { get; set; }
    public string? QualityDiv { get; set; }
    public string? ProductShape { get; set; }
    public string? UnescoMark { get; set; }
    public string? OrigamiMark { get; set; }
    public string? FourMContract { get; set; }
    public string? TkpWrinkleStd { get; set; }
    public string? RecyclingPayment { get; set; }

    public decimal? PaperUsageG { get; set; }
    public decimal? PlasticUsageG { get; set; }
    public decimal? GlassUsageG { get; set; }
    public decimal? PetUsageG { get; set; }
    public decimal? PackPaperUsageG { get; set; }
    public decimal? PackPlasticUsageG { get; set; }

    public string? DesignProposalNo { get; set; }
    public string? SalesPriceDiv { get; set; }
    public string? FreightBilling { get; set; }

    public string? McOrderNo { get; set; }
    public string? McOrderDetailNo { get; set; }

    public int Status { get; set; }
    public bool WfApprovalFlg { get; set; }
    public bool McTransferFlg { get; set; }

    public bool ProvisionalPriceFlg { get; set; }
    public string? PriceChangeReason { get; set; }
    public int? ApprovalStatus { get; set; }

    // ───── 出荷実績（WMS出荷確定 → ERP回写 / Phase4）─────
    /// <summary>累計出荷数</summary>
    public decimal? ShippedQty { get; set; }
    /// <summary>出荷ステータス：0=未出荷 / 5=一部出荷 / 9=出荷済</summary>
    public int ShipStatus { get; set; }
    /// <summary>最終出荷日時</summary>
    public DateTime? LastShipDate { get; set; }
    /// <summary>最終出庫指示NO</summary>
    public string? LastOutboundNo { get; set; }

    /// <summary>第3画面 工程情報（明細単位）</summary>
    public List<OrderProcessDto> Processes { get; set; } = new();

    /// <summary>工程備考</summary>
    public List<OrderProcessNoteDto> ProcessNotes { get; set; } = new();

    /// <summary>材料設定</summary>
    public List<OrderMaterialDto> Materials { get; set; } = new();
}

public class OrderProcessDto
{
    public string ProductCd { get; set; } = string.Empty;
    public string OperationCd { get; set; } = string.Empty;
    public string ProcessCd { get; set; } = string.Empty;

    public string? TopItemCd { get; set; }
    public string? TopBranch1 { get; set; }
    public string? TopBranch2 { get; set; }
    public string? TopBranch3 { get; set; }
    public string? ItemCd { get; set; }
    public string? Branch1 { get; set; }
    public string? Branch2 { get; set; }
    public string? Branch3 { get; set; }

    public string? WorkingGroupCd { get; set; }
    public string? MachineOrVendor { get; set; }
    public bool MachineFixedFlg { get; set; }
    public string? CpDeliveryDiv { get; set; }

    public string? Spec01 { get; set; }
    public string? Spec02 { get; set; }
    public string? Spec03 { get; set; }
    public string? Spec04 { get; set; }
    public string? Spec05 { get; set; }
    public string? Spec06 { get; set; }
    public string? Spec07 { get; set; }
    public string? Spec08 { get; set; }
    public string? Spec09 { get; set; }
    public string? Spec10 { get; set; }

    public string? QtyUnit { get; set; }
    public string? PlateNo1 { get; set; }
    public string? PlateNo2 { get; set; }
    public string? PlateNo3 { get; set; }
    public string? Consumable1 { get; set; }
    public string? Consumable2 { get; set; }
    public string? Consumable3 { get; set; }

    public decimal? PurchaseUnitPrice { get; set; }
    public decimal? FixedPrice { get; set; }
    public decimal LossRate { get; set; }
    public decimal MachineCount { get; set; }
    public int LeadTimeDays { get; set; }
    public string? StorageLocation { get; set; }

    public int SortOrder { get; set; }

    public string? PriorityItem1 { get; set; }
    public string? PriorityItem2 { get; set; }
    public string? PriorityItem3 { get; set; }
    public string? PriorityItem4 { get; set; }
    public string? PriorityItem5 { get; set; }
    public string? PriorityItem6 { get; set; }
    public string? PriorityItem7 { get; set; }
    public string? PriorityItem8 { get; set; }

    public DateTime? ScheduledDate { get; set; }
}

public class OrderProcessNoteDto
{
    public string ProductCd { get; set; } = string.Empty;
    public string OperationCd { get; set; } = string.Empty;
    public string? Note1 { get; set; }
    public string? Note2 { get; set; }
}

public class OrderMaterialDto
{
    public string ProductCd { get; set; } = string.Empty;
    public string ProcessCd { get; set; } = string.Empty;
    public string MaterialCd { get; set; } = string.Empty;
    public string MaterialTypeDiv { get; set; } = "3";
    public string? ItemCd { get; set; }
    public string? Branch1 { get; set; }
    public string? Branch2 { get; set; }
    public string? Branch3 { get; set; }
    public string SupplyDiv { get; set; } = "1";
    public decimal SupplyUnitPrice { get; set; }
    public int SortOrder { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  PA080 受注一覧照会
// ═══════════════════════════════════════════════════════════════

/// <summary>PA080 受注一覧 検索条件（40+ パラメータ）</summary>
public class OrderQueryDto
{
    public string? BaseCd { get; set; }
    public string? SalesPersonCd { get; set; }
    public string? CustomerCd { get; set; }
    public string? CustomerCdTo { get; set; }
    public string? OrderType { get; set; }
    public string? ProductCatBig { get; set; }
    public string? ProductCatMid { get; set; }
    public string? ProductCatSml { get; set; }

    public DateTime? OrderDateFrom { get; set; }
    public DateTime? OrderDateTo { get; set; }
    public DateTime? DeliveryDateFrom { get; set; }
    public DateTime? DeliveryDateTo { get; set; }

    public string? HaibaiNo1From { get; set; }
    public string? HaibaiNo1To { get; set; }
    public string? HaibaiNo2From { get; set; }
    public string? HaibaiNo2To { get; set; }
    public string? HaibaiNo3From { get; set; }
    public string? HaibaiNo3To { get; set; }
    public string? OrderSheetNo { get; set; }
    public string? DefectiveHaibaiNo { get; set; }
    public string? McOrderNo { get; set; }

    public string? ProductCd { get; set; }
    public string? ItemCd { get; set; }
    public string? CustomerItemName { get; set; }
    public string? CustomerPartNo { get; set; }

    public string? SheetFlute { get; set; }
    public string? PaperCd { get; set; }
    public string? PrintCd { get; set; }
    public string? EmbossCd { get; set; }
    public string? MakerCd { get; set; }
    public string? FscOrderType { get; set; }

    public string? SalesPriceDiv { get; set; }
    public string? ProductShape { get; set; }
    public string? DistributionDiv { get; set; }
    public string? Carrier { get; set; }

    public bool? OnlyConsignedSales { get; set; }
    public bool? OnlyMcUntransferred { get; set; }
    public bool? IncludeDeleted { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public int? MaxRows { get; set; }

    /// <summary>排序列（前端 el-table prop，白名单内才生效）</summary>
    public string? SortField { get; set; }
    /// <summary>排序方向：asc / desc</summary>
    public string? SortOrder { get; set; }
}

/// <summary>PA080 一覧 1 行（31列）</summary>
public class OrderListItemDto
{
    public int RowNo { get; set; }
    public string? CustomerCd { get; set; }
    public string? SalesPersonCd { get; set; }
    public string? CustomerName { get; set; }
    public string? SalesPersonName { get; set; }
    public string? OrderSheetNo { get; set; }
    public string? HaibaiNo1 { get; set; }
    public string? DefectiveHaibaiNo { get; set; }
    public string? McOrderNo { get; set; }
    public DateTime? OrderDate { get; set; }
    public DateTime? CustomerDeliveryDate { get; set; }
    public string? ProductCd { get; set; }
    public string? ItemCd { get; set; }
    public string? ProductCatBig { get; set; }
    public string? ProductCatBigName { get; set; }
    /// <summary>シート受注=構成名 / それ以外=CP品名</summary>
    public string? CpItemOrComposition { get; set; }
    public string? SheetFlute { get; set; }
    public string? CompositionF { get; set; }
    public string? CompositionC { get; set; }
    public string? CompositionB { get; set; }
    public string? CompositionItemName { get; set; }
    public decimal? FullSheetWidth { get; set; }
    public decimal? FullSheetFlow { get; set; }
    public decimal? SlitterWidth { get; set; }
    public decimal? SlitterFlow { get; set; }
    public string? QtyUnit { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? IndividualUnitPrice { get; set; }
    public decimal? SetUnitPrice { get; set; }
    public string? UnitPriceUnit { get; set; }
    public decimal? Amount { get; set; }
    public string? ConsignedSalesFlg { get; set; }
    public string? SlipNote { get; set; }
    public string? DeliveryNote { get; set; }
    public string WebOrderNo { get; set; } = string.Empty;
    public int WebOrderDetailNo { get; set; }

    // ───── 多通貨（Gap 4.3、ヘッダから補完）─────
    /// <summary>受注通貨CD（既定 JPY）</summary>
    public string? CurrencyCd { get; set; }
    /// <summary>凍結為替レート</summary>
    public decimal? FxRate { get; set; }
}

/// <summary>仕掛チェック結果（与 PA050 共用）</summary>
public class OrderWipCheckResultDto
{
    /// <summary>0=問題なし / 2=保存・確定済み(L2) / 3=指図済(L3)</summary>
    public int Level { get; set; }
    public string? Message { get; set; }
}

/// <summary>与信チェック結果</summary>
public class CreditCheckResultDto
{
    /// <summary>true=超過 / false=範囲内</summary>
    public bool IsOver { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal Balance { get; set; }
    public string? Message { get; set; }
}

/// <summary>isEditable 判定結果（構成・工程編集可フラグ）</summary>
public class IsEditableResultDto
{
    public bool IsEditable { get; set; }
    public string? Reason { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  PA090 単価訂正
// ═══════════════════════════════════════════════════════════════

/// <summary>PA090 検索条件</summary>
public class OrderPriceCorrectionQueryDto
{
    public string? BaseCd { get; set; }                  // 必須
    public string? SalesPersonCd { get; set; }
    public string? CustomerCd { get; set; }
    public string? CustomerCdTo { get; set; }
    public DateTime? OrderDateFrom { get; set; }
    public DateTime? OrderDateTo { get; set; }
    public DateTime? DeliveryDateFrom { get; set; }
    public DateTime? DeliveryDateTo { get; set; }
    public string? HaibaiNo1 { get; set; }
    public string? OrderSheetNo { get; set; }
    public string? ProductCd { get; set; }
    public string? CustomerItemName { get; set; }
    public decimal? QtyFrom { get; set; }
    public decimal? QtyTo { get; set; }
    public decimal? AmountFrom { get; set; }
    public decimal? AmountTo { get; set; }
    public bool? OnlyProvisional { get; set; }
    public string? ApprovalStatus { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public int? MaxRows { get; set; }
}

/// <summary>PA090 一覧 1 行（37列）</summary>
public class OrderPriceCorrectionItemDto
{
    public int RowNo { get; set; }
    public int? ApprovalStatus { get; set; }
    public string? CustomerCd { get; set; }
    public string? CustomerName { get; set; }
    public string? SalesPersonCd { get; set; }
    public string? SalesPersonName { get; set; }
    public string? OrderSheetNo { get; set; }
    public string? HaibaiNo1 { get; set; }
    public string? DefectiveHaibaiNo { get; set; }
    public string? ProductCd { get; set; }
    public string? ItemCd { get; set; }
    public string? CpItemName1 { get; set; }
    public string? CpItemName2 { get; set; }
    public DateTime? OrderDate { get; set; }
    public DateTime? CustomerDeliveryDate { get; set; }
    public string? Unit { get; set; }
    public string? CpItemOrComposition { get; set; }
    public string? CompositionF { get; set; }
    public string? CompositionC { get; set; }
    public string? CompositionB { get; set; }
    public string? SheetFlute { get; set; }
    public decimal? FullSheetWidth { get; set; }
    public decimal? FullSheetFlow { get; set; }
    public decimal? SlitterWidth { get; set; }
    public decimal? SlitterFlow { get; set; }
    public decimal? Quantity { get; set; }
    public string? QtyUnit { get; set; }

    /// <summary>個別単価（変更前）</summary>
    public decimal? IndividualUnitPriceBefore { get; set; }
    /// <summary>セット単価（変更前）</summary>
    public decimal? SetUnitPriceBefore { get; set; }
    public string? UnitPriceUnit { get; set; }
    /// <summary>個別単価（変更後 — 編集対象）</summary>
    public decimal? IndividualUnitPriceAfter { get; set; }
    /// <summary>セット単価（変更後 — 編集対象）</summary>
    public decimal? SetUnitPriceAfter { get; set; }
    public string? SpecialPriceFlg { get; set; }
    public bool ProvisionalPriceFlg { get; set; }
    public string? PriceChangeReason { get; set; }
    public decimal? Amount { get; set; }
    public string? ConsignedSalesFlg { get; set; }
    public string? SlipNote { get; set; }
    public string? DeliveryNote { get; set; }
    public string WebOrderNo { get; set; } = string.Empty;
    public int WebOrderDetailNo { get; set; }

    /// <summary>UI 用：チェック ON で編集可</summary>
    public bool Selected { get; set; }
    public byte[]? RowVersion { get; set; }
}

/// <summary>PA090 単価訂正 1 行更新リクエスト</summary>
public class OrderPriceCorrectionUpdateDto
{
    public string WebOrderNo { get; set; } = string.Empty;
    public int WebOrderDetailNo { get; set; }
    public decimal? IndividualUnitPriceAfter { get; set; }
    public decimal? SetUnitPriceAfter { get; set; }
    public string? SpecialPriceFlg { get; set; }
    public string? PriceChangeReason { get; set; }
    public byte[]? RowVersion { get; set; }
}

/// <summary>PA090 単価訂正 バッチ更新リクエスト</summary>
public class OrderPriceCorrectionBatchUpdateDto
{
    public List<OrderPriceCorrectionUpdateDto> Items { get; set; } = new();
}

/// <summary>PA090 バッチ更新結果（POWER EGG WF 起票件数を含む）</summary>
public class OrderPriceCorrectionBatchResultDto
{
    public int UpdatedCount { get; set; }
    public int WfRequestedCount { get; set; }
    public List<string> ConflictedKeys { get; set; } = new();
}
