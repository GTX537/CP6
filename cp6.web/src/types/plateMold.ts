// MSBBPA140/150 Web 版型/木型マスタ 类型定义

import type { ApiResult } from '@/types/estimateCalc'

/** 操作種別（5 種） */
export enum PlateMoldOperationType {
  Register = 10,
  Revise = 20,
  Edit = 30,
  Delete = 40,
  View = 50,
}

export interface PlateMoldDto {
  wdPtnNo?: string
  wdRev: number

  baseCd: string
  baseName?: string
  decisionEstimateNo: string
  vrsnName: string
  earningsCd?: string
  arrangeNo?: string
  stDate: string
  endDate: string
  customerCd: string
  customerName?: string
  representativeProductCd: string
  representativeProductName1?: string
  representativeProductName2?: string
  newVerCd?: string
  processCd?: string
  typeClass?: string
  endPlaceCd?: string
  achieveDate?: string

  sheetFlute?: string
  paperCdF?: string
  printCdF?: string
  embossCdF?: string
  makerCdF?: string
  paperCdC?: string
  printCdC?: string
  embossCdC?: string
  makerCdC?: string
  paperCdB?: string
  printCdB?: string
  embossCdB?: string
  makerCdB?: string

  extractionDirect?: string
  duplicatePlateFlg: boolean
  sheetWidth: number
  sheetFlow: number
  bladeWidth: number
  bladeFlow: number
  compositionQty: number
  mfgQty: number
  colorQty: number
  bookQty: number

  flexoThick?: number
  cylinderSize?: number

  supplierCd: string
  supplierName?: string
  processDestination?: string
  processDestinationName?: string
  deliveryDate?: string
  arrivalActualDate?: string

  estimateAmount: number
  decisionAmount: number
  purchaseAmount: number

  salesDate?: string
  strippingCd?: string
  standingCd?: string
  hammerCd?: string
  oversightCd?: string

  placeCd?: string
  shelfLineCd?: string
  wdQty: number
  limitWdQty: number
  dispScheduleDate?: string
  returnScheduleDate?: string
  returnDate?: string
  returnReason?: string
  memo?: string
  salesNote?: string
  dispActualDate?: string

  // 添付情報（8）
  atachInfoSheetFront: boolean
  atachInfoSheetBack: boolean
  atachInfoActual: boolean
  atachInfoBaseplate: boolean
  atachInfoPositive: boolean
  atachInfoNegative: boolean
  atachInfoMo: boolean
  atachInfoFd: boolean

  // 必要物
  needDraft: number
  needMylar: number
  needGalley: number
  needProof: number
  needBlueprint: number
  needComp: number
  needDesignSheet: number
  needOtherName?: string
  needOther?: string

  status: number
  mcTransferFlg: boolean
  mcOrderNo?: string
  mcOrderDetailNo?: string

  rowVersion?: string
}

export interface PlateMoldHistoryItemDto {
  wdPtnNo: string
  wdRev: number
  stDate: string
  endDate: string
  supplierCd?: string
  supplierName?: string
  wdQty: number
  dispScheduleDate?: string
  returnScheduleDate?: string
  returnDate?: string
  returnReason?: string
}

export interface PlateMoldQueryDto {
  baseCd?: string
  staffCd?: string
  stDateFrom?: string
  stDateTo?: string
  arrangeNo?: string
  projectNo?: string
  wdPtnNoFrom?: string
  wdPtnNoTo?: string
  vrsnName?: string
  typeClass?: string
  newVerCd?: string
  customerCd?: string
  representativeProductCd?: string
  processCd?: string
  wdQtyFrom?: number
  wdQtyTo?: number
  supplierCd?: string
  limitWdQtyFrom?: number
  limitWdQtyTo?: number
  placeCd?: string
  shelfLineCd?: string
  achieveDateFrom?: string
  achieveDateTo?: string
  arrivalDateFrom?: string
  arrivalDateTo?: string
  dispScheduleFrom?: string
  dispScheduleTo?: string
  returnScheduleFrom?: string
  returnScheduleTo?: string
  returnDateFrom?: string
  returnDateTo?: string
  onlyLatestRev?: boolean
  page?: number
  pageSize?: number
  maxRows?: number
  sortField?: string
  sortOrder?: string
}

export interface PlateMoldListItemDto {
  rowNo: number
  issue?: boolean
  wdPtnNo: string
  vrsnName?: string
  wdRev: number
  typeClass?: string
  customerCd?: string
  customerName?: string
  representativeProductCd?: string
  representativeProductName?: string
  wdQty: number
  limitWdQty: number
  achieveDate?: string
  supplierCd?: string
  supplierName?: string
  arrivalActualDate?: string
  dispScheduleDate?: string
  returnScheduleDate?: string
  returnDate?: string
  returnReason?: string
  projectNo?: string
  projectName?: string
  processCd?: string
  processName?: string
  placeCd?: string
  placeName?: string
  shelfLineCd?: string
  shelfLineName?: string
  newVerCd?: string
  endPlaceCd?: string
  endPlaceName?: string
  bladeWidth?: number
  bladeFlow?: number
  compositionQty?: number
  mfgQty?: number
  colorQty?: number
  strippingCd?: string
  standingCd?: string
  hammerCd?: string
  oversightCd?: string
  memo?: string
}

export interface PlateMoldOrderLinkResultDto {
  webOrderNo: string
  webOrderDetailNo: number
  arrangeNo: string
}

export interface PlateMoldPeApiResultDto {
  ok: boolean
  message?: string
}

export type { ApiResult }
