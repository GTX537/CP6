import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from '../plm/engineeringProducts'
import type { RuleDraft } from './locationRule'
export interface LocationRuleReviewScope { environmentId: string; evidenceMode: 'REAL' | 'DEMO'; tenantId: string; actorId: string }
export interface LocationRuleReviewSource { reference: EngineeringReference; body: { tenantId: string; id: string; locationId: string; selectorId: string; version: string; draft: RuleDraft; referenceResolution: string; referenceBinding: unknown }; originalJson: string; parentVersion: string | null; isCurrent: boolean; authorId: string; actorId: string; state: string; etag: string; activeSubmissionId: string | null; missing: string[] }

export interface ReviewRequirement { code: string; description: string; roleId: number; minimumApprovals: number }
export interface ReviewPolicyCommand {
  operationId: string; state: 'ENABLED' | 'REVOKED'; requirements: ReviewRequirement[]; externalRequirements: string[]
  separateAuthorAndReviewer: boolean; effectiveFrom: string; effectiveTo: string; reason: string; rejectionDisposition: 'RETURNED' | 'REJECTED'
}
export interface ReviewPolicy { reference: EngineeringReference; fact: { actorId: string; recordedAt: string; body: ReviewPolicyCommand } }
export interface ReviewSubmission {
  submissionId: string; locationRule: EngineeringReference; policy: EngineeringReference; starterId: string; frozenAt: string
  snapshotDigest: string; state: string; transportState: string; isApproved: boolean
}
export interface ReviewContext { tenantId: string; locationRuleId: string; etag: string; locationRule: LocationRuleReviewSource; policies: ReviewPolicy[]; submissions: ReviewSubmission[] }
export interface ReviewSubmitCommand {
  operationId: string; locationRule: EngineeringReference; policy: EngineeringReference; reviewBasis: string
  declaredChangeRelations: string[]; reviewRoundId: string; replacesSubmissionId: string | null; reason: string
}
export interface ReviewResult { operationId: string; locationRuleId: string; kind: 'POLICY' | 'SUBMIT'; reference: EngineeringReference; submission?: ReviewSubmission | null }
export interface ReviewOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ReviewResult | null }
export interface ApplicationReceipt {
  receiptId: string; receiptStreamId: string; submissionId: string; decisionId: string; producerOwner: string
  subject: { subjectId: string; businessRevision: string }; application: string; outcomeCode: string
  businessResultRef: { owner: string; id: string; version: string } | null
}
export interface LocationRuleApproval { id: string; locationRule: EngineeringReference; policy: EngineeringReference; submissionId: string; decisionId: string; approved: boolean; actorId: string; appliedAt: string }
export interface ApplicationContext { locationRuleId: string; submissionId: string; etag: string; state: string; receiptStreamId: string | null; decisionId: string | null; decision: string | null; receipt: ApplicationReceipt | null; application: LocationRuleApproval | null }
export interface ReviewDetail {
  submission: ReviewSubmission; policy: ReviewPolicy; locationRule: LocationRuleReviewSource; previous: LocationRuleReviewSource | null
  package: { locationRule: EngineeringReference; previous: EngineeringReference | null; changeDigest: string; reviewBasis: string; declaredChangeRelations: string[]; body: LocationRuleReviewSource['body'] }
  application: ApplicationContext
}
export interface ApplicationCommand { operationId: string; submissionId: string; expectedReceiptId: string | null; reason: string }
export interface ApplicationResult { operationId: string; objectId: string; receipt: ApplicationReceipt }
export interface ApplicationOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ApplicationResult | null }
export interface NativeReviewTask { taskId: string; instanceId: string; taskStatus: number; instanceStatus: number; taskRowVersion: string; instanceRowVersion: string }
export interface LocationRuleReviewTask { submissionId: string; locationRuleId: string; locationRuleVersion: string; task: NativeReviewTask }
export interface NativeReviewCommand { commandId: string; decision: 'approve' | 'reject'; comment: string; taskRowVersion: string; instanceRowVersion: string }
export interface NativeReviewResult { commandId: string; state: 'COMPLETED'; result: { voteId: string; decisionId: string | null; task: NativeReviewTask }; queryRef: string }
export interface NativeReviewResolution { commandId: string; state: 'COMPLETED' | 'CLOSED' | 'UNKNOWN'; result?: NativeReviewResult | null }
const root = '/location-rule/owner-v1'
export const locationRuleReviewApi = {
  scope: () => http.get<any, EngineeringEnvelope<LocationRuleReviewScope>>(`${root}/review-scope`),
  context: (id: string) => http.get<any, EngineeringEnvelope<ReviewContext>>(`${root}/${encodeURIComponent(id)}/review-context`),
  roles: () => http.get<any, EngineeringEnvelope<{ roleId: number; roleName: string }[]>>(`${root}/review-roles`),
  policy: (id: string, etag: string, input: ReviewPolicyCommand) => http.post<any, EngineeringEnvelope<ReviewResult>>(`${root}/${encodeURIComponent(id)}/review-policies`, input, { headers: { 'If-Match': etag } }),
  submit: (id: string, etag: string, input: ReviewSubmitCommand) => http.post<any, EngineeringEnvelope<ReviewResult>>(`${root}/${encodeURIComponent(id)}/review-submissions`, input, { headers: { 'If-Match': etag } }),
  operation: (id: string, close: boolean) => close
    ? http.post<any, EngineeringEnvelope<ReviewOperation>>(`${root}/review-operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, EngineeringEnvelope<ReviewOperation>>(`${root}/review-operations/${encodeURIComponent(id)}`),
  detail: (id: string, submission: string) => http.get<any, EngineeringEnvelope<ReviewDetail>>(`${root}/${encodeURIComponent(id)}/review-submissions/${encodeURIComponent(submission)}`),
  apply: (id: string, etag: string, input: ApplicationCommand) => http.post<any, EngineeringEnvelope<ApplicationResult>>(`${root}/${encodeURIComponent(id)}/review-binding`, input, { headers: { 'If-Match': etag } }),
  applicationOperation: (id: string, close: boolean) => close
    ? http.post<any, EngineeringEnvelope<ApplicationOperation>>(`${root}/review-binding-operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, EngineeringEnvelope<ApplicationOperation>>(`${root}/review-binding-operations/${encodeURIComponent(id)}`),
  tasks: (id: string) => http.get<any, LocationRuleReviewTask[]>(`/oa/tasks/location-rule-review/${encodeURIComponent(id)}`),
  task: (id: string) => http.get<any, NativeReviewTask>(`/oa/tasks/${encodeURIComponent(id)}/decision-context`),
  decide: (id: string, input: NativeReviewCommand) => http.post<any, NativeReviewResult>(`/oa/tasks/${encodeURIComponent(id)}/decision`, input, { headers: { 'If-Match': `"${input.taskRowVersion}"` } }),
  decisionOperation: async (id: string, task: string, close: boolean): Promise<NativeReviewResolution> => {
    if (close) return http.post<any, NativeReviewResolution>(`/oa/tasks/${encodeURIComponent(task)}/decision-commands/${encodeURIComponent(id)}/close`, {})
    try { return { commandId: id, state: 'COMPLETED', result: await http.get<any, NativeReviewResult>(`/oa/tasks/decision-commands/${encodeURIComponent(id)}`) } }
    catch (error: any) {
      if (error?.response?.status === 404) return { commandId: id, state: 'UNKNOWN' }
      if (error?.response?.status === 409 && error?.response?.data?.code === 'COMMAND_CLOSED') return { commandId: id, state: 'CLOSED' }
      throw error
    }
  },
}
