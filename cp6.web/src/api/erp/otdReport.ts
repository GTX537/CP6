import http from '../http'
import type { ApiResult, OtdReportQuery, OtdReportSummary } from '@/types/erp/otdReport'

export const otdReportApi = {
  summary(query: OtdReportQuery) {
    return http.post<any, ApiResult<OtdReportSummary>>('/otd-report/summary', query)
  },

  exportCsv(query: OtdReportQuery) {
    return http.post<Blob>('/otd-report/export-csv', query, { responseType: 'blob' })
  },
}
