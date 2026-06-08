import http from '../http'
import type { ApiResult, CreditNoteListItem, CreditNotePaged, CreditNoteQuery } from '@/types/creditNote'

export const creditNoteApi = {
  search(query: CreditNoteQuery) {
    return http.post<any, ApiResult<CreditNotePaged>>('/credit-note/search', query)
  },
}

export type { CreditNoteListItem }
