import http from '../http'
import type { UomReference } from '../erp/uomProfiles'
import type { EngineeringCreation, EngineeringEnvelope, EngineeringReference, EngineeringScope } from './engineeringProducts'
export type RequirementEnvelope<T> = EngineeringEnvelope<T>
export type RequirementScope = EngineeringScope
export type RequirementCreation = EngineeringCreation
export type RequirementOperationKind = 'SOURCE' | 'REQUIREMENT'
export interface RequirementScopeAxis { mode: 'UNKNOWN' | 'ALL' | 'VALUES'; namespace: string | null; keys: string[] }
export interface RequirementClauseTerms { schema: 'requirement-clause-terms/1'; customers: RequirementScopeAxis; products: RequirementScopeAxis; configurations: RequirementScopeAxis; from: string | null; until: string | null; fromUnbounded: boolean; untilUnbounded: boolean; necessity: 'UNKNOWN' | 'MANDATORY' | 'OPTIONAL'; waivability: 'UNKNOWN' | 'PROHIBITED' | 'SOURCE_DECISION_REQUIRED'; basis: string }
export interface RequirementOrigin { reference: EngineeringReference; occurrenceId: string | null }
export interface RequirementClarification { id: string; source: EngineeringReference; clauseId: string; question: string; reply?: RequirementOrigin | null }
export interface RequirementSource { kind: 'INTERNAL_PROPOSAL' | 'UNRESOLVED' | 'OWNER_ORIGINAL'; originDescription: string; sourceLabel: string; acquisitionBasis: string; clauses: { id: string; originalText: string; locator: string; terms?: RequirementClauseTerms | null }[]; origin?: RequirementOrigin | null }
export interface RequirementCriterion { id: string; kind: 'QUANTITATIVE' | 'QUALITATIVE'; object: string; conditions: string; methodRequirement: string; lower: string | null; lowerInclusive: boolean | null; upper: string | null; upperInclusive: boolean | null; proposedUnit: string | null; unitBasis: string | null; precision: string | null; qualitativeBasis: string | null; evaluation?: { schema: 'requirement-evaluation/1'; unit: UomReference; lowerUnbounded: boolean; upperUnbounded: boolean } | null; sourceClauseIds?: string[] | null }
export interface RequirementCoverageLink { parentClauseId: string; childClauseId: string; reason: string }
export interface RequirementRelation { id: string; kind: 'PARENT' | 'DERIVED' | 'CONSTRAINT' | 'CONFLICT' | 'DEPENDENCY'; target: EngineeringReference; coverageReason: string; uncovered: string; necessity: string; coverage?: RequirementCoverageLink[] | null }
export interface RequirementDecomposition { parent: EngineeringReference; children: EngineeringReference[]; basis: string }
export interface RequirementCoverage { parent: EngineeringReference; complete: boolean; clauses: { clauseId: string; state: string; terms: RequirementClauseTerms | null; children: { child: EngineeringReference; childClauseId: string; reason: string; scope: string; terms: RequirementClauseTerms | null }[] }[] }
export interface RequirementDefinition { source: EngineeringReference; clauseIds: string[]; interpretation: string; applicableObject: string; necessity: string; clarificationQuestions: string[]; criteria: RequirementCriterion[]; relations: RequirementRelation[]; clarifications?: RequirementClarification[] | null; decomposition?: RequirementDecomposition | null }
export interface RequirementBody { kind: RequirementOperationKind; name: string; source: RequirementSource | null; requirement: RequirementDefinition | null }
export interface RequirementSnapshot { tenantId: string; reference: EngineeringReference; etag: string; isCurrent: boolean; body: RequirementBody; authorId: string; actorId: string; parentVersion: string | null; reason: string; recordedAt: string; originalJson: string; missing: string[]; state: 'DRAFT_ONLY'; coverage?: RequirementCoverage | null }
export interface RequirementDiff { before: EngineeringReference; after: EngineeringReference; changes: { path: string; before: unknown; after: unknown }[] }
export interface RequirementCommand { operationId: string; objectId: string; action: 'CREATE' | 'REVISE'; expectedVersion: string | null; expectedEtag: string | null; expectedAuthorityVersion: string | null; departmentId: string | null; reason: string; body: RequirementBody }
export interface RequirementResult { operationId: string; kind: RequirementOperationKind; objectId: string; version: string; digest: string }
export interface RequirementOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: RequirementResult | null }
const root = '/engineering-requirements/owner-v1'
export const requirementApi = {
  scope: () => http.get<any, RequirementEnvelope<RequirementScope>>(`${root}/scope`),
  creationContext: () => http.get<any, RequirementEnvelope<RequirementCreation>>(`${root}/creation-context`),
  list: (kind?: RequirementOperationKind, search = '', page = 1) => http.get<any, RequirementEnvelope<RequirementSnapshot[]>>(root, { params: { kind, search, page } }),
  read: (id: string, version?: string) => http.get<any, RequirementEnvelope<RequirementSnapshot>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  diff: (id: string, before: string, after: string) => http.get<any, RequirementEnvelope<RequirementDiff>>(`${root}/${encodeURIComponent(id)}/diff`, { params: { before, after } }),
  original: (id: string, version: string, question?: string) => http.get<any, Blob>(`${root}/${encodeURIComponent(id)}/original`, { params: { version, question }, responseType: 'blob' }),
  exportPage: (kind: RequirementOperationKind, search: string, page: number) => http.get<any, Blob>(`${root}/export`, { params: { kind, search, page }, responseType: 'blob' }),
  save: (body: RequirementCommand) => http.post<any, RequirementEnvelope<RequirementResult>>(`${root}/commands`, body),
  operation: (id: string, close: boolean) => close ? http.post<any, RequirementEnvelope<RequirementOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, RequirementEnvelope<RequirementOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
