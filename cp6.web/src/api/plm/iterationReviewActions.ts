import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from './engineeringProducts'
import type { NativeReviewTask, NativeReviewCommand, NativeReviewResult, NativeReviewResolution } from './requirementSetReview'
import { requirementSetReviewApi } from './requirementSetReview'
export interface IterationReceipt {
  receiptId: string; receiptStreamId: string; receiptRevision: string; previousReceiptId: string | null
  submissionId: string; decisionId: string; producerOwner: string; action: string; application: string; outcomeCode: string
  subject: { businessOwner: string; subjectId: string; businessRevision: string }
}
export interface IterationApplication {
  iterationId: string; submissionId: string; etag: string; state: string; controlRowVersion: string | null; businessStage: string
  receiptStreamId: string | null; decisionId: string | null; decision: string | null; receipt: IterationReceipt | null
  application: { id: string; approved: boolean; businessStage: string; appliedIteration: EngineeringReference } | null
  withdrawalDelivery?: { state: string; attempts: number; error: string | null } | null
}
export interface IssueTarget { kind: 'PROCESS' | 'CANDIDATE'; candidate: EngineeringReference | null; path: string | null }
export interface ReviewIssue {
  id: string; iterationId: string; submissionId: string; revision: string; target: IssueTarget; text: string; authorId: string
  responsibleId: string; dueDate: string; severity: 'BLOCKING' | 'NON_BLOCKING'; state: string; verifiedBy: string | null
  responses: { id: string; actorId: string; at: string; text: string; evidence: EngineeringReference[]; correctiveNote: boolean }[]
}
export interface IssueContext { iterationId: string; etag: string; issues: ReviewIssue[] }
export interface IssueCommand {
  operationId: string; submissionId: string; issueId: string; action: 'CREATE' | 'RESPOND' | 'VERIFY' | 'DOWNGRADE' | 'NOTE'
  expectedRevision: string | null; text: string; target: IssueTarget | null; responsibleId: string | null; dueDate: string | null
  evidence: EngineeringReference[] | null; reason: string
}
export interface ApplicationResult { operationId: string; objectId: string; receipt: IterationReceipt }
export interface IssueResult { operationId: string; iterationId: string; issue: ReviewIssue }
export interface WithdrawalResult {
  operationId: string; iterationId: string; state: 'WITHDRAWN' | 'CONFLICT'; transportCommandId: string | null
  appliedReceipt: IterationReceipt | null
  fact: { submissionId: string; owner: string; ownerCommandId: string; subject: { subjectId: string }; scope: { environmentId: string; tenantId: string }; actor: { actualHandlerId: string } } | null
}
export type ActionKind = 'APPLICATION' | 'ISSUE' | 'WITHDRAWAL'
export type ActionResult = ApplicationResult | IssueResult | WithdrawalResult
export interface ActionOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ActionResult | null }
export interface IterationTask { submissionId: string; iterationId: string; task: NativeReviewTask }
const root = '/engineering-iterations/owner-v1'
const operationPath = { APPLICATION: 'review-application-operations', ISSUE: 'review-issue-operations', WITHDRAWAL: 'review-withdrawal-operations' }
export const iterationReviewActions = {
  context: (id: string, submission: string) => http.get<any, EngineeringEnvelope<IterationApplication>>(`${root}/${encodeURIComponent(id)}/review-submissions/${encodeURIComponent(submission)}/application`),
  issues: (id: string) => http.get<any, EngineeringEnvelope<IssueContext>>(`${root}/${encodeURIComponent(id)}/review-issues`),
  issue: (id: string, etag: string, body: IssueCommand) => http.post<any, EngineeringEnvelope<IssueResult>>(`${root}/${encodeURIComponent(id)}/review-issues`, body, { headers: { 'If-Match': etag } }),
  apply: (id: string, etag: string, body: { operationId: string; submissionId: string; expectedReceiptId: string | null; reason: string }) => http.post<any, EngineeringEnvelope<ApplicationResult>>(`${root}/${encodeURIComponent(id)}/review-application`, body, { headers: { 'If-Match': etag } }),
  withdraw: async (id: string, submission: string, version: string, body: { commandId: string; reason: string }): Promise<EngineeringEnvelope<WithdrawalResult>> => {
    try { return await http.post<any, EngineeringEnvelope<WithdrawalResult>>(`${root}/${encodeURIComponent(id)}/review-submissions/${encodeURIComponent(submission)}/withdraw`, body, { headers: { 'If-Match': `"${version}"` } }) }
    catch (e: any) { if (e?.response?.status === 409 && e?.response?.data?.data?.state === 'CONFLICT') return e.response.data; throw e }
  },
  operation: (kind: ActionKind, id: string, close: boolean) => close
    ? http.post<any, EngineeringEnvelope<ActionOperation>>(`${root}/${operationPath[kind]}/${encodeURIComponent(id)}/close`, {})
    : http.get<any, EngineeringEnvelope<ActionOperation>>(`${root}/${operationPath[kind]}/${encodeURIComponent(id)}`),
  tasks: (submission: string) => http.get<any, IterationTask[]>(`/oa/tasks/iteration-review/${encodeURIComponent(submission)}`),
  task: (id: string) => requirementSetReviewApi.task(id),
  decide: (id: string, input: NativeReviewCommand): Promise<NativeReviewResult> => requirementSetReviewApi.decide(id, input),
  decisionOperation: (id: string, task: string, close: boolean): Promise<NativeReviewResolution> => requirementSetReviewApi.decisionOperation(id, task, close),
}
