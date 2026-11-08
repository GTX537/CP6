import http from '@/api/http'
const root = '/platform/privacy-dispositions-v1'
export interface DispositionContext { environmentId: string; actorTenantId: string; actorId: string; actions: string[] }
export interface DispositionTarget { targetTenantId: string; subjectId: string; policyId: string }
export interface DispositionPolicy { policyId: string; version: string; targetTenantId: string; basisRef: string; enabled: boolean }
export interface DispositionHold { version: string; active: boolean; basisRef: string; reviewAt: string | null; ownerId: string; ownerTenantId: string }
export interface DispositionScope extends DispositionTarget { environmentId: string; targetVersion: string; policyVersion: string; basisRef: string; holdVersion: string | null; action: string; fields: string[]; effects: string[]; uncoveredOwners: string[] }
export interface DispositionPreview { scope: DispositionScope; scopeDigest: string; hold: DispositionHold | null; holdReviewOverdue: boolean; blockers: string[] }
export interface DispositionExecution { planId: string; operationId: string; operatorId: string; operatorTenantId: string; targetTenantId: string; subjectId: string; owner: string; action: string; beforeVersion: string; afterVersion: string; occurredAt: string; ownerOutcome: 'APPLIED'; state: 'PARTIAL'; fields: string[]; uncoveredOwners: string[] }
export interface DispositionPlan { planId: string; requestRef: string; requesterId: string; version: string; state: string; approverId: string | null; scope: DispositionScope; scopeDigest: string; currentHold: DispositionHold | null; holdReviewOverdue: boolean; currentBlockers: string[]; approvalCurrent: boolean; execution: DispositionExecution | null }
export interface DispositionOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'NOT_FOUND'; kind: string; planId: string | null; version: string | null; policyId: string | null; targetTenantId: string | null; subjectId: string | null; execution: DispositionExecution | null }
export interface DispositionPolicyCommand { operationId: string; policyId: string; targetTenantId: string; expectedVersion: string | null; basisRef: string; enabled: boolean }
export interface DispositionHoldCommand { operationId: string; targetTenantId: string; subjectId: string; expectedVersion: string | null; active: boolean; basisRef: string; reviewAt: string | null }
export interface DispositionPlanCommand { operationId: string; requestRef: string; target: DispositionTarget; expectedScopeDigest: string }
export interface DispositionDecision { operationId: string; expectedVersion: string; decision: 'APPROVE' | 'REJECT' | 'REVOKE'; decisionRef: string }
export interface DispositionExecute { operationId: string; expectedVersion: string; expectedScopeDigest: string; confirmationRef: string }
export const privacyDispositionApi = {
  context: () => http.get(`${root}/context`) as unknown as Promise<DispositionContext>,
  policy: (id: string) => http.get(`${root}/policies/${id}`) as unknown as Promise<DispositionPolicy | null>,
  configure: (input: DispositionPolicyCommand) => http.post(`${root}/policies`, input) as unknown as Promise<DispositionOperation>,
  hold: (tenant: string, subject: string) => http.get(`${root}/holds/${tenant}/${subject}`) as unknown as Promise<DispositionHold | null>,
  setHold: (input: DispositionHoldCommand) => http.post(`${root}/holds`, input) as unknown as Promise<DispositionOperation>,
  preview: (input: DispositionTarget) => http.post(`${root}/preview`, input) as unknown as Promise<DispositionPreview>,
  request: (input: DispositionPlanCommand) => http.post(`${root}/plans`, input) as unknown as Promise<DispositionOperation>,
  plan: (id: string) => http.get(`${root}/plans/${id}`) as unknown as Promise<DispositionPlan>,
  decide: (id: string, input: DispositionDecision) => http.post(`${root}/plans/${id}/decisions`, input) as unknown as Promise<DispositionOperation>,
  execute: (id: string, input: DispositionExecute) => http.post(`${root}/plans/${id}/execute`, input) as unknown as Promise<DispositionOperation>,
  execution: (id: string) => http.get(`${root}/plans/${id}/execution`) as unknown as Promise<DispositionExecution | null>,
  operation: (id: string) => http.get(`${root}/operations/${id}`) as unknown as Promise<DispositionOperation>,
  close: (id: string) => http.post(`${root}/operations/${id}/close`) as unknown as Promise<DispositionOperation>,
}
