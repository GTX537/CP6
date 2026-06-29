import http from '../http'

export const inboxApi = {
  pending:   () => http.get('/oa/inbox/pending'),
  pendingCc: () => http.get('/oa/inbox/pending-cc'),
  running:   () => http.get('/oa/inbox/running'),
  done:      (p: { year?: number; month?: number; tab?: string }) => http.get('/oa/inbox/done', { params: p }),
  stats:     () => http.get('/oa/inbox/stats'),
  detail:    (instanceId: string) => http.get(`/oa/inbox/detail/${instanceId}`),
  markTaskRead: (id: string) => http.post('/oa/inbox/task/read', { id }),
  markCcRead:   (id: string) => http.post('/oa/inbox/cc/read', { id }),
  batch: (taskIds: string[], approve: boolean, comment?: string) =>
    http.post('/oa/inbox/batch', { taskIds, approve, comment }),
  sendBack: (taskId: string, kind: 'prevStage' | 'starter' | 'node', nodeId?: string, comment?: string) =>
    http.post('/oa/inbox/sendback', { taskId, kind, nodeId, comment }),
}
