import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from './engineeringProducts'
import type { QualityScope } from '../mes/qualitySpecification'
export type OutputScope = QualityScope
export type OutputAction = 'CREATE' | 'REVISE' | 'POLICY' | 'QUALIFY' | 'WITHDRAW'
export interface OutputBody { name: string; manufacturing: EngineeringReference; inputs: { occurrenceId: string; specification: EngineeringReference }[]; outputs: { occurrenceId: string; specification: EngineeringReference; relationDocument: EngineeringReference; noInputDocument: EngineeringReference | null }[] }
export interface OutputPolicy { roleId: number; effectiveFrom: string; effectiveTo: string; basis: string }
export interface OutputView {
  version: { reference: EngineeringReference; originalJson: string; original: { environmentId: string; tenantId: string; evidenceMode: 'REAL' | 'DEMO'; creatorId: string; authorId: string; revisionOrigin: EngineeringReference; product: EngineeringReference; body: OutputBody; manufacturingOriginal: string; wireOriginal: string; wire: { bodyDigest: string }; sourceFingerprint: string } }
  etag: string; controlReference: EngineeringReference; control: { state: string; draftVersion: string }
  policy: { reference: EngineeringReference; body: { input: OutputPolicy } } | null
  qualification: { reference: EngineeringReference; body: { actorId: string; statement: string; recordedAt: string } } | null
  missing: string[]; downstreamMissing: string[]; purpose: 'OUTPUT_RELATIONS_ONLY'
}
export interface OutputCommand { operationId: string; relationId: string; action: OutputAction; expectedEtag: string | null; reason: string; body?: OutputBody; policy?: OutputPolicy }
export interface OutputResult { operationId: string; objectId: string; action: OutputAction; relation: EngineeringReference; control: EngineeringReference; state: string }
export interface OutputOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: OutputResult | null }
const root = '/plm-engineering-output-relations/owner-v1'
export const outputRelationsApi = {
  scope: () => http.get<any, EngineeringEnvelope<OutputScope>>(`${root}/scope`),
  list: (search = '', page = 1) => http.get<any, EngineeringEnvelope<OutputView[]>>(root, { params: { search, page } }),
  read: (id: string, version?: string) => http.get<any, EngineeringEnvelope<OutputView>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  diff: (id: string, before: string, after: string) => http.get<any, EngineeringEnvelope<{ path: string; before: unknown; after: unknown }[]>>(`${root}/${encodeURIComponent(id)}/diff`, { params: { before, after } }),
  roles: () => http.get<any, EngineeringEnvelope<{ roleId: number; roleName: string }[]>>(`${root}/roles`),
  save: (command: OutputCommand) => http.post<any, EngineeringEnvelope<OutputResult>>(`${root}/commands`, command),
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<OutputOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<OutputOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
