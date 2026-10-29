import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from '../plm/engineeringProducts'
export interface ControlScope { tenantId: string; actorId: string; locationId: string }
export interface ControlEntry { reference: EngineeringReference; state: string; original: { id: string; blockId: string; kind: string; actorId: string; reason: string; controlBefore: string; nativeEtag: string } }
export interface ControlView extends ControlScope { etag: string; controlRevision: string; isBlocked: boolean; initializationQuarantined: boolean; otherRestrictionRetained: boolean; openBlocks: number; entries: ControlEntry[]; page: number; qualification: 'NATIVE_LOCAL_CONTROL_ONLY'; admissionGrant: false }
export interface ControlResult { operationId: string; locationId: string; reference: EngineeringReference; blockId: string; effect: 'BLOCK' | 'RELEASE_DECISION' | 'RELEASE'; etag: string; controlRevision: string; isBlocked: boolean }
export interface ControlOperation { operationId: string; locationId: string; state: 'COMMITTED' | 'UNKNOWN' | 'CLOSED'; result?: ControlResult | null }
const root = (l: string) => `/wms/locations/${encodeURIComponent(l)}/local-controls-v1`
export const locationControlApi = {
  read: (l: string, page = 1) => http.get<any, EngineeringEnvelope<ControlView>>(root(l), { params: { page } }),
  command: (l: string, route: string, etag: string, body: object) => http.post<any, EngineeringEnvelope<ControlResult>>(`${root(l)}/${route}`, body, { headers: { 'If-Match': etag } }),
  operation: (l: string, id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<ControlOperation>>(`${root(l)}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<ControlOperation>>(`${root(l)}/operations/${encodeURIComponent(id)}`),
}
