import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from './engineeringProducts'
import type { RequirementSnapshot } from './engineeringRequirements'
import type { UomVersion } from '../erp/uomProfiles'
export interface EvaluationScope { environmentId: string; tenantId: string; actorId: string; evidenceMode: 'REAL' | 'DEMO' }
export interface EvaluationPolicy { state: 'ENABLED' | 'DISABLED'; basis: string; roles: { code: string; roleId: number; minimumApprovals: number }[]; effectiveFrom: string; effectiveTo: string }
export interface EvaluationRecord<T> { reference: EngineeringReference; body: T }
export interface EvaluationInputs { requirement: RequirementSnapshot; relatedOriginals: RequirementSnapshot[]; units: UomVersion[]; missing: string[] }
export interface EvaluationView {
  environmentId: string; tenantId: string; evidenceMode: string; requirement: RequirementSnapshot; etag: string; controlReference: EngineeringReference | null
  control: { requirementId: string; revision: number; state: string; reason: string; actorId: string; recordedAt: string } | null
  policy: EvaluationRecord<{ id: string; actorId: string; input: EvaluationPolicy }> | null
  packet: EvaluationRecord<{ id: string; requirement: EngineeringReference; policy: EngineeringReference; authorId: string; submitterId: string; inputDigest: string; inputs: EvaluationInputs; submittedAt: string; purpose: 'DEFINITION_ONLY' }> | null
  votes: EvaluationRecord<{ id: string; actorId: string; approved: boolean; comment: string; signedAt: string; roleIds: number[] }>[]
  currentApproval: EvaluationRecord<{ id: string; requirement: EngineeringReference; packet: EngineeringReference; policy: EngineeringReference; votes: EngineeringReference[]; inputDigest: string; approvedAt: string; purpose: 'DEFINITION_ONLY' }> | null
  missing: string[]; controlIsCurrent: boolean
}
export type EvaluationAction = 'POLICY' | 'SUBMIT' | 'APPROVE' | 'RETURN' | 'WITHDRAW'
export interface EvaluationCommand { operationId: string; requirementId: string; action: EvaluationAction; expectedEtag: string; reason: string; requirement?: EngineeringReference; policy?: EvaluationPolicy }
export interface EvaluationResult { operationId: string; objectId: string; action: EvaluationAction; requirement: EngineeringReference; control: EngineeringReference; state: string; environmentId: string; evidenceMode: string }
export interface EvaluationOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: EvaluationResult | null }
export interface EvaluationProof { environmentId: string; tenantId: string; evidenceMode: string; requirement: EngineeringReference; control: EngineeringReference | null; approval: EngineeringReference | null; availability: 'AVAILABLE' | 'UNAVAILABLE'; missing: string[]; inputDigest: string; observation: 'READ_ONLY_OBSERVATION'; purpose: 'DEFINITION_ONLY' }
const root = '/requirement-evaluations/owner-v1'
export const evaluationApi = {
  scope: () => http.get<any, EngineeringEnvelope<EvaluationScope>>(`${root}/scope`),
  read: (id: string, version?: string) => http.get<any, EngineeringEnvelope<EvaluationView>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  history: (id: string, revision: string) => http.get<any, EngineeringEnvelope<EvaluationView>>(`${root}/${encodeURIComponent(id)}/controls/${encodeURIComponent(revision)}`),
  roles: () => http.get<any, EngineeringEnvelope<{ roleId: number; roleName: string }[]>>(`${root}/roles`),
  save: (command: EvaluationCommand) => http.post<any, EngineeringEnvelope<EvaluationResult>>(`${root}/commands`, command),
  source: (reference: EngineeringReference) => http.post<any, EngineeringEnvelope<EvaluationProof>>(`${root}/source`, reference),
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<EvaluationOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<EvaluationOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
