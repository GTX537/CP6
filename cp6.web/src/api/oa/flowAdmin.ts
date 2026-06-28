import http from '../http'

export const flowAdminApi = {
  list:   () => http.get('/oa/flow-admin/list'),
  get:    (flowKey: string) => http.get(`/oa/flow-admin/${flowKey}`),
  enable: (flowKey: string, enabled: boolean) =>
    http.post('/oa/flow-admin/enable', { flowKey, enabled }),
}
