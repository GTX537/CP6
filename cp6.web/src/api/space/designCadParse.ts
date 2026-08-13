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

export interface UploadSpaceCadSourceResponse {
  source: { id: string; state: string; sha256: string }
  scanJobId?: string
  jobStatusUrl?: string
}

function url(versionId: string, sourceId: string, jobId: string) {
  return `${root}/versions/${versionId}/sources/${sourceId}/cad-parses/${jobId}`
}

export const designCadParseApi = {
  upload(versionId: string, file: File) {
    const form = new FormData()
    form.append('SourceFormat', file.name.toLowerCase().endsWith('.dwg') ? 'Dwg' : 'Dxf')
    form.append('File', file)
    return http.post<unknown, UploadSpaceCadSourceResponse>(
      `${root}/versions/${versionId}/cad-sources`,
      form,
    )
  },

  get(versionId: string, sourceId: string, jobId: string) {
    return http.get<unknown, SpaceCadParse>(url(versionId, sourceId, jobId))
  },

  getReviewWorkspace(versionId: string, sourceId: string, jobId: string) {
    return http.get<unknown, CadReviewWorkspace>(
      `${url(versionId, sourceId, jobId)}/review-workspace`,
    )
  },

  applyReviewChanges(
    versionId: string,
    sourceId: string,
    jobId: string,
    request: {
      commandBatchId: string
      clientInstanceId: string
      leaseId: string
      expectedFloorRevision: number
      expectedContentRevision: number
      expectedContentHash?: string
      workspaceSha256: string
      changeIds: string[]
    },
  ) {
    return http.post<unknown, {
      commandBatchId: string
      floorRevision: number
      versionContentRevision: number
      appliedChangeCount: number
      workspaceSha256: string
      idempotentReplay: boolean
    }>(`${url(versionId, sourceId, jobId)}/review-workspace:apply`, request)
  },

  cancel(versionId: string, sourceId: string, jobId: string) {
    return http.post(`${url(versionId, sourceId, jobId)}:cancel`)
  },

  retry(
    versionId: string,
    sourceId: string,
    jobId: string,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, { jobId: string; status: string }>(
      `${url(versionId, sourceId, jobId)}:retry`,
      undefined,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },
}
