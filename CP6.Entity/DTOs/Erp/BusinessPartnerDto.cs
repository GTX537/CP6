namespace CP6.Entity.DTOs.Erp;

/// <summary>取引先操作種別（PA110 §3）</summary>
public enum BpOperationType
{
    /// <summary>事前登録（ステータス=0）</summary>
    PreRegister = 5,
    /// <summary>登録（ステータス=1）</summary>
    Register = 10,
    /// <summary>訂正</summary>
    Edit = 20,
    /// <summary>削除（管理者のみ）</summary>
    Delete = 30,
    /// <summary>参照</summary>
    View = 40,
}

/// <summary>取引先 1 個の DTO（全 Tab フィールドを集約）</summary>
public class BusinessPartnerDto
{
    public string BpCd { get; set; } = string.Empty;
    public string BpName { get; set; } = string.Empty;
    public string? BpAbbrev { get; set; }
    public string BaseCd { get; set; } = string.Empty;
    public int Status { get; set; } = 1;

    // 法人 / 公的番号
    public string? StdCoCd { get; set; }
    public string? Ein { get; set; }
    public string? EinType { get; set; }
    public string? LocalPublicCd { get; set; }
    public string? DenzaiNo { get; set; }

    // 住所
    public string? ZipCd { get; set; }
    public string? Addr1 { get; set; }
    public string? Addr2 { get; set; }
    public string? Addr3 { get; set; }
    public string? Addr4 { get; set; }
    public string? Tel { get; set; }
    public string? Fax { get; set; }
    public string? AreaCd { get; set; }
    public string? SalesStaffCd { get; set; }
    public string? BusinessStaffCd { get; set; }

    // 9 個の属性 FLG
    public bool CustomerFlg { get; set; }
    public bool AccountsReceivableFlg { get; set; }
    public bool BillingFlg { get; set; }
    public bool ReceiptFlg { get; set; }
    public bool DeliveryFlg { get; set; }
    public bool SupplierFlg { get; set; }
    public bool AccountsPayableFlg { get; set; }
    public bool PaymentScheduleFlg { get; set; }
    public bool PaymentFlg { get; set; }
    public bool CreditMgmtFlg { get; set; }
    public bool MakerFlg { get; set; }
    public bool PaidSupplyFlg { get; set; }
    public bool RebuyObligationFlg { get; set; }

    // 取引先分類 1〜10 + 販売分析 1〜3
    public string? BpClass01 { get; set; }
    public string? BpClass02 { get; set; }
    public string? BpClass03 { get; set; }
    public string? BpClass04 { get; set; }
    public string? BpClass05 { get; set; }
    public string? BpClass06 { get; set; }
    public string? BpClass07 { get; set; }
    public string? BpClass08 { get; set; }
    public string? BpClass09 { get; set; }
    public string? BpClass10 { get; set; }
    public string? SalesAnalysis1 { get; set; }
    public string? SalesAnalysis2 { get; set; }
    public string? SalesAnalysis3 { get; set; }

