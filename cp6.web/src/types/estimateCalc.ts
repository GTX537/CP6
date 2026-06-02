// MSBBPA010 見積計算書 类型定义
// 对应后端 CP6.Entity/DTOs/EstimateCalcDto.cs

/** 操作种别（与后端 OperationType 枚举对齐） */
export enum OperationType {
  New = 10,
  Edit = 20,
  Delete = 30,
  View = 40,
  Copy = 50,
}

/** 字段控制状态：E=Editable, RO=ReadOnly, D=Disabled, R=Required */
export type FieldState = 'E' | 'RO' | 'D' | 'R'

/** 工程明细行 */
export interface EstimateCalcProcessDto {
  id?: string
  qtnCalcNo?: string
  seqNo: number
  processCd?: string
  processName?: string
  taskCd?: string
  taskName?: string
  wgCd?: string
  mfgLocation?: string
  spec1Label?: string
  spec1Val?: string
  spec2Label?: string
  spec2Val?: string
  spec3Label?: string
  spec3Val?: string
  spec4Label?: string
  spec4Val?: string
  spec5Label?: string
  spec5Val?: string
  spec6Label?: string
  spec6Val?: string
  spec7Label?: string
  spec7Val?: string
  plateNo?: string
  procNote1?: string
  procNote2?: string
}

/** 見積計算書 主档（含工程明细） */
export interface EstimateCalcDto {
  id?: string
  qtnCalcNo?: string
  qtnCalcNoMain?: number
  qtnCalcNoBranch?: number
  refQtnCalcNo?: string

  // 基本信息
  proCd?: string
  qtnDate?: string
  qtnBaseCd?: string
  orderBaseCd?: string
  staffCd?: string
  customerCd?: string
  projectNoParent?: string
  projectNoChild?: string
  projectNoMaterial?: string
  orderType?: string

  // 商品分类
  productCategoryBig?: string
  productCategoryMid?: string
  productCategorySml?: string
  customerProductName1?: string
  customerProductName2?: string

  // 订货信息
  orderQty?: number
  orderYm?: string
  parentChildDiv?: string
  fscProductDiv?: string
  fscMaterialDiv?: string

  // 材质 / 印刷 / 成型
  sheetFlute?: string
  paperCdF?: string
  paperCdC?: string
  paperCdB?: string
  printCdF?: string
  printCdC?: string
  printCdB?: string
  embossCdF?: string
  embossCdC?: string
  embossCdB?: string
  patternCntF?: number
  patternCntB?: number
  sheetPrint?: string

  // 刃・シート尺寸
  bladeWidth?: number
  bladeFlow?: number
  gutterFb?: number
  gutterLr?: number
  sheetDimW?: number
  sheetDimF?: number

  // 最终工序・形状
  finalMachineProc?: string
  productShape1?: string
  productShape2?: string
  distDiv?: string
  recyclePayment?: string
  idMark?: string
  adShape?: string

  // 战略区分 1..10（bool）
  strategicDivs?: boolean[]

  // 备注
  printNote?: string
  mfgNote?: string
  slipNote?: string
  deliveryNote?: string
  shipNote1?: string
  shipNote2?: string

  // 見積り数量 1..8
  estimateQtys?: number[]
  // パレット数 1..8
  palletCnts?: number[]
  // 提案ロット 1 / 2
  proposalLot1?: number
  proposalLot2?: number
  unit?: string
  decidedQty?: number

  // 見積区分 / 面積 / 单价
  qtnDiv?: string
  estimateSqm?: number
  standardUnitPrice?: number
  estimateUnitPrice?: number
  confirmedUnitPrice?: number

  // 工程明细
  processes?: EstimateCalcProcessDto[]

  // 乐观锁 / 软删
  rowVersion?: string
  isDeleted?: boolean
  creator?: string
  createDate?: string
  modifier?: string
  modifyDate?: string
}

/** 查询条件 */
export interface EstimateCalcQuery {
  page: number
  pageSize: number
  qtnCalcNo?: string
  customerCd?: string
  baseCd?: string
  dateFrom?: string
  dateTo?: string
  sortField?: string
  sortOrder?: string
}

/** 列表行（与后端 EstimateCalcListItem 对齐） */
export interface EstimateCalcListItem {
  qtnCalcNo: string
  qtnDate: string
  customerCd?: string
  customerProductName1?: string
  orderQty?: number
  qtnBaseCd?: string
  staffCd?: string
  qtnDiv?: string
  estimateUnitPrice?: number
  createDate?: string
  modifyDate?: string
}

/** 計算結果明细 */
export interface EstimateCalcAmountRow {
  index: number
  qty: number
  unitPrice: number
  amount: number
}

/** 計算結果（后端 Calculate 响应） */
export interface EstimateCalcResult {
  estimateSqm: number
  standardUnitPrice: number
  estimateUnitPrice: number
  confirmedUnitPrice: number
  amountRows: EstimateCalcAmountRow[]
  notes: string[]
}

/** 通用码主档 */
export interface MasterGenericCode {
  groupCode: string
  code: string
  name: string
  attr1?: string
  attr2?: string
  num1?: number
  num2?: number
  sortOrder?: number
}

/** 拠点主档 */
export interface MasterBase {
  baseCd: string
  baseName: string
  isAdminBase: boolean
  discountThreshold?: number
  sortOrder?: number
}

/** 担当者主档 */
export interface MasterStaff {
  staffCd: string
  staffName: string
  baseCd: string
  sysUserId?: string
  sortOrder?: number
}

/** 后端统一响应 */
export interface ApiResult<T = any> {
  code: number
  message: string
  data: T
}
