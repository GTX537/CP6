// MSBBPA100 FSC チェックシート 型定義
import type { ApiResult } from '@/types/estimateCalc'

export interface FscChecklistQueryDto {
  baseCd?: string
  staffCd?: string
  issueDateFrom?: string
  issueDateTo?: string
  qtnNoFrom?: string
  qtnNoTo?: string
  customerCd?: string
  projectNo?: string
  includeUnissued?: boolean
  includeIssued?: boolean
  page?: number
  pageSize?: number
  maxRows?: number
}

export interface FscChecklistItemDto {
  rowNo: number
  staffCd?: string
  staffName?: string
  customerCd?: string
  customerName?: string
  projectNo?: string
  projectName?: string
  qtnNo: string
  qtnIssueDate?: string
  customerItemName1?: string
  customerItemName2?: string
  quantity?: number
  unitPrice?: number
  amount?: number
  totalAmount?: number
  status?: number
  issue: boolean
  fscManagementNo?: string
  issuedLabel?: string
  qtnCalcNo: string
  fscProductDiv?: string
}

export interface FscIssueTarget {
  qtnNo: string
  qtnCalcNo: string
  customerCd?: string
  staffCd?: string
}

export interface FscIssueRequestDto {
  formatName: string
  targets: FscIssueTarget[]
}

export interface FscIssueResultItem {
  fscManagementNo: string
  excelFileName: string
  qtnNo: string
  qtnCalcNo: string
  /** Base64-encoded byte[] (server returns byte[] which axios may parse as base64 string or Uint8Array depending on response type) */
  content?: string
}

export interface FscIssueResultDto {
  issuedCount: number
  items: FscIssueResultItem[]
}

export interface FscFormat {
  name: string
  templatePath: string
}

export type { ApiResult }
