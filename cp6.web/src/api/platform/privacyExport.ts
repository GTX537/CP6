import http from '@/api/http'
import type { SubjectExportManifest } from './gdpr'
const root = '/platform/privacy-exports-v1'
export interface PrivacyExportContext {
  environmentId: string; actorTenantId: string; actorId: string; policyVersion: string
  purposes: string[]; sources: string[]; actions: string[]; maximumAccessMinutes: number
}
export interface PrivacyExportCommand {
  operationId: string; requestRef: string; targetTenantId: string; subjectId: string
  purpose: string; sources: string[]; recipientId: string; policyVersion: string; accessMinutes: number
}
export interface PrivacyExportReceipt {
  operationId: string; requestRef: string; artifactId: string; environmentId: string
  requesterTenantId: string; requesterId: string; operatorId: string; recipientId: string
  targetTenantId: string; subjectId: string; purpose: string; sources: string[]; policyVersion: string
  capturedAt: string; expiresAt: string; length: number; sha256: string; manifest: SubjectExportManifest
}
export interface PrivacyExportOperation { operationId: string; state: 'COMMITTED' | 'NOT_FOUND' | 'CLOSED'; receipt: PrivacyExportReceipt | null }
export interface PrivacyExportArtifact { receipt: PrivacyExportReceipt; availability: 'AVAILABLE' | 'EXPIRED' | 'REVOKED' | 'POLICY_CHANGED' }
export const privacyExportApi = {
  context: () => http.get(`${root}/context`) as unknown as Promise<PrivacyExportContext>,
  generate: (command: PrivacyExportCommand) => http.post(`${root}/commands`, command) as unknown as Promise<PrivacyExportOperation>,
  operation: (id: string) => http.get(`${root}/operations/${id}`) as unknown as Promise<PrivacyExportOperation>,
  close: (id: string) => http.post(`${root}/operations/${id}/close`) as unknown as Promise<PrivacyExportOperation>,
  artifact: (id: string) => http.get(`${root}/artifacts/${id}`) as unknown as Promise<PrivacyExportArtifact>,
  revoke: (id: string) => http.post(`${root}/artifacts/${id}/revoke`) as unknown as Promise<PrivacyExportArtifact>,
  download: (id: string) => http.get(`${root}/artifacts/${id}/download`, { responseType: 'blob' }) as unknown as Promise<Blob>,
}
// Called only after the view has checked that the initiating identity remains current.
export function savePrivacyDownload(blob: Blob, artifact: string) {
  const url = URL.createObjectURL(blob), link = document.createElement('a')
  try { link.href = url; link.download = `subject-export-${artifact}.json`; document.body.appendChild(link); link.click() }
  finally { link.remove(); URL.revokeObjectURL(url) }
}
