import http from '../http'
import type { EngineeringEnvelope, EngineeringReference, EngineeringScope } from '../plm/engineeringProducts'
export interface ContentReference { owner: 'Main.File.Content.v1'; id: string; version: string; hashAlgorithm: 'SHA256'; digest: string; byteLength: number; mediaType: string }
export interface FileSnapshot { tenantId: string; contentRef: ContentReference; parent: EngineeringReference; fileName: string; stateVersion: string; etag: string; inspection: { status: 'CLEAN' | 'PENDING' | 'NOT_CLEAN'; provider: string; engineVersion: string | null; signatureVersion: string | null; checkedAt: string; reason: string }; held: boolean; availability: 'AVAILABLE' | 'HELD' | 'PENDING_INSPECTION' | 'QUARANTINED'; actorId: string; recordedAt: string; action: string; reason: string; isCurrent: boolean; originalDigest: string }
export interface FileUpload { operationId: string; fileId: string; parent: EngineeringReference; fileName: string; byteLength: number; digest: string; reason: string }
export interface FileCommand { operationId: string; fileId: string; action: 'HOLD' | 'RELEASE' | 'INSPECT'; expectedVersion: string; expectedEtag: string; reason: string }
export interface FileResult { operationId: string; kind: 'FILE_CONTENT'; objectId: string; version: string; digest: string }
export interface FileOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: FileResult | null }
const root = '/file-content/owner-v1'
const uploadHeader = (command: FileUpload) => btoa(String.fromCharCode(...new TextEncoder().encode(JSON.stringify(command))))
export const fileContentApi = {
  scope: () => http.get<any, EngineeringEnvelope<EngineeringScope>>(`${root}/scope`),
  list: (parentId: string, page = 1) => http.get<any, EngineeringEnvelope<FileSnapshot[]>>(root, { params: { parentId, page } }),
  read: (id: string, stateVersion?: string) => http.get<any, EngineeringEnvelope<FileSnapshot>>(`${root}/${encodeURIComponent(id)}`, { params: { stateVersion } }),
  upload: (command: FileUpload, bytes: ArrayBuffer) => http.post<any, EngineeringEnvelope<FileResult>>(`${root}/uploads`, bytes, { timeout: 150000, headers: { 'Content-Type': 'application/octet-stream', 'X-File-Command': uploadHeader(command) } }),
  save: (command: FileCommand) => http.post<any, EngineeringEnvelope<FileResult>>(`${root}/commands`, command, { timeout: 150000 }),
  download: (reference: ContentReference) => http.get<any, Blob>(`${root}/${encodeURIComponent(reference.id)}/download`, { params: { version: reference.version, digest: reference.digest }, responseType: 'blob' }),
  operation: (id: string, close: boolean) => close ? http.post<any, EngineeringEnvelope<FileOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, EngineeringEnvelope<FileOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
