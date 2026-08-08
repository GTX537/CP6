import http from '../http'
import type {
  ISpaceExcelPreflightDto,
  IStartSpaceExcelPreflightRequest,
  IStartSpaceExcelPreflightResponse,
  IUploadSpaceExcelSourceResponse,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const root = '/space/design/v1'

export const designExcelPreflightApi = {
  upload(versionId: string, file: File) {
    const form = new FormData()
    form.append('file', file, file.name)
    return http.post<unknown, IUploadSpaceExcelSourceResponse>(
      `${root}/versions/${versionId}/excel-sources`,
      form,
      { timeout: 120_000 },
    )
  },

  start(
    versionId: string,
    sourceId: string,
    request: IStartSpaceExcelPreflightRequest,
    idempotencyKey: string,
  ) {
    return http.post<unknown, IStartSpaceExcelPreflightResponse>(
      `${root}/versions/${versionId}/sources/${sourceId}/excel-preflights`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  get(versionId: string, sourceId: string, jobId: string, issueLimit = 200) {
    return http.get<unknown, ISpaceExcelPreflightDto>(
      `${root}/versions/${versionId}/sources/${sourceId}/excel-preflights/${jobId}`,
      { params: { issueLimit } },
    )
  },

  downloadReport(versionId: string, sourceId: string, jobId: string) {
    return http.get<unknown, Blob>(
      `${root}/versions/${versionId}/sources/${sourceId}/excel-preflights/${jobId}/report`,
      {
        responseType: 'blob',
        headers: { Accept: 'text/csv' },
      },
    )
  },
}
