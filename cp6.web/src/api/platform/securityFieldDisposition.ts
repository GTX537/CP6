import http from '@/api/http'
import type { DispositionHold } from './privacyDisposition'
const root = '/platform/privacy-dispositions-v1/security-fields'
export interface SecurityFieldContext { environmentId: string; actorTenantId: string; actorId: string; owner: string; action: string; actions: string[]; fields: string[]; uncoveredOwners: string[] }
export interface SecurityFieldTarget { logId: string; policyId: string }
export interface SecurityFieldPolicy { policyId: string; version: string; targetTenantId: string; basisRef: string; enabled: boolean; notBeforeUtc: string }
export interface SecurityFieldScope extends SecurityFieldTarget { environmentId: string; operatorTenantId: string; targetTenantId: string; subjectId: string; eventType: number; originalCreatedAt: string; targetVersion: string; beforeDigest: string; policyVersion: string; basisRef: string; notBeforeUtc: string; holdVersion: string | null; fields: string[]; uncoveredOwners: string[] }
export interface SecurityFieldPreview { scope: SecurityFieldScope; scopeDigest: string; classifiedValueCount: number; hold: DispositionHold | null; holdReviewOverdue: boolean; blockers: string[] }
export interface SecurityFieldExecution { planId: string; operationId: string; logId: string; operatorId: string; requesterId: string; approverId: string; owner: string; ownerOutcome: 'APPLIED'; aggregateState: 'PARTIAL'; beforeVersion: string; afterVersion: string; clearedValueCount: number; occurredAt: string; fields: string[]; uncoveredOwners: string[] }
export interface SecurityFieldPlan { planId: string; requesterId: string; version: string; state: string; approverId: string | null; scope: SecurityFieldScope; scopeDigest: string; currentHold: DispositionHold | null; currentBlockers: string[]; approvalCurrent: boolean; currentClassifiedValueCount: number | null; execution: SecurityFieldExecution | null }
export interface SecurityFieldOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'NOT_FOUND'; kind: string; planId: string | null; version: string | null; policyId: string | null; logId: string | null; execution: SecurityFieldExecution | null }
export interface SecurityFieldPolicyCommand { operationId: string; policyId: string; targetTenantId: string; expectedVersion: string | null; basisRef: string; enabled: boolean; notBeforeUtc: string }
export interface SecurityFieldHoldCommand { operationId: string; expectedVersion: string | null; active: boolean; basisRef: string; reviewAt: string | null }
export interface SecurityFieldRequest { operationId: string; requestRef: string; target: SecurityFieldTarget; expectedScopeDigest: string }
export interface SecurityFieldDecision { operationId: string; expectedVersion: string; decision: 'APPROVE' | 'REJECT' | 'REVOKE'; decisionRef: string }
export interface SecurityFieldExecute { operationId: string; expectedVersion: string; expectedScopeDigest: string; confirmationRef: string }
export const securityFieldDispositionApi = {
  context: () => http.get(`${root}/context`) as unknown as Promise<SecurityFieldContext>,
  policy: (id: string) => http.get(`${root}/policies/${id}`) as unknown as Promise<SecurityFieldPolicy | null>,
  configure: (input: SecurityFieldPolicyCommand) => http.post(`${root}/policies`, input) as unknown as Promise<SecurityFieldOperation>,
  hold: (log: string) => http.get(`${root}/logs/${log}/hold`) as unknown as Promise<DispositionHold | null>,
  setHold: (log: string, input: SecurityFieldHoldCommand) => http.post(`${root}/logs/${log}/hold`, input) as unknown as Promise<SecurityFieldOperation>,
  preview: (input: SecurityFieldTarget) => http.post(`${root}/preview`, input) as unknown as Promise<SecurityFieldPreview>,
  request: (input: SecurityFieldRequest) => http.post(`${root}/plans`, input) as unknown as Promise<SecurityFieldOperation>,
  plan: (id: string) => http.get(`${root}/plans/${id}`) as unknown as Promise<SecurityFieldPlan>,
  decide: (id: string, input: SecurityFieldDecision) => http.post(`${root}/plans/${id}/decisions`, input) as unknown as Promise<SecurityFieldOperation>,
  execute: (id: string, input: SecurityFieldExecute) => http.post(`${root}/plans/${id}/execute`, input) as unknown as Promise<SecurityFieldOperation>,
  operation: (id: string) => http.get(`${root}/operations/${id}`) as unknown as Promise<SecurityFieldOperation>,
  close: (id: string) => http.post(`${root}/operations/${id}/close`) as unknown as Promise<SecurityFieldOperation>,
}
