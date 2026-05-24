import http from '../http'
import type {
  QcInspection, QcInspectionItem, QcInspectionSearchQuery,
  QcJudgeRequest, QcJudgeResult, WmsApi,
} from '@/types/wms'

export const qcInspectionApi = {
  search(query: QcInspectionSearchQuery = {}) {
    return http.get<any, WmsApi<QcInspection[]>>('/wms/qc-inspection', { params: query })
  },
  get(no: string) {
    return http.get<any, WmsApi<QcInspection>>(`/wms/qc-inspection/${encodeURIComponent(no)}`)
  },
  createFromInbound(inboundNo: string) {
    return http.post<any, WmsApi<{ inspectionNo: string }>>(`/wms/qc-inspection/from-inbound/${encodeURIComponent(inboundNo)}`)
  },
  createDirect(dto: QcInspection) {
    return http.post<any, WmsApi<{ inspectionNo: string }>>('/wms/qc-inspection', dto)
  },
  saveItems(no: string, items: QcInspectionItem[]) {
    return http.put<any, WmsApi<void>>(`/wms/qc-inspection/${encodeURIComponent(no)}/items`, items)
  },
  judge(no: string, req: QcJudgeRequest) {
    return http.post<any, WmsApi<QcJudgeResult>>(`/wms/qc-inspection/${encodeURIComponent(no)}/judge`, req)
  },
  cancel(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/qc-inspection/${encodeURIComponent(no)}/cancel`)
  },
}
