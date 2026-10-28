import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from './engineeringProducts'
import type { RevisionSnapshot } from './engineeringRevisions'
import type { CandidateSnapshot } from './iterationCandidate'
import type { CheckRun } from './engineeringSourceCheck'
import type { ReviewSubmission } from './iterationReview'
export interface BaselineScope { environmentId: string; tenantId: string; actorId: string; evidenceMode: string }
export interface FreezeCommand { operationId: string; iterationId: string; sourceCandidate: EngineeringReference; reviewSubmissionId: string; reviewRoundId: string; applicationReceiptId: string; revision: EngineeringReference; engineeringCandidate: EngineeringReference; expectedEtag: string; configuration: EngineeringReference; sourceCheck: EngineeringReference; technicalManifestDigest: string; reviewPackageDigest: string; reason: string }
export interface FreezeChoice { iterationId: string; submission: ReviewSubmission; application: { businessStage: string; decision: string | null; receipt: { receiptId: string; application: string; outcomeCode: string } | null }; candidates: CandidateSnapshot[] }
export interface BaselineView { reference: EngineeringReference; original: { environmentId: string; tenantId: string; evidenceMode: string; frozenAt: string; command: FreezeCommand; revision: RevisionSnapshot; check: CheckRun; manifest: NonNullable<CheckRun['manifest']>; outputApplicability: string }; originalJson: string; outputBinding: { bodyDigest: string } | null; outputBindingOriginal: string | null; sourceStatus: string; changes: string[]; state: string; release: string; verification: string }
export interface FreezeResult { operationId: string; baseline: EngineeringReference; originalOperationId: string; state: 'ACCEPTED'; technicalManifestDigest: string; outputBindingDigest: string | null; resultIntentId: string }
export interface SuccessorResult { operationId: string; parentBaseline: EngineeringReference; revision: EngineeringReference; candidate: EngineeringReference; etag: string }
export interface BaselineOperation { operationId: string; state: 'COMMITTED' | 'UNKNOWN' | 'CLOSED'; result?: FreezeResult | SuccessorResult | null }
export interface BaselineUsage { owner: string; status: string; references: EngineeringReference[]; reason: string }
const root = '/plm-engineering-baselines/owner-v1'
export const baselineApi = {
  scope: () => http.get<any, EngineeringEnvelope<BaselineScope>>(`${root}/scope`),
  list: (page = 1) => http.get<any, EngineeringEnvelope<BaselineView[]>>(root, { params: { page } }),
  read: (id: string) => http.get<any, EngineeringEnvelope<BaselineView>>(`${root}/${encodeURIComponent(id)}`),
  choices: (iteration: string) => http.get<any, EngineeringEnvelope<FreezeChoice[]>>(`${root}/choices/${encodeURIComponent(iteration)}`),
  freeze: (c: FreezeCommand) => http.post<any, EngineeringEnvelope<FreezeResult>>(`${root}/freeze`, c),
  cancel: (operationId: string, targetOperationId: string) => http.post<any, EngineeringEnvelope<{ operationId: string; targetOperationId: string; outcome: BaselineOperation }>>(`${root}/cancel`, { operationId, targetOperationId, expectedState: 'UNKNOWN', reason: '用户显式结束未确认冻结；已提交事实保留' }),
  successor: (operationId: string, v: BaselineView, reason: string) => http.post<any, EngineeringEnvelope<SuccessorResult>>(`${root}/successor`, { operationId, baseline: v.reference, expectedRevision: v.original.command.revision, expectedEtag: v.original.command.expectedEtag, reason }),
  operation: (id: string, action: 'FREEZE' | 'SUCCESSOR', close = false) => action === 'FREEZE'
    ? http.get<any, EngineeringEnvelope<BaselineOperation>>(`${root}/operations/${encodeURIComponent(id)}`)
    : close ? http.post<any, EngineeringEnvelope<BaselineOperation>>(`${root}/successor-operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<BaselineOperation>>(`${root}/successor-operations/${encodeURIComponent(id)}`),
  usage: (id: string) => http.get<any, EngineeringEnvelope<BaselineUsage[]>>(`${root}/${encodeURIComponent(id)}/where-used`),
  diff: (before: string, after: string) => http.get<any, EngineeringEnvelope<{ before: EngineeringReference; after: EngineeringReference; changes: { path: string; before: unknown; after: unknown }[] }>>(`${root}/diff`, { params: { before, after } }),
}
