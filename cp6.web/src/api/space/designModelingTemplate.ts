import http from '../http'

const root = '/space/design/v1'

export const standardSpaceModelingTemplateFileName =
  'cp6-space-standard-model-v1.xlsx'

export const designModelingTemplateApi = {
  downloadStandardExcel() {
    return http.get<unknown, Blob>(
      `${root}/modeling-templates/excel/standard`,
      {
        responseType: 'blob',
        headers: {
          Accept:
            'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        },
      },
    )
  },
}
