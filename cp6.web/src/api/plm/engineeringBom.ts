import http from '../http'
import type { EngineeringEnvelope, EngineeringReference, EngineeringScope } from './engineeringProducts'
import type { DraftApplicability, RevisionDiff } from './engineeringRevisions'
import type { UomReference } from '../erp/uomProfiles'
export interface BomOccurrence { occurrenceId: string; parentOccurrenceId: string | null; path: string; semanticOrder: number; name: string; itemRef: EngineeringReference | null; quantity: string | null; unitRef: UomReference | null; quantityBasis: string; loss: string | null; lossBasis: string; applicability: DraftApplicability; reason: string; configurationCondition: string; substitutionRequirement: string; routingOccurrenceId: string | null; note: string; legacyRowId?: string | null }
export interface BomBody { name: string; kind: 'EBOM' | 'MBOM'; revisionOrigin: EngineeringReference; productRef: EngineeringReference; siteRef: EngineeringReference | null; routingRef: EngineeringReference | null; occurrences: BomOccurrence[] }
export interface BomLegacy { mappingRef: EngineeringReference; productRef: EngineeringReference; digest: string; rows: unknown[]; capturedAt: string; suggestedOccurrences: BomOccurrence[] }
export interface BomSnapshot { tenantId: string; reference: EngineeringReference; etag: string; isCurrent: boolean; body: BomBody; action: string; reason: string; parentVersion: string | null; actorId: string; recordedAt: string; legacy: BomLegacy | null; originalJson: string; missing: string[]; state: 'DRAFT'; readiness: 'DRAFT_ONLY' }
export interface BomCommand { operationId: string; bomId: string; action: 'CREATE' | 'REVISE'; expectedVersion: string | null; expectedEtag: string | null; body: BomBody; reason: string; legacyMappingRef?: EngineeringReference | null; legacyDigest?: string | null }
export interface BomResult { operationId: string; kind: 'ENGINEERING_BOM'; objectId: string; version: string; digest: string }
export interface BomOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: BomResult | null }
export interface BomItemChoice { reference: EngineeringReference; code: string; name: string }
export interface BomUsage { bomRef: EngineeringReference; name: string; kind: string; occurrenceId: string; path: string }
const root = '/engineering-bom/owner-v1'
export const bomApi = {
  scope: () => http.get<any, EngineeringEnvelope<EngineeringScope>>(`${root}/scope`),
  list: (revisionId?: string, search = '', page = 1) => http.get<any, EngineeringEnvelope<BomSnapshot[]>>(root, { params: { revisionId, search, page } }),
  items: (kind: string, search = '', page = 1) => http.get<any, EngineeringEnvelope<BomItemChoice[]>>(`${root}/item-choices`, { params: { kind, search, page } }),
  whereUsed: (reference: EngineeringReference, page = 1) => http.post<any, EngineeringEnvelope<BomUsage[]>>(`${root}/where-used`, reference, { params: { page } }),
  read: (id: string, version?: string) => http.get<any, EngineeringEnvelope<BomSnapshot>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  diff: (id: string, before: string, after: string) => http.get<any, EngineeringEnvelope<RevisionDiff>>(`${root}/${encodeURIComponent(id)}/diff`, { params: { before, after } }),
  legacy: (reference: EngineeringReference) => http.post<any, EngineeringEnvelope<BomLegacy>>(`${root}/legacy-preview`, reference),
  save: (command: BomCommand) => http.post<any, EngineeringEnvelope<BomResult>>(`${root}/commands`, command),
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<BomOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<BomOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
