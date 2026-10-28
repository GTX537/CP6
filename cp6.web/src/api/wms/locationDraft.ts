import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from '../plm/engineeringProducts'
import type { DraftContext as WarehouseContext } from './warehouseDraft'
import type { HierarchyPolicy } from './locationHierarchy'
export interface LocationDraftScope { tenantId: string; actorId: string; warehouseId: string }
export interface LocationDraftContext { scope: LocationDraftScope; creation: WarehouseContext['creation']; siteRef: EngineeringReference; policy: HierarchyPolicy; hierarchyRevision: string; fenceCapability: string; barcodeSource: string }
export interface LocationDraftCommand { operationId: string; authorityVersion: string; departmentId: string | null; expectedPolicyVersion: string; expectedHierarchyRevision: string; locationCode: string; locationName: string | null; parentLocationId: string | null; locationLevel: number; barcode: string | null }
export interface LocationDraftResult { operationId: string; warehouseId: string; locationId: string; siteId: string; businessRevision: string; hierarchyRevision: string; etag: string; effect: 'CREATED_QUARANTINED_DRAFT' }
export interface LocationDraftOperation { operationId: string; warehouseId: string; state: 'COMMITTED' | 'UNKNOWN' | 'CLOSED'; result?: LocationDraftResult | null }
export interface LocationDraftView { id: string; warehouseId: string; warehouseCode: string; code: string; name: string | null; level: number; parentId: string | null; parentState: string; etag: string; businessRevision: string; canReadName: boolean; canWriteName: boolean; lifecycle: 'DRAFT'; initializationState: 'QUARANTINED'; reservationKnowledge: 'UNKNOWN'; reservedCapacityQty: null; admissionGrant: false }
const root = (w: string) => `/wms/warehouses/${encodeURIComponent(w)}/location-drafts-v1`
export const locationDraftApi = {
  scope: (w: string) => http.get<any, EngineeringEnvelope<LocationDraftScope>>(`${root(w)}/scope`),
  context: (w: string) => http.get<any, EngineeringEnvelope<LocationDraftContext>>(`${root(w)}/creation-context`),
  list: (w: string, page: number) => http.get<any, EngineeringEnvelope<LocationDraftView[]>>(root(w), { params: { page } }),
  create: (w: string, c: LocationDraftCommand) => http.post<any, EngineeringEnvelope<LocationDraftResult>>(root(w), c),
  operation: (w: string, id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<LocationDraftOperation>>(`${root(w)}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<LocationDraftOperation>>(`${root(w)}/operations/${encodeURIComponent(id)}`),
}
