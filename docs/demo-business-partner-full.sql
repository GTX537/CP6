/* =====================================================================
 * 取引先マスタ 全項目フル投入 デモデータ
 *   - 1 行：BpCd='BP-DEMO-ALL'  株式会社 デモ商事
 *   - 全 19 bit フラグ = 1
 *   - 全 170 列のうち、Nullable も含めて全て埋める
 *   - 実在しそうな紙器業界の大手客先パターンで構築
 *   - MERGE upsert：再実行可能
 *
 *   実行: sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB -E ^
 *               -i "D:\CP6\docs\demo-business-partner-full.sql" -b
 *   cp6.uk: docker cp -> docker exec で同様に実行可
 * ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
  BEGIN TRAN;
  PRINT 'BP full-flag demo: 株式会社デモ商事 投入開始';

  -- 既存削除（v1 を排除し綺麗にやり直す場合）
  -- DELETE FROM T_WebBusinessPartner WHERE BpCd = 'BP-DEMO-ALL';

  MERGE T_WebBusinessPartner AS tgt
  USING (SELECT 'BP-DEMO-ALL' AS BpCd) AS src
    ON tgt.BpCd = src.BpCd
  WHEN MATCHED THEN UPDATE SET
    -- 基本情報
    BpName    = N'株式会社 デモ商事',
    BpAbbrev  = N'デモ商事',
    BaseCd    = '001',
    Status    = 1,
    StdCoCd   = 'STD-DEMO-001',
    Ein       = '9876543210123',
    EinType   = '01',
    LocalPublicCd = '13104',
    DenzaiNo  = 'DZ0001234',
    -- 住所
    ZipCd     = '106-6108',
    Addr1     = N'東京都',
    Addr2     = N'港区',
    Addr3     = N'六本木1-6-1',
    Addr4     = N'泉ガーデンタワー 8F',
    Tel       = '03-5775-1234',
    Fax       = '03-5775-1235',
    AreaCd    = '03',
    SalesStaffCd    = 'S-1001',
    BusinessStaffCd = 'B-2001',
    -- 全 19 フラグ ON
    CustomerFlg               = 1,
    AccountsReceivableFlg     = 1,
    BillingFlg                = 1,
    ReceiptFlg                = 1,
    DeliveryFlg               = 1,
    SupplierFlg               = 1,
    AccountsPayableFlg        = 1,
    PaymentScheduleFlg        = 1,
    PaymentFlg                = 1,
    CreditMgmtFlg             = 1,
    MakerFlg                  = 1,
    PaidSupplyFlg             = 1,
    RebuyObligationFlg        = 1,
    SubcontractTargetFlg      = 1,
    SupplyPriceChangeAllowFlg = 1,
    DeliveryConfirmFlg        = 1,
    PurchaseTaxPriorityFlg    = 1,
    PaidSupplyTaxPriorityFlg  = 1,
    PaidEachTimeFlg           = 1,
    PaymentTaxCalcFlg         = 1,
    BatchPaymentScheduleFlg   = 1,
    GifuInterfaceFlg          = 1,
    McTransferFlg             = 1,
    -- 取引先分類 1〜10
    BpClass01 = 'A1', BpClass02 = 'B2', BpClass03 = 'C3',
    BpClass04 = 'D4', BpClass05 = 'E5', BpClass06 = 'F6',
    BpClass07 = 'G7', BpClass08 = 'H8', BpClass09 = 'I9',
    BpClass10 = 'J0',
    SalesAnalysis1 = 'SA01',
    SalesAnalysis2 = 'SA02',
    SalesAnalysis3 = 'SA03',
    -- 親客 / 換算
    ParentCustomerCd = 'BP-DEMO-ALL',
    UserConverterDiv = '01',
    -- 売掛・与信
    AccountsReceivableCd = 'AR-DEMO-001',
    CreditMgmtCd         = 'CM-DEMO-001',
    -- 客先窓口
    CustomerDept         = N'購買部',
    CustomerContact      = N'山田 太郎',
    CustomerContactTitle = N'部長',
    -- 売上関連
    RecyclingTarget       = '01',
    SalesPostingDiv       = '01',
    SheetSalesCalcMethod  = '02',
    SalesCalcDivM2        = '01',
    SalesCalcDivPiece     = '02',
    FractionCalcDiv       = '01',
    FullSheetSalesDiv     = '01',
    SlitterBillingDiv     = '01',
    SlitterBillingUnitPrice = 12.50,
    SlitterMaxFlow        = 1500.00,
    PrintMinBilling       = '01',
    PrintMinBillingBelow  = 5000.00,
    PrintMinBillingUnit   = 'JPY',
    LaminateMinBilling    = '01',
    LaminateMinBillingBelow = 3000.00,
    LaminateMinBillingUnit  = 'JPY',
    LaminateAddRate       = 5.50,
    LaminateAddDisplay    = '01',
    ProcessingMinEstimate = '01',
    NewSheetUnitPriceBase = '01',
    DeliverySlipOutDiv    = '01',
    DeliverySlipIssueDiv  = '02',
    SpecialSlipDiv        = '01',
    NightLoadDiv          = '02',
    SizePrintDiv          = '01',
    -- 納品計算
    DeliveryCalcOutDiv    = '01',
    DeliveryCalcIssueDiv  = '02',
    DeliveryCalcOutDiv2   = '01',
    DeliveryCalcAddressee = N'株式会社 デモ商事 茨城物流センター 御中',
    DeliveryCalcSender    = N'当社営業部',
    DeliveryCalcZipCd     = '305-0031',
    DeliveryCalcAddr1     = N'茨城県',
    DeliveryCalcAddr2     = N'つくば市',
    DeliveryCalcAddr3     = N'吾妻 2-2-2',
    DeliveryCalcAddr4     = N'デモ商事 茨城物流センター',
    -- 請求
    BillingCd             = 'BL-DEMO-001',
    BillingName           = N'株式会社 デモ商事 経理部',
    ReceiptCd             = 'RC-DEMO-001',
    ReceiptName           = N'株式会社 デモ商事 受領担当',
    CreditMgmtArCd        = 'CM-AR-DEMO-001',
    BillingClosingDay1    = 25,
    BillingPrintDiv       = '01',
    BillingSealDiv        = '01',
    ElectronicBilling     = '01',
    BillingAddressee      = N'株式会社 デモ商事 経理部 御中',
    BillingSender         = N'当社経理部',
    BillingZipCd          = '106-6108',
    BillingAddr1          = N'東京都',
    BillingAddr2          = N'港区',
    BillingAddr3          = N'六本木 1-6-1',
    BillingAddr4          = N'泉ガーデンタワー 8F 経理部',
    -- 入金・振込
    RemittanceName        = N'カ）デモシヨウジ',
    BankCd                = '0001',
    BankBranchCd          = '003',
    RemittanceAccount     = '1234567',
    TempAccountDiv        = '01',
    MainAccountRegDate    = '2024-04-01',
    Drawer                = N'三菱UFJ銀行 六本木支店',
    CollectionDiv         = '01',
    CollectionPlannedDay  = 25,
    BillRotationDiv       = '01',
    ReceiptAddressee      = N'株式会社 デモ商事 経理部 受領担当 御中',
    ReceiptSender         = N'当社経理部',
    ReceiptZipCd          = '106-6108',
    ReceiptAddr1          = N'東京都',
    ReceiptAddr2          = N'港区',
    ReceiptAddr3          = N'六本木 1-6-1',
    ReceiptAddr4          = N'泉ガーデンタワー 8F',
    CollectionNote        = N'毎月25日締め、翌月末日振込（手数料先方負担）',
    -- 物流
    LogisticsGroupCd      = 'LG-001',
    LogisticsGroupName    = N'関東圏物流グループ',
    DeliveryDept          = N'物流センター',
    DeliveryContact       = N'佐藤 花子',
    DeliveryContactTitle  = N'マネージャー',
    TruckLengthLimit      = 10.50,
    DeliveryTimeFrom      = '09:00',
    DeliveryTimeTo        = '17:00',
    PlannedShipTime       = '15:00',
    -- 仕入関連
    SupplierPattern       = '01',
    SubcontractPriceDiv   = '01',
    SupplyPostingDiv      = '01',
    PurchaseFractionDiv   = '01',
    PurchaseTaxFractionDiv = '01',
    PurchaseTaxCd          = 'JP10',
    SupplierCalendarCd     = 'JP-CAL',
    SupplyConsignDiv       = '01',
    PurchaseLotSplitDiv    = '01',
    PurchasePostingDiv     = '01',
    -- 有償支給
    PaidSupplyFractionDiv         = '01',
    PaidSupplyTaxCd               = 'JP10',
    PaidSupplyAmountFractionDiv   = '01',
    PaidSupplyTaxFractionDiv      = '01',
    PaidSupplyTaxCalcDiv          = '01',
    -- FSC
    FscCertificationDiv = 'FSC',
    -- 支払
    PaymentScheduleCd       = 'PS-DEMO-001',
    PaymentCd               = 'PY-DEMO-001',
    PaymentScheduleDeptCd   = 'PSD-DEMO-001',
    PaymentClosingDay1      = 20,
    PaymentTaxFractionDiv   = '01',
    PaymentScheduleDelayDays = 30,
    -- 監査
    Modifier   = 'demo-init',
    ModifyDate = GETDATE()
  WHEN NOT MATCHED THEN INSERT (
    Id, BpCd, BpName, BpAbbrev, BaseCd, Status,
    StdCoCd, Ein, EinType, LocalPublicCd, DenzaiNo,
    ZipCd, Addr1, Addr2, Addr3, Addr4, Tel, Fax, AreaCd,
    SalesStaffCd, BusinessStaffCd,
    CustomerFlg, AccountsReceivableFlg, BillingFlg, ReceiptFlg, DeliveryFlg,
    SupplierFlg, AccountsPayableFlg, PaymentScheduleFlg, PaymentFlg,
    CreditMgmtFlg, MakerFlg, PaidSupplyFlg, RebuyObligationFlg,
    SubcontractTargetFlg, SupplyPriceChangeAllowFlg, DeliveryConfirmFlg,
    PurchaseTaxPriorityFlg, PaidSupplyTaxPriorityFlg, PaidEachTimeFlg,
    PaymentTaxCalcFlg, BatchPaymentScheduleFlg, GifuInterfaceFlg, McTransferFlg,
    BpClass01, BpClass02, BpClass03, BpClass04, BpClass05,
    BpClass06, BpClass07, BpClass08, BpClass09, BpClass10,
    SalesAnalysis1, SalesAnalysis2, SalesAnalysis3,
    ParentCustomerCd, UserConverterDiv,
    AccountsReceivableCd, CreditMgmtCd,
    CustomerDept, CustomerContact, CustomerContactTitle,
    RecyclingTarget, SalesPostingDiv, SheetSalesCalcMethod,
    SalesCalcDivM2, SalesCalcDivPiece, FractionCalcDiv,
    FullSheetSalesDiv, SlitterBillingDiv, SlitterBillingUnitPrice, SlitterMaxFlow,
    PrintMinBilling, PrintMinBillingBelow, PrintMinBillingUnit,
    LaminateMinBilling, LaminateMinBillingBelow, LaminateMinBillingUnit,
    LaminateAddRate, LaminateAddDisplay,
    ProcessingMinEstimate, NewSheetUnitPriceBase,
    DeliverySlipOutDiv, DeliverySlipIssueDiv, SpecialSlipDiv,
    NightLoadDiv, SizePrintDiv,
    DeliveryCalcOutDiv, DeliveryCalcIssueDiv, DeliveryCalcOutDiv2,
    DeliveryCalcAddressee, DeliveryCalcSender, DeliveryCalcZipCd,
    DeliveryCalcAddr1, DeliveryCalcAddr2, DeliveryCalcAddr3, DeliveryCalcAddr4,
    BillingCd, BillingName, ReceiptCd, ReceiptName, CreditMgmtArCd,
    BillingClosingDay1, BillingPrintDiv, BillingSealDiv, ElectronicBilling,
    BillingAddressee, BillingSender, BillingZipCd,
    BillingAddr1, BillingAddr2, BillingAddr3, BillingAddr4,
    RemittanceName, BankCd, BankBranchCd, RemittanceAccount,
    TempAccountDiv, MainAccountRegDate, Drawer,
    CollectionDiv, CollectionPlannedDay, BillRotationDiv,
    ReceiptAddressee, ReceiptSender, ReceiptZipCd,
    ReceiptAddr1, ReceiptAddr2, ReceiptAddr3, ReceiptAddr4,
    CollectionNote,
    LogisticsGroupCd, LogisticsGroupName,
    DeliveryDept, DeliveryContact, DeliveryContactTitle,
    TruckLengthLimit, DeliveryTimeFrom, DeliveryTimeTo, PlannedShipTime,
    SupplierPattern, SubcontractPriceDiv, SupplyPostingDiv,
    PurchaseFractionDiv, PurchaseTaxFractionDiv, PurchaseTaxCd,
    SupplierCalendarCd, SupplyConsignDiv, PurchaseLotSplitDiv, PurchasePostingDiv,
    PaidSupplyFractionDiv, PaidSupplyTaxCd, PaidSupplyAmountFractionDiv,
    PaidSupplyTaxFractionDiv, PaidSupplyTaxCalcDiv,
    FscCertificationDiv,
    PaymentScheduleCd, PaymentCd, PaymentScheduleDeptCd,
    PaymentClosingDay1, PaymentTaxFractionDiv, PaymentScheduleDelayDays,
    IsDeleted, CreateDate, Creator)
  VALUES (
    NEWID(), 'BP-DEMO-ALL', N'株式会社 デモ商事', N'デモ商事', '001', 1,
    'STD-DEMO-001', '9876543210123', '01', '13104', 'DZ0001234',
    '106-6108', N'東京都', N'港区', N'六本木1-6-1', N'泉ガーデンタワー 8F',
    '03-5775-1234', '03-5775-1235', '03',
    'S-1001', 'B-2001',
    1, 1, 1, 1, 1,  -- Customer/AR/Billing/Receipt/Delivery
    1, 1, 1, 1,     -- Supplier/AP/PaySched/Payment
    1, 1, 1, 1,     -- CreditMgmt/Maker/PaidSupply/Rebuy
    1, 1, 1,        -- Subcontract/SupplyPriceChange/DeliveryConfirm
    1, 1, 1,        -- PurchaseTaxPrio/PaidSupplyTaxPrio/PaidEachTime
    1, 1, 1, 1,     -- PaymentTaxCalc/BatchPaySched/Gifu/McTransfer
    'A1', 'B2', 'C3', 'D4', 'E5',
    'F6', 'G7', 'H8', 'I9', 'J0',
    'SA01', 'SA02', 'SA03',
    'BP-DEMO-ALL', '01',
    'AR-DEMO-001', 'CM-DEMO-001',
    N'購買部', N'山田 太郎', N'部長',
    '01', '01', '02',
    '01', '02', '01',
    '01', '01', 12.50, 1500.00,
    '01', 5000.00, 'JPY',
    '01', 3000.00, 'JPY',
    5.50, '01',
    '01', '01',
    '01', '02', '01',
    '02', '01',
    '01', '02', '01',
    N'株式会社 デモ商事 茨城物流センター 御中', N'当社営業部', '305-0031',
    N'茨城県', N'つくば市', N'吾妻 2-2-2', N'デモ商事 茨城物流センター',
    'BL-DEMO-001', N'株式会社 デモ商事 経理部',
    'RC-DEMO-001', N'株式会社 デモ商事 受領担当',
    'CM-AR-DEMO-001',
    25, '01', '01', '01',
    N'株式会社 デモ商事 経理部 御中', N'当社経理部', '106-6108',
    N'東京都', N'港区', N'六本木 1-6-1', N'泉ガーデンタワー 8F 経理部',
    N'カ）デモシヨウジ', '0001', '003', '1234567',
    '01', '2024-04-01', N'三菱UFJ銀行 六本木支店',
    '01', 25, '01',
    N'株式会社 デモ商事 経理部 受領担当 御中', N'当社経理部', '106-6108',
    N'東京都', N'港区', N'六本木 1-6-1', N'泉ガーデンタワー 8F',
    N'毎月25日締め、翌月末日振込（手数料先方負担）',
    'LG-001', N'関東圏物流グループ',
    N'物流センター', N'佐藤 花子', N'マネージャー',
    10.50, '09:00', '17:00', '15:00',
    '01', '01', '01',
    '01', '01', 'JP10',
    'JP-CAL', '01', '01', '01',
    '01', 'JP10', '01',
    '01', '01',
    'FSC',
    'PS-DEMO-001', 'PY-DEMO-001', 'PSD-DEMO-001',
    20, '01', 30,
    0, GETDATE(), 'demo-init');

  -- 結果サマリー
  DECLARE @cnt INT, @flgOn INT;
  SELECT @cnt = COUNT(*) FROM T_WebBusinessPartner WHERE BpCd = 'BP-DEMO-ALL';
  SELECT @flgOn =
      CAST(CustomerFlg AS INT)+CAST(AccountsReceivableFlg AS INT)+CAST(BillingFlg AS INT)
    + CAST(ReceiptFlg AS INT)+CAST(DeliveryFlg AS INT)+CAST(SupplierFlg AS INT)
    + CAST(AccountsPayableFlg AS INT)+CAST(PaymentScheduleFlg AS INT)+CAST(PaymentFlg AS INT)
    + CAST(CreditMgmtFlg AS INT)+CAST(MakerFlg AS INT)+CAST(PaidSupplyFlg AS INT)
    + CAST(RebuyObligationFlg AS INT)+CAST(SubcontractTargetFlg AS INT)
    + CAST(SupplyPriceChangeAllowFlg AS INT)+CAST(DeliveryConfirmFlg AS INT)
    + CAST(PurchaseTaxPriorityFlg AS INT)+CAST(PaidSupplyTaxPriorityFlg AS INT)
    + CAST(PaidEachTimeFlg AS INT)+CAST(PaymentTaxCalcFlg AS INT)
    + CAST(BatchPaymentScheduleFlg AS INT)+CAST(GifuInterfaceFlg AS INT)
    + CAST(McTransferFlg AS INT)
  FROM T_WebBusinessPartner WHERE BpCd = 'BP-DEMO-ALL';

  PRINT '--- 結果 ---';
  PRINT 'BP-DEMO-ALL 行数: ' + CAST(@cnt AS nvarchar(5));
  PRINT 'フラグ ON 数  : ' + CAST(@flgOn AS nvarchar(5)) + ' / 23';
  COMMIT;
  PRINT 'COMMIT done';
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK;
  PRINT 'ERROR: ' + ERROR_MESSAGE();
  THROW;
END CATCH;
