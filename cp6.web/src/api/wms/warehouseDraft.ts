import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from '../plm/engineeringProducts'
import type { WarehouseObservation } from '@/types/wms/warehouseOwner'
export interface DraftScope { tenantId: string; actorId: string }
export interface DraftContext {
  creation: DraftScope & { authorityVersion: string; allowsUnbound: boolean; departments: { id: string; code: string; name: string }[] }
  sites: { reference: EngineeringReference; code: string; name: string; enabled: boolean }[]
  typeOwner: string; typeVersion: string; types: { code: number; name: string }[]; relationshipState: string
}
export interface DraftCommand { operationId: string; authorityVersion: string; departmentId: string | null; siteId: string; warehouseCode: string; warehouseName: string; warehouseType: number; baseCode: string | null; address: string | null; managerCode: string | null; remarks: string | null }
export interface DraftResult { operationId: string; warehouseId: string; siteId: string; businessRevision: string; etag: string; effect: 'CREATED_DRAFT' }
export interface DraftOperation { operationId: string; state: 'COMMITTED' | 'UNKNOWN' | 'CLOSED'; result?: DraftResult | null }
export interface DraftView { warehouse: WarehouseObservation; siteRef: EngineeringReference; lifecycle: 'DRAFT'; businessRevision: string; admissionGrant: false; eligibility: string }
const root = '/wms/master/drafts-v1'
export const warehouseDraftApi = {
  scope: () => http.get<any, EngineeringEnvelope<DraftScope>>(`${root}/scope`),
  context: () => http.get<any, EngineeringEnvelope<DraftContext>>(`${root}/creation-context`),
  list: (page: number) => http.get<any, EngineeringEnvelope<DraftView[]>>(`${root}/warehouses`, { params: { page } }),
  create: (c: DraftCommand) => http.post<any, EngineeringEnvelope<DraftResult>>(`${root}/warehouses`, c),
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<DraftOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<DraftOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
