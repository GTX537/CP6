export type CreditNoteType = 'REFUND' | 'EXCHANGE' | 'SCRAP'

export interface CreditNoteQuery {
  customerCd?: string
  webOrderNo?: string
  type?: CreditNoteType | ''
  dateFrom?: string
  dateTo?: string
  page?: number
  pageSize?: number
}

export interface CreditNoteListItem {
  creditNoteNo: string
  webOrderNo?: string
  rmaNo?: string
  type: CreditNoteType
  customerCd: string
  customerName?: string
  productCd?: string
  qty: number
  amount?: number
  reason?: string
  issueDate: string
  createDate: string
}

export interface CreditNotePaged {
  total?: number
  Total?: number
  pageIndex?: number
  PageIndex?: number
  pageSize?: number
  PageSize?: number
  items?: CreditNoteListItem[]
  Items?: CreditNoteListItem[]
}

export interface ApiResult<T> {
  code: number
  message: string
  data: T
}
