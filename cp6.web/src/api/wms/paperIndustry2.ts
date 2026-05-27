import http from '../http'
import type {
  RemnantMaterial, RemnantSearchQuery,
  PlateMoldStock, PlateMoldSearchQuery,
  SampleStock, SampleSearchQuery,
  WmsApi,
} from '@/types/wms'

export const remnantApi = {
  search(q: RemnantSearchQuery = {}) {
    return http.get<any, WmsApi<RemnantMaterial[]>>('/wms/remnant', { params: q })
  },
  get(no: string) {
    return http.get<any, WmsApi<RemnantMaterial>>(`/wms/remnant/${encodeURIComponent(no)}`)
  },
  create(dto: any) {
    return http.post<any, WmsApi<{ remnantNo: string }>>('/wms/remnant', dto)
  },
  update(no: string, dto: any) {
    return http.put<any, WmsApi<void>>(`/wms/remnant/${encodeURIComponent(no)}`, dto)
  },
  match(materialType: string, minWidthMm: number, minLengthMm: number) {
    return http.get<any, WmsApi<RemnantMaterial[]>>('/wms/remnant/match', { params: { materialType, minWidthMm, minLengthMm } })
  },
  reserve(no: string, reservedFor: string) {
    return http.post<any, WmsApi<void>>(`/wms/remnant/${encodeURIComponent(no)}/reserve`, { reservedFor })
  },
  unreserve(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/remnant/${encodeURIComponent(no)}/unreserve`)
  },
  markUsed(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/remnant/${encodeURIComponent(no)}/use`)
  },
  dispose(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/remnant/${encodeURIComponent(no)}/dispose`)
  },
  delete(no: string) {
    return http.delete<any, WmsApi<void>>(`/wms/remnant/${encodeURIComponent(no)}`)
  },
}

export const plateMoldApi = {
  search(q: PlateMoldSearchQuery = {}) {
    return http.get<any, WmsApi<PlateMoldStock[]>>('/wms/plate-mold', { params: q })
  },
  get(no: string) {
    return http.get<any, WmsApi<PlateMoldStock>>(`/wms/plate-mold/${encodeURIComponent(no)}`)
  },
  create(dto: any) {
    return http.post<any, WmsApi<{ plateNo: string }>>('/wms/plate-mold', dto)
  },
  update(no: string, dto: any) {
    return http.put<any, WmsApi<void>>(`/wms/plate-mold/${encodeURIComponent(no)}`, dto)
  },
  recordUsage(no: string, shots: number) {
    return http.post<any, WmsApi<void>>(`/wms/plate-mold/${encodeURIComponent(no)}/use`, { shots })
  },
  startMaintenance(no: string, nextMaintenanceDate?: string) {
    return http.post<any, WmsApi<void>>(`/wms/plate-mold/${encodeURIComponent(no)}/maintenance/start`, { nextMaintenanceDate })
  },
  completeMaintenance(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/plate-mold/${encodeURIComponent(no)}/maintenance/complete`)
  },
  discard(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/plate-mold/${encodeURIComponent(no)}/discard`)
  },
  warnings(threshold = 0.9) {
    return http.get<any, WmsApi<PlateMoldStock[]>>('/wms/plate-mold/warnings', { params: { threshold } })
  },
  delete(no: string) {
    return http.delete<any, WmsApi<void>>(`/wms/plate-mold/${encodeURIComponent(no)}`)
  },
}

export const sampleApi = {
  search(q: SampleSearchQuery = {}) {
    return http.get<any, WmsApi<SampleStock[]>>('/wms/sample-stock', { params: q })
  },
  get(no: string) {
    return http.get<any, WmsApi<SampleStock>>(`/wms/sample-stock/${encodeURIComponent(no)}`)
  },
  create(dto: any) {
    return http.post<any, WmsApi<{ sampleNo: string }>>('/wms/sample-stock', dto)
  },
  update(no: string, dto: any) {
    return http.put<any, WmsApi<void>>(`/wms/sample-stock/${encodeURIComponent(no)}`, dto)
  },
  lend(no: string, lentTo: string, expectedReturnDate?: string) {
    return http.post<any, WmsApi<void>>(`/wms/sample-stock/${encodeURIComponent(no)}/lend`, { lentTo, expectedReturnDate })
  },
  return(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/sample-stock/${encodeURIComponent(no)}/return`)
  },
  expire(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/sample-stock/${encodeURIComponent(no)}/expire`)
  },
  overdue() {
    return http.get<any, WmsApi<SampleStock[]>>('/wms/sample-stock/overdue')
  },
  delete(no: string) {
    return http.delete<any, WmsApi<void>>(`/wms/sample-stock/${encodeURIComponent(no)}`)
  },
}
