// MSBBPA110 / 120 — Web 取引先マスタ 型定義
import type { ApiResult } from '@/types/estimateCalc'

export enum BpOperationType {
  PreRegister = 5,
  Register = 10,
  Edit = 20,
  Delete = 30,
  View = 40,
}

/** 1 取引先 1 個の DTO（全 Tab 字段を統合） */
export interface BusinessPartnerDto {
  bpCd: string
  bpName: string
  bpAbbrev?: string
  baseCd: string
  status: number

  // 法人 / 公的番号
  stdCoCd?: string
  ein?: string
  einType?: string
  localPublicCd?: string
  denzaiNo?: string

  // 住所
  zipCd?: string
  addr1?: string
  addr2?: string
  addr3?: string
  addr4?: string
  tel?: string
  fax?: string
  areaCd?: string
  salesStaffCd?: string
  businessStaffCd?: string

  // 9 個の属性 FLG
  customerFlg: boolean
  accountsReceivableFlg: boolean
  billingFlg: boolean
  receiptFlg: boolean
  deliveryFlg: boolean
  supplierFlg: boolean
  accountsPayableFlg: boolean
  paymentScheduleFlg: boolean
  paymentFlg: boolean
  creditMgmtFlg: boolean
  makerFlg: boolean
  paidSupplyFlg: boolean
  rebuyObligationFlg: boolean

  // 取引先分類 / 販売分析
  bpClass01?: string
  bpClass02?: string
  bpClass03?: string
  bpClass04?: string
  bpClass05?: string
  bpClass06?: string
  bpClass07?: string
  bpClass08?: string
  bpClass09?: string
  bpClass10?: string
  salesAnalysis1?: string
  salesAnalysis2?: string
  salesAnalysis3?: string

  // 得意先 Tab
  parentCustomerCd?: string
  userConverterDiv?: string
  accountsReceivableCd?: string
  creditMgmtCd?: string
  customerDept?: string
  customerContact?: string
  customerContactTitle?: string
  recyclingTarget?: string
  salesPostingDiv?: string
  sheetSalesCalcMethod?: string
  salesCalcDivM2?: string
  salesCalcDivPiece?: string
  fractionCalcDiv?: string
  fullSheetSalesDiv?: string
  slitterBillingDiv?: string
  slitterBillingUnitPrice?: number
  slitterMaxFlow?: number
  printMinBilling?: string
  printMinBillingBelow?: number
  printMinBillingUnit?: string
  laminateMinBilling?: string
  laminateMinBillingBelow?: number
  laminateMinBillingUnit?: string
  laminateAddRate?: number
  laminateAddDisplay?: string
  processingMinEstimate?: string
  newSheetUnitPriceBase?: string
  deliverySlipOutDiv?: string
  deliverySlipIssueDiv?: string
  specialSlipDiv?: string
  nightLoadDiv?: string
  sizePrintDiv?: string
  deliveryCalcOutDiv?: string
  deliveryCalcIssueDiv?: string
  deliveryCalcOutDiv2?: string
  deliveryCalcAddressee?: string
  deliveryCalcSender?: string
  deliveryCalcZipCd?: string
  deliveryCalcAddr1?: string
  deliveryCalcAddr2?: string
  deliveryCalcAddr3?: string
  deliveryCalcAddr4?: string

  // 売掛先
  billingCd?: string
  billingName?: string

  // 請求先
  receiptCd?: string
  receiptName?: string
  creditMgmtArCd?: string
  billingClosingDay1?: number
  billingPrintDiv?: string
  billingSealDiv?: string
  electronicBilling?: string
  billingAddressee?: string
  billingSender?: string
  billingZipCd?: string
  billingAddr1?: string
  billingAddr2?: string
  billingAddr3?: string
  billingAddr4?: string

  // 入金先
  remittanceName?: string
  bankCd?: string
  bankBranchCd?: string
  remittanceAccount?: string
  tempAccountDiv?: string
  mainAccountRegDate?: string
  drawer?: string
  collectionDiv?: string
  collectionPlannedDay?: number
  billRotationDiv?: string
  receiptAddressee?: string
  receiptSender?: string
  receiptZipCd?: string
  receiptAddr1?: string
  receiptAddr2?: string
  receiptAddr3?: string
  receiptAddr4?: string
  collectionNote?: string