    // 得意先 Tab
    public string? ParentCustomerCd { get; set; }
    public string? UserConverterDiv { get; set; }
    public string? AccountsReceivableCd { get; set; }
    public string? CreditMgmtCd { get; set; }
    public string? CustomerDept { get; set; }
    public string? CustomerContact { get; set; }
    public string? CustomerContactTitle { get; set; }
    public string? RecyclingTarget { get; set; }
    public string? SalesPostingDiv { get; set; }
    public string? SheetSalesCalcMethod { get; set; }
    public string? SalesCalcDivM2 { get; set; }
    public string? SalesCalcDivPiece { get; set; }
    public string? FractionCalcDiv { get; set; }
    public string? FullSheetSalesDiv { get; set; }
    public string? SlitterBillingDiv { get; set; }
    public decimal? SlitterBillingUnitPrice { get; set; }
    public decimal? SlitterMaxFlow { get; set; }
    public string? PrintMinBilling { get; set; }
    public decimal? PrintMinBillingBelow { get; set; }
    public string? PrintMinBillingUnit { get; set; }
    public string? LaminateMinBilling { get; set; }
    public decimal? LaminateMinBillingBelow { get; set; }
    public string? LaminateMinBillingUnit { get; set; }
    public decimal? LaminateAddRate { get; set; }
    public string? LaminateAddDisplay { get; set; }
    public string? ProcessingMinEstimate { get; set; }
    public string? NewSheetUnitPriceBase { get; set; }
    public string? DeliverySlipOutDiv { get; set; }
    public string? DeliverySlipIssueDiv { get; set; }
    public string? SpecialSlipDiv { get; set; }
    public string? NightLoadDiv { get; set; }
    public string? SizePrintDiv { get; set; }
    public string? DeliveryCalcOutDiv { get; set; }
    public string? DeliveryCalcIssueDiv { get; set; }
    public string? DeliveryCalcOutDiv2 { get; set; }
    public string? DeliveryCalcAddressee { get; set; }
    public string? DeliveryCalcSender { get; set; }
    public string? DeliveryCalcZipCd { get; set; }
    public string? DeliveryCalcAddr1 { get; set; }
    public string? DeliveryCalcAddr2 { get; set; }
    public string? DeliveryCalcAddr3 { get; set; }
    public string? DeliveryCalcAddr4 { get; set; }

    // 売掛先
    public string? BillingCd { get; set; }
    public string? BillingName { get; set; }

    // 請求先
    public string? ReceiptCd { get; set; }
    public string? ReceiptName { get; set; }
    public string? CreditMgmtArCd { get; set; }
    public int? BillingClosingDay1 { get; set; }
    public string? BillingPrintDiv { get; set; }
    public string? BillingSealDiv { get; set; }
    public string? ElectronicBilling { get; set; }
    public string? BillingAddressee { get; set; }
    public string? BillingSender { get; set; }
    public string? BillingZipCd { get; set; }
    public string? BillingAddr1 { get; set; }
    public string? BillingAddr2 { get; set; }
    public string? BillingAddr3 { get; set; }
    public string? BillingAddr4 { get; set; }

    // 入金先
    public string? RemittanceName { get; set; }
    public string? BankCd { get; set; }
    public string? BankBranchCd { get; set; }
    public string? RemittanceAccount { get; set; }
    public string? TempAccountDiv { get; set; }
    public DateTime? MainAccountRegDate { get; set; }
    public string? Drawer { get; set; }
    public string? CollectionDiv { get; set; }
    public int? CollectionPlannedDay { get; set; }
    public string? BillRotationDiv { get; set; }
    public string? ReceiptAddressee { get; set; }
    public string? ReceiptSender { get; set; }
    public string? ReceiptZipCd { get; set; }
    public string? ReceiptAddr1 { get; set; }
    public string? ReceiptAddr2 { get; set; }
    public string? ReceiptAddr3 { get; set; }
    public string? ReceiptAddr4 { get; set; }
    public string? CollectionNote { get; set; }

    // 納品先
    public string? LogisticsGroupCd { get; set; }
    public string? LogisticsGroupName { get; set; }
    public string? DeliveryDept { get; set; }
    public string? DeliveryContact { get; set; }
    public string? DeliveryContactTitle { get; set; }
    public decimal? TruckLengthLimit { get; set; }
    public string? DeliveryTimeFrom { get; set; }
    public string? DeliveryTimeTo { get; set; }
    public string? PlannedShipTime { get; set; }

    // 発注先
    public string? SupplierPattern { get; set; }
    public bool SubcontractTargetFlg { get; set; }
    public string? SubcontractPriceDiv { get; set; }
    public string? SupplyPostingDiv { get; set; }
    public bool SupplyPriceChangeAllowFlg { get; set; }
    public bool DeliveryConfirmFlg { get; set; }
    public string? PurchaseFractionDiv { get; set; }
    public string? PurchaseTaxFractionDiv { get; set; }
    public string? PurchaseTaxCd { get; set; }
    public bool PurchaseTaxPriorityFlg { get; set; }
    public string? SupplierCalendarCd { get; set; }
    public string? SupplyConsignDiv { get; set; }
    public string? PurchaseLotSplitDiv { get; set; }
    public string? PurchasePostingDiv { get; set; }
    public string? PaidSupplyFractionDiv { get; set; }
    public string? PaidSupplyTaxCd { get; set; }
    public bool PaidSupplyTaxPriorityFlg { get; set; }
    public string? PaidSupplyAmountFractionDiv { get; set; }
    public string? PaidSupplyTaxFractionDiv { get; set; }
    public string? PaidSupplyTaxCalcDiv { get; set; }
    public string? FscCertificationDiv { get; set; }

