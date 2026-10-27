import http from '../http'
import type { EngineeringEnvelope, EngineeringReference, EngineeringScope } from './engineeringProducts'
import type { RevisionDiff } from './engineeringRevisions'
import type { UomReference } from '../erp/uomProfiles'
export interface ManufacturingMap { mapId: string; kind: 'MAP' | 'SUPPLEMENT' | 'EXCLUDE'; ebomOccurrenceIds: string[]; mbomOccurrenceIds: string[]; operationOccurrenceIds: string[]; reason: string; quantityBasis: string }
export interface ManufacturingOutput { outputOccurrenceId: string; role: 'PRIMARY' | 'CO_PRODUCT' | 'BY_PRODUCT' | 'INTERMEDIATE'; name: string; productRef: EngineeringReference | null; generatingOperationOccurrenceId: string | null; inputOccurrenceIds: string[]; fromOutputOccurrenceIds: string[]; configurationRef: EngineeringReference | null; quantityKind: 'UNKNOWN' | 'FIXED_RATIO' | 'TARGET' | 'RANGE' | 'OBSERVATION'; quantity: string | null; minimum: string | null; maximum: string | null; unitRef: UomReference | null; denominator: string | null; denominatorUnitRef: UomReference | null; quantityBasis: string; specRequirement: string; relationEvidence: string; noInputBasis: string; dispositionRequirement: string }
export interface ManufacturingBody { name: string; revisionOrigin: EngineeringReference; productRef: EngineeringReference; configurationRef: EngineeringReference | null; ebomRef: EngineeringReference; mbomRef: EngineeringReference; routingRef: EngineeringReference; siteRef: EngineeringReference | null; mappings: ManufacturingMap[]; outputs: ManufacturingOutput[] }
export interface ManufacturingSnapshot { tenantId: string; reference: EngineeringReference; etag: string; isCurrent: boolean; body: ManufacturingBody; action: string; reason: string; parentVersion: string | null; actorId: string; recordedAt: string; originalJson: string; missing: string[]; state: 'DRAFT'; readiness: 'DRAFT_ONLY' }
export interface ManufacturingCommand { operationId: string; manufacturingId: string; action: 'CREATE' | 'REVISE'; expectedVersion: string | null; expectedEtag: string | null; body: ManufacturingBody; reason: string }
export interface ManufacturingResult { operationId: string; kind: 'ENGINEERING_MANUFACTURING'; objectId: string; version: string; digest: string }
export interface ManufacturingOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ManufacturingResult | null }
const root = '/engineering-manufacturing/owner-v1'
export const manufacturingApi = {
  scope: () => http.get<any, EngineeringEnvelope<EngineeringScope>>(`${root}/scope`),
  list: (revisionId?: string, search = '', page = 1) => http.get<any, EngineeringEnvelope<ManufacturingSnapshot[]>>(root, { params: { revisionId, search, page } }),
  read: (id: string, version?: string) => http.get<any, EngineeringEnvelope<ManufacturingSnapshot>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  diff: (id: string, before: string, after: string) => http.get<any, EngineeringEnvelope<RevisionDiff>>(`${root}/${encodeURIComponent(id)}/diff`, { params: { before, after } }),
  save: (command: ManufacturingCommand) => http.post<any, EngineeringEnvelope<ManufacturingResult>>(`${root}/commands`, command),
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<ManufacturingOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<ManufacturingOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
