import http from '@/api/http'
import type { CadReviewWorkspace } from '@/modules/space-design/cad-review/cadReviewWorkspace'

const root = '/space/design/v1'

export interface SpaceCadParse {
  jobId: string
  status: string
  sourceState: string
  lastErrorCode?: string
  lastErrorSummary?: string
  artifacts: Array<{
    artifactId: string
    artifactType: string
    sizeBytes: number
  }>
}

function url(versionId: string, sourceId: string, jobId: string) {
  return `${root}/versions/${versionId}/sources/${sourceId}/cad-parses/${jobId}`
}

export const designCadParseApi = {
  get(versionId: string, sourceId: string, jobId: string) {
    return http.get<unknown, SpaceCadParse>(url(versionId, sourceId, jobId))
  },

  getReviewWorkspace(versionId: string, sourceId: string, jobId: string) {
    return http.get<unknown, CadReviewWorkspace>(
      `${url(versionId, sourceId, jobId)}/review-workspace`,
    )
  },

  cancel(versionId: string, sourceId: string, jobId: string) {
    return http.post(`${url(versionId, sourceId, jobId)}:cancel`)
  },
}
