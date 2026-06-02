// MSBBPA030 御見積書 类型定义
// 对应后端 CP6.Entity/DTOs/QuotationDto.cs

import type { ApiResult } from '@/types/estimateCalc'

/** 操作种别（与后端 QuotationOperationType 枚举对齐） */
export enum QuotationOperationType {
  New = 10,
  Edit = 20,
  Delete = 30,
  View = 40,
  Copy = 50,
  Confirm = 60,
  CancelConfirm = 70,
}

/** 御見積書 ⇄ 見積計算書 关联行（中间表 1 行 = 列表中的「使用」✓） */
export interface QuotationCalcDto {
  qtnCalcNo: string
  estimateCheckFlg: number
  estimateCheckDate?: string
  masterConfirmFlg: number
  masterConfirmDate?: string

  /** 只读展示字段（从 EstimateCalc JOIN 过来） */
  qtnCalcDate?: string
  customerProductName1?: string
  customerProductName2?: string
  estimateQty?: number
  confirmedUnitPrice?: number
  unit?: string
  amount?: number
  qtnDiv?: string
}

/** 御見積書 打印用明细（可编辑） */
export interface QuotationDetailDto {
  detailNo: number
  itemName1?: string
  itemName2?: string
  quantity?: number
  unitPrice?: number
  unit?: string
  amount?: number
  printTotalFlg: boolean
  /** 若为「使用✓」关联自动追加，则会回填原 見積計算書No */
  qtnCalcNo?: string
}

/** 御見積書 主档 DTO（扁平结构，包含 主表 + calcs + details） */
export interface QuotationDto {
  // 主键
  qtnNo?: string
  refQtnNo?: string

  // ヘッダー
  baseCd: string
  staffCd: string

  // 見積情報
  customerCd: string
  customerName?: string
  projectNoParent?: string
  projectNoChild?: string
  projectNoMaterial?: string

  // FSC
  fscMgmtNo?: string
  fscChecklistDate?: string

  // 御見積書ヘッダー
  contactPerson?: string
  deliveryLocation?: string
  deliveryDeadline?: string
  freight?: string
  paymentCondition?: string
  validityPeriod?: string

  /** 備考 01..15（15 行） */
  qtnNotes: (string | null | undefined)[]

  // 提出用
  dimensionPrint?: string
  /** 計算書メモ 01..08（8 行） */
  calcNotes: (string | null | undefined)[]

  // 発行 / 金額
  qtnIssueDate?: string
  calcIssueDate?: string
  totalAmount?: number
  printTotalFlg: boolean

  // 状态
  estimateCheckFlg: number
  estimateCheckDate?: string
  masterConfirmFlg: number
  masterConfirmDate?: string

  // メモ
  memo1?: string
  memo2?: string
  memo3?: string

  // 关联
  calcs: QuotationCalcDto[]
  details: QuotationDetailDto[]

  // 乐观锁
  rowVersion?: string

  // 审计
  createDate?: string
  modifyDate?: string
}

/** 一覧行（MSBBPA040） */
export interface QuotationListItem {
  qtnNo: string
  qtnIssueDate?: string
  baseCd?: string
  staffCd?: string
  staffName?: string
  customerCd?: string
  customerName?: string
  projectNoParent?: string
  projectNoChild?: string
  projectNoMaterial?: string
  itemName1?: string
  itemName2?: string
  firstQuantity?: number
  firstUnitPrice?: number
  firstAmount?: number
  totalAmount?: number
  estimateCheckFlg: number
  masterConfirmFlg: number
  /** 综合状态：未承認 / 承認済 / 見積確定済 */
  status?: string
}

/** 查询条件（MSBBPA040 / 后端 QuotationQuery） */
export interface QuotationQuery {
  page: number
  pageSize: number
  baseCd?: string
  staffCd?: string
  issueDateFrom?: string
  issueDateTo?: string
  qtnNoFrom?: string
  qtnNoTo?: string
  customerCd?: string
  projectNoParent?: string
  projectNoChild?: string
  projectNoMaterial?: string
  customerProductName1?: string
  customerProductName2?: string
  /** ["0","9","C"] */
  statuses?: string[]
  sortField?: string
  sortOrder?: string
}

/** 関連見積計算書 候选 */
export interface QuotationCalcCandidate {
  qtnCalcNo: string
  qtnCalcDate?: string
  customerProductName1?: string
  customerProductName2?: string
  estimateQty?: number
  confirmedUnitPrice?: number
  unit?: string
  amount?: number
  qtnDiv?: string
  /** 已在当前御見積書中关联 → 默认勾选「使用」 */
  isLinked: boolean
}

/** 確定登録 请求体 */
export interface QuotationConfirmRequest {
  qtnCalcNos: string[]
  rowVersion?: string
}

/** 発行 请求体 */
export interface QuotationIssueRequest {
  issueQuotation: boolean
  issueSubmitCalc: boolean
  issueCalc: boolean
}

/** Re-export ApiResult so view files 只需 import from '@/types/quotation' */
export type { ApiResult }
