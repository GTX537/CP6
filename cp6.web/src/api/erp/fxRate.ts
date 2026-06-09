import http from '../http'
import type { FxRate, WmsApi } from '@/types/erp/fxRate'

export const fxRateApi = {
  list(currencyCd?: string) {
    return http.get<any, WmsApi<FxRate[]>>('/erp/fx-rate', { params: { currencyCd } })
  },

  resolve(currencyCd: string, asOf?: string) {
    return http.get<any, WmsApi<number | null>>('/erp/fx-rate/resolve', { params: { currencyCd, asOf } })
  },

  create(rate: FxRate) {
    return http.post<any, WmsApi<FxRate>>('/erp/fx-rate', rate)
  },

  update(id: string, rate: FxRate) {
    return http.put<any, WmsApi<unknown>>(`/erp/fx-rate/${encodeURIComponent(id)}`, rate)
  },

  remove(id: string) {
    return http.delete<any, WmsApi<unknown>>(`/erp/fx-rate/${encodeURIComponent(id)}`)
  },
}
