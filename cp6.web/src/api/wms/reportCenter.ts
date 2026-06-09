import http from '../http'
import type {
  MonthlyStockReportRow, AbcAnalysisRow, DeadStockRow,
  InboundHistoryRow, OutboundHistoryRow,
  WmsApi,
} from '@/types/wms/wms'

export const reportApi = {
  monthlyStock(yearMonth: string, warehouseCd?: string) {
    return http.get<any, WmsApi<MonthlyStockReportRow[]>>('/wms/report-center/monthly-stock', { params: { yearMonth, warehouseCd } })
  },
  abcAnalysis(analysisDays = 90, warehouseCd?: string) {
    return http.get<any, WmsApi<AbcAnalysisRow[]>>('/wms/report-center/abc-analysis', { params: { analysisDays, warehouseCd } })
  },
  deadStock(idleDays = 90, warehouseCd?: string) {
    return http.get<any, WmsApi<DeadStockRow[]>>('/wms/report-center/dead-stock', { params: { idleDays, warehouseCd } })
  },
  inboundHistory(fromDate: string, toDate: string, warehouseCd?: string, productCd?: string) {
    return http.get<any, WmsApi<InboundHistoryRow[]>>('/wms/report-center/inbound-history', { params: { fromDate, toDate, warehouseCd, productCd } })
  },
  outboundHistory(fromDate: string, toDate: string, warehouseCd?: string, productCd?: string) {
    return http.get<any, WmsApi<OutboundHistoryRow[]>>('/wms/report-center/outbound-history', { params: { fromDate, toDate, warehouseCd, productCd } })
  },

  /** CSV ダウンロード：URL を組み立てて新規 window/anchor で取得（Bearer 込み） */
  async downloadCsv(path: string, params: Record<string, any>, filename: string) {
    const blob = await http.get(path, { params, responseType: 'blob' }) as any
    const url = URL.createObjectURL(blob as Blob)
    const a = document.createElement('a')
    a.href = url; a.download = filename
    document.body.appendChild(a); a.click(); a.remove()
    setTimeout(() => URL.revokeObjectURL(url), 1000)
  },
}
