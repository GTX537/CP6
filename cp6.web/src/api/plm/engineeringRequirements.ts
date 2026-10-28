import http from '../http'
import type { UomReference } from '../erp/uomProfiles'
import type { EngineeringCreation, EngineeringEnvelope, EngineeringReference, EngineeringScope } from './engineeringProducts'
export type RequirementEnvelope<T> = EngineeringEnvelope<T>
export type RequirementScope = EngineeringScope
export type RequirementCreation = EngineeringCreation
export type RequirementOperationKind = 'SOURCE' | 'REQUIREMENT'
export interface RequirementSource { kind: 'INTERNAL_PROPOSAL' | 'UNRESOLVED'; originDescription: string; sourceLabel: string; acquisitionBasis: string; clauses: { id: string; originalText: string; locator: string }[] }
export interface RequirementCriterion { id: string; kind: 'QUANTITATIVE' | 'QUALITATIVE'; object: string; conditions: string; methodRequirement: string; lower: string | null; lowerInclusive: boolean | null; upper: string | null; upperInclusive: boolean | null; proposedUnit: string | null; unitBasis: string | null; precision: string | null; qualitativeBasis: string | null; evaluation?: { schema: 'requirement-evaluation/1'; unit: UomReference; lowerUnbounded: boolean; upperUnbounded: boolean } | null }
export interface RequirementRelation { id: string; kind: 'PARENT' | 'DERIVED' | 'CONSTRAINT' | 'CONFLICT' | 'DEPENDENCY'; target: EngineeringReference; coverageReason: string; uncovered: string; necessity: string }
export interface RequirementDefinition { source: EngineeringReference; clauseIds: string[]; interpretation: string; applicableObject: string; necessity: string; clarificationQuestions: string[]; criteria: RequirementCriterion[]; relations: RequirementRelation[] }
export interface RequirementBody { kind: RequirementOperationKind; name: string; source: RequirementSource | null; requirement: RequirementDefinition | null }
export interface RequirementSnapshot { tenantId: string; reference: EngineeringReference; etag: string; isCurrent: boolean; body: RequirementBody; authorId: string; actorId: string; parentVersion: string | null; reason: string; recordedAt: string; originalJson: string; missing: string[]; state: 'DRAFT_ONLY' }
export interface RequirementCommand { operationId: string; objectId: string; action: 'CREATE' | 'REVISE'; expectedVersion: string | null; expectedEtag: string | null; expectedAuthorityVersion: string | null; departmentId: string | null; reason: string; body: RequirementBody }
export interface RequirementResult { operationId: string; kind: RequirementOperationKind; objectId: string; version: string; digest: string }
export interface RequirementOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: RequirementResult | null }
const root = '/engineering-requirements/owner-v1'
export const requirementApi = {
  scope: () => http.get<any, RequirementEnvelope<RequirementScope>>(`${root}/scope`),
  creationContext: () => http.get<any, RequirementEnvelope<RequirementCreation>>(`${root}/creation-context`),
  list: (kind?: RequirementOperationKind, search = '', page = 1) => http.get<any, RequirementEnvelope<RequirementSnapshot[]>>(root, { params: { kind, search, page } }),
  read: (id: string, version?: string) => http.get<any, RequirementEnvelope<RequirementSnapshot>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  save: (body: RequirementCommand) => http.post<any, RequirementEnvelope<RequirementResult>>(`${root}/commands`, body),
  operation: (id: string, close: boolean) => close ? http.post<any, RequirementEnvelope<RequirementOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, RequirementEnvelope<RequirementOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
