// MSBBPA130 Web シート単価マスタ 类型定义

import type { ApiResult } from '@/types/estimateCalc'

/** 取込区分（後端 SheetPriceImportDiv と対応） */
export enum SheetPriceImportDiv {
  Standard = 1,    // シート単価
  Estimate = 2,    // シート単価(見積用)
}

export interface SheetUnitPriceDto {
  rowNo: number
  selected: boolean

  revisionDate: string
  baseCd: string
  baseName?: string
  salesStaffCd?: string
  salesStaffName?: string
  customerCd: string
  customerName?: string
  sheetFlute: string
  paperCdF: string
  printCdF: string
  embossCdF: string
  paperCdC: string
  printCdC: string
  embossCdC: string
  paperCdB: string
  printCdB: string
  embossCdB: string
  unitPrice: number

  sourceLine?: number
}

export interface SheetPriceImportResultDto {
  ok: boolean
  rows: SheetUnitPriceDto[]
  errors: string[]
  errorStep?: number
  hasDuplicates: boolean
}

export interface SheetPriceBatchUpdateDto {
  importDiv: SheetPriceImportDiv
  rows: SheetUnitPriceDto[]
}

export interface SheetPriceQueryDto {
  baseDate: string
  baseCd: string
  importDiv?: SheetPriceImportDiv
  customerCd?: string
  salesStaffCd?: string
}

export type { ApiResult }
