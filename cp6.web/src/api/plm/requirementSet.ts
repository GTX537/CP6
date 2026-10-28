import http from '../http'
import type { EngineeringEnvelope, EngineeringReference, EngineeringScope, EngineeringCreation } from './engineeringProducts'
import type { RequirementSnapshot } from './engineeringRequirements'
import type { EvaluationProof } from './requirementEvaluation'
export type RequirementSetCreation = EngineeringCreation
export interface RequirementSetScope extends EngineeringScope { environmentId: string; evidenceMode: 'REAL' | 'DEMO' }
export interface RequirementSetMember { requirement: EngineeringReference; evaluationControl: EngineeringReference | null; approval: EngineeringReference | null; disposition: 'INCLUDED' | 'EXCLUDED'; basis: string }
export interface RequirementSetConflict { id: string; left: EngineeringReference; right: EngineeringReference; intersection: string; proposal: 'UNRESOLVED' | 'CLARIFY' | 'SCOPE_SEPARATION' | 'ECM' | 'DEV'; basis: string }
export interface RequirementSetBody { kind: 'SET'; name: string; scope: string; completenessBasis: string; members: RequirementSetMember[]; conflicts: RequirementSetConflict[] }
export interface RequirementSetSnapshot { tenantId: string; reference: EngineeringReference; etag: string; isCurrent: boolean; body: RequirementSetBody; authorId: string; actorId: string; parentVersion: string | null; reason: string; recordedAt: string; originalJson: string; missing: string[]; state: string; memberStatus: { requirement: RequirementSnapshot; evaluation: EvaluationProof; source: RequirementSnapshot }[]; differences: { requirementId: string; change: string; previous: RequirementSetMember | null; current: RequirementSetMember | null }[] }
export interface RequirementSetCommand { operationId: string; objectId: string; action: 'CREATE' | 'REVISE'; expectedVersion: string | null; expectedEtag: string | null; expectedAuthorityVersion: string | null; departmentId: string | null; reason: string; body: RequirementSetBody }
export interface RequirementSetResult { operationId: string; kind: 'SET'; objectId: string; version: string; digest: string }
export interface RequirementSetOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: RequirementSetResult | null }
const root = '/requirement-set/owner-v1'
export const requirementSetApi = {
  scope: () => http.get<any, EngineeringEnvelope<RequirementSetScope>>(`${root}/scope`),
  creationContext: () => http.get<any, EngineeringEnvelope<RequirementSetCreation>>(`${root}/creation-context`),
  list: (search = '', page = 1) => http.get<any, EngineeringEnvelope<RequirementSetSnapshot[]>>(root, { params: { search, page } }),
  read: (id: string, version?: string) => http.get<any, EngineeringEnvelope<RequirementSetSnapshot>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  save: (body: RequirementSetCommand) => http.post<any, EngineeringEnvelope<RequirementSetResult>>(`${root}/commands`, body),
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<RequirementSetOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<RequirementSetOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
export function requirementSetMissingText(value: string) {
  const [code, id] = value.split(':')
  const labels: Record<string, string> = { SCOPE_REQUIRED: '适用范围未定', COMPLETENESS_BASIS_REQUIRED: '完整性依据未定', MEMBERS_REQUIRED: '尚未编入成员', MEMBER_UNAVAILABLE: '成员批准来源不可用', MEMBER_APPROVAL_CHANGED: '成员批准版本已变化', EXCLUSION_SOURCE_DECISION_REQUIRED: '排除方案尚缺来源有权裁决', SOURCE_CONFLICT_UNRESOLVED: '原需求存在待裁决冲突', CONFLICT_SOURCE_DECISION_REQUIRED: '冲突方案尚缺来源有权裁决', PREVIOUS_MEMBER_OMITTED: '前版成员未保留，需补回并明确后继处理', REQUIREMENT_SET_REVIEW_REQUIRED: '集合尚待 FLOW 评审采用' }
  return (labels[code!] || '存在待核对事项') + (id ? `（${id}）` : '')
}
