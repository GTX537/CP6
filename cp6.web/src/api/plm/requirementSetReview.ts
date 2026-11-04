import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from './engineeringProducts'
import type { RequirementSetSnapshot } from './requirementSet'

export interface ReviewRequirement { code: string; description: string; roleId: number; minimumApprovals: number }
export interface ReviewPolicyCommand {
  operationId: string; state: 'ENABLED' | 'REVOKED'; requirements: ReviewRequirement[]; externalRequirements: string[]
  separateAuthorAndReviewer: boolean; effectiveFrom: string; effectiveTo: string; reason: string
}
export interface ReviewPolicy { reference: EngineeringReference; fact: { actorId: string; recordedAt: string; body: ReviewPolicyCommand } }
export interface ReviewSubmission {
  submissionId: string; requirementSet: EngineeringReference; policy: EngineeringReference; starterId: string; frozenAt: string
  snapshotDigest: string; state: string; transportState: string; isApproved: boolean
}
export interface ReviewContext { tenantId: string; requirementSetId: string; etag: string; requirementSet: RequirementSetSnapshot; policies: ReviewPolicy[]; submissions: ReviewSubmission[] }
export interface ReviewSubmitCommand {
  operationId: string; requirementSet: EngineeringReference; policy: EngineeringReference; reviewBasis: string
  declaredChangeRelations: string[]; reviewRoundId: string; replacesSubmissionId: string | null; reason: string
}
export interface ReviewResult { operationId: string; requirementSetId: string; kind: 'POLICY' | 'SUBMIT'; reference: EngineeringReference; submission?: ReviewSubmission | null }
export interface ReviewOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ReviewResult | null }
export interface ApplicationReceipt {
  receiptId: string; receiptStreamId: string; submissionId: string; decisionId: string; producerOwner: string
  subject: { subjectId: string; businessRevision: string }; application: string; outcomeCode: string
  businessResultRef: { owner: string; id: string; version: string } | null
}
export interface RequirementSetApproval { id: string; requirementSet: EngineeringReference; policy: EngineeringReference; submissionId: string; decisionId: string; approved: boolean; actorId: string; appliedAt: string }
export interface ApplicationContext { requirementSetId: string; submissionId: string; etag: string; state: string; receiptStreamId: string | null; decisionId: string | null; decision: string | null; receipt: ApplicationReceipt | null; application: RequirementSetApproval | null }
export interface ReviewDetail {
  submission: ReviewSubmission; policy: ReviewPolicy; requirementSet: RequirementSetSnapshot; previous: RequirementSetSnapshot | null
  package: { requirementSet: EngineeringReference; previous: EngineeringReference | null; changeDigest: string; reviewBasis: string; declaredChangeRelations: string[]; body: RequirementSetSnapshot['body'] }
  application: ApplicationContext
}
export interface ApplicationCommand { operationId: string; submissionId: string; expectedReceiptId: string | null; reason: string }
export interface ApplicationResult { operationId: string; objectId: string; receipt: ApplicationReceipt }
export interface ApplicationOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ApplicationResult | null }
export interface WithdrawalAuthorization { commandId: string; previousCommandId: string | null; state: string; actualHandlerId: string; requesterId: string; grantId: string | null; grantRevision: string | null }
export interface WithdrawalContext { requirementSetId: string; submissionId: string; state: string; controlRowVersion: string; canWithdraw: boolean; authorizations: WithdrawalAuthorization[]; delivery: { state: string; error: string | null } | null }
export interface WithdrawalResult { operationId: string; requirementSetId: string; state: 'WITHDRAWN' | 'CONFLICT'; fact: { submissionId: string } | null; transportCommandId: string | null; appliedReceipt: ApplicationReceipt | null; previousCommandId: string | null }
export interface WithdrawalOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: WithdrawalResult | null }
export interface BusinessGrantCommand { operationId: string; submissionId: string; grantId: string | null; expectedVersion: string | null; delegateId: string; fromUtc: string; toUtc: string; actions: string[]; enabled: boolean; reason: string }
export interface BusinessGrant extends Omit<BusinessGrantCommand, 'expectedVersion' | 'grantId'> { grantId: string; revision: string; version: string; requirementSetId: string; grantorId: string; recordedAtUtc: string }
export interface BusinessGrantResult { operationId: string; grant: BusinessGrant }
export interface BusinessGrantOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: BusinessGrantResult | null }
export interface NativeReviewTask { taskId: string; instanceId: string; taskStatus: number; instanceStatus: number; taskRowVersion: string; instanceRowVersion: string }
export interface RequirementSetReviewTask { submissionId: string; requirementSetId: string; requirementSetVersion: string; task: NativeReviewTask }
export interface NativeReviewCommand { commandId: string; decision: 'approve' | 'reject'; comment: string; taskRowVersion: string; instanceRowVersion: string }
export interface NativeReviewResult { commandId: string; state: 'COMPLETED'; result: { voteId: string; decisionId: string | null; task: NativeReviewTask }; queryRef: string }
export interface NativeReviewResolution { commandId: string; state: 'COMPLETED' | 'CLOSED' | 'UNKNOWN'; result?: NativeReviewResult | null }
const root = '/requirement-set/owner-v1'
function actingHeaders(behalf: string | null) { return behalf ? { 'X-Acting-As': behalf } : {} }
async function withdrawalPost(url: string, version: string, command: { commandId: string; reason: string }, behalf: string | null) {
  try { return await http.post<any, EngineeringEnvelope<WithdrawalResult>>(url, command, { headers: { 'If-Match': `"${version}"`, ...actingHeaders(behalf) } }) }
  catch (error: any) { if (error?.response?.status === 409 && error.response.data?.data?.state === 'CONFLICT') return error.response.data as EngineeringEnvelope<WithdrawalResult>; throw error }
}
export const requirementSetReviewApi = {
  withdrawalContext: (id: string, submission: string) => http.get<any, EngineeringEnvelope<WithdrawalContext>>(`${root}/${encodeURIComponent(id)}/review-submissions/${encodeURIComponent(submission)}/withdraw-context`),
  withdraw: (id: string, submission: string, version: string, command: { commandId: string; reason: string }, behalf: string | null, previous: string | null = null) => withdrawalPost(`${root}/${encodeURIComponent(id)}/review-submissions/${encodeURIComponent(submission)}/${previous ? `withdrawals/${encodeURIComponent(previous)}/reauthorize` : 'withdraw'}`, version, command, behalf),
  withdrawalOperation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<WithdrawalOperation>>(`${root}/review-withdrawal-operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<WithdrawalOperation>>(`${root}/review-withdrawal-operations/${encodeURIComponent(id)}`),
  businessGrants: (id: string, submission: string) => http.get<any, EngineeringEnvelope<BusinessGrant[]>>(`${root}/${encodeURIComponent(id)}/review-submissions/${encodeURIComponent(submission)}/business-grants`),
  businessDelegates: (id: string) => http.get<any, EngineeringEnvelope<{ id: string; name: string }[]>>(`${root}/${encodeURIComponent(id)}/review-business-delegates`),
  grantBusiness: (id: string, input: BusinessGrantCommand) => http.post<any, EngineeringEnvelope<BusinessGrantResult>>(`${root}/${encodeURIComponent(id)}/review-business-grants`, input),
  businessGrantOperation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<BusinessGrantOperation>>(`${root}/review-business-grant-operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<BusinessGrantOperation>>(`${root}/review-business-grant-operations/${encodeURIComponent(id)}`),
  context: (id: string) => http.get<any, EngineeringEnvelope<ReviewContext>>(`${root}/${encodeURIComponent(id)}/review-context`),
  roles: () => http.get<any, EngineeringEnvelope<{ roleId: number; roleName: string }[]>>(`${root}/review-roles`),
  policy: (id: string, etag: string, input: ReviewPolicyCommand) => http.post<any, EngineeringEnvelope<ReviewResult>>(`${root}/${encodeURIComponent(id)}/review-policies`, input, { headers: { 'If-Match': etag } }),
  submit: (id: string, etag: string, input: ReviewSubmitCommand) => http.post<any, EngineeringEnvelope<ReviewResult>>(`${root}/${encodeURIComponent(id)}/review-submissions`, input, { headers: { 'If-Match': etag } }),
  operation: (id: string, close: boolean) => close
    ? http.post<any, EngineeringEnvelope<ReviewOperation>>(`${root}/review-operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, EngineeringEnvelope<ReviewOperation>>(`${root}/review-operations/${encodeURIComponent(id)}`),
  detail: (id: string, submission: string) => http.get<any, EngineeringEnvelope<ReviewDetail>>(`${root}/${encodeURIComponent(id)}/review-submissions/${encodeURIComponent(submission)}`),
  apply: (id: string, etag: string, input: ApplicationCommand) => http.post<any, EngineeringEnvelope<ApplicationResult>>(`${root}/${encodeURIComponent(id)}/review-application`, input, { headers: { 'If-Match': etag } }),
  applicationOperation: (id: string, close: boolean) => close
    ? http.post<any, EngineeringEnvelope<ApplicationOperation>>(`${root}/review-application-operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, EngineeringEnvelope<ApplicationOperation>>(`${root}/review-application-operations/${encodeURIComponent(id)}`),
  tasks: (id: string) => http.get<any, RequirementSetReviewTask[]>(`/oa/tasks/requirement-set-review/${encodeURIComponent(id)}`),
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
