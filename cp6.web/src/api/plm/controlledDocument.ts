import http from '../http'
import type { EngineeringEnvelope, EngineeringReference, EngineeringScope } from './engineeringProducts'
import type { RevisionDiff } from './engineeringRevisions'
import type { ContentReference } from '../pub/fileContent'
export interface DocumentSource { kind: 'INTERNAL' | 'EXTERNAL'; ownerName: string; organization: string; sourceVersion: string; sourceDate: string | null; acquisitionBasis: string; authorityLanguage: string; authorityBasis: string }
export interface DocumentContent { occurrenceId: string; role: 'SOURCE' | 'CONTROLLED' | 'PREVIEW' | 'TRANSLATION'; file: ContentReference; derivedFromOccurrenceId: string | null; language: string; derivationBasis: string }
export interface DocumentBody { name: string; revisionLabel: string; revisionOrigin: EngineeringReference; productRef: EngineeringReference; source: DocumentSource; contents: DocumentContent[] }
export interface DocumentSnapshot { tenantId: string; reference: EngineeringReference; etag: string; isCurrent: boolean; body: DocumentBody; action: string; reason: string; parentVersion: string | null; actorId: string; recordedAt: string; originalJson: string; missing: string[]; contentStatus: { occurrenceId: string; availability: string; fileName: string; stateVersion: string }[]; state: 'DRAFT' | 'APPROVED' | 'UNKNOWN'; readiness: 'REVIEW_REQUIRED' | 'CONTROLLED' | 'SOURCE_UNAVAILABLE' | 'REVIEW_STATUS_UNAVAILABLE' }
export interface DocumentCommand { operationId: string; documentId: string; action: 'CREATE' | 'REVISE'; expectedVersion: string | null; expectedEtag: string | null; body: DocumentBody; reason: string }
export interface DocumentResult { operationId: string; kind: 'CONTROLLED_DOCUMENT'; objectId: string; version: string; digest: string }
export interface DocumentOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: DocumentResult | null }
const root = '/controlled-document/owner-v1'
export const documentApi = {
  scope: () => http.get<any, EngineeringEnvelope<EngineeringScope>>(`${root}/scope`),
  list: (revisionId?: string, search = '', page = 1) => http.get<any, EngineeringEnvelope<DocumentSnapshot[]>>(root, { params: { revisionId, search, page } }),
  read: (id: string, version?: string) => http.get<any, EngineeringEnvelope<DocumentSnapshot>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  diff: (id: string, before: string, after: string) => http.get<any, EngineeringEnvelope<RevisionDiff>>(`${root}/${encodeURIComponent(id)}/diff`, { params: { before, after } }),
  save: (command: DocumentCommand) => http.post<any, EngineeringEnvelope<DocumentResult>>(`${root}/commands`, command),
  download: (id: string, version: string, occurrence: string) => http.get<any, Blob>(`${root}/${encodeURIComponent(id)}/content/${encodeURIComponent(occurrence)}`, { params: { version }, responseType: 'blob' }),
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<DocumentOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<DocumentOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
