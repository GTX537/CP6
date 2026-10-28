import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from './engineeringProducts'
import type { CheckRun } from './engineeringSourceCheck'
export interface CandidateChange { path: string; before: unknown; after: unknown }
export interface CandidateDefinition { title: string; changeSummary: string; assumptions: string; parentCandidate: EngineeringReference | null; parentBaseline: EngineeringReference | null; iteration: EngineeringReference; inputVersion: string; requirementSet: EngineeringReference; product: EngineeringReference; site: EngineeringReference; sourceCheck: EngineeringReference; engineeringRevision: EngineeringReference; engineeringCandidate: EngineeringReference; technicalSource: CheckRun; technicalSourceOriginal: string; sourceDecision: 'ADOPT_CURRENT' | 'KEEP_ORIGINAL'; driftReason: string }
export interface CandidateSnapshot { reference: EngineeringReference; isCurrent: boolean; original: { environmentId: string; evidenceMode: string; tenantId: string; id: string; iterationId: string; version: string; creatorId: string; actorId: string; etag: string; previousVersion: string | null; definition: CandidateDefinition; previousChanges: CandidateChange[]; parentChanges: CandidateChange[]; reason: string; recordedAt: string }; originalJson: string; sourceDrift: string; findings: string[]; review: string; freeze: string }
export interface CandidateCommand { operationId: string; iterationId: string; candidateId: string; action: 'CREATE' | 'REVISE'; expectedIteration: EngineeringReference; expectedIterationEtag: string; expectedVersion: string | null; expectedEtag: string | null; title: string; changeSummary: string; assumptions: string; sourceCheck: EngineeringReference; parentCandidate: EngineeringReference | null; sourceDecision: 'ADOPT_CURRENT' | 'KEEP_ORIGINAL'; driftReason: string; reason: string }
export interface CandidateResult { operationId: string; iterationId: string; candidateId: string; version: string; digest: string }
export interface CandidateOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: CandidateResult | null }
export interface CandidateDiff { before: EngineeringReference; after: EngineeringReference; changes: CandidateChange[] }
const root = '/iteration-candidates/owner-v1'
export const candidateApi = {
  list: (iteration: string, search = '', page = 1) => http.get<any, EngineeringEnvelope<CandidateSnapshot[]>>(`${root}/${encodeURIComponent(iteration)}`, { params: { search, page } }),
  read: (iteration: string, id: string, version?: string) => http.get<any, EngineeringEnvelope<CandidateSnapshot>>(`${root}/${encodeURIComponent(iteration)}/${encodeURIComponent(id)}`, { params: { version } }),
  compare: (iteration: string, before: EngineeringReference, after: EngineeringReference) => http.post<any, EngineeringEnvelope<CandidateDiff>>(`${root}/${encodeURIComponent(iteration)}/compare`, { before, after }),
  save: (body: CandidateCommand) => http.post<any, EngineeringEnvelope<CandidateResult>>(`${root}/commands`, body),
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<CandidateOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<CandidateOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
