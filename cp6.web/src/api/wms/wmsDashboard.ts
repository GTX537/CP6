import http from '../http'
import type { WmsKpi, WmsTrendPoint, WmsWarehouseValue, WmsAlerts, WmsApi } from '@/types/wms'

export const wmsDashboardApi = {
  kpi() {
    return http.get<any, WmsApi<WmsKpi>>('/wms/dashboard/kpi')
  },
  trend(days = 30) {
    return http.get<any, WmsApi<WmsTrendPoint[]>>('/wms/dashboard/trend', { params: { days } })
  },
  warehouseValue() {
    return http.get<any, WmsApi<WmsWarehouseValue[]>>('/wms/dashboard/warehouse-value')
  },
  alerts() {
    return http.get<any, WmsApi<WmsAlerts>>('/wms/dashboard/alerts')
  },
}
