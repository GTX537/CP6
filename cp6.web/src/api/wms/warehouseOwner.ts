import http from '../http'
import type { WmsApi } from '@/types/wms/wms'
import type { MasterKind, MasterQuery, MasterPage, WarehouseObservation, LocationObservation, MasterNamePatch, MasterNameResult, MasterOperation, MasterScope } from '@/types/wms/warehouseOwner'
const base = '/wms/warehouse/owner-v1'
export const warehouseOwnerApi = {
  warehouses: (params: MasterQuery) => http.get<any, WmsApi<MasterPage<WarehouseObservation>>>(`${base}/warehouses`, { params }),
  locations: (id: string, params: MasterQuery) => http.get<any, WmsApi<MasterPage<LocationObservation>>>(`${base}/warehouses/${encodeURIComponent(id)}/locations`, { params }),
  scope: (kind: MasterKind) => http.get<any, WmsApi<MasterScope>>(`${base}/${kind}/operation-scope`),
  rename: (kind: MasterKind, id: string, operation: string, etag: string, patch: MasterNamePatch) =>
    http.patch<any, WmsApi<MasterNameResult>>(`${base}/${kind}/${encodeURIComponent(id)}/name`, patch, { headers: { 'Idempotency-Key': operation, 'If-Match': etag } }),
  operation: (kind: MasterKind, id: string, operation: string) => http.get<any, WmsApi<MasterOperation>>(`${base}/${kind}/${encodeURIComponent(id)}/operations/${encodeURIComponent(operation)}`),
  close: (kind: MasterKind, id: string, operation: string) => http.post<any, WmsApi<MasterOperation>>(`${base}/${kind}/${encodeURIComponent(id)}/operations/${encodeURIComponent(operation)}/close`),
}
