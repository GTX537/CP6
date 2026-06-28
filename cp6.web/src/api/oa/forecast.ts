import http from '../http'

export const forecastApi = {
  preview: (flowKey: string, varsJson: string) =>
    http.post('/oa/forecast/preview', { flowKey, varsJson }),
}
