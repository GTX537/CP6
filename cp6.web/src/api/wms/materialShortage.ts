import http from '../http'
import type { MaterialShortage, MaterialShortageActionRequest, MaterialShortagePaged, MaterialShortageQuery, WmsApi } from '@/types/wms/materialShortage'

export const materialShortageApi = {
  search(query: MaterialShortageQuery = {}) {
    return http.get<any, WmsApi<MaterialShortagePaged>>('/wms/material-shortage/owner-v1', { params: query })
  },
  resolve(id: string, request: MaterialShortageActionRequest) {
    return http.post<any, WmsApi<MaterialShortage>>(`/wms/material-shortage/owner-v1/${encodeURIComponent(id)}/resolve`, request)
  },
  dismiss(id: string, request: MaterialShortageActionRequest) {
    return http.post<any, WmsApi<MaterialShortage>>(`/wms/material-shortage/owner-v1/${encodeURIComponent(id)}/dismiss`, request)
  },
}
