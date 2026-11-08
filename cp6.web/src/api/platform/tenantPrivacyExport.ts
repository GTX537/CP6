import http from '@/api/http'
import type { SubjectExportManifest } from './gdpr'
const root = '/platform/privacy-tenant-exports-v1'
export type TenantPrivacyExportManifest = Omit<SubjectExportManifest, 'subjectId'>
export interface TenantPrivacyExportContext { environmentId: string; actorTenantId: string; actorId: string; owner: string; policyVersion: string; purposes: string[]; sources: string[]; actions: string[]; maximumAccessMinutes: number }
export interface TenantPrivacyExportCommand { operationId: string; requestRef: string; targetTenantId: string; purpose: string; sources: string[]; recipientId: string; policyVersion: string; accessMinutes: number }
export interface TenantPrivacyExportReceipt { operationId: string; requestRef: string; artifactId: string; environmentId: string; requesterTenantId: string; requesterId: string; operatorId: string; recipientId: string; targetTenantId: string; purpose: string; sources: string[]; policyVersion: string; capturedAt: string; expiresAt: string; length: number; sha256: string; manifest: TenantPrivacyExportManifest }
export interface TenantPrivacyExportOperation { operationId: string; state: 'COMMITTED' | 'NOT_FOUND' | 'CLOSED'; receipt: TenantPrivacyExportReceipt | null }
export interface TenantPrivacyExportArtifact { receipt: TenantPrivacyExportReceipt; availability: 'AVAILABLE' | 'EXPIRED' | 'REVOKED' | 'POLICY_CHANGED' }
export const tenantPrivacyExportApi = {
  context: () => http.get(`${root}/context`) as unknown as Promise<TenantPrivacyExportContext>,
  generate: (input: TenantPrivacyExportCommand) => http.post(`${root}/commands`, input) as unknown as Promise<TenantPrivacyExportOperation>,
  operation: (id: string) => http.get(`${root}/operations/${id}`) as unknown as Promise<TenantPrivacyExportOperation>,
  close: (id: string) => http.post(`${root}/operations/${id}/close`) as unknown as Promise<TenantPrivacyExportOperation>,
  artifact: (id: string) => http.get(`${root}/artifacts/${id}`) as unknown as Promise<TenantPrivacyExportArtifact>,
  revoke: (id: string) => http.post(`${root}/artifacts/${id}/revoke`) as unknown as Promise<TenantPrivacyExportArtifact>,
  download: (id: string) => http.get(`${root}/artifacts/${id}/download`, { responseType: 'blob' }) as unknown as Promise<Blob>,
}
export function saveTenantPrivacyDownload(blob: Blob, artifact: string) {
  const url = URL.createObjectURL(blob), link = document.createElement('a')
  try { link.href = url; link.download = `tenant-export-${artifact}.json`; document.body.appendChild(link); link.click() }
  finally { link.remove(); URL.revokeObjectURL(url) }
}
