import type { ConfigurationOptionCheck } from './configurationRules'
import http from '../http'
import type { EngineeringReference } from './engineeringProducts'
export interface RevisionEnvelope<T> { code: number; data: T }
export interface RevisionScope { tenantId: string; actorId: string }
export interface RevisionCreation extends RevisionScope { authorityVersion: string; allowsUnbound: boolean; departments: { id: string; code: string; name: string }[] }
export interface RevisionOrigin { type: 'NEW_PRODUCT' | 'CHANGE' | 'LEGACY_REVIEW'; noParentReason?: string | null; parentBaselineRef?: EngineeringReference | null; changeRef?: EngineeringReference | null; legacyRef?: EngineeringReference | null }
export interface RevisionOption { name: string; type: 'STRING' | 'DECIMAL' | 'BOOLEAN'; text?: string | null; decimal?: string | null; boolean?: boolean | null }
export type DraftApplicability = 'UNKNOWN' | 'APPLIES' | 'NOT_APPLICABLE'
export interface RevisionConfiguration { options: RevisionOption[]; applicability: DraftApplicability; reason: string; ruleRef?: EngineeringReference | null }
export interface RevisionComponent { occurrenceId: string; kind: 'PRODUCT' | 'EBOM' | 'MBOM' | 'ROUTING' | 'DOCUMENT' | 'QUALITY_SPEC'; path: string; semanticOrder: number; applicability: DraftApplicability; reason: string; note: string; sourceRef: EngineeringReference | null }
export interface RevisionDraft { productRef: EngineeringReference | null; goal: string; configuration: { reference: EngineeringReference; input: RevisionConfiguration; optionCheck?: ConfigurationOptionCheck | null } | null; components: RevisionComponent[] }
export interface RevisionSnapshot { tenantId: string; reference: EngineeringReference; revisionCode: string; etag: string; candidateRef: EngineeringReference; isCurrent: boolean; state: string; origin: RevisionOrigin; draft: RevisionDraft; reason: string; parentWorkingVersion: string | null; recordedAt: string; originalJson: string; readiness: 'DRAFT_ONLY'; missing: string[]; review: 'NOT_REQUESTED'; freeze: 'NOT_REQUESTED' | 'FROZEN' }
export interface RevisionPatch { op: 'SET_GOAL' | 'SET_PRODUCT' | 'SET_CONFIGURATION' | 'CLEAR_CONFIGURATION' | 'UPSERT_COMPONENT' | 'REMOVE_COMPONENT'; goal?: string; productRef?: EngineeringReference; configuration?: RevisionConfiguration; component?: RevisionComponent; occurrenceId?: string }
export interface RevisionCommand { operationId: string; revisionId: string; action: 'CREATE' | 'PATCH'; expectedVersion?: string | null; expectedEtag?: string | null; expectedAuthorityVersion?: string | null; departmentId?: string | null; origin?: RevisionOrigin | null; productRef?: EngineeringReference | null; goal?: string | null; reason: string; patches?: RevisionPatch[] | null }
export type RevisionOperationKind = 'ENGINEERING_REVISION'
export interface RevisionResult { operationId: string; kind: RevisionOperationKind; objectId: string; version: string; digest: string }
export interface RevisionOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: RevisionResult | null }
export interface RevisionDiff { before: EngineeringReference; after: EngineeringReference; changes: { path: string; before: unknown; after: unknown }[] }
const root = '/engineering-revisions/owner-v1'
export const revisionApi = {
  scope: () => http.get<any, RevisionEnvelope<RevisionScope>>(`${root}/scope`),
  creationContext: () => http.get<any, RevisionEnvelope<RevisionCreation>>(`${root}/creation-context`),
  list: (search = '', page = 1) => http.get<any, RevisionEnvelope<RevisionSnapshot[]>>(root, { params: { search, page } }),
  read: (id: string, version?: string) => http.get<any, RevisionEnvelope<RevisionSnapshot>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  diff: (id: string, before: string, after: string) => http.get<any, RevisionEnvelope<RevisionDiff>>(`${root}/${encodeURIComponent(id)}/diff`, { params: { before, after } }),
  save: (body: RevisionCommand) => http.post<any, RevisionEnvelope<RevisionResult>>(`${root}/commands`, body),
  operation: (id: string, close: boolean) => close
    ? http.post<any, RevisionEnvelope<RevisionOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, RevisionEnvelope<RevisionOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
