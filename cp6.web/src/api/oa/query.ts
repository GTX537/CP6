import http from '../http'
import type { FormQueryFilter } from '../../types/oa/advanced'

export const queryApi = {
  search: (filter: FormQueryFilter) => http.post('/oa/query/search', filter),
}
