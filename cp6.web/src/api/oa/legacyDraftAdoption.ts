import http from '../http'
import type { FormMask } from './personalForm'
export interface LegacySourceBinding { legacyVersionId: string; legacyFormId: string; sourceRevision: string; headId: string; formVersionId: string; targetKey: string; version: string; enabled: boolean; boundAtUtc: string; boundBy: string; reason: string; sourceEvidenceState: 'UNVERIFIED_LEGACY' }
export interface LegacySourcePreview { legacyFormId: string; legacyVersionId: string; recordedVersion: number; sourceKind: string; sourceEvidenceState: 'UNVERIFIED_LEGACY'; sourceRevision: string; targetKey: string; targetVersionId: string; targetVersion: number; compatible: boolean; blockers: string[]; current: LegacySourceBinding | null; canConfigure: boolean }
export interface LegacySourceOperation { operationId: string; state: string; result: LegacySourceBinding | null }
export interface LegacyAdoptionPreview { legacyDraftId: string; legacyVersionId: string; sourceVersion: string; mappingVersion: string; sourceEvidenceState: 'UNVERIFIED_LEGACY'; targetKey: string; formVersionId: string; targetVersion: number; schemaJson: string; dataJson: string; title: string | null; fieldMask: FormMask; canAdopt: boolean; blockers: string[]; adoptUntilUtc: string }
const data = <T>(value: { data: T }): T => value.data
const root = '/oa/v2/legacy-form-sources'
export const legacyDraftAdoptionApi = {
  sourcePreview: async (legacyVersionId: string, targetKey: string, targetVersionId: string) => data<LegacySourcePreview>(await http.get(root + '/preview', { params: { legacyVersionId, targetKey, targetVersionId } })),
  configure: async (input: { operationId: string; legacyVersionId: string; expectedSourceRevision: string; targetKey: string; targetVersionId: string; expectedVersion: string; enabled: boolean; reason: string }) => data<LegacySourceOperation>(await http.post(root, input)),
  operation: async (id: string) => data<LegacySourceOperation | null>(await http.get(root + '/operations/' + id)),
  close: async (id: string) => data<LegacySourceOperation>(await http.post(root + '/operations/' + id + '/close')),
  preview: async (id: string, signal?: AbortSignal) => data<LegacyAdoptionPreview>(await http.get(`/oa/v2/legacy-drafts/${id}/preview`, { signal })),
}
