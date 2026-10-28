import http from '../http'
import type { EngineeringEnvelope, EngineeringReference, EngineeringScope } from '../plm/engineeringProducts'
import type { UomReference } from '../erp/uomProfiles'
export interface QualityScope extends EngineeringScope { environmentId: string; evidenceMode: 'REAL' | 'DEMO' }
export interface QualityRequirement {
  id: string; characteristic: string; method: string; definition: string; standardDocument: EngineeringReference
  unit: UomReference | null; lowerLimit: string | null; upperLimit: string | null; target: string | null
  applicability: 'UNKNOWN' | 'APPLIES' | 'NOT_APPLICABLE'; applicabilityBasis: EngineeringReference | null
  mandatory: boolean; waivable: boolean; waiverBasis: EngineeringReference | null
}
export interface QualityBody { name: string; product: EngineeringReference; site: EngineeringReference; stage: string; purposes: string[]; completenessDocument: EngineeringReference; completenessStatement: string; requirements: QualityRequirement[] }
export interface QualityPolicy { state: 'ENABLED' | 'DISABLED'; roles: { code: string; roleId: number; minimumApprovals: number }[]; effectiveFrom: string; effectiveTo: string }
export interface QualityRecord<T> { reference: EngineeringReference; body: T }
export interface QualityView {
  version: Omit<QualityScope, 'actorId'> & { reference: EngineeringReference; creatorId: string; authorId: string; body: QualityBody; previousVersion: string | null; recordedAt: string; originalJson: string }
  controlReference: EngineeringReference; etag: string
  control: { specificationId: string; revision: number; draftVersion: string; state: string; policyId: string | null; packetId: string | null; approvalId: string | null; actorId: string; reason: string; recordedAt: string }
  policy: QualityRecord<{ id: string; actorId: string; input: QualityPolicy }> | null
  packet: QualityRecord<{ id: string; specification: EngineeringReference; policy: EngineeringReference; authorId: string; submitterId: string; requirementsDigest: string; submittedAt: string }> | null
  votes: QualityRecord<{ id: string; packetId: string; actorId: string; roleIds: number[]; approved: boolean; comment: string; signedAt: string }>[]
  currentApproval: QualityRecord<{ id: string; specification: EngineeringReference; packet: EngineeringReference; policy: EngineeringReference; votes: EngineeringReference[]; requirementsDigest: string; completenessStatement: string; approvedAt: string }> | null
  missing: string[]
}
export type QualityAction = 'CREATE' | 'REVISE' | 'POLICY' | 'SUBMIT' | 'APPROVE' | 'RETURN' | 'WITHDRAW'
export interface QualityCommand { operationId: string; specificationId: string; action: QualityAction; expectedEtag: string | null; reason: string; body?: QualityBody; policy?: QualityPolicy }
export interface QualityResult { operationId: string; objectId: string; action: QualityAction; specification: EngineeringReference; control: EngineeringReference; state: string }
export interface QualityOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: QualityResult | null }
export interface QualityProof { specification: EngineeringReference; control: EngineeringReference; approval: EngineeringReference | null; requirementsDigest: string; availability: 'AVAILABLE' | 'UNAVAILABLE'; missing: string[]; requirements: QualityRequirement[]; observation: 'READ_ONLY_OBSERVATION' }
const root = '/quality-specification/owner-v1'
export const qualityApi = {
  scope: () => http.get<any, EngineeringEnvelope<QualityScope>>(`${root}/scope`),
  list: (search = '', page = 1) => http.get<any, EngineeringEnvelope<QualityView[]>>(root, { params: { search, page } }),
  read: (id: string, version?: string) => http.get<any, EngineeringEnvelope<QualityView>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  roles: () => http.get<any, EngineeringEnvelope<{ roleId: number; roleName: string }[]>>(`${root}/roles`),
  save: (command: QualityCommand) => http.post<any, EngineeringEnvelope<QualityResult>>(`${root}/commands`, command),
  source: (reference: EngineeringReference) => http.post<any, EngineeringEnvelope<QualityProof>>(`${root}/source`, reference),
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<QualityOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<QualityOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
