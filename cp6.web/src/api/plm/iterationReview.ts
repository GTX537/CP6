import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from './engineeringProducts'
import type { EngineeringIterationSnapshot } from './engineeringIteration'
import type { CandidateSnapshot } from './iterationCandidate'
export interface ReviewRequirement { code: string; description: string; roleId: number; minimumApprovals: number }
export interface ReviewPolicyCommand { operationId: string; state: 'ENABLED' | 'REVOKED'; requirements: ReviewRequirement[]; rejectDisposition: 'RETURN' | 'REJECT'; separateAuthorAndReviewer: true; effectiveFrom: string; effectiveTo: string; reason: string }
export interface ReviewPolicy { reference: EngineeringReference; fact: { recordedAt: string; body: ReviewPolicyCommand } }
export interface ReviewSubmission { submissionId: string; roundId: string; iteration: EngineeringReference; package: EngineeringReference; policy: EngineeringReference; snapshotDigest: string; dispatchState: string; businessStage: string; applicationState: string }
export interface ReviewContext { iterationId: string; etag: string; iteration: EngineeringIterationSnapshot; candidates: CandidateSnapshot[]; policies: ReviewPolicy[]; submissions: ReviewSubmission[] }
export interface ReviewSubmitCommand { operationId: string; iteration: EngineeringReference; iterationEtag: string; inputVersion: string; candidates: EngineeringReference[]; policy: EngineeringReference; reviewers: string[]; reviewBasis: string; reason: string }
export interface ReviewResult { operationId: string; iterationId: string; kind: 'POLICY' | 'SUBMIT'; reference: EngineeringReference; submission?: ReviewSubmission | null }
export interface ReviewOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ReviewResult | null }
export interface ReviewDetail { submission: ReviewSubmission; package: { iteration: EngineeringReference; candidates: CandidateSnapshot[]; reviewBasis: string; reviewers: string[] }; snapshot: unknown; originalSnapshot: string; originalSubmission: string }
const root = '/engineering-iterations/owner-v1'
export const iterationReviewApi = {
  context: (id: string) => http.get<any, EngineeringEnvelope<ReviewContext>>(`${root}/${encodeURIComponent(id)}/review-context`),
  reviewers: (id: string) => http.get<any, EngineeringEnvelope<{ id: string; name: string }[]>>(`${root}/${encodeURIComponent(id)}/reviewers`),
  roles: () => http.get<any, EngineeringEnvelope<{ roleId: number; roleName: string }[]>>(`${root}/review-roles`),
  policy: (id: string, etag: string, body: ReviewPolicyCommand) => http.post<any, EngineeringEnvelope<ReviewResult>>(`${root}/${encodeURIComponent(id)}/review-policies`, body, { headers: { 'If-Match': etag } }),
  submit: (id: string, etag: string, body: ReviewSubmitCommand) => http.post<any, EngineeringEnvelope<ReviewResult>>(`${root}/${encodeURIComponent(id)}/review-submissions`, body, { headers: { 'If-Match': etag } }),
  detail: (id: string, submission: string) => http.get<any, EngineeringEnvelope<ReviewDetail>>(`${root}/${encodeURIComponent(id)}/review-submissions/${encodeURIComponent(submission)}`),
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<ReviewOperation>>(`${root}/review-operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<ReviewOperation>>(`${root}/review-operations/${encodeURIComponent(id)}`),
}
