import http from '@/api/http'
import type { DispositionHold } from './privacyDisposition'
const root = '/platform/privacy-dispositions-v1/export-payloads'
export interface PayloadContext { environmentId: string; actorTenantId: string; actorId: string; owner: string; action: string; actions: string[]; maximumRetentionDays: number; uncoveredOwners: string[] }
export interface PayloadTarget { artifactId: string; policyId: string }
export interface PayloadPolicy { policyId: string; version: string; targetTenantId: string; basisRef: string; enabled: boolean; minimumRetentionDays: number; requireAccessClosed: boolean }
export interface PayloadScope extends PayloadTarget { environmentId: string; operatorTenantId: string; targetTenantId: string; subjectId: string; payloadVersion: string | null; payloadLength: number | null; originalSha256: string; originalLength: number; purpose: string; capturedAt: string; accessExpiresAt: string; policyVersion: string; basisRef: string; minimumRetentionDays: number; requireAccessClosed: boolean; earliestDispositionAt: string; holdVersion: string | null }
export interface PayloadPreview { scope: PayloadScope; scopeDigest: string; hold: DispositionHold | null; holdReviewOverdue: boolean; accessState: string; blockers: string[] }
export interface PayloadExecution { planId: string; operationId: string; artifactId: string; operatorId: string; requesterId: string; approverId: string; owner: string; ownerOutcome: 'APPLIED'; aggregateState: 'PARTIAL'; removedBytes: number; occurredAt: string; uncoveredOwners: string[] }
export interface PayloadPlan { planId: string; requesterId: string; version: string; state: string; approverId: string | null; scope: PayloadScope; scopeDigest: string; currentHold: DispositionHold | null; accessState: string; currentBlockers: string[]; approvalCurrent: boolean; execution: PayloadExecution | null }
export interface PayloadOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'NOT_FOUND'; kind: string; planId: string | null; version: string | null; policyId: string | null; artifactId: string | null; execution: PayloadExecution | null }
export interface PayloadPolicyCommand { operationId: string; policyId: string; targetTenantId: string; expectedVersion: string | null; basisRef: string; enabled: boolean; minimumRetentionDays: number; requireAccessClosed: boolean }
export interface PayloadHoldCommand { operationId: string; expectedVersion: string | null; active: boolean; basisRef: string; reviewAt: string | null }
export interface PayloadRequest { operationId: string; requestRef: string; target: PayloadTarget; expectedScopeDigest: string }
export interface PayloadDecision { operationId: string; expectedVersion: string; decision: 'APPROVE' | 'REJECT' | 'REVOKE'; decisionRef: string }
export interface PayloadExecute { operationId: string; expectedVersion: string; expectedScopeDigest: string; confirmationRef: string }
export const exportPayloadDispositionApi = {
  context: () => http.get(`${root}/context`) as unknown as Promise<PayloadContext>,
  policy: (id: string) => http.get(`${root}/policies/${id}`) as unknown as Promise<PayloadPolicy | null>,
  configure: (input: PayloadPolicyCommand) => http.post(`${root}/policies`, input) as unknown as Promise<PayloadOperation>,
  hold: (artifact: string) => http.get(`${root}/artifacts/${artifact}/hold`) as unknown as Promise<DispositionHold | null>,
  setHold: (artifact: string, input: PayloadHoldCommand) => http.post(`${root}/artifacts/${artifact}/hold`, input) as unknown as Promise<PayloadOperation>,
  preview: (input: PayloadTarget) => http.post(`${root}/preview`, input) as unknown as Promise<PayloadPreview>,
  request: (input: PayloadRequest) => http.post(`${root}/plans`, input) as unknown as Promise<PayloadOperation>,
  plan: (id: string) => http.get(`${root}/plans/${id}`) as unknown as Promise<PayloadPlan>,
  decide: (id: string, input: PayloadDecision) => http.post(`${root}/plans/${id}/decisions`, input) as unknown as Promise<PayloadOperation>,
  execute: (id: string, input: PayloadExecute) => http.post(`${root}/plans/${id}/execute`, input) as unknown as Promise<PayloadOperation>,
  operation: (id: string) => http.get(`${root}/operations/${id}`) as unknown as Promise<PayloadOperation>,
  close: (id: string) => http.post(`${root}/operations/${id}/close`) as unknown as Promise<PayloadOperation>,
}
