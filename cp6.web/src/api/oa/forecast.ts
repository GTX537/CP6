import http from '../http'

export const forecastApi = {
  preview: (flowKey: string, publishedVersionId: string, varsJson: string) =>
    http.post('/oa/forecast/preview', { flowKey, publishedVersionId, varsJson }),
}
