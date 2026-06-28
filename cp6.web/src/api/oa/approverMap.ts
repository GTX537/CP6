import http from '../http'

export interface ApproverMap {
  id: string; mapKey: string; matchValue: string
  approverUserId?: string | null; approverRoleId?: number | null
  orderNo: number; enable: boolean
}

export const approverMapApi = {
  list: (mapKey?: string) => http.get('/oa/approver-map/list', { params: { mapKey } }),
  keys: () => http.get('/oa/approver-map/keys'),
  create: (body: Partial<ApproverMap>) => http.post('/oa/approver-map', body),
  update: (id: string, body: Partial<ApproverMap>) => http.put(`/oa/approver-map/${id}`, body),
  remove: (id: string) => http.delete(`/oa/approver-map/${id}`),
}
