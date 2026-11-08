import http from '@/api/http'
const root = '/platform/privacy-dispositions-v1/tenant-export-payloads'
export interface TenantPayloadContext { environmentId: string; actorTenantId: string; actorId: string; owner: string; action: string; actions: string[]; maximumRetentionDays: number; uncoveredOwners: string[] }
export interface TenantPayloadTarget { artifactId: string; policyId: string }
export interface TenantPayloadPolicy { policyId: string; version: string; targetTenantId: string; basisRef: string; enabled: boolean; minimumRetentionDays: number; requireAccessClosed: boolean }
export interface TenantPayloadScope extends TenantPayloadTarget { environmentId: string; operatorTenantId: string; targetTenantId: string; payloadVersion: string | null; payloadLength: number | null; originalSha256: string; originalLength: number; purpose: string; capturedAt: string; accessExpiresAt: string; policyVersion: string; basisRef: string; minimumRetentionDays: number; requireAccessClosed: boolean; earliestDispositionAt: string; holdVersion: string | null }
export interface TenantPayloadPreview { scope: TenantPayloadScope; scopeDigest: string; hold: TenantPayloadHold; holdReviewOverdue: boolean; accessState: string; blockers: string[] }
export interface TenantPayloadExecution { planId: string; operationId: string; artifactId: string; operatorId: string; requesterId: string; approverId: string; owner: string; ownerOutcome: 'APPLIED'; aggregateState: 'PARTIAL'; removedBytes: number; occurredAt: string; uncoveredOwners: string[] }
export interface TenantPayloadPlan { planId: string; requesterId: string; version: string; state: string; approverId: string | null; scope: TenantPayloadScope; scopeDigest: string; currentHold: TenantPayloadHold; accessState: string; currentBlockers: string[]; approvalCurrent: boolean; execution: TenantPayloadExecution | null }
export interface TenantPayloadOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'NOT_FOUND'; kind: string; planId: string | null; version: string | null; policyId: string | null; artifactId: string | null; execution: TenantPayloadExecution | null }
export interface TenantPayloadPolicyCommand { operationId: string; policyId: string; targetTenantId: string; expectedVersion: string | null; basisRef: string; enabled: boolean; minimumRetentionDays: number; requireAccessClosed: boolean }
export interface TenantPayloadRequest { operationId: string; requestRef: string; target: TenantPayloadTarget; expectedScopeDigest: string }
export interface TenantPayloadDecision { operationId: string; expectedVersion: string; decision: 'APPROVE' | 'REJECT' | 'REVOKE'; decisionRef: string }
export interface TenantPayloadExecute { operationId: string; expectedVersion: string; expectedScopeDigest: string; confirmationRef: string }
export const tenantExportPayloadDispositionApi = {
  context: () => http.get(`${root}/context`) as unknown as Promise<TenantPayloadContext>,
  policy: (id: string) => http.get(`${root}/policies/${id}`) as unknown as Promise<TenantPayloadPolicy | null>,
  configure: (input: TenantPayloadPolicyCommand) => http.post(`${root}/policies`, input) as unknown as Promise<TenantPayloadOperation>,
  hold: (artifact: string) => http.get(`${root}/artifacts/${artifact}/hold`) as unknown as Promise<TenantPayloadHold>,
  preview: (input: TenantPayloadTarget) => http.post(`${root}/preview`, input) as unknown as Promise<TenantPayloadPreview>,
  request: (input: TenantPayloadRequest) => http.post(`${root}/plans`, input) as unknown as Promise<TenantPayloadOperation>,
  plan: (id: string) => http.get(`${root}/plans/${id}`) as unknown as Promise<TenantPayloadPlan>,
  decide: (id: string, input: TenantPayloadDecision) => http.post(`${root}/plans/${id}/decisions`, input) as unknown as Promise<TenantPayloadOperation>,
  execute: (id: string, input: TenantPayloadExecute) => http.post(`${root}/plans/${id}/execute`, input) as unknown as Promise<TenantPayloadOperation>,
  operation: (id: string) => http.get(`${root}/operations/${id}`) as unknown as Promise<TenantPayloadOperation>,
  close: (id: string) => http.post(`${root}/operations/${id}/close`) as unknown as Promise<TenantPayloadOperation>,
}

export interface TenantPayloadHold { version: string | null; activeCount: number; overdueCount: number }
