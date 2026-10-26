import http from '../http'
export interface PlateSourceScope { tenantId: string; actorId: string }
export interface PlateSourceEnvelope<T> { code: number; data: T }
export interface PlateSourceVersion {
  tenantId: string; reference: { owner: 'Main.Erp.PlateMoldSource.v1'; id: string; version: string; digest: string }
  isCurrent: boolean; sourcePresent: boolean; observedNativeRowVersion: string; currentNativeRowVersion: string; observedAt: string
  technicalApproval: 'UNKNOWN'; physicalQualification: 'UNKNOWN'; originalJson: string
  source: {
    identity: { baseCode: string; number: string; businessRevision: number; name: string }
    technical: Record<string, string | number | boolean | null>; validity: { startDate: string; endDate: string }
    legacyAttachmentFlags: Record<string, boolean>; requirements: Record<string, string | number | null>
    control: { isDeleted: boolean; creator: string | null; legacyTransferStatus: number; legacyMcTransferred: boolean }
  }
}
const root = '/plate-molds/source-owner-v1'
export const plateSourceApi = {
  scope: () => http.get<any, PlateSourceEnvelope<PlateSourceScope>>(`${root}/scope`),
  list: (search: string, page = 1) => http.get<any, PlateSourceEnvelope<PlateSourceVersion[]>>(root, { params: { search, page, pageSize: 20 } }),
  read: (id: string, version?: string) => http.get<any, PlateSourceEnvelope<PlateSourceVersion>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
}