  // 納品先
  logisticsGroupCd?: string
  logisticsGroupName?: string
  deliveryDept?: string
  deliveryContact?: string
  deliveryContactTitle?: string
  truckLengthLimit?: number
  deliveryTimeFrom?: string
  deliveryTimeTo?: string
  plannedShipTime?: string

  // 発注先
  supplierPattern?: string
  subcontractTargetFlg: boolean
  subcontractPriceDiv?: string
  supplyPostingDiv?: string
  supplyPriceChangeAllowFlg: boolean
  deliveryConfirmFlg: boolean
  purchaseFractionDiv?: string
  purchaseTaxFractionDiv?: string
  purchaseTaxCd?: string
  purchaseTaxPriorityFlg: boolean
  supplierCalendarCd?: string
  supplyConsignDiv?: string
  purchaseLotSplitDiv?: string
  purchasePostingDiv?: string
  paidSupplyFractionDiv?: string
  paidSupplyTaxCd?: string
  paidSupplyTaxPriorityFlg: boolean
  paidSupplyAmountFractionDiv?: string
  paidSupplyTaxFractionDiv?: string
  paidSupplyTaxCalcDiv?: string
  fscCertificationDiv?: string

  // 買掛先 / 支払予定管理先 / 支払先
  paymentScheduleCd?: string
  paymentCd?: string
  paymentScheduleDeptCd?: string
  paidEachTimeFlg: boolean
  paymentClosingDay1?: number
  paymentTaxCalcFlg: boolean
  paymentTaxFractionDiv?: string
  batchPaymentScheduleFlg: boolean
  paymentScheduleDelayDays?: number

  gifuInterfaceFlg: boolean
  mcTransferFlg: boolean
  rowVersion?: string
}

// PA120 query / list
export interface BpQueryDto {
  includeCustomer?: boolean
  includeAccountsReceivable?: boolean
  includeBilling?: boolean
  includeReceipt?: boolean
  includeDelivery?: boolean
  includeCreditMgmt?: boolean
  includeSupplier?: boolean
  includeAccountsPayable?: boolean
  includePaymentSchedule?: boolean
  includePayment?: boolean
  includeMaker?: boolean

  registeredDateFrom?: string
  registeredDateTo?: string
  bpCd?: string
  bpName?: string
  ein?: string
  einType?: string
  stdCoCd?: string
  zipCd?: string
  addr?: string
  tel?: string
  fax?: string
  areaCd?: string
  salesWg?: string
  salesStaffCd?: string
  businessWg?: string
  businessStaffCd?: string
  bpClass01?: string
  bpClass02?: string
  bpClass03?: string
  bpClass04?: string
  bpClass05?: string
  bpClass06?: string
  bpClass07?: string
  bpClass08?: string
  bpClass09?: string
  bpClass10?: string
  includePreRegistered?: boolean
  includeRegistered?: boolean
  page?: number
  pageSize?: number
  maxRows?: number
}

export interface BpListItemDto {
  rowNo: number
  status: number
  bpCd?: string
  bpName?: string
  bpAbbrev?: string
  salesWg?: string
  salesWgName?: string
  salesStaffCd?: string
  salesStaffName?: string
  businessWg?: string
  businessWgName?: string
  businessStaffCd?: string
  businessStaffName?: string
  ein?: string
  stdCoCd?: string
  addr1?: string
  addr2?: string
  addr3?: string
  addr4?: string
  customerFlg: boolean
  accountsReceivableFlg: boolean
  billingFlg: boolean
  receiptFlg: boolean
  deliveryFlg: boolean
  creditMgmtFlg: boolean
  supplierFlg: boolean
  accountsPayableFlg: boolean
  paymentScheduleFlg: boolean
  paymentFlg: boolean
  makerFlg: boolean
  createDate?: string
  creator?: string
  creatorName?: string
}

export type { ApiResult }
