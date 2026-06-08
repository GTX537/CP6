import http from '../http'
import type {
  MaterialShortage,
  MaterialShortageActionRequest,
  MaterialShortagePaged,
  MaterialShortageQuery,
  WmsApi,
} from '@/types/materialShortage'

export const materialShortageApi = {
  search(query: MaterialShortageQuery = {}) {
    return http.get<any, WmsApi<MaterialShortagePaged>>('/wms/material-shortage', {
      params: query,
    })
  },

  resolve(id: string, req: MaterialShortageActionRequest) {
    return http.post<any, WmsApi<MaterialShortage>>(
      `/wms/material-shortage/${encodeURIComponent(id)}/resolve`,
      req,
    )
  },

  dismiss(id: string, req: MaterialShortageActionRequest) {
    return http.post<any, WmsApi<MaterialShortage>>(
      `/wms/material-shortage/${encodeURIComponent(id)}/dismiss`,
      req,
    )
  },
}