    // 買掛先 / 支払予定管理先 / 支払先
    public string? PaymentScheduleCd { get; set; }
    public string? PaymentCd { get; set; }
    public string? PaymentScheduleDeptCd { get; set; }
    public bool PaidEachTimeFlg { get; set; }
    public int? PaymentClosingDay1 { get; set; }
    public bool PaymentTaxCalcFlg { get; set; }
    public string? PaymentTaxFractionDiv { get; set; }
    public bool BatchPaymentScheduleFlg { get; set; }
    public int? PaymentScheduleDelayDays { get; set; }

    // 連携 FLG
    public bool GifuInterfaceFlg { get; set; }
    public bool McTransferFlg { get; set; }

    /// <summary>楽観的ロック</summary>
    public byte[]? RowVersion { get; set; }
}

// ═══════════════════════════════════════════════════════════
//  PA120 取引先一覧
// ═══════════════════════════════════════════════════════════

public class BpQueryDto
{
    // 属性 FLG（11 個、既定全 ON）
    public bool? IncludeCustomer { get; set; } = true;
    public bool? IncludeAccountsReceivable { get; set; } = true;
    public bool? IncludeBilling { get; set; } = true;
    public bool? IncludeReceipt { get; set; } = true;
    public bool? IncludeDelivery { get; set; } = true;
    public bool? IncludeCreditMgmt { get; set; } = true;
    public bool? IncludeSupplier { get; set; } = true;
    public bool? IncludeAccountsPayable { get; set; } = true;
    public bool? IncludePaymentSchedule { get; set; } = true;
    public bool? IncludePayment { get; set; } = true;
    public bool? IncludeMaker { get; set; } = true;

    // 基本条件
    public DateTime? RegisteredDateFrom { get; set; }
    public DateTime? RegisteredDateTo { get; set; }
    public string? BpCd { get; set; }
    public string? BpName { get; set; }
    public string? Ein { get; set; }
    public string? EinType { get; set; }
    public string? StdCoCd { get; set; }
    public string? ZipCd { get; set; }
    public string? Addr { get; set; }
    public string? Tel { get; set; }
    public string? Fax { get; set; }
    public string? AreaCd { get; set; }
    public string? SalesWg { get; set; }
    public string? SalesStaffCd { get; set; }
    public string? BusinessWg { get; set; }
    public string? BusinessStaffCd { get; set; }

    public string? BpClass01 { get; set; }
    public string? BpClass02 { get; set; }
    public string? BpClass03 { get; set; }
    public string? BpClass04 { get; set; }
    public string? BpClass05 { get; set; }
    public string? BpClass06 { get; set; }
    public string? BpClass07 { get; set; }
    public string? BpClass08 { get; set; }
    public string? BpClass09 { get; set; }
    public string? BpClass10 { get; set; }

    public bool? IncludePreRegistered { get; set; } = true;
    public bool? IncludeRegistered { get; set; } = true;

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public int? MaxRows { get; set; }

    /// <summary>排序列（前端 el-table prop，白名单内才生效）</summary>
    public string? SortField { get; set; }
    /// <summary>排序方向：asc / desc</summary>
    public string? SortOrder { get; set; }
}

