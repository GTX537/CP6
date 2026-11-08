import http from '@/api/http'
const root = '/platform/privacy-dispositions-v1/restore'
export interface RestoreCandidate { id: string; registrationVersion: string; backupSha256: string; displayName: string }
export interface RestoreContext { environmentId: string; actorTenantId: string; actorId: string; sourceVersion: string; actions: string[]; candidates: RestoreCandidate[] }
export interface RestoreDatabase { name: string; address: string; port: number; oid: number }
export interface RestoreScope { candidateId: string; registrationVersion: string; backupSha256: string; sourceVersion: string; source: RestoreDatabase; target: RestoreDatabase; tombstoneCount: number; uncoveredOwners: string[] }
export interface RestorePreview { scope: RestoreScope; scopeDigest: string; blockers: string[] }
export interface RestoreMetaOperation { operationId: string; planId: string | null; version: string | null; state: string }
export interface RestoreUnit { sourcePlanId: string; targetTenantId: string; subjectId: string; outcome: string; beforeVersion: string | null; afterVersion: string | null }
export interface RestoreResult { operationId: string; state: string; planId: string | null; candidateId: string | null; sourceVersion: string | null; units: RestoreUnit[] | null; aggregateState: 'PARTIAL' }
export interface RestorePlan { planId: string; requesterId: string; version: string; state: string; scope: RestoreScope; scopeDigest: string; approvalCurrent: boolean; currentBlockers: string[]; outcome: RestoreResult | null }
export const privacyRestoreApi = {
  context: () => http.get(`${root}/context`) as unknown as Promise<RestoreContext>,
  preview: (id: string) => http.get(`${root}/candidates/${id}/preview`) as unknown as Promise<RestorePreview>,
  request: (input: { operationId: string; candidateId: string; requestRef: string; expectedScopeDigest: string }) => http.post(`${root}/plans`, input) as unknown as Promise<RestoreMetaOperation>,
  plan: (id: string) => http.get(`${root}/plans/${id}`) as unknown as Promise<RestorePlan>,
  decide: (id: string, input: { operationId: string; expectedVersion: string; decision: string; decisionRef: string }) => http.post(`${root}/plans/${id}/decisions`, input) as unknown as Promise<RestoreMetaOperation>,
  execute: (id: string, input: { operationId: string; expectedVersion: string; expectedScopeDigest: string; confirmationRef: string }) => http.post(`${root}/plans/${id}/execute`, input) as unknown as Promise<RestoreResult>,
  metadata: (id: string) => http.get(`${root}/metadata-operations/${id}`) as unknown as Promise<RestoreMetaOperation | null>,
  closeMetadata: (id: string) => http.post(`${root}/metadata-operations/${id}/close`) as unknown as Promise<RestoreMetaOperation>,
  operation: (id: string) => http.get(`${root}/operations/${id}`) as unknown as Promise<RestoreResult>,
  recover: (id: string) => http.post(`${root}/operations/${id}/recover`) as unknown as Promise<RestoreResult>,
  close: (id: string) => http.post(`${root}/operations/${id}/close`) as unknown as Promise<RestoreResult>,
}
