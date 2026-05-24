import http from '../http'
import type { Warehouse, WmsLocation, WmsApi } from '@/types/wms'

export const warehouseApi = {
  /** 倉庫一覧 */
  search(params: { warehouseCd?: string; warehouseType?: number; baseCd?: string } = {}) {
    return http.get<any, WmsApi<Warehouse[]>>('/wms/warehouse', { params })
  },
  /** 倉庫詳細 */
  get(cd: string) {
    return http.get<any, WmsApi<Warehouse>>(`/wms/warehouse/${encodeURIComponent(cd)}`)
  },
  /** 倉庫新規 */
  create(dto: Warehouse) {
    return http.post<any, WmsApi<{ warehouseCd: string }>>('/wms/warehouse', dto)
  },
  /** 倉庫更新 */
  update(cd: string, dto: Warehouse) {
    return http.put<any, WmsApi<void>>(`/wms/warehouse/${encodeURIComponent(cd)}`, dto)
  },
  /** 倉庫削除（論理削除） */
  delete(cd: string) {
    return http.delete<any, WmsApi<void>>(`/wms/warehouse/${encodeURIComponent(cd)}`)
  },

  // ───────── ロケーション ─────────
  getLocationTree(warehouseCd: string) {
    return http.get<any, WmsApi<WmsLocation[]>>(`/wms/warehouse/${encodeURIComponent(warehouseCd)}/tree`)
  },
  createLocation(dto: WmsLocation) {
    return http.post<any, WmsApi<{ locationCd: string }>>('/wms/warehouse/location', dto)
  },
  updateLocation(cd: string, dto: WmsLocation) {
    return http.put<any, WmsApi<void>>(`/wms/warehouse/location/${encodeURIComponent(cd)}`, dto)
  },
  deleteLocation(cd: string) {
    return http.delete<any, WmsApi<void>>(`/wms/warehouse/location/${encodeURIComponent(cd)}`)
  },
}