public class BpListItemDto
{
    public int RowNo { get; set; }
    public int Status { get; set; }
    public string? BpCd { get; set; }
    public string? BpName { get; set; }
    public string? BpAbbrev { get; set; }
    public string? SalesWg { get; set; }
    public string? SalesWgName { get; set; }
    public string? SalesStaffCd { get; set; }
    public string? SalesStaffName { get; set; }
    public string? BusinessWg { get; set; }
    public string? BusinessWgName { get; set; }
    public string? BusinessStaffCd { get; set; }
    public string? BusinessStaffName { get; set; }
    public string? Ein { get; set; }
    public string? StdCoCd { get; set; }
    public string? Addr1 { get; set; }
    public string? Addr2 { get; set; }
    public string? Addr3 { get; set; }
    public string? Addr4 { get; set; }
    public bool CustomerFlg { get; set; }
    public bool AccountsReceivableFlg { get; set; }
    public bool BillingFlg { get; set; }
    public bool ReceiptFlg { get; set; }
    public bool DeliveryFlg { get; set; }
    public bool CreditMgmtFlg { get; set; }
    public bool SupplierFlg { get; set; }
    public bool AccountsPayableFlg { get; set; }
    public bool PaymentScheduleFlg { get; set; }
    public bool PaymentFlg { get; set; }
    public bool MakerFlg { get; set; }
    public DateTime? CreateDate { get; set; }
    public string? Creator { get; set; }
    public string? CreatorName { get; set; }
}

// ═══════════════════════════════════════════════════════════
//  PA100 FSC チェックシート
// ═══════════════════════════════════════════════════════════

public class FscChecklistQueryDto
{
    public string? BaseCd { get; set; }            // 必須
    public string? StaffCd { get; set; }
    public DateTime? IssueDateFrom { get; set; }
    public DateTime? IssueDateTo { get; set; }
    public string? QtnNoFrom { get; set; }
    public string? QtnNoTo { get; set; }
    public string? CustomerCd { get; set; }
    public string? ProjectNo { get; set; }
    public bool? IncludeUnissued { get; set; } = true;
    public bool? IncludeIssued { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public int? MaxRows { get; set; }
}

public class FscChecklistItemDto
{
    public int RowNo { get; set; }
    public string? StaffCd { get; set; }
    public string? StaffName { get; set; }
    public string? CustomerCd { get; set; }
    public string? CustomerName { get; set; }
    public string? ProjectNo { get; set; }
    public string? ProjectName { get; set; }
    public string QtnNo { get; set; } = string.Empty;
    public DateTime? QtnIssueDate { get; set; }
    public string? CustomerItemName1 { get; set; }
    public string? CustomerItemName2 { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Amount { get; set; }
    public decimal? TotalAmount { get; set; }
    public int? Status { get; set; }
    /// <summary>UI: 発行☑</summary>
    public bool Issue { get; set; }
    public string? FscManagementNo { get; set; }
    public string? IssuedLabel { get; set; }   // FSC 管理 NO あり=発行済 / それ以外=空白
    public string QtnCalcNo { get; set; } = string.Empty;
    public string? FscProductDiv { get; set; }
}

public class FscIssueRequestDto
{
    /// <summary>web.config の出力フォーマット名</summary>
    public string FormatName { get; set; } = string.Empty;
    /// <summary>発行対象（QtnNo + QtnCalcNo の組）</summary>
    public List<FscIssueTarget> Targets { get; set; } = new();
}

public class FscIssueTarget
{
    public string QtnNo { get; set; } = string.Empty;
    public string QtnCalcNo { get; set; } = string.Empty;
    public string? CustomerCd { get; set; }
    public string? StaffCd { get; set; }
}

public class FscIssueResultDto
{
    public int IssuedCount { get; set; }
    public List<FscIssueResultItem> Items { get; set; } = new();
}

public class FscIssueResultItem
{
    public string FscManagementNo { get; set; } = string.Empty;
    public string ExcelFileName { get; set; } = string.Empty;
    public string QtnNo { get; set; } = string.Empty;
    public string QtnCalcNo { get; set; } = string.Empty;
    public byte[]? Content { get; set; }   // Base64 で返却
}

/// <summary>取引先 FLG 変更不可チェック結果</summary>
public class BpFlgChangeCheckResult
{
    public bool Ok { get; set; }
    public List<string> ChangedFlgs { get; set; } = new();
    public string? Message { get; set; }
}
