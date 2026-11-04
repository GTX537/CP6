import http from '../http'
import type { EngineeringEnvelope, EngineeringScope } from '../plm/engineeringProducts'
export interface ToolWithdrawalScope extends EngineeringScope { environmentId: string }
export interface WithdrawalAuthorization { commandId: string; previousCommandId: string | null; state: string; actualHandlerId: string; requesterId: string; grantId: string | null; grantRevision: string | null }
export interface WithdrawalContext { environmentId: string; toolId: string; submissionId: string; state: 'IN_REVIEW' | 'WITHDRAWN'; controlRowVersion: string; canWithdraw: boolean; authorizations: WithdrawalAuthorization[]; delivery: { state: string; error: string | null } | null }
export interface WithdrawalResult {
  operationId: string; toolId: string; state: 'WITHDRAWN'; transportCommandId: string; previousCommandId: string | null
  fact: { submissionId: string; owner: string; scope: { environmentId: string; tenantId: string }; actor: { actualHandlerId: string } }
}
export interface WithdrawalOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: WithdrawalResult | null }
export interface BusinessGrantCommand { operationId: string; submissionId: string; grantId: string | null; expectedVersion: string | null; delegateId: string; fromUtc: string; toUtc: string; actions: string[]; enabled: boolean; reason: string }
export interface BusinessGrant extends Omit<BusinessGrantCommand, 'expectedVersion' | 'grantId'> { grantId: string; revision: string; version: string; toolId: string; grantorId: string; recordedAtUtc: string }
export interface BusinessGrantResult { operationId: string; grant: BusinessGrant }
export interface BusinessGrantOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: BusinessGrantResult | null }
const root = '/wms/specials/v1/tools', operations = '/wms/tool-approval/v1', id = encodeURIComponent
export const toolWithdrawalApi = {
  scope: () => http.get<any, EngineeringEnvelope<ToolWithdrawalScope>>(`${operations}/withdrawal-scope`),
  withdrawalContext: (tool: string, submission: string) => http.get<any, EngineeringEnvelope<WithdrawalContext>>(`${root}/${id(tool)}/approval-submissions/${id(submission)}/withdraw-context`),
  withdraw: (tool: string, submission: string, version: string, command: { commandId: string; reason: string }, behalf: string | null, previous: string | null = null) => http.post<any, EngineeringEnvelope<WithdrawalResult>>(`${root}/${id(tool)}/approval-submissions/${id(submission)}/${previous ? `withdrawals/${id(previous)}/reauthorize` : 'withdraw'}`, command, { headers: { 'If-Match': `"${version}"`, ...(behalf ? { 'X-Acting-As': behalf } : {}) } }),
  withdrawalOperation: (operation: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<WithdrawalOperation>>(`${operations}/withdrawal-operations/${id(operation)}/close`, {}) : http.get<any, EngineeringEnvelope<WithdrawalOperation>>(`${operations}/withdrawal-operations/${id(operation)}`),
  businessGrants: (tool: string, submission: string) => http.get<any, EngineeringEnvelope<BusinessGrant[]>>(`${root}/${id(tool)}/approval-submissions/${id(submission)}/business-grants`),
  businessDelegates: (tool: string) => http.get<any, EngineeringEnvelope<{ id: string; name: string }[]>>(`${root}/${id(tool)}/approval-business-delegates`),
  grantBusiness: (tool: string, input: BusinessGrantCommand) => http.post<any, EngineeringEnvelope<BusinessGrantResult>>(`${root}/${id(tool)}/approval-business-grants`, input),
  businessGrantOperation: (operation: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<BusinessGrantOperation>>(`${operations}/business-grant-operations/${id(operation)}/close`, {}) : http.get<any, EngineeringEnvelope<BusinessGrantOperation>>(`${operations}/business-grant-operations/${id(operation)}`),
}
