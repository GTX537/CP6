import http from '../http'
import type { EngineeringEnvelope } from '../plm/engineeringProducts'
import { locationRuleReviewApi, type LocationRuleReviewScope, type ApplicationReceipt } from './locationRuleReview'
export type { LocationRuleReviewScope } from './locationRuleReview'
export interface WithdrawalAuthorization { commandId: string; previousCommandId: string | null; state: string; actualHandlerId: string; requesterId: string; grantId: string | null; grantRevision: string | null }
export interface WithdrawalContext { locationRuleId: string; submissionId: string; state: 'IN_REVIEW' | 'WITHDRAWN' | 'APPLIED' | 'NOT_CURRENT'; controlRowVersion: string; canWithdraw: boolean; authorizations: WithdrawalAuthorization[]; delivery: { state: string; error: string | null } | null }
export interface WithdrawalResult {
  operationId: string; locationRuleId: string; state: 'WITHDRAWN' | 'CONFLICT'; transportCommandId: string | null; appliedReceipt: ApplicationReceipt | null; previousCommandId: string | null
  fact: { submissionId: string; owner: string; scope: { environmentId: string; tenantId: string }; actor: { actualHandlerId: string } } | null
}
export interface WithdrawalOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: WithdrawalResult | null }
export interface BusinessGrantCommand { operationId: string; submissionId: string; grantId: string | null; expectedVersion: string | null; delegateId: string; fromUtc: string; toUtc: string; actions: string[]; enabled: boolean; reason: string }
export interface BusinessGrant extends Omit<BusinessGrantCommand, 'expectedVersion' | 'grantId'> { grantId: string; revision: string; version: string; locationRuleId: string; grantorId: string; recordedAtUtc: string }
export interface BusinessGrantResult { operationId: string; grant: BusinessGrant }
export interface BusinessGrantOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: BusinessGrantResult | null }
const root = '/location-rule/owner-v1', operations = root, id = encodeURIComponent
async function withdrawalPost(url: string, version: string, command: { commandId: string; reason: string }, behalf: string | null) {
  try { return await http.post<any, EngineeringEnvelope<WithdrawalResult>>(url, command, { headers: { 'If-Match': `"${version}"`, ...(behalf ? { 'X-Acting-As': behalf } : {}) } }) }
  catch (error: any) {
    if (error?.response?.status === 409 && error.response.data?.code === 0 && error.response.data?.data?.state === 'CONFLICT') return error.response.data as EngineeringEnvelope<WithdrawalResult>
    throw error
  }
}
export const locationRuleWithdrawalApi = {
  scope: locationRuleReviewApi.scope,
  withdrawalContext: (tool: string, submission: string) => http.get<any, EngineeringEnvelope<WithdrawalContext>>(`${root}/${id(tool)}/review-submissions/${id(submission)}/withdraw-context`),
  withdraw: (rule: string, submission: string, version: string, command: { commandId: string; reason: string }, behalf: string | null, previous: string | null = null) => withdrawalPost(`${root}/${id(rule)}/review-submissions/${id(submission)}/${previous ? `withdrawals/${id(previous)}/reauthorize` : 'withdraw'}`, version, command, behalf),
  withdrawalOperation: (operation: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<WithdrawalOperation>>(`${operations}/review-withdrawal-operations/${id(operation)}/close`, {}) : http.get<any, EngineeringEnvelope<WithdrawalOperation>>(`${operations}/review-withdrawal-operations/${id(operation)}`),
  businessGrants: (tool: string, submission: string) => http.get<any, EngineeringEnvelope<BusinessGrant[]>>(`${root}/${id(tool)}/review-submissions/${id(submission)}/business-grants`),
  businessDelegates: (tool: string) => http.get<any, EngineeringEnvelope<{ id: string; name: string }[]>>(`${root}/${id(tool)}/review-business-delegates`),
  grantBusiness: (tool: string, input: BusinessGrantCommand) => http.post<any, EngineeringEnvelope<BusinessGrantResult>>(`${root}/${id(tool)}/review-business-grants`, input),
  businessGrantOperation: (operation: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<BusinessGrantOperation>>(`${operations}/review-business-grant-operations/${id(operation)}/close`, {}) : http.get<any, EngineeringEnvelope<BusinessGrantOperation>>(`${operations}/review-business-grant-operations/${id(operation)}`),
}
