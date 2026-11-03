import http from '../http'

export interface LegacyDefinition {
  kind: 'FLOW' | 'FORM'; definitionId: string; key: string; versionId: string | null
  recordedVersion: number; recordedStatus: number | null
  source: 'CURRENT_HEAD_RECONSTRUCTION' | 'LEGACY_VERSION_SNAPSHOT' | 'LEGACY_HEAD_BACKFILL'
  fields: Record<string, string | null>; recordedPublishedAtUtc: string | null; recordedPublishedBy: string | null
  evidenceState: 'UNVERIFIED_LEGACY'; canWrite: false
}
export const legacyDefinitionsApi = {
  read: async (kind: 'FLOW' | 'FORM', key?: string, page = 1, signal?: AbortSignal): Promise<LegacyDefinition[]> => {
    const response: any = await http.get(`/oa/v2/legacy-definitions/${kind}${key ? '/' + encodeURIComponent(key) : ''}`, { params: { page }, signal })
    return response.data ?? response
  },
}
