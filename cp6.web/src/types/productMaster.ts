// MSBBPA050 / MSBBPA060 Web 製品マスタ 类型定义
// 对应后端 CP6.Entity/DTOs/ProductDto.cs

import type { ApiResult } from '@/types/estimateCalc'

/** 操作种别（与后端 ProductOperationType 枚举对齐） */
export enum ProductOperationType {
  New = 10,
  Edit = 20,
  Delete = 30,
  View = 40,
  Copy = 50,
}

/** 第1页 部材一覧 1 行 */
export interface ProductMemberDto {
  rowNo: number
  productCd?: string
  estimateCalcNo?: string
  quotationNo?: string
  projectNoParent?: string
  projectNoChild?: string
  projectNoMaterial?: string
  customerProductName1?: string
  customerProductName2?: string
  /** 0=未作成 / 1=承認待ち / 9=承認済 or mc転送済 */
  status: number
  wfApproved: boolean
  masterLinked: boolean
}

/** 第2页 基本情報（70+ フィールド） */
export interface ProductBasicInfoDto {
  customerCd: string
  customerName?: string
  setProductCd: string
  setProductName?: string
  parentChildDiv: string

  customerItemName1?: string
  customerItemName2?: string
  customerPartNo?: string
  cpItemName1?: string
  cpItemName2?: string
  janCode?: string
  setRatio: number

  productUsage?: string
  distributionDiv?: string
  confidentialInfo?: string
  seizureDiv?: string
  importanceDiv?: string
  mChange?: string
  qualityDiv?: string
  shipInspection?: string
  productShape?: string
  unescoMark?: string
  origamiMark?: string
  fourMContract?: string
  tkpWrinkleStd?: string
  foodSafety?: string
  adShape?: string

  /** 戦略商品区分 01〜10 (10 个 bool) */
  strategicDivs: boolean[]

  fscProductDiv?: string
  fscMaterialDiv?: string
  fscManagementNo?: string
  recyclingPayment?: string
  idMark?: string

  paperUsageG?: number
  plasticUsageG?: number
  glassUsageG?: number
  petUsageG?: number
  packPaperUsageG?: number
  packPlasticUsageG?: number

  designProposalNo?: string
  orderType?: string
  sheetFlute?: string

  paperCdF?: string
  printCdF?: string
  embossCdF?: string
  makerCdF?: string
  paperCdC?: string
  printCdC?: string
  embossCdC?: string
  paperCdB?: string
  printCdB?: string
  embossCdB?: string
  makerCdB?: string
  sheetPrint?: string

  bladeWidth?: number
  bladeFlow?: number
  gutterFb?: number
  gutterLr?: number
  sheetDimW?: number
  sheetDimF?: number
  finalMachineProcess?: string

  printNote?: string
  mfgNote?: string
  slipNote?: string
  deliveryNote?: string
  shipNote1?: string
  shipNote2?: string

  productCatBig?: string
  productCatMid?: string
  productCatSml?: string

  salesPriceDiv: string
  qtyUnit?: string
  unitPriceUnit?: string
  purchaseVendor?: string
  freightBilling: string
  fixedShipment?: string
  deliveryReserve?: number
  salesSample?: number

  /** 受注後変更不可（数量単位/単価単位）— サーバ判定 */
  isReadOnlyByOrder?: boolean
}

/** 第3页 工程行 */
export interface ProductProcessDto {
  taskCd: string
  processCd: string
  topItemCd?: string
  topBranch1?: string
  topBranch2?: string
  topBranch3?: string
  itemCd?: string
  branch1?: string
  branch2?: string
  branch3?: string
  wgCd?: string
  machineOrVendor?: string
  machineFixedFlg: boolean
  cpDeliveryDiv?: string

  /** 工程仕様 01〜10 */
  specs: (string | undefined)[]

  plateNo1?: string
  plateNo2?: string
  plateNo3?: string
  consumable1?: string
  consumable2?: string
  consumable3?: string

  purchasePrice?: number
  fixedPrice?: number
  lossRate?: number
  machineCount?: number
  leadTime?: number
  processNote1?: string
  processNote2?: string
  storageDest?: string
  sortOrder: number

  /** 製造順優先項目 1〜8 */
  manufOrderPrios: (string | undefined)[]
}

/** 第4页 材料行 */
export interface ProductMaterialDto {
  processCd: string
  materialCd: string
  materialTypeDiv: string
  itemCd?: string
  branch1?: string
  branch2?: string
  branch3?: string
  supplyDiv?: string
  supplyPrice?: number
  sortOrder: number
}

/** 第5页 ロット別単価行 */
export interface ProductLotPriceDto {
  detailNo: number
  itemCd?: string
  branch1?: string
  branch2?: string
  branch3?: string
  currentPriceDate?: string
  newPriceDate?: string
  currentBranchDate?: string
  newBranchDate?: string
  lotQty: number
  currentSetPrice?: number
  newSetPrice?: number
  currentUnitPrice?: number
  newUnitPrice?: number
  currentBranchPrice?: number
  newBranchPrice?: number
  purchasePrice?: number
  priceApplyBasis?: string
}

/** 第3页 Popup 連産品行 */
export interface ProductCoProductDto {
  processCd: string
  rowNo: number
  coProductName?: string
  qtyRatio: number
  nextProcessCd?: string
}

/** 製品マスタ全 5 表统一 DTO */
export interface ProductDto {
  productCd?: string
  itemCd?: string
  branch1?: string
  branch2?: string
  branch3?: string
  members: ProductMemberDto[]
  basicInfo: ProductBasicInfoDto
  processes: ProductProcessDto[]
  coProducts: ProductCoProductDto[]
  materials: ProductMaterialDto[]
  lotPrices: ProductLotPriceDto[]
  status: number
  wfApprovalFlg: boolean
  mcTransferFlg: boolean
  rowVersion?: string
}

/** MSBBPA060 一覧行 */
export interface ProductListItemDto {
  productCd: string
  setProductCd?: string
  setProductName?: string
  customerCd?: string
  customerName?: string
  customerItemName1?: string
  customerItemName2?: string
  projectNoParent?: string
  projectNoChild?: string
  quotationNo?: string
  estimateCalcNo?: string
  status: number
  wfApprovalFlg: boolean
  mcTransferFlg: boolean
  modifyDate?: string
}

/** MSBBPA060 检索条件 */
export interface ProductQuery {
  page: number
  pageSize: number
  productCdFrom?: string
  productCdTo?: string
  customerCd?: string
  setProductCd?: string
  projectNoParent?: string
  projectNoChild?: string
  quotationNo?: string
  estimateCalcNo?: string
  customerItemName1?: string
  customerItemName2?: string
  designProposalNo?: string
  modifyDateFrom?: string
  modifyDateTo?: string
  statuses?: number[]
}

/** 仕掛チェック 結果（10 章） */
export interface WipCheckResultDto {
  /** 0=問題なし / 1=警告 / 2=エラー(確定済) / 3=エラー(指図済) */
  level: number
  wipHandleNumbers: string[]
  msgId?: string
  message?: string
}

/** ProductMasterView 打开时由父组件/URL 传入的初始参数 */
export interface ProductMasterOpenParams {
  op: 'new' | 'view' | 'edit' | 'copy' | 'delete'
  productCd?: string
  /** 由 MSBBPA040 御見積書一覧跳过来时填入：从御見積書 NO 引入部材一覧 */
  quotationNo?: string
}

export type {
  ApiResult, // re-export for convenience
}
