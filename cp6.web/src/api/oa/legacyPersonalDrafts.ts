import http from '../http'

export interface LegacyPersonalDraft {
  id: string
  title: string | null
  formKey: string | null
  recordedVersion: number | null
  status: number
  recordedUpdatedAt: string
  source: 'LEGACY_VERSION_SNAPSHOT' | 'LEGACY_HEAD_BACKFILL' | 'MISSING_VERSION_SNAPSHOT'
  evidenceState: 'UNVERIFIED_LEGACY'
  canWrite: false
}
export interface LegacyPersonalDraftPage { items: LegacyPersonalDraft[]; total: number; page: number; pageSize: number }
export async function legacyPersonalDrafts(page: number, signal: AbortSignal): Promise<LegacyPersonalDraftPage> {
  const response = await http.get('/oa/legacy-drafts', { params: { page, pageSize: 20 }, signal })
  return response.data as LegacyPersonalDraftPage
}
